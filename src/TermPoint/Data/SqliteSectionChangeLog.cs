using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TermPoint.Data;

/// <summary>
/// SQLite implementation of <see cref="ISectionChangeLog"/>: TEMP tables and TEMP triggers on the context's one
/// connection to the working copy D' (desktop build only; the demo has no undo).
///
/// <para><b>Why TEMP.</b> Probed 2026-11-26 (see <c>release_state/1.2.5/undo/NOTES.md</c>, "Probe results"): TEMP objects
/// never reach D' or any file copied from it (the save's snapshot is byte-identical with and without the log), emptying
/// them is not an edit to a save in flight (<c>PRAGMA data_version</c> on the snapshot's connection does not change), an
/// entry commits and rolls back with the repository write that made it, and the log is gone whenever the connection is
/// replaced. All of that is pinned by <c>SectionChangeLogTests</c>.</para>
///
/// <para><b>Catch 1: pooling.</b> A pooled connection hands its TEMP tables to the next opener of the same connection
/// string. <see cref="DatabaseContext"/> clears the pool on every close path, and <see cref="Install"/> also drops any
/// <c>undo_</c> object it finds first, as a second guard, so a log always starts empty.</para>
///
/// <para><b>Objects</b> (all prefixed <c>undo_</c>): <c>undo_state</c> (one row: current step, pause mode, other-changes
/// and history-incomplete flags), <c>undo_steps</c>, <c>undo_entries</c> (every <c>Sections</c> column, old and new) and
/// the triggers. Section triggers read the state in their WHEN clauses: no pause and a current step records an entry;
/// no pause and no step is the barrier (empties both lists, sets history-incomplete); pause NotAStep sets
/// other-changes; pause WritingHistory does nothing. For every other table in <c>sqlite_master</c>, generated when the
/// log is created, a trigger on insert, update and delete only sets other-changes, whatever the pause.</para>
///
/// <para>Not thread-safe by itself: like every repository write it is meant for the UI thread that owns the connection.</para>
/// </summary>
internal sealed class SqliteSectionChangeLog : ISectionChangeLog
{
    /// <summary>The <c>Sections</c> columns other than <c>id</c>, in table order.</summary>
    private static readonly string[] Columns =
    {
        "semester_id", "semester_name", "academic_year_name", "course_id", "room_id", "section_code", "course_code", "data",
    };

    private readonly Func<SqliteConnection> _connection;
    private volatile bool _available;

    /// <summary>
    /// The tables whose deletes are copied into <c>undo_deleted</c>: every table of the main database with both an
    /// <c>id</c> and a <c>data</c> column. Filled by <see cref="Install"/>; <see cref="WasDeleted"/> only builds SQL
    /// from names found in this set.
    /// </summary>
    private HashSet<string> _deletedTables = new(StringComparer.Ordinal);

    /// <summary>Creates the log over the context's CURRENT connection (replaced on reopen, hence a delegate).</summary>
    /// <param name="connection">Returns the context's current connection.</param>
    public SqliteSectionChangeLog(Func<SqliteConnection> connection) => _connection = connection;

    /// <inheritdoc/>
    public bool IsAvailable => _available;

    /// <inheritdoc/>
    public Exception? InstallError { get; private set; }

    /// <inheritdoc/>
    public event Action? Reset;

    /// <summary>Raises <see cref="Reset"/>. Called by <see cref="DatabaseContext"/> at the end of every reopen.</summary>
    internal void RaiseReset() => Reset?.Invoke();

    // ── Creation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the log on the current connection, starting empty (any <c>undo_</c> TEMP object left by a pooled
    /// connection is dropped first). Never throws: on failure it records <see cref="InstallError"/>, removes whatever it
    /// created and leaves <see cref="IsAvailable"/> false, because creating the log must never fail the open.
    /// Must be called only on a writer session (a <c>query_only</c> connection refuses TEMP DDL).
    /// </summary>
    internal void Install()
    {
        _available = false;
        var conn = _connection();
        try
        {
            Exec(conn, "PRAGMA temp_store=MEMORY");
            DropAll(conn);
            Exec(conn, BuildTablesSql());
            Exec(conn, BuildSectionTriggersSql());
            Exec(conn, BuildOtherTableTriggersSql(conn));
            InstallError = null;
            _available = true;
        }
        catch (Exception ex)
        {
            InstallError = ex;
            try { DropAll(conn); } catch { /* best effort: the log stays unavailable anyway */ }
        }
    }

    /// <summary>
    /// Marks the log unavailable. Called BEFORE the connection is disposed (reopen, close, dispose) so no caller
    /// reaches a closed connection through the log. <see cref="InstallError"/> is kept.
    /// </summary>
    internal void MarkUnavailable() => _available = false;

    /// <summary>Drops every <c>undo_</c> trigger and table in the TEMP schema.</summary>
    /// <param name="conn">The connection.</param>
    private static void DropAll(SqliteConnection conn)
    {
        var drops = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            // Triggers first: they reference the tables. ESCAPE because '_' is a LIKE wildcard.
            cmd.CommandText = @"SELECT type, name FROM sqlite_temp_master
                                WHERE type IN ('trigger', 'table') AND name LIKE 'undo\_%' ESCAPE '\'
                                ORDER BY CASE type WHEN 'trigger' THEN 0 ELSE 1 END";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                drops.Add($"DROP {r.GetString(0).ToUpperInvariant()} IF EXISTS temp.{Quote(r.GetString(1))}");
        }
        foreach (var sql in drops) Exec(conn, sql);
    }

    /// <summary>SQL creating the state, steps and entries tables.</summary>
    private static string BuildTablesSql()
    {
        var oldCols = string.Join(", ", Columns.Select(c => $"o_{c} TEXT"));
        var newCols = string.Join(", ", Columns.Select(c => $"n_{c} TEXT"));
        return $"""
            CREATE TEMP TABLE undo_state(
                id INTEGER PRIMARY KEY CHECK (id = 1),
                current_step INTEGER,
                pause INTEGER NOT NULL DEFAULT 0,
                other_changes INTEGER NOT NULL DEFAULT 0,
                history_incomplete INTEGER NOT NULL DEFAULT 0);
            INSERT INTO temp.undo_state(id) VALUES (1);
            CREATE TEMP TABLE undo_steps(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                label TEXT NOT NULL,
                at TEXT NOT NULL,
                edit_count INTEGER NOT NULL DEFAULT 0,
                is_undone INTEGER NOT NULL DEFAULT 0);
            CREATE TEMP TABLE undo_deleted(
                tbl TEXT NOT NULL,
                id TEXT NOT NULL,
                data TEXT,
                PRIMARY KEY (tbl, id));
            CREATE TEMP TABLE undo_entries(
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                step_id INTEGER NOT NULL,
                op TEXT NOT NULL,
                section_id TEXT NOT NULL,
                {oldCols},
                {newCols});
            """;
    }

    /// <summary>
    /// SQL creating the three triggers per event on <c>main.Sections</c>: log (pause none, step current), barrier
    /// (pause none, no step) and other-changes (pause NotAStep). Pause WritingHistory matches none of them.
    /// </summary>
    private static string BuildSectionTriggersSql()
    {
        const string NoPause = "(SELECT pause FROM temp.undo_state) = 0";
        const string HasStep = "(SELECT current_step FROM temp.undo_state) IS NOT NULL";
        var oldNames = string.Join(", ", Columns.Select(c => $"o_{c}"));
        var newNames = string.Join(", ", Columns.Select(c => $"n_{c}"));
        string Vals(string row) => string.Join(", ", Columns.Select(c => $"{row}.{c}"));
        const string Step = "(SELECT current_step FROM temp.undo_state)";

        var insertEntry = new Dictionary<string, string>
        {
            ["INSERT"] = $"INSERT INTO temp.undo_entries(step_id, op, section_id, {newNames}) " +
                         $"VALUES ({Step}, 'I', NEW.id, {Vals("NEW")});",
            ["UPDATE"] = $"INSERT INTO temp.undo_entries(step_id, op, section_id, {oldNames}, {newNames}) " +
                         $"VALUES ({Step}, 'U', OLD.id, {Vals("OLD")}, {Vals("NEW")});",
            ["DELETE"] = $"INSERT INTO temp.undo_entries(step_id, op, section_id, {oldNames}) " +
                         $"VALUES ({Step}, 'D', OLD.id, {Vals("OLD")});",
        };

        var sb = new System.Text.StringBuilder();
        foreach (var (ev, body) in insertEntry)
        {
            sb.AppendLine($"CREATE TEMP TRIGGER undo_sections_{ev.ToLowerInvariant()}_log AFTER {ev} ON main.Sections " +
                          $"WHEN {NoPause} AND {HasStep} BEGIN {body} END;");
            // The flag updates carry a WHERE so a flag already set writes nothing: SQLite's total_changes() counts trigger
            // writes too (code and tests use it to count rows written), and most writes find the flag already set.
            sb.AppendLine($"CREATE TEMP TRIGGER undo_sections_{ev.ToLowerInvariant()}_barrier AFTER {ev} ON main.Sections " +
                          $"WHEN {NoPause} AND NOT {HasStep} BEGIN " +
                          "DELETE FROM temp.undo_entries; DELETE FROM temp.undo_steps; " +
                          "UPDATE temp.undo_state SET history_incomplete = 1 WHERE history_incomplete = 0; END;");
            sb.AppendLine($"CREATE TEMP TRIGGER undo_sections_{ev.ToLowerInvariant()}_other AFTER {ev} ON main.Sections " +
                          "WHEN (SELECT pause FROM temp.undo_state) = 1 BEGIN " +
                          "UPDATE temp.undo_state SET other_changes = 1 WHERE other_changes = 0; END;");
        }
        return sb.ToString();
    }

    /// <summary>
    /// SQL creating, for every table of the main database other than <c>Sections</c>, a trigger per event that only sets
    /// "other changes". Names are quoted properly; the trigger names carry an index so they need no quoting.
    /// </summary>
    /// <param name="conn">The connection whose <c>sqlite_master</c> is read.</param>
    private string BuildOtherTableTriggersSql(SqliteConnection conn)
    {
        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT name FROM main.sqlite_master
                                WHERE type = 'table' AND name <> 'Sections' AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
                                ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        // A delete in a table with an id and a data column also keeps the deleted row's data (undo_deleted), so undo can
        // tell "deleted this session" and name what was deleted. Other tables (no id or no data) only set the flag.
        var deletedTables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM pragma_table_info($t, 'main')";
            cmd.Parameters.AddWithValue("$t", table);
            using var r = cmd.ExecuteReader();
            while (r.Read()) columns.Add(r.GetString(0));
            if (columns.Contains("id") && columns.Contains("data")) deletedTables.Add(table);
        }
        _deletedTables = deletedTables;

        const string SetFlag = "UPDATE temp.undo_state SET other_changes = 1 WHERE other_changes = 0;";
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < tables.Count; i++)
            foreach (var ev in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                var keep = ev == "DELETE" && deletedTables.Contains(tables[i])
                    ? $"INSERT OR REPLACE INTO temp.undo_deleted(tbl, id, data) VALUES ('{tables[i].Replace("'", "''")}', OLD.id, OLD.data); "
                    : "";
                sb.AppendLine($"CREATE TEMP TRIGGER undo_other_{i}_{ev.ToLowerInvariant()} AFTER {ev} ON main.{Quote(tables[i])} " +
                              $"BEGIN {keep}{SetFlag} END;");
            }
        return sb.ToString();
    }

    // ── Steps and pauses ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public long BeginStep(string label, DateTime at)
    {
        var conn = Require();
        RequireIdle(conn);
        Exec(conn, "DELETE FROM temp.undo_entries WHERE step_id IN (SELECT id FROM temp.undo_steps WHERE is_undone = 1); " +
                   "DELETE FROM temp.undo_steps WHERE is_undone = 1");
        Exec(conn, "INSERT INTO temp.undo_steps(label, at) VALUES ($label, $at)",
            ("$label", label), ("$at", at.ToString("o", CultureInfo.InvariantCulture)));
        var id = Scalar(conn, "SELECT last_insert_rowid()");
        Exec(conn, "UPDATE temp.undo_state SET current_step = $id", ("$id", id));
        return id;
    }

    /// <inheritdoc/>
    public void EndStep(long stepId, long editCount)
    {
        if (!_available) return;
        var conn = _connection();
        Exec(conn, "UPDATE temp.undo_state SET current_step = NULL");

        // An update that changed nothing (same row in every column) is not an edit: drop its entry.
        var same = string.Join(" AND ", Columns.Select(c => $"o_{c} IS n_{c}"));
        Exec(conn, $"DELETE FROM temp.undo_entries WHERE step_id = $id AND op = 'U' AND {same}", ("$id", stepId));
        if (Scalar(conn, "SELECT COUNT(*) FROM temp.undo_entries WHERE step_id = $id", ("$id", stepId)) == 0)
            Exec(conn, "DELETE FROM temp.undo_steps WHERE id = $id", ("$id", stepId));
        else
            Exec(conn, "UPDATE temp.undo_steps SET edit_count = $n WHERE id = $id", ("$n", editCount), ("$id", stepId));
    }

    /// <inheritdoc/>
    public void BeginPause(ChangeLogPause mode)
    {
        if (mode == ChangeLogPause.None)
            throw new ArgumentException("A pause needs a mode other than None.", nameof(mode));
        var conn = Require();
        RequireIdle(conn);
        Exec(conn, "UPDATE temp.undo_state SET pause = $m", ("$m", (long)mode));
    }

    /// <inheritdoc/>
    public void EndPause()
    {
        if (!_available) return;
        Exec(_connection(), "UPDATE temp.undo_state SET pause = 0");
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<UndoStepInfo> GetSteps()
    {
        var steps = new List<UndoStepInfo>();
        if (!_available) return steps;
        using var cmd = _connection().CreateCommand();
        cmd.CommandText = "SELECT id, label, at, edit_count, is_undone FROM temp.undo_steps ORDER BY id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            steps.Add(new UndoStepInfo(r.GetInt64(0), r.GetString(1),
                DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                r.GetInt64(3), r.GetInt64(4) != 0));
        return steps;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SectionLogEntry> GetEntries(long stepId)
    {
        var entries = new List<SectionLogEntry>();
        if (!_available) return entries;
        var oldCols = string.Join(", ", Columns.Select(c => $"o_{c}"));
        var newCols = string.Join(", ", Columns.Select(c => $"n_{c}"));
        using var cmd = _connection().CreateCommand();
        cmd.CommandText = $"SELECT seq, step_id, op, section_id, {oldCols}, {newCols} " +
                          "FROM temp.undo_entries WHERE step_id = $id ORDER BY seq";
        cmd.Parameters.AddWithValue("$id", stepId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var op = r.GetString(2) switch { "I" => SectionLogOp.Insert, "U" => SectionLogOp.Update, _ => SectionLogOp.Delete };
            var id = r.GetString(3);
            entries.Add(new SectionLogEntry(r.GetInt64(0), r.GetInt64(1), op, id,
                op == SectionLogOp.Insert ? null : ReadRow(r, id, 4),
                op == SectionLogOp.Delete ? null : ReadRow(r, id, 4 + Columns.Length)));
        }
        return entries;
    }

    /// <inheritdoc/>
    public SectionRow? ReadCurrent(string sectionId)
    {
        if (!_available) return null;
        using var cmd = _connection().CreateCommand();
        cmd.CommandText = $"SELECT {string.Join(", ", Columns)} FROM main.Sections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sectionId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r, sectionId, 0) : null;
    }

    /// <summary>Reads the eight <c>Sections</c> columns starting at <paramref name="offset"/> into a row.</summary>
    /// <param name="r">A reader positioned on a row.</param>
    /// <param name="id">The row's id.</param>
    /// <param name="offset">Ordinal of the first of the eight columns.</param>
    private static SectionRow ReadRow(SqliteDataReader r, string id, int offset)
    {
        string? S(int i) => r.IsDBNull(offset + i) ? null : r.GetString(offset + i);
        return new SectionRow(id, S(0) ?? "", S(1), S(2), S(3), S(4), S(5), S(6), S(7) ?? "{}");
    }

    // ── Writing history back ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public void WriteRows(long stepId, IReadOnlyList<(string SectionId, SectionRow? Row)> writes, bool markUndone)
    {
        var conn = Require();
        BeginPause(ChangeLogPause.WritingHistory);
        try
        {
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var (sectionId, row) in writes)
                    WriteOne(conn, tx, sectionId, row);
                Exec(conn, "UPDATE temp.undo_steps SET is_undone = $u WHERE id = $id", tx,
                    ("$u", markUndone ? 1L : 0L), ("$id", stepId));
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        finally
        {
            EndPause();
        }
    }

    /// <summary>Deletes, updates or inserts one section row, every column, inside <paramref name="tx"/>.</summary>
    /// <param name="conn">The connection.</param>
    /// <param name="tx">The open transaction.</param>
    /// <param name="sectionId">The row's id.</param>
    /// <param name="row">The row to write; null deletes.</param>
    private static void WriteOne(SqliteConnection conn, SqliteTransaction tx, string sectionId, SectionRow? row)
    {
        if (row is null)
        {
            Exec(conn, "DELETE FROM main.Sections WHERE id = $id", tx, ("$id", sectionId));
            return;
        }

        // The readable columns are recomputed from the course, semester and academic year as they are NOW, with the
        // same subqueries SectionRepository.Insert and Update use: a course whose calendar code was changed after the
        // step must not bring its old code back. Every other column is exactly as logged.
        var values = new (string, object?)[]
        {
            ("$id", sectionId), ("$semester_id", row.SemesterId), ("$course_id", row.CourseId),
            ("$room_id", row.RoomId), ("$section_code", row.SectionCode), ("$data", row.Data),
        };
        var updated = Exec(conn, UpdateSql, tx, values);
        if (updated > 0) return;
        Exec(conn, InsertSql, tx, values);
    }

    /// <summary>Updates one <c>Sections</c> row; the readable columns use the subqueries of <c>SectionRepository.Update</c>.</summary>
    private const string UpdateSql = """
        UPDATE main.Sections
        SET semester_id        = $semester_id,
            course_id          = $course_id,
            room_id            = $room_id,
            section_code       = $section_code,
            course_code        = (SELECT json_extract(data, '$.calendarCode') FROM Courses WHERE id = $course_id),
            semester_name      = (SELECT name FROM Semesters WHERE id = $semester_id),
            academic_year_name = (SELECT ay.name FROM AcademicYears ay
                                  JOIN Semesters s ON s.academic_year_id = ay.id WHERE s.id = $semester_id),
            data               = $data
        WHERE id = $id
        """;

    /// <summary>Inserts one <c>Sections</c> row; the readable columns use the subqueries of <c>SectionRepository.Insert</c>.</summary>
    private const string InsertSql = """
        INSERT INTO main.Sections (id, semester_id, course_id, room_id, section_code, course_code,
                                   semester_name, academic_year_name, data)
        VALUES ($id, $semester_id, $course_id, $room_id, $section_code,
                (SELECT json_extract(data, '$.calendarCode') FROM Courses WHERE id = $course_id),
                (SELECT name FROM Semesters WHERE id = $semester_id),
                (SELECT ay.name FROM AcademicYears ay
                 JOIN Semesters s ON s.academic_year_id = ay.id WHERE s.id = $semester_id),
                $data)
        """;

    // ── Deleted this session ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool WasDeleted(string table, string id) => TryReadDeleted(table, id, out _);

    /// <inheritdoc/>
    public string? DeletedData(string table, string id) => TryReadDeleted(table, id, out var data) ? data : null;

    /// <summary>
    /// Reads the copy of a row deleted this session, provided the row does not exist now (a row deleted and then put
    /// back under the same id has not been deleted, as far as undo is concerned).
    /// </summary>
    /// <param name="table">The table's name (only tables the log tracks are looked up).</param>
    /// <param name="id">The row's id.</param>
    /// <param name="data">The logged <c>data</c> column, or null.</param>
    /// <returns>True when the row was deleted this session and is still gone.</returns>
    private bool TryReadDeleted(string table, string id, out string? data)
    {
        data = null;
        if (!_available || !_deletedTables.Contains(table)) return false;
        using var cmd = _connection().CreateCommand();
        cmd.CommandText = "SELECT data FROM temp.undo_deleted WHERE tbl = $t AND id = $id " +
                          $"AND NOT EXISTS (SELECT 1 FROM main.{Quote(table)} WHERE id = $id)";
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        data = r.IsDBNull(0) ? null : r.GetString(0);
        return true;
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
        var conn = Require();
        Exec(conn, "DELETE FROM temp.undo_entries; DELETE FROM temp.undo_steps; " +
                   "UPDATE temp.undo_state SET history_incomplete = 1");
    }

    // ── Flags and trimming ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool OtherChanges => _available && Scalar(_connection(), "SELECT other_changes FROM temp.undo_state") != 0;

    /// <inheritdoc/>
    public bool HistoryIncomplete => _available && Scalar(_connection(), "SELECT history_incomplete FROM temp.undo_state") != 0;

    /// <inheritdoc/>
    public void MarkHistoryIncomplete() => Exec(Require(), "UPDATE temp.undo_state SET history_incomplete = 1");

    /// <inheritdoc/>
    public void TrimAfterSave(long editCountAtSnapshot, bool resetFlags)
    {
        var conn = Require();

        // A step still being made (edit count not stored yet) is never trimmed.
        const string Doomed = "(is_undone = 1 OR edit_count <= $n) AND id IS NOT (SELECT current_step FROM temp.undo_state)";
        Exec(conn, $"DELETE FROM temp.undo_entries WHERE step_id IN (SELECT id FROM temp.undo_steps WHERE {Doomed})",
            ("$n", editCountAtSnapshot));
        Exec(conn, $"DELETE FROM temp.undo_steps WHERE {Doomed}", ("$n", editCountAtSnapshot));
        if (resetFlags)
            Exec(conn, "UPDATE temp.undo_state SET other_changes = 0, history_incomplete = 0");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>The connection, or an exception when the log is unavailable.</summary>
    /// <exception cref="InvalidOperationException">The log is unavailable.</exception>
    private SqliteConnection Require()
    {
        if (!_available)
            throw new InvalidOperationException("The section change log is not available on this connection.");
        return _connection();
    }

    /// <summary>Throws when a step or a pause is active (steps and pauses do not nest).</summary>
    /// <param name="conn">The connection.</param>
    /// <exception cref="InvalidOperationException">A step or pause is already active.</exception>
    private static void RequireIdle(SqliteConnection conn)
    {
        if (Scalar(conn, "SELECT COUNT(*) FROM temp.undo_state WHERE current_step IS NOT NULL OR pause <> 0") != 0)
            throw new InvalidOperationException("A change-log step or pause is already active; they do not nest.");
    }

    /// <summary>Quotes an identifier for SQL.</summary>
    /// <param name="name">The identifier.</param>
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    /// <summary>Runs a statement (or several) with named parameters.</summary>
    private static int Exec(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
        => Exec(conn, sql, null, args);

    /// <summary>Runs a statement with named parameters, optionally in a transaction.</summary>
    /// <returns>Rows affected.</returns>
    private static int Exec(SqliteConnection conn, string sql, SqliteTransaction? tx, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Runs a scalar integer query with named parameters.</summary>
    private static long Scalar(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}

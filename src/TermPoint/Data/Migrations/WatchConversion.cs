using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TermPoint.Models;

namespace TermPoint.Data.Migrations;

/// <summary>
/// Schema version 1 to 2: turns every row of the old <c>ProgramWatches</c> table into the Access Analyzer's
/// form, a tag marked <c>isAccessWatch</c> (design: <c>designs/1.2.5/access-analyzer.md</c>, "The upgrade of
/// today's watches", D28, Q37-Q39). Called by <see cref="DatabaseContext"/>'s migration inside its transaction,
/// before the <c>user_version</c> stamp, on every copy of the file that opens at version 1 or lower: a writer's
/// working copy (reaching the shared file with the next ordinary save) and a reader's throw-away snapshot.
///
/// <para><b>Reads the old rows as JSON, not as a model class</b> (the old <c>ProgramWatch</c> model has been deleted).
/// Writes only what the rules below say: the watched flag on a tag row, new tag rows, and the
/// <c>tagIds</c> member of section and course JSON (nothing else in those documents is rewritten, so unknown
/// fields from newer versions survive untouched). A row is rewritten only when its tags really change.</para>
///
/// <para><b>The rules</b> (a "needing" row is one that is not rule 1):</para>
/// <list type="number">
///   <item><b>Tag mode, exactly one tag, no levels:</b> that tag is marked watched, if the watch is enabled
///         (a disabled one needs nothing: Q39). No new tag, no section writes. A missing tag row: nothing to do.</item>
///   <item><b>Course mode:</b> a new tag named as the watch is added to each listed course's <c>tagIds</c>
///         (so new sections get it) and to the <c>tagIds</c> of those courses' sections in the watch's
///         semester only (Q38). Never replaces existing tags; never duplicates.</item>
///   <item><b>Tag mode with several tags, or with levels (or levels only):</b> a new tag named as the watch is
///         added to the sections the watch covered in its semester (today's rule, see
///         the removed ProgramConflictService's <c>ResolveCoveredSections</c>: all the tags, and a section level in the
///         list when levels are given). Not on courses. A watch that covered nothing still gets its (empty)
///         tag, so nothing the user made disappears.</item>
///   <item><b>Same name in several semesters</b> (case-insensitive, trimmed): the rows form a group.
///         Rule-1 rows each mark their own tag. All the group's other rows share ONE new tag, each tagging its
///         own semester's sections, watched when any of those rows is enabled. (A group that mixes rule 1
///         with the other cases therefore ends up with the marked single tag(s) plus one new tag.)</item>
///   <item><b>New tag id</b> = the id of the group's first needing row, in semester order. Semester order is
///         the academic year's name, then the semester's <c>sort_order</c>, then its id (a semester that no
///         longer exists sorts last); rows of one semester are ordered by watch id. If that id is somehow
///         already used by a value row, a deterministic id is derived from it.</item>
///   <item><b>Name clash (Q37):</b> if a tag (or an earlier new tag) already has the name, case-insensitively,
///         the new tag is "NAME (watch)", then "NAME (watch 2)", "(watch 3)"... The name is the first row's
///         trimmed name ("Watch" when blank).</item>
///   <item><b>Disabled watches (Q39)</b> convert too, as unwatched tags.</item>
///   <item><b>Multi-campus</b> stays off (the field is not written).</item>
///   <item><b>All <c>ProgramWatches</c> rows are deleted</b> in the same transaction.</item>
/// </list>
///
/// <para>Deterministic: no clock, no random ids, every collection iterated in a fixed order, so two copies of
/// the same unconverted file convert to identical rows. The <c>mode</c> member of the watch JSON is a number
/// (0 tag, 1 course) in every row today's app wrote, because the enum has no string converter; a string
/// ("tag"/"course", any case) is accepted too, and a missing member falls back to the table's <c>mode</c>
/// column.</para>
/// </summary>
internal static class WatchConversion
{
    private const string TagType = "tag";

    /// <summary>One old watch row, parsed from its JSON.</summary>
    private sealed record OldWatch(
        string Id, string SemesterId, string Name, bool IsCourseMode, bool IsEnabled,
        List<string> TagIds, List<string> CourseIds, List<string> LevelIds)
    {
        /// <summary>Rule 1: one tag and no levels, so the tag itself becomes the watch.</summary>
        public bool IsSingleTag => !IsCourseMode && TagIds.Count == 1 && LevelIds.Count == 0;
    }

    /// <summary>A section, course or value row whose JSON may change.</summary>
    private sealed class DocRow(string id, JsonObject root)
    {
        public string Id { get; } = id;
        public JsonObject Root { get; } = root;
        public bool Changed { get; set; }
    }

    /// <summary>
    /// Converts every <c>ProgramWatches</c> row and deletes them. Does nothing (and writes nothing) when the
    /// table is empty.
    /// </summary>
    /// <param name="conn">The open connection; the <c>ProgramWatches</c> table must exist.</param>
    /// <param name="tx">The migration's transaction; every command is enlisted in it.</param>
    /// <exception cref="SqliteException">A statement failed; the caller rolls the transaction back.</exception>
    internal static void Run(SqliteConnection conn, SqliteTransaction tx)
    {
        var watches = ReadWatches(conn, tx);
        if (watches.Count == 0) return;

        var semesterRank = ReadSemesterRanks(conn, tx);
        watches = watches
            .OrderBy(w => semesterRank.GetValueOrDefault(w.SemesterId, int.MaxValue))
            .ThenBy(w => w.SemesterId, StringComparer.Ordinal)
            .ThenBy(w => w.Id, StringComparer.Ordinal)
            .ToList();

        // Existing tags: id -> parsed row, plus every value id (for the id-collision guard) and tag names.
        var tagRows = ReadValueRows(conn, tx, TagType);
        var usedIds = ReadAllValueIds(conn, tx);
        var usedNames = new HashSet<string>(
            tagRows.Values.Select(t => NameKey(t.Root["name"] is JsonValue nv && nv.TryGetValue<string>(out var n) ? n : string.Empty)));
        var nextSortOrder = tagRows.Count == 0 ? 0 : tagRows.Values.Max(t => ReadInt(t.Root["sortOrder"])) + 1;

        var sections = new Dictionary<string, List<DocRow>>(StringComparer.Ordinal);   // semester id -> rows
        var courses = new Lazy<Dictionary<string, DocRow>>(() => ReadCourses(conn, tx));
        var newTags = new List<(string Id, string Name, int SortOrder, bool Watched)>();

        // Groups in the order of their first row; rows inside a group are already in semester order.
        foreach (var group in watches.GroupBy(w => NameKey(w.Name)).ToList())
        {
            var needing = new List<OldWatch>();
            foreach (var w in group)
            {
                if (w.IsSingleTag)
                {
                    // Rule 1. A missing tag row or a disabled watch needs nothing.
                    if (w.IsEnabled && tagRows.TryGetValue(w.TagIds[0], out var tag)
                        && !(tag.Root["isAccessWatch"] is JsonValue av && av.TryGetValue<bool>(out var already) && already))
                    {
                        tag.Root["isAccessWatch"] = true;
                        tag.Changed = true;
                    }
                }
                else needing.Add(w);
            }
            if (needing.Count == 0) continue;

            var tagId = usedIds.Contains(needing[0].Id) ? DeriveId(needing[0].Id) : needing[0].Id;
            usedIds.Add(tagId);
            var name = UniqueName(needing[0].Name, usedNames);
            newTags.Add((tagId, name, nextSortOrder++, needing.Any(w => w.IsEnabled)));

            foreach (var w in needing)
            {
                if (!sections.TryGetValue(w.SemesterId, out var rows))
                    sections[w.SemesterId] = rows = ReadSections(conn, tx, w.SemesterId);

                if (w.IsCourseMode)
                {
                    var courseIds = new HashSet<string>(w.CourseIds, StringComparer.Ordinal);
                    foreach (var cid in w.CourseIds.Distinct(StringComparer.Ordinal))
                        if (courses.Value.TryGetValue(cid, out var course)) AddTag(course, tagId);
                    foreach (var sec in rows)
                        if (sec.Root["courseId"] is JsonValue cv && cv.TryGetValue<string>(out var c) && courseIds.Contains(c))
                            AddTag(sec, tagId);
                }
                else
                {
                    foreach (var sec in rows)
                        if (Covers(w, sec)) AddTag(sec, tagId);
                }
            }
        }

        // Writes, in a fixed order.
        foreach (var t in tagRows.Values.Where(t => t.Changed).OrderBy(t => t.Id, StringComparer.Ordinal))
            UpdateData(conn, tx, "SchedulingEnvironmentValues", t);
        foreach (var (id, name, sortOrder, watched) in newTags)
            InsertTag(conn, tx, id, name, sortOrder, watched);
        foreach (var sec in sections.OrderBy(s => s.Key, StringComparer.Ordinal).SelectMany(s => s.Value).Where(s => s.Changed))
            UpdateData(conn, tx, "Sections", sec);
        if (courses.IsValueCreated)
            foreach (var c in courses.Value.Values.Where(c => c.Changed).OrderBy(c => c.Id, StringComparer.Ordinal))
                UpdateData(conn, tx, "Courses", c);

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM ProgramWatches";
        del.ExecuteNonQuery();
    }

    // ── Coverage and tagging ──────────────────────────────────────────────────

    /// <summary>
    /// Today's tag-mode coverage: the section carries ALL the watch's tags, and (when levels are listed) its
    /// <c>level</c> is one of them. No tags and no levels covers nothing.
    /// </summary>
    /// <param name="w">A tag-mode watch.</param>
    /// <param name="sec">A section of the watch's semester.</param>
    /// <returns>True when the section was covered.</returns>
    private static bool Covers(OldWatch w, DocRow sec)
    {
        if (w.TagIds.Count == 0 && w.LevelIds.Count == 0) return false;
        if (w.TagIds.Count > 0)
        {
            var has = StringList(sec.Root["tagIds"]);
            if (!w.TagIds.All(has.Contains)) return false;
        }
        if (w.LevelIds.Count > 0)
        {
            var level = sec.Root["level"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            if (string.IsNullOrEmpty(level) || !w.LevelIds.Contains(level)) return false;
        }
        return true;
    }

    /// <summary>Appends <paramref name="tagId"/> to the row's <c>tagIds</c> unless it is already there.</summary>
    /// <param name="row">The section or course row.</param>
    /// <param name="tagId">The tag to add.</param>
    private static void AddTag(DocRow row, string tagId)
    {
        if (row.Root["tagIds"] is not JsonArray arr)
        {
            arr = new JsonArray();
            row.Root["tagIds"] = arr;
        }
        if (arr.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s == tagId)) return;
        arr.Add(tagId);
        row.Changed = true;
    }

    // ── Names and ids ─────────────────────────────────────────────────────────

    /// <summary>The comparison key for names: trimmed, case-insensitive.</summary>
    /// <param name="name">A tag or watch name.</param>
    /// <returns>The key.</returns>
    private static string NameKey(string name) => name.Trim().ToLowerInvariant();

    /// <summary>
    /// Picks the new tag's name (Q37): the watch's own name, or "NAME (watch)", "NAME (watch 2)"... when a tag
    /// already has it, and records the choice in <paramref name="used"/>.
    /// </summary>
    /// <param name="watchName">The watch's name as stored.</param>
    /// <param name="used">Name keys of every tag so far, existing and new.</param>
    /// <returns>The name for the new tag.</returns>
    private static string UniqueName(string watchName, HashSet<string> used)
    {
        var baseName = watchName.Trim();
        if (baseName.Length == 0) baseName = "Watch";
        var candidate = baseName;
        for (var n = 1; used.Contains(NameKey(candidate)); n++)
            candidate = n == 1 ? $"{baseName} (watch)" : $"{baseName} (watch {n})";
        used.Add(NameKey(candidate));
        return candidate;
    }

    /// <summary>A repeatable GUID derived from an id, for the (practically impossible) id collision.</summary>
    /// <param name="id">The colliding id.</param>
    /// <returns>A new id, the same every time for the same input.</returns>
    private static string DeriveId(string id)
        => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(id + ":watch"))).ToString();

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>Reads every old watch row (in id order) and parses its JSON.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <returns>The parsed watches.</returns>
    private static List<OldWatch> ReadWatches(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, semester_id, name, mode, data FROM ProgramWatches ORDER BY id";
        var result = new List<OldWatch>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var root = ParseObject(r.GetString(4));
            var name = root["name"] is JsonValue nv && nv.TryGetValue<string>(out var ns) ? ns
                     : r.IsDBNull(2) ? string.Empty : r.GetString(2);
            result.Add(new OldWatch(
                r.GetString(0), r.GetString(1), name,
                IsCourse(root["mode"], r.IsDBNull(3) ? null : r.GetString(3)),
                !(root["isEnabled"] is JsonValue ev && ev.TryGetValue<bool>(out var e)) || e,
                StringList(root["tagIds"]), StringList(root["courseIds"]), StringList(root["levelIds"])));
        }
        return result;
    }

    /// <summary>Course mode when the JSON says 1 or "course"; falls back to the column text when absent.</summary>
    /// <param name="mode">The JSON <c>mode</c> member (number or string), or null.</param>
    /// <param name="columnText">The table's <c>mode</c> column ("tag"/"course"), or null.</param>
    /// <returns>True for course mode.</returns>
    private static bool IsCourse(JsonNode? mode, string? columnText)
    {
        if (mode is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i == 1;
            if (v.TryGetValue<string>(out var s))
                return int.TryParse(s, out var si) ? si == 1 : s.Trim().Equals("course", StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(columnText?.Trim(), "course", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Semester id to its position in semester order (see the class remarks).</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <returns>The rank of each semester.</returns>
    private static Dictionary<string, int> ReadSemesterRanks(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT s.id FROM Semesters s LEFT JOIN AcademicYears ay ON ay.id = s.academic_year_id " +
            "ORDER BY COALESCE(ay.name, ''), s.sort_order, s.id";
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ranks[r.GetString(0)] = ranks.Count;
        return ranks;
    }

    /// <summary>The <c>SchedulingEnvironmentValues</c> rows of one type, keyed by id.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <param name="type">The value type, e.g. <c>tag</c>.</param>
    /// <returns>The parsed rows.</returns>
    private static Dictionary<string, DocRow> ReadValueRows(SqliteConnection conn, SqliteTransaction tx, string type)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, data FROM SchedulingEnvironmentValues WHERE type = $t ORDER BY id";
        cmd.Parameters.AddWithValue("$t", type);
        return ReadDocRows(cmd).ToDictionary(d => d.Id, StringComparer.Ordinal);
    }

    /// <summary>Every id in <c>SchedulingEnvironmentValues</c>, whatever its type.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <returns>The set of ids.</returns>
    private static HashSet<string> ReadAllValueIds(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM SchedulingEnvironmentValues";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    }

    /// <summary>The sections of one semester, in id order.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <param name="semesterId">The semester.</param>
    /// <returns>The parsed rows.</returns>
    private static List<DocRow> ReadSections(SqliteConnection conn, SqliteTransaction tx, string semesterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, data FROM Sections WHERE semester_id = $s ORDER BY id";
        cmd.Parameters.AddWithValue("$s", semesterId);
        return ReadDocRows(cmd);
    }

    /// <summary>Every course, keyed by id.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <returns>The parsed rows.</returns>
    private static Dictionary<string, DocRow> ReadCourses(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, data FROM Courses ORDER BY id";
        return ReadDocRows(cmd).ToDictionary(d => d.Id, StringComparer.Ordinal);
    }

    /// <summary>Runs an (id, data) query and parses each row into a <see cref="DocRow"/>.</summary>
    /// <param name="cmd">The command to run.</param>
    /// <returns>The rows in query order.</returns>
    private static List<DocRow> ReadDocRows(SqliteCommand cmd)
    {
        var rows = new List<DocRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(new DocRow(r.GetString(0), ParseObject(r.GetString(1))));
        return rows;
    }

    /// <summary>Parses a <c>data</c> column; anything that is not a JSON object reads as an empty one.</summary>
    /// <param name="json">The column text.</param>
    /// <returns>The parsed object.</returns>
    private static JsonObject ParseObject(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { return new JsonObject(); }
    }

    /// <summary>The strings of a JSON array (other element kinds and a missing array read as nothing).</summary>
    /// <param name="node">The array node, or null.</param>
    /// <returns>The strings, in order.</returns>
    private static List<string> StringList(JsonNode? node)
    {
        var list = new List<string>();
        if (node is JsonArray arr)
            foreach (var n in arr)
                if (n is JsonValue v && v.TryGetValue<string>(out var s)) list.Add(s);
        return list;
    }

    /// <summary>An int member, 0 when missing or not a number.</summary>
    /// <param name="node">The member, or null.</param>
    /// <returns>The value.</returns>
    private static int ReadInt(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

    // ── Writing ───────────────────────────────────────────────────────────────

    /// <summary>Writes a changed row's <c>data</c> back, leaving its other columns alone.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <param name="table">The table (a fixed name, never user input).</param>
    /// <param name="row">The changed row.</param>
    private static void UpdateData(SqliteConnection conn, SqliteTransaction tx, string table, DocRow row)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"UPDATE {table} SET data = $d WHERE id = $id";
        cmd.Parameters.AddWithValue("$d", row.Root.ToJsonString());
        cmd.Parameters.AddWithValue("$id", row.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inserts one new tag row (readable <c>type</c> and <c>name</c> columns filled in).</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="tx">The migration's transaction.</param>
    /// <param name="id">The new tag's id.</param>
    /// <param name="name">Its name.</param>
    /// <param name="sortOrder">Its sort order (after the existing tags).</param>
    /// <param name="watched">Whether it is marked as an access watch.</param>
    private static void InsertTag(SqliteConnection conn, SqliteTransaction tx, string id, string name, int sortOrder, bool watched)
    {
        var value = new SchedulingEnvironmentValue { Id = id, Name = name, SortOrder = sortOrder, IsAccessWatch = watched };
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO SchedulingEnvironmentValues (id, type, name, data) VALUES ($id, $type, $name, $data)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$type", TagType);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(value));
        cmd.ExecuteNonQuery();
    }
}

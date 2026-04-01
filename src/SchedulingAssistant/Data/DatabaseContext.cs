using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace SchedulingAssistant.Data;

/// <summary>
/// SQLite-backed implementation of <see cref="IDatabaseContext"/>.
/// Opens the database file, creates the schema on first run, applies migrations, and seeds initial data.
/// </summary>
public class DatabaseContext : IDatabaseContext
{
    // Stored as SqliteConnection so internal helpers (SeedData, schema migrations) can use it
    // directly without casting. Exposed publicly as the base DbConnection type.
    // Not readonly because ReinitializeConnection needs to reassign it after a file refresh.
    private SqliteConnection _conn;

    /// <inheritdoc/>
    public DbConnection Connection => _conn;

    public DatabaseContext(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        try
        {
            _conn.Open();
            InitializeSchema();
            Migrate();
            SeedData.EnsureSeeded(_conn);
        }
        catch (Exception ex)
        {
            _conn.Dispose();
            throw new InvalidOperationException(
                $"Failed to open or initialize the database at '{dbPath}'. " +
                "The file may be locked by another process, corrupted, or the path may be invalid.",
                ex);
        }
    }

    private void InitializeSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AcademicYears (
                id   TEXT PRIMARY KEY,
                name TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Semesters (
                id               TEXT PRIMARY KEY,
                academic_year_id TEXT NOT NULL DEFAULT '',
                name             TEXT NOT NULL,
                sort_order       INTEGER NOT NULL DEFAULT 0,
                data             TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Instructors (
                id         TEXT PRIMARY KEY,
                last_name  TEXT,
                first_name TEXT,
                initials   TEXT,
                data       TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Rooms (
                id          TEXT PRIMARY KEY,
                building    TEXT,
                room_number TEXT,
                data        TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS LegalStartTimes (
                academic_year_id TEXT NOT NULL,
                block_length     REAL NOT NULL,
                start_times      TEXT NOT NULL,
                PRIMARY KEY (academic_year_id, block_length)
            );

            CREATE TABLE IF NOT EXISTS Subjects (
                id           TEXT PRIMARY KEY,
                name         TEXT,
                abbreviation TEXT,
                data         TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Courses (
                id            TEXT PRIMARY KEY,
                subject_id    TEXT NOT NULL,
                calendar_code TEXT,
                title         TEXT,
                data          TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Sections (
                id           TEXT PRIMARY KEY,
                semester_id  TEXT NOT NULL,
                course_id    TEXT,
                room_id      TEXT,
                section_code TEXT,
                course_code  TEXT,
                data         TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS SchedulingEnvironmentValues (
                id   TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                name TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS BlockPatterns (
                id   TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS AcademicUnits (
                id   TEXT PRIMARY KEY,
                name TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Releases (
                id            TEXT PRIMARY KEY,
                semester_id   TEXT NOT NULL,
                instructor_id TEXT NOT NULL,
                data          TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS InstructorCommitments (
                id              TEXT PRIMARY KEY,
                instructor_id   TEXT NOT NULL,
                semester_id     TEXT NOT NULL,
                instructor_name TEXT,
                semester_name   TEXT,
                data            TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS SectionPrefixes (
                id     TEXT PRIMARY KEY,
                prefix TEXT NOT NULL,
                data   TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Campuses (
                id   TEXT PRIMARY KEY,
                name TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Handles schema migrations for existing databases.
    /// All tables are created by <see cref="InitializeSchema"/>; this method only handles
    /// column additions and data backfills for databases created before certain columns existed.
    /// </summary>
    private void Migrate()
    {
        using var cmd = _conn.CreateCommand();

        // Rename SectionPropertyValues to SchedulingEnvironmentValues if the old table still exists
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SectionPropertyValues'";
        var oldTableExists = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        if (oldTableExists)
        {
            cmd.CommandText = "ALTER TABLE SectionPropertyValues RENAME TO SchedulingEnvironmentValues";
            cmd.ExecuteNonQuery();
        }

        // Purge invalid commitment records (missing instructor_id or semester_id)
        cmd.CommandText = "DELETE FROM InstructorCommitments WHERE instructor_id IS NULL OR instructor_id = '' OR semester_id IS NULL OR semester_id = ''";
        cmd.ExecuteNonQuery();

        // Add academic_year_id column to LegalStartTimes if it doesn't exist
        // (for databases upgraded from the old schema)
        cmd.CommandText = "PRAGMA table_info(LegalStartTimes)";
        using var reader = cmd.ExecuteReader();
        var columns = new HashSet<string>();
        while (reader.Read())
            columns.Add((string)reader[1]);

        if (!columns.Contains("academic_year_id"))
        {
            cmd.CommandText = "ALTER TABLE LegalStartTimes ADD COLUMN academic_year_id TEXT DEFAULT ''";
            cmd.ExecuteNonQuery();
        }

        // Human-readable columns for DB browsing (added across all entity tables)
        AddColumnIfMissing(_conn, "AcademicYears",       "name",          "TEXT");
        AddColumnIfMissing(_conn, "Instructors",         "last_name",     "TEXT");
        AddColumnIfMissing(_conn, "Instructors",         "first_name",    "TEXT");
        AddColumnIfMissing(_conn, "Instructors",         "initials",      "TEXT");
        AddColumnIfMissing(_conn, "Rooms",               "building",      "TEXT");
        AddColumnIfMissing(_conn, "Rooms",               "room_number",   "TEXT");
        AddColumnIfMissing(_conn, "Subjects",            "name",          "TEXT");
        AddColumnIfMissing(_conn, "Subjects",            "abbreviation",  "TEXT");
        AddColumnIfMissing(_conn, "Courses",             "calendar_code", "TEXT");
        AddColumnIfMissing(_conn, "Courses",             "title",         "TEXT");
        AddColumnIfMissing(_conn, "Sections",            "section_code",  "TEXT");
        AddColumnIfMissing(_conn, "Sections",            "course_code",   "TEXT");
        AddColumnIfMissing(_conn, "SchedulingEnvironmentValues", "name",   "TEXT");
        AddColumnIfMissing(_conn, "AcademicUnits",       "name",          "TEXT");
        AddColumnIfMissing(_conn, "InstructorCommitments", "instructor_name", "TEXT");
        AddColumnIfMissing(_conn, "InstructorCommitments", "semester_name",   "TEXT");

        BackfillReadableColumns(_conn);
    }

    /// <summary>
    /// Adds a column to a table if it does not already exist.
    /// Safe to call on every startup — exits immediately when column is present.
    /// </summary>
    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string columnDef)
    {
        using var infoCmd = conn.CreateCommand();
        infoCmd.CommandText = $"PRAGMA table_info({table})";
        var found = false;
        using (var r = infoCmd.ExecuteReader())
            while (r.Read())
                if (r.GetString(1) == column) { found = true; break; }

        if (found) return;

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDef}";
        alter.ExecuteNonQuery();
    }

    /// <summary>
    /// One-time backfill: populates human-readable columns from JSON data for rows
    /// that were written before the columns existed (WHERE col IS NULL guard).
    /// Safe to run on every startup — does nothing once all rows are populated.
    /// </summary>
    private static void BackfillReadableColumns(SqliteConnection conn)
    {
        var statements = new[]
        {
            "UPDATE AcademicYears SET name = json_extract(data, '$.name') WHERE name IS NULL",

            "UPDATE Instructors SET last_name  = json_extract(data, '$.lastName'),  " +
                                   "first_name = json_extract(data, '$.firstName'), " +
                                   "initials   = json_extract(data, '$.initials')   " +
            "WHERE last_name IS NULL",

            "UPDATE Rooms SET building    = json_extract(data, '$.building'),   " +
                             "room_number = json_extract(data, '$.roomNumber')  " +
            "WHERE building IS NULL",

            "UPDATE Subjects SET name         = json_extract(data, '$.name'),                  " +
                                "abbreviation = json_extract(data, '$.calendarAbbreviation')   " +
            "WHERE name IS NULL",

            "UPDATE Courses SET calendar_code = json_extract(data, '$.calendarCode'), " +
                               "title        = json_extract(data, '$.title')          " +
            "WHERE calendar_code IS NULL",

            "UPDATE Sections SET section_code = json_extract(data, '$.sectionCode'), " +
                                "course_code  = (SELECT json_extract(c.data, '$.calendarCode') " +
                                                "FROM Courses c WHERE c.id = Sections.course_id) " +
            "WHERE section_code IS NULL",

            "UPDATE SchedulingEnvironmentValues SET name = json_extract(data, '$.name') WHERE name IS NULL",

            "UPDATE AcademicUnits SET name = json_extract(data, '$.name') WHERE name IS NULL",

            "UPDATE InstructorCommitments SET instructor_name = " +
                "(SELECT first_name || ' ' || last_name FROM Instructors i WHERE i.id = InstructorCommitments.instructor_id) " +
            "WHERE instructor_name IS NULL",

            "UPDATE InstructorCommitments SET semester_name = " +
                "(SELECT ay.name || ' — ' || s.name FROM Semesters s JOIN AcademicYears ay ON ay.id = s.academic_year_id WHERE s.id = InstructorCommitments.semester_id) " +
            "WHERE semester_name IS NULL",
        };

        foreach (var sql in statements)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Closes the current database connection and clears the connection pool.
    /// Call this before <see cref="CheckoutService.RefreshReadOnlySnapshotAsync"/> overwrites D''
    /// so that the file is not held open during the rename. Follow with
    /// <see cref="ReinitializeConnection"/> to reopen after the overwrite.
    /// </summary>
    public void CloseConnection()
    {
        _conn.Dispose();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Closes the current connection and opens a new one to the specified database path.
    /// Used by read-only instances after <see cref="CheckoutService.RefreshReadOnlySnapshotAsync"/>
    /// updates the D'' snapshot file — this reconnects to the refreshed file content.
    /// </summary>
    /// <param name="newDbPath">
    /// Absolute path to the database file. Typically this is the same path as before (D''),
    /// but the underlying file has been replaced with a fresh copy from D.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown if the new database cannot be opened.</exception>
    public void ReinitializeConnection(string newDbPath)
    {
        // Close and clear the old connection from the pool.
        _conn.Dispose();
        SqliteConnection.ClearAllPools();

        // Open a new connection to the (possibly replaced) database file.
        _conn = new SqliteConnection($"Data Source={newDbPath}");
        try
        {
            _conn.Open();
            // No need to call InitializeSchema or Migrate — the new file already has the schema
            // (it was a copy of the old D'', which was a consistent snapshot).
        }
        catch (Exception ex)
        {
            _conn.Dispose();
            throw new InvalidOperationException(
                $"Failed to reopen the database at '{newDbPath}' after refresh. " +
                "The file may be locked by another process.",
                ex);
        }
    }

    public void Dispose()
    {
        _conn.Dispose();

        // Microsoft.Data.Sqlite pools connections by default: Dispose() returns
        // the connection to the pool rather than closing it, so the OS file handle
        // on D is retained. ClearAllPools() destroys every pooled entry, releasing
        // all file handles immediately. Without this, File.Move(D.tmp → D) in
        // CheckoutService.SaveAsync fails on Windows with "Access to the path is
        // denied" because the pool still holds D open without FILE_SHARE_DELETE.
        SqliteConnection.ClearAllPools();
    }
}

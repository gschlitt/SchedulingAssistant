using Microsoft.Data.Sqlite;

namespace SchedulingAssistant.Data;

public class DatabaseContext : IDisposable
{
    public SqliteConnection Connection { get; }

    public DatabaseContext(string dbPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            Connection = new SqliteConnection($"Data Source={dbPath}");
            Connection.Open();

            InitializeSchema();
            Migrate();
            SeedData.EnsureSeeded(Connection);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open or initialize the database at '{dbPath}'. " +
                "The file may be locked by another process, corrupted, or the path may be invalid.",
                ex);
        }
    }

    private void InitializeSchema()
    {
        using var cmd = Connection.CreateCommand();
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

            CREATE TABLE IF NOT EXISTS SectionPropertyValues (
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
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Handles schema migrations for existing databases.
    /// </summary>
    private void Migrate()
    {
        using var cmd = Connection.CreateCommand();

        // BlockPatterns table (if missing)
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BlockPatterns (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();

        // AcademicUnits table (if missing)
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AcademicUnits (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();

        // Releases table (if missing)
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Releases (
                id TEXT PRIMARY KEY,
                semester_id TEXT NOT NULL,
                instructor_id TEXT NOT NULL,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
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
        AddColumnIfMissing(Connection, "AcademicYears",       "name",          "TEXT");
        AddColumnIfMissing(Connection, "Instructors",         "last_name",     "TEXT");
        AddColumnIfMissing(Connection, "Instructors",         "first_name",    "TEXT");
        AddColumnIfMissing(Connection, "Instructors",         "initials",      "TEXT");
        AddColumnIfMissing(Connection, "Rooms",               "building",      "TEXT");
        AddColumnIfMissing(Connection, "Rooms",               "room_number",   "TEXT");
        AddColumnIfMissing(Connection, "Subjects",            "name",          "TEXT");
        AddColumnIfMissing(Connection, "Subjects",            "abbreviation",  "TEXT");
        AddColumnIfMissing(Connection, "Courses",             "calendar_code", "TEXT");
        AddColumnIfMissing(Connection, "Courses",             "title",         "TEXT");
        AddColumnIfMissing(Connection, "Sections",            "section_code",  "TEXT");
        AddColumnIfMissing(Connection, "Sections",            "course_code",   "TEXT");
        AddColumnIfMissing(Connection, "SectionPropertyValues", "name",        "TEXT");
        AddColumnIfMissing(Connection, "AcademicUnits",       "name",          "TEXT");

        BackfillReadableColumns(Connection);
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

            "UPDATE SectionPropertyValues SET name = json_extract(data, '$.name') WHERE name IS NULL",

            "UPDATE AcademicUnits SET name = json_extract(data, '$.name') WHERE name IS NULL",
        };

        foreach (var sql in statements)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}

using Microsoft.Data.Sqlite;

namespace SchedulingAssistant.Data;

public class DatabaseContext : IDisposable
{
    public SqliteConnection Connection { get; }

    public DatabaseContext(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        Connection = new SqliteConnection($"Data Source={dbPath}");
        Connection.Open();

        InitializeSchema();
        Migrate();
        SeedData.EnsureSeeded(Connection);
    }

    private void InitializeSchema()
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AcademicYears (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Semesters (
                id TEXT PRIMARY KEY,
                academic_year_id TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Instructors (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Rooms (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS LegalStartTimes (
                block_length REAL PRIMARY KEY,
                start_times TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Subjects (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Courses (
                id TEXT PRIMARY KEY,
                subject_id TEXT NOT NULL,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS Sections (
                id TEXT PRIMARY KEY,
                semester_id TEXT NOT NULL,
                course_id TEXT,
                instructor_id TEXT,
                room_id TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Handles schema migrations for existing databases.
    /// </summary>
    private void Migrate()
    {
        // Add course_id column to Sections if it doesn't exist (pre-Course era databases)
        if (!ColumnExists("Sections", "course_id"))
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE Sections ADD COLUMN course_id TEXT";
            cmd.ExecuteNonQuery();
        }

        // Add academic_year_id column to Semesters if it doesn't exist
        if (!ColumnExists("Semesters", "academic_year_id"))
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE Semesters ADD COLUMN academic_year_id TEXT NOT NULL DEFAULT ''";
            cmd.ExecuteNonQuery();
        }
    }

    private bool ColumnExists(string table, string column)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}

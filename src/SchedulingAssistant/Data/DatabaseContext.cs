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
                academic_year_id TEXT NOT NULL,
                block_length REAL NOT NULL,
                start_times TEXT NOT NULL,
                PRIMARY KEY (academic_year_id, block_length)
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
                room_id TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS SectionPropertyValues (
                id   TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS BlockPatterns (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
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

        // Add academic_year_id column to LegalStartTimes if it doesn't exist
        // (for databases upgraded from the old schema)
        cmd.CommandText = """
            PRAGMA table_info(LegalStartTimes);
            """;
        using var reader = cmd.ExecuteReader();
        var columns = new HashSet<string>();
        while (reader.Read())
            columns.Add((string)reader[1]);

        if (!columns.Contains("academic_year_id"))
        {
            cmd.CommandText = """
                ALTER TABLE LegalStartTimes ADD COLUMN academic_year_id TEXT DEFAULT '';
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}

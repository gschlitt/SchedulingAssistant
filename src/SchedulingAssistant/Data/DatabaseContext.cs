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
                room_id TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS SectionPropertyValues (
                id   TEXT PRIMARY KEY,
                type TEXT NOT NULL,
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
        // No pending migrations.
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}

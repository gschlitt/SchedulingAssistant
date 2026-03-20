namespace SchedulingAssistant.Exceptions;

/// <summary>
/// Thrown by <see cref="App.InitializeServices"/> when <c>PRAGMA integrity_check</c>
/// reports structural problems in the main database file. Catching this in the startup
/// path allows <see cref="MainWindow"/> to present a restore dialog before any further
/// reads or writes are attempted.
/// </summary>
public class DatabaseCorruptException : Exception
{
    /// <summary>Full path to the database file that failed the integrity check.</summary>
    public string DatabasePath { get; }

    /// <param name="databasePath">Full path to the corrupt database file.</param>
    public DatabaseCorruptException(string databasePath)
        : base($"Database integrity check failed: {databasePath}")
    {
        DatabasePath = databasePath;
    }
}

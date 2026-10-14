namespace TermPoint.Exceptions;

/// <summary>
/// Thrown by <see cref="App.InitializeServices"/> when the local copy of the database (D' for a writer,
/// D'' for a reader) shows positive evidence of damage: <c>PRAGMA integrity_check</c> reported problems, or
/// SQLite said the file is corrupt or not a database. A copy that merely could not be read right now is NOT
/// this — that is <see cref="DatabaseInUseException"/>. <see cref="MainWindow"/> catches it and decides what to
/// do (spec item 5, step 5d): re-check the shared database, and either make one fresh copy or send the user to
/// the recovery chooser. Nothing is opened, read or written through the damaged copy before then.
/// </summary>
public class DatabaseCorruptException : Exception
{
    /// <summary>Full path to the database file that failed the integrity check. For the log only: never shown to the user.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// A short SQLite summary of the damage — the first lines of the <c>integrity_check</c> output, or SQLite's
    /// code and message — or null when none is available. Names pages and tables, never data and never a path.
    /// For the log and, later, the damage report (spec item 5, step 5e).
    /// </summary>
    public string? Summary { get; }

    /// <param name="databasePath">Full path to the corrupt database file.</param>
    /// <param name="summary">SQLite's short account of the damage, or null.</param>
    public DatabaseCorruptException(string databasePath, string? summary = null)
        : base($"Database integrity check failed: {databasePath}")
    {
        DatabasePath = databasePath;
        Summary      = summary;
    }
}

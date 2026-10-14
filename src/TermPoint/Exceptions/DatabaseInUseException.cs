namespace TermPoint.Exceptions;

/// <summary>
/// Thrown by <see cref="App.InitializeServices"/> when the local copy of the database (D' for a writer,
/// D'' for a reader) could not be examined because another program on this computer is using it
/// (spec item 5, step 5d) — most often antivirus or backup software scanning a file that was just copied.
/// The check was retried quietly (<see cref="Services.LocalCopyIntegrity.MaxAttempts"/> attempts, one second apart)
/// before this was thrown, so it means "still busy", not "damaged": the file's health is unknown and must
/// never be described to the user as damage. <see cref="MainWindow"/> catches it and offers Try again / Close.
/// </summary>
public class DatabaseInUseException : Exception
{
    /// <summary>Full path of the local copy that could not be examined. For the log only: never shown to the user.</summary>
    public string DatabasePath { get; }

    /// <summary>How many times the check was attempted before giving up.</summary>
    public int Attempts { get; }

    /// <summary>
    /// SQLite's code and message for the last attempt (for example <c>SQLite error 14: 'unable to open database
    /// file'.</c>), or null when none is available. Contains no path and no data. For the log only.
    /// </summary>
    public string? Summary { get; }

    /// <param name="databasePath">Full path of the local copy that could not be examined.</param>
    /// <param name="attempts">How many times the check was attempted.</param>
    /// <param name="summary">SQLite's code and message for the last attempt, or null.</param>
    public DatabaseInUseException(string databasePath, int attempts, string? summary = null)
        // The message is path-free on purpose: the wizard shows exception messages inline.
        : base($"The local copy of the database is being used by another program (checked {attempts} time(s)).")
    {
        DatabasePath = databasePath;
        Attempts     = attempts;
        Summary      = summary;
    }
}

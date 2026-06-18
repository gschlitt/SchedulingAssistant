namespace TermPoint.Exceptions;

/// <summary>
/// Thrown by <see cref="App.InitializeServices"/> (via <see cref="Data.DatabaseContext"/>) when the
/// database cannot be opened because its folder rejects file creation/modification. The most common
/// cause is Windows Defender <b>Controlled Folder Access</b> protecting a known folder such as
/// Documents or Desktop, but a read-only ACL or read-only media produce the same symptom. Catching
/// this in the startup path lets <see cref="MainWindow"/> show a neutral, actionable message instead
/// of the raw SQLite "unable to open database file" (error 14) error.
/// </summary>
public class DatabaseFolderNotWritableException : Exception
{
    /// <summary>Full path to the database file whose folder rejected writes.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Neutral, plain-language message safe to show to non-technical users. Avoids alarming terms
    /// (e.g. "ransomware") and tells the user what to do — choose a different location.
    /// </summary>
    public string UserMessage { get; }

    /// <summary>
    /// Optional technical explanation aimed at IT support, naming Controlled Folder Access and the
    /// allow-an-app remedy. <c>null</c> when the folder is not a protected known folder (i.e. the
    /// cause is a generic non-writable folder rather than CFA).
    /// </summary>
    public string? ItDetail { get; }

    /// <param name="databasePath">Full path to the database file that could not be opened.</param>
    /// <param name="userMessage">Neutral, user-facing message describing the problem and the fix.</param>
    /// <param name="itDetail">Optional technical detail for IT support; <c>null</c> to omit.</param>
    /// <param name="inner">The underlying SQLite/IO exception that triggered this.</param>
    public DatabaseFolderNotWritableException(string databasePath, string userMessage, string? itDetail, Exception? inner)
        : base($"Database folder is not writable: {databasePath}", inner)
    {
        DatabasePath = databasePath;
        UserMessage  = userMessage;
        ItDetail     = itDetail;
    }
}

namespace TermPoint.Services;

/// <summary>
/// What the database-session lifecycle (<see cref="SessionLifecycle"/>) needs from a database that has just been
/// opened (spec item 9.1, step 9.1b): exactly the operations it performs on the root view model today, and
/// nothing more, so the lifecycle itself never references a view model type. <see cref="MainWindow"/> wraps the
/// <c>MainWindowViewModel</c> that <c>App.InitializeServices</c> returns in a small adapter that implements this
/// by calling exactly the same members; a test supplies a plain fake.
/// </summary>
public interface IOpenedDatabase
{
    /// <summary>
    /// Sets the database name shown in the title bar and the full path it was opened from (the canonical path of
    /// the shared database, never a local working copy).
    /// </summary>
    /// <param name="name">The display name (the file name without its extension).</param>
    /// <param name="fullPath">The canonical full path of the shared database (D).</param>
    void SetDatabaseName(string name, string fullPath);

    /// <summary>
    /// Re-applies the academic year and semester selection that was captured before the previous database was
    /// closed, so a switch (for example upgrading read-only to write) keeps the user's place.
    /// </summary>
    /// <param name="yearId">The ID of the academic year to select.</param>
    /// <param name="semesterIds">The IDs of the semesters to select within it.</param>
    void RestoreSemesterSelection(string yearId, IReadOnlySet<string> semesterIds);
}

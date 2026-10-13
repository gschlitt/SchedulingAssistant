namespace TermPoint.Services;

/// <summary>How far a database switch had got when it stopped (spec item 4d).</summary>
public enum SwitchPhase
{
    /// <summary>
    /// The pre-check had not passed yet, or it refused. The database that was open, if any, has not
    /// been touched: no save, no lock released, no container disposed.
    /// </summary>
    BeforeTeardown,

    /// <summary>
    /// The previous database has been (at least partly) closed: it was saved and released and its
    /// container disposed. Nothing is open any more, whatever happens next.
    /// </summary>
    AfterTeardown
}

/// <summary>How a database switch ended.</summary>
public enum SwitchResult
{
    /// <summary>The requested database is open and the window is wired to it.</summary>
    Opened,

    /// <summary>
    /// The switch did not happen, but the database that was open before it still is, untouched. The
    /// user has been told why; nothing more is needed.
    /// </summary>
    StayedOnCurrentDatabase,

    /// <summary>
    /// The switch did not happen and NO database is open: either the previous one was already closed
    /// when the failure hit, or none was open to begin with (retrying from the chooser, the
    /// post-wizard open). The window must be covered and the user sent to the database chooser.
    /// </summary>
    NoDatabaseOpen
}

/// <summary>
/// The rules for what happens when a database switch does not end with the requested database open
/// (spec item 4d), kept free of any UI so they can be unit-tested:
/// <list type="bullet">
/// <item><description><see cref="Decide"/> — stay on the open database, or go to the chooser.</description></item>
/// <item><description><see cref="RunChooserLoopAsync"/> — keep offering the chooser until a database opens or the user leaves.</description></item>
/// </list>
///
/// <para><b>Background:</b> a switch used to close the old database's services before checking the new
/// one out. If the checkout then failed (unreachable, file gone, "Couldn't Open the Database" answered
/// Close, …) <c>MainWindow.SwitchDatabaseAsync</c> just returned, leaving a window bound to a database that
/// was no longer connected. The decided behaviour (user, 2026-10-12) is the same clean "no database
/// open" state as at startup: the window is covered and the startup chooser is shown, in recovery mode
/// with <c>RecoveryReason.SwitchFailed</c>.</para>
/// </summary>
public static class DatabaseSwitchRecovery
{
    /// <summary>
    /// Decides how a switch that did NOT open the requested database ends.
    ///
    /// <list type="bullet">
    /// <item><description><see cref="SwitchPhase.AfterTeardown"/> → always <see cref="SwitchResult.NoDatabaseOpen"/>:
    /// the old database is closed, so staying on it is impossible.</description></item>
    /// <item><description><see cref="SwitchPhase.BeforeTeardown"/> with a database open →
    /// <see cref="SwitchResult.StayedOnCurrentDatabase"/>: a refusal by the pre-check leaves everything as it was.</description></item>
    /// <item><description><see cref="SwitchPhase.BeforeTeardown"/> with NO database open →
    /// <see cref="SwitchResult.NoDatabaseOpen"/>: there is nothing to stay on (the chooser retry, or the
    /// post-wizard open), so a refusal must lead to the chooser too, not to a dead end.</description></item>
    /// </list>
    /// </summary>
    /// <param name="phase">How far the switch had got when it stopped.</param>
    /// <param name="databaseOpen">
    /// Whether a database was open and wired to the window when the switch began — tracked explicitly by the
    /// window, never inferred from the state of the services.
    /// </param>
    /// <returns>
    /// <see cref="SwitchResult.StayedOnCurrentDatabase"/> or <see cref="SwitchResult.NoDatabaseOpen"/>; never
    /// <see cref="SwitchResult.Opened"/>, which is not a failure.
    /// </returns>
    public static SwitchResult Decide(SwitchPhase phase, bool databaseOpen)
        => phase == SwitchPhase.BeforeTeardown && databaseOpen
            ? SwitchResult.StayedOnCurrentDatabase
            : SwitchResult.NoDatabaseOpen;

    /// <summary>
    /// Offers the database chooser until a database opens or the user leaves: the "no database open" loop
    /// that follows a failed switch. Every pass shows the chooser (which awaits the user, so the loop can
    /// never spin on its own); a chosen database is opened; if that fails too the chooser comes back,
    /// naming the path that just failed.
    /// </summary>
    /// <param name="failedPath">The database whose open failed first; shown by the first chooser as the path that couldn't be opened.</param>
    /// <param name="showChooserAsync">
    /// Shows the chooser for the given failed path and returns the database the user picked, or null when
    /// they left it — Exit, which shuts the app down, or Create New, which hands over to the setup wizard
    /// (that flow opens its own database). Either way the loop ends.
    /// </param>
    /// <param name="tryOpenAsync">
    /// Opens the chosen database and returns true only if it ended up open. A failure has already been
    /// reported to the user and the app is back in the no-database state.
    /// </param>
    /// <returns>True when the loop ended with a database opened from the chooser; false when the user left.</returns>
    public static async Task<bool> RunChooserLoopAsync(
        string failedPath,
        Func<string, Task<string?>> showChooserAsync,
        Func<string, Task<bool>> tryOpenAsync)
    {
        var lastFailedPath = failedPath;

        while (true)
        {
            var chosen = await showChooserAsync(lastFailedPath);
            if (chosen is null)
                return false;

            if (await tryOpenAsync(chosen))
                return true;

            lastFailedPath = chosen;
        }
    }
}

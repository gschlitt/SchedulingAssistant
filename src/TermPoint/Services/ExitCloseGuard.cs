namespace TermPoint.Services;

/// <summary>Where the main window is in the life of its close, as seen by <see cref="ExitCloseGuard"/>.</summary>
public enum ExitCloseState
{
    /// <summary>No close has been accepted yet: the window is open and in normal use.</summary>
    Open,

    /// <summary>A close was accepted and its teardown (the exit save, the release, the clean-up) is still running.</summary>
    ClosingInProgress,

    /// <summary>The teardown has finished (or the backstop has passed): the window may now really close.</summary>
    ReadyToClose
}

/// <summary>What the window must do with one close request (see <see cref="ExitCloseGuard.OnCloseRequested"/>).</summary>
public enum ExitCloseDecision
{
    /// <summary>The first request: cancel this close, cover the window and run the teardown.</summary>
    StartTeardown,

    /// <summary>A further request while the teardown runs: cancel it and do nothing else.</summary>
    Ignore,

    /// <summary>Let the close go through: the teardown has finished, or the backstop has passed.</summary>
    LetClose
}

/// <summary>
/// The pure decision behind the main window's close (spec item 15, the 1.2.3 part). Today's problem: the first
/// click on the window's X cancels the close and runs the exit save, but a SECOND click while that save is still
/// running used to be let straight through, ending the process mid-save (the next start then offered
/// "Restore Unsaved Changes?" to a user who had simply closed TermPoint). This class answers one question for
/// every close request: start the teardown, ignore the request, or let the window close.
///
/// <para><b>States.</b> <see cref="ExitCloseState.Open"/> → <see cref="ExitCloseState.ClosingInProgress"/> (the
/// first request) → <see cref="ExitCloseState.ReadyToClose"/> (the teardown finished). The window's second,
/// programmatic <c>Close()</c> after the teardown arrives in <see cref="ExitCloseState.ReadyToClose"/> and is let
/// through.</para>
///
/// <para><b>The backstop.</b> A request that arrives <see cref="Backstop"/> or more after the teardown started is
/// let through even though the teardown has not finished, so a save that hangs can never leave the user with a
/// window that will not close. That is safe because the shared database only ever changes by one atomic rename of a
/// checked copy (rule 1) and an interrupted exit leaves the user's changes kept on this computer for the next
/// open. Time is read from the injected <see cref="TimeProvider"/>'s monotonic stopwatch, so changing the
/// computer's clock cannot fire the backstop early or hold it back.</para>
///
/// <para>No Avalonia types and no I/O: <see cref="MainWindow"/> keeps the window work (hiding, covering, closing)
/// and asks this class what to do. Not thread-safe: it is used from the UI thread only.</para>
/// </summary>
public sealed class ExitCloseGuard
{
    /// <summary>
    /// How long after the teardown started a further close request is let through even though the teardown
    /// has not finished. Two minutes: longer than any save that is still making progress on a slow link, short
    /// enough that a hung one does not trap the user.
    /// </summary>
    public static readonly TimeSpan Backstop = TimeSpan.FromMinutes(2);

    /// <summary>The curtain text while an exit save may be running (the session holds the write lock).</summary>
    public const string SavingAndClosingText = "Saving and closing…";

    /// <summary>The curtain text when there is nothing to save (a read-only session).</summary>
    public const string ClosingText = "Closing…";

    /// <summary>The clock; the system clock in production, a controllable fake in tests.</summary>
    private readonly TimeProvider _time;

    /// <summary>The monotonic timestamp (<see cref="TimeProvider.GetTimestamp"/>) at which the teardown started.</summary>
    private long _teardownStartedAt;

    /// <summary>Creates a guard in the <see cref="ExitCloseState.Open"/> state.</summary>
    /// <param name="time">The time source; <see cref="TimeProvider.System"/> when omitted. Tests pass a fake.</param>
    public ExitCloseGuard(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Where the close is now. Read by the window to decide whether the refusal checks still apply and whether input is blocked.</summary>
    public ExitCloseState State { get; private set; } = ExitCloseState.Open;

    /// <summary>
    /// True once the backstop has let a close through while the teardown was still running. The window is then
    /// already closing, so the teardown's own final <c>Close()</c> must not be called a second time; the caller
    /// also reads it to log that the backstop fired.
    /// </summary>
    public bool BackstopFired { get; private set; }

    /// <summary>
    /// The text for the curtain that covers the window during the close: "Saving and closing…" when the session
    /// is in write mode (the exit save may take a while), "Closing…" otherwise.
    /// </summary>
    /// <param name="writeAccess">True when the session holds the write lock (<c>CheckoutMode.WriteAccess</c>).</param>
    /// <returns>The status line to show on the curtain.</returns>
    public static string StatusText(bool writeAccess) => writeAccess ? SavingAndClosingText : ClosingText;

    /// <summary>
    /// Decides what to do with a close request made now.
    ///
    /// <list type="bullet">
    /// <item><description><see cref="ExitCloseState.Open"/>: the first request. Moves to
    /// <see cref="ExitCloseState.ClosingInProgress"/>, starts the backstop clock and returns
    /// <see cref="ExitCloseDecision.StartTeardown"/>.</description></item>
    /// <item><description><see cref="ExitCloseState.ClosingInProgress"/>: <see cref="ExitCloseDecision.Ignore"/>,
    /// unless <see cref="Backstop"/> or more has passed since the teardown started (the boundary itself counts as
    /// passed), in which case the guard becomes <see cref="ExitCloseState.ReadyToClose"/>, sets
    /// <see cref="BackstopFired"/> and returns <see cref="ExitCloseDecision.LetClose"/>.</description></item>
    /// <item><description><see cref="ExitCloseState.ReadyToClose"/>: <see cref="ExitCloseDecision.LetClose"/>.</description></item>
    /// </list>
    /// </summary>
    /// <returns>What the window must do with this request.</returns>
    public ExitCloseDecision OnCloseRequested()
    {
        switch (State)
        {
            case ExitCloseState.Open:
                State = ExitCloseState.ClosingInProgress;
                _teardownStartedAt = _time.GetTimestamp();
                return ExitCloseDecision.StartTeardown;

            case ExitCloseState.ClosingInProgress:
                if (_time.GetElapsedTime(_teardownStartedAt) < Backstop)
                    return ExitCloseDecision.Ignore;

                // The backstop: the teardown is taking too long. Let this close through (the kept working copy
                // protects the user's changes) and remember it, so the late-finishing teardown does not close again.
                State = ExitCloseState.ReadyToClose;
                BackstopFired = true;
                return ExitCloseDecision.LetClose;

            default:
                return ExitCloseDecision.LetClose;
        }
    }

    /// <summary>
    /// Runs the teardown and, however it ends, moves to <see cref="ExitCloseState.ReadyToClose"/>, so the window's
    /// final <c>Close()</c> is never refused and the window can never be left open, covered and ignoring its X.
    /// A failure is caught and handed back instead of thrown, so the caller can log it and still close.
    /// Await it on the UI thread: it resumes on the caller's context, where the window is closed.
    /// </summary>
    /// <param name="teardown">The teardown to run (<c>SessionLifecycle.TeardownForCloseAsync</c>).</param>
    /// <returns>The exception the teardown threw, or null when it completed normally.</returns>
    public async Task<Exception?> RunTeardownAsync(Func<Task> teardown)
    {
        try
        {
            await teardown();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            State = ExitCloseState.ReadyToClose;
        }
    }
}

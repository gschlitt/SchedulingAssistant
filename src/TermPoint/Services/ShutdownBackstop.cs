using TermPoint.Exceptions;

namespace TermPoint.Services;

/// <summary>
/// Ends the process if it is still alive a while after the exit teardown has finished (spec 1.2.4 item 4.3, design
/// <c>designs/restore-relaunch-1.2.4.md</c>, change B).
///
/// <para><b>Why.</b> After a Save &amp; Backup restore, Avalonia's main loop sometimes never ended although the main
/// window had closed and the teardown had finished: an invisible TermPoint stayed alive until sign-out (field tests
/// 2026-10-22 and 2026-10-27). <c>MainWindow.OnClosed</c> asks Avalonia for the shutdown again (change A); this is
/// the last resort when even that is abandoned.</para>
///
/// <para><b>When it fires.</b> <see cref="Arm"/> is called at "Managed teardown complete" on every close, when the
/// exit save, the lock release, the disposal of the services and the guard release are all done (four rules: nothing
/// is left to protect). After <see cref="Delay"/>, if <c>Main</c> has not returned, it: waits (bounded) for the write
/// lock's background delete, so no stale lock file is left for colleagues; ends the run marker, so the next start does
/// not report "ended unexpectedly"; logs and reports one handled event; and exits with code 0. A normal exit never
/// reaches it: the process ends first, and the timer runs on a background thread.</para>
/// </summary>
public sealed class ShutdownBackstop
{
    /// <summary>How long after the teardown the process may still be alive before the backstop ends it.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);

    /// <summary>The longest the backstop waits for the write lock's background delete before exiting.</summary>
    public static readonly TimeSpan ReleaseWait = TimeSpan.FromSeconds(5);

    /// <summary>The time given to the handled-event report to be sent before the process exits.</summary>
    public static readonly TimeSpan ReportGrace = TimeSpan.FromSeconds(2);

    private readonly Func<bool> _mainReturned;
    private readonly Func<Task?> _pendingRelease;
    private readonly Action _endRunMarker;
    private readonly Action<string> _log;
    private readonly Action _report;
    private readonly Action<int> _exit;
    private readonly Func<TimeSpan, Task> _wait;
    private int _armed; // 0 = not armed; 1 = armed. Interlocked, so Arm is idempotent.

    /// <param name="mainReturned">True once <c>Program.Main</c>'s dispatcher loop has ended (a normal exit).</param>
    /// <param name="pendingRelease">The write lock's background delete still running, or null (<c>WriteLockService.PendingRelease</c>).</param>
    /// <param name="endRunMarker">Marks this run as ended normally (<c>SessionRunMarker.End</c>).</param>
    /// <param name="log">Writes one local log line.</param>
    /// <param name="report">Sends the handled-event report.</param>
    /// <param name="exit">Ends the process with the given code (<see cref="Environment.Exit"/> in production).</param>
    /// <param name="wait">Waits the given time (<see cref="Task.Delay(TimeSpan)"/> in production; instant in tests).</param>
    public ShutdownBackstop(
        Func<bool> mainReturned,
        Func<Task?> pendingRelease,
        Action endRunMarker,
        Action<string> log,
        Action report,
        Action<int> exit,
        Func<TimeSpan, Task>? wait = null)
    {
        _mainReturned   = mainReturned;
        _pendingRelease = pendingRelease;
        _endRunMarker   = endRunMarker;
        _log            = log;
        _report         = report;
        _exit           = exit;
        _wait           = wait ?? Task.Delay;
    }

    /// <summary>
    /// The production backstop: the given checks, <see cref="HandledEventReporter"/> for the report, and
    /// <see cref="Environment.Exit"/> to end the process.
    /// </summary>
    /// <param name="mainReturned">True once <c>Program.Main</c>'s dispatcher loop has ended.</param>
    /// <param name="pendingRelease">The write lock's background delete still running, or null.</param>
    /// <param name="endRunMarker">Marks this run as ended normally.</param>
    /// <param name="logger">The application logger.</param>
    /// <returns>The backstop, not yet armed.</returns>
    public static ShutdownBackstop CreateDefault(Func<bool> mainReturned, Func<Task?> pendingRelease, Action endRunMarker, IAppLogger logger)
        => new(mainReturned, pendingRelease, endRunMarker,
               log:    message => logger.LogInfo(message),
               report: () => HandledEventReporter.Report(HandledEvent.ShutdownBackstopFired, logger),
               exit:   Environment.Exit);

    /// <summary>
    /// Starts the timer, once (later calls do nothing). The work runs on a thread-pool (background) thread, so it
    /// never keeps the process alive and never blocks the caller. Never throws.
    /// </summary>
    /// <returns>The timer's task (for tests); production ignores it.</returns>
    public Task Arm()
    {
        if (Interlocked.Exchange(ref _armed, 1) == 1)
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            try
            {
                await _wait(Delay).ConfigureAwait(false);
                await FireIfStillRunningAsync().ConfigureAwait(false);
            }
            catch
            {
                // The backstop must never be the thing that fails an exit.
            }
        });
    }

    /// <summary>
    /// The decision and the work after the delay. Does nothing when <c>Main</c> has returned. Otherwise waits (at most
    /// <see cref="ReleaseWait"/>) for the lock file's background delete, ends the run marker, logs, reports, gives the
    /// report <see cref="ReportGrace"/>, and exits with code 0. Each step is guarded so a failure never stops the exit.
    /// </summary>
    /// <returns>True when the backstop ended (or tried to end) the process.</returns>
    internal async Task<bool> FireIfStillRunningAsync()
    {
        if (_mainReturned())
            return false;

        try
        {
            var release = _pendingRelease();
            if (release is not null && !release.IsCompleted)
                await Task.WhenAny(release, _wait(ReleaseWait)).ConfigureAwait(false);
        }
        catch { /* a failed or faulted delete must not stop the exit */ }

        // Check again: a slow but normal exit may have finished while we waited.
        if (_mainReturned())
            return false;

        try { _endRunMarker(); } catch { /* never throws by contract; belt and braces */ }
        try
        {
            _log($"[Shutdown] Still running {Delay.TotalSeconds:0} s after the teardown finished: the main loop did not " +
                 "end. Ending the process now (nothing is left to save; the lock was released).");
        }
        catch { }
        try { _report(); await _wait(ReportGrace).ConfigureAwait(false); } catch { }

        _exit(0);
        return true;
    }
}

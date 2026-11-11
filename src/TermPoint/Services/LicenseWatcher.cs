#if !BROWSER
using TermPoint.Licensing;

namespace TermPoint.Services;

/// <summary>
/// Daily licence check for a session that stays open across days. An hourly timer calls <see cref="CheckNow"/>;
/// the first call on each new local date re-evaluates the licence in the background and posts that day's notice
/// (see <see cref="LicenseNotices.ForDailyCheck"/>). A timeout or failure posts nothing and is retried at the next tick.
/// The session's own access level is never changed here: switching to read-only mid-session would mean dropping the
/// write lock, which is the risky path.
///
/// <para>Everything it depends on is injected (clock, evaluation, session status, notice sink) so tests drive
/// <see cref="CheckNow"/> directly with no timers.</para>
/// </summary>
public sealed class LicenseWatcher : IDisposable
{
    /// <summary>How often the timer asks whether a new day has started.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IClock _clock;
    private readonly Func<AppAccessResult?> _evaluate;
    private readonly Func<AppAccessResult> _sessionStatus;
    private readonly Action<AppNotification> _post;
    private readonly Action<string>? _log;
    private DateOnly _lastNoticeDate;
    private Timer? _timer;
    private int _checking;

    /// <summary>
    /// Creates a watcher. Its "last notice date" starts as today, because the open-time notice has just been shown.
    /// </summary>
    /// <param name="clock">The clock deciding what "today" is.</param>
    /// <param name="evaluate">Runs a fresh licence evaluation off the UI thread; returns null on timeout. May throw.</param>
    /// <param name="sessionStatus">Returns the access result this session opened with.</param>
    /// <param name="post">Receives the notice to show (must be safe to call from any thread).</param>
    /// <param name="log">Optional sink for diagnostic lines. Null = silent.</param>
    public LicenseWatcher(
        IClock clock,
        Func<AppAccessResult?> evaluate,
        Func<AppAccessResult> sessionStatus,
        Action<AppNotification> post,
        Action<string>? log = null)
    {
        _clock = clock;
        _evaluate = evaluate;
        _sessionStatus = sessionStatus;
        _post = post;
        _log = log;
        _lastNoticeDate = clock.Today;
    }

    /// <summary>
    /// Starts the hourly timer. Calling it again restarts it.
    /// </summary>
    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => CheckNow(), null, CheckInterval, CheckInterval);
    }

    /// <summary>
    /// Does the check once: nothing on the same date; on a new date, evaluates and posts the notice (if any).
    /// A timeout or exception is logged and the date is NOT advanced, so the next call retries. Never throws.
    /// </summary>
    internal void CheckNow()
    {
        // Ticks can't overlap: a slow share must not stack up evaluations.
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return;

        try
        {
            var today = _clock.Today;
            if (today == _lastNoticeDate)
                return;

            AppAccessResult? fresh;
            try
            {
                fresh = _evaluate();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Licensing] Daily check failed ({ex.GetType().Name}: {ex.Message}); will retry");
                return;
            }

            if (fresh is null)
            {
                _log?.Invoke("[Licensing] Daily check timed out; will retry");
                return;
            }

            var notice = LicenseNotices.ForDailyCheck(_sessionStatus(), fresh, today);
            if (notice is not null)
                _post(notice);

            _lastNoticeDate = today;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Licensing] Daily check error ({ex.GetType().Name}: {ex.Message})");
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    /// <summary>Stops the timer. Safe to call more than once.</summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
#endif

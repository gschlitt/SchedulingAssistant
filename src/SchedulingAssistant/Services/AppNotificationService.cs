using System.Reflection;

namespace SchedulingAssistant.Services;

/// <summary>
/// Singleton service that manages the application notification banner queue.
///
/// <para><b>Behaviour:</b> Notifications are shown one at a time. When multiple
/// notifications are queued, dismissing the current one reveals the next (FIFO order).
/// The banner is hidden when the queue is empty.</para>
///
/// <para><b>Sources of notifications:</b>
/// <list type="bullet">
///   <item>Versioned feature announcements from <see cref="AppAnnouncements"/>, shown
///   once per version after an upgrade (see <see cref="EnqueueUnseenAnnouncements"/>).</item>
///   <item>Logged errors forwarded automatically from <see cref="IAppLogger.ErrorLogged"/>.</item>
/// </list>
/// </para>
///
/// <para><b>Threading:</b> <see cref="Enqueue"/> and <see cref="Dismiss"/> are
/// thread-safe. <see cref="NotificationChanged"/> is always fired on the Avalonia
/// UI thread so subscribers may update observable properties directly.</para>
/// </summary>
public class AppNotificationService
{
    // Normal-priority items (errors, warnings, operational notices).
    private readonly Queue<AppNotification> _queue = new();

    // Low-priority items (versioned announcements). Drained only when _queue is empty.
    private readonly Queue<AppNotification> _lowPriorityQueue = new();

    private Timer? _autoDismissTimer;
    private readonly object _syncLock = new();

    /// <summary>The notification currently shown in the banner, or null when the banner is hidden.</summary>
    public AppNotification? Current { get; private set; }

    /// <summary>True when there is a notification currently being displayed.</summary>
    public bool HasNotification => Current is not null;

    /// <summary>
    /// Fired on the Avalonia UI thread whenever <see cref="Current"/> changes —
    /// either a new notification became current or the current one was dismissed.
    /// </summary>
    public event EventHandler? NotificationChanged;

    /// <summary>
    /// Constructs the service and subscribes to <paramref name="logger"/> so that
    /// any call to <see cref="IAppLogger.LogError"/> automatically surfaces as an
    /// error notification in the banner.
    /// </summary>
    /// <param name="logger">Application logger whose errors are forwarded to the banner.</param>
    public AppNotificationService(IAppLogger logger)
    {
        logger.ErrorLogged += OnLoggerError;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a notification to the queue.
    ///
    /// <para>Normal-priority notifications (<see cref="AppNotification.IsLowPriority"/> = false)
    /// are placed in the main queue and become current immediately if nothing else is showing.
    /// If a low-priority announcement is currently displayed and a normal notification arrives,
    /// the announcement is demoted back to the low-priority queue so the operational notice
    /// surfaces first.</para>
    ///
    /// <para>Low-priority notifications (<see cref="AppNotification.IsLowPriority"/> = true,
    /// used for versioned announcements) are only shown once the main queue is empty, ensuring
    /// they never delay error messages or backup warnings.</para>
    /// </summary>
    /// <param name="notification">The notification to queue.</param>
    public void Enqueue(AppNotification notification)
    {
        bool becameCurrent;
        lock (_syncLock)
        {
            if (notification.IsLowPriority)
            {
                // Low-priority: only surface immediately if nothing else is showing or queued.
                if (Current is null && _queue.Count == 0)
                {
                    Current       = notification;
                    becameCurrent = true;
                }
                else
                {
                    _lowPriorityQueue.Enqueue(notification);
                    becameCurrent = false;
                }
            }
            else
            {
                // Normal-priority: if a low-priority item is currently displayed, demote it
                // back to the FRONT of the low-priority queue so the user returns to it
                // after the operational notice is dismissed (not to the back, which would
                // bury it behind announcements that hadn't started showing yet).
                if (Current is { IsLowPriority: true })
                {
                    // Prepend by rebuilding: demoted item first, then existing items.
                    var demoted  = Current;
                    var existing = _lowPriorityQueue.ToArray();
                    _lowPriorityQueue.Clear();
                    _lowPriorityQueue.Enqueue(demoted);
                    foreach (var item in existing)
                        _lowPriorityQueue.Enqueue(item);

                    Current       = notification;
                    becameCurrent = true;
                }
                else if (Current is null)
                {
                    Current       = notification;
                    becameCurrent = true;
                }
                else
                {
                    _queue.Enqueue(notification);
                    becameCurrent = false;
                }
            }
        }

        if (becameCurrent)
        {
            FireNotificationChanged();
            if (notification.AutoDismissAfter is { } delay)
                ScheduleAutoDismiss(delay);
        }
    }

    /// <summary>
    /// Dismisses the current notification and advances to the next queued one (if any).
    /// Normal-priority items in the main queue are exhausted first; low-priority
    /// announcements are shown only once the main queue is empty.
    /// No-op when there is no current notification.
    /// </summary>
    public void Dismiss()
    {
        AppNotification? next;
        lock (_syncLock)
        {
            _autoDismissTimer?.Dispose();
            _autoDismissTimer = null;
            // Drain normal queue first; fall back to low-priority queue.
            next    = _queue.Count > 0          ? _queue.Dequeue()
                    : _lowPriorityQueue.Count > 0 ? _lowPriorityQueue.Dequeue()
                    : null;
            Current = next;
        }

        FireNotificationChanged();

        if (next?.AutoDismissAfter is { } delay)
            ScheduleAutoDismiss(delay);
    }

    /// <summary>
    /// Checks <see cref="AppAnnouncements.All"/> against
    /// <see cref="AppSettings.LastAcknowledgedVersion"/> and enqueues any announcements
    /// the current user has not yet seen. Announcements are enqueued oldest-first.
    ///
    /// <para>After enqueueing, <see cref="AppSettings.LastAcknowledgedVersion"/> is
    /// advanced to the running app version and saved, so the same announcements are not
    /// shown again on the next launch.</para>
    /// </summary>
    /// <param name="announcements">
    /// Announcement list to check. Pass a custom list in unit tests; pass null to use
    /// <see cref="AppAnnouncements.All"/> (the production default).
    /// </param>
    public void EnqueueUnseenAnnouncements(IReadOnlyList<VersionedAnnouncement>? announcements = null)
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var settings       = AppSettings.Current;
        var lastAck        = Version.TryParse(settings.LastAcknowledgedVersion, out var v) ? v : null;

        var unseen = GetUnseenAnnouncements(
            announcements ?? AppAnnouncements.All,
            lastAck,
            currentVersion);

        foreach (var announcement in unseen)
        {
            Enqueue(new AppNotification
            {
                Message       = announcement.Message,
                Severity      = NotificationSeverity.Info,
                IsDismissable = true,
                IsLowPriority = true,   // announcements never block operational notices
                LinkText      = announcement.LinkText,
                LinkUrl       = announcement.LinkUrl
            });
        }

        // Advance the acknowledged pointer even if there were no new announcements,
        // so that future launches on this version are skipped quickly.
        if (lastAck is null || currentVersion > lastAck)
        {
            settings.LastAcknowledgedVersion = currentVersion.ToString();
            settings.Save();
        }
    }

    // ── Testable core logic ───────────────────────────────────────────────────

    /// <summary>
    /// Returns announcements from <paramref name="all"/> that have not yet been seen
    /// by the user, sorted ascending by version (oldest first).
    ///
    /// <para>An announcement is "unseen" when its <c>SinceVersion</c>:
    /// <list type="bullet">
    ///   <item>is at or below <paramref name="currentApp"/> (guards against impossible future entries), AND</item>
    ///   <item>is strictly greater than <paramref name="lastAcknowledged"/>
    ///   (or <paramref name="lastAcknowledged"/> is null, meaning never acknowledged).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="all">The full list of versioned announcements to filter.</param>
    /// <param name="lastAcknowledged">Highest version the user has acknowledged, or null if never.</param>
    /// <param name="currentApp">The running application version.</param>
    /// <returns>Filtered and sorted list of unseen announcements.</returns>
    internal static IReadOnlyList<VersionedAnnouncement> GetUnseenAnnouncements(
        IEnumerable<VersionedAnnouncement> all,
        Version?                           lastAcknowledged,
        Version                            currentApp)
    {
        return all
            .Where(a => a.SinceVersion <= currentApp
                     && (lastAcknowledged is null || a.SinceVersion > lastAcknowledged))
            .OrderBy(a => a.SinceVersion)
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Starts (or restarts) a one-shot timer to auto-dismiss the current notification.</summary>
    private void ScheduleAutoDismiss(TimeSpan delay)
    {
        _autoDismissTimer?.Dispose();
        _autoDismissTimer = new Timer(
            _ => Dismiss(),
            null,
            (long)delay.TotalMilliseconds,
            Timeout.Infinite);
    }

    /// <summary>
    /// Fires <see cref="NotificationChanged"/> on the Avalonia UI thread so that
    /// bound ViewModels can safely update observable properties.
    /// </summary>
    private void FireNotificationChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => NotificationChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Handles <see cref="IAppLogger.ErrorLogged"/> — wraps the logged message in
    /// an error-severity notification so it surfaces in the banner as well as the log file.
    /// </summary>
    private void OnLoggerError(object? sender, string message)
    {
        Enqueue(new AppNotification
        {
            Message       = message,
            Severity      = NotificationSeverity.Error,
            IsDismissable = true
        });
    }
}

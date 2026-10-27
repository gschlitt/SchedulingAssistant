using Avalonia.Threading;
using TermPoint.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TermPoint.Services;

/// <summary>
/// Result of <see cref="WriteLockService.ForceAcquire"/>: what became of a user-confirmed
/// stale-lock takeover once the lock file was re-checked at click time (spec item 9a).
/// </summary>
public enum ForceAcquireResult
{
    /// <summary>The abandoned lock was removed (or already gone) and this instance now holds the write lock.</summary>
    Acquired,

    /// <summary>
    /// This instance did not get the lock: another instance created it between the delete and our own
    /// create (the lost-race case), or there was nothing to act on (no lock path, XAML designer).
    /// </summary>
    NotAcquired,

    /// <summary>
    /// The lock file is no longer the one the user was asked about — its heartbeat advanced (the holder
    /// woke and resumed), a different session took it, or it could not be read. Nothing was deleted and
    /// nothing was created; <see cref="WriteLockService.CurrentHolder"/> has been refreshed from the file.
    /// </summary>
    HolderActiveAgain
}

/// <summary>
/// Manages a file-based write lock that prevents two instances of the app from
/// writing to the same SQLite database simultaneously.
///
/// <para>A <c>.lock</c> file is placed alongside the <c>.db</c> file. Its JSON payload
/// records the username, machine name, and acquisition timestamp of the holder, plus a
/// heartbeat timestamp that is refreshed every <see cref="HeartbeatIntervalSeconds"/>
/// seconds. Any instance can inspect this file to determine whether the lock is live
/// or abandoned.</para>
///
/// <para><b>Writer path:</b> <see cref="TryAcquire"/> attempts <see cref="FileMode.CreateNew"/>
/// — an operation that is atomic at the OS/SMB-server level, ensuring exactly one
/// competing caller wins. The winner writes the JSON payload and starts the heartbeat
/// timer.</para>
///
/// <para><b>Reader path:</b> If <see cref="FileMode.CreateNew"/> fails (file exists), the
/// existing file is read. If it looks abandoned (<see cref="IsStaleLock"/>; see below) the caller
/// asks the user and, on a yes, waits out <see cref="StaleConfirmationRemaining"/> and calls
/// <see cref="ForceAcquire"/>, which re-reads the file and deletes it only if it is still the same
/// abandoned lock (spec item 9a). A
/// live-looking lock means this instance enters read-only mode and starts a polling timer that
/// fires every <see cref="PollIntervalSeconds"/> seconds. When the poll detects the lock is
/// gone or stale, it raises <see cref="LockStateChanged"/> on the UI thread so the banner
/// can offer the user a prompt to switch to edit mode.</para>
///
/// <para><b>What "abandoned" means (spec item 9b).</b> The reader never trusts the writer's clock.
/// A lock is abandoned when its heartbeat (its <see cref="LockFileData.SessionGuid"/> and
/// <see cref="LockFileData.Heartbeat"/>) has been seen <b>unchanged for
/// <see cref="StaleLockThresholdSeconds"/> of this computer's own time</b> (<see cref="LockHeartbeatWatch"/>).
/// The polling path judges by that alone. The open path also accepts the timestamp inside the file
/// being older than the threshold by this computer's clock, but only as a <i>hint</i> so an obviously
/// dead lock is offered at once; the takeover itself still needs the heartbeat to have stood still
/// for <see cref="StaleConfirmationSeconds"/> of local time. So a writer whose clock is minutes behind
/// but which keeps heart-beating is never taken from, and a dead writer whose clock is ahead is still
/// found out by the polling path.</para>
///
/// <para><b>Thread safety:</b> The heartbeat timer callback runs on a thread-pool thread
/// and accesses the lock file without synchronization (it is the only writer while it
/// holds the lock). The poll timer callback is marshalled to the Avalonia UI thread via
/// <c>Dispatcher.UIThread.Post</c> before touching any observable state or raising
/// events, so subscribers do not need to dispatch.</para>
///
/// <para><b>Emergency override:</b> A user can always reclaim write access by manually
/// deleting the <c>.lock</c> file in File Explorer. The next poll (within
/// <see cref="PollIntervalSeconds"/> seconds) will detect the absent file and offer the
/// "Switch to edit mode" prompt, or the user can restart the app to acquire the lock
/// immediately on startup.</para>
/// </summary>
public sealed class WriteLockService : IDisposable
{
    // ── Constants ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Interval in seconds at which the writer renews its heartbeat timestamp.
    /// Kept short enough that readers detect a crash within a few minutes,
    /// but long enough to avoid excessive file I/O on network shares.
    /// </summary>
    public const int HeartbeatIntervalSeconds = 60;

    /// <summary>
    /// Interval in seconds at which read-only instances poll the lock file to
    /// detect that the writer has released or crashed. Matched to the heartbeat
    /// interval so readers respond within approximately one full cycle.
    /// </summary>
    public const int PollIntervalSeconds = 60;

    /// <summary>
    /// A lock whose heartbeat has not CHANGED for this many seconds, measured on this computer's own
    /// clock (<see cref="LockHeartbeatWatch"/>), is considered stale (writer crashed, was killed, or is
    /// asleep). Set to 3× the heartbeat interval so a single missed beat does not trigger a false
    /// reclaim. The reader polling path judges by this alone. The open path additionally uses it as a
    /// hint against the timestamp inside the file (see <see cref="DetectStaleLock"/>), because a lock
    /// that already looks long dead by the clock is worth offering at once rather than after a wait.
    /// </summary>
    public const int StaleLockThresholdSeconds = 180;

    /// <summary>
    /// How long, in seconds of this computer's own time, a lock's heartbeat must be seen UNCHANGED
    /// before a user-confirmed takeover may go ahead (spec item 9b). A live writer rewrites its
    /// heartbeat every <see cref="HeartbeatIntervalSeconds"/> seconds, so seeing it stand still for one
    /// full interval plus a margin of 10 s proves the writer is not renewing, independent of either
    /// computer's clock. Shorter than <see cref="StaleLockThresholdSeconds"/> on purpose: on the open
    /// path the clock comparison has already said "long dead", and this is only the confirmation that the
    /// clock was not misleading, so the extra wait stays short.
    /// </summary>
    public const int StaleConfirmationSeconds = HeartbeatIntervalSeconds + 10;

    /// <summary>
    /// Number of consecutive heartbeat renewal failures before
    /// <see cref="HeartbeatFailed"/> is raised. Set to 3 (matching
    /// <c>StaleLockThresholdSeconds / HeartbeatIntervalSeconds</c>) so the event
    /// fires at roughly the same moment an external reader would classify the lock as
    /// stale and attempt a takeover. (F11, data-integrity-agenda 2026-05-04.)
    /// </summary>
    public const int HeartbeatFailureThreshold = 3;

    // ── State ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes all mutations of the lock file (heartbeat renewal vs. release/delete).
    /// <see cref="Timer.Dispose()"/> does not wait for an in-flight heartbeat callback, so
    /// without this gate a <see cref="RenewHeartbeatAsync"/> already running when
    /// <see cref="Release"/> deletes the file could re-create it via <c>File.Move</c>,
    /// orphaning the lock. The gate also serializes the wake-driven
    /// <see cref="ForceRenewHeartbeat"/> against the scheduled heartbeat.
    /// <para>A <see cref="SemaphoreSlim"/> rather than a monitor so waiters are async and
    /// never block a thread on the share: the lock file lives on the network, and every
    /// file operation under this gate is deadline-bounded via <see cref="NetworkFileOps"/>
    /// (lockup audit P0-1 — a stalled heartbeat holding a monitor across raw SMB I/O
    /// hard-locked any UI-thread <see cref="Release"/> indefinitely).</para>
    /// </summary>
    private readonly SemaphoreSlim _lockFileGate = new(1, 1);

    /// <summary>
    /// The in-flight background lock-file deletion queued by the most recent
    /// <see cref="Release"/>, or null when none is pending. Non-faulting by construction.
    /// <see cref="TryAcquire"/> waits on it (bounded) before creating a new lock file so a
    /// same-path re-acquire cannot have its fresh lock deleted by the earlier release;
    /// <see cref="Dispose"/> waits on it (bounded) so a clean exit still deletes the file.
    /// </summary>
    private Task? _pendingReleaseTask;

    /// <summary>
    /// Exposes the pending release-deletion task so teardown code (e.g.
    /// <see cref="CheckoutService"/>) can await it with a cap of its choosing.
    /// Null when no release is in flight.
    /// </summary>
    internal Task? PendingRelease => _pendingReleaseTask;

    /// <summary>
    /// True while the read-only poll timer is running (this instance is watching the lock file for
    /// the holder to release or go stale). Exposed so tests can assert which read-only sessions poll;
    /// not used by production code.
    /// </summary>
    internal bool IsPolling => _pollRunning;

    private string? _lockFilePath;
    private Timer? _heartbeatTimer;
    /// <summary>
    /// Running count of consecutive heartbeat renewal failures. Reset to zero on
    /// any successful renewal. When it reaches <see cref="HeartbeatFailureThreshold"/>,
    /// <see cref="HeartbeatFailed"/> is raised and the counter is reset so the event
    /// fires at most once per threshold-window.
    /// </summary>
    private int _heartbeatConsecutiveFailures;
    private Timer? _pollTimer;
    private Timer? _wakeDetectionTimer;
    private DateTime _lastWakeTick;
    private bool _disposed;

    /// <summary>
    /// True while the poll is "on": set where the poll timer is created, cleared where it is disposed. In the
    /// app it always equals <c>_pollTimer is not null</c>; it is a separate flag so <see cref="IsPolling"/> keeps
    /// its meaning when <c>startTimers</c> is false and no <see cref="Timer"/> object exists (test-only mode).
    /// </summary>
    private bool _pollRunning;

    /// <summary>
    /// False only in tests that drive heartbeat, poll and wake checks by hand
    /// (<see cref="TickHeartbeatAsync"/>, <see cref="TickPollAsync"/>, <see cref="TickWakeCheck"/>): the
    /// <c>Start*</c> methods then create no <see cref="Timer"/>. Always true in the app.
    /// </summary>
    private readonly bool _startTimers;

    /// <summary>
    /// Who this service says it is in the lock file and who it treats as "this machine" when judging a holder.
    /// <see cref="LockIdentity.Current"/> in the app; a per-simulated-computer value in tests.
    /// </summary>
    private readonly LockIdentity _identity;

    /// <summary>
    /// The time source for everything this service MEASURES on the reader side: how long a lock's
    /// heartbeat has been unchanged (<see cref="_heartbeatWatch"/>), and "now" for the open path's
    /// clock hint, and for everything it STAMPS on the writer side (lock-file times, heartbeat renewal, wake
    /// detection). <see cref="TimeProvider.System"/> in production; a controllable fake in tests.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// Times how long the lock holder's snapshot (session + heartbeat) has stood unchanged, on this
    /// computer's own monotonic clock. Shared by the open path (<see cref="DetectStaleLock"/>), the
    /// polling path (<see cref="PollLockFile"/>) and the takeover confirmation
    /// (<see cref="StaleConfirmationRemaining"/>), and deliberately not cleared by
    /// <see cref="Release"/> or <see cref="TryAcquire"/>, so a measurement made by one carries to the next.
    /// </summary>
    private readonly LockHeartbeatWatch _heartbeatWatch;

    /// <summary>
    /// Creates a lock service.
    /// </summary>
    /// <param name="timeProvider">
    /// Time source for measuring how long a lock's heartbeat has been unchanged. Defaults to
    /// <see cref="TimeProvider.System"/>; tests pass a fake so minutes of waiting take no real time.
    /// </param>
    public WriteLockService(TimeProvider? timeProvider = null)
        : this(timeProvider, null, startTimers: true) { }

    /// <summary>
    /// Test seam: creates a lock service with its own identity and, optionally, no background timers. The app
    /// never calls this; the public constructor above is exactly this one with the defaults. (Separate from
    /// the public constructor only because <see cref="LockIdentity"/> is internal.)
    /// </summary>
    /// <param name="timeProvider">
    /// Time source for every time this service reads or stamps. <see cref="TimeProvider.System"/> when null.
    /// </param>
    /// <param name="identity">
    /// Who this service is in the lock file (machine, user, process id, start time) and how it looks up other
    /// processes on its machine. <see cref="LockIdentity.Current"/> (the real process) when null.
    /// </param>
    /// <param name="startTimers">
    /// True (the default) starts the real heartbeat, poll and wake-detection timers. False creates none; the test
    /// then drives one tick at a time with <see cref="TickHeartbeatAsync"/>, <see cref="TickPollAsync"/> and
    /// <see cref="TickWakeCheck"/>. Everything else about starting and stopping is unchanged.
    /// </param>
    internal WriteLockService(TimeProvider? timeProvider, LockIdentity? identity = null, bool startTimers = true)
    {
        _time           = timeProvider ?? TimeProvider.System;
        _identity       = identity ?? LockIdentity.Current;
        _startTimers    = startTimers;
        _heartbeatWatch = new LockHeartbeatWatch(_time);
        _lastWakeTick   = _time.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// The time source this service measures with. Exposed so the takeover wait
    /// (<see cref="CheckoutService.WaitForTakeOverConfirmationAsync"/>) sleeps on the same clock the
    /// confirmation is measured on.
    /// </summary>
    internal TimeProvider Time => _time;

    /// <summary>
    /// A stable GUID generated once per app session. Written to the lock file so that
    /// wake-checks and crash recovery can distinguish this exact session from another
    /// by the same user on the same machine.
    /// </summary>
    public string SessionGuid { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// True when this instance currently holds the write lock.
    /// False in read-only mode or before <see cref="TryAcquire"/> is called.
    /// </summary>
    public bool IsWriter { get; private set; }

    /// <summary>
    /// Information about the process that currently holds the lock. Populated
    /// when this instance is in read-only mode; null when this instance is the
    /// writer or when <see cref="TryAcquire"/> has not yet been called.
    /// </summary>
    public LockFileData? CurrentHolder { get; private set; }

    /// <summary>
    /// True when a read-only instance has detected that the write lock is no
    /// longer held (lock file gone or heartbeat stale). The UI should offer the
    /// user a prompt to switch to edit mode. Automatically reset to false when
    /// <see cref="TryAcquire"/> is called again (e.g., on DB switch).
    /// </summary>
    public bool WriteLockBecameAvailable { get; private set; }

    /// <summary>
    /// True when <see cref="TryAcquire"/> found an existing lock whose heartbeat was
    /// stale, but did not auto-reclaim it. The caller should prompt the user for
    /// confirmation, then call <see cref="ForceAcquire"/> (passing <see cref="CurrentHolder"/>
    /// as the snapshot that was judged stale) if they agree.
    /// Reset to false at the start of each <see cref="TryAcquire"/> call.
    /// </summary>
    public bool IsStaleLock { get; private set; }

    /// <summary>
    /// True when the most recent <see cref="TryAcquire"/> took over the lock from a holder
    /// whose process was <b>provably dead</b> (same machine, recorded PID gone or reused).
    /// Lets the startup path surface a "recovered from a previous session" notice.
    /// Reset to false at the start of each <see cref="TryAcquire"/> call.
    /// </summary>
    public bool ReclaimedDeadSession { get; private set; }

    /// <summary>
    /// True when <see cref="TryAcquire"/> went read-only because the lock is held by a
    /// <b>still-running process on this same machine</b> (a second live instance). Lets the
    /// read-only banner explain the situation precisely instead of the generic message.
    /// Reset to false at the start of each <see cref="TryAcquire"/> call.
    /// </summary>
    public bool HolderIsLiveSameMachine { get; private set; }

    /// <summary>
    /// Non-null when the most recent <see cref="TryAcquire"/> could not even <b>create</b> the
    /// lock file — as opposed to losing it to an existing holder. Set when <c>CreateNew</c> fails
    /// with a write/permission error (Windows Controlled Folder Access, an NTFS ACL denial, a
    /// disk-full condition, or a sharing violation) <i>and</i> no lock file is actually present.
    /// <para>This is a fundamentally different situation from contention: there is no other
    /// instance to wait for. Callers should surface this message to the user instead of the
    /// generic "locked by another instance" wording, and should not poll for the lock to free up.
    /// Reset to null at the start of every <see cref="TryAcquire"/>.</para>
    /// </summary>
    public string? LockWriteError { get; private set; }

    /// <summary>
    /// Optional technical detail aimed at IT support when <see cref="LockWriteError"/> is set.
    /// Names Controlled Folder Access and the allow-an-app remedy so the user can forward it
    /// to their IT department. Null when the failure is not CFA-related (e.g. disk full) or
    /// when no lock-write failure occurred.
    /// </summary>
    public string? LockWriteItDetail { get; private set; }

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when lock state changes — specifically when a
    /// read-only instance's poll detects the lock is gone or stale. Subscribers
    /// should re-read <see cref="IsWriter"/>, <see cref="CurrentHolder"/>, and
    /// <see cref="WriteLockBecameAvailable"/> to update their state.
    /// </summary>
    public event Action? LockStateChanged;

    /// <summary>
    /// Raised on the UI thread when the gap-detection timer observes that the
    /// machine likely resumed from sleep (elapsed time between ticks exceeded the
    /// expected interval by a significant margin).
    /// </summary>
    public event Action? WakeDetected;

    /// <summary>
    /// Raised on the heartbeat timer thread (NOT the UI thread) after
    /// <see cref="HeartbeatFailureThreshold"/> consecutive heartbeat renewal failures.
    /// Signals that the lock file is likely stale from the perspective of other
    /// readers, who may attempt a takeover. Subscribers should route this through
    /// the same lock-loss handling path used for save-time and wake-check failures.
    /// (F11, data-integrity-agenda 2026-05-04.)
    ///
    /// <para>Fired at most once per threshold-window: the failure counter is reset to
    /// zero after the event, so a further uninterrupted run of failures triggers the
    /// event again after another <see cref="HeartbeatFailureThreshold"/> beats.</para>
    ///
    /// <para>Not fired on the UI thread — handlers must dispatch to the UI thread
    /// themselves if they need to update observable state.</para>
    /// </summary>
    public event Action? HeartbeatFailed;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Grants write access without acquiring a file lock. Used in the browser (WASM)
    /// demo build, where there is no file system and no competing processes.
    /// </summary>
    public void AcquireDemo()
    {
        IsWriter = true;
    }

    /// <summary>
    /// Attempts to acquire the write lock for the given database file. If this
    /// instance already holds a lock (e.g., the user is switching databases),
    /// the previous lock is released first.
    /// </summary>
    /// <param name="dbPath">
    /// Absolute path to the SQLite <c>.db</c> file. The lock file is placed in
    /// the same directory with the same base name and a <c>.lock</c> extension.
    /// </param>
    public void TryAcquire(string dbPath)
    {
        // Never touch the real lock file inside the Avalonia XAML previewer. The
        // Designer HostApp loads this assembly in a separate VS-owned process; if it
        // reaches this method it would create and heartbeat a .lock file against the
        // user's real database. Bail out doing no file I/O. (Belt-and-suspenders — the
        // primary guard is in MainWindow.OnOpened.)
        if (Avalonia.Controls.Design.IsDesignMode) return;

        // Release any lock we currently hold (handles DB-switching).
        Release();

        // Wait (bounded) for the release's queued file deletion before creating a new
        // lock file: on a same-path re-acquire, the pending delete would otherwise race
        // the fresh file into oblivion (both carry this session's GUID, so the delete's
        // ownership check cannot tell them apart). TryAcquire always runs on a
        // thread-pool worker under a NetworkFileOps.RunAsync deadline, so this wait
        // never blocks the UI thread. DeleteLockFileGatedAsync is non-faulting, so
        // Wait() here cannot throw.
        var pending = _pendingReleaseTask;
        bool releaseSettled = pending is null || pending.IsCompleted
                              || pending.Wait(TimeSpan.FromSeconds(12));

        WriteLockBecameAvailable = false;
        IsStaleLock = false;
        ReclaimedDeadSession = false;
        HolderIsLiveSameMachine = false;
        LockWriteError = null;
        LockWriteItDetail = null;
        _lockFilePath = Path.ChangeExtension(dbPath, ".lock");

        if (!releaseSettled)
        {
            // The share is not responding; acquiring now would be unsafe and would fail
            // anyway. Stay read-only with an explanatory error rather than polling.
            LockWriteError = "The previous session's lock is still being released — the network location is not responding.";
            IsWriter = false;
            App.Logger.LogInfo("[WriteLockService] TryAcquire aborted — pending lock release did not settle in time.");
            Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
            return;
        }

        if (TryCreateLockFile())
        {
            IsWriter = true;
            StartHeartbeat();
            StartWakeDetection();
            App.Logger.LogInfo($"[WriteLockService] Acquired write lock: {_lockFilePath}");
        }
        else if (IsStaleLock)
        {
            IsWriter = false;
            App.Logger.LogInfo($"[WriteLockService] Stale lock detected for {CurrentHolder?.Username}@{CurrentHolder?.Machine} — awaiting user confirmation to take over.");
        }
        else if (LockWriteError is not null)
        {
            // We could not even create the lock file (a blocked write — Controlled Folder Access,
            // an ACL denial, disk full, etc.). There is no holder to wait for, so polling for the
            // lock to free up is pointless and would only spawn a misleading "switch to edit"
            // prompt that is guaranteed to fail again. Stay read-only; the banner explains why.
            IsWriter = false;
            App.Logger.LogInfo($"[WriteLockService] Read-only mode — lock-file write blocked: {LockWriteError}");
        }
        else
        {
            IsWriter = false;
            StartPolling();
            App.Logger.LogInfo($"[WriteLockService] Read-only mode; lock held by {CurrentHolder?.Username}@{CurrentHolder?.Machine}");
        }

        // Notify all subscribers (MainWindowViewModel, SectionListViewModel, etc.) that
        // lock state has changed, so they can re-evaluate IsWriter and update their UI.
        Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
    }

    /// <summary>
    /// Releases the write lock if this instance holds it, stopping the heartbeat
    /// timer and deleting the lock file. Also stops the poll timer if this instance
    /// is in read-only mode. Safe to call multiple times or when in read-only mode
    /// — both are no-ops for the lock file deletion step.
    ///
    /// <para><b>Ownership re-check:</b> Before deleting the lock file, this method
    /// re-reads it and verifies the stored <c>SessionGuid</c> still matches our own.
    /// If the file is gone, corrupted, or owned by a different session, the deletion
    /// is skipped — we must never trample a rightful holder's claim even though we
    /// locally believe <see cref="IsWriter"/> is true. (This can happen after another
    /// user has modified the lock file out from under us; our <see cref="IsWriter"/>
    /// flag won't have been reset because we only learn about the lock theft when
    /// <see cref="CheckoutService.SaveAsync"/> does its own verification.)</para>
    /// </summary>
    public void Release()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _pollTimer?.Dispose();
        _pollTimer = null;
        _pollRunning = false;
        _wakeDetectionTimer?.Dispose();
        _wakeDetectionTimer = null;

        bool wasWriter = IsWriter;
        var path = _lockFilePath;

        // Flip local state immediately. Release() is called on the UI thread from the
        // CheckoutService flows, so it must never touch the network share itself — the
        // lock file lives on the share and a raw delete here froze the whole window when
        // the share was unreachable (lockup audit P0-1). Bool/reference writes are atomic
        // and the heartbeat re-checks IsWriter *inside* the gate before touching the file.
        IsWriter = false;
        CurrentHolder = null;
        _lockFilePath = null;

        if (wasWriter && path is not null)
        {
            // Delete the lock file on a background task, serialized against any in-flight
            // heartbeat by _lockFileGate and deadline-bounded by NetworkFileOps. Orderings
            // match the old mutex guarantee: a mid-flight heartbeat either bails (IsWriter
            // is now false) or finishes its File.Move first and this task then deletes the
            // file it just rewrote — either way the file ends deleted, never orphaned.
            _pendingReleaseTask = Task.Run(() => DeleteLockFileGatedAsync(path));
        }

        // Notify subscribers (MainWindowViewModel, SubjectListViewModel, etc.) so any
        // UI enablement bound to IsWriter re-evaluates. Without this, a demotion from
        // write → read would leave write-gated controls (Add Section, Delete, etc.)
        // enabled until the next time TryAcquire/ForceAcquire happens to fire the event.
        if (wasWriter)
            Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
    }

    /// <summary>
    /// Background completion of <see cref="Release"/>: verifies the lock file is still
    /// ours, then deletes it. All share I/O is deadline-bounded; on any timeout the file
    /// is abandoned in place — with the heartbeat stopped it goes stale within
    /// <see cref="StaleLockThresholdSeconds"/> and the standard stale-lock takeover
    /// reclaims it, so abandonment inconveniences but never blocks another user.
    /// Non-faulting by construction: callers may Wait() on it without exception handling.
    /// </summary>
    /// <param name="path">The lock file path captured before state was cleared.</param>
    private async Task DeleteLockFileGatedAsync(string path)
    {
#if BROWSER
        // The browser demo never acquires a real lock file — nothing to delete.
        // (NetworkFileOps is excluded from the browser target.)
        await Task.CompletedTask;
#else
        await _lockFileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Ownership re-check (see Release() doc): never trample another session's
            // claim even though we locally believed we were the writer.
            var (readCompleted, json) = await NetworkFileOps.ReadAllTextAsync(path).ConfigureAwait(false);
            if (!readCompleted)
            {
                App.Logger.LogInfo($"[WriteLockService] Release: lock-file read timed out — abandoning {path} (stale-lock takeover will reclaim it).");
                return;
            }

            var data = ParseLockFile(json);
            if (data?.SessionGuid != SessionGuid)
            {
                App.Logger.LogInfo($"[WriteLockService] Release: lock file at {path} is no longer ours; skipping delete.");
                return;
            }

            if (await NetworkFileOps.DeleteAsync(path).ConfigureAwait(false))
            {
                App.Logger.LogInfo($"[WriteLockService] Released write lock: {path}");
                CrashPoints.Hit("Lock.AfterDeleted");
            }
            else
                App.Logger.LogInfo($"[WriteLockService] Release: lock-file delete timed out — abandoning {path} (stale-lock takeover will reclaim it).");
        }
        catch (Exception ex)
        {
            // Missing file (already released/stolen), JSON garbage, or an I/O error the
            // deadline wrapper propagated — all leave the file in a state stale-lock
            // handling covers. Log and move on; this task must never fault.
            App.Logger.LogInfo($"[WriteLockService] Warning — could not delete lock file on release: {ex.Message}");
        }
        finally
        {
            _lockFileGate.Release();
        }
#endif
    }

    /// <summary>
    /// Stops all timers WITHOUT deleting the lock file, abandoning the lock in place.
    /// Used on shutdown when the final save failed transiently (network unreachable):
    /// the lock file is deliberately left behind so that no other instance acquires
    /// write access and modifies D before this user's unsaved changes are restored
    /// at their next launch. With the heartbeat stopped, the abandoned lock goes
    /// stale after <see cref="StaleLockThresholdSeconds"/> and remains claimable by
    /// anyone through the standard stale-lock takeover prompt — so another user is
    /// inconvenienced, informed (the prompt names this user), but never blocked.
    ///
    /// <para>After this call <see cref="IsWriter"/> is false, so a subsequent
    /// <see cref="Release"/> or <see cref="Dispose"/> will NOT delete the abandoned
    /// lock file — the file must survive process exit.</para>
    /// </summary>
    public void Suspend()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _pollTimer?.Dispose();
        _pollTimer = null;
        _pollRunning = false;
        _wakeDetectionTimer?.Dispose();
        _wakeDetectionTimer = null;

        // No gate needed: Suspend performs no file I/O, and an in-flight heartbeat either
        // bails on seeing IsWriter=false or finishes rewriting the file first — both
        // orderings leave the lock file present, which is exactly what Suspend requires.
        if (IsWriter)
            App.Logger.LogInfo($"[WriteLockService] Suspended (lock file left in place): {_lockFilePath}");
        IsWriter      = false;
        CurrentHolder = null;
        _lockFilePath = null;
    }

    /// <summary>
    /// Releases the write lock (calls <see cref="Release"/>) and frees resources.
    /// Safe to call more than once; subsequent calls are no-ops.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();

        // Give the queued lock-file deletion a bounded window to finish so a clean exit
        // on a healthy share still removes the file (milliseconds in practice). On a dead
        // share the wait caps out and the file is abandoned — stale-lock takeover covers
        // it — instead of the old behavior of blocking exit for the SMB redirector timeout.
        try { _pendingReleaseTask?.Wait(TimeSpan.FromSeconds(6)); }
        catch { /* non-faulting by construction; belt-and-suspenders */ }
    }

    // ── Lock file helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Breaks a stale lock and acquires write access — but only if the lock file is still the
    /// <b>same</b> abandoned lock the user was asked about. Call only after the user has confirmed
    /// they wish to take over a stale session (i.e., <see cref="IsStaleLock"/> is true following a
    /// <see cref="TryAcquire"/> call), passing the holder snapshot that <see cref="TryAcquire"/>
    /// judged stale.
    ///
    /// <para><b>Click-time re-check (spec item 9a).</b> The takeover prompt can sit open for minutes.
    /// A laptop that was asleep may wake and resume its heartbeats in the meantime, or another
    /// machine may take the lock over. So immediately before deleting anything the lock file is
    /// re-read (<see cref="RecheckStaleHolder"/>) and compared with <paramref name="expectedHolder"/>:</para>
    /// <list type="bullet">
    ///   <item><b>Missing</b> — nobody holds the lock any more; just create ours (the atomic
    ///         <see cref="FileMode.CreateNew"/> arbitrates against anyone else doing the same).</item>
    ///   <item><b>Same <see cref="LockFileData.SessionGuid"/> and same <see cref="LockFileData.Heartbeat"/></b> —
    ///         the lock is exactly as judged abandoned: delete it and create ours.</item>
    ///   <item><b>Anything else</b> (heartbeat advanced, a different session, unreadable) — NOTHING is
    ///         deleted and nothing is created. <see cref="CurrentHolder"/> is refreshed from the file and
    ///         <see cref="ForceAcquireResult.HolderActiveAgain"/> is returned.</item>
    /// </list>
    ///
    /// <para><b>Residual race:</b> the re-read and the delete are two separate share operations (a few
    /// milliseconds apart; there is no atomic compare-and-delete on a file share). A holder that renews its
    /// heartbeat, or a machine that takes the lock, inside that gap would still have its lock deleted.
    /// That gap is milliseconds, where the window before this check was the whole time the prompt was
    /// open. The holder would then meet the ordinary lock-loss path (wake check, heartbeat verification
    /// or its next save finds the lock no longer ours and demotes the session, keeping its unsaved
    /// changes for recovery), exactly as for any takeover.</para>
    ///
    /// <para>Runs synchronously and is meant to be called under a <see cref="NetworkFileOps"/> deadline
    /// (<see cref="CheckoutService.ForceCheckoutAsync"/> does), like <see cref="TryAcquire"/>.</para>
    /// </summary>
    /// <param name="expectedHolder">
    /// The holder snapshot the stale judgement was made on (<see cref="CurrentHolder"/> as
    /// <see cref="TryAcquire"/> left it). Only its <see cref="LockFileData.SessionGuid"/> and
    /// <see cref="LockFileData.Heartbeat"/> are compared.
    /// </param>
    /// <returns>
    /// <see cref="ForceAcquireResult.Acquired"/> when this instance now holds the lock;
    /// <see cref="ForceAcquireResult.HolderActiveAgain"/> when the lock changed since it was judged
    /// stale (and was left alone); <see cref="ForceAcquireResult.NotAcquired"/> when another instance
    /// won the create race (polling is started) or there was nothing to act on.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expectedHolder"/> is null.</exception>
    public ForceAcquireResult ForceAcquire(LockFileData expectedHolder)
    {
        ArgumentNullException.ThrowIfNull(expectedHolder);

        // See TryAcquire — never break/create a lock file in the XAML previewer.
        if (Avalonia.Controls.Design.IsDesignMode) return ForceAcquireResult.NotAcquired;

        if (_lockFilePath is null) return ForceAcquireResult.NotAcquired;

        // Re-check immediately before the delete — keep nothing else between the two.
        var recheck = RecheckStaleHolder(expectedHolder, out var currentHolder);

        if (recheck == StaleRecheck.Changed)
        {
            // The holder is (or someone else now is) alive. Take nothing; show who it is.
            IsStaleLock   = false;
            CurrentHolder = currentHolder;

            // We have just seen this snapshot, so start timing it from now (a no-op if the polling
            // path was already timing exactly this one). The read-only polling that follows then
            // judges it by how long it stands still, not by the writer's clock.
            if (currentHolder is not null)
                _heartbeatWatch.Observe(_lockFilePath, currentHolder);

            App.Logger.LogInfo(
                $"[WriteLockService] ForceAcquire: lock changed since it was judged stale — leaving it alone " +
                $"(now {currentHolder?.Username ?? "unreadable"}@{currentHolder?.Machine ?? "?"}).");
            Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
            return ForceAcquireResult.HolderActiveAgain;
        }

        // Unchanged (still the abandoned lock): break it. Missing: nothing to delete.
        if (recheck == StaleRecheck.Unchanged)
        {
            try { File.Delete(_lockFilePath); } catch (Exception ex)
            {
                App.Logger.LogInfo($"[WriteLockService] ForceAcquire: could not delete stale lock: {ex.Message}");
            }
        }

        // The abandoned lock is gone (deleted now, or already missing): forget it, so whatever appears
        // at this path next is timed from scratch.
        _heartbeatWatch.Clear();

        IsStaleLock = false;
        CurrentHolder = null;

        ForceAcquireResult result;
        if (TryCreateOnce())
        {
            IsWriter = true;
            StartHeartbeat();
            StartWakeDetection();
            App.Logger.LogInfo($"[WriteLockService] ForceAcquire: lock acquired after breaking stale lock.");
            result = ForceAcquireResult.Acquired;
        }
        else
        {
            // Another instance beat us to it.
            IsWriter = false;
            StartPolling();
            App.Logger.LogInfo($"[WriteLockService] ForceAcquire: lost race — another instance acquired the lock.");
            result = ForceAcquireResult.NotAcquired;
        }

        Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
        return result;
    }

    /// <summary>
    /// How much longer a user-confirmed takeover of <paramref name="staleHolder"/> must wait before
    /// <see cref="ForceAcquire"/> may be called (spec item 9b). The takeover needs the holder's heartbeat
    /// to have been seen unchanged for <see cref="StaleConfirmationSeconds"/> of this computer's own time
    /// (<see cref="LockHeartbeatWatch"/>), counted from the first time this process saw that very
    /// snapshot, which is when <see cref="TryAcquire"/> read it (or earlier, if the polling path had
    /// already been watching it). It is usually already satisfied by the time the user has read the
    /// prompt; when it is not, the caller waits this long (cancellably), then calls
    /// <see cref="ForceAcquire"/>, whose click-time re-check returns
    /// <see cref="ForceAcquireResult.HolderActiveAgain"/> if the heartbeat moved in the meantime.
    /// Reads no file and does no I/O.
    /// </summary>
    /// <param name="staleHolder">
    /// The snapshot the stale judgement was made on (<see cref="CurrentHolder"/> as
    /// <see cref="TryAcquire"/> left it). A snapshot this service is not timing (never observed, or
    /// since replaced by a different one) has the full wait ahead of it.
    /// </param>
    /// <returns>The time still to wait; <see cref="TimeSpan.Zero"/> when the takeover may proceed now.</returns>
    public TimeSpan StaleConfirmationRemaining(LockFileData staleHolder)
    {
        ArgumentNullException.ThrowIfNull(staleHolder);

        var unchanged = _lockFilePath is null
            ? TimeSpan.Zero
            : _heartbeatWatch.UnchangedFor(_lockFilePath, staleHolder);
        var remaining = TimeSpan.FromSeconds(StaleConfirmationSeconds) - unchanged;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Forces an immediate heartbeat renewal. Used by <see cref="CheckoutService"/>
    /// after wake-from-sleep detection to refresh the lock file without waiting for
    /// the next scheduled heartbeat tick. Returns the renewal task so callers (and
    /// tests) can await completion; the renewal is non-throwing by construction.
    /// </summary>
    internal Task ForceRenewHeartbeat() => RenewHeartbeatAsync();

    /// <summary>
    /// Enters reader mode for the given database path WITHOUT attempting to acquire
    /// the lock. Used by <see cref="CheckoutService.DemoteToReadOnlyAsync"/> so a
    /// session that has just lost write access still gets the reader polling timer
    /// — that way, if the current holder later releases cleanly, this session is
    /// offered the "switch to edit mode" prompt via <see cref="LockStateChanged"/>.
    ///
    /// <para>Safe to call while <see cref="IsWriter"/> is false. Populates
    /// <see cref="CurrentHolder"/> from the current contents of the lock file (may
    /// be null if the file is gone), starts the poll timer, and fires
    /// <see cref="LockStateChanged"/> on the UI thread.</para>
    /// </summary>
    /// <param name="dbPath">Absolute path to the source database (D), not the working copy.</param>
    public void EnterReaderMode(string dbPath)
    {
        // Shouldn't happen — caller (CheckoutService) always Releases first — but guard.
        if (IsWriter)
        {
            App.Logger.LogInfo("[WriteLockService] EnterReaderMode called while IsWriter=true; ignoring.");
            return;
        }

        _pollTimer?.Dispose();
        _pollTimer = null;
        _pollRunning = false;

        _lockFilePath = Path.ChangeExtension(dbPath, ".lock");
        WriteLockBecameAvailable = false;
        IsStaleLock = false;

        // Populate CurrentHolder from whatever's in the lock file right now. If the
        // file is absent, CurrentHolder ends up null and the very first poll tick
        // will classify the lock as available and raise LockStateChanged again.
        if (File.Exists(_lockFilePath))
        {
            ReadCurrentHolder();

            // Start (or, when this is the very snapshot the stale prompt was about, continue) timing how
            // long it stands still: the polling that starts below counts from the first time THIS
            // process saw it, not from its own first tick.
            if (CurrentHolder is not null)
                _heartbeatWatch.Observe(_lockFilePath, CurrentHolder);
        }
        else
        {
            CurrentHolder = null;
        }

        StartPolling();
        App.Logger.LogInfo($"[WriteLockService] Entered reader mode for {_lockFilePath}; polling started.");

        Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
    }

    /// <summary>
    /// Top-level lock acquisition attempt.
    /// <list type="number">
    ///   <item>Try to create the file atomically (<see cref="TryCreateOnce"/>). Success ⇒ writer.</item>
    ///   <item>Otherwise an existing lock is present and <see cref="CurrentHolder"/> is populated.
    ///         Decide based on whether the <b>holding process is alive</b>:
    ///     <list type="bullet">
    ///       <item><b>Dead</b> (same machine, recorded PID gone or reused): auto-reclaim — delete
    ///             and re-create the lock. Sets <see cref="ReclaimedDeadSession"/>.</item>
    ///       <item><b>Alive</b> (same machine, PID still running): a live sibling instance — go
    ///             read-only and set <see cref="HolderIsLiveSameMachine"/>. Never reclaimed,
    ///             regardless of heartbeat age, so a paused/sleeping sibling is never stolen from.</item>
    ///       <item><b>Unknown</b> (different machine, or a lock from an older build with no PID,
    ///             or the process state can't be read): fall back to heartbeat-age
    ///             <see cref="DetectStaleLock"/>, which sets <see cref="IsStaleLock"/> for the
    ///             caller's user-confirmation prompt.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// </summary>
    /// <returns>True if this instance now holds the lock (freshly created or reclaimed from a
    /// dead holder); false otherwise.</returns>
    private bool TryCreateLockFile()
    {
        if (TryCreateOnce())
            return true;

        // An existing lock blocked us; CurrentHolder is now populated.
        switch (GetHolderLiveness(CurrentHolder))
        {
            case HolderLiveness.Dead:
                if (TryReclaimDeadHolder())
                    return true;
                // Lost the race (someone re-created the lock between our delete and create);
                // re-read the new holder and fall through to the heartbeat path.
                ReadCurrentHolder();
                return DetectStaleLock();

            case HolderLiveness.Alive:
                // A live process on this machine holds the lock — a second instance. Do NOT
                // reclaim, do NOT mark stale (even if the heartbeat looks old, e.g. the sibling
                // is paused at a breakpoint or mid-sleep). Read-only is the only safe outcome.
                HolderIsLiveSameMachine = true;
                return false;

            default: // Unknown — cross-machine, old-format lock, or unreadable process state.
                return DetectStaleLock();
        }
    }

    /// <summary>Liveness of the process that currently holds the lock.</summary>
    private enum HolderLiveness
    {
        /// <summary>The holder process is still running on this machine.</summary>
        Alive,
        /// <summary>The holder process is provably gone (same machine; PID absent or reused).</summary>
        Dead,
        /// <summary>Can't tell — different machine, old-format lock, or the process can't be queried.</summary>
        Unknown
    }

    /// <summary>
    /// Determines whether the recorded lock holder's process is still alive. Only conclusive
    /// on the <b>same machine</b> (a PID is meaningless across machines). Returns
    /// <see cref="HolderLiveness.Unknown"/> whenever it cannot be certain, so we never steal a
    /// lock on a guess.
    /// </summary>
    private HolderLiveness GetHolderLiveness(LockFileData? holder)
    {
        if (holder is null) return HolderLiveness.Unknown;
        if (!string.Equals(holder.Machine, _identity.MachineName, StringComparison.OrdinalIgnoreCase))
            return HolderLiveness.Unknown;       // can't inspect a PID on another machine
        if (holder.ProcessId <= 0)
            return HolderLiveness.Unknown;       // lock written by an older build — no PID

        try
        {
            using var p = _identity.LookupProcess(holder.ProcessId);

            // The PID is in use. Guard against PID reuse: if we recorded a start time and the
            // live process started at a different time, the original holder is gone and this is
            // an unrelated process that happened to inherit the id.
            if (holder.ProcessStartTimeUtc is { } recordedStart)
            {
                DateTime liveStart;
                try { liveStart = p.StartTimeUtc; }
                catch { return HolderLiveness.Unknown; } // can't read start time → don't guess

                if (Math.Abs((liveStart - recordedStart).TotalSeconds) > 2)
                    return HolderLiveness.Dead;  // PID reused — original holder is gone
            }

            return HolderLiveness.Alive;
        }
        catch (ArgumentException)
        {
            // No process with that id exists — the holder is gone.
            return HolderLiveness.Dead;
        }
        catch (Exception ex)
        {
            // Access denied or any other failure — be conservative and don't reclaim.
            App.Logger.LogInfo($"[WriteLockService] Could not determine holder liveness (PID {holder.ProcessId}): {ex.Message}");
            return HolderLiveness.Unknown;
        }
    }

    /// <summary>
    /// Breaks a lock whose holder process is provably dead and re-acquires it atomically.
    /// Sets <see cref="ReclaimedDeadSession"/> on success. Returns false if another instance
    /// won the race to re-create the lock between our delete and create.
    /// </summary>
    private bool TryReclaimDeadHolder()
    {
        var deadHolder = CurrentHolder;
        try { File.Delete(_lockFilePath!); }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[WriteLockService] Could not delete dead holder's lock: {ex.Message}");
            return false;
        }

        if (TryCreateOnce())
        {
            ReclaimedDeadSession = true;
            App.Logger.LogInfo(
                $"[WriteLockService] Reclaimed lock from dead session " +
                $"(PID {deadHolder?.ProcessId}, {deadHolder?.Username}@{deadHolder?.Machine}).");
            return true;
        }

        App.Logger.LogInfo("[WriteLockService] TryReclaimDeadHolder: lost race — another instance re-created the lock.");
        return false;
    }

    /// <summary>
    /// One atomic attempt to create the lock file using <see cref="FileMode.CreateNew"/>.
    /// On success, writes the lock JSON payload and returns true. On failure (file already
    /// exists), reads the existing file into <see cref="CurrentHolder"/> and returns false.
    /// </summary>
    private bool TryCreateOnce()
    {
        try
        {
            // The stream is closed (and the payload flushed) at the end of this block, so the crash
            // point below sees a complete lock file.
            using (var fs = File.Open(_lockFilePath!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                WriteLockData(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // CreateNew failed. Two fundamentally different causes share this catch:
            //   (a) the lock file ALREADY EXISTS → another instance holds it (normal contention).
            //   (b) the create/write was BLOCKED → Windows Controlled Folder Access (ransomware
            //       protection), an NTFS ACL denial, a disk-full condition, or a sharing violation.
            //       In this case NO lock file exists and we are NOT contending with another holder.
            //
            // FileMode.CreateNew throws an IOException for BOTH a pre-existing file (ERROR_FILE_EXISTS)
            // and a blocked write (e.g. Controlled Folder Access surfaces ERROR_FILE_NOT_FOUND /
            // ERROR_ACCESS_DENIED), so the exception type alone is ambiguous. Disambiguate by asking
            // whether the file is actually present: a READ (File.Exists) is not restricted by
            // Controlled Folder Access, which only blocks modification.
            if (File.Exists(_lockFilePath!))
            {
                // (a) genuine existing lock — read the holder and report contention.
                ReadCurrentHolder();
                return false;
            }

            // (b) write failure — no lock exists, but we could not create one. Record an accurate,
            // actionable reason so the caller surfaces it instead of silently demoting to read-only
            // with a phantom "locked by another instance" holder.
            CurrentHolder  = null;
            var (userMsg, itDetail) = DescribeLockWriteFailure(ex);
            LockWriteError    = userMsg;
            LockWriteItDetail = itDetail;
            App.Logger.LogInfo(
                $"[WriteLockService] Lock-file create blocked (no lock present): type={ex.GetType().Name} " +
                $"hresult=0x{ex.HResult:X8} msg=\"{ex.Message}\" path={_lockFilePath}");
            return false;
        }

        // Created and closed. Outside the try above so a hook's exception can never be mistaken for
        // a failed create (the catch only handles IOException / UnauthorizedAccessException).
        CrashPoints.Hit("Lock.AfterCreated");
        return true;
    }

    /// <summary>
    /// Checks whether the existing lock looks abandoned and, if so, sets <see cref="IsStaleLock"/> to
    /// true so the caller can prompt the user before calling <see cref="ForceAcquire"/>. Does NOT
    /// auto-reclaim the lock.
    ///
    /// <para><b>Two signals (spec item 9b).</b> The lock is offered for takeover when EITHER
    /// <list type="bullet">
    ///   <item>the timestamp inside the file is older than <see cref="StaleLockThresholdSeconds"/> by this
    ///         computer's clock. This is only a <b>hint</b>: it compares two computers' clocks, which can
    ///         disagree by minutes, so it can neither prove a lock dead nor keep a dead one alive. Its
    ///         job is to let an obviously dead lock be offered immediately; or</item>
    ///   <item>this process has seen the same heartbeat unchanged for <see cref="StaleLockThresholdSeconds"/>
    ///         of its own time (<see cref="LockHeartbeatWatch"/>). This is clock-free and catches a dead
    ///         writer whose clock runs ahead, which the hint would call alive. The polling path reaches
    ///         it, then the user's "Take over as writer" click re-enters here.</item>
    /// </list>
    /// Either way the actual takeover waits for <see cref="StaleConfirmationRemaining"/>, so the hint
    /// alone can never take a lock from a writer that is still renewing its heartbeat.</para>
    ///
    /// <para>Every call also records the sighting, so the confirmation clock starts the first time
    /// this process reads the holder.</para>
    /// </summary>
    /// <returns>Always false — stale detection is not the same as acquisition.</returns>
    private bool DetectStaleLock()
    {
        if (CurrentHolder is null) return false;

        var unchanged  = _heartbeatWatch.Observe(_lockFilePath!, CurrentHolder);
        var clockAge   = (_time.GetUtcNow().UtcDateTime - CurrentHolder.Heartbeat).TotalSeconds;
        var clockHint  = clockAge > StaleLockThresholdSeconds;
        var stoodStill = unchanged.TotalSeconds >= StaleLockThresholdSeconds;
        if (!clockHint && !stoodStill) return false;

        App.Logger.LogInfo(
            $"[WriteLockService] Stale lock detected (clock says {clockAge:F0}s old, heartbeat unchanged " +
            $"for {unchanged.TotalSeconds:F0}s here) — setting IsStaleLock for caller to handle.");
        IsStaleLock = true;
        return false;
    }

    /// <summary>
    /// Reads the existing lock file and deserializes its JSON into
    /// <see cref="CurrentHolder"/>.
    /// </summary>
    /// <remarks>
    /// <para>File-not-found or permission errors set <see cref="CurrentHolder"/> to null
    /// (the lock may have been deleted between our existence check and the read).</para>
    /// <para>JSON corruption is treated as a stale/unreadable lock: <see cref="CurrentHolder"/>
    /// is set to a synthetic entry with an ancient heartbeat so that stale-lock detection
    /// sees it as expired, rather than treating corruption as "no lock held".</para>
    /// </remarks>
    private void ReadCurrentHolder()
    {
        try
        {
            var json = File.ReadAllText(_lockFilePath!);
            CurrentHolder = ParseLockFile(json);
        }
        catch (JsonException ex)
        {
            // Lock file exists but contains garbage — treat as an unreadable stale lock
            // rather than "no lock." A synthetic holder with an ancient heartbeat ensures
            // DetectStaleLock() and PollLockFile() classify it as stale.
            App.Logger.LogInfo($"[WriteLockService] Lock file corrupted (treating as stale): {ex.Message}");
            CurrentHolder = CreateCorruptedHolder();
        }
        catch (Exception ex)
        {
            // File not found, permission denied, or other I/O error — the lock file
            // may have been deleted between our existence check and this read.
            App.Logger.LogInfo($"[WriteLockService] Could not read lock file: {ex.Message}");
            CurrentHolder = null;
        }
    }

    /// <summary>
    /// The label written into <see cref="LockFileData.Username"/> and <see cref="LockFileData.Machine"/>
    /// of the synthetic holder that stands in for an unreadable (garbled) lock file. User-facing
    /// text (<see cref="StaleLockPrompt"/>) tests for it so it never shows "(corrupted) on (corrupted)".
    /// </summary>
    internal const string CorruptedHolderLabel = "(corrupted)";

    /// <summary>
    /// Builds the synthetic holder used when a lock file exists but cannot be parsed: an ancient
    /// heartbeat so the open path's clock hint classifies it as expired (rather than treating
    /// corruption as "no lock held"), and an empty <see cref="LockFileData.SessionGuid"/> so two
    /// reads of the same garbled file compare as the same lock (which is also how polling times it:
    /// a file that stays garbled stands unchanged).
    /// </summary>
    /// <returns>A new synthetic holder.</returns>
    private static LockFileData CreateCorruptedHolder() => new()
    {
        Username  = CorruptedHolderLabel,
        Machine   = CorruptedHolderLabel,
        Acquired  = DateTime.MinValue,
        Heartbeat = DateTime.MinValue,
    };

    /// <summary>
    /// Turns a lock file's text into its holder, and registers the holder's user name and machine name with
    /// <see cref="PrivacyScrubber"/> so they are removed from anything sent in an error report (spec item 6, step 6c).
    /// Every read of a lock file, here and in <see cref="CheckoutService"/>, goes through this one method, so no
    /// holder's name can be missed.
    /// </summary>
    /// <param name="json">The lock file's text.</param>
    /// <returns>The holder; null for a file holding the JSON literal <c>null</c>.</returns>
    /// <exception cref="JsonException">The text is not valid lock-file JSON (also for empty text).</exception>
    internal static LockFileData? ParseLockFile(string? json)
    {
        var holder = JsonSerializer.Deserialize<LockFileData>(json ?? string.Empty);
        if (holder is not null)
        {
            PrivacyScrubber.RegisterSensitive(holder.Username, PrivacyScrubber.UserLabel);
            PrivacyScrubber.RegisterSensitive(holder.Machine,  PrivacyScrubber.MachineLabel);
        }
        return holder;
    }

    /// <summary>Holder marker: nobody holds the lock (or no holder is known).</summary>
    internal const string HolderNone = "none";

    /// <summary>Holder marker: the lock file couldn't be read, or names nobody.</summary>
    internal const string HolderUnreadable = "unreadable";

    /// <summary>Holder marker: this user, on this computer.</summary>
    internal const string HolderSelf = "self";

    /// <summary>Holder marker: this user's name, on another computer.</summary>
    internal const string HolderSelfOtherComputer = "self-other-computer";

    /// <summary>Holder marker: someone else.</summary>
    internal const string HolderOther = "other";

    /// <summary>
    /// What a breadcrumb records about a lock holder instead of their name (spec item 6, step 6c; decided 2026-10-21):
    /// how the holder relates to this user, which is what diagnosis needs, and identifies nobody.
    /// </summary>
    /// <param name="holder">The holder, or null.</param>
    /// <returns><see cref="HolderNone"/>, <see cref="HolderUnreadable"/>, <see cref="HolderSelf"/>,
    /// <see cref="HolderSelfOtherComputer"/> or <see cref="HolderOther"/>.</returns>
    internal string DescribeHolder(LockFileData? holder)
    {
        if (holder is null)
            return HolderNone;
        if (string.IsNullOrWhiteSpace(holder.Username) || holder.Username == CorruptedHolderLabel)
            return HolderUnreadable;
        if (!string.Equals(holder.Username, _identity.UserName, StringComparison.OrdinalIgnoreCase))
            return HolderOther;
        return string.Equals(holder.Machine, _identity.MachineName, StringComparison.OrdinalIgnoreCase)
            ? HolderSelf
            : HolderSelfOtherComputer;
    }

    /// <summary>The verdict of <see cref="RecheckStaleHolder"/>.</summary>
    private enum StaleRecheck
    {
        /// <summary>The lock file no longer exists — nobody holds the lock.</summary>
        Missing,
        /// <summary>The lock file holds the same session and the same heartbeat as when it was judged stale.</summary>
        Unchanged,
        /// <summary>The lock file differs from the snapshot, or could not be read — treat the holder as alive.</summary>
        Changed
    }

    /// <summary>
    /// Re-reads the lock file and compares it with the snapshot that was judged stale. The comparison
    /// is on <see cref="LockFileData.SessionGuid"/> and <see cref="LockFileData.Heartbeat"/>: a
    /// renewing holder always advances the heartbeat, and a new holder always has a new session.
    ///
    /// <para><b>Unreadable means changed.</b> A read that fails for any reason other than "the file is
    /// gone" (a sharing violation while the holder renews, access denied, …) cannot prove the lock is
    /// unchanged, so it reports <see cref="StaleRecheck.Changed"/> and nothing is deleted. A file that
    /// reads but does not parse is compared as the synthetic corrupted holder
    /// (<see cref="CreateCorruptedHolder"/>): still garbled the same way as when it was judged stale
    /// compares as <see cref="StaleRecheck.Unchanged"/> (a permanently garbled lock must stay
    /// takeable); a lock that was valid and is now garbled compares as changed.</para>
    ///
    /// <para>Uses a plain synchronous read: the caller (<see cref="ForceAcquire"/>) already runs under a
    /// <see cref="NetworkFileOps"/> deadline, like <see cref="ReadCurrentHolder"/>.</para>
    /// </summary>
    /// <param name="expected">The holder snapshot the stale judgement was made on.</param>
    /// <param name="current">The lock file's present contents; null when missing or unreadable.</param>
    /// <returns>Whether the lock file is gone, unchanged or changed.</returns>
    private StaleRecheck RecheckStaleHolder(LockFileData expected, out LockFileData? current)
    {
        current = null;

        string json;
        try
        {
            json = File.ReadAllText(_lockFilePath!);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return StaleRecheck.Missing;
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[WriteLockService] ForceAcquire: could not re-read the lock file — leaving it alone: {ex.Message}");
            return StaleRecheck.Changed;
        }

        try { current = ParseLockFile(json) ?? CreateCorruptedHolder(); }
        catch (JsonException) { current = CreateCorruptedHolder(); }

        return string.Equals(expected.SessionGuid, current.SessionGuid, StringComparison.Ordinal)
               && expected.Heartbeat == current.Heartbeat
            ? StaleRecheck.Unchanged
            : StaleRecheck.Changed;
    }

    /// <summary>
    /// Builds a neutral user-facing message and an optional IT-detail string for a failure to
    /// <b>create</b> the lock file (as distinct from contending with one that already exists).
    /// Recognises the common Windows HResults so the read-only banner can tell the user something
    /// actionable — and, for CFA/permission blocks, offer a "Details for IT" button with the
    /// specific Controlled Folder Access remedy.
    /// </summary>
    /// <param name="ex">The <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// thrown by <c>CreateNew</c>.</param>
    /// <returns>A tuple of (neutral user-facing message, optional IT detail). The IT detail is null
    /// when the failure is not CFA/permission-related (e.g. disk full).</returns>
    private static (string UserMessage, string? ItDetail) DescribeLockWriteFailure(Exception ex)
    {
        // 0x80070002 ERROR_FILE_NOT_FOUND and 0x80070005 ERROR_ACCESS_DENIED are both surfaced by
        // Controlled Folder Access when it blocks a write to a protected folder (Documents, Desktop,
        // etc.); access-denied is also a plain NTFS permission problem.
        // 0x80070070 ERROR_DISK_FULL is a full volume.
        return (uint)ex.HResult switch
        {
            0x80070002 or 0x80070005 => (
                "Read-only — this folder's security settings prevented TermPoint from opening the database for editing.",
                "Windows Controlled Folder Access (ransomware protection) is blocking TermPoint from " +
                "writing to this folder. To fix this, either allow TermPoint in Windows Security → " +
                "Virus & Threat Protection → Ransomware Protection → Allow an app through Controlled " +
                "Folder Access, or move the database to a folder that is not protected (outside of " +
                "Documents, Desktop, etc.)."
            ),
            0x80070070 => (
                "Read-only — couldn't open the database for editing because the disk is full.",
                null
            ),
            _ => (
                "Read-only — this folder's settings prevented TermPoint from opening the database for editing. " +
                "Check the folder's permissions and your antivirus settings.",
                null
            ),
        };
    }

    /// <summary>
    /// Serializes a new <see cref="LockFileData"/> payload to the provided stream.
    /// Both <c>Acquired</c> and <c>Heartbeat</c> are set to the current UTC time.
    /// <see cref="SessionGuid"/> is included so wake-checks can identify this exact session.
    /// </summary>
    /// <param name="fs">An open, writable file stream positioned at the start.</param>
    private void WriteLockData(Stream fs)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var data = new LockFileData
        {
            Username            = _identity.UserName,
            Machine             = _identity.MachineName,
            SessionGuid         = SessionGuid,
            Acquired            = now,
            Heartbeat           = now,
            ProcessId           = _identity.ProcessId,
            ProcessStartTimeUtc = _identity.ProcessStartTimeUtc
        };
        JsonSerializer.Serialize(fs, data);
    }

    /// <summary>
    /// Rewrites the lock file with a fresh heartbeat timestamp, preserving the
    /// original <see cref="LockFileData.Acquired"/> value. Uses an atomic
    /// write-to-temp-then-rename pattern so readers never see a partial file.
    /// Fired by the heartbeat timer (fire-and-forget); non-throwing — logs and
    /// returns on any error. All share I/O is deadline-bounded via
    /// <see cref="NetworkFileOps"/> so the gate is never held longer than
    /// ~3 deadlines even against a black-holed share.
    /// </summary>
    private async Task RenewHeartbeatAsync()
    {
#if BROWSER
        // The browser demo never holds a real write lock — no heartbeat to renew.
        // (NetworkFileOps is excluded from the browser target.)
        await Task.CompletedTask;
#else
        bool raiseFailed = false;

        // Serialize against Release() (and any concurrent ForceRenewHeartbeat) so we never
        // re-create the lock file after it has been deleted. Zero-timeout wait: if the gate
        // is contended a release or another renewal is already in flight — skipping this
        // tick is harmless (the stale threshold is 3× the heartbeat interval).
        if (!await _lockFileGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (_lockFilePath is null || !IsWriter) return;
            var path = _lockFilePath;
            bool renewed = false;
            try
            {
                // Read the existing data to preserve the original Acquired timestamp,
                // then update the heartbeat.
                var (readCompleted, json) = await NetworkFileOps.ReadAllTextAsync(path).ConfigureAwait(false);
                if (readCompleted)
                {
                    var data = ParseLockFile(json) ?? new LockFileData();
                    data.Heartbeat = _time.GetUtcNow().UtcDateTime;

                    // Write to a temp file first, then atomically replace. This ensures
                    // a reader never observes a partially-written JSON file.
                    var tmp = path + ".tmp";
                    var tmpWritten = await NetworkFileOps.WriteAllTextAsync(tmp, JsonSerializer.Serialize(data)).ConfigureAwait(false);
                    if (tmpWritten) CrashPoints.Hit("Heartbeat.AfterTmpWritten");
                    renewed = tmpWritten
                           && await NetworkFileOps.MoveAsync(tmp, path).ConfigureAwait(false);
                }

                if (!renewed)
                    App.Logger.LogInfo("[WriteLockService] Heartbeat renewal failed: network operation timed out.");
            }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[WriteLockService] Heartbeat renewal failed: {ex.Message}");
            }

            if (renewed)
            {
                // Successful renewal: reset the consecutive-failure counter. (F11.)
                _heartbeatConsecutiveFailures = 0;
            }
            else
            {
                // Escalate after enough consecutive failures: the lock file is likely stale
                // from the perspective of readers. (F11, data-integrity-agenda 2026-05-04.)
                _heartbeatConsecutiveFailures++;
                if (_heartbeatConsecutiveFailures >= HeartbeatFailureThreshold)
                {
                    _heartbeatConsecutiveFailures = 0; // reset so a second run fires again
                    App.Logger.LogInfo(
                        $"[WriteLockService] {HeartbeatFailureThreshold} consecutive heartbeat " +
                        "failures — raising HeartbeatFailed.");
                    raiseFailed = true;
                }
            }
        }
        finally
        {
            _lockFileGate.Release();
        }

        // Raise OUTSIDE the gate so a handler that calls back into this service (Release,
        // EnterReaderMode, …) cannot deadlock on _lockFileGate.
        if (raiseFailed)
            HeartbeatFailed?.Invoke();
#endif
    }

    // ── Timer management ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts the background heartbeat timer. Fires every
    /// <see cref="HeartbeatIntervalSeconds"/> seconds and calls
    /// <see cref="RenewHeartbeat"/>. Only started when this instance is the writer.
    /// </summary>
    private void StartHeartbeat()
    {
        if (!_startTimers) return; // test mode: TickHeartbeatAsync drives it by hand
        var interval = TimeSpan.FromSeconds(HeartbeatIntervalSeconds);
        // Fire-and-forget: RenewHeartbeatAsync is non-throwing by construction, and the
        // gate's zero-timeout wait means overlapping ticks skip rather than queue.
        _heartbeatTimer = new Timer(_ => _ = RenewHeartbeatAsync(), null, interval, interval);
    }

    /// <summary>
    /// Starts the background polling timer. Fires every <see cref="PollIntervalSeconds"/>
    /// seconds and calls <see cref="PollLockFile"/> on the UI thread. Only started
    /// when this instance is in read-only mode.
    /// </summary>
    private void StartPolling()
    {
#if !BROWSER
        _pollRunning = true;
        if (!_startTimers) return; // test mode: TickPollAsync drives it by hand
        var interval = TimeSpan.FromSeconds(PollIntervalSeconds);
        _pollTimer = new Timer(_ =>
        {
            _ = Dispatcher.UIThread.InvokeAsync(() => RunPollTickAsync());
        }, null, interval, interval);
#endif
    }

#if !BROWSER
    /// <summary>
    /// The body of one poll tick: <see cref="PollLockFile"/> with its failure logged rather than thrown.
    /// Shared by the poll timer (which runs it on the UI thread) and <see cref="TickPollAsync"/>.
    /// </summary>
    /// <returns>A task that completes when the poll finishes; never faults.</returns>
    private async Task RunPollTickAsync()
    {
        try { await PollLockFile(); }
        catch (Exception ex) { App.Logger.LogError(ex, "[WriteLockService] PollLockFile failed"); }
    }
#endif

    /// <summary>
    /// Starts the wake-detection timer. Fires every 30 seconds and compares the
    /// elapsed time to the expected interval. If the gap significantly exceeds 30s
    /// (indicating the machine was asleep), raises <see cref="WakeDetected"/> on
    /// the UI thread. Only started when this instance holds the write lock.
    /// </summary>
    private void StartWakeDetection()
    {
        const int checkIntervalSeconds = 30;

        _lastWakeTick = _time.GetUtcNow().UtcDateTime;
        if (!_startTimers) return; // test mode: TickWakeCheck drives it by hand
        var interval = TimeSpan.FromSeconds(checkIntervalSeconds);

        _wakeDetectionTimer = new Timer(_ =>
        {
            if (WakeGapExceeded())
                Dispatcher.UIThread.Post(() => WakeDetected?.Invoke());
        }, null, interval, interval);
    }

    /// <summary>A gap between wake checks longer than this many seconds (3x the 30 s interval) means the machine was asleep.</summary>
    private const int WakeThresholdSeconds = 90;

    /// <summary>
    /// The body of one wake-detection tick: measures the time since the previous tick, remembers "now" for the
    /// next one, and reports whether the gap was long enough to infer the machine was asleep (logging it if so).
    /// Shared by the wake timer and <see cref="TickWakeCheck"/>; the caller decides how to raise
    /// <see cref="WakeDetected"/>.
    /// </summary>
    /// <returns>True when the gap exceeded <see cref="WakeThresholdSeconds"/>.</returns>
    private bool WakeGapExceeded()
    {
        var now     = _time.GetUtcNow().UtcDateTime;
        var elapsed = (now - _lastWakeTick).TotalSeconds;
        _lastWakeTick = now;

        if (elapsed > WakeThresholdSeconds)
        {
            App.Logger.LogInfo($"[WriteLockService] Wake detected — gap was {elapsed:F0}s.");
            return true;
        }
        return false;
    }

    // ── Manual ticks (test seam) ───────────────────────────────────────────────

    /// <summary>
    /// Runs ONE heartbeat tick on demand, exactly the work the heartbeat timer does
    /// (<see cref="RenewHeartbeatAsync"/>). Used with <c>startTimers: false</c>. Never throws.
    /// </summary>
    /// <returns>A task that completes when the renewal (or its skip/failure handling) is done.</returns>
    internal Task TickHeartbeatAsync() => RenewHeartbeatAsync();

    /// <summary>
    /// Runs ONE wake-detection tick on demand, exactly the check the wake timer makes. Differs from the timer
    /// in one way: when a wake is detected the timer raises <see cref="WakeDetected"/> through
    /// <c>Dispatcher.UIThread.Post</c>, whereas this raises it directly on the calling thread, so a test sees it
    /// synchronously. Used with <c>startTimers: false</c>.
    /// </summary>
    internal void TickWakeCheck()
    {
        if (WakeGapExceeded())
            WakeDetected?.Invoke();
    }

#if !BROWSER
    /// <summary>
    /// Runs ONE poll tick on demand, exactly the work the poll timer does (<see cref="PollLockFile"/> with its
    /// failure logged), but called directly rather than posted to the UI thread. Used with
    /// <c>startTimers: false</c>. Never throws.
    /// </summary>
    /// <returns>A task that completes when the poll finishes.</returns>
    internal Task TickPollAsync() => RunPollTickAsync();
#endif

    /// <summary>
    /// Checks whether the write lock is still held by another process. Called on
    /// the UI thread by the polling timer. If the lock file is absent, or its heartbeat
    /// has stood unchanged for <see cref="StaleLockThresholdSeconds"/> of this computer's
    /// own time (spec item 9b; the timestamp inside the file is never compared with this
    /// computer's clock), sets <see cref="WriteLockBecameAvailable"/> to true,
    /// stops the polling timer, and raises <see cref="LockStateChanged"/> so the
    /// UI can offer the "Switch to edit mode" prompt.
    ///
    /// <para><b>Network-awareness:</b> Uses <see cref="NetworkFileOps"/> for all
    /// file I/O so the poll does not hang when the share is unreachable. When a
    /// "lock file gone" result comes back, the same reachability probe used by
    /// <see cref="CheckoutService.VerifyLockIsOursAsync"/> is applied: we check
    /// whether D itself is reachable before concluding the lock was genuinely
    /// released. Without this, the SMB redirector's cached negative response
    /// would cause a false "write access available" offer during a network
    /// outage. See the decision table on <c>VerifyLockIsOursAsync</c> for the
    /// full rationale.</para>
    ///
    /// <para>Exposed as <c>internal</c> so that unit tests can invoke it directly,
    /// bypassing the timer and the UI-thread dispatch.</para>
    /// </summary>
#if !BROWSER
    internal async Task PollLockFile()
    {
        if (IsWriter || _lockFilePath is null) return;
        if (WriteLockBecameAvailable) return; // Already notified — wait for user action.

        bool available;

        var (existsCompleted, lockExists) = await NetworkFileOps.ExistsAsync(_lockFilePath);

        if (!existsCompleted)
        {
            // Network timeout — cannot determine lock state. Skip this cycle.
            App.Logger.LogInfo("[WriteLockService] Poll: network timeout — skipping cycle.");
            return;
        }

        if (!lockExists)
        {
            // Lock file appears gone — but is the share actually reachable?
            // Probe the parent directory to distinguish genuine release from
            // an SMB cache artifact. The directory is the share mount point —
            // it will exist as long as the share is accessible.
            var shareDir = Path.GetDirectoryName(_lockFilePath)!;
            var (dirCompleted, dirExists) = await NetworkFileOps.DirectoryExistsAsync(shareDir);
            if (!dirCompleted || !dirExists)
            {
                App.Logger.LogInfo("[WriteLockService] Poll: lock file gone but share directory unreachable — network down, skipping.");
                return;
            }

            available = true;
            _heartbeatWatch.Clear();   // whatever is created here next is a new lock — time it afresh
            App.Logger.LogInfo("[WriteLockService] Poll: lock file gone — write access available.");
        }
        else
        {
            // Read the lock file via the timeout-aware wrapper (the raw File.ReadAllText this
            // replaces ran on the UI thread and could hang for the full SMB timeout if the
            // share died between the Exists check above and the read).
            //
            // The read can also THROW fast with a sharing violation when this poll collides
            // with the writer's heartbeat rename of the same file — a guaranteed-occasional
            // cross-process collision on a lossy share, same transient class as the save-path
            // contended read in CheckoutService.VerifyLockIsOursOnceAsync. Both failure modes
            // must SKIP the cycle: the previous code funnelled them into CurrentHolder = null,
            // which the age computation treated as infinitely stale — flipping a PERMANENT
            // false "write access available" offer (the poll timer is disposed on the first
            // available=true) off a single transient, and inviting this reader to take over
            // a live writer's lock.
            string? json;
            try
            {
                var (readCompleted, text) = await NetworkFileOps.ReadAllTextAsync(_lockFilePath);
                if (!readCompleted)
                {
                    App.Logger.LogInfo("[WriteLockService] Poll: lock read timed out — skipping cycle.");
                    return;
                }
                json = text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.LogInfo(
                    $"[WriteLockService] Poll: lock read contended ({ex.GetType().Name}) — skipping cycle.");
                return;
            }

            // Judge the lock by whether its heartbeat CHANGES, timed on this computer's own clock —
            // never by comparing the timestamp inside the file with this computer's clock, which can
            // be minutes off the writer's (spec item 9b). A live writer rewrites the heartbeat every
            // minute, so every poll that sees a new heartbeat restarts the measurement; only a lock
            // that stands still for the whole stale threshold is offered for takeover.
            //
            // A successfully-read but unparseable lock file is observed as the synthetic corrupted
            // holder (matching ReadCurrentHolder): it never changes, so it is offered for takeover
            // once it has stood the same for the threshold — an unusable lock is not respected
            // forever, yet one garbled read (a half-synced file, say) cannot flip a permanent offer.
            LockFileData? data;
            try
            {
                data = ParseLockFile(json);
                CurrentHolder = data;
            }
            catch (JsonException)
            {
                App.Logger.LogInfo("[WriteLockService] Poll: lock file corrupted — timing it as an unreadable lock.");
                data = null;
                CurrentHolder = CreateCorruptedHolder();
            }

            var unchanged = _heartbeatWatch.Observe(_lockFilePath, data ?? CreateCorruptedHolder());
            available = unchanged.TotalSeconds >= StaleLockThresholdSeconds;
            if (available)
                App.Logger.LogInfo(
                    $"[WriteLockService] Poll: lock heartbeat unchanged for {unchanged.TotalSeconds:F0}s — write access available.");
        }

        if (available)
        {
            WriteLockBecameAvailable = true;
            _pollTimer?.Dispose();
            _pollTimer = null;
            _pollRunning = false;
            LockStateChanged?.Invoke();
        }
    }
#endif
}

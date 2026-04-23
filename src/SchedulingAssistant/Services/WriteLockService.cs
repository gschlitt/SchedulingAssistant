using Avalonia.Threading;
using SchedulingAssistant.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SchedulingAssistant.Services;

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
/// existing file is read. If its heartbeat is older than <see cref="StaleLockThresholdSeconds"/>
/// the lock is considered abandoned: the file is deleted and creation is retried once. A
/// fresh lock means this instance enters read-only mode and starts a polling timer that
/// fires every <see cref="PollIntervalSeconds"/> seconds. When the poll detects the lock is
/// gone or stale, it raises <see cref="LockStateChanged"/> on the UI thread so the banner
/// can offer the user a prompt to switch to edit mode.</para>
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
    /// A lock whose heartbeat timestamp is older than this many seconds is
    /// considered stale (writer crashed or was killed). Set to 3× the heartbeat
    /// interval so a single missed beat does not trigger a false reclaim.
    /// </summary>
    public const int StaleLockThresholdSeconds = 180;

    // ── State ──────────────────────────────────────────────────────────────────

    private string? _lockFilePath;
    private Timer? _heartbeatTimer;
    private Timer? _pollTimer;
    private Timer? _wakeDetectionTimer;
    private DateTime _lastWakeTick = DateTime.UtcNow;
    private bool _disposed;

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
    /// confirmation, then call <see cref="ForceAcquire"/> if they agree.
    /// Reset to false at the start of each <see cref="TryAcquire"/> call.
    /// </summary>
    public bool IsStaleLock { get; private set; }

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

    // ── Public API ─────────────────────────────────────────────────────────────

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
        // Release any lock we currently hold (handles DB-switching).
        Release();

        WriteLockBecameAvailable = false;
        IsStaleLock = false;
        _lockFilePath = Path.ChangeExtension(dbPath, ".lock");

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
        _wakeDetectionTimer?.Dispose();
        _wakeDetectionTimer = null;

        if (IsWriter && _lockFilePath is not null)
        {
            if (LockFileStillOurs(_lockFilePath))
            {
                try
                {
                    File.Delete(_lockFilePath);
                    App.Logger.LogInfo($"[WriteLockService] Released write lock: {_lockFilePath}");
                }
                catch (Exception ex)
                {
                    App.Logger.LogInfo($"[WriteLockService] Warning — could not delete lock file on release: {ex.Message}");
                }
            }
            else
            {
                App.Logger.LogInfo($"[WriteLockService] Release: lock file at {_lockFilePath} is no longer ours; skipping delete.");
            }
        }

        var wasWriter = IsWriter;

        IsWriter = false;
        CurrentHolder = null;
        _lockFilePath = null;

        // Notify subscribers (MainWindowViewModel, SubjectListViewModel, etc.) so any
        // UI enablement bound to IsWriter re-evaluates. Without this, a demotion from
        // write → read would leave write-gated controls (Add Section, Delete, etc.)
        // enabled until the next time TryAcquire/ForceAcquire happens to fire the event.
        if (wasWriter)
            Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
    }

    /// <summary>
    /// Returns true when the lock file at <paramref name="path"/> exists and contains
    /// a JSON payload whose <see cref="LockFileData.SessionGuid"/> matches ours.
    /// Returns false for a missing file, unreadable file, parse error, or a mismatched
    /// or missing <c>SessionGuid</c> — in all those cases the caller must treat the
    /// lock as not-ours and avoid touching it.
    /// </summary>
    private bool LockFileStillOurs(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<LockFileData>(json);
            return data?.SessionGuid == SessionGuid;
        }
        catch
        {
            return false;
        }
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
    }

    // ── Lock file helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Breaks a stale lock and acquires write access. Call only after the user has
    /// confirmed they wish to take over a stale session (i.e., <see cref="IsStaleLock"/>
    /// is true following a <see cref="TryAcquire"/> call).
    /// </summary>
    public void ForceAcquire()
    {
        if (_lockFilePath is null) return;
        try { File.Delete(_lockFilePath); } catch (Exception ex)
        {
            App.Logger.LogInfo($"[WriteLockService] ForceAcquire: could not delete stale lock: {ex.Message}");
        }

        IsStaleLock = false;
        CurrentHolder = null;

        if (TryCreateOnce())
        {
            IsWriter = true;
            StartHeartbeat();
            StartWakeDetection();
            App.Logger.LogInfo($"[WriteLockService] ForceAcquire: lock acquired after breaking stale lock.");
        }
        else
        {
            // Another instance beat us to it.
            IsWriter = false;
            StartPolling();
            App.Logger.LogInfo($"[WriteLockService] ForceAcquire: lost race — another instance acquired the lock.");
        }

        Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
    }

    /// <summary>
    /// Forces an immediate heartbeat renewal. Used by <see cref="CheckoutService"/>
    /// after wake-from-sleep detection to refresh the lock file without waiting for
    /// the next scheduled heartbeat tick.
    /// </summary>
    internal void ForceRenewHeartbeat() => RenewHeartbeat();

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

        _lockFilePath = Path.ChangeExtension(dbPath, ".lock");
        WriteLockBecameAvailable = false;
        IsStaleLock = false;

        // Populate CurrentHolder from whatever's in the lock file right now. If the
        // file is absent, CurrentHolder ends up null and the very first poll tick
        // will classify the lock as available and raise LockStateChanged again.
        if (File.Exists(_lockFilePath))
            ReadCurrentHolder();
        else
            CurrentHolder = null;

        StartPolling();
        App.Logger.LogInfo($"[WriteLockService] Entered reader mode for {_lockFilePath}; polling started.");

        Dispatcher.UIThread.Post(() => LockStateChanged?.Invoke());
    }

    /// <summary>
    /// Top-level lock acquisition attempt. First tries to create the file
    /// atomically; if the file exists and its heartbeat is stale, sets
    /// <see cref="IsStaleLock"/> and returns false so the caller can prompt the user.
    /// Populates <see cref="CurrentHolder"/> on failure.
    /// </summary>
    /// <returns>
    /// True if this instance successfully created the lock file and became the
    /// writer; false if another instance holds a lock (fresh or stale).
    /// </returns>
    private bool TryCreateLockFile()
    {
        return TryCreateOnce() || DetectStaleLock();
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
            using var fs = File.Open(_lockFilePath!, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            WriteLockData(fs);
            return true;
        }
        catch (IOException)
        {
            // File already exists — read it to populate CurrentHolder.
            ReadCurrentHolder();
            return false;
        }
    }

    /// <summary>
    /// Checks whether the existing lock is stale (heartbeat older than
    /// <see cref="StaleLockThresholdSeconds"/>). If so, sets <see cref="IsStaleLock"/>
    /// to true so the caller can prompt the user before calling <see cref="ForceAcquire"/>.
    /// Does NOT auto-reclaim the lock.
    /// </summary>
    /// <returns>Always false — stale detection is not the same as acquisition.</returns>
    private bool DetectStaleLock()
    {
        if (CurrentHolder is null) return false;
        var age = (DateTime.UtcNow - CurrentHolder.Heartbeat).TotalSeconds;
        if (age <= StaleLockThresholdSeconds) return false;

        App.Logger.LogInfo($"[WriteLockService] Stale lock detected (age {age:F0}s) — setting IsStaleLock for caller to handle.");
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
            CurrentHolder = JsonSerializer.Deserialize<LockFileData>(json);
        }
        catch (JsonException ex)
        {
            // Lock file exists but contains garbage — treat as an unreadable stale lock
            // rather than "no lock." A synthetic holder with an ancient heartbeat ensures
            // DetectStaleLock() and PollLockFile() classify it as stale.
            App.Logger.LogInfo($"[WriteLockService] Lock file corrupted (treating as stale): {ex.Message}");
            CurrentHolder = new LockFileData
            {
                Username  = "(corrupted)",
                Machine   = "(corrupted)",
                Acquired  = DateTime.MinValue,
                Heartbeat = DateTime.MinValue,
            };
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
    /// Serializes a new <see cref="LockFileData"/> payload to the provided stream.
    /// Both <c>Acquired</c> and <c>Heartbeat</c> are set to the current UTC time.
    /// <see cref="SessionGuid"/> is included so wake-checks can identify this exact session.
    /// </summary>
    /// <param name="fs">An open, writable file stream positioned at the start.</param>
    private void WriteLockData(Stream fs)
    {
        var now = DateTime.UtcNow;
        var data = new LockFileData
        {
            Username    = Environment.UserName,
            Machine     = Environment.MachineName,
            SessionGuid = SessionGuid,
            Acquired    = now,
            Heartbeat   = now
        };
        JsonSerializer.Serialize(fs, data);
    }

    /// <summary>
    /// Rewrites the lock file with a fresh heartbeat timestamp, preserving the
    /// original <see cref="LockFileData.Acquired"/> value. Uses an atomic
    /// write-to-temp-then-rename pattern so readers never see a partial file.
    /// Called by the heartbeat timer; non-throwing — logs and returns on any error.
    /// </summary>
    private void RenewHeartbeat()
    {
        if (_lockFilePath is null || !IsWriter) return;
        try
        {
            // Read the existing data to preserve the original Acquired timestamp,
            // then update the heartbeat.
            var json   = File.ReadAllText(_lockFilePath);
            var data   = JsonSerializer.Deserialize<LockFileData>(json) ?? new LockFileData();
            data.Heartbeat = DateTime.UtcNow;

            // Write to a temp file first, then atomically replace. This ensures
            // a reader never observes a partially-written JSON file.
            var tmp = _lockFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data));
            File.Move(tmp, _lockFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[WriteLockService] Heartbeat renewal failed: {ex.Message}");
        }
    }

    // ── Timer management ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts the background heartbeat timer. Fires every
    /// <see cref="HeartbeatIntervalSeconds"/> seconds and calls
    /// <see cref="RenewHeartbeat"/>. Only started when this instance is the writer.
    /// </summary>
    private void StartHeartbeat()
    {
        var interval = TimeSpan.FromSeconds(HeartbeatIntervalSeconds);
        _heartbeatTimer = new Timer(_ => RenewHeartbeat(), null, interval, interval);
    }

    /// <summary>
    /// Starts the background polling timer. Fires every <see cref="PollIntervalSeconds"/>
    /// seconds and calls <see cref="PollLockFile"/> on the UI thread. Only started
    /// when this instance is in read-only mode.
    /// </summary>
    private void StartPolling()
    {
        var interval = TimeSpan.FromSeconds(PollIntervalSeconds);
        _pollTimer = new Timer(_ => Dispatcher.UIThread.Post(async () => await PollLockFile()), null, interval, interval);
    }

    /// <summary>
    /// Starts the wake-detection timer. Fires every 30 seconds and compares the
    /// elapsed time to the expected interval. If the gap significantly exceeds 30s
    /// (indicating the machine was asleep), raises <see cref="WakeDetected"/> on
    /// the UI thread. Only started when this instance holds the write lock.
    /// </summary>
    private void StartWakeDetection()
    {
        const int checkIntervalSeconds = 30;
        const int wakeThresholdSeconds = 90; // 3× expected — must have been asleep

        _lastWakeTick = DateTime.UtcNow;
        var interval = TimeSpan.FromSeconds(checkIntervalSeconds);

        _wakeDetectionTimer = new Timer(_ =>
        {
            var now     = DateTime.UtcNow;
            var elapsed = (now - _lastWakeTick).TotalSeconds;
            _lastWakeTick = now;

            if (elapsed > wakeThresholdSeconds)
            {
                App.Logger.LogInfo($"[WriteLockService] Wake detected — gap was {elapsed:F0}s.");
                Dispatcher.UIThread.Post(() => WakeDetected?.Invoke());
            }
        }, null, interval, interval);
    }

    /// <summary>
    /// Checks whether the write lock is still held by another process. Called on
    /// the UI thread by the polling timer. If the lock file is absent or its
    /// heartbeat is stale, sets <see cref="WriteLockBecameAvailable"/> to true,
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
            App.Logger.LogInfo("[WriteLockService] Poll: lock file gone — write access available.");
        }
        else
        {
            ReadCurrentHolder();
            var age = CurrentHolder is null
                ? double.MaxValue
                : (DateTime.UtcNow - CurrentHolder.Heartbeat).TotalSeconds;
            available = age > StaleLockThresholdSeconds;
            if (available)
                App.Logger.LogInfo($"[WriteLockService] Poll: lock is stale (age {age:F0}s) — write access available.");
        }

        if (available)
        {
            WriteLockBecameAvailable = true;
            _pollTimer?.Dispose();
            _pollTimer = null;
            LockStateChanged?.Invoke();
        }
    }
}

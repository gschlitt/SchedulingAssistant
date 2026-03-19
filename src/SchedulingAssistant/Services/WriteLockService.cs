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
    private bool _disposed;

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

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when lock state changes — specifically when a
    /// read-only instance's poll detects the lock is gone or stale. Subscribers
    /// should re-read <see cref="IsWriter"/>, <see cref="CurrentHolder"/>, and
    /// <see cref="WriteLockBecameAvailable"/> to update their state.
    /// </summary>
    public event Action? LockStateChanged;

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
        _lockFilePath = Path.ChangeExtension(dbPath, ".lock");

        if (TryCreateLockFile())
        {
            IsWriter = true;
            StartHeartbeat();
            App.Logger.LogInfo($"[WriteLockService] Acquired write lock: {_lockFilePath}");
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
    /// </summary>
    public void Release()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _pollTimer?.Dispose();
        _pollTimer = null;

        if (IsWriter && _lockFilePath is not null)
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

        IsWriter = false;
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
    }

    // ── Lock file helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Top-level lock acquisition attempt. First tries to create the file
    /// atomically; if the file exists and its heartbeat is stale, deletes it and
    /// tries once more. Populates <see cref="CurrentHolder"/> on failure.
    /// </summary>
    /// <returns>
    /// True if this instance successfully created the lock file and became the
    /// writer; false if another instance holds a fresh lock.
    /// </returns>
    private bool TryCreateLockFile()
    {
        return TryCreateOnce() || TryReclaimStaleAndCreate();
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
    /// If the lock held by <see cref="CurrentHolder"/> is older than
    /// <see cref="StaleLockThresholdSeconds"/>, deletes the stale lock file and
    /// retries <see cref="TryCreateOnce"/>. If two instances attempt this
    /// simultaneously, only one will win the subsequent <see cref="FileMode.CreateNew"/>;
    /// the other falls back to read-only mode.
    /// </summary>
    /// <returns>True if the stale reclaim and subsequent creation both succeeded.</returns>
    private bool TryReclaimStaleAndCreate()
    {
        if (CurrentHolder is null) return false;
        var age = (DateTime.UtcNow - CurrentHolder.Heartbeat).TotalSeconds;
        if (age <= StaleLockThresholdSeconds) return false;

        App.Logger.LogInfo($"[WriteLockService] Stale lock detected (age {age:F0}s). Reclaiming.");

        try
        {
            File.Delete(_lockFilePath!);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[WriteLockService] Could not delete stale lock: {ex.Message}");
        }

        CurrentHolder = null;
        return TryCreateOnce();
    }

    /// <summary>
    /// Reads the existing lock file and deserializes its JSON into
    /// <see cref="CurrentHolder"/>. All exceptions are swallowed; on failure
    /// <see cref="CurrentHolder"/> is set to null.
    /// </summary>
    private void ReadCurrentHolder()
    {
        try
        {
            var json = File.ReadAllText(_lockFilePath!);
            CurrentHolder = JsonSerializer.Deserialize<LockFileData>(json);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[WriteLockService] Could not read lock file: {ex.Message}");
            CurrentHolder = null;
        }
    }

    /// <summary>
    /// Serializes a new <see cref="LockFileData"/> payload to the provided stream.
    /// Both <c>Acquired</c> and <c>Heartbeat</c> are set to the current UTC time.
    /// </summary>
    /// <param name="fs">An open, writable file stream positioned at the start.</param>
    private static void WriteLockData(Stream fs)
    {
        var now = DateTime.UtcNow;
        var data = new LockFileData
        {
            Username  = Environment.UserName,
            Machine   = Environment.MachineName,
            Acquired  = now,
            Heartbeat = now
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
        _pollTimer = new Timer(_ => Dispatcher.UIThread.Post(PollLockFile), null, interval, interval);
    }

    /// <summary>
    /// Checks whether the write lock is still held by another process. Called on
    /// the UI thread by the polling timer. If the lock file is absent or its
    /// heartbeat is stale, sets <see cref="WriteLockBecameAvailable"/> to true,
    /// stops the polling timer, and raises <see cref="LockStateChanged"/> so the
    /// UI can offer the "Switch to edit mode" prompt.
    ///
    /// <para>Exposed as <c>internal</c> so that unit tests can invoke it directly,
    /// bypassing the timer and the UI-thread dispatch.</para>
    /// </summary>
    internal void PollLockFile()
    {
        if (IsWriter || _lockFilePath is null) return;
        if (WriteLockBecameAvailable) return; // Already notified — wait for user action.

        bool available;

        if (!File.Exists(_lockFilePath))
        {
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

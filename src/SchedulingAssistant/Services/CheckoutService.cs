using Avalonia.Threading;
using SchedulingAssistant.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchedulingAssistant.Services;

/// <summary>
/// Manages the checkout / save lifecycle for every database the app opens.
///
/// <para><b>Core concept:</b> When a database D is opened, <see cref="CheckoutAsync"/>
/// copies it to a local working copy D' under <c>%AppData%\TermPoint\working\</c>,
/// acquires a write lock, and returns the path of D' so that <c>DatabaseContext</c>
/// (and therefore all SQLite writes) target D' on the local drive — never D directly.
/// An explicit <see cref="SaveAsync"/> call pushes D' back to D.</para>
///
/// <para><b>Always-on:</b> This flow is active for every database, whether it is on a
/// network drive or a local drive. Local-database users see a small overhead on open
/// and save, but gain a consistent explicit-save model.</para>
///
/// <para><b>New databases:</b> When D does not yet exist, D' is set equal to D (degenerate
/// mode). <c>DatabaseContext</c> creates the schema at that path normally. The first
/// <see cref="SaveAsync"/> call is a no-op for this case.</para>
///
/// <para><b>Thread safety:</b> <see cref="SaveAsync"/> is designed to be called from
/// background threads (e.g., the autosave timer). All events are raised on the Avalonia
/// UI thread via <c>Dispatcher.UIThread.Post</c>.</para>
///
/// <para><b>Testability:</b> Constructor parameters allow injecting an isolated
/// <see cref="WriteLockService"/>, <see cref="IAppLogger"/>, a synchronous dispatcher,
/// and a custom working directory. Production code passes <c>App.LockService</c> and
/// <c>App.Logger</c>; unit tests supply their own isolated instances.</para>
/// </summary>
public sealed class CheckoutService : IDisposable
{
    // ── Dependencies ─────────────────────────────────────────────────────────

    private readonly WriteLockService _lockService;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Dispatcher used to raise events on the UI thread. In production this is
    /// <c>Dispatcher.UIThread.Post</c>; in unit tests it is typically <c>a => a()</c>
    /// for synchronous, immediate dispatch.
    /// </summary>
    private readonly Action<Action> _dispatch;

    /// <summary>
    /// Directory in which working copies D' are stored. Set at construction time.
    /// Defaults to <c>%AppData%\TermPoint\working\</c>; overridden in tests to use
    /// an isolated temporary directory.
    /// </summary>
    private readonly string _workingDir;

    // ── State ──────────────────────────────────────────────────────────────────

    private Timer?  _autoSaveTimer;
    private bool    _disposed;

    /// <summary>Current checkout mode.</summary>
    public CheckoutMode Mode { get; private set; } = CheckoutMode.ReadOnly;

    /// <summary>
    /// Path to D — the database the user opened. May be on a network drive or local.
    /// Empty until <see cref="CheckoutAsync"/> is called.
    /// </summary>
    public string SourcePath { get; private set; } = string.Empty;

    /// <summary>
    /// Path to D' — the local working copy. All SQLite writes target this path.
    /// Equals <see cref="SourcePath"/> for new databases (degenerate mode).
    /// Empty until <see cref="CheckoutAsync"/> is called.
    /// </summary>
    public string WorkingPath { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of D at the time of the last successful checkout or save.
    /// Used to detect external modifications to D before overwriting it.
    /// Null for new databases (D did not exist at checkout time).
    /// </summary>
    public string? HashAtCheckout { get; private set; }

    /// <summary>
    /// True when D' contains changes that have not yet been saved back to D.
    /// Set to true immediately after a successful checkout. Cleared after each
    /// successful <see cref="SaveAsync"/>.
    /// </summary>
    public bool SessionDirty { get; private set; }

    /// <summary>
    /// Identity of the process holding the write lock when this instance is in
    /// read-only mode. Null in write mode or before checkout.
    /// </summary>
    public LockFileData? CurrentHolder { get; private set; }

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when a wake-from-sleep check finds that the write
    /// lock is no longer held by this session. The app should switch to read-only
    /// mode and notify the user that unsaved changes have been lost.
    /// </summary>
    public event Action? SessionTimedOut;

    /// <summary>
    /// Raised on the UI thread after each successful <see cref="SaveAsync"/> call.
    /// The UI can use this to show a brief "Saved" indicator.
    /// </summary>
    public event Action? SaveCompleted;

    /// <summary>
    /// Raised on the UI thread when <see cref="SaveAsync"/> fails for a reason
    /// other than a transient copy error. The string parameter contains a
    /// human-readable description suitable for display to the user.
    /// Autosave stops when this is raised for <see cref="SaveOutcome.LockLost"/>
    /// or <see cref="SaveOutcome.SourceModified"/>; it continues on
    /// <see cref="SaveOutcome.CopyError"/> (transient — worth retrying).
    /// </summary>
    public event Action<string>? SaveFailed;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="CheckoutService"/>.
    /// </summary>
    /// <param name="lockService">
    /// Lock service to use. Defaults to <see cref="App.LockService"/> when null.
    /// Pass an isolated instance in unit tests.
    /// </param>
    /// <param name="logger">
    /// Logger to use. Defaults to <see cref="App.Logger"/> when null.
    /// Pass a null-object logger in unit tests to suppress file I/O.
    /// </param>
    /// <param name="dispatch">
    /// Action used to dispatch callbacks to the UI thread. Defaults to
    /// <c>Dispatcher.UIThread.Post</c> when null. In unit tests, pass
    /// <c>a => a()</c> for synchronous, in-place dispatch so that event
    /// handlers can be observed immediately.
    /// </param>
    /// <param name="workingDir">
    /// Directory in which working copies D' are stored. Defaults to
    /// <c>%AppData%\TermPoint\working\</c> when null. Override in unit tests
    /// to use an isolated temporary directory and avoid polluting the real
    /// working folder.
    /// </param>
    public CheckoutService(
        WriteLockService? lockService = null,
        IAppLogger? logger           = null,
        Action<Action>? dispatch     = null,
        string? workingDir           = null)
    {
        _lockService = lockService ?? App.LockService;
        _logger      = logger      ?? App.Logger;
        _dispatch    = dispatch    ?? (action => Dispatcher.UIThread.Post(action));
        _workingDir  = workingDir  ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TermPoint",
            "working");
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a database for editing by acquiring the write lock and copying D to D'.
    /// Must be called before <see cref="App.InitializeServices"/> so that the correct
    /// path (D' for write mode, D for read-only mode) can be passed in.
    /// </summary>
    /// <param name="sourcePath">Full path to D — the database the user selected.</param>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> — lock acquired; use <see cref="WorkingPath"/> as dbPath.<br/>
    /// <see cref="CheckoutOutcome.ReadOnly"/>    — a fresh lock exists; use <see cref="SourcePath"/> as dbPath.<br/>
    /// <see cref="CheckoutOutcome.StaleHolder"/> — a stale lock exists; prompt the user then call <see cref="ForceCheckoutAsync"/>.<br/>
    /// <see cref="CheckoutOutcome.Failed"/>      — fatal error (e.g., copy hash mismatch); do not proceed.
    /// </returns>
    public async Task<CheckoutOutcome> CheckoutAsync(string sourcePath)
    {
        SourcePath = sourcePath;

        // ── New-database shortcut ──────────────────────────────────────────────
        // D does not exist yet. Use SourcePath as WorkingPath (degenerate mode):
        // DatabaseContext creates the schema directly at SourcePath and SaveAsync is a no-op.
        if (!File.Exists(sourcePath))
        {
            WorkingPath     = sourcePath;
            HashAtCheckout  = null;
            Mode            = CheckoutMode.WriteAccess;
            SessionDirty    = true;
            CurrentHolder   = null;

            _lockService.TryAcquire(sourcePath);
            if (!_lockService.IsWriter)
            {
                // Extremely unlikely — someone locked a file that doesn't exist yet.
                Mode          = CheckoutMode.ReadOnly;
                CurrentHolder = _lockService.CurrentHolder;
                return CheckoutOutcome.ReadOnly;
            }

            _lockService.WakeDetected += OnWake;
            return CheckoutOutcome.WriteAccess;
        }

        // ── Normal path — D exists ────────────────────────────────────────────
        WorkingPath = ComputeWorkingPath(sourcePath);

        _lockService.TryAcquire(sourcePath);

        if (_lockService.IsStaleLock)
        {
            CurrentHolder = _lockService.CurrentHolder;
            return CheckoutOutcome.StaleHolder;
        }

        if (!_lockService.IsWriter)
        {
            Mode          = CheckoutMode.ReadOnly;
            CurrentHolder = _lockService.CurrentHolder;
            return CheckoutOutcome.ReadOnly;
        }

        // We hold the lock — copy D to D'.
        Directory.CreateDirectory(_workingDir);
        HashAtCheckout = ComputeHash(sourcePath);
        WriteDirtyMarker();

        try
        {
            CopyWithSharing(sourcePath, WorkingPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: failed to copy D to D'");
            _lockService.Release();
            DeleteDirtyMarker();
            return CheckoutOutcome.Failed;
        }

        // Verify the copy.
        var copyHash = ComputeHash(WorkingPath);
        if (copyHash != HashAtCheckout)
        {
            _logger.LogInfo("CheckoutService: hash mismatch after copy — retrying once.");
            try { CopyWithSharing(sourcePath, WorkingPath); } catch { /* handled below */ }
            copyHash = ComputeHash(WorkingPath);

            if (copyHash != HashAtCheckout)
            {
                _logger.LogInfo("CheckoutService: hash mismatch on retry — aborting checkout.");
                _lockService.Release();
                DeleteDirtyMarker();
                try { File.Delete(WorkingPath); } catch { }
                return CheckoutOutcome.Failed;
            }
        }

        Mode          = CheckoutMode.WriteAccess;
        SessionDirty  = true;
        CurrentHolder = null;

        _lockService.WakeDetected += OnWake;
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// Breaks a stale lock and completes a checkout. Call after the user has confirmed
    /// they wish to take over the abandoned session (i.e., after
    /// <see cref="CheckoutAsync"/> returned <see cref="CheckoutOutcome.StaleHolder"/>).
    /// </summary>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> on success.
    /// <see cref="CheckoutOutcome.Failed"/> if another instance won the race.
    /// </returns>
    public async Task<CheckoutOutcome> ForceCheckoutAsync()
    {
        _lockService.ForceAcquire();

        if (!_lockService.IsWriter)
        {
            Mode          = CheckoutMode.ReadOnly;
            CurrentHolder = _lockService.CurrentHolder;
            return CheckoutOutcome.Failed;
        }

        // Lock acquired — now copy D to D' (same as CheckoutAsync write path).
        Directory.CreateDirectory(_workingDir);
        HashAtCheckout = ComputeHash(SourcePath);
        WriteDirtyMarker();

        try
        {
            CopyWithSharing(SourcePath, WorkingPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: ForceCheckout — failed to copy D to D'");
            _lockService.Release();
            DeleteDirtyMarker();
            return CheckoutOutcome.Failed;
        }

        var copyHash = ComputeHash(WorkingPath);
        if (copyHash != HashAtCheckout)
        {
            _logger.LogInfo("CheckoutService: ForceCheckout hash mismatch — aborting.");
            _lockService.Release();
            DeleteDirtyMarker();
            try { File.Delete(WorkingPath); } catch { }
            return CheckoutOutcome.Failed;
        }

        Mode          = CheckoutMode.WriteAccess;
        SessionDirty  = true;
        CurrentHolder = null;

        _lockService.WakeDetected += OnWake;
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// Saves D' back to D. Verifies the lock is still held and D has not been
    /// externally modified, writes a timestamped backup of D', copies D' to a
    /// temporary file, verifies it, then atomically renames it to D.
    /// </summary>
    /// <param name="releaseLockAfter">
    /// When true, deletes the lock file after a successful save.
    /// Pass true only on graceful shutdown.
    /// </param>
    /// <returns>A <see cref="SaveOutcome"/> describing the result.</returns>
    public async Task<SaveOutcome> SaveAsync(bool releaseLockAfter = false)
    {
        if (Mode != CheckoutMode.WriteAccess)
            return SaveOutcome.NotInWriteMode;

        // Degenerate mode — D' IS D (new database that was never copied).
        // Nothing to copy back; just release the lock if requested.
        if (WorkingPath == SourcePath)
        {
            SessionDirty = false;
            if (releaseLockAfter) _lockService.Release();
            _dispatch(() => SaveCompleted?.Invoke());
            return SaveOutcome.Success;
        }

        // ── Step 1: Verify we still hold the lock ─────────────────────────────
        if (!VerifyLockIsOurs())
        {
            await HandleSessionTimeoutAsync();
            return SaveOutcome.LockLost;
        }

        // ── Step 2: Verify D has not been externally modified ─────────────────
        if (HashAtCheckout is not null)
        {
            string currentSourceHash;
            try { currentSourceHash = ComputeHash(SourcePath); }
            catch (Exception ex)
            {
                _logger.LogInfo($"CheckoutService: could not hash source — {ex.Message}");
                // If D can't be read (e.g. network unavailable), treat as a copy error.
                var errMsg = $"Cannot reach the database location: {ex.Message}";
                _dispatch(() => SaveFailed?.Invoke(errMsg));
                return SaveOutcome.CopyError;
            }

            if (currentSourceHash != HashAtCheckout)
            {
                const string msg = "The database was modified outside this session. Save aborted.";
                _logger.LogInfo("CheckoutService: source hash mismatch — " + msg);
                _dispatch(() => SaveFailed?.Invoke(msg));
                return SaveOutcome.SourceModified;
            }
        }

        // ── Step 3: Write pre-save backup of D' ───────────────────────────────
        TakePreSaveBackup();

        // ── Step 4: Copy D' → D.tmp ───────────────────────────────────────────
        var tmpPath = SourcePath + ".tmp";
        try
        {
            CopyWithSharing(WorkingPath, tmpPath);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to write to database location: {ex.Message}";
            _logger.LogError(ex, "CheckoutService: D.tmp copy failed");
            _dispatch(() => SaveFailed?.Invoke(msg));
            return SaveOutcome.CopyError;
        }

        // ── Step 5: Verify D.tmp hash ─────────────────────────────────────────
        var workingHash = ComputeHash(WorkingPath);
        var tmpHash     = ComputeHash(tmpPath);
        if (tmpHash != workingHash)
        {
            _logger.LogInfo("CheckoutService: D.tmp hash mismatch — deleting and aborting.");
            try { File.Delete(tmpPath); } catch { }
            const string msg = "Copy verification failed. Please try saving again.";
            _dispatch(() => SaveFailed?.Invoke(msg));
            return SaveOutcome.CopyError;
        }

        // ── Step 6: Atomically rename D.tmp → D ──────────────────────────────
        try
        {
            File.Move(tmpPath, SourcePath, overwrite: true);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to finalize save: {ex.Message}";
            _logger.LogError(ex, "CheckoutService: D.tmp rename failed");
            try { File.Delete(tmpPath); } catch { }
            _dispatch(() => SaveFailed?.Invoke(msg));
            return SaveOutcome.CopyError;
        }

        // ── Step 7 & 8: Update state ──────────────────────────────────────────
        HashAtCheckout = workingHash;
        SessionDirty   = false;
        DeleteDirtyMarker();

        if (releaseLockAfter)
            _lockService.Release();

        _logger.LogInfo("CheckoutService: Save completed successfully.");
        _dispatch(() => SaveCompleted?.Invoke());
        return SaveOutcome.Success;
    }

    /// <summary>
    /// Detects whether a previous session for <paramref name="sourcePath"/> ended
    /// ungracefully with unsaved changes. Call at startup before
    /// <see cref="CheckoutAsync"/>.
    /// </summary>
    /// <param name="sourcePath">The database path the user is about to open.</param>
    /// <returns>
    /// True when both D' and its dirty marker exist, indicating unfinished work.
    /// </returns>
    public bool DetectCrashRecovery(string sourcePath)
    {
        var workingPath  = ComputeWorkingPath(sourcePath);
        var dirtyMarker  = workingPath + ".dirty";
        return File.Exists(dirtyMarker) && File.Exists(workingPath);
    }

    /// <summary>
    /// Acquires the lock and saves the orphaned D' back to D. Call when the user
    /// confirms they want to recover unsaved changes from a previous crashed session.
    /// After this returns, call <see cref="CheckoutAsync"/> normally to start a new session.
    /// </summary>
    /// <param name="sourcePath">The database path being recovered.</param>
    /// <returns>The save outcome; if not <see cref="SaveOutcome.Success"/>, discard instead.</returns>
    public async Task<SaveOutcome> ResumeFromCrashAsync(string sourcePath)
    {
        SourcePath  = sourcePath;
        WorkingPath = ComputeWorkingPath(sourcePath);

        _lockService.TryAcquire(sourcePath);
        if (!_lockService.IsWriter)
            return SaveOutcome.LockLost;

        Mode           = CheckoutMode.WriteAccess;
        HashAtCheckout = File.Exists(sourcePath) ? ComputeHash(sourcePath) : null;

        var result = await SaveAsync(releaseLockAfter: true);

        // Reset state so a fresh CheckoutAsync can follow.
        Mode        = CheckoutMode.ReadOnly;
        SourcePath  = string.Empty;
        WorkingPath = string.Empty;
        return result;
    }

    /// <summary>
    /// Discards the orphaned working copy from a previous crashed session.
    /// Call when the user declines crash recovery.
    /// </summary>
    /// <param name="sourcePath">The database path whose recovery data to discard.</param>
    public void DiscardCrash(string sourcePath)
    {
        var workingPath = ComputeWorkingPath(sourcePath);
        try { File.Delete(workingPath); }          catch { }
        try { File.Delete(workingPath + ".dirty"); } catch { }
    }

    /// <summary>
    /// Deletes any orphaned <c>D.tmp</c> file left alongside D from a previous
    /// crashed save. Call at startup before <see cref="CheckoutAsync"/>.
    /// </summary>
    /// <param name="sourcePath">The database path to check for an orphaned tmp file.</param>
    public void CleanupOrphanedTmp(string sourcePath)
    {
        var tmpPath = sourcePath + ".tmp";
        if (!File.Exists(tmpPath)) return;
        try
        {
            File.Delete(tmpPath);
            _logger.LogInfo($"CheckoutService: cleaned up orphaned tmp file: {tmpPath}");
        }
        catch (Exception ex)
        {
            _logger.LogInfo($"CheckoutService: could not delete orphaned tmp: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the write lock and optionally saves first. Call on graceful shutdown.
    /// </summary>
    /// <param name="saveFirst">
    /// When true and in write mode, runs <see cref="SaveAsync"/> before releasing.
    /// </param>
    public async Task ReleaseAsync(bool saveFirst)
    {
        StopAutoSave();
        _lockService.WakeDetected -= OnWake;

        if (saveFirst && Mode == CheckoutMode.WriteAccess)
            await SaveAsync(releaseLockAfter: true);
        else
            _lockService.Release();

        // Clean up the working copy if the session ended cleanly.
        if (!SessionDirty && File.Exists(WorkingPath) && WorkingPath != SourcePath)
        {
            try { File.Delete(WorkingPath); } catch { }
        }

        Mode = CheckoutMode.ReadOnly;
    }

    /// <summary>
    /// Discards D' and switches to read-only mode without saving. Called when the
    /// write lock is lost (e.g., session timeout after wake from sleep).
    /// </summary>
    public async Task DiscardAndGoReadonlyAsync()
    {
        StopAutoSave();
        _lockService.WakeDetected -= OnWake;

        if (WorkingPath != SourcePath)
        {
            if (File.Exists(WorkingPath))
                try { File.Delete(WorkingPath); } catch { }
            DeleteDirtyMarker();
        }

        Mode         = CheckoutMode.ReadOnly;
        SessionDirty = false;

        await Task.CompletedTask;
    }

    // ── Autosave ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the autosave timer, firing every
    /// <see cref="AppSettings.AutoSaveIntervalMinutes"/> minutes.
    /// Any previously running timer is stopped first.
    /// </summary>
    public void StartAutoSave()
    {
        StopAutoSave();
        var intervalMs = Math.Max(AppSettings.Current.AutoSaveIntervalMinutes, 1) * 60_000;
        _autoSaveTimer = new Timer(
            async _ => await AutoSaveTickAsync(),
            null,
            intervalMs,
            intervalMs);
    }

    /// <summary>Stops the autosave timer. Safe to call when no timer is running.</summary>
    public void StopAutoSave()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>Stops the autosave timer and releases resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAutoSave();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Invoked each time the autosave timer fires. Calls <see cref="SaveAsync"/>
    /// and stops the timer on non-transient failures.
    /// </summary>
    private async Task AutoSaveTickAsync()
    {
        if (Mode != CheckoutMode.WriteAccess) return;

        var outcome = await SaveAsync();
        if (outcome is SaveOutcome.LockLost or SaveOutcome.SourceModified)
            StopAutoSave(); // SaveFailed event is already raised inside SaveAsync.
        // CopyError: keep the timer running — transient, worth retrying next cycle.
    }

    /// <summary>
    /// Called when <see cref="WriteLockService.WakeDetected"/> fires.
    /// Re-reads the lock file to verify this session still owns it.
    /// </summary>
    private void OnWake()
    {
        if (Mode != CheckoutMode.WriteAccess) return;

        if (!VerifyLockIsOurs())
        {
            _logger.LogInfo("CheckoutService: wake check — lock is no longer ours. Timing out session.");
            _ = HandleSessionTimeoutAsync();
        }
        else
        {
            _logger.LogInfo("CheckoutService: wake check — lock confirmed. Renewing heartbeat.");
            _lockService.ForceRenewHeartbeat();
        }
    }

    /// <summary>Discards D' and raises <see cref="SessionTimedOut"/> on the UI thread.</summary>
    private async Task HandleSessionTimeoutAsync()
    {
        await DiscardAndGoReadonlyAsync();
        _dispatch(() => SessionTimedOut?.Invoke());
    }

    /// <summary>
    /// Returns true when the lock file at D.lock still contains this session's
    /// <see cref="WriteLockService.SessionGuid"/>.
    /// </summary>
    private bool VerifyLockIsOurs()
    {
        var lockPath = Path.ChangeExtension(SourcePath, ".lock");
        if (!File.Exists(lockPath)) return false;
        try
        {
            var json = File.ReadAllText(lockPath);
            var data = JsonSerializer.Deserialize<LockFileData>(json);
            return data?.SessionGuid == _lockService.SessionGuid;
        }
        catch { return false; }
    }

    /// <summary>
    /// Copies D' to a timestamped backup file in the configured backup folder.
    /// Non-throwing — a backup failure is logged but does not abort the save.
    /// </summary>
    private void TakePreSaveBackup()
    {
        var folder = AppSettings.Current.BackupFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        try
        {
            var dbName    = Path.GetFileNameWithoutExtension(SourcePath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var dest      = Path.Combine(folder, $"{dbName}_{timestamp}.db");
            CopyWithSharing(WorkingPath, dest);
        }
        catch (Exception ex)
        {
            _logger.LogInfo($"CheckoutService: pre-save backup failed (non-critical): {ex.Message}");
        }
    }

    /// <summary>Writes a dirty marker file alongside D' to track ungraceful exits.</summary>
    private void WriteDirtyMarker()
    {
        try { File.WriteAllText(WorkingPath + ".dirty", DateTime.UtcNow.ToString("O")); }
        catch { }
    }

    /// <summary>Deletes the dirty marker file. Non-throwing.</summary>
    private void DeleteDirtyMarker()
    {
        try { File.Delete(WorkingPath + ".dirty"); } catch { }
    }

    /// <summary>
    /// Derives a stable, user-invisible path for D' from the source path.
    /// Uses the first 8 hex chars of SHA-256(normalizedSourcePath) as a prefix
    /// to avoid collisions between databases with the same filename in different folders.
    /// </summary>
    /// <param name="sourcePath">Absolute path to D.</param>
    /// <returns>Absolute path for D' under <see cref="_workingDir"/>.</returns>
    private string ComputeWorkingPath(string sourcePath)
    {
        var normalized = Path.GetFullPath(sourcePath).ToLowerInvariant();
        var hashBytes  = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var shortHash  = Convert.ToHexString(hashBytes)[..8];
        var dbFileName = Path.GetFileName(sourcePath);
        return Path.Combine(_workingDir, $"{shortHash}_{dbFileName}");
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as an uppercase hex string.
    /// Opens with <see cref="FileShare.ReadWrite"/> so the hash can be computed even
    /// while a <c>DatabaseContext</c> (or any other process) holds the file open for
    /// writing — which is the normal state for both D (source) and D' (working copy)
    /// throughout a checkout session.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to hash.</param>
    /// <returns>Uppercase hex SHA-256 digest, e.g. "3A4B…".</returns>
    /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
    private static string ComputeHash(string filePath)
    {
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="dest"/>, opening the
    /// source with <see cref="FileShare.ReadWrite"/> so the copy succeeds even while
    /// a <c>DatabaseContext</c> holds the source file open — the common case for both
    /// D (being checked out) and D' (being saved back to D).
    /// </summary>
    /// <param name="source">Path of the file to read.</param>
    /// <param name="dest">Path of the file to create or overwrite.</param>
    /// <exception cref="IOException">Thrown when either file cannot be accessed.</exception>
    private static void CopyWithSharing(string source, string dest)
    {
        using var src = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var dst = new FileStream(
            dest, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }
}

/// <summary>Indicates whether this app instance holds the write lock.</summary>
public enum CheckoutMode
{
    /// <summary>Lock acquired; all edits go to D'.</summary>
    WriteAccess,
    /// <summary>Another instance holds the lock; this instance reads D directly.</summary>
    ReadOnly
}

/// <summary>Result of a <see cref="CheckoutService.CheckoutAsync"/> call.</summary>
public enum CheckoutOutcome
{
    /// <summary>Lock acquired and D' is ready. Pass <see cref="CheckoutService.WorkingPath"/> to <c>InitializeServices</c>.</summary>
    WriteAccess,
    /// <summary>A fresh lock exists. Pass <see cref="CheckoutService.SourcePath"/> to <c>InitializeServices</c> (read-only).</summary>
    ReadOnly,
    /// <summary>A stale lock exists. Prompt the user, then call <see cref="CheckoutService.ForceCheckoutAsync"/> if confirmed.</summary>
    StaleHolder,
    /// <summary>Fatal error (hash mismatch or copy failure). Do not proceed.</summary>
    Failed
}

/// <summary>Result of a <see cref="CheckoutService.SaveAsync"/> call.</summary>
public enum SaveOutcome
{
    /// <summary>D' was successfully written to D.</summary>
    Success,
    /// <summary>The lock file no longer names this session — another user took over.</summary>
    LockLost,
    /// <summary>D was modified externally since checkout — save aborted.</summary>
    SourceModified,
    /// <summary>The D.tmp copy or rename failed (transient error — retry is appropriate).</summary>
    CopyError,
    /// <summary>Save was called while not in write mode.</summary>
    NotInWriteMode
}

using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SchedulingAssistant.Services;

/// <summary>
/// Manages the checkout / save lifecycle for every database the app opens.
///
/// <para><b>Write-access mode:</b> When a database D is opened and the write lock is
/// available, <see cref="CheckoutAsync"/> acquires the lock, copies D to a local working
/// copy D' under <c>%AppData%\TermPoint\working\</c>, and returns
/// <see cref="CheckoutOutcome.WriteAccess"/>. <c>DatabaseContext</c> targets D' — never
/// D directly. An explicit <see cref="SaveAsync"/> call pushes D' back to D.</para>
///
/// <para><b>Read-only mode:</b> When another instance already holds the write lock,
/// <see cref="CheckoutAsync"/> copies D to a local read-only snapshot D'' (same working
/// directory, "_ro" suffix) and returns <see cref="CheckoutOutcome.ReadOnly"/>.
/// <c>DatabaseContext</c> targets D'' — again, never D directly. The user can call
/// <see cref="RefreshReadOnlySnapshotAsync"/> to re-copy D → D'' and see the latest data.
/// D'' is deleted on <see cref="ReleaseAsync"/>; every open is a fresh copy from D.</para>
///
/// <para><b>Always-on:</b> Both modes apply to every database, whether it is on a
/// network drive or a local drive. The indirection through D' or D'' is what prevents any
/// instance from ever holding D open, allowing the writer's atomic rename (D.tmp → D)
/// to succeed.</para>
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
    /// Optional reference to the application's <see cref="BackupService"/>, used to take
    /// a pre-save DB snapshot before writing D' → D. Set after DI initialisation via
    /// <see cref="SetBackupService"/> because <see cref="BackupService"/> requires a live
    /// <see cref="IDatabaseContext"/> that does not exist until after checkout completes.
    /// </summary>
    private BackupService? _backupService;

    /// <summary>
    /// Dispatcher used to raise events on the UI thread. In production this is
    /// <c>Dispatcher.UIThread.Post</c>; in unit tests it is typically <c>a => a()</c>
    /// for synchronous, immediate dispatch.
    /// </summary>
    private readonly Action<Action> _dispatch;

    /// <summary>
    /// Directory in which working copies D' (write-access) and D'' (read-only snapshots)
    /// are stored. Set at construction time. Defaults to <c>%AppData%\TermPoint\working\</c>;
    /// overridden in tests to use an isolated temporary directory.
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
    /// Path to the local working copy: D' in write-access mode, D'' in read-only mode.
    /// All SQLite operations (reads and writes) target this path — D is never opened directly.
    /// Equals <see cref="SourcePath"/> for new databases (degenerate write mode only).
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
    /// True when D' (write-access mode) contains changes that have not yet been saved
    /// back to D. Set to true immediately after a successful write-access checkout.
    /// Cleared after each successful <see cref="SaveAsync"/>.
    /// Always false in read-only mode — D'' is a read-only snapshot and is never written to.
    /// </summary>
    public bool SessionDirty { get; private set; }

    /// <summary>
    /// Identity of the process (user, machine) holding the write lock when this instance is in
    /// read-only mode. Null in write mode or before checkout.
    /// </summary>
    public LockFileData? CurrentHolder { get; private set; }

    /// <summary>
    /// True when this instance is in read-only mode using a D'' (local snapshot) working copy.
    /// False in write-access mode.
    /// <para>
    /// When true, <see cref="RefreshReadOnlySnapshotAsync"/> can be called to re-copy D → D''.
    /// </para>
    /// </summary>
    public bool IsReadOnlyMode { get; private set; }

    /// <summary>
    /// When <see cref="IsReadOnlyMode"/> is true, the path to D'' (the local read-only snapshot).
    /// When in write mode, this is null.
    /// </summary>
    public string? ReadOnlyWorkingPath { get; private set; }

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
    /// Directory in which working copies D' (write-access) and read-only snapshots D''
    /// are stored. Defaults to <c>%AppData%\TermPoint\working\</c> when null. Override
    /// in unit tests to use an isolated temporary directory and avoid polluting the real
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
    /// Wires up the <see cref="BackupService"/> so that <see cref="SaveAsync"/> can
    /// delegate pre-save snapshots to it. Called from <c>MainWindow.SetupMainWindowAsync</c>
    /// after the DI container is built and the backup session has started.
    /// </summary>
    /// <param name="backupService">The application-wide backup service instance.</param>
    public void SetBackupService(BackupService backupService) =>
        _backupService = backupService;

    /// <summary>
    /// Opens a database session. In write mode, acquires the write lock and copies D → D'.
    /// In read-only mode, copies D → D'' via <see cref="SetupReadOnlySnapshotAsync"/> so
    /// that D is never held open directly.
    /// Must be called before <see cref="App.InitializeServices"/> so that the correct
    /// working path (D' or D'') can be passed in.
    /// </summary>
    /// <param name="sourcePath">Full path to D — the database the user selected.</param>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> — lock acquired; use <see cref="WorkingPath"/> (= D') as dbPath.<br/>
    /// <see cref="CheckoutOutcome.ReadOnly"/>    — a live lock exists; use <see cref="WorkingPath"/> (= D'') as dbPath.<br/>
    /// <see cref="CheckoutOutcome.StaleHolder"/> — a stale lock exists; prompt the user, then call <see cref="ForceCheckoutAsync"/> or <see cref="SetupReadOnlySnapshotAsync"/>.<br/>
    /// <see cref="CheckoutOutcome.Failed"/>      — fatal error (e.g., copy hash mismatch); caller should fall back via <see cref="SetupReadOnlySnapshotAsync"/> to open read-only.
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
        _lockService.TryAcquire(sourcePath);

        if (_lockService.IsStaleLock)
        {
            // Pre-compute D' so ForceCheckoutAsync can use WorkingPath immediately.
            WorkingPath   = ComputeWorkingPath(sourcePath);
            CurrentHolder = _lockService.CurrentHolder;
            return CheckoutOutcome.StaleHolder;
        }

        if (!_lockService.IsWriter)
        {
            // Read-only mode: set up D'' via the shared helper so D is never held open.
            CurrentHolder = _lockService.CurrentHolder;
            var d2 = await SetupReadOnlySnapshotAsync();
            return d2 is not null ? CheckoutOutcome.ReadOnly : CheckoutOutcome.Failed;
        }

        // Write-access path: copy D to D' (existing logic).
        WorkingPath = ComputeWorkingPath(sourcePath);

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
            try { CopyWithSharing(sourcePath, WorkingPath); } catch { /* hash check on next line catches this */ }
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

        // ── Step 4: Copy D' → D.tmp via the SQLite Online Backup API ────────
        // BackupSqliteDatabase coordinates with the SQLite engine rather than
        // copying raw bytes. This guarantees a consistent snapshot even if a
        // write transaction is mid-commit on the DatabaseContext connection
        // (something a raw File.Copy cannot guarantee).
        var tmpPath = SourcePath + ".tmp";
        try
        {
            BackupSqliteDatabase(WorkingPath, tmpPath);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to write to database location: {ex.Message}";
            _logger.LogError(ex, "CheckoutService: D.tmp copy failed");
            _dispatch(() => SaveFailed?.Invoke(msg));
            return SaveOutcome.CopyError;
        }

        // ── Step 5: Hash D.tmp for post-save conflict detection ──────────────
        // BackupDatabase produces a semantically identical but not byte-identical
        // copy of D' (SQLite increments page-level counters in the destination).
        // We therefore hash D.tmp itself — the file that will become the new D —
        // so that HashAtCheckout matches D after the rename and conflict detection
        // on the next save works correctly.
        var newSourceHash = ComputeHash(tmpPath);

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
        HashAtCheckout = newSourceHash;
        SessionDirty   = false;
        DeleteDirtyMarker();

        if (releaseLockAfter)
            _lockService.Release();

        _logger.LogInfo("CheckoutService: Save completed successfully.");
        _dispatch(() => SaveCompleted?.Invoke());
        return SaveOutcome.Success;
    }

    /// <summary>
    /// For read-only instances, re-copies D → D'' to fetch the latest data.
    /// Re-verifies the copy hash on success. Includes a 500ms delay before reading D
    /// to account for SMB client-side caching.
    ///
    /// <para>Because D'' may be held open by <c>DatabaseContext</c>, the caller must close
    /// the connection before the overwrite and reopen it after. Use the two callbacks for this:
    /// <paramref name="beforeOverwrite"/> (close the connection) and
    /// <paramref name="afterOverwrite"/> (reopen the connection).</para>
    /// </summary>
    /// <returns>
    /// <see cref="RefreshOutcome.Updated"/> — D'' was successfully re-copied from D.<br/>
    /// <see cref="RefreshOutcome.SourceUnavailable"/> — D could not be reached or the copy hash never matched; D'' is unchanged.
    /// </returns>
    /// <param name="beforeOverwrite">
    /// Optional async callback invoked after the new copy is verified but before D'' is overwritten.
    /// Use this to close the <c>DatabaseContext</c> connection so the overwrite succeeds without a
    /// sharing violation (e.g. call <c>DatabaseContext.CloseConnection()</c>).
    /// </param>
    /// <param name="afterOverwrite">
    /// Optional async callback invoked after D'' has been atomically overwritten with the fresh copy.
    /// Use this to reopen the <c>DatabaseContext</c> connection
    /// (e.g. call <c>DatabaseContext.ReinitializeConnection(WorkingPath)</c>).
    /// Only called on success; never called when <see cref="RefreshOutcome.SourceUnavailable"/> is returned.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown when not in read-only mode or <see cref="ReadOnlyWorkingPath"/> is null.</exception>
    public async Task<RefreshOutcome> RefreshReadOnlySnapshotAsync(
        Func<Task>? beforeOverwrite = null,
        Func<Task>? afterOverwrite  = null)
    {
        if (Mode != CheckoutMode.ReadOnly || !IsReadOnlyMode || ReadOnlyWorkingPath is null)
            throw new InvalidOperationException("RefreshReadOnlySnapshot called while not in read-only snapshot mode.");

        // Add SMB cache delay before reading D to ensure we get fresh data.
        const int baseDelayMs = 500;
        const int jitterMs    = 500;
        await Task.Delay(baseDelayMs + Random.Shared.Next(0, jitterMs));

        var tmpPath = ReadOnlyWorkingPath + ".tmp";

        // Attempt up to 3 times in case D is modified mid-copy (hash mismatch).
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string sourceHash;
            try { sourceHash = ComputeHash(SourcePath); }
            catch (Exception ex)
            {
                _logger.LogInfo($"CheckoutService: could not read D for refresh: {ex.Message}");
                return RefreshOutcome.SourceUnavailable;
            }

            try { CopyWithSharing(SourcePath, tmpPath); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: failed to copy D to D''.tmp during refresh");
                try { File.Delete(tmpPath); } catch { }
                return RefreshOutcome.SourceUnavailable;
            }

            var copyHash = ComputeHash(tmpPath);
            if (copyHash == sourceHash)
            {
                // Close the DatabaseContext connection before overwriting D''.
                if (beforeOverwrite is not null)
                {
                    try { await beforeOverwrite(); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "CheckoutService: beforeOverwrite callback threw during refresh");
                        try { File.Delete(tmpPath); } catch { }
                        return RefreshOutcome.SourceUnavailable;
                    }
                }

                try
                {
                    File.Move(tmpPath, ReadOnlyWorkingPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CheckoutService: failed to rename D''.tmp during refresh");
                    try { File.Delete(tmpPath); } catch { }
                    return RefreshOutcome.SourceUnavailable;
                }

                HashAtCheckout = sourceHash;

                // Reopen the DatabaseContext connection to the now-updated D''.
                if (afterOverwrite is not null)
                {
                    try { await afterOverwrite(); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "CheckoutService: afterOverwrite callback threw during refresh");
                        return RefreshOutcome.SourceUnavailable;
                    }
                }

                _logger.LogInfo("CheckoutService: read-only refresh — D'' updated successfully.");
                return RefreshOutcome.Updated;
            }

            // Hash mismatch — D was likely being written during copy. Retry.
            _logger.LogInfo($"CheckoutService: refresh hash mismatch on attempt {attempt + 1}/3 — retrying.");
            try { File.Delete(tmpPath); } catch { }

            if (attempt < 2)
                await Task.Delay(100 * (attempt + 1));
        }

        _logger.LogInfo("CheckoutService: refresh failed — hash mismatch after 3 retries.");
        return RefreshOutcome.SourceUnavailable;
    }

    /// <summary>
    /// Detects whether a previous session for <paramref name="sourcePath"/> ended
    /// ungracefully with unsaved changes. Call at startup before
    /// <see cref="CheckoutAsync"/>.
    /// </summary>
    /// <param name="sourcePath">The database path the user is about to open.</param>
    /// <returns>
    /// True when both D' and its dirty marker exist, indicating unfinished work.
    /// Ignores read-only snapshots (D'' files with "_ro" suffix) since they never have dirty markers.
    /// </returns>
    public bool DetectCrashRecovery(string sourcePath)
    {
        var workingPath  = ComputeWorkingPath(sourcePath);
        var dirtyMarker  = workingPath + ".dirty";
        var hasCrash     = File.Exists(dirtyMarker) && File.Exists(workingPath);

        // Sanity check: also verify that WorkingPath doesn't contain "_ro" (read-only snapshot).
        // Read-only snapshots never have dirty markers, so this should never happen in practice,
        // but defend against it just in case.
        if (hasCrash && workingPath.Contains("_ro"))
        {
            _logger.LogInfo("CheckoutService: detected _ro path with dirty marker — skipping (invalid state).");
            return false;
        }

        return hasCrash;
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

        // Clean up the working copy (D' or D'') on a clean shutdown.
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
    /// Delegates to <see cref="BackupService.TakeDbSnapshot"/> when a backup service
    /// has been wired up via <see cref="SetBackupService"/>. No-op otherwise.
    /// Non-throwing — a backup failure inside the service is logged there and does not
    /// propagate to the caller.
    /// </summary>
    private void TakePreSaveBackup() => _backupService?.TakeDbSnapshot();

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
    /// Creates the read-only snapshot D'' for the current <see cref="SourcePath"/> by copying
    /// D → D'', without touching the write lock. Sets <see cref="ReadOnlyWorkingPath"/>,
    /// <see cref="WorkingPath"/>, <see cref="IsReadOnlyMode"/>, <see cref="HashAtCheckout"/>,
    /// and <see cref="Mode"/> on success.
    ///
    /// <para>This method is called by <see cref="CheckoutAsync"/> when another writer holds the
    /// lock, and also as a fallback when the user declines a stale-lock takeover, when a
    /// force-checkout loses the race, or when the initial checkout fails — ensuring D'' is
    /// always used instead of opening D directly.</para>
    /// </summary>
    /// <returns>
    /// The path to D'' on success (same as <see cref="ReadOnlyWorkingPath"/>).
    /// <c>null</c> if D'' cannot be created (e.g. D is inaccessible or the copy hash never
    /// matches); the caller should fall back to opening <see cref="SourcePath"/> directly.
    /// </returns>
    public Task<string?> SetupReadOnlySnapshotAsync()
    {
        ReadOnlyWorkingPath = ComputeReadOnlyWorkingPath(SourcePath);
        Directory.CreateDirectory(_workingDir);
        var roTmpPath = ReadOnlyWorkingPath + ".tmp";

        try { CopyWithSharing(SourcePath, roTmpPath); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: failed to copy D to D''.tmp");
            try { File.Delete(roTmpPath); } catch { }
            return Task.FromResult<string?>(null);
        }

        var roSourceHash = ComputeHash(SourcePath);
        var roCopyHash   = ComputeHash(roTmpPath);
        if (roCopyHash != roSourceHash)
        {
            _logger.LogInfo("CheckoutService: D'' copy hash mismatch — retrying once.");
            try { CopyWithSharing(SourcePath, roTmpPath); } catch { }
            roCopyHash = ComputeHash(roTmpPath);

            if (roCopyHash != roSourceHash)
            {
                _logger.LogInfo("CheckoutService: D'' hash mismatch on retry — aborting.");
                try { File.Delete(roTmpPath); } catch { }
                return Task.FromResult<string?>(null);
            }
        }

        try { File.Move(roTmpPath, ReadOnlyWorkingPath, overwrite: true); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: failed to rename D''.tmp to D''");
            try { File.Delete(roTmpPath); } catch { }
            return Task.FromResult<string?>(null);
        }

        WorkingPath    = ReadOnlyWorkingPath;
        HashAtCheckout = roSourceHash;
        Mode           = CheckoutMode.ReadOnly;
        IsReadOnlyMode = true;
        _logger.LogInfo("CheckoutService: read-only setup — D'' created.");
        return Task.FromResult<string?>(ReadOnlyWorkingPath);
    }

    /// <summary>
    /// Derives a stable, user-invisible path for D'' (read-only snapshot) from the source path.
    /// Similar to <see cref="ComputeWorkingPath"/>, but appends a "_ro" suffix before the file extension
    /// to distinguish read-only snapshots from write-access working copies in the same directory.
    ///
    /// <para><b>Why separate from D'?</b> When a read-only instance is active, it holds D'' (a local snapshot).
    /// If the same user later opens the database in write mode, a new D' will be created. The "_ro" suffix
    /// ensures D'' and D' don't collide or interfere.</para>
    /// </summary>
    /// <param name="sourcePath">Absolute path to D (the source database).</param>
    /// <returns>Absolute path for D'' (read-only snapshot) under <see cref="_workingDir"/>.</returns>
    private string ComputeReadOnlyWorkingPath(string sourcePath)
    {
        var normalized = Path.GetFullPath(sourcePath).ToLowerInvariant();
        var hashBytes  = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var shortHash  = Convert.ToHexString(hashBytes)[..8];
        var dbFileName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext        = Path.GetExtension(sourcePath);
        return Path.Combine(_workingDir, $"{shortHash}_{dbFileName}_ro{ext}");
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as an uppercase hex string.
    /// Opens with <see cref="FileShare.ReadWrite"/> so the hash can be computed even
    /// while a <c>DatabaseContext</c> (or any other process) holds the file open for
    /// writing — which is the normal state for D (source), D' (write-access working copy),
    /// and D'' (read-only snapshot) throughout a checkout session.
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
    /// Creates a consistent copy of a live SQLite database at <paramref name="sourcePath"/>
    /// using the SQLite Online Backup API. Unlike a raw file copy, this coordinates with
    /// the database engine and always produces a valid snapshot regardless of concurrent
    /// write activity on the existing <c>DatabaseContext</c> connection.
    /// Use this whenever D' is the source; use <see cref="CopyWithSharing"/> when
    /// copying a file that is not being actively written by <c>DatabaseContext</c>
    /// (e.g., D → D' at checkout time, or D → D'' at snapshot time).
    /// </summary>
    /// <param name="sourcePath">Path to the source SQLite database (typically D').</param>
    /// <param name="destPath">Path for the destination file (created or overwritten).</param>
    /// <exception cref="Exception">Propagates any SQLite or I/O error to the caller.</exception>
    private static void BackupSqliteDatabase(string sourcePath, string destPath)
    {
        // Pooling=False is essential: with the default connection pool, Dispose()
        // returns connections to the pool rather than closing them, leaving the
        // destination file (D.tmp) open. File.Move(D.tmp → D) then fails with
        // "access denied" because the pool still holds the file handle.
        using var source = new SqliteConnection($"Data Source={sourcePath};Pooling=False");
        using var dest   = new SqliteConnection($"Data Source={destPath};Pooling=False");
        source.Open();
        dest.Open();
        source.BackupDatabase(dest);
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="dest"/>, opening the
    /// source with <see cref="FileShare.ReadWrite"/> so the copy succeeds even while
    /// a <c>DatabaseContext</c> holds the source file open — the common case for
    /// D (being checked out to D' or snapshotted to D'') and D' (being saved back to D).
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
    /// <summary>Another instance holds the lock; this instance reads D'' (a local read-only snapshot).</summary>
    ReadOnly
}

/// <summary>Result of a <see cref="CheckoutService.CheckoutAsync"/> call.</summary>
public enum CheckoutOutcome
{
    /// <summary>Lock acquired and D' is ready. Pass <see cref="CheckoutService.WorkingPath"/> (= D') to <c>InitializeServices</c>.</summary>
    WriteAccess,
    /// <summary>A fresh lock exists; D'' was created. Pass <see cref="CheckoutService.WorkingPath"/> (= D'') to <c>InitializeServices</c>.</summary>
    ReadOnly,
    /// <summary>A stale lock exists. Prompt the user, then call <see cref="CheckoutService.ForceCheckoutAsync"/> if confirmed.</summary>
    StaleHolder,
    /// <summary>Fatal error (hash mismatch or copy failure). Caller should attempt <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/> as a fallback.</summary>
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

/// <summary>Result of a <see cref="CheckoutService.RefreshReadOnlySnapshotAsync"/> call.</summary>
public enum RefreshOutcome
{
    /// <summary>D'' was successfully re-copied from D.</summary>
    Updated,
    /// <summary>D could not be reached or the copy hash never matched; D'' is unchanged.</summary>
    SourceUnavailable,
}

using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TermPoint.Services;

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
/// <para><b>New databases:</b> When D does not yet exist, there is nothing to copy.
/// <c>WorkingPath</c> is set to <c>SourcePath</c> directly — there is no separate D';
/// the file <c>DatabaseContext</c> creates <i>is</i> the authoritative database.
/// <see cref="SaveAsync"/> is a no-op in this mode because every write already targets
/// the final location.</para>
///
/// <para><b>Thread safety:</b> <see cref="SaveAsync"/> is designed to be called from
/// background threads (e.g., the autosave timer). All events are raised on the Avalonia
/// UI thread via <c>Dispatcher.UIThread.Post</c>.</para>
///
/// <para><b>Testability:</b> Constructor parameters allow injecting an isolated
/// <see cref="WriteLockService"/>, <see cref="IAppLogger"/>, a synchronous dispatcher,
/// and a custom working directory. Production code passes <c>App.LockService</c> and
/// <c>App.Logger</c>; unit tests supply their own isolated instances.</para>
///
/// <para><b>Lock-loss scenarios — "Somehow, write access disappears out from under us":</b>
/// This could possibly happen, for example, if a writer's network access goes down
/// for a little while, another user starts up, the app notices the stale lock file
/// and gives the second user write access. Now when the network comes back up
/// the write access is gone. Other possibilities include someone accidently deleting 
/// or corrupting the lock file.
/// 
/// The app can discover it no longer owns the write lock in three ways. In all
/// three cases the session is demoted to read-only in place (without rebuilding
/// the view-model) and a banner is shown instructing the user to exit and
/// restart if they want to bid for write access again. The sequence is:
/// <see cref="WriteLockLost"/> raised → MainWindow handler calls
/// <see cref="DemoteToReadOnlyAsync"/> → banner surfaced → reader polling
/// resumes so a later clean release by the new holder can still offer an
/// in-app takeover prompt without a restart.</para>
///
/// <list type="number">
///   <item><b>Save-time verification.</b> <see cref="SaveAsync"/> calls
///         <see cref="VerifyLockIsOurs"/> before writing D' → D. If the lock
///         file is missing or its <c>SessionGuid</c> doesn't match ours, the
///         save is aborted and <see cref="HandleLockLossAsync"/> fires.
///         Triggers: lock file manually deleted, or contents rewritten by
///         another process.</item>
///   <item><b>Wake-from-sleep check.</b> <see cref="WriteLockService.WakeDetected"/>
///         fires when the 30-second tick gap exceeds 90 s — a clear
///         "we were asleep" signal. <see cref="OnWake"/> verifies the lock on a
///         background thread (the SMB reconnect can take 30 s; doing it on the
///         UI thread froze the window) and, if we no longer own it, fires the
///         same <see cref="HandleLockLossAsync"/> path. Triggers: another
///         user took over via stale-lock prompt while this machine slept.</item>
///   <item><b>Heartbeat renewal failure.</b> After <see cref="WriteLockService.HeartbeatFailureThreshold"/>
///         consecutive failures, <see cref="WriteLockService.HeartbeatFailed"/> fires.
///         <see cref="OnHeartbeatFailed"/> calls <see cref="VerifyLockIsOursAsync"/> to
///         distinguish "share unreachable" (keep writer state, show transient warning)
///         from "lock genuinely taken" (demote via <see cref="HandleLockLossAsync"/>).
///         This matches the save-time and wake-from-sleep paths, which already make
///         this distinction, and prevents false-positive demotions during transient
///         network interruptions. (F11, 2026-05-04; W1, 2026-06-24.)</item>
/// </list>
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
    /// Directory in which working copies D' (write-access) and D'' (read-only snapshots)
    /// are stored. Set at construction time. Defaults to <c>%AppData%\TermPoint\working\</c>;
    /// overridden in tests to use an isolated temporary directory.
    /// </summary>
    private readonly string _workingDir;

    // ── State ──────────────────────────────────────────────────────────────────

    private Timer?  _autoSaveTimer;
    private bool    _disposed;

    /// <summary>
    /// 0 = idle, 1 = a save is currently in flight. Mutated only via
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so concurrent
    /// callers (UI button + autosave timer + ReleaseAsync) cannot pass the
    /// "is a save running?" gate simultaneously.
    /// </summary>
    private int     _saveInFlight;

    /// <summary>
    /// 0 = idle, 1 = a heartbeat-failure verification is in flight. Prevents stacking
    /// verification attempts if <see cref="WriteLockService.HeartbeatFailed"/> fires again
    /// while an earlier <see cref="VerifyLockIsOursAsync"/> call is still awaiting the
    /// network. Mutated via <see cref="Interlocked.CompareExchange(ref int, int, int)"/>.
    /// </summary>
    private int     _heartbeatVerifyInFlight;

    /// <summary>
    /// Hash of the D.tmp snapshot from a save whose step-5 rename was never confirmed
    /// (timed out or threw). The abandoned <c>File.Move</c> thread can still complete
    /// the rename seconds later — the "ghost rename" — leaving D updated while
    /// <see cref="HashAtCheckout"/> is stale. Without this field, every subsequent save
    /// would misread that state as an external modification (sticky
    /// <see cref="SaveOutcome.SourceModified"/>, autosave stopped). Step 2 of
    /// <see cref="SaveAsyncCore"/> compares a mismatched source hash against this value
    /// and, on a match, adopts the ghost save as successful and continues.
    /// Set by <see cref="RecordPendingSaveHash"/>, cleared on checkout and on every
    /// confirmed save. Also persisted into the dirty marker (see
    /// <see cref="Models.DirtyMarkerData.PendingSaveHash"/>) so recovery after a
    /// restart can recognize a post-exit ghost.
    /// </summary>
    private string? _pendingSaveHash;

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
    /// True when the last write-access checkout took over the lock from a holder whose process
    /// was provably dead (a crashed previous session on this machine). The startup path reads
    /// this to show a "recovered from a previous session" notice. Pass-through of
    /// <see cref="WriteLockService.ReclaimedDeadSession"/>, snapshotted at checkout time.
    /// </summary>
    public bool ReclaimedDeadSession { get; private set; }

    /// <summary>
    /// True when this instance went read-only because the lock is held by another
    /// <b>still-running instance on the same machine</b>. Lets the read-only banner explain
    /// that a second window is already editing this database. Pass-through of
    /// <see cref="WriteLockService.HolderIsLiveSameMachine"/>, snapshotted at checkout time.
    /// </summary>
    public bool HolderIsLiveSameMachine { get; private set; }

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

    /// <summary>
    /// Identity of the new lock holder when write access was lost to a takeover
    /// (another user overwrote the lock file with their own <c>SessionGuid</c>).
    /// Populated by <see cref="VerifyLockIsOursOnceAsync"/> when it reads a foreign
    /// GUID from the lock file. Null when the lock file was simply deleted (no new
    /// holder) or when no lock loss has occurred.
    ///
    /// <para>Read by <c>MainWindow.OnWriteLockLost</c> to build a cause-specific
    /// banner that names the user who took over.</para>
    /// </summary>
    public LockFileData? LockLossNewHolder { get; private set; }

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when this session is found to no longer own the
    /// write lock. Fired from every lock-loss detection path — see the
    /// "Lock-loss scenarios" section on <see cref="CheckoutService"/> for the
    /// full list (save-time verification, wake-from-sleep check, …).
    ///
    /// <para>The <see cref="WriteLockLostReason"/> parameter tells the handler
    /// whether the loss was a takeover by another user or an external lock-file
    /// deletion. The handler can also read <see cref="LockLossNewHolder"/> for the
    /// new holder's identity (non-null only for <see cref="WriteLockLostReason.TakenOver"/>).</para>
    ///
    /// <para>The handler (<c>MainWindow.OnWriteLockLost</c>) is expected to
    /// surface a cause-specific banner, then call <see cref="DemoteToReadOnlyAsync"/>
    /// to transition into read-only mode and reload the panels from the fresh
    /// D'' snapshot. Unsaved changes in D' are lost by design — the alternative
    /// would be to trample whoever now holds the lock.</para>
    /// </summary>
    public event Action<WriteLockLostReason>? WriteLockLost;

    /// <summary>
    /// Raised on the UI thread after each successful <see cref="SaveAsync"/> call.
    /// The UI can use this to show a brief "Saved" indicator.
    /// </summary>
    public event Action? SaveCompleted;

    /// <summary>
    /// Raised on the UI thread when a save attempt begins (after the
    /// <see cref="_saveInFlight"/> gate is passed). Drives the "Saving…" indicator:
    /// with chunked stall-aware transfers, a large database on a slow link can
    /// legitimately save for tens of seconds, and without feedback that is
    /// indistinguishable from a dead Save button. Always followed by exactly one
    /// <see cref="SaveFinished"/>, whatever the outcome.
    /// </summary>
    public event Action? SaveStarted;

    /// <summary>
    /// Raised on the UI thread when a save attempt ends — success or any failure.
    /// Deliberately independent of <see cref="SaveCompleted"/>/<see cref="SaveFailed"/>:
    /// some outcomes (e.g. <see cref="SaveOutcome.LockLost"/>) raise neither, and the
    /// "Saving…" indicator must never be left stuck on.
    /// </summary>
    public event Action? SaveFinished;

    /// <summary>
    /// Raised on the UI thread when a <see cref="SaveAsync"/> call is skipped because
    /// another save is already in flight. Without it the skipped click is silent and
    /// indistinguishable from a dead Save button — the field-test failure mode where
    /// the user clicks Save repeatedly during a long post-outage save. The UI reacts
    /// with a transient "A save is already in progress…" notice; no state changes.
    /// </summary>
    public event Action? SaveAlreadyInProgress;

    /// <summary>
    /// Raised on the UI thread the first time the database is written to after a
    /// checkout or save. The UI can use this to show an "Unsaved changes" indicator.
    /// </summary>
    public event Action? BecameDirty;

    /// <summary>
    /// Raised <b>synchronously</b> on the calling thread, inside step 7 of
    /// <c>SaveAsyncCore</c>, immediately before the dirty marker file is deleted.
    /// <para><see cref="Data.IDatabaseContext.ResetDirty"/> subscribes to this so that
    /// <c>_dirtyFired</c> is reset to 0 BEFORE the marker is removed. Any user-initiated
    /// write that arrives in the very small window between reset and delete will be able
    /// to re-fire <c>MarkDirty</c> (CAS 0→1 succeeds), giving the post-save edit a chance
    /// to re-write the marker. This closes the F2 race documented in
    /// <c>data-integrity-agenda.md</c>: the previous wiring (via the post-step-7
    /// <see cref="SaveCompleted"/> event, dispatched to the UI thread) could leave a
    /// hundred-millisecond gap during which writes silently failed to re-arm the marker,
    /// causing crash recovery to misreport "clean exit" and discard real edits.</para>
    /// </summary>
    public event Action? BeforeDirtyMarkerDeleted;

    /// <summary>
    /// Raised on the UI thread when <see cref="SaveAsync"/> fails.
    /// <para>The <c>string</c> parameter is a human-readable description suitable for
    /// display to the user. The <c>bool</c> parameter is <c>autoDismiss</c>: when
    /// <c>true</c> the error is transient and the banner should clear itself after a
    /// short delay (a background autosave will retry on its own); when <c>false</c>
    /// the error is sticky and requires user attention (e.g. lock lost, source
    /// modified, or a failed manual save).</para>
    /// Autosave stops when this is raised for <see cref="SaveOutcome.LockLost"/>
    /// or <see cref="SaveOutcome.SourceModified"/>; it continues on
    /// <see cref="SaveOutcome.CopyError"/> (transient — worth retrying).
    /// </summary>
    public event Action<string, bool>? SaveFailed;

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
    /// Opens a database session. In write mode, acquires the write lock and copies D → D'.
    /// In read-only mode, copies D → D'' via <see cref="SetupReadOnlySnapshotAsync"/> so
    /// that D is never held open directly.
    /// Must be called before <see cref="App.InitializeServices"/> so that the correct
    /// working path (D' or D'') can be passed in.
    /// </summary>
    /// <param name="sourcePath">Full path to D — the database the user selected.</param>
    /// <param name="recoverWorkingCopy">
    /// When true, an existing D' left behind by a crashed session is adopted as the
    /// working copy instead of being overwritten by a fresh D → D' copy. Recovery is
    /// only completed when D's current hash still matches the hash recorded in the
    /// dirty marker (proof no other writer touched D since the crash); otherwise
    /// <see cref="CheckoutOutcome.RecoveryConflict"/> is returned and the caller
    /// should offer to export the unsaved changes. See <see cref="InspectCrashRecovery"/>.
    /// </param>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> — lock acquired; use <see cref="WorkingPath"/> (= D') as dbPath.<br/>
    /// <see cref="CheckoutOutcome.ReadOnly"/>    — a live lock exists; use <see cref="WorkingPath"/> (= D'') as dbPath.<br/>
    /// <see cref="CheckoutOutcome.StaleHolder"/> — a stale lock exists; prompt the user, and on the basis of user's decision  call <see cref="ForceCheckoutAsync"/> or <see cref="SetupReadOnlySnapshotAsync"/>.<br/>
    /// <see cref="CheckoutOutcome.RecoveryConflict"/> — recovery was requested but cannot be applied safely; D' and its marker are left intact for export.<br/>
    /// <see cref="CheckoutOutcome.Failed"/>      — fatal error (e.g., copy hash mismatch); caller should fall back via <see cref="SetupReadOnlySnapshotAsync"/> to open read-only.
    /// </returns>
    public async Task<CheckoutOutcome> CheckoutAsync(string sourcePath, bool recoverWorkingCopy = false)
    {
        SourcePath = sourcePath;
        LockLossNewHolder = null;
        _pendingSaveHash  = null;   // fresh session — no unconfirmed rename outstanding

        // Tri-state probe: distinguishes "location answered, file absent" (Missing —
        // safe to take the new-database path) from any network failure (Unreachable —
        // fast or slow). A plain exists-check would misread a fast-failing dead share
        // as a brand-new database and try to create a .lock at an unreachable path.
        var probe = await NetworkFileOps.ProbeFileAsync(sourcePath);

        if (probe == FileProbeResult.Unreachable)
        {
            _logger.LogInfo("CheckoutService: source probe failed — network unreachable");
            _logger.LogBreadcrumb("Checkout: NetworkUnreachable", new() { ["path"] = sourcePath });
            return CheckoutOutcome.NetworkUnreachable;
        }

        var exists = probe == FileProbeResult.Exists;

        // Recovery requested but D itself is gone (deleted or renamed while the
        // unsaved changes were stranded). There is no source to verify against, so
        // automatic recovery is off the table — surface the conflict and let the
        // caller offer an exported copy of the unsaved changes instead.
        if (!exists && recoverWorkingCopy)
        {
            _logger.LogBreadcrumb("Checkout: RecoveryConflict (source missing)", new() { ["path"] = sourcePath });
            return CheckoutOutcome.RecoveryConflict;
        }

        // ── New-database shortcut ──────────────────────────────────────────────
        // D does not exist yet — there is nothing to copy. WorkingPath is set to
        // SourcePath directly; there is no separate D'. DatabaseContext creates the
        // schema at this path, so every write targets the final location. SaveAsync
        // is a no-op in this mode because there is no D' → D copy-back to perform.
        if (!exists)
        {
            WorkingPath     = sourcePath;
            HashAtCheckout  = null;
            Mode            = CheckoutMode.WriteAccess;
            SessionDirty    = true;
            CurrentHolder   = null;

            var lockCompleted = await NetworkFileOps.RunAsync(
                () => _lockService.TryAcquire(sourcePath), "lock acquire (new DB)");

            if (!lockCompleted)
            {
                _logger.LogInfo("CheckoutService: lock acquire timed out — network unreachable");
                return CheckoutOutcome.NetworkUnreachable;
            }

            if (!_lockService.IsWriter)
            {
                // Extremely unlikely — someone locked a file that doesn't exist yet.
                Mode          = CheckoutMode.ReadOnly;
                CurrentHolder = _lockService.CurrentHolder;
                return CheckoutOutcome.ReadOnly;
            }

            _lockService.WakeDetected += OnWake;
            _logger.LogBreadcrumb("Checkout: WriteAccess (new DB)", new() { ["path"] = sourcePath });
            return CheckoutOutcome.WriteAccess;
        }

        // ── License-gated read-only mode ────────────────────────────────────
        // If the license evaluation determined ReadOnly access (unlicensed, expired,
        // or trial elapsed), force read-only mode regardless of lock availability.
        if (App.LicenseStatus.AccessLevel == Licensing.AccessLevel.ReadOnly)
        {
            CurrentHolder = null;
            var d2License = await SetupReadOnlySnapshotAsync();
            var outcome = d2License is not null ? CheckoutOutcome.ReadOnly : CheckoutOutcome.Failed;
            _logger.LogBreadcrumb($"Checkout: {outcome} (license: {App.LicenseStatus.Reason})", new() { ["path"] = sourcePath });
            return outcome;
        }

        // ── Voluntary reader (observer) mode ──────────────────────────────────
        // The user chose to open as an observer. Never call TryAcquire: this instance
        // must not create the .lock file (so it can never block a writer), must not poll,
        // and must not offer to take over write access. Open against a read-only snapshot
        // (D'') instead. Only meaningful when D already exists — the !exists shortcut above
        // ignores reader mode and runs the normal write path.
        if (AppSettings.Current.OpenInReaderMode)
        {
            CurrentHolder = null;   // no specific holder — read-only by the user's own choice
            var d2Reader = await SetupReadOnlySnapshotAsync();
            var outcome = d2Reader is not null ? CheckoutOutcome.ReadOnly : CheckoutOutcome.Failed;
            _logger.LogBreadcrumb($"Checkout: {outcome} (observer mode)", new() { ["path"] = sourcePath });
            return outcome;
        }

        // ── Normal path — D exists ────────────────────────────────────────────
        var lockAcquireCompleted = await NetworkFileOps.RunAsync(
            () => _lockService.TryAcquire(sourcePath), "lock acquire");

        if (!lockAcquireCompleted)
        {
            _logger.LogInfo("CheckoutService: lock acquire timed out — network unreachable");
            return CheckoutOutcome.NetworkUnreachable;
        }

        if (_lockService.IsStaleLock)
        {
            // Pre-compute D' so ForceCheckoutAsync can use WorkingPath immediately.
            WorkingPath   = ComputeWorkingPath(sourcePath);
            CurrentHolder = _lockService.CurrentHolder;
            _logger.LogBreadcrumb("Checkout: StaleHolder", new() { ["path"] = sourcePath, ["holder"] = CurrentHolder?.Username ?? "?" });
            return CheckoutOutcome.StaleHolder;
        }

        if (!_lockService.IsWriter)
        {
            // Read-only mode: set up D'' via the shared helper so D is never held open.
            CurrentHolder           = _lockService.CurrentHolder;
            HolderIsLiveSameMachine = _lockService.HolderIsLiveSameMachine;
            var d2 = await SetupReadOnlySnapshotAsync();
            var outcome = d2 is not null ? CheckoutOutcome.ReadOnly : CheckoutOutcome.Failed;
            _logger.LogBreadcrumb($"Checkout: {outcome}", new() { ["path"] = sourcePath, ["holder"] = CurrentHolder?.Username ?? "?" });
            return outcome;
        }

        // We hold the lock. Capture whether we got it by reclaiming a dead session, so the UI
        // can tell the user we recovered it. (Cleared for clean acquisitions.)
        ReclaimedDeadSession = _lockService.ReclaimedDeadSession;

        // Recovery mode: adopt the crashed session's D' instead of copying D over it.
        if (recoverWorkingCopy)
            return await FinishRecoveryCheckoutAsync();

        // Write-access path: copy D to D' (existing logic).
        WorkingPath = ComputeWorkingPath(sourcePath);

        // We hold the lock — copy D to D'.
        Directory.CreateDirectory(_workingDir);

        var (hashCompleted, sourceHash) = await NetworkFileOps.ComputeHashAsync(sourcePath);

        if (!hashCompleted)
        {
            _logger.LogInfo("CheckoutService: checkout hash timed out — network unreachable");
            _lockService.Release();
            return CheckoutOutcome.NetworkUnreachable;
        }

        HashAtCheckout = sourceHash;

        // D' is opened in WAL mode; a stale -wal/-shm sidecar from a prior session
        // paired with a freshly copied .db is a corruption hazard — remove them first.
        TryDeleteWalSidecars(WorkingPath);

        try
        {
            var copyCompleted = await NetworkFileOps.CopyAsync(sourcePath, WorkingPath);

            if (!copyCompleted)
            {
                _logger.LogInfo("CheckoutService: checkout copy timed out — network unreachable");
                _lockService.Release();
                DeleteDirtyMarker();
                return CheckoutOutcome.NetworkUnreachable;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: failed to copy D to D'");
            _lockService.Release();
            DeleteDirtyMarker();
            return CheckoutOutcome.Failed;
        }

        // Verify the copy. WorkingPath is local — no timeout needed.
        var copyHash = ComputeHash(WorkingPath);
        if (copyHash != HashAtCheckout)
        {
            _logger.LogInfo("CheckoutService: hash mismatch after copy — retrying once.");

            var retryCompleted = await NetworkFileOps.CopyAsync(sourcePath, WorkingPath);

            if (!retryCompleted)
            {
                _logger.LogInfo("CheckoutService: checkout retry copy timed out — network unreachable");
                _lockService.Release();
                DeleteDirtyMarker();
                TryDelete(WorkingPath);
                return CheckoutOutcome.NetworkUnreachable;
            }

            copyHash = ComputeHash(WorkingPath);

            if (copyHash != HashAtCheckout)
            {
                _logger.LogInfo("CheckoutService: hash mismatch on retry — aborting checkout.");
                _lockService.Release();
                DeleteDirtyMarker();
                TryDelete(WorkingPath);
                return CheckoutOutcome.Failed;
            }
        }

        Mode          = CheckoutMode.WriteAccess;
        SessionDirty  = true;
        CurrentHolder = null;

        _lockService.WakeDetected    += OnWake;
        _lockService.HeartbeatFailed += OnHeartbeatFailed;   // F11
        _logger.LogBreadcrumb("Checkout: WriteAccess", new() { ["path"] = sourcePath });
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// Breaks a stale lock and completes a checkout. Call after the user has confirmed
    /// they wish to take over the abandoned session (i.e., after
    /// <see cref="CheckoutAsync"/> returned <see cref="CheckoutOutcome.StaleHolder"/>).
    /// </summary>
    /// <param name="recoverWorkingCopy">
    /// Same semantics as on <see cref="CheckoutAsync"/>: adopt the crashed session's
    /// existing D' instead of copying D over it, verifying D against the dirty-marker
    /// hash first. Pass the same value that was passed to the preceding
    /// <see cref="CheckoutAsync"/> call.
    /// </param>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> on success.
    /// <see cref="CheckoutOutcome.RecoveryConflict"/> when recovery was requested but cannot be applied safely.
    /// <see cref="CheckoutOutcome.Failed"/> if another instance won the race.
    /// </returns>
    public async Task<CheckoutOutcome> ForceCheckoutAsync(bool recoverWorkingCopy = false)
    {
        var forceCompleted = await NetworkFileOps.RunAsync(
            () => _lockService.ForceAcquire(), "force lock acquire");

        if (!forceCompleted)
        {
            _logger.LogInfo("CheckoutService: ForceCheckout lock acquire timed out — network unreachable");
            return CheckoutOutcome.NetworkUnreachable;
        }

        if (!_lockService.IsWriter)
        {
            Mode          = CheckoutMode.ReadOnly;
            CurrentHolder = _lockService.CurrentHolder;
            return CheckoutOutcome.Failed;
        }

        // Recovery mode: adopt the crashed session's D' instead of copying D over it.
        if (recoverWorkingCopy)
            return await FinishRecoveryCheckoutAsync();

        // Lock acquired — now copy D to D' (same as CheckoutAsync write path).
        Directory.CreateDirectory(_workingDir);

        var (hashCompleted, sourceHash) = await NetworkFileOps.ComputeHashAsync(SourcePath);

        if (!hashCompleted)
        {
            _logger.LogInfo("CheckoutService: ForceCheckout hash timed out — network unreachable");
            _lockService.Release();
            return CheckoutOutcome.NetworkUnreachable;
        }

        HashAtCheckout = sourceHash;

        // Same stale-sidecar guard as CheckoutAsync (see comment there).
        TryDeleteWalSidecars(WorkingPath);

        try
        {
            var copyCompleted = await NetworkFileOps.CopyAsync(SourcePath, WorkingPath);

            if (!copyCompleted)
            {
                _logger.LogInfo("CheckoutService: ForceCheckout copy timed out — network unreachable");
                _lockService.Release();
                DeleteDirtyMarker();
                return CheckoutOutcome.NetworkUnreachable;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: ForceCheckout — failed to copy D to D'");
            _lockService.Release();
            DeleteDirtyMarker();
            return CheckoutOutcome.Failed;
        }

        // WorkingPath is local — no timeout needed.
        var copyHash = ComputeHash(WorkingPath);
        if (copyHash != HashAtCheckout)
        {
            _logger.LogInfo("CheckoutService: ForceCheckout hash mismatch — aborting.");
            _lockService.Release();
            DeleteDirtyMarker();
            TryDelete(WorkingPath);
            return CheckoutOutcome.Failed;
        }

        Mode          = CheckoutMode.WriteAccess;
        SessionDirty  = true;
        CurrentHolder = null;

        _lockService.WakeDetected    += OnWake;
        _lockService.HeartbeatFailed += OnHeartbeatFailed;   // F11
        _logger.LogBreadcrumb("ForceCheckout: WriteAccess");
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// Shared tail of <see cref="CheckoutAsync"/> and <see cref="ForceCheckoutAsync"/>
    /// for crash recovery: adopts the crashed session's existing D' as the working copy
    /// instead of overwriting it with a fresh D → D' copy.
    ///
    /// <para><b>Safety gate:</b> recovery only proceeds when D's current hash equals the
    /// hash stored in the dirty marker — proof that no other writer modified D while the
    /// unsaved changes were stranded. A missing D', an unreadable/legacy marker (no hash),
    /// or a hash mismatch all return <see cref="CheckoutOutcome.RecoveryConflict"/> with
    /// the lock released and D' + marker left intact, so the caller can offer to export
    /// the unsaved changes before discarding them.</para>
    ///
    /// <para><b>On success:</b> the session resumes exactly as a normal write checkout
    /// (mode, events, autosave eligibility), except the dirty marker is deliberately left
    /// in place — D' still differs from D until the next successful save, so the marker
    /// remains an accurate "unsaved changes exist" signal.</para>
    ///
    /// <para>Precondition: the caller has already acquired the write lock.</para>
    /// </summary>
    private async Task<CheckoutOutcome> FinishRecoveryCheckoutAsync()
    {
        WorkingPath = ComputeWorkingPath(SourcePath);
        var marker = TryReadMarker(WorkingPath + ".dirty");

        if (marker?.HashAtCheckout is null || !File.Exists(WorkingPath))
        {
            // No verifiable snapshot hash (legacy/corrupt marker) or D' vanished —
            // automatic recovery would risk trampling D. Hand back to the caller.
            _lockService.Release();
            _logger.LogBreadcrumb("Recovery: conflict (no verifiable marker or missing D')");
            return CheckoutOutcome.RecoveryConflict;
        }

        var (hashCompleted, sourceHash) = await NetworkFileOps.ComputeHashAsync(SourcePath);
        if (!hashCompleted)
        {
            _lockService.Release();
            _logger.LogInfo("CheckoutService: recovery hash timed out — network unreachable");
            return CheckoutOutcome.NetworkUnreachable;
        }

        // D must match one of OUR hashes to prove no foreign writer touched it:
        //  • HashAtCheckout — D unchanged since the crashed session checked out/saved.
        //  • PendingSaveHash — D was updated by the crashed session's own ghost rename
        //    (a save whose File.Move landed after its deadline, possibly after exit).
        //    D then equals a snapshot of D' at that save, so D' is a superset — safe.
        var matchesCheckout = string.Equals(sourceHash, marker.HashAtCheckout,  StringComparison.OrdinalIgnoreCase);
        var matchesPending  = marker.PendingSaveHash is not null
                           && string.Equals(sourceHash, marker.PendingSaveHash, StringComparison.OrdinalIgnoreCase);

        if (!matchesCheckout && !matchesPending)
        {
            // D changed since the crash (another writer saved). The unsaved changes
            // are based on a stale D and cannot be applied automatically.
            _lockService.Release();
            _logger.LogBreadcrumb("Recovery: conflict (source modified since crash)");
            return CheckoutOutcome.RecoveryConflict;
        }

        if (matchesPending)
            _logger.LogBreadcrumb("Recovery: matched via pending save hash (post-exit ghost rename)");

        // D is byte-identical to what the crashed session checked out — safe to resume.
        HashAtCheckout = sourceHash;
        Mode           = CheckoutMode.WriteAccess;
        SessionDirty   = true;
        CurrentHolder  = null;

        _lockService.WakeDetected    += OnWake;
        _lockService.HeartbeatFailed += OnHeartbeatFailed;
        _logger.LogBreadcrumb("Recovery: WriteAccess (resumed crashed session)", new() { ["path"] = SourcePath });
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// Saves D' back to D. Verifies the lock is still held and D has not been
    /// externally modified, snapshots D' to a LOCAL temp file via the SQLite Online
    /// Backup API, streams the snapshot to D.tmp with a stall-aware chunked copy,
    /// verifies the copy by hash, then atomically renames it to D. (Timestamped
    /// rotating backups are a separate concern, handled by <see cref="BackupService"/>.)
    /// </summary>
    /// <param name="releaseLockAfter">
    /// When true, deletes the lock file after a successful save.
    /// Pass true only on graceful shutdown.
    /// </param>
    /// <param name="isAutoSave">
    /// When true, the save was triggered by the autosave timer rather than the user.
    /// Transient failures (<see cref="SaveOutcome.CopyError"/>) then raise
    /// <see cref="SaveFailed"/> with an auto-dismissing, "will retry automatically"
    /// message, because the next autosave cycle will retry on its own.
    /// </param>
    /// <returns>A <see cref="SaveOutcome"/> describing the result.</returns>
    public async Task<SaveOutcome> SaveAsync(bool releaseLockAfter = false, bool isAutoSave = false)
    {
        if (Mode != CheckoutMode.WriteAccess)
            return SaveOutcome.NotInWriteMode;

        // Atomic check-then-set: if another thread already set _saveInFlight to 1,
        // CompareExchange returns its old value (1) and we bail. Only one caller
        // can transition 0 → 1 across all threads.
        if (Interlocked.CompareExchange(ref _saveInFlight, 1, 0) != 0)
        {
            _logger.LogInfo("CheckoutService: SaveAsync skipped — a save is already in progress.");
            _dispatch(() => SaveAlreadyInProgress?.Invoke());
            return SaveOutcome.CopyError;
        }

        try
        {
            _dispatch(() => SaveStarted?.Invoke());
            return await SaveAsyncCore(releaseLockAfter, isAutoSave);
        }
        finally
        {
            Volatile.Write(ref _saveInFlight, 0);
            // Guaranteed counterpart to SaveStarted — fires on every outcome so the
            // "Saving…" indicator can never be left stuck on.
            _dispatch(() => SaveFinished?.Invoke());
        }
    }

    /// <summary>
    /// Raises <see cref="SaveFailed"/> for a transient <see cref="SaveOutcome.CopyError"/>.
    /// For an autosave the message gains a "will retry automatically" note and the banner
    /// is marked auto-dismissing (the next autosave cycle retries on its own); for a manual
    /// save the message is shown as-is and the banner stays until the next successful save.
    /// </summary>
    /// <param name="message">Base human-readable error message.</param>
    /// <param name="isAutoSave">Whether the originating save was an autosave.</param>
    private void RaiseTransientSaveError(string message, bool isAutoSave)
    {
        var text = isAutoSave
            ? message + " The app will retry automatically."
            : message;
        _dispatch(() => SaveFailed?.Invoke(text, isAutoSave));
    }

    /// <summary>
    /// One-shot probe after a step-5 rename timeout: re-hashes D and reports whether it
    /// already equals <paramref name="expectedHash"/> (the hash of the D.tmp snapshot).
    /// True means the "timed-out" move actually landed just past the deadline and the
    /// save succeeded. Any probe failure (timeout, file transiently missing mid-move,
    /// I/O error) reports false — the caller then records the pending hash and the
    /// step-2 self-heal / recovery paths pick the ghost up whenever it lands.
    /// </summary>
    /// <param name="expectedHash">Hash of the D.tmp snapshot that was being renamed onto D.</param>
    private async Task<bool> ProbeGhostRenameLandedAsync(string? expectedHash)
    {
        if (expectedHash is null) return false;
        try
        {
            var (completed, hash) = await NetworkFileOps.ComputeHashAsync(SourcePath);
            return completed && string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Records the snapshot hash of a save whose rename was never confirmed, in memory
    /// (<see cref="_pendingSaveHash"/>, for same-session self-heal in step 2) and in the
    /// dirty marker file (for cross-restart recognition by
    /// <see cref="FinishRecoveryCheckoutAsync"/>). The marker is only rewritten when one
    /// already exists — no marker means no unsaved edits, in which case a post-exit
    /// ghost is harmless (D' content and ghost content are identical) and the in-memory
    /// copy covers the rest of this session.
    /// Internal so tests can simulate the rename-timeout state deterministically.
    /// </summary>
    /// <param name="hash">Hash of the D.tmp snapshot whose rename is unconfirmed.</param>
    internal void RecordPendingSaveHash(string? hash)
    {
        _pendingSaveHash = hash;
        if (HasDirtyMarker)
            WriteDirtyMarker();
    }

    private async Task<SaveOutcome> SaveAsyncCore(bool releaseLockAfter, bool isAutoSave)
    {
        _logger.LogBreadcrumb("Save: started", new()
        {
            ["autoSave"] = isAutoSave.ToString(),
            ["releaseLock"] = releaseLockAfter.ToString(),
        });

        // New-database mode — D never existed at checkout time, so there is no
        // separate D'. WorkingPath points directly at the authoritative database;
        // every write already went to the final location. No copy-back needed.
        if (WorkingPath == SourcePath)
        {
            SessionDirty = false;
            if (releaseLockAfter) _lockService.Release();
            _dispatch(() => SaveCompleted?.Invoke());
            return SaveOutcome.Success;
        }

        // ── Step 1: Verify we still hold the lock ─────────────────────────────
        var lockResult = await VerifyLockIsOursAsync();

        if (lockResult == LockVerificationResult.Unreachable)
        {
            _logger.LogInfo("CheckoutService: lock verification timed out — network unreachable");
            RaiseTransientSaveError(NetworkFileOps.UnreachableMessage, isAutoSave);
            return SaveOutcome.CopyError;
        }

        if (lockResult == LockVerificationResult.NotOurs)
        {
            // If the lock file was merely REMOVED (antivirus, cloud-sync — no new
            // holder), try to re-create it atomically before giving up the session.
            if (await TryRecoverRemovedLockAsync())
            {
                _logger.LogBreadcrumb("Save: lock re-acquired after external removal — continuing");
                // Fall through — the save proceeds normally under the restored lock.
            }
            else
            {
                _logger.LogBreadcrumb("Save: LockLost");
                await HandleLockLossAsync();
                return SaveOutcome.LockLost;
            }
        }

        // ── Step 2: Verify D has not been externally modified ─────────────────
        if (HashAtCheckout is not null)
        {
            var (hashCompleted, currentSourceHash) = await NetworkFileOps.ComputeHashAsync(SourcePath);

            if (!hashCompleted)
            {
                _logger.LogInfo("CheckoutService: source hash timed out — network unreachable");
                RaiseTransientSaveError(NetworkFileOps.UnreachableMessage, isAutoSave);
                return SaveOutcome.CopyError;
            }

            if (currentSourceHash != HashAtCheckout)
            {
                // Ghost-rename self-heal: if D's hash equals the snapshot hash of a save
                // whose rename was never confirmed, D was updated by OUR OWN delayed
                // File.Move landing after its 5s deadline — not by another writer.
                // Adopt that save as successful and continue with the current one.
                // (SHA-256 equality with a foreign write is not a practical concern.)
                if (_pendingSaveHash is not null &&
                    string.Equals(currentSourceHash, _pendingSaveHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo("CheckoutService: source matches pending save hash — " +
                                    "our delayed rename landed; treating prior save as successful.");
                    _logger.LogBreadcrumb("Save: GhostRenameHealed");
                    HashAtCheckout   = _pendingSaveHash;
                    _pendingSaveHash = null;
                    // Fall through to step 3 — the current save proceeds normally.
                }
                else
                {
                    const string msg = "The database was modified outside this session. Save aborted.";
                    _logger.LogInfo("CheckoutService: source hash mismatch — " + msg);
                    _logger.LogBreadcrumb("Save: SourceModified");
                    // Sticky: a genuine conflict the user must resolve — never auto-dismiss.
                    _dispatch(() => SaveFailed?.Invoke(msg, false));
                    return SaveOutcome.SourceModified;
                }
            }
        }

        // ── Step 3: Snapshot D' locally, then stream the snapshot to D.tmp ───
        // 3a. BackupSqliteDatabase coordinates with the SQLite engine rather than
        //     copying raw bytes. This guarantees a consistent snapshot even if a
        //     write transaction is mid-commit on the DatabaseContext connection
        //     (something a raw File.Copy cannot guarantee). The backup targets a
        //     LOCAL temp file: the consistency guarantee is about the engine-
        //     coordinated READ of D'; the network only ever sees the finished,
        //     quiescent snapshot. This keeps SQLite's page-level writes off SMB
        //     and out from under any network deadline (finding #7).
        var tmpPath   = SourcePath  + ".tmp";
        var localTmp  = WorkingPath + ".savetmp";
        string newSourceHash;
        try
        {
            try
            {
                await Task.Run(() => BackupSqliteDatabase(WorkingPath, localTmp));
            }
            catch (Exception ex)
            {
                var msg = $"Failed to snapshot the database: {ex.Message}";
                _logger.LogError(ex, "CheckoutService: local save snapshot failed");
                RaiseTransientSaveError(msg, isAutoSave);
                return SaveOutcome.CopyError;
            }

            // 3b. Hash the snapshot for post-save conflict detection. BackupDatabase
            //     output is not byte-identical to D' (SQLite increments page-level
            //     counters in the destination), so we hash the exact bytes that will
            //     become the new D. Local file — no deadline needed.
            try
            {
                newSourceHash = ComputeHash(localTmp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: could not hash local save snapshot");
                RaiseTransientSaveError($"Failed to prepare the save: {ex.Message}", isAutoSave);
                return SaveOutcome.CopyError;
            }

            // 3c. Stream the quiescent snapshot to D.tmp. CopyAsync is chunked with a
            //     per-chunk stall deadline, so a large database on a slow link takes
            //     as long as it takes; only genuine stalls fail. The snapshot is a
            //     closed single file (no live connection, no journal sidecar), so a
            //     raw byte copy is safe here.
            try
            {
                var copyCompleted = await NetworkFileOps.CopyAsync(localTmp, tmpPath);

                if (!copyCompleted)
                {
                    _logger.LogInfo("CheckoutService: snapshot → D.tmp copy stalled — network unreachable");
                    await NetworkFileOps.DeleteAsync(tmpPath);
                    RaiseTransientSaveError(NetworkFileOps.UnreachableMessage, isAutoSave);
                    return SaveOutcome.CopyError;
                }
            }
            catch (Exception ex)
            {
                var msg = $"Failed to write to database location: {ex.Message}";
                _logger.LogError(ex, "CheckoutService: D.tmp copy failed");
                await NetworkFileOps.DeleteAsync(tmpPath);
                RaiseTransientSaveError(msg, isAutoSave);
                return SaveOutcome.CopyError;
            }
        }
        finally
        {
            // The local snapshot has served its purpose (or the save failed) —
            // it is never needed after the network copy. A crash in this window
            // leaves an orphan; CleanupStaleCrashArtifacts sweeps it at startup.
            TryDelete(localTmp);
        }

        // ── Step 4: Verify the network copy before it becomes D ─────────────
        // Guards against a torn/corrupted transfer: D.tmp must hash identically to
        // the local snapshot before the rename makes it the authoritative database.
        var (verifyCompleted, tmpHash) = await NetworkFileOps.ComputeHashAsync(tmpPath);

        if (!verifyCompleted)
        {
            _logger.LogInfo("CheckoutService: D.tmp verification hash stalled — network unreachable");
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseTransientSaveError(NetworkFileOps.UnreachableMessage, isAutoSave);
            return SaveOutcome.CopyError;
        }

        if (!string.Equals(tmpHash, newSourceHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInfo("CheckoutService: D.tmp does not match the local snapshot — torn transfer; aborting save.");
            _logger.LogBreadcrumb("Save: TornTransfer");
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseTransientSaveError("The save could not be verified after writing.", isAutoSave);
            return SaveOutcome.CopyError;
        }

        // ── Step 5: Atomically rename D.tmp → D ──────────────────────────────
        try
        {
            var renameCompleted = await NetworkFileOps.MoveAsync(tmpPath, SourcePath);

            if (!renameCompleted)
            {
                // The 5s deadline elapsed, but the abandoned File.Move thread may
                // still complete the rename ("ghost rename"). Probe once: if D
                // already bears the snapshot hash, the move landed just past the
                // deadline and this save actually succeeded.
                if (await ProbeGhostRenameLandedAsync(newSourceHash))
                {
                    _logger.LogInfo("CheckoutService: rename timed out but D already matches " +
                                    "the snapshot — treating save as successful.");
                    _logger.LogBreadcrumb("Save: GhostRenameConfirmedByProbe");
                    // Fall through to step 6 as a normal success.
                }
                else
                {
                    // Genuinely unconfirmed. Remember the snapshot hash (in memory and
                    // in the dirty marker) so a ghost landing later — even after a
                    // restart — is recognized as ours instead of as a foreign write.
                    RecordPendingSaveHash(newSourceHash);
                    _logger.LogInfo("CheckoutService: D.tmp rename timed out — network unreachable");
                    RaiseTransientSaveError(NetworkFileOps.UnreachableMessage, isAutoSave);
                    return SaveOutcome.CopyError;
                }
            }
        }
        catch (Exception ex)
        {
            // A thrown rename almost always means the move truly did not happen, but
            // on a network share the failure report itself can be unreliable —
            // recording the pending hash is harmless when the ghost never lands
            // (it simply never matches anything).
            RecordPendingSaveHash(newSourceHash);
            var msg = $"Failed to finalize save: {ex.Message}";
            _logger.LogError(ex, "CheckoutService: D.tmp rename failed");
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseTransientSaveError(msg, isAutoSave);
            return SaveOutcome.CopyError;
        }

        // ── Step 6: Update state ─────────────────────────────────────────────
        HashAtCheckout   = newSourceHash;
        _pendingSaveHash = null;   // this save is confirmed — no ghost outstanding
        SessionDirty     = false;

        // D' now matches D — delete the dirty marker. If the user makes further
        // edits, DatabaseContext.MarkDirty() will re-write it (after ResetDirty rearms it).
        // This keeps the marker as an accurate signal: present = unsaved changes exist.
        //
        // Fire BeforeDirtyMarkerDeleted SYNCHRONOUSLY first so subscribers (notably
        // DatabaseContext.ResetDirty) can rearm MarkDirty before the marker file is
        // removed. See the event docstring for the F2 race rationale.
        BeforeDirtyMarkerDeleted?.Invoke();
        DeleteDirtyMarker();
        if (releaseLockAfter)
            _lockService.Release();

        _logger.LogInfo("CheckoutService: Save completed successfully.");
        _logger.LogBreadcrumb("Save: Success");
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
            string? sourceHash;
            try
            {
                bool hashCompleted;
                (hashCompleted, sourceHash) = await NetworkFileOps.ComputeHashAsync(SourcePath);

                if (!hashCompleted)
                {
                    _logger.LogInfo("CheckoutService: refresh hash timed out — network unreachable");
                    return RefreshOutcome.SourceUnavailable;
                }
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"CheckoutService: could not read D for refresh: {ex.Message}");
                return RefreshOutcome.SourceUnavailable;
            }

            try
            {
                var copyCompleted = await NetworkFileOps.CopyAsync(SourcePath, tmpPath);

                if (!copyCompleted)
                {
                    _logger.LogInfo("CheckoutService: refresh copy timed out — network unreachable");
                    await NetworkFileOps.DeleteAsync(tmpPath);
                    return RefreshOutcome.SourceUnavailable;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: failed to copy D to D''.tmp during refresh");
                TryDelete(tmpPath);
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
                        TryDelete(tmpPath);
                        return RefreshOutcome.SourceUnavailable;
                    }
                }

                try
                {
                    // Connection is closed at this point (beforeOverwrite); drop any
                    // WAL sidecars so the fresh copy is not paired with a stale -wal.
                    TryDeleteWalSidecars(ReadOnlyWorkingPath);
                    File.Move(tmpPath, ReadOnlyWorkingPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CheckoutService: failed to rename D''.tmp during refresh");
                    TryDelete(tmpPath);
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
            TryDelete(tmpPath);

            if (attempt < 2)
                await Task.Delay(100 * (attempt + 1));
        }

        _logger.LogInfo("CheckoutService: refresh failed — hash mismatch after 3 retries.");
        return RefreshOutcome.SourceUnavailable;
    }

    /// <summary>
    /// Called by <see cref="Data.DatabaseContext"/> on the first user-initiated write
    /// of a session. Writes the dirty marker so crash recovery detection works correctly.
    /// Only acts when in <see cref="CheckoutMode.WriteAccess"/> mode — no-op otherwise.
    /// </summary>
    public void MarkDirty()
    {
        if (Mode == CheckoutMode.WriteAccess && WorkingPath != SourcePath)
        {
            // The marker is the ONLY thing standing between a crash and the startup
            // sweep deleting D' as "no edits were made" — a silent write failure here
            // would silently disarm crash protection. Retry once (transient AV holds
            // clear in milliseconds), then warn the user honestly.
            if (!WriteDirtyMarker() && !WriteDirtyMarker())
            {
                _logger.LogError(null,
                    "CheckoutService: could not write the dirty marker — crash protection degraded for this session.");
                _dispatch(() => SaveFailed?.Invoke(
                    "TermPoint couldn't create its crash-protection file for this session. " +
                    "Saving still works normally, but unsaved changes may not be recoverable " +
                    "after a crash — save your work frequently.",
                    true));
            }
            _dispatch(() => BecameDirty?.Invoke());
        }
    }

    /// <summary>
    /// Cleans up orphaned working-copy and dirty-marker files that represent states
    /// where no user data was lost and no crash notification is needed:
    /// D' without a marker (crash before any edits), or a marker without D'
    /// (interrupted discard from a previous session). Call at startup before
    /// <see cref="DetectCrashRecovery"/>.
    /// </summary>
    /// <param name="sourcePath">The database path being opened.</param>
    public void CleanupStaleCrashArtifacts(string sourcePath)
    {
        var workingPath = ComputeWorkingPath(sourcePath);
        var markerPath  = workingPath + ".dirty";

        // A crash between the local save-snapshot and the end of the network copy
        // leaves an orphaned .savetmp beside D'. It is a derived artifact (D' is
        // authoritative) — always safe to delete.
        var saveTmpPath = workingPath + ".savetmp";
        if (File.Exists(saveTmpPath))
        {
            TryDelete(saveTmpPath);
            _logger.LogInfo($"CheckoutService: cleaned up orphaned save snapshot: {saveTmpPath}");
        }

        bool hasWorking = File.Exists(workingPath);
        bool hasMarker  = File.Exists(markerPath);

        if (hasWorking && !hasMarker)
        {
            TryDelete(workingPath);
            TryDeleteWalSidecars(workingPath);
            _logger.LogInfo($"CheckoutService: cleaned up untracked working copy (no dirty marker): {workingPath}");
        }
        else if (!hasWorking && hasMarker)
        {
            TryDelete(markerPath);
            _logger.LogInfo($"CheckoutService: cleaned up orphaned dirty marker (no working copy): {markerPath}");
        }
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
    /// Discards the orphaned working copy from a previous crashed session.
    /// Call when the user declines crash recovery.
    /// </summary>
    /// <param name="sourcePath">The database path whose recovery data to discard.</param>
    public void DiscardCrash(string sourcePath)
    {
        var workingPath = ComputeWorkingPath(sourcePath);
        TryDelete(workingPath);
        TryDeleteWalSidecars(workingPath);
        TryDelete(workingPath + ".dirty");
    }

    /// <summary>
    /// Examines the crash artifacts for <paramref name="sourcePath"/> so the caller can
    /// decide what recovery to offer. Call after <see cref="DetectCrashRecovery"/> returns
    /// true (returns null when no artifacts exist).
    ///
    /// <para><b>Side effect (intentional):</b> the integrity check opens D' with a
    /// read-write connection, which makes SQLite replay any hot rollback journal left by
    /// the crash — completing SQLite's own crash recovery — before verifying the file.
    /// A read-only open would refuse to roll back the journal and misreport a perfectly
    /// recoverable D' as damaged.</para>
    /// </summary>
    /// <param name="sourcePath">The database path D whose crash artifacts to inspect.</param>
    /// <returns>
    /// Null when no crash artifacts exist; otherwise a <see cref="CrashRecoveryInfo"/>
    /// with the working-copy path, the marker's recorded source hash (null for
    /// legacy/unreadable markers), and whether D' passed the integrity check.
    /// </returns>
    public CrashRecoveryInfo? InspectCrashRecovery(string sourcePath)
    {
        if (!DetectCrashRecovery(sourcePath)) return null;

        var workingPath = ComputeWorkingPath(sourcePath);
        var markerHash  = TryReadMarkerHash(workingPath + ".dirty");
        var intact      = BackupService.CheckIntegrity(workingPath, SqliteOpenMode.ReadWrite);
        return new CrashRecoveryInfo(workingPath, markerHash, intact);
    }

    /// <summary>
    /// Moves a damaged crash-recovery working copy aside (suffix
    /// <c>.corrupt-yyyyMMdd-HHmmss</c>) instead of deleting it, and removes its dirty
    /// marker. Archiving rather than deleting preserves a salvage option — a corrupt
    /// SQLite file usually still yields most of its rows to <c>.recover</c>/<c>.dump</c>
    /// in a support scenario — at zero cost to the user, who never sees the file.
    /// </summary>
    /// <param name="sourcePath">The database path D whose damaged working copy to archive.</param>
    /// <returns>The archive path, or null when the move failed (the file is deleted instead).</returns>
    public string? ArchiveCorruptWorkingCopy(string sourcePath)
    {
        var workingPath = ComputeWorkingPath(sourcePath);
        var archivePath = workingPath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(workingPath, archivePath, overwrite: true);
            // Archive the WAL sidecar too — after a crash it can hold committed data
            // not yet checkpointed into the .db, which a salvage attempt would need.
            try { File.Move(workingPath + "-wal", archivePath + "-wal", overwrite: true); } catch { }
            _logger.LogInfo($"CheckoutService: archived damaged working copy to {archivePath}");
        }
        catch (Exception ex)
        {
            _logger.LogInfo($"CheckoutService: could not archive damaged working copy ({ex.Message}) — deleting instead.");
            TryDelete(workingPath);
            archivePath = null;
        }
        TryDeleteWalSidecars(workingPath);
        TryDelete(workingPath + ".dirty");
        return archivePath;
    }

    /// <summary>
    /// Deletes any orphaned <c>D.tmp</c> file left alongside D from a previous
    /// crashed save. Call at startup before <see cref="CheckoutAsync"/>.
    /// </summary>
    /// <param name="sourcePath">The database path to check for an orphaned tmp file.</param>
    public async Task CleanupOrphanedTmpAsync(string sourcePath)
    {
        var tmpPath = sourcePath + ".tmp";
        var (completed, exists) = await NetworkFileOps.ExistsAsync(tmpPath);
        if (!completed || !exists) return;

        var deleted = await NetworkFileOps.DeleteAsync(tmpPath);
        if (deleted)
            _logger.LogInfo($"CheckoutService: cleaned up orphaned tmp file: {tmpPath}");
        else
            _logger.LogInfo($"CheckoutService: could not delete orphaned tmp (timeout or error)");
    }

    /// <summary>
    /// Releases the write lock and optionally saves first. Call on graceful shutdown.
    ///
    /// <para>Any in-flight save (e.g. an autosave tick that started just before
    /// shutdown) is drained first, so the final save cannot be silently skipped by the
    /// <see cref="_saveInFlight"/> gate and the lock is never released mid-write.</para>
    ///
    /// <para><b>Lock disposition on a failed final save:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="SaveOutcome.CopyError"/> (transient — network down, I/O error):
    ///         the lock file is deliberately LEFT IN PLACE via
    ///         <see cref="WriteLockService.Suspend"/>. D' and its dirty marker survive for
    ///         recovery at next launch, and the abandoned lock (stale after
    ///         <see cref="WriteLockService.StaleLockThresholdSeconds"/>) keeps other users
    ///         from writing D underneath those unsaved changes without an informed
    ///         stale-lock takeover.</item>
    ///   <item><see cref="SaveOutcome.SourceModified"/> / <see cref="SaveOutcome.LockLost"/>:
    ///         the lock is released normally — holding it protects nothing (the changes
    ///         can never be applied automatically; recovery will offer an export).</item>
    /// </list>
    /// </summary>
    /// <param name="saveFirst">
    /// When true and in write mode, runs <see cref="SaveAsync"/> before releasing.
    /// </param>
    /// <returns>
    /// The outcome of the final save, so callers can decide whether it is safe to delete
    /// the working copy (<see cref="CleanupWorkingCopy"/>) — only on
    /// <see cref="SaveOutcome.Success"/>. Returns Success when no save was requested
    /// or the session was not in write mode.
    /// </returns>
    public async Task<SaveOutcome> ReleaseAsync(bool saveFirst)
    {
        _logger.LogBreadcrumb("Release", new() { ["saveFirst"] = saveFirst.ToString() });
        StopAutoSave();
        _lockService.WakeDetected -= OnWake;

        var outcome = SaveOutcome.Success;

        if (saveFirst && Mode == CheckoutMode.WriteAccess)
        {
            // StopAutoSave stops the timer but not a tick already in flight; wait for
            // it so the final save below actually runs instead of hitting the
            // _saveInFlight gate and being skipped.
            await WaitForInFlightSaveAsync();

            outcome = await SaveAsync(releaseLockAfter: true);

            if (outcome == SaveOutcome.CopyError)
                _lockService.Suspend();          // keep the lock file — see doc above
            else if (outcome != SaveOutcome.Success)
                _lockService.Release();          // SourceModified / LockLost
            // Success: SaveAsync already released the lock (releaseLockAfter: true).
        }
        else
        {
            _lockService.Release();
        }

        Mode = CheckoutMode.ReadOnly;
        LockLossNewHolder = null;
        return outcome;
    }

    /// <summary>
    /// Waits (bounded) for any in-flight <see cref="SaveAsync"/> to finish. A save is
    /// bounded by a handful of 5-second-capped network operations plus retries, so the
    /// 60-second ceiling comfortably covers the worst case without risking a hung
    /// shutdown if something unforeseen wedges the save.
    /// </summary>
    private async Task WaitForInFlightSaveAsync()
    {
        const int maxWaitMs = 60_000, pollMs = 50;
        for (var waited = 0; waited < maxWaitMs && Volatile.Read(ref _saveInFlight) == 1; waited += pollMs)
            await Task.Delay(pollMs);
    }

    /// <summary>
    /// Deletes D' (or D'') from the working directory. Call after the database
    /// connection has been closed — the file cannot be deleted while SQLite holds it open.
    /// On crash the file is left behind; <see cref="CleanupStaleCrashArtifacts"/> or
    /// <see cref="DetectCrashRecovery"/> handles it at next startup.
    /// </summary>
    public void CleanupWorkingCopy()
    {
        if (!string.IsNullOrEmpty(WorkingPath) && File.Exists(WorkingPath) && WorkingPath != SourcePath)
        {
            try { File.Delete(WorkingPath); }
            catch (Exception ex) { _logger.LogInfo($"CheckoutService: could not delete working copy: {ex.Message}"); }
        }
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
            _ =>
            {
                _ = Task.Run(async () =>
                {
                    try { await AutoSaveTickAsync(); }
                    catch (Exception ex) { App.Logger.LogError(ex, "AutoSave tick failed"); }
                });
            },
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

        var outcome = await SaveAsync(isAutoSave: true);
        if (outcome is SaveOutcome.LockLost or SaveOutcome.SourceModified)
            StopAutoSave(); // SaveFailed event is already raised inside SaveAsync.
        // CopyError: keep the timer running — transient, worth retrying next cycle.
    }

    /// <summary>
    /// Called when <see cref="WriteLockService.WakeDetected"/> fires — i.e., the
    /// wake-detection timer saw a gap long enough to infer the machine was
    /// asleep. Re-reads the lock file to verify this session still owns it.
    /// If yes, forces an immediate heartbeat renewal so the lock looks fresh to
    /// other readers. If no, routes through
    /// <see cref="HandleLockLossAsync"/>, which raises
    /// <see cref="WriteLockLost"/> so MainWindow can demote in place.
    ///
    /// <para>The event is dispatched on the UI thread, but the verification work
    /// (reading the <c>.lock</c> file on a network share) can block for 20–30
    /// seconds right after a wake while the SMB client reconnects. Running that
    /// synchronously on the UI thread would freeze the window. We therefore hop
    /// to a background thread immediately and only touch the UI — via
    /// <see cref="_dispatch"/> inside <see cref="HandleLockLossAsync"/> —
    /// after the decision is made.</para>
    ///
    /// <para>No-op when not in write mode — readers don't own a lock, so there's
    /// nothing to verify.</para>
    /// </summary>
    private void OnWake()
    {
        if (Mode != CheckoutMode.WriteAccess) return;
        _logger.LogBreadcrumb("Wake detected");

        // Fire-and-forget — VerifyLockIsOursAsync uses NetworkFileOps internally
        // so the UI stays responsive during the SMB reconnect delay.
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await VerifyLockIsOursAsync();
                switch (result)
                {
                    case LockVerificationResult.NotOurs:
                        if (await TryRecoverRemovedLockAsync())
                        {
                            _logger.LogInfo("CheckoutService: wake check — lock file was removed; re-acquired.");
                            break;
                        }
                        _logger.LogInfo("CheckoutService: wake check — lock is no longer ours. Timing out session.");
                        await HandleLockLossAsync();
                        break;
                    case LockVerificationResult.Unreachable:
                        _logger.LogInfo("CheckoutService: wake check — network unreachable, keeping current state.");
                        break;
                    case LockVerificationResult.Ours:
                        _logger.LogInfo("CheckoutService: wake check — lock confirmed. Renewing heartbeat.");
                        await _lockService.ForceRenewHeartbeat();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: wake-check task failed");
            }
        });
    }

    /// <summary>
    /// Attempts to recover from an <b>externally removed</b> lock file by atomically
    /// re-creating it, instead of demoting the session and stranding the user's
    /// unsaved edits.
    ///
    /// <para><b>Why this is safe:</b> this runs only when lock verification found the
    /// file <i>missing</i> with the share reachable and no new holder
    /// (<see cref="LockLossNewHolder"/> is null) — the signature of antivirus,
    /// cloud-sync, or backup software deleting the file, not of another user taking
    /// over. Re-creation goes through <see cref="WriteLockService.TryAcquire"/>, whose
    /// <c>FileMode.CreateNew</c> is atomic at the SMB level: if another instance claims
    /// the lock in the race window, our create loses cleanly and the normal demotion
    /// path runs. A genuine takeover (foreign <c>SessionGuid</c> in the file) never
    /// reaches this method — trampling a rightful holder remains impossible.</para>
    ///
    /// <para>On success the session simply continues as writer (TryAcquire restarted
    /// the heartbeat and wake timers; our event subscriptions were never removed) and
    /// an auto-dismissing banner tells the user what happened.</para>
    /// </summary>
    /// <returns>True when the lock was re-acquired and the session continues as writer.</returns>
    private async Task<bool> TryRecoverRemovedLockAsync()
    {
        // A takeover has a new holder — never contest it.
        if (LockLossNewHolder is not null) return false;

        var completed = await NetworkFileOps.RunAsync(
            () => _lockService.TryAcquire(SourcePath), "lock re-acquire after removal");

        if (!completed || !_lockService.IsWriter)
            return false;

        _logger.LogInfo("CheckoutService: lock file was removed externally; re-acquired atomically — session continues.");
        _logger.LogBreadcrumb("Lock re-acquired after external removal");
        _dispatch(() => SaveFailed?.Invoke(
            "The database lock file was removed by another program (possibly antivirus " +
            "or cloud-sync software). TermPoint has restored it and your session " +
            "continues normally.",
            true)); // auto-dismiss — informational, nothing is wrong anymore
        return true;
    }

    /// <summary>
    /// Stops autosave and unhooks the wake handler, then raises
    /// <see cref="WriteLockLost"/> on the UI thread with a cause-specific
    /// <see cref="WriteLockLostReason"/>. Does NOT transition
    /// <see cref="Mode"/>, release the lock, delete D', or set up D''.
    ///
    /// <para>The reason is derived from <see cref="LockLossNewHolder"/>, which
    /// was populated by <see cref="VerifyLockIsOursOnceAsync"/> during the
    /// verification that preceded this call. Non-null = another user took over;
    /// null = lock file was deleted or corrupted (external interference).</para>
    ///
    /// <para>The handler of <see cref="WriteLockLost"/> is responsible for the
    /// full demotion via <see cref="DemoteToReadOnlyAsync"/>, which closes the
    /// <c>DatabaseContext</c> connection at the right moment, releases the lock
    /// (without deleting it if it is no longer ours), deletes D', creates D'',
    /// and reopens the connection. Keeping state transitions in the handler means
    /// the UI stays consistent with <see cref="CheckoutService"/> state, and the
    /// existing view-model / banner is preserved.</para>
    /// </summary>
    private Task HandleLockLossAsync()
    {
        StopAutoSave();
        _lockService.WakeDetected    -= OnWake;
        _lockService.HeartbeatFailed -= OnHeartbeatFailed;   // F11 — unsubscribe on first loss
        var reason = LockLossNewHolder is not null
            ? WriteLockLostReason.TakenOver
            : WriteLockLostReason.LockFileRemoved;
        _logger.LogBreadcrumb("Lock lost", new()
        {
            ["reason"] = reason.ToString(),
            ["newHolder"] = LockLossNewHolder?.Username ?? "(none)",
        });
        _dispatch(() => WriteLockLost?.Invoke(reason));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when <see cref="WriteLockService.HeartbeatFailed"/> fires — i.e., the heartbeat
    /// timer has failed to renew the lock file <see cref="WriteLockService.HeartbeatFailureThreshold"/>
    /// consecutive times.
    ///
    /// <para><b>Why not demote immediately?</b> The heartbeat failed because a network-file
    /// write failed — almost always because the share is unreachable. But the save-time and
    /// wake-from-sleep paths both distinguish "share unreachable" (transient — keep writer
    /// state) from "lock genuinely taken" (permanent — demote). If we skipped that check
    /// here, a 2–4 minute WiFi/VPN interruption on the writer's own machine would trigger a
    /// false-positive demotion and destroy unsaved edits, even though no other instance ever
    /// contended for the lock. (W1, write-access-loss-agenda 2026-06-24.)</para>
    ///
    /// <para><b>Sequence:</b> <see cref="VerifyLockIsOursAsync"/> reads the lock file with
    /// the same reachability-probe logic used by the save and wake paths.
    /// <list type="bullet">
    ///   <item><see cref="LockVerificationResult.NotOurs"/> — genuine loss. Route through
    ///         <see cref="HandleLockLossAsync"/> (same as before W1).</item>
    ///   <item><see cref="LockVerificationResult.Unreachable"/> — transient network issue.
    ///         Keep writer state; surface a transient, auto-dismissing warning via
    ///         <see cref="SaveFailed"/> so the user knows saves are paused. The heartbeat
    ///         timer continues; a successful renewal clears the warning naturally (the next
    ///         autosave cycle succeeds and raises <see cref="SaveCompleted"/>).</item>
    ///   <item><see cref="LockVerificationResult.Ours"/> — false alarm (heartbeat write
    ///         failed for a reason other than ownership loss). Force-renew immediately.</item>
    /// </list></para>
    ///
    /// <para>A re-entrancy guard (<see cref="_heartbeatVerifyInFlight"/>) prevents stacking
    /// verification attempts if <see cref="WriteLockService.HeartbeatFailed"/> fires again
    /// while an earlier verification is still awaiting the network.</para>
    ///
    /// <para>This handler is called from the heartbeat timer thread (not the UI thread).
    /// The verification runs on a thread-pool thread via <see cref="Task.Run"/> (the SMB
    /// reconnect can take 20–30 s); UI updates are dispatched through
    /// <see cref="_dispatch"/>.</para>
    /// </summary>
    private void OnHeartbeatFailed()
    {
        if (Mode != CheckoutMode.WriteAccess) return;
        _logger.LogBreadcrumb("Heartbeat failed");

        if (Interlocked.CompareExchange(ref _heartbeatVerifyInFlight, 1, 0) != 0)
        {
            _logger.LogInfo("CheckoutService: HeartbeatFailed — verification already in flight; skipping.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInfo("CheckoutService: HeartbeatFailed — verifying lock ownership before acting.");
                var result = await VerifyLockIsOursAsync();
                switch (result)
                {
                    case LockVerificationResult.NotOurs:
                        if (await TryRecoverRemovedLockAsync())
                        {
                            // Heartbeats were failing because the file vanished; the
                            // re-acquire recreated it and restarted the heartbeat timer.
                            _logger.LogInfo("CheckoutService: heartbeat verify — lock file was removed; re-acquired.");
                            break;
                        }
                        _logger.LogInfo("CheckoutService: heartbeat verify — lock genuinely lost; demoting.");
                        await HandleLockLossAsync();
                        break;

                    case LockVerificationResult.Unreachable:
                        _logger.LogInfo("CheckoutService: heartbeat verify — share unreachable; keeping writer state.");
                        _dispatch(() => SaveFailed?.Invoke(
                            "Network connection interrupted — your changes are safe locally " +
                            "but cannot be saved until the connection is restored.",
                            true));
                        break;

                    case LockVerificationResult.Ours:
                        _logger.LogInfo("CheckoutService: heartbeat verify — lock still ours; renewing.");
                        await _lockService.ForceRenewHeartbeat();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: heartbeat-verify task failed");
            }
            finally
            {
                Volatile.Write(ref _heartbeatVerifyInFlight, 0);
            }
        });
    }

    /// <summary>
    /// Demotes the current write-access session to read-only in place, without
    /// rebuilding the DI container or swapping the main view-model. Intended for
    /// the <see cref="WriteLockLost"/> handler and any other "lock lost" flow
    /// that needs to keep the UI alive.
    ///
    /// <para>Sequence:</para>
    /// <list type="number">
    ///   <item>Stop autosave and unhook the wake handler (idempotent — also done
    ///         by <see cref="HandleLockLossAsync"/>).</item>
    ///   <item>Release the write lock. <see cref="WriteLockService.Release"/>
    ///         re-reads the lock file and skips the delete if the <c>SessionGuid</c>
    ///         is no longer ours, so a foreign holder's claim is preserved.</item>
    ///   <item>Invoke <paramref name="beforeClose"/> so the caller can close the
    ///         <c>DatabaseContext</c> connection to D'.</item>
    ///   <item>Delete D' and its dirty marker (the Windows file-lock no longer
    ///         blocks the delete once the connection is closed).</item>
    ///   <item>Copy D → D'' via <see cref="SetupReadOnlySnapshotAsync"/>, which
    ///         also flips <see cref="Mode"/> to <see cref="CheckoutMode.ReadOnly"/>,
    ///         sets <see cref="IsReadOnlyMode"/>, and updates
    ///         <see cref="WorkingPath"/> to point at D''.</item>
    ///   <item>Call <see cref="WriteLockService.EnterReaderMode"/> so the reader
    ///         poll timer is started against D's lock file. This is what lets a
    ///         demoted session later be offered an in-app takeover prompt if the
    ///         current holder releases the lock cleanly — without it, the only
    ///         way back to write mode would be exiting and restarting the app.</item>
    ///   <item>Invoke <paramref name="afterOpen"/> so the caller can reopen the
    ///         <c>DatabaseContext</c> connection to the new <see cref="WorkingPath"/>
    ///         (= D'').</item>
    /// </list>
    ///
    /// <para>Returns <c>true</c> on successful demotion. Returns <c>false</c> if
    /// this instance was not in write mode, or if <see cref="SetupReadOnlySnapshotAsync"/>
    /// could not produce D'' (e.g., D is unreachable). On failure, <paramref name="afterOpen"/>
    /// is still invoked — but the caller should check the return value and surface an
    /// error, because the <c>DatabaseContext</c> has already been closed and may have
    /// no valid file to reopen against.</para>
    /// </summary>
    /// <param name="beforeClose">
    /// Async callback invoked after the lock is released but before D' is deleted.
    /// The caller must close the <c>DatabaseContext</c> connection here so that D'
    /// can be deleted cleanly on Windows.
    /// </param>
    /// <param name="afterOpen">
    /// Async callback invoked after D'' has been created and <see cref="WorkingPath"/>
    /// updated. The caller must reopen the <c>DatabaseContext</c> connection to
    /// <see cref="WorkingPath"/> here.
    /// </param>
    public async Task<bool> DemoteToReadOnlyAsync(
        Func<Task>? beforeClose = null,
        Func<Task>? afterOpen = null)
    {
        if (Mode != CheckoutMode.WriteAccess)
            return false;

        _logger.LogBreadcrumb("Demoting to read-only");

        // 1 & 2 — stop timers and release the lock (safe against foreign holders).
        StopAutoSave();
        _lockService.WakeDetected -= OnWake;
        _lockService.Release();

        // 3 — let the caller close the DatabaseContext connection to D'.
        if (beforeClose is not null)
        {
            try { await beforeClose(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: DemoteToReadOnly — beforeClose callback threw");
            }
        }

        // 4 — dispose of D' now that the connection is closed. When unsaved edits
        // exist (dirty marker present), D' and the marker are deliberately PRESERVED:
        // they are the only copy of that work, and the crash-recovery flow will offer
        // to restore or export it the next time this database is opened with write
        // access (the marker's hash check decides which). With no marker, nothing
        // unsaved is at stake and the files are cleaned up as before.
        if (WorkingPath != SourcePath && !string.IsNullOrEmpty(WorkingPath))
        {
            if (HasDirtyMarker)
            {
                _logger.LogInfo("CheckoutService: DemoteToReadOnly — preserving D' and dirty marker (unsaved changes kept for recovery).");
                _logger.LogBreadcrumb("Demote: unsaved changes preserved");
            }
            else
            {
                if (File.Exists(WorkingPath))
                {
                    try { File.Delete(WorkingPath); }
                    catch (Exception ex)
                    {
                        _logger.LogInfo($"CheckoutService: DemoteToReadOnly — could not delete D': {ex.Message}");
                    }
                }
                TryDeleteWalSidecars(WorkingPath);
                DeleteDirtyMarker();
            }
        }

        SessionDirty = false;

        // 5 — create D''. SetupReadOnlySnapshotAsync updates WorkingPath and Mode.
        var readOnlyPath = await SetupReadOnlySnapshotAsync();
        var success = readOnlyPath is not null;

        if (!success)
        {
            _logger.LogInfo("CheckoutService: DemoteToReadOnly — SetupReadOnlySnapshotAsync failed.");
            // Still flip Mode so nothing else tries to write.
            Mode = CheckoutMode.ReadOnly;
            SessionDirty = false;
        }

        // 5b — enter reader mode so the poll timer starts. Without this, a session
        // that lost write access would never notice the current holder releasing
        // cleanly and would stay stuck in read-only until restart.
        // EnterReaderMode reads the lock file on the share to populate CurrentHolder;
        // deadline-wrap it so a dead share cannot stall this (UI-thread) continuation.
        await NetworkFileOps.RunAsync(
            () => _lockService.EnterReaderMode(SourcePath), "enter reader mode");

        // 6 — let the caller reopen the DatabaseContext connection. Called even on
        // failure so the caller can decide how to surface the problem, but WorkingPath
        // may not point at a valid file.
        if (afterOpen is not null)
        {
            try { await afterOpen(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: DemoteToReadOnly — afterOpen callback threw");
                return false;
            }
        }

        return success;
    }

    /// <summary>
    /// Checks whether the lock file at D.lock still contains this session's
    /// <see cref="WriteLockService.SessionGuid"/>, distinguishing "network
    /// unreachable" from "lock genuinely lost."
    ///
    /// <para><b>Why this is tricky — the SMB caching trap:</b><br/>
    /// <see cref="NetworkFileOps"/> wraps every call in a 5-second timeout, which
    /// catches the obvious failure mode: the SMB client hangs for 20–60 seconds
    /// waiting for a dead server. But there is a second, subtler failure mode:
    /// after a few seconds of disconnection, the Windows SMB redirector flushes
    /// its metadata cache and begins returning <c>false</c> from
    /// <see cref="File.Exists"/> <i>immediately</i> — well within the 5-second
    /// deadline. The call completes, reports "file not found," and our timeout
    /// never fires. If we took that at face value, we would conclude the lock
    /// file was deleted by another user and trigger a lock-loss demotion — even
    /// though the file is still there on the (unreachable) server.</para>
    ///
    /// <para><b>The reachability probe:</b><br/>
    /// Whenever an operation returns a "not found" or unparseable result, we
    /// probe the <i>parent directory</i> of <see cref="SourcePath"/> as a
    /// sanity check. The directory is the share mount point — it will exist
    /// as long as the share is accessible, regardless of whether individual
    /// files have been created or deleted. If the directory probe times out
    /// or returns false, the share is unreachable and we return
    /// <see cref="LockVerificationResult.Unreachable"/> rather than
    /// <see cref="LockVerificationResult.NotOurs"/>. Only when the probe
    /// confirms the directory is reachable do we trust the lock-file result.</para>
    ///
    /// <para><b>Decision table:</b></para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>D.lock result</term>
    ///     <term>Directory probe</term>
    ///     <term>Conclusion</term>
    ///   </listheader>
    ///   <item><term>Timeout</term><term>—</term><term>Unreachable</term></item>
    ///   <item><term>Not found</term><term>Not found / timeout</term><term>Unreachable (SMB cache lie)</term></item>
    ///   <item><term>Not found</term><term>Found</term><term>NotOurs (genuinely deleted)</term></item>
    ///   <item><term>Found, ours</term><term>—</term><term>Ours</term></item>
    ///   <item><term>Found, other GUID</term><term>—</term><term>NotOurs</term></item>
    ///   <item><term>Found, parse error</term><term>Timeout</term><term>Unreachable</term></item>
    ///   <item><term>Found, parse error</term><term>Found</term><term>NotOurs (corrupt lock file)</term></item>
    /// </list>
    ///
    /// <para><b>Transient-rename retry:</b><br/>
    /// The heartbeat timer in <see cref="WriteLockService"/> renews the lock file
    /// using an atomic write-to-temp-then-rename pattern (<c>File.Move(.lock.tmp,
    /// .lock, overwrite:true)</c>). On local NTFS this is truly atomic — a concurrent
    /// reader always sees either the old or the new complete file. On SMB/network
    /// shares, however, the rename may briefly make the file invisible or unreadable
    /// to a remote reader (a sharing violation, a momentary "not found" from the
    /// redirector, or a truncated read that fails JSON parsing).<br/><br/>
    /// Because both the heartbeat and the save originate from the <i>same</i>
    /// process, this transient would be a self-inflicted false-positive lock loss:
    /// the save reads "not ours" and demotes to read-only even though no other user
    /// touched the lock. The consequence is immediate, irreversible session demotion
    /// with no confirmation dialog.<br/><br/>
    /// To guard against this, the first <see cref="LockVerificationResult.NotOurs"/>
    /// result triggers a single retry after a short pause
    /// (<see cref="LockRetryDelayMs"/>). The delay is long enough for any in-flight
    /// <c>File.Move</c> to settle (SMB renames complete in low milliseconds on a
    /// healthy LAN) but short enough to be imperceptible in the save path. A genuine
    /// lock loss — file deleted by another user, or GUID overwritten by a takeover —
    /// will still be <c>NotOurs</c> on the retry and proceed to demotion. Only a
    /// transient that self-heals within the retry window is absorbed.</para>
    /// </summary>
    /// <returns>
    /// <see cref="LockVerificationResult.Ours"/> — lock file exists and belongs to this session.<br/>
    /// <see cref="LockVerificationResult.NotOurs"/> — lock file is missing or belongs to another session
    ///     (and the network share is confirmed reachable, ruling out an SMB cache artifact).<br/>
    /// <see cref="LockVerificationResult.Unreachable"/> — the network share could not be reached;
    ///     the caller should treat this as a transient failure, not a lock-loss event.
    /// </returns>
    private async Task<LockVerificationResult> VerifyLockIsOursAsync()
    {
        var result = await VerifyLockIsOursOnceAsync();

        if (result == LockVerificationResult.NotOurs)
        {
            // A single retry absorbs transient "not found" / sharing-violation /
            // corrupt-read results caused by our own heartbeat's atomic rename
            // racing this read on a network share. A genuine lock loss (another
            // user deleted or overwrote the file) will still be NotOurs after
            // the delay and proceed to demotion normally.
            _logger.LogInfo(
                $"CheckoutService: lock verification returned NotOurs — retrying in " +
                $"{LockRetryDelayMs}ms to rule out a transient heartbeat-rename overlap.");
            await Task.Delay(LockRetryDelayMs);
            result = await VerifyLockIsOursOnceAsync();
        }

        return result;
    }

    /// <summary>
    /// Delay in milliseconds before retrying a <see cref="LockVerificationResult.NotOurs"/>
    /// result. Long enough for an in-flight <c>File.Move</c> to settle on a network share
    /// (SMB renames complete in low milliseconds on a healthy LAN), short enough to be
    /// imperceptible in the save path.
    /// </summary>
    internal const int LockRetryDelayMs = 200;

    /// <summary>
    /// Single-attempt lock verification. Checks whether D.lock exists, reads its
    /// JSON payload, and compares the <c>SessionGuid</c> to ours. Uses network-timeout
    /// wrappers and the reachability probe for all file I/O. Called by
    /// <see cref="VerifyLockIsOursAsync"/>, which wraps it in a one-retry loop.
    /// </summary>
    private async Task<LockVerificationResult> VerifyLockIsOursOnceAsync()
    {
        var lockPath = Path.ChangeExtension(SourcePath, ".lock");

        // ── Step 1: Does D.lock exist? ───────────────────────────────────────
        var (existsCompleted, exists) = await NetworkFileOps.ExistsAsync(lockPath);
        if (!existsCompleted)
            return LockVerificationResult.Unreachable;

        if (!exists)
        {
            // D.lock appears missing — but is the share actually reachable?
            // Probe the parent directory to distinguish a genuine deletion
            // from an SMB cache returning a stale "not found."
            var sourceDir = Path.GetDirectoryName(SourcePath)!;
            var (dirCompleted, dirExists) = await NetworkFileOps.DirectoryExistsAsync(sourceDir);
            if (!dirCompleted || !dirExists)
                return LockVerificationResult.Unreachable;

            // Share directory is reachable, so D.lock was genuinely deleted.
            LockLossNewHolder = null;
            return LockVerificationResult.NotOurs;
        }

        // ── Step 2: Read and parse D.lock ────────────────────────────────────
        // The read can THROW (rather than time out) when D.lock is momentarily locked
        // by another process — most commonly our OWN heartbeat timer, whose atomic
        // write-temp-then-rename briefly holds the file open. On a network share that
        // window widens under latency/loss and surfaces as ERROR_SHARING_VIOLATION
        // ("the process cannot access the file because it is being used by another
        // process"). NetworkFileOps deliberately lets such exceptions propagate (see its
        // class doc), so we absorb them here: a contended read is a transient,
        // self-inflicted condition, NOT a lock loss. We return Unreachable so the caller
        // keeps writer state and retries next cycle. Demoting to read-only because our own
        // heartbeat held the lock file for a few milliseconds would be exactly the
        // false-positive loss of write access this whole verification path exists to prevent.
        //
        // Deliberate trade-off: FileNotFoundException is an IOException, so a lock file
        // GENUINELY deleted in the race window between Step 1's ExistsAsync (true) and this
        // read is also classified Unreachable here — a true lock loss whose detection is
        // delayed by one cycle. The next verify (next save/heartbeat/wake) takes the
        // !exists branch in Step 1, runs the directory reachability probe, and classifies
        // it NotOurs correctly. One cycle of delayed true-positive is the price of never
        // demoting on a transient — consistent with the design bias documented on
        // VerifyLockIsOursAsync (transient beats destructive).
        bool readCompleted;
        string? json;
        try
        {
            (readCompleted, json) = await NetworkFileOps.ReadAllTextAsync(lockPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInfo(
                $"CheckoutService: lock read contended ({ex.GetType().Name}: {ex.Message}) " +
                "— treating as transient, keeping writer state.");
            return LockVerificationResult.Unreachable;
        }

        if (!readCompleted)
            return LockVerificationResult.Unreachable;

        try
        {
            var data = JsonSerializer.Deserialize<LockFileData>(json ?? "");
            if (data?.SessionGuid == _lockService.SessionGuid)
                return LockVerificationResult.Ours;

            // Lock file belongs to a different session — capture the new holder
            // so the WriteLockLost handler can name them in the banner.
            LockLossNewHolder = data;
            return LockVerificationResult.NotOurs;
        }
        catch
        {
            // JSON parse failure — could be a truncated read from a flaky connection.
            // Same reachability probe: if the share directory is unreachable, blame the network.
            var sourceDir = Path.GetDirectoryName(SourcePath)!;
            var (probeCompleted, _) = await NetworkFileOps.DirectoryExistsAsync(sourceDir);
            if (!probeCompleted)
                return LockVerificationResult.Unreachable;

            // Share is reachable but the lock file is genuinely unreadable (corrupt).
            // Treat as external interference — no known new holder.
            LockLossNewHolder = null;
            return LockVerificationResult.NotOurs;
        }
    }

    /// <summary>
    /// Result of <see cref="VerifyLockIsOursAsync"/>. See that method's XML doc
    /// for the full decision table.
    /// </summary>
    private enum LockVerificationResult
    {
        /// <summary>Lock file exists and contains this session's GUID.</summary>
        Ours,
        /// <summary>Lock file is missing or belongs to another session (share confirmed reachable).</summary>
        NotOurs,
        /// <summary>Network share could not be reached — treat as transient, not lock loss.</summary>
        Unreachable
    }

    /// <summary>
    /// Writes a dirty marker file alongside D' to track ungraceful exits. The marker
    /// carries <see cref="HashAtCheckout"/> (see <see cref="Models.DirtyMarkerData"/>)
    /// so that crash recovery can later prove D was not modified by another writer
    /// while the unsaved changes were stranded.
    /// </summary>
    /// <returns>True when the marker was written; false on any I/O failure.</returns>
    private bool WriteDirtyMarker()
        => TryWriteAllText(WorkingPath + ".dirty", JsonSerializer.Serialize(new DirtyMarkerData
        {
            Timestamp       = DateTime.UtcNow,
            HashAtCheckout  = HashAtCheckout,
            PendingSaveHash = _pendingSaveHash,
        }));

    /// <summary>
    /// Reads and parses a dirty marker file. Returns null when the file is missing,
    /// unreadable, or in the legacy bare-timestamp format — callers must treat null as
    /// "cannot verify" and take the conservative (export) recovery path.
    /// </summary>
    /// <param name="markerPath">Full path to the .dirty marker file.</param>
    internal static DirtyMarkerData? TryReadMarker(string markerPath)
    {
        try
        {
            return JsonSerializer.Deserialize<DirtyMarkerData>(File.ReadAllText(markerPath));
        }
        catch
        {
            return null; // missing file, legacy bare-timestamp marker, or corrupt JSON
        }
    }

    /// <summary>
    /// Convenience wrapper over <see cref="TryReadMarker"/> returning only the
    /// <see cref="Models.DirtyMarkerData.HashAtCheckout"/> value.
    /// </summary>
    /// <param name="markerPath">Full path to the .dirty marker file.</param>
    internal static string? TryReadMarkerHash(string markerPath)
        => TryReadMarker(markerPath)?.HashAtCheckout;

    /// <summary>
    /// True when a dirty marker currently exists for this session's working copy —
    /// i.e. unsaved user edits exist that have not yet been written back to D.
    /// Used by shutdown paths to decide whether a failed final save means real data
    /// is at stake (keep D' for recovery, warn the user) or nothing was lost.
    /// </summary>
    public bool HasDirtyMarker =>
        !string.IsNullOrEmpty(WorkingPath)
        && WorkingPath != SourcePath
        && File.Exists(WorkingPath + ".dirty");

    /// <summary>Deletes the dirty marker file. Non-throwing.</summary>
    private void DeleteDirtyMarker()
        => TryDelete(WorkingPath + ".dirty");

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
    public async Task<string?> SetupReadOnlySnapshotAsync()
    {
        ReadOnlyWorkingPath = ComputeReadOnlyWorkingPath(SourcePath);
        Directory.CreateDirectory(_workingDir);
        var roTmpPath = ReadOnlyWorkingPath + ".tmp";

        try
        {
            var copyCompleted = await NetworkFileOps.CopyAsync(SourcePath, roTmpPath);

            if (!copyCompleted)
            {
                _logger.LogInfo("CheckoutService: read-only setup copy timed out — network unreachable");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: failed to copy D to D''.tmp");
            TryDelete(roTmpPath);
            return null;
        }

        var (hashCompleted, roSourceHash) = await NetworkFileOps.ComputeHashAsync(SourcePath);

        if (!hashCompleted)
        {
            _logger.LogInfo("CheckoutService: read-only setup hash timed out — network unreachable");
            return null;
        }

        // roTmpPath is local — no timeout needed.
        var roCopyHash = ComputeHash(roTmpPath);
        if (roCopyHash != roSourceHash)
        {
            _logger.LogInfo("CheckoutService: D'' copy hash mismatch — retrying once.");

            var retryCompleted = await NetworkFileOps.CopyAsync(SourcePath, roTmpPath);

            if (!retryCompleted)
            {
                _logger.LogInfo("CheckoutService: read-only setup retry timed out — network unreachable");
                return null;
            }

            roCopyHash = ComputeHash(roTmpPath);

            if (roCopyHash != roSourceHash)
            {
                _logger.LogInfo("CheckoutService: D'' hash mismatch on retry — aborting.");
                TryDelete(roTmpPath);
                return null;
            }
        }

        // Drop any WAL sidecars from a previous session before the fresh copy lands.
        TryDeleteWalSidecars(ReadOnlyWorkingPath);
        try { File.Move(roTmpPath, ReadOnlyWorkingPath, overwrite: true); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckoutService: failed to rename D''.tmp to D''");
            TryDelete(roTmpPath);
            return null;
        }

        WorkingPath    = ReadOnlyWorkingPath;
        HashAtCheckout = roSourceHash;
        Mode           = CheckoutMode.ReadOnly;
        IsReadOnlyMode = true;
        _logger.LogInfo("CheckoutService: read-only setup — D'' created.");
        return ReadOnlyWorkingPath;
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
    /// Use this whenever D' is the source; use <see cref="NetworkFileOps.CopyAsync"/> when
    /// copying a file that is not being actively written by <c>DatabaseContext</c>
    /// (e.g., D → D' at checkout time, or D → D'' at snapshot time).
    /// <para>The destination should be a LOCAL path: the backup writes page-by-page —
    /// the slowest possible I/O pattern for SMB and impossible to bound with a
    /// deadline. The save pipeline snapshots locally, then streams the finished file
    /// to the network with the stall-aware <see cref="NetworkFileOps.CopyAsync"/>.</para>
    /// </summary>
    /// <param name="sourcePath">Path to the source SQLite database (typically D').</param>
    /// <param name="destPath">Path for the destination file (created or overwritten; should be local).</param>
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

        // The page-for-page backup inherits D's journal-mode header from D', which
        // DatabaseContext opens in WAL mode. This snapshot becomes the new D on the
        // network, and WAL must never be active on a network file — normalize to
        // rollback mode so the shipped file is a self-contained single .db.
        using var journalMode = dest.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode=DELETE";
        journalMode.ExecuteScalar();
    }

    /// <summary>Best-effort file deletion. Non-throwing — failures are silently ignored.</summary>
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    /// <summary>
    /// Best-effort deletion of the SQLite WAL sidecar files (<c>-wal</c>/<c>-shm</c>) for a
    /// database path. Working copies run in WAL mode (see <c>DatabaseContext</c>); whenever a
    /// working file is deleted or replaced wholesale, its sidecars must go with it — a stale
    /// <c>-wal</c> next to a different <c>.db</c> is a corruption hazard on the next open.
    /// </summary>
    /// <param name="dbPath">Path to the database file whose sidecars to remove.</param>
    private static void TryDeleteWalSidecars(string dbPath)
    {
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }

    /// <summary>Best-effort file write. Non-throwing; reports success so callers that
    /// depend on the file existing (the dirty marker) can react to a failure.</summary>
    private static bool TryWriteAllText(string path, string content)
    {
        try { File.WriteAllText(path, content); return true; } catch { return false; }
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
    Failed,
    /// <summary>Network share is unreachable (operation timed out). Do not attempt read-only fallback — D is inaccessible.</summary>
    NetworkUnreachable,
    /// <summary>
    /// Crash recovery was requested but the unsaved changes cannot be applied safely:
    /// D was modified since the crash, the marker carries no verifiable hash, or D/D'
    /// is missing. D' and its marker are left intact; the caller should offer to export
    /// the unsaved changes (then <see cref="CheckoutService.DiscardCrash"/>) and re-run
    /// a normal checkout. The write lock is NOT held when this is returned.
    /// </summary>
    RecoveryConflict
}

/// <summary>
/// Snapshot of the crash artifacts for a database, produced by
/// <see cref="CheckoutService.InspectCrashRecovery"/> so the startup flow can decide
/// which recovery offer to make.
/// </summary>
/// <param name="WorkingPath">Full path to the crashed session's working copy D'.</param>
/// <param name="HashAtCheckout">
/// Hash of D recorded in the dirty marker when the crashed session became dirty, or
/// null when the marker is legacy/unreadable (recovery must then take the export path).
/// </param>
/// <param name="WorkingCopyIntact">
/// True when D' passed a read-write integrity check (which also replays any hot
/// journal). False means the file is damaged and only archiving/export salvage applies.
/// </param>
public sealed record CrashRecoveryInfo(
    string WorkingPath,
    string? HashAtCheckout,
    bool WorkingCopyIntact);

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

/// <summary>
/// Why the writer lost their lock mid-session. Passed through the
/// <see cref="CheckoutService.WriteLockLost"/> event so the UI can build
/// a cause-specific banner.
/// </summary>
public enum WriteLockLostReason
{
    /// <summary>
    /// The lock file now belongs to a different session — another user (or the same
    /// user on another machine) took over the lock. <see cref="CheckoutService.LockLossNewHolder"/>
    /// contains the new holder's identity.
    /// </summary>
    TakenOver,

    /// <summary>
    /// The lock file was deleted (or is unreadable) while the share is reachable.
    /// Likely caused by antivirus, cloud-sync, or backup software.
    /// <see cref="CheckoutService.LockLossNewHolder"/> will be <c>null</c>.
    /// </summary>
    LockFileRemoved,
}

using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TermPoint.Services;

/// <summary>
/// Manages the checkout / save lifecycle for every database the app opens.
///
/// <para><b>The four rules for the shared database</b> (CLAUDE.md). The shared database is a SQLite file on a
/// network share, with no database server. The code that saves, locks, exits, switches, restores and recovers it is
/// held to four rules, and every change to those paths in this class is checked against them before it is laid out:</para>
/// <list type="number">
///   <item><b>The shared database only changes by one atomic rename of a copy that has been checked.</b></item>
///   <item><b>Never write it unless the lock is ours and the database is exactly as we last saw it.</b></item>
///   <item><b>Never delete the only copy of someone's unsaved work until a copy exists somewhere else.</b> The
///         guarantee must come from the data layer (for example, a closed connection), never from a dialog that
///         hides the controls: a modal blocks only the main window, not detached panels, sticky notes, focus-loss
///         saves or background work.</item>
///   <item><b>Never confuse the user.</b>
///         <list type="bullet">
///           <item>Every message says what happened, whether their work is safe, and what to do next.</item>
///           <item>Never ask about something the user has already dealt with.</item>
///           <item>One message per problem, in plain words: no "working copy" or "local copy", no paths, no log text.</item>
///         </list></item>
/// </list>
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
/// <para><b>Version gate:</b> a database can carry a <see cref="MinimumVersionToWrite"/> stamp
/// naming the oldest TermPoint allowed to edit it. At every write-access exit — after D' is
/// copied (or adopted, for crash recovery) and verified — the stamp is read from D'. If this app
/// is older than the stamp, the checkout is abandoned: the lock is released (never held, never
/// polled, so no "Take over" offer can loop back into the gate), D' is discarded (except during
/// crash recovery, where D' and its dirty marker are kept so recovery is offered again after the
/// update), and the session opens read-only on a D'' snapshot with
/// <see cref="RequiredAppVersion"/> set so the UI can explain why. It follows the same pattern as
/// the license-gated read-only mode. A brand-new database carries no stamp, so it passes the gate.
/// The gate fails SAFE (spec item 3, step 3c): if the stamp cannot be read at all (a copy another program is
/// holding), or a stamp this release must write cannot be written, it retries quietly and then
/// REFUSES write access (<see cref="CheckoutOutcome.VersionCheckUnavailable"/>) rather than letting a session edit
/// without having checked or without having stamped. A D' that SQLite reports as damaged is left to the open-time
/// integrity check, which runs next and refuses it. It also runs again whenever the open-time integrity handler
/// replaces D' with a fresh copy of D (<see cref="RefreshLocalCopyAfterFailedCheckAsync"/>).
/// See <see cref="ApplyVersionGateAsync"/>.</para>
///
/// <para><b>Always-on:</b> Both modes apply to every database, whether it is on a
/// network drive or a local drive. The indirection through D' or D'' is what prevents any
/// instance from ever holding D open, allowing the writer's atomic rename (D.tmp → D)
/// to succeed.</para>
///
/// <para><b>New databases:</b> this service never creates D and never runs a session on it.
/// A new database is created first — built in a local temp file and copied to the share by
/// <see cref="NewDatabaseFile.CreateAsync"/> — and is then checked out like any other, so every
/// session works on D' or D''. If D does not exist when <see cref="CheckoutAsync"/> is called,
/// the result is <see cref="CheckoutOutcome.SourceMissing"/>: no lock is taken and nothing is
/// created.</para>
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

    /// <summary>
    /// Version of the running app, compared against a database's
    /// <see cref="MinimumVersionToWrite"/> stamp by the version gate. Set at construction;
    /// defaults to the TermPoint assembly version, overridden in tests.
    /// </summary>
    private readonly Version _appVersion;

    // ── Test seams for the version gate ──────────────────────────────────────

    /// <summary>
    /// The minimum writer version this release stamps into databases it opens for writing.
    /// Defaults to <see cref="MinimumVersionToWrite.ThisReleaseRequires"/> (null in 1.2.3 —
    /// the raising code is dormant). Settable only so tests can exercise the stamping path.
    /// </summary>
    internal string? ThisReleaseRequires { get; set; } = MinimumVersionToWrite.ThisReleaseRequires;

    /// <summary>
    /// One-line reason stored beside the stamp. Defaults to
    /// <see cref="MinimumVersionToWrite.ThisReleaseReason"/>. Settable for tests only.
    /// </summary>
    internal string? ThisReleaseReason { get; set; } = MinimumVersionToWrite.ThisReleaseReason;

    /// <summary>
    /// When non-null, replaces <see cref="PlatformCapabilities.InstallChannel"/> when deciding
    /// whether this build may stamp a database. Null (the default) means "use the real channel",
    /// read at the moment of use rather than at construction. Settable for tests only.
    /// </summary>
    internal InstallChannel? ChannelOverride { get; set; }

    /// <summary>
    /// Reads the <see cref="MinimumVersionToWrite"/> stamp of the file at the given path, once. Defaults to
    /// <see cref="MinimumVersionToWrite.ReadStamp"/>. Settable only so tests can make the read fail a chosen
    /// number of times (the gate's quiet retries) and then fall through to the real read.
    /// </summary>
    internal Func<string, StampRead> StampReader { get; set; } = MinimumVersionToWrite.ReadStamp;

    /// <summary>
    /// Raises the stamp in the file at the first argument to the version in the second, with the reason in the
    /// third; returns true when it wrote, false when the existing stamp already covered it, and throws when it
    /// could not. Defaults to <see cref="MinimumVersionToWrite.Raise"/>. Settable only so tests can make a raise
    /// fail a chosen number of times (for instance by write-protecting D' for the first attempt).
    /// </summary>
    internal Func<string, string, string?, bool> StampRaiser { get; set; } = MinimumVersionToWrite.Raise;

    /// <summary>
    /// How the version gate waits between its quiet retries (<see cref="LocalCopyIntegrity.RetryDelay"/> in
    /// production: a real asynchronous delay, so the window stays alive). Settable only so tests can skip the
    /// wait and record that it was asked for.
    /// </summary>
    internal Func<TimeSpan, Task> GateRetryDelay { get; set; } = delay => Task.Delay(delay);

    /// <summary>
    /// DEBUG-ONLY fault injection for the version gate (spec item 3, step 3c): when non-null and its fault is
    /// <see cref="InjectedIntegrityFault.Stamp"/>, each of the gate's stamp-read attempts that the plan's budget
    /// covers reports the file as unreadable. Null (the default) means every real code path runs untouched. Only
    /// <c>App.Checkout</c> ever gets a plan, the very one <c>App.IntegrityFaults</c> holds, which is null in
    /// Release builds, so shipped builds never inject anything. An injected failure is only ever logged: it is
    /// never reported to the developers. Settable so tests can drive the plan directly.
    /// </summary>
    internal IntegrityFaultPlan? IntegrityFaultPlan { get; set; }

    /// <summary>
    /// DEBUG-ONLY fault injection (spec item 4d): when non-null, <see cref="CheckoutAsync"/> and
    /// <see cref="SetupReadOnlySnapshotAsync"/> fail on purpose as the plan dictates, so the failed-switch
    /// recovery and the "Couldn't Open the Database" prompt can be exercised by hand. Null (the default)
    /// means every real code path runs untouched. Only <c>App.Checkout</c> ever gets a plan, from
    /// <see cref="CheckoutFaultPlan.FromEnvironment"/>, which returns null in Release builds — so shipped
    /// builds never inject anything. Settable so tests can drive the plan directly.
    /// </summary>
    internal CheckoutFaultPlan? FaultPlan { get; set; }

    /// <summary>
    /// DEBUG-ONLY fault injection for <see cref="RestoreFromBackupAsync"/> (spec item 5): when non-null, a
    /// restore fails on purpose at the stage the plan names, so a severed network mid-restore, a damaged
    /// backup, a lost lock or an unconfirmed rename can be exercised by hand. Null (the default) means every
    /// real code path runs untouched. Only <c>App.Checkout</c> ever gets a plan, from
    /// <see cref="Services.RestoreFaultPlan.FromEnvironment"/>, which returns null in Release builds, so shipped
    /// builds never inject anything. Settable so tests can drive the plan directly.
    /// </summary>
    internal RestoreFaultPlan? RestoreFaultPlan { get; set; }

    // ── State ──────────────────────────────────────────────────────────────────

    private Timer?  _autoSaveTimer;
    private bool    _disposed;

    /// <summary>
    /// True while autosave is "on": set in <see cref="StartAutoSave"/>, cleared in <see cref="StopAutoSave"/>.
    /// In the app it always equals <c>_autoSaveTimer is not null</c>; it is a separate flag so
    /// <see cref="IsAutoSaveRunning"/> keeps its meaning when <c>startTimers</c> is false and no
    /// <see cref="Timer"/> exists (test-only mode).
    /// </summary>
    private bool    _autoSaveRunning;

    /// <summary>
    /// False only in tests that drive autosave by hand with <see cref="TickAutoSaveAsync"/>:
    /// <see cref="StartAutoSave"/> then creates no <see cref="Timer"/>. Always true in the app.
    /// </summary>
    private readonly bool _startTimers;

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
    /// All SQLite operations (reads and writes) target this path — D is never opened directly,
    /// so this never equals <see cref="SourcePath"/> for a real session.
    /// Empty until <see cref="CheckoutAsync"/> is called.
    /// </summary>
    public string WorkingPath { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of D at the time of the last successful checkout or save.
    /// Used to detect external modifications to D before overwriting it.
    /// Null until the first checkout has copied D.
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

    /// <summary>
    /// The lock holder exactly as <see cref="CheckoutAsync"/> saw it when it judged the lock inactive
    /// and returned <see cref="CheckoutOutcome.StaleHolder"/>; null at any other time. This is the
    /// "expected" side of the click-time re-check: <see cref="ForceCheckoutAsync"/> hands it to
    /// <see cref="WriteLockService.ForceAcquire"/>, which deletes the lock file only if it still holds
    /// this same session and heartbeat (spec item 9a). Cleared at the start of every checkout and once
    /// <see cref="ForceCheckoutAsync"/> has used it, so a takeover can never run on a stale snapshot.
    /// <para>Its local observation time (when this process first saw exactly this session and heartbeat)
    /// is kept by the lock service's <see cref="LockHeartbeatWatch"/> and read back through
    /// <see cref="GetTakeOverWait"/> (spec item 9b).</para>
    /// </summary>
    private LockFileData? _staleHolderAtDetection;

    /// <summary>
    /// The database's <see cref="MinimumVersionToWrite"/> stamp when the version gate tripped
    /// during the most recent checkout (this app is older than the stamp, so the session opened
    /// read-only); null when the gate did not trip. Read by the main window to pick the
    /// read-only banner wording and to skip the "someone else is editing" recovery message,
    /// which would be wrong here. Reset to null at the start of every checkout.
    /// </summary>
    public string? RequiredAppVersion { get; private set; }

    /// <summary>
    /// True once a save has been refused with <see cref="SaveOutcome.SourceModified"/>: the shared database was
    /// changed by someone else since this session opened it, so this session's changes can never be written to it
    /// (spec item 10, step 10a).
    ///
    /// <para><b>Sticky.</b> Nothing clears it while the session lives: not a later successful-looking save, not the
    /// user dismissing a banner. It is cleared only by a NEW checkout (the first thing <see cref="CheckoutAsync"/>
    /// does) and by <see cref="ResetAfterFailedOpenAsync"/>, which forgets the session. The main window reads it to
    /// decide whether the "save a copy / discard" choice applies; the matching event is
    /// <see cref="SaveConflictDetected"/>.</para>
    /// </summary>
    public bool HasSaveConflict { get; private set; }

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
    /// D'' snapshot. Unsaved changes in D' are never written over the new holder's
    /// work: D' and its dirty marker are kept, and at the next open with write access
    /// they are offered back (restored if D is unchanged, otherwise offered as a
    /// separate copy via "Save a Copy…").</para>
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
    /// the error is sticky and requires user attention (e.g. lock lost or a failed
    /// manual save).</para>
    /// <para>A <see cref="SaveOutcome.SourceModified"/> refusal does NOT raise this event: it raises
    /// <see cref="SaveConflictDetected"/> instead, which the main window shows as its own persistent banner.</para>
    /// <para><b>The outage notice</b> (spec item 28, finding F15): every "the database location isn't responding"
    /// failure, from a save of any kind or from the heartbeat's lock check, raises this event with the one text
    /// <see cref="SavePrompt.OutageNotice"/> and <c>autoDismiss: false</c>, so it stays up until the location responds
    /// again. The window clears it on <see cref="SaveCompleted"/> (a save worked) or on
    /// <see cref="LocationReachableAgain"/> (the heartbeat reached the lock file again); both clear only that notice
    /// (<see cref="SavePrompt.IsOutageNotice"/>), never another banner.</para>
    /// Autosave stops after <see cref="SaveOutcome.LockLost"/> or <see cref="SaveOutcome.SourceModified"/>; it
    /// continues on <see cref="SaveOutcome.CopyError"/> (transient — worth retrying).
    /// </summary>
    public event Action<string, bool>? SaveFailed;

    /// <summary>
    /// Raised on the UI thread when the heartbeat's lock check reads the lock file again and finds it is still this
    /// session's (<c>LockVerificationResult.Ours</c>): the database location responds. The window uses it to clear the
    /// outage notice (<see cref="SavePrompt.IsOutageNotice"/>) that an earlier failed check or save raised, and only
    /// that notice. Raised on EVERY such check, not just the first after an outage, so handlers must be idempotent
    /// (clearing an outage notice that is not on screen does nothing). Carries no data and changes no state in this
    /// class: it only reports a fact the heartbeat already found.
    /// </summary>
    public event Action? LocationReachableAgain;

    /// <summary>
    /// Raised on the UI thread when a save is refused with <see cref="SaveOutcome.SourceModified"/> (the shared
    /// database was changed elsewhere since this session opened it), after <see cref="HasSaveConflict"/> has been
    /// set. Replaces the <see cref="SaveFailed"/> call that used to be made here (spec item 10, step 10a).
    ///
    /// <para><b>Raised on EVERY refused save</b>, not just the first (a manual save after autosave stopped is refused
    /// the same way), so handlers must be idempotent.</para>
    /// </summary>
    public event Action? SaveConflictDetected;

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
    /// <param name="appVersion">
    /// Version of the running app, compared against a database's
    /// <see cref="MinimumVersionToWrite"/> stamp. Defaults to the TermPoint assembly version
    /// (or 0.0.0 if it cannot be read — which would block any stamped database, the safe
    /// direction). Override in unit tests to simulate older and newer releases.
    /// </param>
    /// <param name="startTimers">
    /// True (the default, and the only value the app uses) lets <see cref="StartAutoSave"/> create the real
    /// autosave timer. Tests pass false and drive one autosave tick at a time with
    /// <see cref="TickAutoSaveAsync"/>.
    /// </param>
    public CheckoutService(
        WriteLockService? lockService = null,
        IAppLogger? logger           = null,
        Action<Action>? dispatch     = null,
        string? workingDir           = null,
        Version? appVersion          = null,
        bool startTimers             = true)
    {
        _startTimers = startTimers;
        _appVersion  = appVersion ?? typeof(CheckoutService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _lockService = lockService ?? App.LockService;
        _logger      = logger      ?? App.Logger;
        _dispatch    = dispatch    ?? (action => Dispatcher.UIThread.Post(action));
        _workingDir  = workingDir  ?? DefaultWorkingDirectory;
    }

    /// <summary>
    /// The per-user folder that holds the local working copies D' and read-only snapshots D'' —
    /// <c>%AppData%\TermPoint\working\</c> (the platform equivalent elsewhere). The default for the
    /// constructor's <c>workingDir</c>, and shared with <see cref="DatabaseInstanceGuard"/>, whose
    /// guard files must sit in the same per-user location as the working copies they protect.
    /// </summary>
    public static string DefaultWorkingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermPoint",
        "working");

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
    /// <param name="ignoreObserverMode">
    /// When true, the voluntary observer setting (<see cref="AppSettings.OpenInReaderMode"/>) is
    /// ignored for THIS checkout only, so the database opens for editing even though the user has
    /// "open as observer" turned on. Pass it only for a database the user has just created (File →
    /// New, wizard step 3): they must be able to write into it straight away. The setting itself is
    /// untouched and applies to every later open. Nothing else is bypassed: the license gate, the
    /// write lock (a live holder still makes this read-only), the version gate and crash recovery
    /// behave exactly as without the flag. Defaults to false.
    /// </param>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> — lock acquired; use <see cref="WorkingPath"/> (= D') as dbPath. An orphaned <c>D.tmp</c> beside D has been deleted (only ever on this outcome: see <see cref="CleanupOrphanedTmpAsync"/>).<br/>
    /// <see cref="CheckoutOutcome.ReadOnly"/>    — a live lock exists, the license/observer mode forced read-only, or the database's <see cref="MinimumVersionToWrite"/> stamp is newer than this app (<see cref="RequiredAppVersion"/> is then non-null); use <see cref="WorkingPath"/> (= D'') as dbPath.<br/>
    /// <see cref="CheckoutOutcome.StaleHolder"/> — a stale lock exists; prompt the user (<see cref="StaleLockPrompt"/>), and on the basis of user's decision call <see cref="WaitForTakeOverConfirmationAsync"/> then <see cref="ForceCheckoutAsync"/>, or <see cref="SetupReadOnlySnapshotAsync"/> (followed by <see cref="StartReaderPollingAsync"/>).<br/>
    /// <see cref="CheckoutOutcome.RecoveryConflict"/> — recovery was requested but cannot be applied safely; D' and its marker are left intact for export.<br/>
    /// <see cref="CheckoutOutcome.SourceMissing"/> — D does not exist (and recovery was not requested). Nothing was locked or created; tell the user and stop. Create new databases with <see cref="NewDatabaseFile.CreateAsync"/> BEFORE calling this.<br/>
    /// <see cref="CheckoutOutcome.SourceUnreadable"/> — D exists but another program holds it open (or access is denied). The write lock taken for the attempt has been released and nothing was left behind; tell the user to try again in a moment.<br/>
    /// <see cref="CheckoutOutcome.VersionCheckUnavailable"/> — D' was copied but the version gate could not do its job (the stamp could not be read, or a stamp this release must write could not be written) even after quiet retries. Write access is refused, the lock has been released and D' removed (kept, with its marker, for a recovery open); offer "Try again" / "Close".<br/>
    /// <see cref="CheckoutOutcome.Failed"/>      — fatal error (e.g., copy hash mismatch); caller should fall back via <see cref="SetupReadOnlySnapshotAsync"/> to open read-only.
    /// </returns>
    public async Task<CheckoutOutcome> CheckoutAsync(
        string sourcePath, bool recoverWorkingCopy = false, bool ignoreObserverMode = false)
    {
        SourcePath = sourcePath;
        LockLossNewHolder = null;
        HasSaveConflict = false;    // a new checkout starts from a fresh copy of D — no conflict yet
        RequiredAppVersion = null;  // version gate has not tripped for this checkout (yet)
        _pendingSaveHash  = null;   // fresh session — no unconfirmed rename outstanding
        _staleHolderAtDetection = null;   // a takeover may only act on THIS checkout's stale judgement

        // DEBUG-ONLY fault injection (TERMPOINT_DEBUG_FAIL_CHECKOUT; null in Release builds and in
        // every ordinary run). Returns exactly what the real early exits return, BEFORE anything is
        // locked, copied or created — so, like those exits, it takes nothing it would have to give back
        // and leaves any crash-recovery data untouched.
        if (FaultPlan is { } plan && plan.TryConsumeCheckoutFault(out var injected))
        {
            _logger.LogInfo(
                $"[DEBUG fault injection] {CheckoutFaultPlan.EnvironmentVariableName}: " +
                $"failing the checkout of {sourcePath} as {injected}; {plan.Description}.");
            return injected switch
            {
                InjectedCheckoutFault.Unreachable => CheckoutOutcome.NetworkUnreachable,
                InjectedCheckoutFault.Missing     => CheckoutOutcome.SourceMissing,
                _                                 => CheckoutOutcome.SourceUnreadable
            };
        }

        // Tri-state probe: distinguishes "location answered, file absent" (Missing —
        // reported as SourceMissing) from any network failure (Unreachable — fast or
        // slow). A plain exists-check would misread a fast-failing dead share as a
        // missing file and send the user to "database not found" UX during an outage.
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

        // ── D does not exist ───────────────────────────────────────────────────
        // There is nothing to copy, and a session is never run on D itself (the old
        // "new database" shortcut did exactly that and has been removed). New databases
        // are created BEFORE checkout by NewDatabaseFile, so this is only reachable if D
        // was deleted/renamed in between, or the caller passed a wrong path. Report it
        // WITHOUT taking the lock or creating anything — an early return here also means
        // no .lock file is ever written next to a file that does not exist.
        if (!exists)
        {
            _logger.LogInfo("CheckoutService: source database does not exist");
            _logger.LogBreadcrumb("Checkout: SourceMissing", new() { ["path"] = sourcePath });
            return CheckoutOutcome.SourceMissing;
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
        // (D'') instead. D is known to exist here (SourceMissing returned above).
        // Skipped — and only this branch — when the caller says the user has just created this
        // database (ignoreObserverMode): it must open for editing. The license gate above and the
        // lock/version-gate logic below still apply.
        if (AppSettings.Current.OpenInReaderMode && !ignoreObserverMode)
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

            // Remember exactly what was judged inactive: ForceCheckoutAsync re-reads the lock file
            // at click time and only breaks it if it is still this session with this heartbeat.
            _staleHolderAtDetection = CurrentHolder;
            _logger.LogBreadcrumb("Checkout: StaleHolder", new() { ["path"] = sourcePath, ["holder"] = _lockService.DescribeHolder(CurrentHolder) });
            return CheckoutOutcome.StaleHolder;
        }

        if (!_lockService.IsWriter)
        {
            // Read-only mode: set up D'' via the shared helper so D is never held open.
            CurrentHolder           = _lockService.CurrentHolder;
            HolderIsLiveSameMachine = _lockService.HolderIsLiveSameMachine;
            var d2 = await SetupReadOnlySnapshotAsync();
            var outcome = d2 is not null ? CheckoutOutcome.ReadOnly : CheckoutOutcome.Failed;
            _logger.LogBreadcrumb($"Checkout: {outcome}", new() { ["path"] = sourcePath, ["holder"] = _lockService.DescribeHolder(CurrentHolder) });
            return outcome;
        }

        // We hold the lock. Capture whether we got it by reclaiming a dead session, so the UI
        // can tell the user we recovered it. (Cleared for clean acquisitions.)
        ReclaimedDeadSession = _lockService.ReclaimedDeadSession;
        CrashPoints.Hit("Checkout.AfterLockAcquired");

        // Recovery mode: adopt the crashed session's D' instead of copying D over it.
        if (recoverWorkingCopy)
            return await FinishRecoveryCheckoutAsync();

        // Write-access path: copy D to D' (existing logic).
        WorkingPath = ComputeWorkingPath(sourcePath);

        // We hold the lock — copy D to D'.
        Directory.CreateDirectory(_workingDir);

        var (hashCompleted, sourceHash, hashUnreadable) = await HashSourceAsync(sourcePath, "checkout hash");

        if (hashUnreadable)
        {
            // D exists but another program holds it exclusively (or access is denied). We have
            // ALREADY taken the write lock above, so give it back — otherwise the .lock file and
            // its heartbeat would outlive this failed checkout until the app exits, blocking
            // colleagues. Nothing was copied yet, so there is no D' to clean up.
            _lockService.Release();
            return CheckoutOutcome.SourceUnreadable;
        }

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

        // Copy D → D' and prove the copy is byte-identical to the D that was just hashed (one retry on a
        // mismatch). Whatever goes wrong, the lock is given back and the marker removed — the retry stage
        // also removes the bad copy — and the user is told the real reason.
        var copyOutcome = await CopyAndVerifyAsync(sourcePath, WorkingPath, sourceHash!, "checkout");
        if (copyOutcome != CopyVerifyOutcome.Ok)
        {
            _lockService.Release();
            DeleteDirtyMarker();
            return copyOutcome switch
            {
                CopyVerifyOutcome.Unreachable => CheckoutOutcome.NetworkUnreachable,
                CopyVerifyOutcome.Unreadable  => CheckoutOutcome.SourceUnreadable,
                _                             => CheckoutOutcome.Failed
            };
        }

        CrashPoints.Hit("Checkout.AfterCopyVerified");

        // D' is verified. Before granting write access, make sure this app is new enough to
        // edit this database (and, in a Store/DirectDownload release that requires it, stamp it).
        if (await ApplyVersionGateAsync(isRecovery: false) is { } gated)
            return gated;
        CrashPoints.Hit("Checkout.AfterVersionGate");

        // Write access is now certain and the lock is ours: only now may an orphaned D.tmp be removed.
        await CleanupOrphanedTmpOnceWriterAsync();

        Mode          = CheckoutMode.WriteAccess;
        SessionDirty  = true;
        CurrentHolder = null;

        _lockService.WakeDetected    += OnWake;
        _lockService.HeartbeatFailed += OnHeartbeatFailed;   // F11
        _logger.LogBreadcrumb("Checkout: WriteAccess", new() { ["path"] = sourcePath });
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// How much longer the user must wait before a confirmed takeover of the lock that
    /// <see cref="CheckoutAsync"/> judged inactive may go ahead (spec item 9b). The takeover requires that
    /// holder's heartbeat to have been seen UNCHANGED for <see cref="WriteLockService.StaleConfirmationSeconds"/>
    /// of this computer's own time, counted from the first time this process saw it — usually when the
    /// checkout read it. That is typically already satisfied by the time the user has read the prompt;
    /// otherwise the UI shows a short cancellable wait (<see cref="WaitForTakeOverConfirmationAsync"/>)
    /// before calling <see cref="ForceCheckoutAsync"/>. A live holder renews its heartbeat once a minute,
    /// so a heartbeat that stood still that long is not being renewed, whatever either computer's clock says;
    /// and if it did move during the wait, <see cref="ForceCheckoutAsync"/>'s click-time re-check returns
    /// <see cref="CheckoutOutcome.HolderActiveAgain"/>. Does no I/O.
    /// </summary>
    /// <returns>
    /// The time still to wait; <see cref="TimeSpan.Zero"/> when the takeover may proceed immediately, and
    /// also when there is no pending stale judgement (<see cref="ForceCheckoutAsync"/> refuses that case itself).
    /// </returns>
    public TimeSpan GetTakeOverWait()
        => _staleHolderAtDetection is { } expected
            ? _lockService.StaleConfirmationRemaining(expected)
            : TimeSpan.Zero;

    /// <summary>
    /// Waits out <see cref="GetTakeOverWait"/> without blocking the calling thread, so the UI stays alive
    /// and can offer a Cancel button. Sleeps on the lock service's own time source, then asks again, so a
    /// timer that fires fractionally early cannot cut the wait short. Nothing is read, deleted or created
    /// here: the lock file is only looked at afterwards, by <see cref="ForceCheckoutAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the user gives up waiting.</param>
    /// <returns>
    /// True when the full wait has elapsed and <see cref="ForceCheckoutAsync"/> may be called; false when
    /// <paramref name="cancellationToken"/> was cancelled first, or when the wait cannot be satisfied
    /// (the snapshot is no longer the one being timed). The caller should treat false exactly like
    /// "Open read-only".
    /// </returns>
    public async Task<bool> WaitForTakeOverConfirmationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // One full wait, plus a couple of tiny top-ups in case a timer fired fractionally early. A
            // wait that is still unsatisfied after that means the snapshot is not the one being timed
            // (it was replaced); refuse rather than wait forever.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var remaining = GetTakeOverWait();
                if (remaining <= TimeSpan.Zero) return true;
                await Task.Delay(remaining, _lockService.Time, cancellationToken);
            }
            return GetTakeOverWait() <= TimeSpan.Zero;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Breaks a stale lock and completes a checkout. Call after the user has confirmed
    /// they wish to take over the abandoned session (i.e., after
    /// <see cref="CheckoutAsync"/> returned <see cref="CheckoutOutcome.StaleHolder"/>)
    /// and after any wait that <see cref="GetTakeOverWait"/> asked for has run out
    /// (<see cref="WaitForTakeOverConfirmationAsync"/>; spec item 9b).
    ///
    /// <para><b>Click-time re-check (spec item 9a):</b> the prompt may have been open for minutes, so
    /// before anything is deleted <see cref="WriteLockService.ForceAcquire"/> re-reads the lock file and
    /// compares it with the holder <see cref="CheckoutAsync"/> judged inactive
    /// (<see cref="_staleHolderAtDetection"/>). Only the SAME lock (same session, same heartbeat) is
    /// broken; if the holder has woken up and resumed — or someone else took the lock — nothing is
    /// deleted or created and the result is <see cref="CheckoutOutcome.HolderActiveAgain"/>. A lock
    /// that has meanwhile vanished is simply created. There is a residual millisecond-scale race
    /// between the re-read and the delete, documented on <see cref="WriteLockService.ForceAcquire"/>.</para>
    /// </summary>
    /// <param name="recoverWorkingCopy">
    /// Same semantics as on <see cref="CheckoutAsync"/>: adopt the crashed session's
    /// existing D' instead of copying D over it, verifying D against the dirty-marker
    /// hash first. Pass the same value that was passed to the preceding
    /// <see cref="CheckoutAsync"/> call.
    /// </param>
    /// <returns>
    /// <see cref="CheckoutOutcome.WriteAccess"/> on success.
    /// <see cref="CheckoutOutcome.HolderActiveAgain"/> when the lock changed since it was judged
    /// inactive: nothing was deleted or created, <see cref="CurrentHolder"/> names who holds it now, and
    /// D' (and, in recovery mode, its dirty marker) are untouched. The caller should tell the user and
    /// open read-only.
    /// <see cref="CheckoutOutcome.ReadOnly"/> when the version gate tripped (the database's
    /// <see cref="MinimumVersionToWrite"/> stamp is newer than this app): the lock is released,
    /// D'' is already set up, and <see cref="RequiredAppVersion"/> is non-null.
    /// <see cref="CheckoutOutcome.VersionCheckUnavailable"/> when the version gate could not read (or raise) the stamp even after
    /// quiet retries: write access is refused, the lock is released again, and (outside recovery) D' is removed.
    /// <see cref="CheckoutOutcome.RecoveryConflict"/> when recovery was requested but cannot be applied safely.
    /// <see cref="CheckoutOutcome.SourceUnreadable"/> when D could not be read (held open by another program / access denied); the lock is released again.
    /// <see cref="CheckoutOutcome.Failed"/> if another instance won the race.
    /// </returns>
    public async Task<CheckoutOutcome> ForceCheckoutAsync(bool recoverWorkingCopy = false)
    {
        RequiredAppVersion = null;  // version gate has not tripped for this checkout (yet)

        // The snapshot the user's yes was given on. Single use: a second takeover needs a new
        // CheckoutAsync (and a new prompt). Without one there is nothing to compare the lock file
        // against, so refuse rather than break a lock blindly — the caller falls back to read-only.
        var expectedHolder = _staleHolderAtDetection;
        _staleHolderAtDetection = null;
        if (expectedHolder is null)
        {
            _logger.LogInfo("CheckoutService: ForceCheckout called without a preceding StaleHolder checkout — refusing to break the lock.");
            return CheckoutOutcome.Failed;
        }

        var (forceCompleted, acquireResult) = await NetworkFileOps.RunAsync(
            () => _lockService.ForceAcquire(expectedHolder), "force lock acquire");

        if (!forceCompleted)
        {
            _logger.LogInfo("CheckoutService: ForceCheckout lock acquire timed out — network unreachable");
            return CheckoutOutcome.NetworkUnreachable;
        }

        if (acquireResult == ForceAcquireResult.HolderActiveAgain)
        {
            // The holder woke up (or someone else took the lock) while the prompt was open.
            // ForceAcquire deleted and created nothing, and no D' has been touched, so (in recovery
            // mode) the crashed session's D' and dirty marker are exactly as they were.
            CurrentHolder = _lockService.CurrentHolder;
            _logger.LogBreadcrumb("ForceCheckout: HolderActiveAgain", new() { ["holder"] = _lockService.DescribeHolder(CurrentHolder) });
            return CheckoutOutcome.HolderActiveAgain;
        }

        if (!_lockService.IsWriter)
        {
            Mode          = CheckoutMode.ReadOnly;
            CurrentHolder = _lockService.CurrentHolder;
            return CheckoutOutcome.Failed;
        }
        CrashPoints.Hit("Checkout.AfterLockAcquired");

        // Recovery mode: adopt the crashed session's D' instead of copying D over it.
        if (recoverWorkingCopy)
            return await FinishRecoveryCheckoutAsync();

        // Lock acquired — now copy D to D' (same as CheckoutAsync write path).
        Directory.CreateDirectory(_workingDir);

        var (hashCompleted, sourceHash, hashUnreadable) = await HashSourceAsync(SourcePath, "ForceCheckout hash");

        if (hashUnreadable)
        {
            // Same as CheckoutAsync: D is held / unreadable, and we already own the (forced) lock —
            // release it rather than leave a live .lock behind. No D' has been touched yet.
            _lockService.Release();
            return CheckoutOutcome.SourceUnreadable;
        }

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
        catch (Exception ex) when (DatabaseValidator.IsLockedOrAccessDenied(ex))
        {
            _logger.LogInfo($"CheckoutService: ForceCheckout — cannot read D to copy it ({ex.GetType().Name}: {ex.Message})");
            _lockService.Release();
            DeleteDirtyMarker();
            return CheckoutOutcome.SourceUnreadable;
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

        CrashPoints.Hit("Checkout.AfterCopyVerified");

        // Same version gate as CheckoutAsync (see ApplyVersionGateAsync).
        if (await ApplyVersionGateAsync(isRecovery: false) is { } gated)
            return gated;
        CrashPoints.Hit("Checkout.AfterVersionGate");

        // Write access is now certain and the lock is ours (see CheckoutAsync).
        await CleanupOrphanedTmpOnceWriterAsync();

        Mode          = CheckoutMode.WriteAccess;
        SessionDirty  = true;
        CurrentHolder = null;

        _lockService.WakeDetected    += OnWake;
        _lockService.HeartbeatFailed += OnHeartbeatFailed;   // F11
        _logger.LogBreadcrumb("ForceCheckout: WriteAccess");
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// Starts watching the lock file for a session that opened read-only on a D'' snapshot after a
    /// stale-lock prompt — because the user chose "Open read-only", because the holder proved to be
    /// <see cref="CheckoutOutcome.HolderActiveAgain"/>, or because the takeover lost the create race.
    /// Without this the session would never learn that the holder has since released (or gone
    /// stale again): <see cref="WriteLockService.TryAcquire"/> does not start the poll timer in its
    /// stale branch. With it, a later clean release raises <see cref="WriteLockService.LockStateChanged"/>
    /// and the banner offers "Take over as writer" — which goes back through the whole checkout
    /// (and this prompt), so nothing is taken without a fresh decision.
    ///
    /// <para>Call it only after <see cref="SetupReadOnlySnapshotAsync"/> succeeded in those cases. It is
    /// deliberately not part of <see cref="SetupReadOnlySnapshotAsync"/>: the license-gated, observer
    /// and version-gated read-only sessions must NOT poll (no "Take over" may be offered to them).
    /// Wraps <see cref="WriteLockService.EnterReaderMode"/> in a network deadline; on timeout the
    /// session simply stays unpolled, exactly as before. Safe to call when already polling.</para>
    /// </summary>
    public async Task StartReaderPollingAsync()
    {
        if (string.IsNullOrEmpty(SourcePath)) return;

        var started = await NetworkFileOps.RunAsync(
            () => _lockService.EnterReaderMode(SourcePath), "start reader polling");

        if (started)
            CurrentHolder = _lockService.CurrentHolder;
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
    /// <para><b>Version gate:</b> the adopted D' is checked against its
    /// <see cref="MinimumVersionToWrite"/> stamp before the session resumes
    /// (<see cref="ApplyVersionGateAsync"/>). If this app is too old the outcome is
    /// <see cref="CheckoutOutcome.ReadOnly"/> and D' + marker are kept untouched, so recovery is
    /// offered again after the user updates. If the stamp cannot be read or raised the outcome is
    /// <see cref="CheckoutOutcome.VersionCheckUnavailable"/>, and D' + marker are likewise kept.</para>
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

        var (hashCompleted, sourceHash, hashUnreadable) = await HashSourceAsync(SourcePath, "recovery hash");
        if (hashUnreadable)
        {
            // D is held / unreadable, so the "D unchanged since the crash" proof cannot be made right
            // now. Release the lock and KEEP D' and its dirty marker untouched: the user's unsaved
            // edits stay recoverable and recovery is offered again on the next attempt.
            _lockService.Release();
            return CheckoutOutcome.SourceUnreadable;
        }
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
        CrashPoints.Hit("Recovery.AfterSourceVerified");

        // Version gate on the ADOPTED D' (its stamp is a superset of D's: stamps only go up).
        // On a trip, D' and its dirty marker are deliberately kept so recovery is offered
        // again once the user has updated TermPoint.
        if (await ApplyVersionGateAsync(isRecovery: true) is { } gated)
            return gated;

        // Write access is now certain and the lock is ours (see CheckoutAsync).
        await CleanupOrphanedTmpOnceWriterAsync();

        Mode           = CheckoutMode.WriteAccess;
        SessionDirty   = true;
        CurrentHolder  = null;

        _lockService.WakeDetected    += OnWake;
        _lockService.HeartbeatFailed += OnHeartbeatFailed;
        _logger.LogBreadcrumb("Recovery: WriteAccess (resumed crashed session)", new() { ["path"] = SourcePath });
        return CheckoutOutcome.WriteAccess;
    }

    /// <summary>
    /// The version gate (spec item 3, steps 3b and 3c). Called at each of the three write-access exits —
    /// <see cref="CheckoutAsync"/>, <see cref="ForceCheckoutAsync"/>, and
    /// <see cref="FinishRecoveryCheckoutAsync"/> — once D' is verified and the lock is held, but
    /// BEFORE <see cref="Mode"/> becomes <see cref="CheckoutMode.WriteAccess"/> and before the
    /// wake/heartbeat handlers are subscribed; and again by <see cref="RefreshWriterCopyAsync"/>, on the
    /// NEW D', whenever the open-time integrity handler replaces D' with a fresh copy of D (the first run
    /// judged a file that no longer exists, and a stamp raised into it is gone with it).
    ///
    /// <para><b>1. Read.</b> The <see cref="MinimumVersionToWrite"/> stamp is read from D' as a classified
    /// answer (<see cref="StampRead"/>): found, genuinely absent, <i>could not be read</i> (a copy that
    /// antivirus is still holding), or <i>damaged</i>. A read that could not be made is retried quietly —
    /// <see cref="LocalCopyIntegrity.MaxAttempts"/> attempts in all, <see cref="LocalCopyIntegrity.RetryDelay"/>
    /// apart, the same as the open-time integrity check — and if it still cannot be made, write access is
    /// REFUSED (see below). It is never treated as "no stamp": a gate that waved a locked file through would
    /// let an old app edit a database it is not allowed to.</para>
    ///
    /// <para><b>A damaged D'</b> (<see cref="StampReadKind.Damaged"/>) is neither retried nor refused here: the
    /// gate stops (no block, no raise) and the checkout continues, so the open-time integrity check — which
    /// runs next on every path into editing (startup and <c>SwitchDatabaseAsync</c>, which serves Open/Recent,
    /// take-over and the wizard) — refuses the file and handles the damage as spec item 5, step 5d designed
    /// (fresh copy, after which this gate runs again; the recovery chooser and one report if D itself is
    /// damaged). Refusing here instead would show the "in use" prompt for damage, and since D' is a verified
    /// byte copy of D, every Try again would copy the same damage and loop, never reaching the restore. A
    /// damaged file cannot pass that check, so it is never edited with its stamp unknown (user decision,
    /// 2026-10-14).</para>
    ///
    /// <para><b>2. Block.</b> If this app is older than the stamp, write access is refused and the session
    /// falls back to a read-only D'' snapshot, exactly like the license-gated read-only mode:</para>
    /// <list type="bullet">
    ///   <item>the lock is released and <see cref="WriteLockService.EnterReaderMode"/> is
    ///         deliberately NOT called — no poll timer, so the UI can never offer a "Take over"
    ///         that would loop straight back into this gate;</item>
    ///   <item>normally D' (and its WAL sidecars and dirty marker) are deleted, mirroring the
    ///         copy-failure branches; during crash recovery (<paramref name="isRecovery"/>) D' and
    ///         its dirty marker are KEPT, because they hold the user's only copy of unsaved edits
    ///         and recovery should be offered again after the update;</item>
    ///   <item><see cref="RequiredAppVersion"/> records the stamp for the banner.</item>
    /// </list>
    ///
    /// <para><b>3. Raise (dormant in 1.2.3).</b> When not blocked, and this release sets
    /// <see cref="ThisReleaseRequires"/>, and the install channel is <see cref="InstallChannel.Store"/> or
    /// <see cref="InstallChannel.DirectDownload"/>, the stamp is raised in D'. Writing into D' means it
    /// reaches D in the same save as the data it protects. A raise that fails is retried quietly like the
    /// read, and if it still fails write access is REFUSED: editing without the stamp would save new-format
    /// data where older versions can still edit it (user decision, 2026-10-14; before step 3c a failed raise
    /// was logged and ignored). A malformed <see cref="ThisReleaseRequires"/> is a bug in the release, not
    /// something waiting helps, so it is refused at once (and reported).</para>
    ///
    /// <para><b>Refusing write access</b> (<see cref="CheckoutOutcome.VersionCheckUnavailable"/>): the lock is
    /// released (no reader mode), D' with its sidecars and marker is deleted exactly as in the blocking path
    /// — except during crash recovery, where D' and its marker are kept so the unsaved changes are offered
    /// again — and <see cref="Mode"/> is read-only. No D'' is made: the caller tells the user and offers
    /// Try again / Close.</para>
    ///
    /// <para>A brand-new database (created by <see cref="NewDatabaseFile"/> just before checkout)
    /// has no stamp to honour, so it passes the gate; its schema is created by this very version.</para>
    /// </summary>
    /// <param name="isRecovery">
    /// True when called from crash recovery, where D' holds unsaved edits that must survive a trip or a refusal.
    /// </param>
    /// <returns>
    /// Null when the checkout should continue to write access. Otherwise the outcome to return:
    /// <see cref="CheckoutOutcome.ReadOnly"/> (D'' created), <see cref="CheckoutOutcome.Failed"/>
    /// (blocked, but D'' could not be created; the caller's usual fallback applies) or
    /// <see cref="CheckoutOutcome.VersionCheckUnavailable"/> (the stamp could not be read or raised).
    /// </returns>
    private async Task<CheckoutOutcome?> ApplyVersionGateAsync(bool isRecovery)
    {
        // ── 1. Read the stamp ─────────────────────────────────────────────────────────────
        var read = StampRead.None;
        var readFailure = await RunWithQuietRetriesAsync("reading the stamp", () =>
        {
            read = ReadStampOnce();
            return read.IsUnreadable ? read.Detail ?? "could not be read" : null;
        });
        if (readFailure is not null)
            return RefuseWriteAccessForUncheckedVersion(isRecovery, $"the stamp could not be read ({readFailure})");

        if (read.IsDamaged)
        {
            // Not this gate's to handle (see the method docs): the open-time integrity check runs next, refuses
            // the file and takes the damage path. No raise either — it could only fail on a damaged file, and a
            // refusal would turn damage back into the misleading "in use" loop.
            _logger.LogInfo(
                $"CheckoutService: version gate — the local copy is damaged ({read.Detail}); " +
                "leaving it to the open-time integrity check.");
            _logger.LogBreadcrumb("Checkout: version gate found the local copy damaged", new() { ["recovery"] = isRecovery.ToString() });
            return null;
        }

        var stamp = read.Value;   // null: the database is genuinely unstamped
        _logger.LogInfo($"CheckoutService: version gate — stamp {stamp ?? "none"}, running {_appVersion}.");

        // ── 2. Block ──────────────────────────────────────────────────────────────────────
        if (stamp is not null && MinimumVersionToWrite.Blocks(stamp, _appVersion))
        {
            _logger.LogInfo(
                $"CheckoutService: database requires TermPoint {stamp} to edit; running {_appVersion} — opening read-only.");
            _logger.LogBreadcrumb("Checkout: ReadOnly (version gate)", new()
            {
                ["path"]     = SourcePath,
                ["required"] = stamp,
                ["app"]      = _appVersion.ToString(),
            });

            StandDownFromWriteAccess(isRecovery);
            RequiredAppVersion = stamp;

            return await SetupReadOnlySnapshotAsync() is not null
                ? CheckoutOutcome.ReadOnly
                : CheckoutOutcome.Failed;
        }

        // ── 3. Raise (dormant) ────────────────────────────────────────────────────────────
        // Only a release that sets ThisReleaseRequires, running from a Store or direct-download install,
        // ever writes a stamp — never a dev/sideload build.
        var requires = ThisReleaseRequires;
        if (!string.IsNullOrWhiteSpace(requires)
            && (ChannelOverride ?? PlatformCapabilities.InstallChannel) is InstallChannel.Store or InstallChannel.DirectDownload)
        {
            // Waiting cannot fix a requirement that is not a version: refuse at once, and tell the developers.
            if (!Version.TryParse(requires.Trim(), out _))
            {
                _logger.LogError(
                    new InvalidOperationException($"ThisReleaseRequires '{requires}' is not a valid version."),
                    "CheckoutService: this release's MinimumVersionToWrite requirement is malformed — refusing write access.");
                return RefuseWriteAccessForUncheckedVersion(isRecovery, "this release's required version is malformed");
            }

            var raiseFailure = await RunWithQuietRetriesAsync("raising the stamp", () =>
            {
                try
                {
                    if (StampRaiser(WorkingPath, requires, ThisReleaseReason))
                    {
                        _logger.LogInfo($"CheckoutService: raised MinimumVersionToWrite to {requires}.");
                        _logger.LogBreadcrumb("Checkout: stamped MinimumVersionToWrite", new() { ["version"] = requires });
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    return $"{ex.GetType().Name}: {ex.Message}";
                }
            });
            if (raiseFailure is not null)
                return RefuseWriteAccessForUncheckedVersion(isRecovery, $"the stamp could not be raised ({raiseFailure})");
        }

        return null;
    }

    /// <summary>
    /// One attempt to read the stamp of D' for the version gate: the DEBUG-ONLY <c>stamp</c> fault first (each
    /// call counts as one attempt against the plan's budget and, while it fires, reports the file as
    /// unreadable — logged, never reported to the developers), then the real <see cref="StampReader"/>.
    /// </summary>
    /// <returns>The classified read; never throws (<see cref="MinimumVersionToWrite.ReadStamp"/> does not).</returns>
    private StampRead ReadStampOnce()
    {
        if (IntegrityFaultPlan is { } plan && plan.TryConsume(InjectedIntegrityFault.Stamp))
        {
            _logger.LogInfo(
                $"[DEBUG fault injection] {IntegrityFaultPlan.EnvironmentVariableName}: the version gate's stamp read " +
                $"reports the file as unreadable; {plan.Description}.");
            return StampRead.Couldnt("DEBUG fault injection: stamp read");
        }

        return StampReader(WorkingPath);
    }

    /// <summary>
    /// Runs one step of the version gate until it succeeds or the attempts run out — the same quiet retries
    /// as the open-time integrity check of the local copy: <see cref="LocalCopyIntegrity.MaxAttempts"/> attempts
    /// in all, <see cref="LocalCopyIntegrity.RetryDelay"/> apart (waited with <see cref="GateRetryDelay"/>, so
    /// the window stays alive and tests need not wait). No dialog, banner or report on the way: each failed
    /// attempt is one local log line.
    /// </summary>
    /// <param name="what">Short label for the log lines, e.g. "reading the stamp".</param>
    /// <param name="attempt">Runs the step once and returns null when it succeeded, otherwise a short description of why it did not. Must not throw.</param>
    /// <returns>Null when an attempt succeeded; otherwise the last attempt's description of the failure.</returns>
    private async Task<string?> RunWithQuietRetriesAsync(string what, Func<string?> attempt)
    {
        string? failure = null;
        for (var n = 1; n <= LocalCopyIntegrity.MaxAttempts; n++)
        {
            failure = attempt();
            if (failure is null)
                return null;

            _logger.LogInfo($"CheckoutService: version gate — {what} failed, attempt {n} of {LocalCopyIntegrity.MaxAttempts} ({failure}).");
            if (n < LocalCopyIntegrity.MaxAttempts)
                await GateRetryDelay(LocalCopyIntegrity.RetryDelay);
        }
        return failure;
    }

    /// <summary>
    /// The common first half of leaving write access at the version gate, whether the gate blocked or refused:
    /// gives the lock back WITHOUT entering reader mode (no poll timer, so no "Take over" can loop back into
    /// the gate), unhooks the wake and heartbeat handlers (a no-op at the three checkout exits, where they are
    /// not yet subscribed; needed when the gate runs again in a live write session after a fresh local copy),
    /// removes D' with its WAL sidecars and dirty marker — unless this is crash recovery, where D' and its marker
    /// hold the user's only copy of unsaved edits and are kept — and parks the session at read-only.
    /// </summary>
    /// <param name="isRecovery">True for a crash-recovery checkout (keep D' and its marker).</param>
    private void StandDownFromWriteAccess(bool isRecovery)
    {
        // Give the lock back, but do NOT enter reader mode (see ApplyVersionGateAsync).
        _lockService.Release();
        _lockService.WakeDetected    -= OnWake;
        _lockService.HeartbeatFailed -= OnHeartbeatFailed;

        if (!isRecovery)
        {
            TryDeleteWalSidecars(WorkingPath);
            TryDelete(WorkingPath);
            DeleteDirtyMarker();
        }

        CurrentHolder = null;
        Mode          = CheckoutMode.ReadOnly;
        SessionDirty  = false;
    }

    /// <summary>
    /// Refuses write access because the version gate could not do its job (spec item 3, step 3c): the stamp
    /// could not be read, or a stamp this release must write could not be written, even after the quiet
    /// retries. Gives up the write session (<see cref="StandDownFromWriteAccess"/>) and logs it. Nothing is
    /// reported to the developers: a file another program is holding is an ordinary local condition.
    /// </summary>
    /// <param name="isRecovery">True for a crash-recovery checkout (D' and its marker are kept).</param>
    /// <param name="why">Technical account for the local log only; never shown to the user.</param>
    /// <returns><see cref="CheckoutOutcome.VersionCheckUnavailable"/>.</returns>
    private CheckoutOutcome RefuseWriteAccessForUncheckedVersion(bool isRecovery, string why)
    {
        _logger.LogInfo($"CheckoutService: write access refused — the version check could not be completed: {why}.");
        _logger.LogBreadcrumb("Checkout: VersionCheckUnavailable", new() { ["recovery"] = isRecovery.ToString() });

        StandDownFromWriteAccess(isRecovery);
        RequiredAppVersion = null;   // the gate did not trip: nobody knows what the database requires
        return CheckoutOutcome.VersionCheckUnavailable;
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
    /// message, because the next autosave cycle will retry on its own. The exception is "the
    /// database location isn't responding": that failure always raises the one sticky outage notice
    /// (<see cref="SavePrompt.OutageNotice"/>), whoever started the save.
    /// </param>
    /// <returns>
    /// A <see cref="SaveOutcome"/> describing the result. <b>This method does not throw</b> (spec item 4,
    /// step 4c): a plain file error on D, D.tmp or D' (a sharing violation, access denied, a vanished
    /// file) and any exception nobody anticipated come back as <see cref="SaveOutcome.CopyError"/> with a
    /// <see cref="SaveFailed"/> banner (exactly one, with a <see cref="SavePrompt"/> text and no
    /// <see cref="IAppLogger.LogError"/> call, so no second banner; the problem is reported through
    /// <see cref="IAppLogger.ReportHandled"/> instead, see <see cref="ReportSaveFailure"/>), leaving D
    /// untouched and D' + the dirty marker in place. Callers
    /// (the Save button, autosave, the exit save in <see cref="ReleaseAsync"/>) can therefore rely on the
    /// outcome alone.
    /// </returns>
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
        catch (Exception ex)
        {
            // Final safety net. SaveAsyncCore turns every failure it expects into a SaveOutcome; this
            // catches the ones nobody expected (a thrown event handler, a lock-service fault, a bug)
            // so they can no longer escape to a caller. Each caller then sees the same thing as for
            // any other transient failure: CopyError and the normal "couldn't save" banner, with D'
            // and its dirty marker untouched. In particular ReleaseAsync at exit takes its deliberate
            // CopyError branch (lock parked via Suspend, D' + marker kept for recovery) instead of
            // reaching OnClosing / the UI-thread last-resort handler through an exception.
            //
            // ReportSaveFailure (not LogError): an exception we did not anticipate must still reach
            // Bugsnag, but as ONE path-free handled report — LogError would also raise ErrorLogged and
            // put a second, red banner next to the save banner (spec item 12).
            // Nothing is deleted here — we cannot know whether D.tmp or the local snapshot belong to
            // this save; a leftover D.tmp is overwritten by the next save, and the local snapshot is
            // swept at startup.
            ReportSaveFailure(SaveStep.Unexpected, ex,
                "unexpected exception in SaveAsync — treating as a failed save", isAutoSave);
            RaiseTransientSaveError(SavePrompt.UnexpectedMessage, isAutoSave);
            return SaveOutcome.CopyError;
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
    /// The banner text is <see cref="SavePrompt.ForTransientFailure"/> of <paramref name="message"/>:
    /// the retry note that fits how the save started (autosave: "TermPoint will try again
    /// automatically."; manual: "Try saving again in a moment."). The banner is marked auto-dismissing
    /// for an autosave (the next autosave cycle retries on its own) and stays until the next
    /// successful save for a manual one.
    ///
    /// <para>Not used for "the database location isn't responding": that failure has one notice of its own
    /// (<see cref="RaiseLocationUnreachable"/>) that never auto-dismisses.</para>
    /// </summary>
    /// <param name="message">A transient <see cref="SavePrompt"/> text (one that ends "Your changes are kept.").</param>
    /// <param name="isAutoSave">Whether the originating save was an autosave.</param>
    private void RaiseTransientSaveError(string message, bool isAutoSave)
        => RaiseSaveError(SavePrompt.ForTransientFailure(message, isAutoSave), isAutoSave);

    /// <summary>
    /// Raises the ONE notice for "the database location isn't responding" (spec item 28, finding F15) through
    /// <see cref="SaveFailed"/>, for a save of any kind (autosave, manual, the exit save) that timed out and for the
    /// heartbeat's lock check. The text is <see cref="SavePrompt.OutageNotice"/>: with unsaved changes it says they are
    /// kept and how they get saved (autosave retries; otherwise press Save), and with none it says so. The banner is
    /// ALWAYS sticky (<c>autoDismiss: false</c>), even for an autosave: it stays up until the location responds again
    /// (a save works, or the heartbeat reaches the lock file: <see cref="LocationReachableAgain"/>).
    ///
    /// <para>Only chooses the text and stickiness of a banner; it reads two facts and changes no state, so it cannot
    /// affect any save, lock or recovery decision. "Unsaved changes" is <see cref="HasDirtyMarker"/>, the signal the
    /// shutdown paths use; "autosave is on" is <see cref="IsAutoSaveRunning"/>, which in the app always equals the
    /// <c>AppSettings.AutoSaveEnabled</c> switch (the window starts and stops the timer with it) and, unlike the
    /// static setting, is a fact of this service, so "TermPoint will try again automatically" is true exactly when
    /// the timer runs.</para>
    /// </summary>
    private void RaiseLocationUnreachable()
        => RaiseSaveError(SavePrompt.OutageNotice(HasDirtyMarker, IsAutoSaveRunning), autoDismiss: false);

    /// <summary>
    /// The one place that dispatches <see cref="SaveFailed"/> for a failed save: raises it on the UI thread
    /// with <paramref name="text"/> exactly as given.
    /// </summary>
    /// <param name="text">The complete banner text.</param>
    /// <param name="autoDismiss">True when the banner may disappear by itself; false when it must stay.</param>
    private void RaiseSaveError(string text, bool autoDismiss)
        => _dispatch(() => SaveFailed?.Invoke(text, autoDismiss));

    /// <summary>
    /// The save steps already sent to the error reporter this session (see <see cref="ReportSaveFailure"/>).
    /// Guarded by <c>lock</c> on itself: saves run on background threads.
    /// </summary>
    private readonly HashSet<SaveStep> _reportedSaveSteps = new();

    /// <summary>
    /// Records a save failure that is NOT an ordinary retryable condition, without raising
    /// <see cref="IAppLogger.ErrorLogged"/> (so the save banner is the only banner). Replaces the
    /// <c>LogError</c> calls the save path used to make (spec item 12).
    ///
    /// <para>1. A LOCAL log line with the full original exception (paths in it are fine: it never leaves
    /// this computer). 2. A handled report to the error reporter, ONCE PER SESSION PER STEP: a persistent
    /// fault such as a full share would otherwise send one report per autosave cycle. The report carries a
    /// <see cref="SaveFailedException"/> (fixed text per step plus the original's type name, never its
    /// message, never an inner exception) and metadata of step, exception type, HRESULT and autosave flag:
    /// no paths, file names or user names. The banner is raised by the caller every time; only the report is
    /// throttled. Never throws.</para>
    /// </summary>
    /// <param name="step">Which step of the save failed.</param>
    /// <param name="original">The exception that was caught, or null when there is none.</param>
    /// <param name="logContext">Short description of the failure, for the local log line.</param>
    /// <param name="isAutoSave">Whether the failing save was an autosave (goes into the report's metadata).</param>
    private void ReportSaveFailure(SaveStep step, Exception? original, string logContext, bool isAutoSave)
    {
        try
        {
            _logger.LogInfo(original is null
                ? $"CheckoutService: {logContext}"
                : $"CheckoutService: {logContext} — {original}");

            bool firstTime;
            lock (_reportedSaveSteps)
                firstTime = _reportedSaveSteps.Add(step);
            if (!firstTime)
                return;

            var metadata = new Dictionary<string, string>
            {
                ["step"]          = step.ToString(),
                ["exceptionType"] = original?.GetType().Name ?? "none",
                ["hresult"]       = original is null ? "none" : $"0x{original.HResult:X8}",
                ["autoSave"]      = isAutoSave.ToString()
            };

            _logger.ReportHandled(
                SaveFailedException.Create(step, original?.GetType().Name),
                context:     $"Save failed: {step}",
                metadata:    metadata,
                metadataTab: "save",
                groupingKey: $"save-failed:{step}");
        }
        catch (Exception)
        {
            // Reporting must never turn a handled save failure into a crash.
        }
    }

    /// <summary>
    /// Classifies a plain file error from a save step that touches the shared location (the D.tmp copy, the
    /// D.tmp verify, the rename onto D), records it where appropriate, and returns the text for the
    /// transient save banner.
    ///
    /// <para>Three cases:</para>
    /// <list type="bullet">
    /// <item><description>A file another program holds open, or one we may not access
    /// (<see cref="DatabaseValidator.IsLockedOrAccessDenied"/>): an ordinary, retryable condition. Written to
    /// the local log only, nothing reported; banner <see cref="SavePrompt.InUseMessage"/>.</description></item>
    /// <item><description>A file or folder that is not there (<see cref="FileNotFoundException"/> /
    /// <see cref="DirectoryNotFoundException"/> — e.g. D.tmp deleted between the copy and the verify):
    /// unexpected, so it is reported once per step (<see cref="ReportSaveFailure"/>); the same
    /// <see cref="SavePrompt.InUseMessage"/> banner ("…or can't be accessed").</description></item>
    /// <item><description>Any other I/O error (disk full, network name deleted, …): reported the same way;
    /// banner <see cref="SavePrompt.WriteFailedMessage"/>.</description></item>
    /// </list>
    /// No case calls <c>LogError</c>, and no banner text contains the exception's message.
    /// </summary>
    /// <param name="ex">The exception the step threw.</param>
    /// <param name="step">Which step threw (<see cref="SaveStep.CopyToLocation"/>, <see cref="SaveStep.VerifyAtLocation"/> or <see cref="SaveStep.Rename"/>).</param>
    /// <param name="logContext">Short description of the failing step, for the local log line.</param>
    /// <param name="isAutoSave">Whether the failing save was an autosave (goes into the report's metadata).</param>
    /// <returns>The <see cref="SavePrompt"/> text to pass to <see cref="RaiseTransientSaveError"/>.</returns>
    private string DescribeSaveFileError(Exception ex, SaveStep step, string logContext, bool isAutoSave)
    {
        if (DatabaseValidator.IsLockedOrAccessDenied(ex))
        {
            _logger.LogInfo($"CheckoutService: {logContext} — file in use or access denied ({ex.GetType().Name}: {ex.Message})");
            return SavePrompt.InUseMessage;
        }

        ReportSaveFailure(step, ex, logContext, isAutoSave);
        return ex is FileNotFoundException or DirectoryNotFoundException
            ? SavePrompt.InUseMessage
            : SavePrompt.WriteFailedMessage;
    }

    /// <summary>
    /// Test seam: awaited inside <see cref="SaveAsync"/> after the snapshot has been streamed to D.tmp and
    /// before D.tmp is verified (step 4), with the D.tmp path. Lets a test deterministically make D.tmp
    /// vanish or become unreadable at exactly that moment — a window of microseconds that cannot otherwise
    /// be hit reliably — or throw an arbitrary exception to exercise the safety net in
    /// <see cref="SaveAsync"/>. Always null in the shipped app; never set outside tests.
    /// </summary>
    internal Func<string, Task>? BeforeTmpVerifyAsync { get; set; }

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
    /// Records the snapshot hash of a save whose rename has not been confirmed, in memory
    /// (<see cref="_pendingSaveHash"/>, for same-session self-heal in step 2) and in the
    /// dirty marker file (for cross-restart recognition by
    /// <see cref="FinishRecoveryCheckoutAsync"/>). The marker is only rewritten when one
    /// already exists — no marker means no unsaved edits, in which case a post-exit
    /// ghost is harmless (D' content and ghost content are identical) and the in-memory
    /// copy covers the rest of this session.
    ///
    /// <para><b>When it is called (F8):</b> <see cref="SaveAsyncCore"/> calls this immediately BEFORE the
    /// step-5 rename, not only after a rename that timed out or threw. Until the marker is deleted in
    /// step 6, a crash or power cut can strike at any point after a rename that did succeed; without the
    /// fingerprint in the marker, the next start finds D changed, matches neither the marker's
    /// <c>HashAtCheckout</c> nor a <c>PendingSaveHash</c>, and answers "Can't Restore Unsaved Changes"
    /// although D already holds every change. With the fingerprint recorded first, recovery recognises D as
    /// this session's own save (<c>matchesPending</c>) and the Restore works. Step 6 clears it again on
    /// success.</para>
    ///
    /// Internal so tests can simulate the rename-timeout state deterministically.
    /// </summary>
    /// <param name="hash">Hash of the D.tmp snapshot that is about to be renamed onto D (or whose rename is unconfirmed).</param>
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

        // Invariant guard — a working copy must never be D itself. CheckoutAsync always
        // hands out D' or D'' (the old "new database runs directly on D" shortcut was
        // removed in 1.2.3, spec item 4a), so this is unreachable in normal operation. It
        // stays as a cheap safety net: if WorkingPath ever equalled SourcePath, the
        // D' → D pipeline below would snapshot and rename D onto itself, so treat the save
        // as a no-op instead. (CleanupWorkingCopy, MarkDirty and HasDirtyMarker carry the
        // same WorkingPath != SourcePath protection for the same reason.)
        if (WorkingPath == SourcePath)
        {
            SessionDirty = false;
            if (releaseLockAfter) _lockService.Release();
            _dispatch(() => SaveCompleted?.Invoke());
            return SaveOutcome.Success;
        }

        // ── Steps 1–2: the lock is still ours, and D is still the version we last saw ──
        // The same two checks run again, as the final check, immediately before the rename (step 4b).
        // The stamp of D taken here is what that final check compares against (the cheap path).
        var (firstRefusal, sourceStamp) = await CheckLockAndSourceAsync(
            isAutoSave, isFinalCheck: false, stampAtFirstCheck: null);
        if (firstRefusal is { } refused)
            return refused;

        CrashPoints.Hit("Save.AfterSourceCheck");

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
                // D' itself is the failing party here (the share has not been touched yet). BackupSqliteDatabase
                // opens D' with Mode=ReadWrite, so a working copy that has vanished (on macOS an open file can
                // be deleted under a running app) fails the open instead of being re-created EMPTY and pushed
                // over D. Say so plainly rather than quoting SQLite's "unable to open database file".
                ReportSaveFailure(SaveStep.Snapshot, ex, "local save snapshot failed", isAutoSave);
                if (File.Exists(WorkingPath))
                    RaiseTransientSaveError(SavePrompt.PrepareFailedMessage, isAutoSave);
                else
                    // Not transient: retrying can never succeed, so no retry note and the banner is sticky
                    // even for an autosave.
                    RaiseSaveError(SavePrompt.SessionFileMissingMessage, autoDismiss: false);
                return SaveOutcome.CopyError;
            }
            CrashPoints.Hit("Save.AfterLocalSnapshot");

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
                ReportSaveFailure(SaveStep.SnapshotHash, ex, "could not hash local save snapshot", isAutoSave);
                RaiseTransientSaveError(SavePrompt.PrepareFailedMessage, isAutoSave);
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
                    RaiseLocationUnreachable();
                    return SaveOutcome.CopyError;
                }
            }
            catch (Exception ex)
            {
                // A D.tmp another program holds open (or a read of the local snapshot that is refused)
                // is a retryable lock/access condition — see DescribeSaveFileError. The delete is
                // best effort: it fails harmlessly while the other program still holds D.tmp, and a
                // leftover D.tmp is simply overwritten by the next save.
                var msg = DescribeSaveFileError(ex, SaveStep.CopyToLocation, "D.tmp copy failed", isAutoSave);
                await NetworkFileOps.DeleteAsync(tmpPath);
                RaiseTransientSaveError(msg, isAutoSave);
                return SaveOutcome.CopyError;
            }
            CrashPoints.Hit("Save.AfterTmpCopy");
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
        //
        // ComputeHashAsync propagates open errors: D.tmp may have been deleted between the copy and
        // here, or an antivirus / backup / sync tool may hold it exclusively. Either way nothing has
        // been renamed yet, so D is untouched; delete the D.tmp this save wrote (best effort, deadline
        // bounded) and report a clean CopyError.
        if (BeforeTmpVerifyAsync is { } beforeVerify)
            await beforeVerify(tmpPath);   // test seam — null in production

        bool verifyCompleted;
        string? tmpHash;
        try
        {
            (verifyCompleted, tmpHash) = await NetworkFileOps.ComputeHashAsync(tmpPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var msg = DescribeSaveFileError(ex, SaveStep.VerifyAtLocation, "D.tmp verification could not read the file", isAutoSave);
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseTransientSaveError(msg, isAutoSave);
            return SaveOutcome.CopyError;
        }

        if (!verifyCompleted)
        {
            _logger.LogInfo("CheckoutService: D.tmp verification hash stalled — network unreachable");
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseLocationUnreachable();
            return SaveOutcome.CopyError;
        }

        if (!string.Equals(tmpHash, newSourceHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInfo("CheckoutService: D.tmp does not match the local snapshot — torn transfer; aborting save.");
            _logger.LogBreadcrumb("Save: TornTransfer");
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseTransientSaveError(SavePrompt.VerifyFailedMessage, isAutoSave);
            return SaveOutcome.CopyError;
        }

        CrashPoints.Hit("Save.AfterTmpVerify");

        // ── Step 4b: Final check — the lock is still ours and D is still unchanged (F12) ────────────
        // Steps 1–2 ran before the snapshot and the upload. The upload takes seconds on a slow link, and
        // hours when the laptop goes to sleep mid-save ("click X, then shut the lid" starts the exit save
        // and suspends the machine before it ends). Meanwhile our lock can have gone stale and another
        // user taken it over and saved; renaming D.tmp onto D now would silently overwrite their save.
        // So the same two checks run again, as the last thing before the rename. They cost one lock-file
        // read and one stat of D (the stamp from the first check is compared; D is hashed again only if
        // the stamp changed). If either refuses, D is untouched: return the refusal; the working copy D'
        // and its dirty marker stay, exactly as for a refusal at the start, so nothing the user typed is
        // lost. D.tmp is deleted only while the lock is still ours (rule 2: no write to the share without
        // the lock). D.tmp has one name for every writer, so once the lock has changed hands the file may
        // be the new holder's save in progress; ours is left for the next lock holder, whose checkout
        // deletes an orphaned D.tmp (CleanupOrphanedTmpAsync) and whose save overwrites it.
        //
        // A gap of microseconds between the last check and the rename remains: another user's rename can
        // land in it. It cannot be closed on a plain file share, which has no compare-and-swap and no
        // server to arbitrate; the lock and the final check make it a matter of microseconds instead of
        // the length of the upload, and it is accepted.
        var (finalRefusal, _) = await CheckLockAndSourceAsync(
            isAutoSave, isFinalCheck: true, stampAtFirstCheck: sourceStamp);
        if (finalRefusal is { } refusedAtFinalCheck)
        {
            var lockStillOurs = refusedAtFinalCheck != SaveOutcome.LockLost;
            _logger.LogInfo("CheckoutService: the final check before the rename refused the save — " +
                            (lockStillOurs ? "D.tmp discarded." : "lock lost, D.tmp left for the new holder's cleanup."));
            _logger.LogBreadcrumb("Save: refused at final check", new() { ["outcome"] = refusedAtFinalCheck.ToString() });
            if (lockStillOurs)
                await NetworkFileOps.DeleteAsync(tmpPath);
            return refusedAtFinalCheck;
        }

        CrashPoints.Hit("Save.AfterFinalCheck");

        // ── Step 5: Atomically rename D.tmp → D ──────────────────────────────
        // Record the snapshot's fingerprint BEFORE the rename (F8), in memory and in the dirty marker.
        // Until step 6 deletes the marker, a crash or power cut can strike after a rename that DID
        // succeed. If the marker lacked the fingerprint, the next start would see D changed, match
        // neither HashAtCheckout nor a pending save hash, and refuse the Restore with "Can't Restore"
        // although D already holds every change. With it recorded, recovery recognises D as this
        // session's own save (matchesPending) and the Restore works. Step 6 clears it on success. The
        // failure branches below record it again, which changes nothing: they predate this call, and an
        // unconfirmed rename may still land.
        RecordPendingSaveHash(newSourceHash);
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
                    RaiseLocationUnreachable();
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
            var msg = DescribeSaveFileError(ex, SaveStep.Rename, "D.tmp rename failed", isAutoSave);
            await NetworkFileOps.DeleteAsync(tmpPath);
            RaiseTransientSaveError(msg, isAutoSave);
            return SaveOutcome.CopyError;
        }

        CrashPoints.Hit("Save.AfterRename");

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
        CrashPoints.Hit("Save.AfterMarkerDelete");
        if (releaseLockAfter)
            _lockService.Release();

        _logger.LogInfo("CheckoutService: Save completed successfully.");
        _logger.LogBreadcrumb("Save: Success");
        _dispatch(() => SaveCompleted?.Invoke());
        return SaveOutcome.Success;
    }

    /// <summary>
    /// What a save sees of the shared database D without reading its content: the file's size and its
    /// last-write time (UTC), read in one stat. Step 2 of a save keeps the stamp it took before hashing D; the
    /// final check before the rename takes it again, and re-hashes D only when the two differ. A write to D
    /// (another user's rename onto it, or any program changing it) changes the last-write time, so a difference
    /// is the cue to look closer; a stamp that has not changed is accepted as "D has not changed" (a hash
    /// would only confirm it, at the cost of reading the whole database over the network).
    /// </summary>
    /// <param name="Length">The file's size in bytes.</param>
    /// <param name="LastWriteUtc">The file's last-write time, UTC.</param>
    private readonly record struct SourceStamp(long Length, DateTime LastWriteUtc);

    /// <summary>
    /// Takes a <see cref="SourceStamp"/> of the shared database D under the network deadline, reporting a plain
    /// file error as a result instead of letting it escape — the stat counterpart of <see cref="HashSourceAsync"/>.
    /// The read is one metadata round-trip, run through <see cref="NetworkFileOps.RunAsync{T}(Func{T}, string, string?, string?)"/>
    /// so it gets the same <see cref="NetworkFileOps.TimeoutMs"/> deadline (and test fault seam) as every other
    /// read of the share. A missing D throws <see cref="FileNotFoundException"/> from <see cref="FileInfo.Length"/>,
    /// which is an <see cref="IOException"/> and so lands in the unreadable case, as a missing D does for the hash.
    /// </summary>
    /// <param name="path">Full path of D.</param>
    /// <param name="stage">Short label for the log line, e.g. "save: source stamp".</param>
    /// <returns>
    /// <c>(true, stamp, false)</c> when the stat completed within the deadline; <c>(false, default, false)</c> when
    /// it timed out; <c>(false, default, true)</c> when it threw <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/>. Any other exception type still propagates.
    /// </returns>
    private async Task<(bool Completed, SourceStamp Stamp, bool Unreadable)> StatSourceAsync(string path, string stage)
    {
        try
        {
            var (completed, stamp) = await NetworkFileOps.RunAsync(
                () =>
                {
                    var info = new FileInfo(path);
                    return new SourceStamp(info.Length, info.LastWriteTimeUtc);   // Length throws if D is missing
                },
                "File.Stat", path, null);
            return (completed, stamp, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInfo($"CheckoutService: {stage} — cannot read the source database ({ex.GetType().Name}: {ex.Message})");
            return (false, default, true);
        }
    }

    /// <summary>
    /// The two checks a save makes before it may write to the shared database D, run at the start of the save
    /// (steps 1–2) and again, as the final check, immediately before the rename onto D (step 4b, F12).
    ///
    /// <para><b>Step 1 — the lock is still ours.</b> <see cref="VerifyLockIsOursAsync"/> with its one retry. A
    /// share that cannot be reached is a transient <see cref="SaveOutcome.CopyError"/>; a lock file that was merely
    /// REMOVED (antivirus, cloud-sync — no new holder) is re-created atomically by
    /// <see cref="TryRecoverRemovedLockAsync"/> and the save goes on; any other loss is
    /// <see cref="SaveOutcome.LockLost"/> after <see cref="HandleLockLossAsync"/>.</para>
    ///
    /// <para><b>Step 2 — D is still the version we last saw.</b> Skipped when <see cref="HashAtCheckout"/> is null.
    /// Otherwise D is stat'ed (<see cref="StatSourceAsync"/>) and then hashed, the stamp being read BEFORE the hash
    /// so that any change after it shows up as a difference later. A hash that differs from
    /// <see cref="HashAtCheckout"/> is first compared with <see cref="_pendingSaveHash"/> (the ghost-rename
    /// self-heal: our own delayed rename landed); otherwise it is <see cref="SaveOutcome.SourceModified"/> with
    /// <see cref="HasSaveConflict"/> set and <see cref="SaveConflictDetected"/> raised. A timeout is an
    /// unreachable-network <see cref="SaveOutcome.CopyError"/>; a file error on D is an in-use
    /// <see cref="SaveOutcome.CopyError"/>.</para>
    ///
    /// <para><b>The final check is cheap.</b> When <paramref name="isFinalCheck"/> is true and
    /// <paramref name="stampAtFirstCheck"/> is given, D is only stat'ed; if its size and last-write time are
    /// exactly those of the first check, D has not been written since and the (whole-file, over-the-network) hash
    /// is skipped. If either differs, D is hashed and compared exactly as in the first check — a difference of the
    /// stamp alone (a reader touching the file, say) therefore costs a hash but never a false refusal.</para>
    ///
    /// <para><b>Crash points.</b> <c>Save.AfterLockCheck</c> is hit between the two steps of the FIRST check only;
    /// the final check hits none (its own point, <c>Save.AfterFinalCheck</c>, is hit by the caller), so a save
    /// still reaches each point once and in catalogue order.</para>
    ///
    /// <para>Writes nothing to D, D.tmp or D'. The caller owns any D.tmp cleanup after a refusal.</para>
    /// </summary>
    /// <param name="isAutoSave">True when the autosave timer started the save; selects the auto-dismissing banner text.</param>
    /// <param name="isFinalCheck">False for the check at the start of the save, true for the one before the rename.</param>
    /// <param name="stampAtFirstCheck">
    /// The stamp the first check returned, for the cheap comparison of the final check; null on the first check, or
    /// when the first check took none (<see cref="HashAtCheckout"/> was null), in which case the final check hashes.
    /// </param>
    /// <returns>
    /// <c>Refusal</c>: null when the save may continue, otherwise the <see cref="SaveOutcome"/> the save must return
    /// (any banner or event has already been raised). <c>Stamp</c>: the stamp of D this check took (null when step 2
    /// was skipped, or when the final check found it unchanged and so has nothing newer to report).
    /// </returns>
    private async Task<(SaveOutcome? Refusal, SourceStamp? Stamp)> CheckLockAndSourceAsync(
        bool isAutoSave, bool isFinalCheck, SourceStamp? stampAtFirstCheck)
    {
        // ── Step 1: Verify we still hold the lock ─────────────────────────────
        var lockResult = await VerifyLockIsOursAsync();

        if (lockResult == LockVerificationResult.Unreachable)
        {
            _logger.LogInfo("CheckoutService: lock verification timed out — network unreachable");
            RaiseLocationUnreachable();
            return (SaveOutcome.CopyError, null);
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
                return (SaveOutcome.LockLost, null);
            }
        }
        if (!isFinalCheck)
            CrashPoints.Hit("Save.AfterLockCheck");

        // ── Step 2: Verify D has not been externally modified ─────────────────
        if (HashAtCheckout is null)
            return (null, null);

        // 2a. Stat D BEFORE hashing it, so a change after this read always shows up as a difference
        //     between two stamps (a change between the stat and the hash is caught by the hash itself,
        //     and also changes the stamp the final check will see). Like the hash below, a plain file
        //     error on D is reported as a result, and nothing has been written yet, so there is nothing
        //     to clean up.
        var (statCompleted, stampNow, statUnreadable) =
            await StatSourceAsync(SourcePath, isFinalCheck ? "save: final source stamp" : "save: source stamp");

        if (statUnreadable)
        {
            RaiseTransientSaveError(SavePrompt.InUseMessage, isAutoSave);
            return (SaveOutcome.CopyError, null);
        }

        if (!statCompleted)
        {
            _logger.LogInfo("CheckoutService: source stat timed out — network unreachable");
            RaiseLocationUnreachable();
            return (SaveOutcome.CopyError, null);
        }

        // 2b. The cheap path of the final check: size and last-write time are exactly what the first
        //     check saw, so nobody has written D since. Skip the hash (it reads the whole database over
        //     the network). The first check never takes this path: it has nothing to compare with.
        if (isFinalCheck && stampAtFirstCheck is { } firstStamp && firstStamp == stampNow)
            return (null, stampNow);

        // 2c. Hash D. HashSourceAsync reports a plain file error on D (another program holds it
        //     exclusively, access denied, file gone) as a result instead of letting the IOException escape.
        var (hashCompleted, currentSourceHash, sourceUnreadable) =
            await HashSourceAsync(SourcePath, isFinalCheck ? "save: final source hash" : "save: source hash");

        if (sourceUnreadable)
        {
            RaiseTransientSaveError(SavePrompt.InUseMessage, isAutoSave);
            return (SaveOutcome.CopyError, null);
        }

        if (!hashCompleted)
        {
            _logger.LogInfo("CheckoutService: source hash timed out — network unreachable");
            RaiseLocationUnreachable();
            return (SaveOutcome.CopyError, null);
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
                // Fall through — the current save proceeds normally.
            }
            else
            {
                _logger.LogInfo("CheckoutService: source hash mismatch — the database was modified outside this session; save refused.");
                _logger.LogBreadcrumb("Save: SourceModified");
                // A genuine conflict the user must resolve (spec item 10, step 10a). Not a SaveFailed
                // banner: the sticky state is HasSaveConflict, and the main window shows its own
                // persistent banner (Save a Copy… / Discard Changes) when SaveConflictDetected fires.
                HasSaveConflict = true;
                _dispatch(() => SaveConflictDetected?.Invoke());
                return (SaveOutcome.SourceModified, null);
            }
        }

        return (null, stampNow);
    }

    /// <summary>
    /// Replaces the shared database D with the backup at <paramref name="backupPath"/>, safely, using the
    /// shared <see cref="DatabaseRestore"/> routine: the backup is staged and integrity-checked locally, the
    /// current D is kept beside itself as a verified "before restore" copy, and the backup reaches D through
    /// <c>D.tmp</c> and the save's atomic rename. Any failure before the rename leaves D exactly as it was. See
    /// <see cref="DatabaseRestore"/> for the stages and <see cref="RestoreOutcome"/> for the outcomes.
    ///
    /// <para><b>Gating:</b> only a session that holds write access can restore. A reader, a read-only licence or
    /// a session that has lost its lock gets <see cref="RestoreOutcome.NotWriter"/> with nothing touched. The
    /// restore takes the SAME in-flight gate as <see cref="SaveAsync"/>, so a save and a restore can never
    /// interleave: if a save (or an autosave) is running the answer is <see cref="RestoreOutcome.Busy"/>, and
    /// while a restore runs a concurrent save is refused as "already in progress". The lock is re-verified at the
    /// start and again right before the swap; if it is no longer ours the restore simply refuses
    /// (<see cref="RestoreOutcome.LockLost"/>); it never tries to recover a removed lock or demote the session.</para>
    ///
    /// <para><b>AFTER <see cref="RestoreOutcome.Restored"/> OR <see cref="RestoreOutcome.RenameUnconfirmed"/> THE
    /// SESSION IS STALE, AND THE CALLER MUST END IT.</b> The working copy still holds the OLD database. This method
    /// deliberately leaves <see cref="HashAtCheckout"/> unchanged: D no longer hashes to it, so every later
    /// <see cref="SaveAsync"/> refuses with <see cref="SaveOutcome.SourceModified"/> and the stale working copy
    /// can never be pushed over the restored database. Autosave is stopped. The caller must now release the lock
    /// WITHOUT saving (<c>ReleaseAsync(saveFirst: false)</c>), discard the working copy and re-open D. It must
    /// save first, <i>before</i> calling this method, if the user's edits matter: unsaved edits are not in the
    /// restored database, and are in the pre-restore copy only if they were saved. Every other outcome leaves D,
    /// the working copy and the session fully usable.</para>
    /// </summary>
    /// <param name="backupPath">Full path of the backup file to restore (may be on a share). It is only copied, never opened with SQLite.</param>
    /// <returns>
    /// The outcome. This method <b>does not throw</b>: an exception nobody anticipated comes back as
    /// <see cref="RestoreOutcome.Failed"/> (and is logged with LogError). The result of a successful restore
    /// carries <see cref="RestoreResult.PreRestoreCopyPath"/> and <see cref="RestoreResult.RestoredHash"/>.
    /// </returns>
    public async Task<RestoreResult> RestoreFromBackupAsync(string backupPath)
    {
        // Same gating as every other write: a restore is a write to D, so only the lock holder may do it.
        // HashAtCheckout is the proof of what D looked like when this session last read or wrote it.
        if (Mode != CheckoutMode.WriteAccess || !_lockService.IsWriter || HashAtCheckout is null)
        {
            _logger.LogInfo("CheckoutService: RestoreFromBackupAsync refused — this session does not hold write access.");
            return new RestoreResult(RestoreOutcome.NotWriter);
        }

        // The same gate as SaveAsync: a restore and a save (manual, autosave or the exit save) must never
        // interleave, or a save could push the working copy over the restored database mid-swap.
        if (Interlocked.CompareExchange(ref _saveInFlight, 1, 0) != 0)
        {
            _logger.LogInfo("CheckoutService: RestoreFromBackupAsync refused — a save is in progress.");
            return new RestoreResult(RestoreOutcome.Busy);
        }

        try
        {
            _logger.LogBreadcrumb("Restore: started");

            var result = await DatabaseRestore.ReplaceAsync(
                backupPath,
                SourcePath,
                HashAtCheckout,
                VerifyLockForRestoreAsync,
                DateTime.Now,
                faultPlan: RestoreFaultPlan,
                logger: _logger).ConfigureAwait(false);

            // D has been (or may have been) replaced: this session is stale. Stop the autosave timer so it
            // does not keep ticking into refusals. HashAtCheckout is left as it is ON PURPOSE (see above).
            if (result.Outcome is RestoreOutcome.Restored or RestoreOutcome.RenameUnconfirmed)
                StopAutoSave();

            _logger.LogBreadcrumb($"Restore: {result.Outcome}");
            return result;
        }
        catch (Exception ex)
        {
            // DatabaseRestore.ReplaceAsync never throws, so this is a safety net only (a fault in the
            // lock service, the logger, StopAutoSave...). LogError: it must reach the error reporter.
            _logger.LogError(ex, "CheckoutService: unexpected exception in RestoreFromBackupAsync");
            return new RestoreResult(RestoreOutcome.Failed, Error: ex);
        }
        finally
        {
            Volatile.Write(ref _saveInFlight, 0);
        }
    }

    /// <summary>
    /// Adapts <see cref="VerifyLockIsOursAsync"/> (with its one retry for a transient heartbeat-rename overlap)
    /// to the routine's <see cref="RestoreLockCheck"/>. Deliberately does NOT call
    /// <see cref="TryRecoverRemovedLockAsync"/> or <see cref="HandleLockLossAsync"/>: a restore that finds the
    /// lock gone just refuses; the normal save/heartbeat paths deal with lock loss.
    /// </summary>
    /// <returns>Ours, NotOurs or Unreachable, mirroring the verification result.</returns>
    private Task<RestoreLockCheck> VerifyLockForRestoreAsync()
        => VerifyLockForRestoreAtAsync(SourcePath, recordNewHolder: true);

    /// <summary>
    /// <see cref="VerifyLockForRestoreAsync"/> for the lock on ANY database, not just the session's own: the
    /// restore into a database nobody has open (<see cref="RestoreIntoAsync"/>) holds the lock on its target
    /// through the same lock service, and asks this whether it is still ours.
    /// </summary>
    /// <param name="sourcePath">Full path of the database whose <c>.lock</c> file is checked.</param>
    /// <param name="recordNewHolder">
    /// True to remember a foreign holder in <see cref="LockLossNewHolder"/> (what the session's own checks do, for
    /// the lock-loss banner); false for a restore into another database, which must not touch session state.
    /// </param>
    /// <returns>Ours, NotOurs or Unreachable, mirroring the verification result.</returns>
    private async Task<RestoreLockCheck> VerifyLockForRestoreAtAsync(string sourcePath, bool recordNewHolder)
    {
        return await VerifyLockIsOursAtAsync(sourcePath, recordNewHolder).ConfigureAwait(false) switch
        {
            LockVerificationResult.Ours    => RestoreLockCheck.Ours,
            LockVerificationResult.NotOurs => RestoreLockCheck.NotOurs,
            _                              => RestoreLockCheck.Unreachable
        };
    }

    /// <summary>
    /// How long <see cref="RestoreIntoAsync"/> waits for the background deletion of the lock file it just
    /// released. The deletion is one deadline-bounded read and one deadline-bounded delete
    /// (<see cref="NetworkFileOps.TimeoutMs"/> each), so this is comfortably past both.
    /// </summary>
    internal const int RestoreLockReleaseWaitMs = 12_000;

    /// <summary>
    /// Restores a backup into a database that THIS app does not have open — the database chooser's restore, which
    /// runs when no session exists (startup, recovery, "already open", after a failed switch). Unlike
    /// <see cref="RestoreFromBackupAsync"/> there is no session to protect, so this method does not read or change
    /// <see cref="SourcePath"/>, <see cref="WorkingPath"/>, <see cref="Mode"/>, <see cref="HashAtCheckout"/> or any
    /// other session state. The caller opens the database afterwards through the normal startup flow.
    ///
    /// <para><b>What it does,</b> by what is at <paramref name="targetPath"/> (probed, deadline-bounded):</para>
    /// <list type="bullet">
    /// <item><description><b>Unreachable</b> — <see cref="RestoreOutcome.Unreachable"/>; nothing was touched.</description></item>
    /// <item><description><b>Missing</b> — the create path, <see cref="DatabaseRestore.CreateFromBackupAsync"/>: the
    /// backup is checked and a new database is placed the way a brand-new one is. No lock is needed (there is
    /// nothing to protect), and nothing is ever overwritten: a file that appears meanwhile gives
    /// <see cref="RestoreOutcome.AlreadyExists"/>.</description></item>
    /// <item><description><b>Exists</b> — the replace path (only when <paramref name="allowReplace"/>; otherwise
    /// <see cref="RestoreOutcome.AlreadyExists"/> and nothing is touched):
    ///   <list type="number">
    ///   <item><description>Take the write lock on the TARGET. Held by someone else → <see cref="RestoreOutcome.LockHeld"/>
    ///   naming the holder (an abandoned lock too: a restore never takes a lock over; the user opens the database
    ///   normally, which offers the take-over). The lock file could not even be created (Controlled Folder Access,
    ///   permissions, a full disk) → <see cref="RestoreOutcome.Failed"/> carrying the reason in
    ///   <see cref="RestoreResult.Detail"/>.</description></item>
    ///   <item><description>Hash the target (deadline-bounded) and run <see cref="DatabaseRestore.ReplaceAsync"/> with
    ///   that hash and a lock check for THIS target, so the routine's guarantees hold: any failure before the final
    ///   rename leaves the database byte-identical.</description></item>
    ///   <item><description>ALWAYS release the lock, and wait for its file to be deleted before returning: the caller's
    ///   next step opens this same database, and its lock acquisition must not find our own just-released lock
    ///   file.</description></item>
    ///   </list></description></item>
    /// </list>
    ///
    /// <para>Refused up front, touching nothing: <see cref="RestoreOutcome.Busy"/> when this service holds a write
    /// lock or has a write session open, or a save is in flight (the chooser never runs then; this is the second
    /// line of defence), and <see cref="RestoreOutcome.Failed"/> (with <see cref="RestoreResult.Detail"/>) when
    /// <paramref name="targetPath"/> is the backup file itself.</para>
    /// </summary>
    /// <param name="backupPath">Full path of the backup file (may be on a share). It is only copied, never opened with SQLite.</param>
    /// <param name="targetPath">Full path of the database to restore into (may be on a share, may not exist yet). It is never opened with SQLite.</param>
    /// <param name="allowReplace">
    /// True (the default) to replace a database found at the target. The chooser passes false when it showed the
    /// user "a new database will be created here" and so asked no confirmation, so a database that appears at the
    /// target meanwhile is refused (<see cref="RestoreOutcome.AlreadyExists"/>) instead of being replaced unconfirmed.
    /// </param>
    /// <returns>
    /// The outcome. This method <b>does not throw</b>: an exception nobody anticipated comes back as
    /// <see cref="RestoreOutcome.Failed"/> (and is logged with LogError). <see cref="RestoreOutcome.Restored"/> and
    /// <see cref="RestoreOutcome.Created"/> mean the target now holds the backup and may be opened;
    /// <see cref="RestoreOutcome.RenameUnconfirmed"/> means it might (the caller should NOT open it, but tell the
    /// user); every other outcome leaves the target exactly as it was.
    /// </returns>
    public async Task<RestoreResult> RestoreIntoAsync(string backupPath, string targetPath, bool allowReplace = true)
    {
        // Second line of defence: a session that holds (or is writing to) a database must not have the lock
        // service — which is shared with it — re-pointed at another database by TryAcquire.
        if (_lockService.IsWriter || Mode == CheckoutMode.WriteAccess)
        {
            _logger.LogInfo("CheckoutService: RestoreIntoAsync refused — this app already has a database open for editing.");
            return new RestoreResult(RestoreOutcome.Busy);
        }

        // Restoring a file onto itself would destroy the only copy. The chooser disables Continue first.
        if (RestoreDestination.IsSameFile(backupPath, targetPath))
        {
            _logger.LogInfo("CheckoutService: RestoreIntoAsync refused — the destination is the backup file itself.");
            return new RestoreResult(
                RestoreOutcome.Failed,
                Error:  new InvalidOperationException("The restore destination is the backup file itself."),
                Detail: RestorePrompt.DestinationIsBackupFileText);
        }

        // The same gate as SaveAsync and RestoreFromBackupAsync, so none of them can interleave with another.
        if (Interlocked.CompareExchange(ref _saveInFlight, 1, 0) != 0)
        {
            _logger.LogInfo("CheckoutService: RestoreIntoAsync refused — a save or restore is in progress.");
            return new RestoreResult(RestoreOutcome.Busy);
        }

        try
        {
            _logger.LogBreadcrumb("Restore into: started");

            var result = await RestoreIntoCoreAsync(backupPath, targetPath, allowReplace).ConfigureAwait(false);

            _logger.LogBreadcrumb($"Restore into: {result.Outcome}");
            return result;
        }
        catch (Exception ex)
        {
            // Everything below this method returns results instead of throwing, so this is a safety net only.
            // LogError: it must reach the error reporter.
            _logger.LogError(ex, "CheckoutService: unexpected exception in RestoreIntoAsync");
            return new RestoreResult(RestoreOutcome.Failed, Error: ex);
        }
        finally
        {
            Volatile.Write(ref _saveInFlight, 0);
        }
    }

    /// <summary>
    /// The body of <see cref="RestoreIntoAsync"/> once its refusals have passed: probes the target and goes down the
    /// create path (missing), the replace path (exists), or reports the location unreachable.
    /// </summary>
    /// <param name="backupPath">The backup file.</param>
    /// <param name="targetPath">The database to restore into.</param>
    /// <param name="allowReplace">Whether an existing database may be replaced.</param>
    /// <returns>The outcome; see <see cref="RestoreIntoAsync"/>.</returns>
    private async Task<RestoreResult> RestoreIntoCoreAsync(string backupPath, string targetPath, bool allowReplace)
    {
        switch (await NetworkFileOps.ProbeFileAsync(targetPath).ConfigureAwait(false))
        {
            case FileProbeResult.Unreachable:
                _logger.LogInfo("CheckoutService: RestoreIntoAsync — the destination did not answer.");
                return new RestoreResult(RestoreOutcome.Unreachable);

            case FileProbeResult.Missing:
                return await DatabaseRestore.CreateFromBackupAsync(
                    backupPath, targetPath, faultPlan: RestoreFaultPlan, logger: _logger).ConfigureAwait(false);
        }

        // Something is there. Replacing it needs the caller's say-so.
        if (!allowReplace)
        {
            _logger.LogInfo("CheckoutService: RestoreIntoAsync — a database is at the destination and replacing was not allowed.");
            return new RestoreResult(RestoreOutcome.AlreadyExists);
        }

        return await ReplaceUnderLockAsync(backupPath, targetPath).ConfigureAwait(false);
    }

    /// <summary>
    /// The replace path of <see cref="RestoreIntoAsync"/>: takes the write lock on the target, hashes it, runs
    /// <see cref="DatabaseRestore.ReplaceAsync"/>, and always releases the lock again — waiting until its file is
    /// gone. A lock that someone else holds is never taken over or touched.
    ///
    /// <para><b>Why the shared lock service:</b> it is the one that already knows how to read, judge and
    /// heartbeat a lock file; the heartbeat keeps our lock alive through a slow network restore. TryAcquire first
    /// releases whatever this service held, which is why <see cref="RestoreIntoAsync"/> refuses while it holds a
    /// write lock. A <c>TryAcquire</c> that outlives its deadline may still create the lock file a moment later
    /// (the same exposure <see cref="CheckoutAsync"/> has); the release in the <c>finally</c> clears our state
    /// either way.</para>
    /// </summary>
    /// <param name="backupPath">The backup file.</param>
    /// <param name="targetPath">The existing database to replace.</param>
    /// <returns>LockHeld, Failed (lock file not writable), Unreachable, FileInUse, or whatever <see cref="DatabaseRestore.ReplaceAsync"/> returns.</returns>
    private async Task<RestoreResult> ReplaceUnderLockAsync(string backupPath, string targetPath)
    {
        try
        {
            // 1. The write lock on the TARGET, deadline-bounded like CheckoutAsync's.
            var acquireCompleted = await NetworkFileOps.RunAsync(
                () => _lockService.TryAcquire(targetPath), "restore lock acquire").ConfigureAwait(false);
            if (!acquireCompleted)
            {
                _logger.LogInfo("CheckoutService: RestoreIntoAsync — lock acquire timed out — network unreachable");
                return new RestoreResult(RestoreOutcome.Unreachable);
            }

            if (!_lockService.IsWriter)
            {
                // Read what the lock service learned BEFORE the finally releases it (Release clears the holder).
                if (_lockService.LockWriteError is { } writeError)
                {
                    _logger.LogInfo("CheckoutService: RestoreIntoAsync — the lock file could not be created.");
                    return new RestoreResult(RestoreOutcome.Failed, Error: new IOException(writeError), Detail: writeError);
                }

                _logger.LogInfo("CheckoutService: RestoreIntoAsync — the database is locked by someone else; nothing was touched.");
                return new RestoreResult(RestoreOutcome.LockHeld, Holder: _lockService.CurrentHolder);
            }

            // 2. The target's current hash: what ReplaceAsync insists it still has at the swap.
            string? targetHash;
            try
            {
                var hashCompleted = false;
                (hashCompleted, targetHash) = await NetworkFileOps.ComputeHashAsync(targetPath).ConfigureAwait(false);
                if (!hashCompleted)
                {
                    _logger.LogInfo("CheckoutService: RestoreIntoAsync — hashing the database timed out — network unreachable");
                    return new RestoreResult(RestoreOutcome.Unreachable);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // It was there a moment ago (the probe) and is gone now: someone moved or deleted it. Nothing was touched.
                _logger.LogInfo($"CheckoutService: RestoreIntoAsync — the database disappeared ({ex.GetType().Name})");
                return new RestoreResult(RestoreOutcome.Failed, Error: ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogInfo($"CheckoutService: RestoreIntoAsync — cannot read the database ({ex.GetType().Name}: {ex.Message})");
                return new RestoreResult(RestoreOutcome.FileInUse, Error: ex);
            }

            // 3. The restore itself, with a lock check for THIS target that leaves the session's state alone.
            return await DatabaseRestore.ReplaceAsync(
                backupPath,
                targetPath,
                targetHash!,
                () => VerifyLockForRestoreAtAsync(targetPath, recordNewHolder: false),
                DateTime.Now,
                faultPlan: RestoreFaultPlan,
                logger: _logger).ConfigureAwait(false);
        }
        finally
        {
            // 4. Always give the lock back, and wait for its file to go before the caller opens this database.
            _lockService.Release();
            await WaitForLockReleaseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits (bounded by <see cref="RestoreLockReleaseWaitMs"/>) for the background deletion of the lock file queued
    /// by the last <see cref="WriteLockService.Release"/>. Never throws: the lock service's release task cannot
    /// fault. If the share is too slow to finish in time, the file is left to the stale-lock handling (a later open
    /// would wait for it, bounded, too).
    /// </summary>
    private async Task WaitForLockReleaseAsync()
    {
        var pending = _lockService.PendingRelease;
        if (pending is null || pending.IsCompleted)
            return;

        var finished = await Task.WhenAny(pending, Task.Delay(RestoreLockReleaseWaitMs)).ConfigureAwait(false);
        if (finished != pending)
            _logger.LogInfo("CheckoutService: RestoreIntoAsync — the lock file's deletion did not finish in time.");
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
    /// Called only when the overwrite worked; <b>not</b> called when the overwrite failed after
    /// <paramref name="beforeOverwrite"/> had closed the connection, and a call that throws still returns
    /// <see cref="RefreshOutcome.SourceUnavailable"/>. In 1.2.3 nothing here reopens the connection in those two
    /// cases: a caller that supplied both callbacks can tell that the connection was lost by seeing that
    /// <paramref name="beforeOverwrite"/> ran and <paramref name="afterOverwrite"/> did not complete
    /// (<c>MainWindowViewModel.RefreshData</c> does, and says so: <see cref="RefreshPrompt.ConnectionLost"/>).
    /// Every failure is logged with <c>LogInfo</c>, never <c>LogError</c>, so none puts a log-text banner on screen.
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
                // LogInfo, not LogError: LogError would also put a red log-text banner on screen (spec item 28); the
                // view model shows one plain line instead. The exception's type and message stay in the log line.
                _logger.LogInfo(
                    $"CheckoutService: failed to copy D to D''.tmp during refresh ({ex.GetType().Name}: {ex.Message})");
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
                        _logger.LogInfo(
                            $"CheckoutService: beforeOverwrite callback threw during refresh ({ex.GetType().Name}: {ex.Message})");
                        TryDelete(tmpPath);
                        return RefreshOutcome.SourceUnavailable;
                    }
                }
                CrashPoints.Hit("Refresh.AfterConnectionClosed");

                try
                {
                    // Connection is closed at this point (beforeOverwrite); drop any
                    // WAL sidecars so the fresh copy is not paired with a stale -wal.
                    TryDeleteWalSidecars(ReadOnlyWorkingPath);
                    File.Move(tmpPath, ReadOnlyWorkingPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogInfo(
                        $"CheckoutService: failed to rename D''.tmp during refresh ({ex.GetType().Name}: {ex.Message})");
                    TryDelete(tmpPath);
                    return RefreshOutcome.SourceUnavailable;
                }

                CrashPoints.Hit("Refresh.AfterSwap");

                HashAtCheckout = sourceHash;

                // Reopen the DatabaseContext connection to the now-updated D''.
                if (afterOverwrite is not null)
                {
                    try { await afterOverwrite(); }
                    catch (Exception ex)
                    {
                        _logger.LogInfo(
                            $"CheckoutService: afterOverwrite callback threw during refresh ({ex.GetType().Name}: {ex.Message})");
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
                // ReportSaveFailure, not LogError: one banner (the warning below), one path-free report.
                ReportSaveFailure(SaveStep.CrashProtectionMarker, null,
                    "could not write the dirty marker — crash protection degraded for this session.",
                    isAutoSave: false);
                RaiseSaveError(SavePrompt.CrashProtectionMessage, autoDismiss: true);
            }
            CrashPoints.Hit("Dirty.AfterMarkerWritten");
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

        // Likewise an interrupted fresh-copy build (RefreshLocalCopyAfterFailedCheckAsync): derived from D, never
        // holds user data.
        var freshTmpPath = workingPath + FreshCopySuffix;
        if (File.Exists(freshTmpPath))
        {
            TryDelete(freshTmpPath);
            _logger.LogInfo($"CheckoutService: cleaned up orphaned fresh-copy temp file: {freshTmpPath}");
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
        CrashPoints.Hit("Discard.AfterWorkingFileDeleted");
        TryDeleteWalSidecars(workingPath);
        CrashPoints.Hit("Discard.AfterSidecarsDeleted");
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
    /// Saves the unsaved changes held in the crash-recovery file D' of <paramref name="sourcePath"/> as a NEW,
    /// standalone database at <paramref name="destPath"/> (spec item 16, step 16a). Offered after a
    /// <see cref="CheckoutOutcome.RecoveryConflict"/>: D changed since the crash, so the unsaved changes cannot be
    /// resumed, and this keeps them as a separate file the user can open later.
    ///
    /// <para><b>Snapshot, then place.</b> D' is copied with the SQLite Online Backup API into a temporary LOCAL file
    /// (<see cref="BackupSqliteDatabase"/>, exactly as a save does), and only that finished, quiescent snapshot is
    /// placed at <paramref name="destPath"/> with <see cref="NewFilePlacement.PlaceAsync"/>. The snapshot is needed
    /// because D' is a WAL-mode database: recent commits may live only in its <c>-wal</c> file, which a raw
    /// <c>File.Copy</c> of the <c>.db</c> would silently leave behind. The backup reads through the WAL and writes a
    /// self-contained, rollback-mode file, so the export holds every change and WAL is never active on a file the
    /// user may put on a share.</para>
    ///
    /// <para><b>Never overwrites.</b> The destination must not exist. This is not a nicety: the user is free to pick
    /// any path in the save dialog, including D itself, and by now the write lock has been released. An overwrite of D
    /// here would replace the department's database with the user's stale version, with no lock held, silently
    /// erasing every colleague's save made since the crash (and corrupting an editor that has D open). So an existing
    /// file, D included, is refused with <see cref="NewDatabaseOutcome.AlreadyExists"/> and the caller asks for a
    /// new name. Creating a file at D's path is allowed only when D does not exist (nothing is lost then).
    /// A user who really wants their version to become the database uses "Restore from a backup" in the startup
    /// chooser, which takes the write lock and keeps the replaced database.</para>
    ///
    /// <para><b>Safe for the shared database and for D'.</b> D' is only read; D' and its <c>.dirty</c> marker are
    /// never deleted, moved or edited here (the caller discards them with <see cref="DiscardCrash"/> only after a
    /// confirmed export). D is never opened or written. Every operation on the destination's location is
    /// deadline-bounded (<see cref="NetworkFileOps"/>), and the snapshot runs on a thread-pool thread, so a dead
    /// share cannot freeze the UI.</para>
    ///
    /// <para>Never throws: failures come back in the result. A leftover <c>dest.tmp</c> is removed before returning
    /// (nothing else would ever sweep one beside a file the user chose), and the local snapshot and its sidecars are
    /// always deleted.</para>
    /// </summary>
    /// <param name="sourcePath">The shared database path D whose crash-recovery file D' to export.</param>
    /// <param name="destPath">Full path of the file to create. It must not exist.</param>
    /// <param name="localDirectory">
    /// Folder for the temporary local snapshot. Defaults to the system temp folder; tests supply their own so they
    /// can assert nothing is left behind.
    /// </param>
    /// <returns>
    /// <see cref="NewDatabaseOutcome.Created"/> when <paramref name="destPath"/> now holds the complete export;
    /// <see cref="NewDatabaseOutcome.AlreadyExists"/> when a file is (or appeared) there, untouched;
    /// <see cref="NewDatabaseOutcome.Unreachable"/> when the destination did not answer in time;
    /// <see cref="NewDatabaseOutcome.Failed"/> for anything else (D' missing or unreadable, an IO error), with the
    /// exception in <see cref="NewDatabaseResult.Error"/>.
    /// </returns>
    public async Task<NewDatabaseResult> ExportRecoveredCopyAsync(
        string sourcePath, string destPath, string? localDirectory = null)
    {
        var localPath = Path.Combine(
            localDirectory ?? Path.GetTempPath(),
            $"termpoint-recovered-{Guid.NewGuid():N}.db");

        NewDatabaseResult result;
        try
        {
            result = await SnapshotAndPlaceRecoveredCopyAsync(sourcePath, destPath, localPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never throws: the caller turns the result into a user message and logs the error itself.
            result = new NewDatabaseResult(NewDatabaseOutcome.Failed, ex);
        }
        finally
        {
            // The local snapshot is scratch: delete it with every sidecar SQLite may have made. Best effort.
            TryDelete(localPath);
            TryDeleteWalSidecars(localPath);
            TryDelete(localPath + "-journal");
        }

        _logger.LogInfo(
            $"CheckoutService: recovered-copy export to '{destPath}': {result.Outcome}" +
            (result.Error is null ? "" : $" ({result.Error.Message})"));
        return result;
    }

    /// <summary>
    /// The body of <see cref="ExportRecoveredCopyAsync"/>: refuses early, takes the local snapshot of D', and places it.
    /// Exceptions propagate to the caller, which converts them to a Failed result and removes the local snapshot.
    /// </summary>
    /// <param name="sourcePath">The shared database path D whose crash-recovery file D' to export.</param>
    /// <param name="destPath">Full path of the file to create (never overwritten).</param>
    /// <param name="localPath">Full path of the temporary local snapshot to create.</param>
    /// <returns>The outcome, as <see cref="ExportRecoveredCopyAsync"/> documents it.</returns>
    /// <exception cref="IOException">A file operation failed (the snapshot, or the transfer to the destination).</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the destination was denied.</exception>
    /// <exception cref="SqliteException">D' could not be read as a database.</exception>
    private async Task<NewDatabaseResult> SnapshotAndPlaceRecoveredCopyAsync(
        string sourcePath, string destPath, string localPath)
    {
        // Nothing to export: report it without creating anything (BackupSqliteDatabase would also refuse to
        // create a missing D', but saying so up front gives a clear error).
        var workingPath = ComputeWorkingPath(sourcePath);
        if (!File.Exists(workingPath))
            return new NewDatabaseResult(NewDatabaseOutcome.Failed,
                new FileNotFoundException("The unsaved changes to export were not found.", workingPath));

        // Cheap refusal before any work: the destination is already taken (D included) or does not answer.
        var early = await NewFilePlacement.RefuseIfPresentAsync(destPath).ConfigureAwait(false);
        if (early is not null) return early;

        // Snapshot D' locally (WAL-proof, rollback mode), then place it without overwrite. PlaceAsync re-checks the
        // destination just before writing, because the snapshot takes a moment. The leftover-.tmp removal is
        // awaited: nothing else would ever sweep a stray dest.tmp beside a file the user chose.
        await Task.Run(() => BackupSqliteDatabase(workingPath, localPath)).ConfigureAwait(false);
        CrashPoints.Hit("Export.AfterLocalSnapshot");
        return await NewFilePlacement.PlaceAsync(localPath, destPath, awaitTmpCleanup: true, logger: _logger)
            .ConfigureAwait(false);
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
        => ArchiveCorruptFile(ComputeWorkingPath(sourcePath), deleteIfMoveFails: true);

    /// <summary>
    /// Sets aside the local copy of the current session — D' for a writer, D'' for a reader — because it keeps
    /// failing its open-time integrity check (spec item 5, step 5d). The file is RENAMED to
    /// <c>.corrupt-yyyyMMdd-HHmmss</c> beside where it was (with its WAL sidecar and any dirty marker handled as
    /// <see cref="ArchiveCorruptWorkingCopy"/> does), never deleted: it is evidence for IT support and, for a writer
    /// whose copy somehow held unsaved work, the only copy of it. If the rename fails the file is left exactly
    /// where it is — this method never deletes the file itself — and null is returned.
    ///
    /// <para>Call it BEFORE <see cref="ResetAfterFailedOpenAsync"/>, which forgets <see cref="WorkingPath"/> (and
    /// deletes a copy that has no dirty marker).</para>
    /// </summary>
    /// <returns>The path the file was renamed to, or null when there was no local copy to set aside or the rename failed.</returns>
    public string? SetAsideFailedLocalCopy()
    {
        if (string.IsNullOrEmpty(WorkingPath) || WorkingPath == SourcePath || !File.Exists(WorkingPath))
            return null;

        return ArchiveCorruptFile(WorkingPath, deleteIfMoveFails: false);
    }

    /// <summary>
    /// Renames a damaged local file to a unique <c>.corrupt-yyyyMMdd-HHmmss</c> name beside it (a second archive
    /// in the same second gets <c>-2</c>, <c>-3</c>, … — an earlier archive, which may hold a user's unsaved work,
    /// is never overwritten), carries its WAL sidecar along, and removes its dirty marker and shared-memory file.
    /// The one implementation behind <see cref="ArchiveCorruptWorkingCopy"/> and <see cref="SetAsideFailedLocalCopy"/>.
    /// </summary>
    /// <param name="workingPath">Full path of the damaged local file (D' or D'').</param>
    /// <param name="deleteIfMoveFails">
    /// True (crash recovery's long-standing behaviour): if the rename fails, delete the file instead, so a damaged
    /// recovery file can never be offered again. False: leave the file and its marker untouched.
    /// </param>
    /// <returns>The archive path; null when the rename failed (and, with <paramref name="deleteIfMoveFails"/>, the file was deleted).</returns>
    private string? ArchiveCorruptFile(string workingPath, bool deleteIfMoveFails)
    {
        var stamp       = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var archivePath = $"{workingPath}.corrupt-{stamp}";
        for (var n = 2; File.Exists(archivePath); n++)
            archivePath = $"{workingPath}.corrupt-{stamp}-{n}";

        try
        {
            File.Move(workingPath, archivePath, overwrite: false);
            // Archive the WAL sidecar too — after a crash it can hold committed data
            // not yet checkpointed into the .db, which a salvage attempt would need.
            try { File.Move(workingPath + "-wal", archivePath + "-wal", overwrite: true); } catch { }
            _logger.LogInfo($"CheckoutService: archived damaged working copy to {archivePath}");
        }
        catch (Exception ex)
        {
            if (!deleteIfMoveFails)
            {
                _logger.LogInfo($"CheckoutService: could not set aside the damaged local file ({ex.Message}) — left where it is.");
                return null;
            }

            _logger.LogInfo($"CheckoutService: could not archive damaged working copy ({ex.Message}) — deleting instead.");
            TryDelete(workingPath);
            archivePath = null;
        }
        TryDeleteWalSidecars(workingPath);
        TryDelete(workingPath + ".dirty");
        return archivePath;
    }

    /// <summary>
    /// Suffix of the temporary file a fresh local copy is built in, beside the local copy it replaces. Swept at
    /// the next start (<see cref="CleanupStaleCrashArtifacts"/>) if a crash leaves one behind.
    /// </summary>
    internal const string FreshCopySuffix = ".freshtmp";

    /// <summary>
    /// Makes ONE fresh local copy of the shared database after the open-time integrity check of the current local
    /// copy failed with damage and the shared database itself passed (spec item 5, step 5d). Never throws; D is
    /// never changed.
    ///
    /// <para><b>Writer</b> (<see cref="Mode"/> is <see cref="CheckoutMode.WriteAccess"/>, the lock is ours):</para>
    /// <list type="number">
    /// <item><description><b>Refuses if the local copy holds unsaved work</b> (it has a dirty marker):
    /// <see cref="LocalCopyRefreshResult.HoldsUnsavedChanges"/>. Unsaved work must never be overwritten; the caller
    /// sets that copy aside first (<see cref="ArchiveCorruptWorkingCopy"/>).</description></item>
    /// <item><description>Takes the same in-flight gate as <see cref="SaveAsync"/>, so no save can interleave.</description></item>
    /// <item><description>Hashes D, copies D to a temporary file beside D' and verifies that its hash equals the
    /// hash of D just taken (<see cref="CopyAndVerifyAsync"/>, the checkout's own copy-and-verify step, with its one
    /// retry).</description></item>
    /// <item><description>Replaces D' with the verified file (its WAL sidecars are removed first) and updates
    /// <see cref="HashAtCheckout"/> to D's hash, so a later save sees D as unchanged.</description></item>
    /// <item><description><b>Runs the version gate again</b> on the new D' (<see cref="ApplyVersionGateAsync"/>; spec
    /// item 3, step 3c), because the gate that ran at checkout judged the file just replaced: it reads the
    /// stamp and, in a release that raises it, raises it into the new D'. If the new D' must be read-only
    /// (this app is older than its stamp) the writer is stood down — see <see cref="LocalCopyRefreshResult.NowReadOnly"/>.</description></item>
    /// </list>
    /// <para>Until that last step the write lock is never released or re-acquired — there is no window in which a
    /// colleague could take it — and any failure leaves the lock held and whatever D' was as it was. (When the gate
    /// stands the writer down, the lock is released on purpose: the session is no longer a writer.)</para>
    ///
    /// <para><b>Reader:</b> re-makes the read-only snapshot D'' (<see cref="SetupReadOnlySnapshotAsync"/>). A reader
    /// holds no lock and has no unsaved work, so there is nothing to protect.</para>
    /// </summary>
    /// <returns>
    /// <see cref="LocalCopyRefreshResult.Ok"/> when the local copy has been replaced by a verified copy of D;
    /// <see cref="LocalCopyRefreshResult.NowReadOnly"/> when it was, but the version gate then found the database
    /// requires a newer TermPoint (the session is now a read-only one on D'', <see cref="RequiredAppVersion"/> is
    /// set, the lock is released) — open again from <see cref="WorkingPath"/>;
    /// <see cref="LocalCopyRefreshResult.Unreachable"/>, <see cref="LocalCopyRefreshResult.Unreadable"/> or
    /// <see cref="LocalCopyRefreshResult.Failed"/> when it could not be made (the previous local copy is left in
    /// place, or, for a reader whose snapshot failed, as <see cref="SetupReadOnlySnapshotAsync"/> leaves it).
    /// <see cref="LocalCopyRefreshResult.Unreadable"/> is also what the re-run gate reports when it cannot read
    /// or raise the stamp even after its quiet retries: write access is then refused and the lock released, and
    /// the handler shows its front-door "can't read" message and gives up (rare on rare, accepted);
    /// <see cref="LocalCopyRefreshResult.HoldsUnsavedChanges"/> for a writer's copy with a dirty marker.
    /// </returns>
    public async Task<LocalCopyRefreshResult> RefreshLocalCopyAfterFailedCheckAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(SourcePath))
            {
                _logger.LogInfo("CheckoutService: RefreshLocalCopy — no database is open.");
                return LocalCopyRefreshResult.Failed;
            }

            _logger.LogBreadcrumb("RefreshLocalCopy: started", new() { ["mode"] = Mode.ToString() });

            if (Mode != CheckoutMode.WriteAccess)
            {
                // A reader: D'' is just a snapshot. SetupReadOnlySnapshotAsync copies, verifies and replaces it
                // and reports every failure as null (never throws).
                var snapshot = await SetupReadOnlySnapshotAsync();
                return snapshot is not null ? LocalCopyRefreshResult.Ok : LocalCopyRefreshResult.Failed;
            }

            return await RefreshWriterCopyAsync();
        }
        catch (Exception ex)
        {
            // The steps above report every failure they expect; this is the safety net, so the method keeps its
            // promise never to throw. LogError: an unanticipated failure must reach the error reporter.
            _logger.LogError(ex, "CheckoutService: unexpected exception while making a fresh local copy");
            return LocalCopyRefreshResult.Failed;
        }
    }

    /// <summary>
    /// The writer half of <see cref="RefreshLocalCopyAfterFailedCheckAsync"/>; see there for the contract.
    /// </summary>
    /// <returns>The result. An unexpected exception propagates to the caller's safety net.</returns>
    private async Task<LocalCopyRefreshResult> RefreshWriterCopyAsync()
    {
        if (!_lockService.IsWriter)
        {
            _logger.LogInfo("CheckoutService: RefreshLocalCopy refused — this session no longer holds the write lock.");
            return LocalCopyRefreshResult.Failed;
        }

        // Unsaved work is never overwritten. The caller archives such a copy first; this is the backstop.
        if (HasDirtyMarker)
        {
            _logger.LogInfo("CheckoutService: RefreshLocalCopy refused — the local copy holds unsaved changes.");
            return LocalCopyRefreshResult.HoldsUnsavedChanges;
        }

        // The same gate as SaveAsync: a save and a replacement of D' must never interleave.
        if (Interlocked.CompareExchange(ref _saveInFlight, 1, 0) != 0)
        {
            _logger.LogInfo("CheckoutService: RefreshLocalCopy refused — a save is in progress.");
            return LocalCopyRefreshResult.Failed;
        }

        var tmpPath = WorkingPath + FreshCopySuffix;
        try
        {
            var (hashCompleted, sourceHash, hashUnreadable) = await HashSourceAsync(SourcePath, "fresh local copy hash");
            if (hashUnreadable)
                return LocalCopyRefreshResult.Unreadable;
            if (!hashCompleted || sourceHash is null)
            {
                _logger.LogInfo("CheckoutService: RefreshLocalCopy — hashing D timed out; network unreachable.");
                return LocalCopyRefreshResult.Unreachable;
            }

            Directory.CreateDirectory(_workingDir);
            TryDelete(tmpPath);

            var copyOutcome = await CopyAndVerifyAsync(SourcePath, tmpPath, sourceHash, "fresh local copy");
            if (copyOutcome != CopyVerifyOutcome.Ok)
            {
                TryDelete(tmpPath);
                return copyOutcome switch
                {
                    CopyVerifyOutcome.Unreachable => LocalCopyRefreshResult.Unreachable,
                    CopyVerifyOutcome.Unreadable  => LocalCopyRefreshResult.Unreadable,
                    _                             => LocalCopyRefreshResult.Failed
                };
            }

            // The verified copy replaces D'. D' is opened in WAL mode, so a stale -wal / -shm beside the file
            // being replaced would be a corruption hazard on the next open: remove them first.
            TryDeleteWalSidecars(WorkingPath);
            try
            {
                NetworkFileOps.MoveWithRetry(tmpPath, WorkingPath, overwrite: true);
            }
            catch (Exception ex)
            {
                // D' itself is held by another program (or may not be replaced): leave things as they are.
                _logger.LogInfo($"CheckoutService: RefreshLocalCopy — could not put the fresh copy in place ({ex.GetType().Name}: {ex.Message})");
                TryDelete(tmpPath);
                return DatabaseValidator.IsLockedOrAccessDenied(ex)
                    ? LocalCopyRefreshResult.Unreadable
                    : LocalCopyRefreshResult.Failed;
            }

            // D' now IS D as of sourceHash: that is the baseline every later save compares D against.
            HashAtCheckout   = sourceHash;
            _pendingSaveHash = null;
            _logger.LogInfo("CheckoutService: RefreshLocalCopy — D' replaced by a fresh, verified copy of D.");

            // The version gate ran at checkout on the D' that has just been thrown away (spec item 3, step 3c):
            // the new D' was never checked, and a stamp this release raised into the old one is gone. Run the
            // gate again. It stands the writer down itself if it must: the lock is released, RequiredAppVersion
            // is set and a read-only D'' is made (NowReadOnly), or write access is refused (Unreadable).
            // A fresh copy holds no unsaved work (a dirty marker was refused above), so D' is never kept.
            var gated = await ApplyVersionGateAsync(isRecovery: false);
            var result = gated switch
            {
                null                                    => LocalCopyRefreshResult.Ok,
                CheckoutOutcome.ReadOnly                => LocalCopyRefreshResult.NowReadOnly,
                CheckoutOutcome.VersionCheckUnavailable => LocalCopyRefreshResult.Unreadable,
                _                                       => LocalCopyRefreshResult.Failed
            };
            _logger.LogBreadcrumb($"RefreshLocalCopy: {result}");
            return result;
        }
        finally
        {
            TryDelete(tmpPath);   // normally already moved away; never leave a stray behind on any path
            Volatile.Write(ref _saveInFlight, 0);
        }
    }

    /// <summary>
    /// Deletes any orphaned <c>D.tmp</c> file left alongside D from a previous crashed save.
    ///
    /// <para><b>Runs ONLY while this process holds the write lock</b>, through
    /// <see cref="CleanupOrphanedTmpOnceWriterAsync"/>, at the three write-access exits of a checkout
    /// (spec item 4). D.tmp is the in-flight file of whoever is saving, and only the lock holder saves —
    /// so before the lock is ours a D.tmp may be a colleague's save in progress, and deleting it makes
    /// their save fail. No read-only, observer, licence-gated, version-gated, failed or refused open ever
    /// calls this. It is <c>internal</c> (not public) so nothing outside the checkout can call it at the
    /// wrong time; tests reach it directly.</para>
    /// </summary>
    /// <param name="sourcePath">The database path to check for an orphaned tmp file.</param>
    internal async Task CleanupOrphanedTmpAsync(string sourcePath)
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
    /// Housekeeping at the moment write access is confirmed: removes an orphaned <c>D.tmp</c> beside
    /// <see cref="SourcePath"/> (<see cref="CleanupOrphanedTmpAsync"/>). Called from the three
    /// write-access exits — <see cref="CheckoutAsync"/>, <see cref="ForceCheckoutAsync"/> and
    /// <see cref="FinishRecoveryCheckoutAsync"/> — after the version gate has passed and just before
    /// <see cref="Mode"/> becomes <see cref="CheckoutMode.WriteAccess"/>, i.e. only when the lock is ours.
    ///
    /// <para><b>It can never fail the checkout.</b> The network calls inside are already deadline-bounded
    /// and report failure as a result; anything that still throws is logged and swallowed. A D.tmp left
    /// behind is harmless: <see cref="SaveAsync"/> streams its snapshot to D.tmp with
    /// <c>FileMode.Create</c> (overwriting whatever is there) and then renames it over D, so the next save
    /// simply replaces it.</para>
    /// </summary>
    private async Task CleanupOrphanedTmpOnceWriterAsync()
    {
        try
        {
            await CleanupOrphanedTmpAsync(SourcePath);
        }
        catch (Exception ex)
        {
            _logger.LogInfo($"CheckoutService: orphaned-tmp cleanup failed ({ex.GetType().Name}: {ex.Message}) — continuing.");
        }
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
            CrashPoints.Hit("Release.AfterFinalSave");

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
        CrashPoints.Hit("Release.AfterLockReleased");

        Mode = CheckoutMode.ReadOnly;
        LockLossNewHolder = null;
        return outcome;
    }

    /// <summary>
    /// Waits (bounded) for any in-flight <see cref="SaveAsync"/> to finish. A save is
    /// bounded by a handful of 5-second-capped network operations plus retries, so the
    /// 60-second ceiling comfortably covers the worst case without risking a hung
    /// shutdown if something unforeseen wedges the save. Also awaited by the restore from backup
    /// (<c>MainWindow.RestoreFromBackupAsync</c>) before its own save-first, so an autosave tick that is
    /// already running doesn't turn "save, then restore" into "the save failed, restore not started".
    /// </summary>
    internal async Task WaitForInFlightSaveAsync()
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

    /// <summary>
    /// Returns the service to a clean "no database open" state after an open or switch FAILED once the
    /// previous database had already been closed (spec item 4d). Call it before showing the database
    /// chooser, so the next attempt starts from a known state and nothing left over can do harm:
    ///
    /// <list type="bullet">
    /// <item><description><b>Lock and timers:</b> stops autosave and the wake handler, gives back the write
    /// lock if the failed attempt had got as far as taking it (a checkout that succeeded and a later step
    /// threw), and parks <see cref="Mode"/> at read-only. A lock deliberately parked by
    /// <see cref="WriteLockService.Suspend"/> (a failed final save of the PREVIOUS database) is not a held
    /// lock, so it is left in place.</description></item>
    /// <item><description><b>Working copy — never deleted if it holds unsaved work:</b> a working copy that
    /// has a dirty marker is KEPT, exactly as the exit and switch teardowns keep it, so it is offered back
    /// the next time that database is opened. Only a copy with no marker (nothing was ever edited) is
    /// deleted. Whatever was or wasn't deleted, <see cref="WorkingPath"/> is then FORGOTTEN.</description></item>
    /// </list>
    ///
    /// <para><b>Why forgetting the path matters:</b> after a failed checkout <see cref="WorkingPath"/>
    /// still names whatever the previous session (or a half-finished attempt at a stale lock) left in
    /// it — possibly a kept D' with unsaved edits. <see cref="MainWindow.OnClosing"/> and the next switch
    /// both call <see cref="CleanupWorkingCopy"/>, which deletes <see cref="WorkingPath"/> whenever the
    /// final save "succeeds" (and it always does when no save was needed). Choosing Exit in the chooser
    /// that follows a failed switch would therefore have deleted the previous database's kept working
    /// copy, leaving only an orphaned marker for the next startup to sweep away.</para>
    ///
    /// <para>Nothing is saved: an attempt that never finished opening has no edits of its own, and pushing
    /// a recovered or damaged D' over D would be exactly the wrong thing to do. Safe to call at any time,
    /// repeatedly, and on a service that never opened anything.</para>
    /// </summary>
    public async Task ResetAfterFailedOpenAsync()
    {
        _logger.LogBreadcrumb("ResetAfterFailedOpen", new() { ["workingPath"] = WorkingPath });

        // Lock, autosave, wake handler, Mode. saveFirst:false — see the remarks above.
        await ReleaseAsync(saveFirst: false);
        _lockService.HeartbeatFailed -= OnHeartbeatFailed;

        // Same rule as the exit and switch teardowns: a marker means unsaved work, so keep the file.
        if (!HasDirtyMarker)
            CleanupWorkingCopy();
        else
            _logger.LogInfo("CheckoutService: failed open — keeping the working copy and its dirty marker (unsaved changes kept for recovery).");

        // Forget everything about the failed attempt (and the previous session): from here on there is
        // no working copy for CleanupWorkingCopy / HasDirtyMarker to find.
        SourcePath              = string.Empty;
        HasSaveConflict         = false;
        WorkingPath             = string.Empty;
        ReadOnlyWorkingPath     = null;
        IsReadOnlyMode          = false;
        HashAtCheckout          = null;
        SessionDirty            = false;
        CurrentHolder           = null;
        ReclaimedDeadSession    = false;
        HolderIsLiveSameMachine = false;
        RequiredAppVersion      = null;
        _pendingSaveHash        = null;
        _staleHolderAtDetection = null;
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
        _autoSaveRunning = true;
        if (!_startTimers) return; // test mode: TickAutoSaveAsync drives it by hand
        var intervalMs = Math.Max(AppSettings.Current.AutoSaveIntervalMinutes, 1) * 60_000;
        _autoSaveTimer = new Timer(
            _ =>
            {
                _ = Task.Run(() => RunAutoSaveTickAsync());
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
        _autoSaveRunning = false;
    }

    /// <summary>
    /// True while autosave is on (<see cref="StartAutoSave"/> called and not since stopped, whether by
    /// <see cref="StopAutoSave"/> or by a tick that hit a non-transient failure). Test-visible; the app
    /// does not read it.
    /// </summary>
    internal bool IsAutoSaveRunning => _autoSaveRunning;

    /// <summary>
    /// Runs ONE autosave tick on demand, exactly the work the autosave timer does
    /// (<see cref="AutoSaveTickAsync"/>); like the timer it never throws (a failure is logged). Used with
    /// <c>startTimers: false</c>.
    /// </summary>
    /// <returns>A task that completes when the tick's save (or its skip) is done.</returns>
    internal Task TickAutoSaveAsync() => RunAutoSaveTickAsync();

    /// <summary>
    /// The body of one autosave timer tick: <see cref="AutoSaveTickAsync"/> with any failure logged rather than
    /// thrown. Shared by the autosave timer (which runs it on the thread pool) and <see cref="TickAutoSaveAsync"/>.
    /// </summary>
    /// <returns>A task that completes when the tick is done; never faults.</returns>
    private async Task RunAutoSaveTickAsync()
    {
        try { await AutoSaveTickAsync(); }
        catch (Exception ex) { App.Logger.LogError(ex, "AutoSave tick failed"); }
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
            StopAutoSave(); // SaveFailed / SaveConflictDetected is already raised inside SaveAsync.
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
            LockLossPrompt.LockFilePutBack,
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
            // The holder's relation to this user, never their name (spec item 6, step 6c).
            ["newHolder"] = _lockService.DescribeHolder(LockLossNewHolder),
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
    ///         Keep writer state; surface the one sticky outage notice
    ///         (<see cref="RaiseLocationUnreachable"/>, the same notice a save that cannot reach
    ///         the location raises) via <see cref="SaveFailed"/> so the user knows saves are
    ///         paused. The heartbeat timer continues; the notice clears when the location
    ///         responds again: a later save succeeds (<see cref="SaveCompleted"/>) or a later
    ///         verification finds the lock file again (<see cref="LocationReachableAgain"/>).</item>
    ///   <item><see cref="LockVerificationResult.Ours"/> — false alarm (heartbeat write
    ///         failed for a reason other than ownership loss). Raise
    ///         <see cref="LocationReachableAgain"/>, then force-renew immediately.</item>
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
                        // The same sticky notice a save that cannot reach the location raises (spec item 28, F15).
                        RaiseLocationUnreachable();
                        break;

                    case LockVerificationResult.Ours:
                        _logger.LogInfo("CheckoutService: heartbeat verify — lock still ours; renewing.");
                        // The lock file was just read: the location responds. Tell the window so it can clear the
                        // outage notice (and only that notice). A report of a fact already found; the renewal below
                        // is exactly as before.
                        _dispatch(() => LocationReachableAgain?.Invoke());
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
        CrashPoints.Hit("Demote.AfterLockReleased");

        // 3 — let the caller close the DatabaseContext connection to D'.
        if (beforeClose is not null)
        {
            try { await beforeClose(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutService: DemoteToReadOnly — beforeClose callback threw");
            }
        }
        CrashPoints.Hit("Demote.AfterConnectionClosed");

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

        CrashPoints.Hit("Demote.AfterWorkingCopyHandled");

        SessionDirty = false;

        // 5 — create D''. SetupReadOnlySnapshotAsync updates WorkingPath and Mode.
        var readOnlyPath = await SetupReadOnlySnapshotAsync();
        var success = readOnlyPath is not null;
        CrashPoints.Hit("Demote.AfterSnapshotCreated");

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
    private Task<LockVerificationResult> VerifyLockIsOursAsync()
        => VerifyLockIsOursAtAsync(SourcePath, recordNewHolder: true);

    /// <summary>
    /// <see cref="VerifyLockIsOursAsync"/> for the lock on any database: the one-retry loop of that method, with the
    /// database path passed in instead of read from <see cref="SourcePath"/>. The session's own checks call it with
    /// <see cref="SourcePath"/> (through the parameterless method, so their behaviour is unchanged); a restore into a
    /// database this app does not have open (<see cref="RestoreIntoAsync"/>) calls it with that database's path.
    /// </summary>
    /// <param name="sourcePath">Full path of the database whose <c>.lock</c> file is checked.</param>
    /// <param name="recordNewHolder">
    /// True to remember a foreign holder in <see cref="LockLossNewHolder"/> (for the session's lock-loss banner);
    /// false when the check is not about the session's own database and must not touch session state.
    /// </param>
    /// <returns>The same three-way answer as <see cref="VerifyLockIsOursAsync"/>.</returns>
    private async Task<LockVerificationResult> VerifyLockIsOursAtAsync(string sourcePath, bool recordNewHolder)
    {
        var result = await VerifyLockIsOursOnceAtAsync(sourcePath, recordNewHolder);

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
            result = await VerifyLockIsOursOnceAtAsync(sourcePath, recordNewHolder);
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
    private Task<LockVerificationResult> VerifyLockIsOursOnceAsync()
        => VerifyLockIsOursOnceAtAsync(SourcePath, recordNewHolder: true);

    /// <summary>
    /// <see cref="VerifyLockIsOursOnceAsync"/> for the lock on any database (see
    /// <see cref="VerifyLockIsOursAtAsync"/> for why): the single-attempt check with the database path passed in.
    /// The decision table, the reachability probe and the contended-read handling are exactly those of the
    /// session's own check.
    /// </summary>
    /// <param name="sourcePath">Full path of the database whose <c>.lock</c> file is checked.</param>
    /// <param name="recordNewHolder">True to remember a foreign holder in <see cref="LockLossNewHolder"/>; false to leave session state alone.</param>
    /// <returns>Ours, NotOurs or Unreachable.</returns>
    private async Task<LockVerificationResult> VerifyLockIsOursOnceAtAsync(string sourcePath, bool recordNewHolder)
    {
        var lockPath = Path.ChangeExtension(sourcePath, ".lock");

        // ── Step 1: Does D.lock exist? ───────────────────────────────────────
        var (existsCompleted, exists) = await NetworkFileOps.ExistsAsync(lockPath);
        if (!existsCompleted)
            return LockVerificationResult.Unreachable;

        if (!exists)
        {
            // D.lock appears missing — but is the share actually reachable?
            // Probe the parent directory to distinguish a genuine deletion
            // from an SMB cache returning a stale "not found."
            var sourceDir = Path.GetDirectoryName(sourcePath)!;
            var (dirCompleted, dirExists) = await NetworkFileOps.DirectoryExistsAsync(sourceDir);
            if (!dirCompleted || !dirExists)
                return LockVerificationResult.Unreachable;

            // Share directory is reachable, so D.lock was genuinely deleted.
            if (recordNewHolder) LockLossNewHolder = null;
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
            var data = WriteLockService.ParseLockFile(json);
            if (data?.SessionGuid == _lockService.SessionGuid)
                return LockVerificationResult.Ours;

            // Lock file belongs to a different session — capture the new holder
            // so the WriteLockLost handler can name them in the banner.
            if (recordNewHolder) LockLossNewHolder = data;
            return LockVerificationResult.NotOurs;
        }
        catch
        {
            // JSON parse failure — could be a truncated read from a flaky connection.
            // Same reachability probe: if the share directory is unreachable, blame the network.
            var sourceDir = Path.GetDirectoryName(sourcePath)!;
            var (probeCompleted, _) = await NetworkFileOps.DirectoryExistsAsync(sourceDir);
            if (!probeCompleted)
                return LockVerificationResult.Unreachable;

            // Share is reachable but the lock file is genuinely unreadable (corrupt).
            // Treat as external interference — no known new holder.
            if (recordNewHolder) LockLossNewHolder = null;
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
    /// The identity of a database, as far as its local files are concerned: its full path, lower-cased
    /// (invariant culture), so that every spelling of one path — relative or absolute, any letter case —
    /// names the same database. The one definition shared by D' / D'' naming
    /// (<see cref="ComputeWorkingPath"/>, <see cref="ComputeReadOnlyWorkingPath"/>) and by
    /// <see cref="DatabaseInstanceGuard"/>, so "the same database" can never mean two different things:
    /// two sessions that would share a working copy are exactly the two sessions the guard keeps apart.
    /// </summary>
    /// <param name="sourcePath">Path to D, in any spelling.</param>
    /// <returns>The normalised (full, lower-case) path; it is a key, not a path to open.</returns>
    /// <exception cref="ArgumentException">The path is empty or contains invalid characters.</exception>
    /// <exception cref="NotSupportedException">The path format is not supported.</exception>
    /// <exception cref="PathTooLongException">The path is too long.</exception>
    internal static string NormalizeSourcePath(string sourcePath)
        => Path.GetFullPath(sourcePath).ToLowerInvariant();

    /// <summary>
    /// Derives a stable, user-invisible path for D' from the source path.
    /// Uses the first 8 hex chars of SHA-256(normalizedSourcePath) as a prefix
    /// to avoid collisions between databases with the same filename in different folders.
    /// </summary>
    /// <param name="sourcePath">Absolute path to D.</param>
    /// <returns>Absolute path for D' under <see cref="_workingDir"/>.</returns>
    private string ComputeWorkingPath(string sourcePath)
    {
        var normalized = NormalizeSourcePath(sourcePath);
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
    /// <c>null</c> if D'' cannot be created (e.g. D is inaccessible, held open exclusively by
    /// another program, or the copy hash never matches) — I/O and permission failures reading D
    /// are reported this way, never thrown. The caller must NOT fall back to opening <see cref="SourcePath"/> directly —
    /// opening D with SQLite changes its header (so the active editor's next save fails) and
    /// holds it open. Tell the user and offer to try again, or give up; see
    /// <see cref="SetupReadOnlySnapshotWithRetryAsync"/>.
    /// </returns>
    public async Task<string?> SetupReadOnlySnapshotAsync()
    {
        // DEBUG-ONLY fault injection (see FaultPlan): report "the local copy could not be made" without
        // touching any state, exactly as a failed copy does, so the Try again / Close prompt appears.
        if (FaultPlan is { } plan && plan.TryConsumeSnapshotFault())
        {
            _logger.LogInfo(
                $"[DEBUG fault injection] {CheckoutFaultPlan.EnvironmentVariableName}: " +
                $"failing the read-only snapshot of {SourcePath}; {plan.Description}.");
            return null;
        }

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
            // A database another program holds exclusively (or that we may not read) is an
            // ordinary, retryable condition — log it quietly (LogError raises a Bugsnag report and
            // a banner). Anything else keeps the loud error.
            if (DatabaseValidator.IsLockedOrAccessDenied(ex))
                _logger.LogInfo($"CheckoutService: cannot read D to make D'' ({ex.GetType().Name}: {ex.Message})");
            else
                _logger.LogError(ex, "CheckoutService: failed to copy D to D''.tmp");
            TryDelete(roTmpPath);
            return null;
        }

        // An I/O or permission failure reading D must make this method return null (the caller then
        // shows the Try again / Close prompt) — never escape as an exception.
        var (hashCompleted, roSourceHash, hashUnreadable) = await HashSourceAsync(SourcePath, "read-only setup hash");

        if (hashUnreadable)
        {
            TryDelete(roTmpPath);
            return null;
        }

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

            bool retryCompleted;
            try
            {
                retryCompleted = await NetworkFileOps.CopyAsync(SourcePath, roTmpPath);
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"CheckoutService: read-only setup retry copy failed ({ex.GetType().Name}: {ex.Message})");
                TryDelete(roTmpPath);
                return null;
            }

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
    /// Runs <see cref="SetupReadOnlySnapshotAsync"/> until it succeeds or the user gives up.
    /// Each time the snapshot cannot be made, <paramref name="askTryAgainAsync"/> is awaited:
    /// <c>true</c> tries again, <c>false</c> stops. D is never opened as a fallback.
    ///
    /// <para>The loop lives here, rather than in the window code that shows the dialog, so it can
    /// be tested without any UI. There is no attempt limit: every pass is bounded by the network
    /// deadlines inside <see cref="SetupReadOnlySnapshotAsync"/>, and each retry needs an explicit
    /// user decision, so it cannot spin.</para>
    /// </summary>
    /// <param name="askTryAgainAsync">
    /// Called after each failed attempt. Shows the <see cref="LocalCopyCheckPrompt.CouldntMakeCopyMessage"/> prompt and returns
    /// true for "Try again", false for "Close". Not called after a successful attempt.
    /// </param>
    /// <returns>The path to D'' on success; <c>null</c> when the user chose not to try again.</returns>
    public async Task<string?> SetupReadOnlySnapshotWithRetryAsync(Func<Task<bool>> askTryAgainAsync)
    {
        while (true)
        {
            var snapshotPath = await SetupReadOnlySnapshotAsync();
            if (snapshotPath is not null)
                return snapshotPath;

            if (!await askTryAgainAsync())
                return null;
        }
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
        var normalized = NormalizeSourcePath(sourcePath);
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
    /// Hashes the shared source database D with <see cref="NetworkFileOps.ComputeHashAsync"/>, but
    /// reports an I/O or permission failure as a result instead of letting it escape.
    ///
    /// <para><b>Why:</b> <c>ComputeHashAsync</c> deliberately propagates open exceptions. For D that
    /// means a healthy database that another program holds exclusively (sharing violation) or that
    /// we may not read throws <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>.
    /// Left uncaught in a checkout, that exception skipped every cleanup branch: the write lock
    /// taken just before stayed held (live <c>.lock</c> file and heartbeat) until the app exited.
    /// Callers turn <c>Unreadable</c> into <see cref="CheckoutOutcome.SourceUnreadable"/> (or, for
    /// the read-only snapshot, <c>null</c>) after cleaning up.</para>
    /// </summary>
    /// <param name="path">Full path of D.</param>
    /// <param name="stage">Short label for the log line, e.g. "checkout hash".</param>
    /// <returns>
    /// <c>Completed</c>/<c>Hash</c> exactly as <see cref="NetworkFileOps.ComputeHashAsync"/> returns
    /// them when it ran to a conclusion (<c>Unreadable</c> false); or <c>(false, null, true)</c> when
    /// it threw <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
    /// Any other exception type still propagates.
    /// </returns>
    private async Task<(bool Completed, string? Hash, bool Unreadable)> HashSourceAsync(string path, string stage)
    {
        try
        {
            var (completed, hash) = await NetworkFileOps.ComputeHashAsync(path);
            return (completed, hash, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInfo($"CheckoutService: {stage} — cannot read the source database ({ex.GetType().Name}: {ex.Message})");
            return (false, null, true);
        }
    }

    /// <summary>How <see cref="CopyAndVerifyAsync"/> ended.</summary>
    internal enum CopyVerifyOutcome
    {
        /// <summary>The copy landed and hashes like the source.</summary>
        Ok,

        /// <summary>The copy (or its retry) stalled past the network deadline.</summary>
        Unreachable,

        /// <summary>The source could not be read: another program holds it open, or access is denied.</summary>
        Unreadable,

        /// <summary>Any other failure, including a copy that still does not match after the retry.</summary>
        Failed
    }

    /// <summary>
    /// Copies the shared database D to a LOCAL file and proves the copy is byte-identical to D — the one
    /// copy-and-verify step shared by <see cref="CheckoutAsync"/> (D → D') and the fresh copy made after a failed
    /// open-time check (<see cref="RefreshLocalCopyAfterFailedCheckAsync"/>). D is only read, never changed.
    ///
    /// <para><b>Sequence:</b> stream D to <paramref name="destPath"/> with the stall-aware chunked copy; hash the
    /// result (local, no deadline needed) and compare it with <paramref name="expectedHash"/>, the hash of D taken
    /// just before. A mismatch usually means D was being written during the copy, so it is tried once more; a second
    /// mismatch is a failure. A failure of the RETRY stage deletes <paramref name="destPath"/> (a half-verified file
    /// must not be mistaken for a good one); a failure of the first copy leaves whatever was written, as the checkout
    /// always has.</para>
    ///
    /// <para>Never touches the lock, the marker or any session state: the caller decides what to give back.
    /// <c>internal</c> so tests can drive its failure branches directly.</para>
    /// </summary>
    /// <param name="sourcePath">Full path of D (may be on a share).</param>
    /// <param name="destPath">Full path of the local file to write (created or overwritten).</param>
    /// <param name="expectedHash">Uppercase hex SHA-256 of D, taken just before the copy.</param>
    /// <param name="stage">Short label for the log lines, e.g. "checkout".</param>
    /// <returns>How it ended; see <see cref="CopyVerifyOutcome"/>.</returns>
    /// <exception cref="IOException">The freshly written local file could not be hashed.</exception>
    internal async Task<CopyVerifyOutcome> CopyAndVerifyAsync(
        string sourcePath, string destPath, string expectedHash, string stage)
    {
        try
        {
            if (!await NetworkFileOps.CopyAsync(sourcePath, destPath))
            {
                _logger.LogInfo($"CheckoutService: {stage} copy timed out — network unreachable");
                return CopyVerifyOutcome.Unreachable;
            }
        }
        catch (Exception ex) when (DatabaseValidator.IsLockedOrAccessDenied(ex))
        {
            // D became held / unreadable between the hash and the copy: an ordinary, retryable condition.
            _logger.LogInfo($"CheckoutService: {stage} — cannot read D to copy it ({ex.GetType().Name}: {ex.Message})");
            return CopyVerifyOutcome.Unreadable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"CheckoutService: failed to copy D to the local file ({stage})");
            return CopyVerifyOutcome.Failed;
        }

        // Verify the copy. destPath is local — no timeout needed.
        if (ComputeHash(destPath) == expectedHash)
            return CopyVerifyOutcome.Ok;

        _logger.LogInfo($"CheckoutService: {stage} — hash mismatch after copy, retrying once.");

        bool retryCompleted;
        try
        {
            retryCompleted = await NetworkFileOps.CopyAsync(sourcePath, destPath);
        }
        catch (Exception ex)
        {
            // A locked / access-denied D gets its own outcome so the user is told the real reason.
            var unreadable = DatabaseValidator.IsLockedOrAccessDenied(ex);
            _logger.LogInfo($"CheckoutService: {stage} retry copy failed ({ex.GetType().Name}: {ex.Message})");
            TryDelete(destPath);
            return unreadable ? CopyVerifyOutcome.Unreadable : CopyVerifyOutcome.Failed;
        }

        if (!retryCompleted)
        {
            _logger.LogInfo($"CheckoutService: {stage} retry copy timed out — network unreachable");
            TryDelete(destPath);
            return CopyVerifyOutcome.Unreachable;
        }

        if (ComputeHash(destPath) != expectedHash)
        {
            _logger.LogInfo($"CheckoutService: {stage} — hash mismatch on retry, giving up.");
            TryDelete(destPath);
            return CopyVerifyOutcome.Failed;
        }

        return CopyVerifyOutcome.Ok;
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
    /// <exception cref="Exception">
    /// Propagates any SQLite or I/O error to the caller — including a SQLite "unable to open database
    /// file" error when <paramref name="sourcePath"/> does not exist (see the remarks on the source
    /// connection below). In that case <paramref name="destPath"/> is not created either.
    /// </exception>
    private static void BackupSqliteDatabase(string sourcePath, string destPath)
    {
        // Pooling=False is essential: with the default connection pool, Dispose()
        // returns connections to the pool rather than closing them, leaving the
        // destination file (D.tmp) open. File.Move(D.tmp → D) then fails with
        // "access denied" because the pool still holds the file handle.
        //
        // The SOURCE is opened Mode=ReadWrite — read-write but NEVER create. The default mode
        // (ReadWriteCreate) would silently conjure an EMPTY database at D' if the working copy had
        // been deleted while the app ran (on macOS unlinking an open file succeeds), and the save
        // would then snapshot that empty file and push it over the shared database D: the whole
        // department's schedule replaced by nothing. With ReadWrite a missing D' is a failed open, which
        // SaveAsync reports as a clean CopyError before anything is written to the share. (ReadWrite rather
        // than ReadOnly changes only the create flag, so WAL handling stays exactly as it was.)
        // The DESTINATION is a brand-new local snapshot file and must be created.
        using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadWrite;Pooling=False");
        using var dest   = new SqliteConnection($"Data Source={destPath};Pooling=False");
        // Source first: if it cannot be opened, the destination is never created.
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

/// <summary>
/// How <see cref="CheckoutService.RefreshLocalCopyAfterFailedCheckAsync"/> ended (spec item 5, step 5d).
/// </summary>
public enum LocalCopyRefreshResult
{
    /// <summary>The local copy is now a freshly made, hash-verified copy of the shared database.</summary>
    Ok,

    /// <summary>The shared database could not be reached in time. Nothing was changed.</summary>
    Unreachable,

    /// <summary>
    /// The shared database (or, for a writer, the local file to be replaced) is held by another program, or access is
    /// denied. Nothing was changed. ALSO returned (step 3c) when the copy was made but the re-run version gate could
    /// not read the new copy's stamp, or raise one, even after its quiet retries: the writer has then been stood
    /// down (lock released, new local copy removed) and the caller gives up.
    /// </summary>
    Unreadable,

    /// <summary>Any other failure, including a copy that would not verify. The previous local copy is left as it was.</summary>
    Failed,

    /// <summary>
    /// A writer's local copy holds unsaved changes (it has a dirty marker) and was NOT replaced. The caller must set
    /// it aside first; unsaved work is never overwritten.
    /// </summary>
    HoldsUnsavedChanges,

    /// <summary>
    /// The fresh copy was made, but the version gate, run again on it (spec item 3, step 3c), found that the database
    /// requires a newer TermPoint than this one. The writer has been stood down: the lock is released, the new local
    /// copy removed, <see cref="CheckoutService.RequiredAppVersion"/> is set and the session is now a read-only one on
    /// a fresh snapshot D'' (<see cref="CheckoutService.WorkingPath"/>). The caller opens again from
    /// <see cref="CheckoutService.WorkingPath"/>; the read-only banner explains why. Writers only.
    /// </summary>
    NowReadOnly
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
    /// <summary>
    /// A stale lock exists. Prompt the user (<see cref="StaleLockPrompt"/>), then, if confirmed, wait out
    /// <see cref="CheckoutService.GetTakeOverWait"/> (<see cref="CheckoutService.WaitForTakeOverConfirmationAsync"/>)
    /// and call <see cref="CheckoutService.ForceCheckoutAsync"/>; otherwise (declined, or the wait was
    /// cancelled) open read-only with
    /// <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/> and <see cref="CheckoutService.StartReaderPollingAsync"/>.
    /// </summary>
    StaleHolder,
    /// <summary>
    /// Fatal error (hash mismatch or copy failure). Caller should attempt
    /// <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/> as a fallback — and, if even that
    /// fails, must not fall back to opening D directly.
    /// </summary>
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
    RecoveryConflict,
    /// <summary>
    /// D does not exist, so there is nothing to check out. Returned without taking the write lock
    /// and without creating any file. New databases must be created (with
    /// <see cref="NewDatabaseFile.CreateAsync"/>) before checkout; this outcome therefore means D
    /// was moved, renamed or deleted in the meantime. Tell the user — there is no fallback.
    /// </summary>
    SourceMissing,
    /// <summary>
    /// D exists but could not be read: another program holds it open exclusively (a sharing
    /// violation) or access is denied. Says nothing about the file's health, so it must not be
    /// described as damaged. Any write lock taken for this attempt has been RELEASED (no
    /// <c>.lock</c> file or heartbeat is left behind) and no working copy is left; in the
    /// crash-recovery path D' and its dirty marker are kept so recovery is offered again. There is
    /// no read-only fallback — it would need to read D too. Tell the user to try again in a moment.
    /// </summary>
    SourceUnreadable,
    /// <summary>
    /// Returned only by <see cref="CheckoutService.ForceCheckoutAsync"/>: the user confirmed taking over a
    /// lock judged inactive, but when the lock file was re-read at click time it was no longer the same
    /// lock — the holder woke and resumed its heartbeat, another session took the lock, or the file could
    /// not be read. Nothing was deleted or created, the write lock is NOT held, and
    /// <see cref="CheckoutService.CurrentHolder"/> names whoever holds it now (null if unreadable). In
    /// crash-recovery mode D' and its dirty marker are untouched. The caller should tell the user
    /// (<see cref="StaleLockPrompt.BuildActiveAgainMessage"/>) and open read-only via
    /// <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/> followed by
    /// <see cref="CheckoutService.StartReaderPollingAsync"/>.
    /// </summary>
    HolderActiveAgain,
    /// <summary>
    /// The write lock was taken and D' made, but the version gate (spec item 3, step 3c) could not do its job: the
    /// database's <see cref="MinimumVersionToWrite"/> stamp could not be read, or a stamp this release must write
    /// could not be written, even after quiet retries (3 attempts, 1 s apart) — typically antivirus or backup
    /// software still holding the file that was just copied. Write access is REFUSED rather than guessed at.
    /// The write lock has been released (no reader mode, no polling), <see cref="CheckoutService.Mode"/> is read-only,
    /// no D'' was made, and D' with its sidecars and marker has been removed — except in a crash-recovery open, where
    /// D' and its dirty marker are kept so the unsaved changes are offered again. D is untouched. Says nothing about
    /// the health of D, so it must not be described as damage. The caller shows the "in use" prompt
    /// (<see cref="LocalCopyCheckPrompt.InUseMessage"/>, Try again / Close): Try again runs the checkout again with
    /// the same arguments; Close gives up like any other failed checkout.
    /// </summary>
    VersionCheckUnavailable
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
    /// <summary>
    /// A transient failure — retry is appropriate. D was not changed and D' + its dirty marker are intact.
    /// Covers a stalled or failed D.tmp copy, verify or rename, a file error on D or D.tmp (in use by
    /// another program, access denied, vanished), a missing or unreadable working copy D', an overlapping
    /// save, and any unexpected exception (the safety net in <see cref="CheckoutService.SaveAsync"/>).
    /// </summary>
    CopyError,
    /// <summary>Save was called while not in write mode.</summary>
    NotInWriteMode
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

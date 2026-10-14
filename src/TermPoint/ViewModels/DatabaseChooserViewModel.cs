using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;
using TermPoint.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace TermPoint.ViewModels;

/// <summary>
/// Whether the chooser is shown for normal startup selection or for recovery.
/// </summary>
public enum ChooserMode
{
    /// <summary>Normal startup — user picks which database to open.</summary>
    Normal,

    /// <summary>The saved database is missing or corrupt — recovery context is shown.</summary>
    Recovery
}

/// <summary>
/// Indicates why the database recovery was triggered (only relevant in Recovery mode).
/// </summary>
public enum RecoveryReason
{
    /// <summary>The database file could not be found at the path saved in settings.</summary>
    NotFound,

    /// <summary>The database file exists but failed its integrity check.</summary>
    Corrupt,

    /// <summary>The location of the saved database (typically a network share) could not be reached.</summary>
    Unreachable,

    /// <summary>
    /// The saved database exists but couldn't be read just now (another program has it open, or
    /// access is denied). Not damage — the user is expected to simply retry.
    /// </summary>
    Unreadable,

    /// <summary>
    /// A database switch failed after the previously open database had already been closed (spec
    /// item 4d), so no database is open. Not a statement about the saved path — which stays in the
    /// recent list, since a retry will often succeed — only that the user must now choose one.
    /// </summary>
    SwitchFailed,

    /// <summary>
    /// Another live TermPoint process of this user already has the chosen database open on this
    /// computer (spec item 4b, <see cref="DatabaseInstanceGuard"/>), so this one was refused before it
    /// touched anything. Not damage, and not a problem with the path — which stays in the recent list.
    /// The user picks a different database or exits; picking the same one again is refused again.
    /// </summary>
    AlreadyOpen
}

/// <summary>
/// The result chosen by the user in the database chooser window.
/// </summary>
public enum ChooserOutcome
{
    /// <summary>User exited without resolving — app should shut down.</summary>
    None,

    /// <summary>A valid database path was resolved (recent item, browse, or restore).</summary>
    Resolved,

    /// <summary>User chose to create a new database — route to the setup wizard.</summary>
    StartWizard
}

/// <summary>
/// Which action option the user has selected below the recent databases list.
/// </summary>
public enum ChooserOption
{
    None,
    BrowseForIt,
    RestoreFromBackup,
    CreateNew
}

/// <summary>
/// What the chooser knows about the restore destination, from its deadline-bounded probe (spec item 5c). Decides
/// the Step 2 status text and whether Continue is enabled.
/// </summary>
public enum RestoreDestinationState
{
    /// <summary>No destination yet.</summary>
    None,

    /// <summary>The destination is being probed; Continue waits for the answer.</summary>
    Checking,

    /// <summary>A file is at the destination: restoring replaces it (after a confirmation), keeping the current file beside it.</summary>
    Exists,

    /// <summary>Nothing is at the destination: restoring creates a new database there.</summary>
    Missing,

    /// <summary>The destination did not answer in time; Continue stays disabled.</summary>
    Unreachable,

    /// <summary>The destination is the backup file itself, which must never be restored onto itself; Continue stays disabled.</summary>
    IsBackupFile
}

/// <summary>
/// View model for the database chooser window. Shown at startup to let the user
/// pick from recent databases, browse for a file, restore from a backup, or create
/// a new database via the wizard. In recovery mode, an error banner explains what
/// went wrong with the previously configured database.
/// </summary>
/// <remarks>
/// File-picker delegates (<see cref="PickDatabaseFileAsync"/>,
/// <see cref="PickBackupFileAsync"/>, <see cref="PickRestoreFolderAsync"/>) are
/// injected by the window code-behind so the VM has no reference to the Window.
/// The window subscribes to <see cref="CloseRequested"/> and hides itself when
/// the VM fires it.
/// </remarks>
public partial class DatabaseChooserViewModel : ObservableObject
{
    // ── Shared user-facing messages / mappings ───────────────────────────

    /// <summary>
    /// Inline error for a database file that exists but couldn't be read (held open by another
    /// program, or access denied). Deliberately says nothing about damage: the file is probably
    /// fine, and a user told otherwise might restore a backup over a good, current database.
    /// </summary>
    public const string UnreadableDatabaseMessage =
        "TermPoint can't read that database right now. Another program may have it open, " +
        "or you may not have permission to read it. Try again in a moment.";

    /// <summary>
    /// Message for a database file that fails its integrity check — the one wording that may say
    /// "damaged". Shared by the chooser and by the database-switch pre-check
    /// (<see cref="DatabaseSwitchPrecheck"/>) so both report real corruption identically.
    /// </summary>
    public const string DamagedDatabaseMessage =
        "That database failed its integrity check and may be damaged.";

    /// <summary>
    /// The recovery-banner text when a database switch failed after the previous database was closed
    /// (<see cref="RecoveryReason.SwitchFailed"/>). The path beneath it is the one that could not be opened.
    /// </summary>
    public const string SwitchFailedDescription =
        "TermPoint couldn't open the database you chose, and the database you had open has been closed. " +
        "Choose a database to continue.";

    /// <summary>
    /// The recovery-banner text when the chosen database is already open in another TermPoint window of
    /// this user on this computer (<see cref="RecoveryReason.AlreadyOpen"/>). The path beneath it is the
    /// database that was refused.
    /// </summary>
    public const string AlreadyOpenDescription =
        "That database is already open in another TermPoint window on this computer. " +
        "Choose a different database, or exit.";

    /// <summary>Inline error for a chosen backup file that exists but couldn't be read right now.</summary>
    public const string UnreadableBackupMessage =
        "That backup can't be read right now. Another program may have it open. Try again in a moment.";

    /// <summary>
    /// Maps a validation outcome to the startup <see cref="RecoveryReason"/> shown in the
    /// recovery-mode chooser. Shared by both startup branches in <c>MainWindow</c> so an
    /// outcome can never be mapped differently depending on the "always open most recent"
    /// setting. <see cref="DatabaseValidationResult.Unreadable"/> maps to its own reason — it is
    /// never folded into Corrupt or NotFound.
    /// </summary>
    /// <param name="validation">A non-Ok result of <c>DatabaseValidator.ValidateAsync</c>.</param>
    /// <returns>
    /// Corrupt, Unreachable and Unreadable map to the reason of the same name; everything else
    /// (Missing) maps to <see cref="RecoveryReason.NotFound"/>.
    /// </returns>
    public static RecoveryReason RecoveryReasonFor(DatabaseValidationResult validation) => validation switch
    {
        DatabaseValidationResult.Corrupt     => RecoveryReason.Corrupt,
        DatabaseValidationResult.Unreachable => RecoveryReason.Unreachable,
        DatabaseValidationResult.Unreadable  => RecoveryReason.Unreadable,
        _                                    => RecoveryReason.NotFound
    };

    /// <summary>
    /// The inline message shown when a recent database fails validation (double-click open and
    /// the Continue button share it, so they cannot drift apart).
    /// </summary>
    /// <param name="validation">A non-Ok result of <c>DatabaseValidator.ValidateAsync</c>.</param>
    /// <returns>The user-facing message for that result.</returns>
    private static string DescribeRecentOpenFailure(DatabaseValidationResult validation) => validation switch
    {
        DatabaseValidationResult.Unreachable => NetworkFileOps.UnreachableMessage,
        DatabaseValidationResult.Unreadable  => UnreadableDatabaseMessage,
        DatabaseValidationResult.Corrupt     => DamagedDatabaseMessage,
        _                                    => "That database could not be found — it may have been moved or deleted."
    };

    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the chooser view model in normal (non-recovery) mode.
    /// </summary>
    public DatabaseChooserViewModel() : this(ChooserMode.Normal, null, null) { }

    /// <summary>
    /// Initialises the chooser view model.
    /// </summary>
    /// <param name="mode">Whether this is a normal chooser or a recovery scenario.</param>
    /// <param name="reason">Why recovery was triggered (ignored in Normal mode).</param>
    /// <param name="lastKnownPath">The database path from settings, shown in recovery mode.</param>
    public DatabaseChooserViewModel(ChooserMode mode, RecoveryReason? reason, string? lastKnownPath)
    {
        Mode          = mode;
        Reason        = reason;
        LastKnownPath = lastKnownPath ?? "(not set)";
        _lastKnownPathArgument = lastKnownPath;

        if (mode == ChooserMode.Recovery && reason.HasValue)
        {
            ProblemDescription = reason.Value switch
            {
                RecoveryReason.NotFound =>
                    "TermPoint could not find your most recently used database at its previous location. " +
                    "It may have been moved, renamed, or deleted, or the drive may be inaccessible.",
                RecoveryReason.Unreachable =>
                    "The location of your most recently used database is currently unreachable. " +
                    "Check your network connection, or choose another database.",
                RecoveryReason.Unreadable =>
                    "TermPoint couldn't read your most recently used database just now. " +
                    "Another program may have it open (for example a backup or sync tool), " +
                    "or you may not have permission to read it. " +
                    "Try again in a moment, or choose another database.",
                RecoveryReason.SwitchFailed => SwitchFailedDescription,
                RecoveryReason.AlreadyOpen  => AlreadyOpenDescription,
                _ =>
                    "TermPoint found the database file but it failed an integrity check and may be damaged. " +
                    "It is not safe to open this file."
            };
        }

        AlwaysOpenRecent = AppSettings.Current.AlwaysOpenMostRecentDatabase;
        LoadRecentDatabases();
    }

    // ── Mode ──────────────────────────────────────────────────────────────

    /// <summary>Whether we are in normal chooser or recovery mode.</summary>
    public ChooserMode Mode { get; }

    /// <summary>True when the window is showing a recovery scenario with error banner.</summary>
    public bool IsRecoveryMode => Mode == ChooserMode.Recovery;

    /// <summary>The reason recovery was triggered (null in normal mode).</summary>
    public RecoveryReason? Reason { get; }

    /// <summary>The last-known path from settings, displayed in recovery mode.</summary>
    public string LastKnownPath { get; }

    /// <summary>Localised description of the problem (recovery mode only).</summary>
    public string? ProblemDescription { get; }

    /// <summary>Window title — changes based on mode.</summary>
    public string WindowTitle => IsRecoveryMode ? "Database Recovery" : "Choose Database";

    // ── Outcome (read by MainWindow after the window hides) ──────────────

    /// <summary>
    /// The outcome chosen by the user.
    /// Read by <see cref="MainWindow"/> after the window hides.
    /// </summary>
    public ChooserOutcome Outcome { get; private set; } = ChooserOutcome.None;

    /// <summary>
    /// The resolved database path.
    /// Non-null only when <see cref="Outcome"/> is <see cref="ChooserOutcome.Resolved"/>.
    /// </summary>
    public string? ResolvedPath { get; private set; }

    // ── Recent databases ─────────────────────────────────────────────────

    /// <summary>
    /// Recently opened databases. Deliberately NOT filtered by existence: probing
    /// each path with <c>File.Exists</c> on the UI thread blocks for the full SMB
    /// redirector timeout per dead-share entry (~60s stalls before the window can
    /// render). Opening a stale entry routes through the deadline-bounded
    /// validation flow, which shows proper missing/unreachable UX.
    /// </summary>
    public ObservableCollection<RecentDatabaseItem> RecentDatabases { get; } = new();

    /// <summary>True when at least one recent database is available.</summary>
    [ObservableProperty]
    private bool _hasRecentDatabases;

    /// <summary>The currently selected recent database item (single-click selects).</summary>
    [ObservableProperty]
    private RecentDatabaseItem? _selectedRecentDatabase;

    partial void OnSelectedRecentDatabaseChanged(RecentDatabaseItem? value)
    {
        // When a recent item is selected, deselect any action option
        if (value is not null)
            SelectedOption = ChooserOption.None;

        RecentError = null;
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ContinueButtonText));
    }

    /// <summary>
    /// Validation error shown inline when the user tries to open a recent database
    /// that is unreachable or missing.
    /// </summary>
    [ObservableProperty]
    private string? _recentError;

    /// <summary>Populates the recent databases list from AppSettings.</summary>
    private void LoadRecentDatabases()
    {
        RecentDatabases.Clear();
        foreach (var path in AppSettings.Current.RecentDatabases)
        {
            // Skip the path we already know is unreachable — the banner above already
            // tells the user about it, and listing it again invites a pointless click.
            // Deliberately NOT done for RecoveryReason.Unreadable: that file is probably fine
            // and the user is expected to retry it from this list once the other program lets go.
            // Likewise for RecoveryReason.SwitchFailed: the failure may have been momentary (a dropped
            // connection, a locked file), so the path stays listed and the user can simply retry it.
            // And for RecoveryReason.AlreadyOpen: the path is fine; the user may close the other
            // window and pick it again.
            if (Reason == RecoveryReason.Unreachable
                && path.Equals(LastKnownPath, StringComparison.OrdinalIgnoreCase))
                continue;

            RecentDatabases.Add(new RecentDatabaseItem
            {
                Path = path,
                DisplayName = Path.GetFileName(path)
            });
        }
        HasRecentDatabases = RecentDatabases.Count > 0;
    }

    /// <summary>
    /// Opens a recent database by double-click. Validates the path first so a
    /// dead network share produces a bounded (~5s) inline error instead of a
    /// 60-second hang inside the checkout flow.
    /// </summary>
    [RelayCommand]
    private async Task OpenRecentDatabase(RecentDatabaseItem? item)
    {
        if (item is null) return;

        RecentError = null;
        var validation = await DatabaseValidator.ValidateAsync(item.Path);
        if (validation != DatabaseValidationResult.Ok)
        {
            RecentError = DescribeRecentOpenFailure(validation);
            return;
        }

        ResolveWith(item.Path);
    }

    // ── Always-open-recent checkbox ──────────────────────────────────────

    /// <summary>
    /// Bound two-way to the "Always open my most recent database" checkbox.
    /// Persists immediately to AppSettings on change.
    /// </summary>
    [ObservableProperty]
    private bool _alwaysOpenRecent;

    partial void OnAlwaysOpenRecentChanged(bool value)
    {
        AppSettings.Current.AlwaysOpenMostRecentDatabase = value;
        AppSettings.Current.Save();
    }

    // ── Action option selection ──────────────────────────────────────────

    /// <summary>Which of the action options the user has selected.</summary>
    [ObservableProperty]
    private ChooserOption _selectedOption = ChooserOption.None;

    partial void OnSelectedOptionChanged(ChooserOption value)
    {
        // When an action option is selected, deselect any recent item
        if (value != ChooserOption.None)
            SelectedRecentDatabase = null;

        OnPropertyChanged(nameof(IsOptionBrowseSelected));
        OnPropertyChanged(nameof(IsOptionRestoreSelected));
        OnPropertyChanged(nameof(IsOptionCreateSelected));
        OnPropertyChanged(nameof(IsOptionBrowseExpanded));
        OnPropertyChanged(nameof(IsOptionRestoreExpanded));
        OnPropertyChanged(nameof(IsOptionCreateExpanded));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ContinueButtonText));
    }

    /// <summary>True when Browse is selected — radio-button binding.</summary>
    public bool IsOptionBrowseSelected => SelectedOption == ChooserOption.BrowseForIt;

    /// <summary>True when Restore is selected — radio-button binding.</summary>
    public bool IsOptionRestoreSelected => SelectedOption == ChooserOption.RestoreFromBackup;

    /// <summary>True when Create New is selected — radio-button binding.</summary>
    public bool IsOptionCreateSelected => SelectedOption == ChooserOption.CreateNew;

    /// <summary>Controls visibility of the Browse detail panel.</summary>
    public bool IsOptionBrowseExpanded => SelectedOption == ChooserOption.BrowseForIt;

    /// <summary>Controls visibility of the Restore detail panel.</summary>
    public bool IsOptionRestoreExpanded => SelectedOption == ChooserOption.RestoreFromBackup;

    /// <summary>Controls visibility of the Create New detail panel.</summary>
    public bool IsOptionCreateExpanded => SelectedOption == ChooserOption.CreateNew;

    /// <summary>Selects the Browse option.</summary>
    [RelayCommand]
    private void SelectOptionBrowse() => SelectedOption = ChooserOption.BrowseForIt;

    /// <summary>Selects the Restore option.</summary>
    [RelayCommand]
    private void SelectOptionRestore() => SelectedOption = ChooserOption.RestoreFromBackup;

    /// <summary>Selects the Create New option.</summary>
    [RelayCommand]
    private void SelectOptionCreate() => SelectedOption = ChooserOption.CreateNew;

    // ── Browse for database (same as old Option A) ───────────────────────

    /// <summary>The path chosen via the file picker, after passing validation.</summary>
    [ObservableProperty]
    private string? _browsedPath;

    partial void OnBrowsedPathChanged(string? value) =>
        OnPropertyChanged(nameof(CanContinue));

    /// <summary>Validation error for browse, or null when the chosen file is valid.</summary>
    [ObservableProperty]
    private string? _browseError;

    /// <summary>
    /// Injected by the window code-behind. Opens an OS file picker and returns
    /// the chosen path, or null if the user cancelled.
    /// </summary>
    public Func<Task<string?>>? PickDatabaseFileAsync { get; set; }

    /// <summary>Opens the file picker and validates the chosen file.</summary>
    [RelayCommand]
    private async Task BrowseForDatabase()
    {
        if (PickDatabaseFileAsync is null) return;

        var path = await PickDatabaseFileAsync();
        if (path is null) return;

        var result = await DatabaseValidator.ValidateAsync(path);
        if (result == DatabaseValidationResult.Ok)
        {
            BrowsedPath = path;
            BrowseError = null;
        }
        else
        {
            BrowsedPath = null;
            BrowseError = result switch
            {
                DatabaseValidationResult.Corrupt     => "That file does not appear to be a valid database — please try another.",
                DatabaseValidationResult.Unreachable => NetworkFileOps.UnreachableMessage,
                DatabaseValidationResult.Unreadable  => UnreadableDatabaseMessage,
                _                                    => "The selected file could not be found."
            };
        }
    }

    // ── Restore from backup (spec item 5, step 5c) ───────────────────────

    /// <summary>
    /// The raw database path the chooser was opened about (the constructor's argument, null when none), as opposed
    /// to <see cref="LastKnownPath"/>, which is a display string ("(not set)" when there is none). Used to choose the
    /// default restore destination (<see cref="RestoreDestination.SuggestRestorePath"/>).
    /// </summary>
    private readonly string? _lastKnownPathArgument;

    /// <summary>Path to the backup file chosen by the user.</summary>
    [ObservableProperty]
    private string? _backupPath;

    partial void OnBackupPathChanged(string? value)
    {
        // The backup may now be the very file the destination names, which changes the status and Continue.
        BeginRestoreDestinationProbe();
        OnPropertyChanged(nameof(CanContinue));
    }

    /// <summary>Suggested full path where the backup will be restored (shown in Step 2).</summary>
    [ObservableProperty]
    private string? _restorePath;

    partial void OnRestorePathChanged(string? value)
    {
        BeginRestoreDestinationProbe();
        OnPropertyChanged(nameof(CanContinue));
    }

    /// <summary>Error message for restore, or null when everything is valid.</summary>
    [ObservableProperty]
    private string? _restoreError;

    /// <summary>Controls whether the Step 2 destination row is visible.</summary>
    [ObservableProperty]
    private bool _isBackupStepTwoVisible;

    /// <summary>
    /// True while the restore runs. The window disables every control (options, recents, Browse…, Change folder…,
    /// Continue and Exit) and shows <see cref="RestoringText"/>; closing the window is blocked too, because exiting
    /// mid-restore would leave a stray lock file behind.
    /// </summary>
    [ObservableProperty]
    private bool _isRestoring;

    partial void OnIsRestoringChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    /// <summary>The in-progress line shown while <see cref="IsRestoring"/> (text lives in <see cref="RestorePrompt"/>).</summary>
    public string RestoringText => RestorePrompt.RestoringText;

    /// <summary>The reassurance shown under the destination status: restoring never modifies the backup file.</summary>
    public string BackupNotModifiedNote => RestorePrompt.BackupNotModifiedNote;

    /// <summary>
    /// What the chooser knows about the restore destination, from a deadline-bounded probe. Drives
    /// <see cref="RestoreStatusText"/> and whether Continue is enabled (only Exists and Missing allow it).
    /// </summary>
    [ObservableProperty]
    private RestoreDestinationState _restoreDestinationState = RestoreDestinationState.None;

    partial void OnRestoreDestinationStateChanged(RestoreDestinationState value)
    {
        OnPropertyChanged(nameof(RestoreStatusText));
        OnPropertyChanged(nameof(IsRestoreStatusProblem));
        OnPropertyChanged(nameof(CanContinue));
    }

    /// <summary>
    /// The Step 2 status line for the destination: what the restore will do there (replace the database that is
    /// there, create a new one) or why it cannot be done (not responding, the backup file itself). Empty before a
    /// destination exists. The wording lives in <see cref="RestorePrompt"/>.
    /// </summary>
    public string RestoreStatusText => RestoreDestinationState switch
    {
        RestoreDestinationState.Checking     => RestorePrompt.DestinationCheckingText,
        RestoreDestinationState.Exists       => RestorePrompt.BuildDestinationExistsText(RestorePath ?? string.Empty),
        RestoreDestinationState.Missing      => RestorePrompt.DestinationMissingText,
        RestoreDestinationState.Unreachable  => RestorePrompt.DestinationUnreachableText,
        RestoreDestinationState.IsBackupFile => RestorePrompt.DestinationIsBackupFileText,
        _                                    => string.Empty
    };

    /// <summary>True when <see cref="RestoreStatusText"/> reports a problem (shown in the error colour) rather than information.</summary>
    public bool IsRestoreStatusProblem =>
        RestoreDestinationState is RestoreDestinationState.Unreachable or RestoreDestinationState.IsBackupFile;

    /// <summary>
    /// Incremented every time a new probe starts, so a probe that finishes after the destination changed again
    /// can tell it is out of date and discard its answer.
    /// </summary>
    private int _restoreProbeVersion;

    /// <summary>The most recent destination probe (completed when none is running). Exposed to tests so they can wait for the answer.</summary>
    internal Task RestoreProbeTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// How the destination is probed: <see cref="NetworkFileOps.ProbeFileAsync"/> (deadline-bounded, tells "absent"
    /// from "no answer"). A seam so tests can hold an answer back and release answers in any order.
    /// </summary>
    internal Func<string, Task<FileProbeResult>> DestinationProbe { get; set; } = NetworkFileOps.ProbeFileAsync;

    /// <summary>
    /// Injected by the window code-behind. Opens an OS file picker for backup files.
    /// </summary>
    public Func<Task<string?>>? PickBackupFileAsync { get; set; }

    /// <summary>
    /// Injected by the window code-behind. Opens an OS folder picker for the
    /// restore destination.
    /// </summary>
    public Func<Task<string?>>? PickRestoreFolderAsync { get; set; }

    /// <summary>
    /// Injected by the window code-behind. Shows a modal two-button confirmation: (title, message, confirm label)
    /// → true when the user confirmed. Used only when the restore would replace an existing database.
    /// </summary>
    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>
    /// Injected by the window code-behind. Shows a modal message with an OK button: (title, message). Used to
    /// announce a completed restore before the database opens.
    /// </summary>
    public Func<string, string, Task>? ShowMessageAsync { get; set; }

    /// <summary>
    /// The operation that performs the restore: (backup path, destination path, may an existing database be
    /// replaced) → result. Null (the default) means <see cref="CheckoutService.RestoreIntoAsync"/> on the app's
    /// checkout service; tests replace it so no real files are needed.
    /// </summary>
    internal Func<string, string, bool, Task<RestoreResult>>? RestoreOperation { get; set; }

    /// <summary>Opens the file picker, validates the backup, and reveals Step 2.</summary>
    [RelayCommand]
    private async Task BrowseForBackup()
    {
        if (PickBackupFileAsync is null) return;

        var path = await PickBackupFileAsync();
        if (path is null) return;

        // A damaged BACKUP is reported as such (spec item 5, step 5e), not as a damaged shared database.
        var backupValidation = await DatabaseValidator.ValidateAsync(path, DamagedFileKind.BackupBeingRestored);
        if (backupValidation == DatabaseValidationResult.Unreachable)
        {
            RestoreError = NetworkFileOps.UnreachableMessage;
            return;
        }
        // A backup another program has open is not a bad backup — say so, with its own wording,
        // rather than the "not a valid database backup" message below.
        if (backupValidation == DatabaseValidationResult.Unreadable)
        {
            RestoreError = UnreadableBackupMessage;
            return;
        }
        if (backupValidation != DatabaseValidationResult.Ok)
        {
            RestoreError = "That file could not be opened as a valid database backup.";
            return;
        }

        var suggested = RestoreDestination.SuggestRestorePath(path, _lastKnownPathArgument, AppSettings.Current.DatabasePath);
        var nothingChanged = BackupPath == path && RestorePath == suggested;

        BackupPath   = path;
        RestoreError = null;
        RestorePath  = suggested;
        IsBackupStepTwoVisible = true;

        // Picking the same backup again changes neither property, so neither would start a probe — but the
        // destination may have changed in the meantime (a file created, a share that went away): ask again.
        if (nothingChanged)
            BeginRestoreDestinationProbe();
    }

    /// <summary>
    /// Opens the folder picker and moves the restore destination to the chosen folder. The file name stays the
    /// one derived from the backup (<see cref="RestoreDestination.SuggestFileName"/>); only the folder changes.
    /// </summary>
    [RelayCommand]
    private async Task ChangeRestoreFolder()
    {
        if (PickRestoreFolderAsync is null || RestorePath is null) return;

        var folder = await PickRestoreFolderAsync();
        if (folder is null) return;

        RestoreError = null;
        RestorePath  = RestoreDestination.WithFolder(RestorePath, folder);
    }

    /// <summary>
    /// Starts a fresh, deadline-bounded probe of <see cref="RestorePath"/> and updates
    /// <see cref="RestoreDestinationState"/> when it answers — never blocking the UI. A destination that is the
    /// backup file itself needs no probe and is refused at once. Called whenever the destination or the backup
    /// changes; an answer that arrives after the destination changed again is discarded.
    /// </summary>
    private void BeginRestoreDestinationProbe()
    {
        var version     = ++_restoreProbeVersion;
        var destination = RestorePath;

        if (string.IsNullOrWhiteSpace(destination))
        {
            RestoreDestinationState = RestoreDestinationState.None;
            RestoreProbeTask        = Task.CompletedTask;
        }
        else if (RestoreDestination.IsSameFile(BackupPath, destination))
        {
            RestoreDestinationState = RestoreDestinationState.IsBackupFile;
            RestoreProbeTask        = Task.CompletedTask;
        }
        else
        {
            RestoreDestinationState = RestoreDestinationState.Checking;
            RestoreProbeTask        = ProbeRestoreDestinationAsync(destination, version);
        }

        // The "replaces …" text names the destination, so it can change without the state changing.
        OnPropertyChanged(nameof(RestoreStatusText));
    }

    /// <summary>
    /// Probes <paramref name="destination"/> (<see cref="DestinationProbe"/>: bounded by the network deadline, tells
    /// "absent" from "no answer") and publishes the answer, unless a newer probe has started.
    /// </summary>
    /// <param name="destination">The path to probe.</param>
    /// <param name="version">The probe version this call belongs to; stale when it no longer equals the current one.</param>
    private async Task ProbeRestoreDestinationAsync(string destination, int version)
    {
        var probe = await DestinationProbe(destination);
        if (version != _restoreProbeVersion)
            return;

        RestoreDestinationState = probe switch
        {
            FileProbeResult.Exists  => RestoreDestinationState.Exists,
            FileProbeResult.Missing => RestoreDestinationState.Missing,
            _                       => RestoreDestinationState.Unreachable
        };
    }

    /// <summary>
    /// Restores the chosen backup to the chosen destination and, when that worked, resolves the chooser with the
    /// destination so the database opens through the normal startup flow.
    ///
    /// <para>If the destination already holds a database the user first confirms ("This replaces … for everyone
    /// who uses it"); creating a new file needs no confirmation. The decision is passed on as
    /// <c>allowReplace</c>, so a database that appears at the destination after the user was told "a new database
    /// will be created" is refused rather than replaced unconfirmed. While the restore runs
    /// <see cref="IsRestoring"/> is true. A replaced database is announced with a "Backup Restored" message before
    /// it opens; a created one just opens; every failure is shown inline and the user stays in the chooser.</para>
    /// </summary>
    private async Task RestoreFromBackupAndOpenAsync()
    {
        var backup    = BackupPath!;
        var target    = RestorePath!;
        var replacing = RestoreDestinationState == RestoreDestinationState.Exists;
        RestoreError  = null;

        if (replacing)
        {
            // Without a way to ask, never replace a database silently.
            if (ConfirmAsync is null) return;

            var confirmed = await ConfirmAsync(
                RestorePrompt.ChooserConfirmTitle,
                RestorePrompt.BuildChooserConfirmMessage(Path.GetFileName(backup), target),
                RestorePrompt.ConfirmLabel);
            if (!confirmed) return;
        }

        RestoreResult result;
        IsRestoring = true;
        try
        {
            var restore = RestoreOperation ?? DefaultRestoreOperation;
            result = await restore(backup, target, replacing);
        }
        catch (Exception ex)
        {
            // RestoreIntoAsync never throws, so this is a safety net: never leave the chooser stuck "restoring".
            App.Logger.LogError(ex, "DatabaseChooserViewModel: unexpected exception while restoring a backup");
            result = new RestoreResult(RestoreOutcome.Failed, Error: ex);
        }
        finally
        {
            IsRestoring = false;
        }

        var (title, message) = RestorePrompt.Describe(result, RestoreContext.Chooser);
        switch (result.Outcome)
        {
            case RestoreOutcome.Restored:
                // The replaced database is kept beside it; say so, then open it.
                if (ShowMessageAsync is not null)
                    await ShowMessageAsync(title, message);
                ResolveWith(target);
                break;

            case RestoreOutcome.Created:
                ResolveWith(target);
                break;

            default:
                // Stay in the chooser. The destination may have changed (a file appeared, a lock was released).
                RestoreError = message;
                BeginRestoreDestinationProbe();
                break;
        }
    }

    /// <summary>The production restore operation: <see cref="CheckoutService.RestoreIntoAsync"/> on the app's checkout service.</summary>
    /// <param name="backup">The backup file.</param>
    /// <param name="target">The destination database.</param>
    /// <param name="allowReplace">Whether an existing database may be replaced.</param>
    private static Task<RestoreResult> DefaultRestoreOperation(string backup, string target, bool allowReplace)
        => App.Checkout.RestoreIntoAsync(backup, target, allowReplace);

    /// <summary>
    /// Resolves the chooser: the user picked (or restored to) <paramref name="path"/>, which
    /// <see cref="MainWindow"/> opens next.
    /// </summary>
    /// <param name="path">The database to open.</param>
    private void ResolveWith(string? path)
    {
        Outcome      = ChooserOutcome.Resolved;
        ResolvedPath = path;
        CloseRequested?.Invoke();
    }

    // ── Footer state ──────────────────────────────────────────────────────

    /// <summary>
    /// True when enough information is available to proceed.
    /// Bound to the Continue/Open button's IsEnabled.
    /// </summary>
    public bool CanContinue
    {
        get
        {
            // Nothing can be started while a restore is running.
            if (IsRestoring) return false;

            // A selected recent database is always openable
            if (SelectedRecentDatabase is not null) return true;

            return SelectedOption switch
            {
                ChooserOption.BrowseForIt       => BrowsedPath is not null,
                // The destination must have answered (replace or create) and must not be the backup itself.
                ChooserOption.RestoreFromBackup  => BackupPath is not null && RestorePath is not null
                                                    && RestoreDestinationState is RestoreDestinationState.Exists
                                                                               or RestoreDestinationState.Missing,
                ChooserOption.CreateNew          => true,
                _                                => false
            };
        }
    }

    /// <summary>Context-sensitive label for the Continue footer button.</summary>
    public string ContinueButtonText
    {
        get
        {
            if (SelectedRecentDatabase is not null) return "Open";

            return SelectedOption switch
            {
                ChooserOption.BrowseForIt       => "Open This Database",
                ChooserOption.RestoreFromBackup  => "Restore and Open",
                ChooserOption.CreateNew          => "Start Setup Wizard",
                _                                => "Open"
            };
        }
    }

    // ── Close signal ──────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the VM wants the window to hide.
    /// The window subscribes in code-behind and calls Hide() in response.
    /// </summary>
    public event Action? CloseRequested;

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Exits without resolving — app will shut down.</summary>
    [RelayCommand]
    private void Exit()
    {
        // Exiting mid-restore would abandon it and leave a stray lock file; the window disables this too.
        if (IsRestoring) return;

        Outcome = ChooserOutcome.None;
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Proceeds with the current selection — either a recent database or an action option.
    /// For recent databases, validates the path first (bounded ~5s) to avoid a 60s hang
    /// on unreachable shares. For Restore, restores the backup safely (see
    /// <see cref="RestoreFromBackupAndOpenAsync"/>) before signalling completion.
    /// </summary>
    [RelayCommand]
    private async Task Continue()
    {
        if (!CanContinue) return;

        // Recent database selected — validate before handing to checkout.
        if (SelectedRecentDatabase is not null)
        {
            RecentError = null;
            var validation = await DatabaseValidator.ValidateAsync(SelectedRecentDatabase.Path);
            if (validation != DatabaseValidationResult.Ok)
            {
                RecentError = DescribeRecentOpenFailure(validation);
                return;
            }

            ResolveWith(SelectedRecentDatabase.Path);
            return;
        }

        switch (SelectedOption)
        {
            case ChooserOption.BrowseForIt:
                ResolveWith(BrowsedPath);
                break;

            case ChooserOption.RestoreFromBackup:
                await RestoreFromBackupAndOpenAsync();
                break;

            case ChooserOption.CreateNew:
                Outcome = ChooserOutcome.StartWizard;
                CloseRequested?.Invoke();
                break;
        }
    }
}

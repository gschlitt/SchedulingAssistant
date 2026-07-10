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
    Unreachable
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

        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ContinueButtonText));
    }

    /// <summary>Populates the recent databases list from AppSettings.</summary>
    private void LoadRecentDatabases()
    {
        RecentDatabases.Clear();
        foreach (var path in AppSettings.Current.RecentDatabases)
        {
            // No existence probe here — see the RecentDatabases doc comment.
            RecentDatabases.Add(new RecentDatabaseItem
            {
                Path = path,
                DisplayName = Path.GetFileName(path)
            });
        }
        HasRecentDatabases = RecentDatabases.Count > 0;
    }

    /// <summary>Opens a recent database by double-click.</summary>
    [RelayCommand]
    private void OpenRecentDatabase(RecentDatabaseItem? item)
    {
        if (item is null) return;
        Outcome      = ChooserOutcome.Resolved;
        ResolvedPath = item.Path;
        CloseRequested?.Invoke();
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
                _                                    => "The selected file could not be found."
            };
        }
    }

    // ── Restore from backup (same as old Option B) ───────────────────────

    /// <summary>Path to the backup file chosen by the user.</summary>
    [ObservableProperty]
    private string? _backupPath;

    partial void OnBackupPathChanged(string? value) =>
        OnPropertyChanged(nameof(CanContinue));

    /// <summary>Suggested full path where the backup will be copied (shown in Step 2).</summary>
    [ObservableProperty]
    private string? _restorePath;

    partial void OnRestorePathChanged(string? value) =>
        OnPropertyChanged(nameof(CanContinue));

    /// <summary>Error message for restore, or null when everything is valid.</summary>
    [ObservableProperty]
    private string? _restoreError;

    /// <summary>Controls whether the Step 2 destination row is visible.</summary>
    [ObservableProperty]
    private bool _isBackupStepTwoVisible;

    /// <summary>
    /// Injected by the window code-behind. Opens an OS file picker for backup files.
    /// </summary>
    public Func<Task<string?>>? PickBackupFileAsync { get; set; }

    /// <summary>
    /// Injected by the window code-behind. Opens an OS folder picker for the
    /// restore destination.
    /// </summary>
    public Func<Task<string?>>? PickRestoreFolderAsync { get; set; }

    /// <summary>Opens the file picker, validates the backup, and reveals Step 2.</summary>
    [RelayCommand]
    private async Task BrowseForBackup()
    {
        if (PickBackupFileAsync is null) return;

        var path = await PickBackupFileAsync();
        if (path is null) return;

        var backupValidation = await DatabaseValidator.ValidateAsync(path);
        if (backupValidation == DatabaseValidationResult.Unreachable)
        {
            RestoreError = NetworkFileOps.UnreachableMessage;
            return;
        }
        if (backupValidation != DatabaseValidationResult.Ok)
        {
            RestoreError = "That file could not be opened as a valid database backup.";
            return;
        }

        BackupPath  = path;
        RestoreError = null;
        SuggestRestorePath();
        IsBackupStepTwoVisible = true;
    }

    /// <summary>Builds a sensible default destination path for the restore operation.</summary>
    private void SuggestRestorePath()
    {
        if (BackupPath is null) return;

        var originalName = Path.GetFileName(AppSettings.Current.DatabasePath)
                        ?? Path.GetFileName(BackupPath);
        var folder = Path.GetDirectoryName(BackupPath)!;
        RestorePath = Path.Combine(folder, originalName!);
    }

    /// <summary>Opens the folder picker and updates the restore destination.</summary>
    [RelayCommand]
    private async Task ChangeRestoreFolder()
    {
        if (PickRestoreFolderAsync is null) return;

        var folder = await PickRestoreFolderAsync();
        if (folder is null) return;

        var originalName = Path.GetFileName(AppSettings.Current.DatabasePath)
                        ?? Path.GetFileName(BackupPath!);
        RestorePath = Path.Combine(folder, originalName!);
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
            // A selected recent database is always openable
            if (SelectedRecentDatabase is not null) return true;

            return SelectedOption switch
            {
                ChooserOption.BrowseForIt       => BrowsedPath is not null,
                ChooserOption.RestoreFromBackup  => BackupPath is not null && RestorePath is not null,
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
        Outcome = ChooserOutcome.None;
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Proceeds with the current selection — either a recent database or an action option.
    /// For Restore, copies the backup file before signalling completion.
    /// </summary>
    [RelayCommand]
    private void Continue()
    {
        if (!CanContinue) return;

        // Recent database selected — open it directly
        if (SelectedRecentDatabase is not null)
        {
            Outcome      = ChooserOutcome.Resolved;
            ResolvedPath = SelectedRecentDatabase.Path;
            CloseRequested?.Invoke();
            return;
        }

        switch (SelectedOption)
        {
            case ChooserOption.BrowseForIt:
                Outcome      = ChooserOutcome.Resolved;
                ResolvedPath = BrowsedPath;
                CloseRequested?.Invoke();
                break;

            case ChooserOption.RestoreFromBackup:
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RestorePath!)!);
                    File.Copy(BackupPath!, RestorePath!, overwrite: true);
                    Outcome      = ChooserOutcome.Resolved;
                    ResolvedPath = RestorePath;
                    CloseRequested?.Invoke();
                }
                catch (Exception ex)
                {
                    RestoreError = $"Could not restore the backup: {ex.Message}";
                }
                break;

            case ChooserOption.CreateNew:
                Outcome = ChooserOutcome.StartWizard;
                CloseRequested?.Invoke();
                break;
        }
    }
}

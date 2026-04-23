using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Indicates why the database recovery window was shown.
/// </summary>
public enum RecoveryReason
{
    /// <summary>The database file could not be found at the path saved in settings.</summary>
    NotFound,

    /// <summary>The database file exists but failed its integrity check.</summary>
    Corrupt
}

/// <summary>
/// The result chosen by the user in the database recovery window.
/// </summary>
public enum RecoveryOutcome
{
    /// <summary>User exited without resolving — app should shut down.</summary>
    None,

    /// <summary>A valid database path was resolved (Option A or B).</summary>
    Resolved,

    /// <summary>User chose to start over — route to the setup wizard.</summary>
    StartWizard
}

/// <summary>
/// Which of the three recovery options the user has selected.
/// </summary>
public enum RecoveryOption
{
    None,
    BrowseForIt,
    RestoreFromBackup,
    StartFresh
}

/// <summary>
/// View model for the database recovery window. Shown when the configured database
/// is missing or corrupt at startup. Presents three options: browse for the file,
/// restore from a backup, or start the setup wizard from scratch.
/// </summary>
/// <remarks>
/// File-picker delegates (<see cref="PickDatabaseFileAsync"/>,
/// <see cref="PickBackupFileAsync"/>, <see cref="PickRestoreFolderAsync"/>) are
/// injected by the window code-behind so the VM has no reference to the Window.
/// The window subscribes to <see cref="CloseRequested"/> and closes itself when
/// the VM fires it.
/// </remarks>
public partial class DatabaseRecoveryViewModel : ObservableObject
{
    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the recovery view model.
    /// </summary>
    /// <param name="reason">Why recovery was triggered — controls the intro text.</param>
    /// <param name="lastKnownPath">The database path from settings, shown to the user for context.</param>
    public DatabaseRecoveryViewModel(RecoveryReason reason, string? lastKnownPath)
    {
        Reason        = reason;
        LastKnownPath = lastKnownPath ?? "(not set)";

        ProblemDescription = reason == RecoveryReason.NotFound
            ? "TermPoint could not find the database file at the location saved in your settings. " +
              "It may have been moved, renamed, or deleted."
            : "TermPoint found the database file but it failed an integrity check and may be damaged. " +
              "It is not safe to open this file.";
    }

    // ── Outcome (read by MainWindow after the window closes) ──────────────

    /// <summary>The reason this window was shown.</summary>
    public RecoveryReason Reason { get; }

    /// <summary>The last-known path from settings, displayed for the user's reference.</summary>
    public string LastKnownPath { get; }

    /// <summary>Localised description of the problem.</summary>
    public string ProblemDescription { get; }

    /// <summary>
    /// The outcome chosen by the user.
    /// Read by <see cref="MainWindow"/> after the window closes.
    /// </summary>
    public RecoveryOutcome Outcome { get; private set; } = RecoveryOutcome.None;

    /// <summary>
    /// The resolved database path.
    /// Non-null only when <see cref="Outcome"/> is <see cref="RecoveryOutcome.Resolved"/>.
    /// </summary>
    public string? ResolvedPath { get; private set; }

    // ── Option selection ──────────────────────────────────────────────────

    /// <summary>Which of the three options the user has currently selected.</summary>
    [ObservableProperty]
    private RecoveryOption _selectedOption = RecoveryOption.None;

    partial void OnSelectedOptionChanged(RecoveryOption value)
    {
        OnPropertyChanged(nameof(IsOptionASelected));
        OnPropertyChanged(nameof(IsOptionBSelected));
        OnPropertyChanged(nameof(IsOptionCSelected));
        OnPropertyChanged(nameof(IsOptionAExpanded));
        OnPropertyChanged(nameof(IsOptionBExpanded));
        OnPropertyChanged(nameof(IsOptionCExpanded));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ContinueButtonText));
    }

    /// <summary>True when Option A (Browse) is selected — used for radio-button binding.</summary>
    public bool IsOptionASelected => SelectedOption == RecoveryOption.BrowseForIt;

    /// <summary>True when Option B (Restore) is selected — used for radio-button binding.</summary>
    public bool IsOptionBSelected => SelectedOption == RecoveryOption.RestoreFromBackup;

    /// <summary>True when Option C (Start fresh) is selected — used for radio-button binding.</summary>
    public bool IsOptionCSelected => SelectedOption == RecoveryOption.StartFresh;

    /// <summary>Controls the visibility of the Option A detail panel.</summary>
    public bool IsOptionAExpanded => SelectedOption == RecoveryOption.BrowseForIt;

    /// <summary>Controls the visibility of the Option B detail panel.</summary>
    public bool IsOptionBExpanded => SelectedOption == RecoveryOption.RestoreFromBackup;

    /// <summary>Controls the visibility of the Option C detail panel.</summary>
    public bool IsOptionCExpanded => SelectedOption == RecoveryOption.StartFresh;

    /// <summary>Selects Option A.</summary>
    [RelayCommand]
    private void SelectOptionA() => SelectedOption = RecoveryOption.BrowseForIt;

    /// <summary>Selects Option B.</summary>
    [RelayCommand]
    private void SelectOptionB() => SelectedOption = RecoveryOption.RestoreFromBackup;

    /// <summary>Selects Option C.</summary>
    [RelayCommand]
    private void SelectOptionC() => SelectedOption = RecoveryOption.StartFresh;

    // ── Option A: Browse for the database file ────────────────────────────

    /// <summary>The path chosen via the file picker, after passing validation.</summary>
    [ObservableProperty]
    private string? _browsedPath;

    partial void OnBrowsedPathChanged(string? value) =>
        OnPropertyChanged(nameof(CanContinue));

    /// <summary>Validation error for Option A, or null when the chosen file is valid.</summary>
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

    // ── Option B: Restore from a backup ──────────────────────────────────

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

    /// <summary>Error message for Option B, or null when everything is valid.</summary>
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

        // Default: same folder as the backup, with the original database filename.
        // Falls back to the backup filename if no saved path exists.
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
    /// True when enough information is available to proceed with the selected option.
    /// Bound to the Continue button's <c>IsEnabled</c>.
    /// </summary>
    public bool CanContinue => SelectedOption switch
    {
        RecoveryOption.BrowseForIt       => BrowsedPath is not null,
        RecoveryOption.RestoreFromBackup => BackupPath is not null && RestorePath is not null,
        RecoveryOption.StartFresh        => true,
        _                                => false
    };

    /// <summary>Context-sensitive label for the Continue footer button.</summary>
    public string ContinueButtonText => SelectedOption switch
    {
        RecoveryOption.BrowseForIt       => "Open This Database",
        RecoveryOption.RestoreFromBackup => "Restore and Open",
        RecoveryOption.StartFresh        => "Start Setup Wizard",
        _                                => "Continue"
    };

    // ── Close signal ──────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the VM wants the window to close.
    /// The window subscribes in code-behind and calls <c>Close()</c> in response.
    /// </summary>
    public event Action? CloseRequested;

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Exits without resolving — app will shut down.</summary>
    [RelayCommand]
    private void Exit()
    {
        Outcome = RecoveryOutcome.None;
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Proceeds with whichever option is currently selected.
    /// For Option B, copies the backup file before signalling completion.
    /// </summary>
    [RelayCommand]
    private void Continue()
    {
        if (!CanContinue) return;

        switch (SelectedOption)
        {
            case RecoveryOption.BrowseForIt:
                Outcome      = RecoveryOutcome.Resolved;
                ResolvedPath = BrowsedPath;
                CloseRequested?.Invoke();
                break;

            case RecoveryOption.RestoreFromBackup:
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RestorePath!)!);
                    File.Copy(BackupPath!, RestorePath!, overwrite: true);
                    Outcome      = RecoveryOutcome.Resolved;
                    ResolvedPath = RestorePath;
                    CloseRequested?.Invoke();
                }
                catch (Exception ex)
                {
                    RestoreError = $"Could not restore the backup: {ex.Message}";
                }
                break;

            case RecoveryOption.StartFresh:
                Outcome = RecoveryOutcome.StartWizard;
                CloseRequested?.Invoke();
                break;
        }
    }
}

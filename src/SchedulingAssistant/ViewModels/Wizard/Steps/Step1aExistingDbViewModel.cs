using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 1a — asks whether the user already has a TermPoint database set up by a colleague.
///
/// "Yes" path: the user provides the existing DB file and a backup folder.
///             <see cref="ValidateExistingDb"/> opens the DB, saves paths, marks
///             <see cref="IsInitialSetupComplete"/> = true, and the wizard closes immediately.
/// "No" path:  the wizard continues to the normal institution / configuration flow.
/// </summary>
public partial class Step1aExistingDbViewModel : WizardStepViewModel
{
    private readonly Window _ownerWindow;

    public override string StepTitle => "Existing Database";

    // ── Choice ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(IsExistingDbChoice))]
    [NotifyPropertyChangedFor(nameof(IsNewSetupChoice))]
    private bool _hasExistingDb = false;

    /// <summary>Radio-button proxy — true when the user selects "I already have a database".</summary>
    public bool IsExistingDbChoice
    {
        get => _hasExistingDb;
        set { if (value) HasExistingDb = true; }
    }

    /// <summary>Radio-button proxy — true when the user selects the fresh-install path.</summary>
    public bool IsNewSetupChoice
    {
        get => !_hasExistingDb;
        set { if (value) HasExistingDb = false; }
    }

    // ── Paths ────────────────────────────────────────────────────────────────

    /// <summary>Full path to the existing .db file chosen by the user.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _dbPath = string.Empty;

    /// <summary>Backup folder path chosen by the user.</summary>
    [ObservableProperty]
    private string _backupFolder = string.Empty;

    /// <summary>Validation error set by the orchestrator on a failed open attempt.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // ── Advancement ──────────────────────────────────────────────────────────

    /// <summary>
    /// Always true on the new-setup path.
    /// On the existing-DB path, requires at least a DB file path.
    /// </summary>
    public override bool CanAdvance =>
        !HasExistingDb || !string.IsNullOrWhiteSpace(DbPath);

    // ── Constructor ──────────────────────────────────────────────────────────

    public Step1aExistingDbViewModel(Window ownerWindow) => _ownerWindow = ownerWindow;

    // ── Browse commands ──────────────────────────────────────────────────────

    /// <summary>Opens a file picker so the user can locate the existing .db file.</summary>
    [RelayCommand]
    private async Task BrowseDbFile()
    {
        var result = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Select TermPoint Database",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TermPoint Database") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] },
                new FilePickerFileType("All Files")          { Patterns = ["*.*"] }
            ]
        });

        if (result.Count > 0)
        {
            DbPath       = result[0].TryGetLocalPath() ?? string.Empty;
            ErrorMessage = string.Empty;
        }
    }

    /// <summary>Opens a folder picker so the user can choose their local backup location.</summary>
    [RelayCommand]
    private async Task BrowseBackupFolder()
    {
        var result = await _ownerWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = "Choose Backup Folder",
            AllowMultiple = false
        });

        if (result.Count > 0)
            BackupFolder = result[0].TryGetLocalPath() ?? string.Empty;
    }
}

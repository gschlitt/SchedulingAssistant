using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Services;

namespace TermPoint.ViewModels.Wizard.Steps;

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
    private readonly FolderAssessor _assessor;

    public override string StepTitle => "Existing Database";

    /// <summary>
    /// Advisory warnings for the folder containing the selected database file.
    /// Non-blocking — the user can proceed despite warnings (the DB already exists there).
    /// </summary>
    public ObservableCollection<FolderWarning> DbFolderWarnings { get; } = new();

    /// <summary>
    /// Advisory warnings for the chosen backup folder.
    /// </summary>
    public ObservableCollection<FolderWarning> BackupFolderWarnings { get; } = new();

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

    /// <summary>
    /// When true, the user wants to join this shared database as a read-only observer.
    /// Persisted to <c>AppSettings.OpenInReaderMode</c> by the orchestrator so the
    /// real checkout opens read-only without ever acquiring the write lock. Never gates Next.
    /// </summary>
    [ObservableProperty]
    private bool _openInReaderMode = false;

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

    public Step1aExistingDbViewModel(Window ownerWindow)
        : this(ownerWindow, FolderAssessor.CreateForCurrentMachine()) { }

    internal Step1aExistingDbViewModel(Window ownerWindow, FolderAssessor assessor)
    {
        _ownerWindow = ownerWindow;
        _assessor    = assessor;
    }

    /// <summary>
    /// Assesses the folder of the chosen database file whenever <see cref="DbPath"/> changes.
    /// Fire-and-forget: the assessment probes the filesystem, which can block on an
    /// unreachable network path, so it runs deadline-bounded off the UI thread
    /// (<see cref="FolderAssessor.AssessAsync"/>) with a latest-value guard.
    /// </summary>
    partial void OnDbPathChanged(string value) => _ = AssessDbPathAsync(value);

    private async Task AssessDbPathAsync(string value)
    {
        DbFolderWarnings.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;

        var folder = Path.GetDirectoryName(value);
        if (string.IsNullOrEmpty(folder)) return;

        var assessment = await _assessor.AssessAsync(folder);
        if (value != DbPath) return; // superseded by a newer edit while probing

        DbFolderWarnings.Clear();
        foreach (var w in assessment.Warnings)
        {
            // For existing DBs, don't warn about writability — the DB is already there,
            // and the user may intentionally open it read-only.
            if (w.Kind == WarningKind.NotWritable) continue;
            DbFolderWarnings.Add(w);
        }
    }

    // ── Browse commands ──────────────────────────────────────────────────────

    /// <summary>
    /// Assesses the backup folder whenever <see cref="BackupFolder"/> changes.
    /// Fire-and-forget for the same reason as <see cref="OnDbPathChanged"/>.
    /// </summary>
    partial void OnBackupFolderChanged(string value) => _ = AssessBackupFolderAsync(value);

    private async Task AssessBackupFolderAsync(string value)
    {
        BackupFolderWarnings.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;

        var assessment = await _assessor.AssessAsync(value);
        if (value != BackupFolder) return; // superseded by a newer edit while probing

        BackupFolderWarnings.Clear();
        foreach (var w in assessment.Warnings)
        {
            // NotWritable is expected for folders that don't exist yet — they'll be
            // created later. Only warn if the folder already exists.
            if (w.Kind == WarningKind.NotWritable && !assessment.FolderExists)
                continue;
            BackupFolderWarnings.Add(w);
        }
    }

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

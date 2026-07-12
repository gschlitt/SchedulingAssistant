using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Services;

namespace TermPoint.ViewModels.Wizard.Steps;

/// <summary>
/// Step 2 — choose the database folder, confirm/edit the database filename, and choose the backup folder.
/// The database filename is pre-seeded from the academic unit abbreviation and is editable by the user.
/// Advancing creates the database file and sets IsInitialSetupComplete = true.
///
/// On construction, <see cref="FolderAssessor"/> surveys the machine for suitable database
/// locations and presents them as clickable suggestions. When the user picks a folder (via
/// suggestion or Browse), the folder is assessed and any warnings (CFA-protected, cloud-synced)
/// are shown inline as advisories.
/// </summary>
public partial class Step2DatabaseViewModel : WizardStepViewModel
{
    private readonly Window _ownerWindow;
    private readonly FolderAssessor _assessor;

    public override string StepTitle => "Database Location";

    /// <summary>Pre-assessed folder suggestions, populated at construction.</summary>
    public ObservableCollection<FolderSuggestion> SuggestedFolders { get; } = new();

    /// <summary>
    /// Advisory warnings for the currently chosen database folder. Non-blocking — the user
    /// can proceed despite warnings. Empty when the folder has no issues.
    /// </summary>
    public ObservableCollection<FolderWarning> DbFolderWarnings { get; } = new();

    /// <summary>
    /// Advisory warnings for the currently chosen backup folder. Same checks as the
    /// database folder — CFA, cloud sync, writability.
    /// </summary>
    public ObservableCollection<FolderWarning> BackupFolderWarnings { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(DbFullPath))]
    [NotifyPropertyChangedFor(nameof(SameFolderWarning))]
    [NotifyPropertyChangedFor(nameof(IsFilenameReady))]
    private string _dbFolder = string.Empty;

    /// <summary>
    /// The editable database filename (e.g. "CS-TT.db").
    /// Pre-seeded from the academic unit abbreviation; the user can change it before advancing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(DbFullPath))]
    [NotifyPropertyChangedFor(nameof(DbFilenameError))]
    [NotifyPropertyChangedFor(nameof(IsFilenameReady))]
    private string _dbFilename = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(SameFolderWarning))]
    private string _backupFolder = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public override bool CanAdvance =>
        !string.IsNullOrWhiteSpace(DbFolder) &&
        !string.IsNullOrWhiteSpace(BackupFolder) &&
        DbFilenameError is null;

    /// <summary>
    /// True when the database folder is set and the filename is valid.
    /// Controls visibility of the backup-folder section so the user sees it only
    /// after they have confirmed the filename — creating a natural top-to-bottom flow.
    /// </summary>
    public bool IsFilenameReady =>
        !string.IsNullOrWhiteSpace(DbFolder) && DbFilenameError is null;

    /// <summary>
    /// Validation error for the filename field, or null when the filename is acceptable.
    /// A filename is invalid if it is blank, contains path-separator characters, or does not
    /// end with the <c>.db</c> extension.
    /// </summary>
    public string? DbFilenameError
    {
        get
        {
            var name = DbFilename.Trim();
            if (string.IsNullOrEmpty(name))
                return "Filename cannot be blank.";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "Filename contains invalid characters.";
            if (!name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                return "Filename must end in .db.";
            return null;
        }
    }

    /// <summary>
    /// When the filename is edited and has no extension at all, silently append <c>.db</c>
    /// so the user does not have to type it explicitly.  If the user has started typing an
    /// extension (e.g. "myfile.t") the value is left unchanged and <see cref="DbFilenameError"/>
    /// will display the validation message.
    /// </summary>
    partial void OnDbFilenameChanged(string value)
    {
        var trimmed = value.Trim();
        if (!string.IsNullOrEmpty(trimmed) && string.IsNullOrEmpty(Path.GetExtension(trimmed)))
            DbFilename = trimmed + ".db";
    }

    /// <summary>
    /// True when the backup folder resolves to the same path as the database folder.
    /// Used to display a recommendation warning in the view; does not block advancement.
    /// </summary>
    public bool SameFolderWarning
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DbFolder) || string.IsNullOrWhiteSpace(BackupFolder))
                return false;
            try
            {
                var db  = Path.GetFullPath(DbFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var bak = Path.GetFullPath(BackupFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(db, bak, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[Step2DatabaseViewModel] SameFolderWarning path comparison failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>The full database path (folder + filename) shown to the user as a read-only confirmation.</summary>
    public string DbFullPath => string.IsNullOrWhiteSpace(DbFolder) || string.IsNullOrWhiteSpace(DbFilename.Trim())
        ? string.Empty
        : Path.Combine(DbFolder, DbFilename.Trim());

    public Step2DatabaseViewModel(string acUnitAbbrev, Window ownerWindow)
        : this(acUnitAbbrev, ownerWindow, FolderAssessor.CreateForCurrentMachine()) { }

    /// <summary>
    /// Testable constructor that accepts an explicit <see cref="FolderAssessor"/>.
    /// </summary>
    internal Step2DatabaseViewModel(string acUnitAbbrev, Window ownerWindow, FolderAssessor assessor)
    {
        _ownerWindow = ownerWindow;
        _assessor    = assessor;
        _dbFilename  = BuildDbFilename(acUnitAbbrev);

        _ = LoadSuggestionsAsync(acUnitAbbrev);
    }

    /// <summary>
    /// Populates <see cref="SuggestedFolders"/> on a background thread. Fire-and-forget
    /// from the constructor — any failure results in an empty suggestion list (Browse
    /// still works). Marshals results back to the UI thread via the collection.
    /// </summary>
    private async Task LoadSuggestionsAsync(string? institutionAbbrev)
    {
        try
        {
            await foreach (var s in _assessor.SuggestLocationsAsync(institutionAbbrev))
                SuggestedFolders.Add(s);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[Step2DatabaseViewModel] Suggestion loading failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs folder assessment whenever <see cref="DbFolder"/> changes (user Browse or suggestion click).
    /// Populates <see cref="DbFolderWarnings"/> with any advisories.
    /// Fire-and-forget: the assessment probes the filesystem, which can block on an
    /// unreachable network path, so it runs deadline-bounded off the UI thread
    /// (<see cref="FolderAssessor.AssessAsync"/>) with a latest-value guard.
    /// </summary>
    partial void OnDbFolderChanged(string value) => _ = AssessDbFolderAsync(value);

    private async Task AssessDbFolderAsync(string value)
    {
        DbFolderWarnings.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;

        var assessment = await _assessor.AssessAsync(value);
        if (value != DbFolder) return; // superseded by a newer edit while probing

        DbFolderWarnings.Clear();
        foreach (var w in assessment.Warnings)
        {
            // NotWritable is expected for folders that don't exist yet — they'll be
            // created at commit time. Only warn if the folder already exists.
            if (w.Kind == WarningKind.NotWritable && !assessment.FolderExists)
                continue;
            DbFolderWarnings.Add(w);
        }
    }

    /// <summary>
    /// Runs folder assessment whenever <see cref="BackupFolder"/> changes.
    /// Populates <see cref="BackupFolderWarnings"/> with any advisories.
    /// Fire-and-forget for the same reason as <see cref="OnDbFolderChanged"/>.
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
            if (w.Kind == WarningKind.NotWritable && !assessment.FolderExists)
                continue;
            BackupFolderWarnings.Add(w);
        }
    }

    /// <summary>Selects a suggested folder as the database folder.</summary>
    [RelayCommand]
    private void SelectSuggestion(FolderSuggestion suggestion)
    {
        DbFolder = suggestion.Path;
    }

    /// <summary>
    /// Derives the suggested database filename from the academic unit abbreviation.
    /// Falls back to "data-TT.db" when the abbreviation is blank.
    /// </summary>
    public static string BuildDbFilename(string acUnitAbbrev)
    {
        var stem = string.IsNullOrWhiteSpace(acUnitAbbrev) ? "data" : acUnitAbbrev.Trim();
        return $"{stem}-TT.db";
    }

    [RelayCommand]
    private async Task BrowseDbFolder()
    {
        var folder = await PickFolder("Choose Database Folder");
        if (folder is not null)
            DbFolder = folder;
    }

    [RelayCommand]
    private async Task BrowseBackupFolder()
    {
        var folder = await PickFolder("Choose Backup Folder");
        if (folder is not null)
            BackupFolder = folder;
    }

    private async Task<string?> PickFolder(string title)
    {
        var result = await _ownerWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}

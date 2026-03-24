using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 2 — choose the database folder, confirm/edit the database filename, and choose the backup folder.
/// The database filename is pre-seeded from the academic unit abbreviation and is editable by the user.
/// Advancing creates the database file and sets IsInitialSetupComplete = true.
/// </summary>
public partial class Step2DatabaseViewModel : WizardStepViewModel
{
    private readonly Window _ownerWindow;

    public override string StepTitle => "Database Location";

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
    /// A filename is invalid if it is blank or contains path-separator characters.
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
            return null;
        }
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
            catch { return false; }
        }
    }

    /// <summary>The full database path (folder + filename) shown to the user as a read-only confirmation.</summary>
    public string DbFullPath => string.IsNullOrWhiteSpace(DbFolder) || string.IsNullOrWhiteSpace(DbFilename.Trim())
        ? string.Empty
        : Path.Combine(DbFolder, DbFilename.Trim());

    public Step2DatabaseViewModel(string acUnitAbbrev, Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
        _dbFilename  = BuildDbFilename(acUnitAbbrev);
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

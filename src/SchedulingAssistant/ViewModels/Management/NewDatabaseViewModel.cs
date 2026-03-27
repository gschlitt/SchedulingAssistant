using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Text.RegularExpressions;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the File → New flyout.
///
/// Collects the new database name, location, and backup folder from the user,
/// and optionally transfers the current database's scheduling configuration
/// (campuses, section prefixes, block patterns, block lengths / legal start times,
/// and semester templates from the currently selected academic year) into the new DB.
///
/// The config-transfer path works by snapshotting the current DB before switching,
/// then applying the snapshot to the freshly created DB using the new DI container.
/// No intermediate file is written — the .tpconfig pipeline is used in-memory.
///
/// Callers must set <see cref="PickFolderAsync"/> and <see cref="SwitchDatabaseAsync"/>
/// before the Create command is invoked.
/// </summary>
public partial class NewDatabaseViewModel : ViewModelBase
{
    private readonly ShareViewModel  _shareVm;         // reuses SnapshotConfig
    private readonly SemesterContext _semesterContext;

    // Matches a bare 4-digit year such as "2024".
    private static readonly Regex _yearPattern = new(@"^\d{4}$", RegexOptions.Compiled);

    // ── Callbacks set by MainWindowViewModel ─────────────────────────────────

    /// <summary>
    /// Opens a system folder-picker dialog. Returns the chosen path, or null if cancelled.
    /// Must be set before <see cref="CreateDatabaseCommand"/> is executed.
    /// </summary>
    public Func<string, Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>
    /// Switches the application to a different (possibly new) database file.
    /// Must be set before <see cref="CreateDatabaseCommand"/> is executed.
    /// </summary>
    public Func<string, Task>? SwitchDatabaseAsync { get; set; }

    // ── User inputs ───────────────────────────────────────────────────────────

    /// <summary>The database name entered by the user (becomes the filename stem).</summary>
    [ObservableProperty] private string _dbName = string.Empty;

    /// <summary>The folder in which the new database file will be created.</summary>
    [ObservableProperty] private string _dbFolder = string.Empty;

    /// <summary>The folder for automated database backups.</summary>
    [ObservableProperty] private string _backupFolder = string.Empty;

    /// <summary>
    /// When true, the current database's configuration is copied into the new DB.
    /// Reveals <see cref="FirstAcademicYearName"/> and the config-source notice.
    /// </summary>
    [ObservableProperty] private bool _transferConfig;

    /// <summary>
    /// The name for the first academic year to create in the new DB when transferring
    /// configuration. Only used when <see cref="TransferConfig"/> is true.
    /// </summary>
    [ObservableProperty] private string _firstAcademicYearName = string.Empty;

    /// <summary>True while the Create operation is running.</summary>
    [ObservableProperty] private bool _isCreating;

    /// <summary>Inline validation or error message; empty string when all inputs are valid.</summary>
    [ObservableProperty] private string _errorMessage = string.Empty;

    // ── Computed display properties ───────────────────────────────────────────

    /// <summary>
    /// Full path preview shown to the user (e.g. "C:\Data\CS-TT.db").
    /// Empty when name or folder is not yet set.
    /// </summary>
    public string DbFullPath
    {
        get
        {
            var stem = DbName.Trim();
            if (string.IsNullOrEmpty(stem) || string.IsNullOrEmpty(DbFolder)) return string.Empty;
            return Path.Combine(DbFolder, stem + "-TT.db");
        }
    }

    /// <summary>
    /// True when the backup folder resolves to the same directory as the database folder.
    /// Displayed as a non-blocking advisory warning.
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

    /// <summary>
    /// Describes the academic year situation shown inside the Transfer Configuration panel.
    /// When an AY is selected, names it and explains it will be recreated.
    /// When no AY is selected, prompts the user to enter one.
    /// </summary>
    public string ConfigSourceLabel =>
        _semesterContext.SelectedAcademicYear is { } ay
            ? $"The academic year \"{ay.Name}\" and its semester configuration will be recreated in the new database."
            : "No academic year is currently selected. Campuses, section prefixes, and block patterns will still be transferred. Enter the starting academic year for your new database below.";

    /// <summary>True when the currently selected academic year is available for transfer.</summary>
    public bool HasConfigSource => _semesterContext.SelectedAcademicYear is not null;

    /// <summary>
    /// True when the user must manually supply a starting academic year name — i.e. transfer is
    /// requested but there is no source AY whose name can be carried over automatically.
    /// </summary>
    public bool ShowFirstAcademicYearField => TransferConfig && !HasConfigSource;

    /// <summary>
    /// Validation message for <see cref="FirstAcademicYearName"/>.
    /// Null when the field is empty (no error yet) or when the value is acceptable.
    /// Mirrors the identical validation logic in <c>Step5AcademicYearViewModel</c>.
    /// </summary>
    public string? FirstAcademicYearNameError
    {
        get
        {
            var trimmed = FirstAcademicYearName.Trim();
            if (trimmed.Length == 0)
                return null; // no message while the field is still empty

            // Accept a bare 4-digit year (expands on commit) or the full "YYYY-YYYY+1" form.
            if (!_yearPattern.IsMatch(trimmed) && !(trimmed.Length == 9 && trimmed[4] == '-'))
                return "Enter the start year (e.g. 2025) and it will expand automatically.";

            if (trimmed.Length == 9)
            {
                if (!int.TryParse(trimmed[..4], out var y1) || !int.TryParse(trimmed[5..], out var y2))
                    return "Use the format YYYY-YYYY (e.g. 2025-2026).";
                if (y2 != y1 + 1)
                    return $"The second year must be {y1 + 1}.";
            }

            return null;
        }
    }

    /// <summary>True when all required inputs are valid and the Create button may be clicked.</summary>
    public bool CanCreate
    {
        get
        {
            if (IsCreating) return false;
            if (string.IsNullOrWhiteSpace(DbName.Trim())) return false;
            if (DbName.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (string.IsNullOrWhiteSpace(DbFolder)) return false;
            if (string.IsNullOrWhiteSpace(BackupFolder)) return false;
            if (TransferConfig && !HasConfigSource)
            {
                // No source AY — user must supply a valid starting academic year name.
                if (string.IsNullOrWhiteSpace(FirstAcademicYearName.Trim())) return false;
                if (FirstAcademicYearNameError is not null) return false;
            }
            return true;
        }
    }

    // ── Property-change notifications for computed properties ─────────────────
    // [NotifyPropertyChangedFor] cannot target manually-implemented properties,
    // so we propagate changes via the generated partial callbacks instead.

    partial void OnDbNameChanged(string value)
    {
        OnPropertyChanged(nameof(DbFullPath));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnDbFolderChanged(string value)
    {
        OnPropertyChanged(nameof(DbFullPath));
        OnPropertyChanged(nameof(SameFolderWarning));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnBackupFolderChanged(string value)
    {
        OnPropertyChanged(nameof(SameFolderWarning));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnTransferConfigChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(ShowFirstAcademicYearField));
    }
    partial void OnFirstAcademicYearNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(FirstAcademicYearNameError));
    }
    partial void OnIsCreatingChanged(bool value)         => OnPropertyChanged(nameof(CanCreate));

    public NewDatabaseViewModel(ShareViewModel shareVm, SemesterContext semesterContext)
    {
        _shareVm         = shareVm;
        _semesterContext = semesterContext;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Expands a bare 4-digit start year to the canonical "YYYY-YYYY+1" form.
    /// Leaves the value unchanged if it is already in the full form or is unrecognised.
    /// </summary>
    /// <param name="raw">The raw text entered by the user.</param>
    /// <returns>The expanded or unchanged name, trimmed of whitespace.</returns>
    private static string ExpandAyName(string raw)
    {
        var trimmed = raw.Trim();
        if (_yearPattern.IsMatch(trimmed))
        {
            var y1 = int.Parse(trimmed);
            return $"{y1}-{y1 + 1}";
        }
        return trimmed;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Opens a folder-picker to choose the database location.</summary>
    [RelayCommand]
    private async Task BrowseDbFolder()
    {
        var path = await PickFolderAsync!("Choose Database Folder");
        if (path is not null) DbFolder = path;
    }

    /// <summary>
    /// Called when the First Academic Year Name TextBox loses focus.
    /// Expands a bare 4-digit year entry (e.g. "2025" → "2025-2026") in place.
    /// </summary>
    [RelayCommand]
    private void CommitFirstAcademicYearName() => FirstAcademicYearName = ExpandAyName(FirstAcademicYearName);

    /// <summary>Opens a folder-picker to choose the backup location.</summary>
    [RelayCommand]
    private async Task BrowseBackupFolder()
    {
        var path = await PickFolderAsync!("Choose Backup Folder");
        if (path is not null) BackupFolder = path;
    }

    /// <summary>
    /// Creates the new database, optionally transfers configuration from the current DB,
    /// and switches the application to the new database.
    ///
    /// Sequence:
    ///   1. Snapshot current config (before switch, while current repos are valid).
    ///   2. Update BackupFolderPath in AppSettings (SwitchDatabaseAsync will persist it).
    ///   3. Switch to the new database path (reinitialises DI; current flyout disappears).
    ///   4. Apply the config snapshot to the new DB using fresh repos from App.Services.
    /// </summary>
    [RelayCommand]
    private async Task CreateDatabase()
    {
        if (!CanCreate) return;

        IsCreating    = true;
        ErrorMessage  = string.Empty;

        var newDbPath = DbFullPath;
        if (string.IsNullOrEmpty(newDbPath))
        {
            ErrorMessage = "Could not determine the database path.";
            IsCreating   = false;
            return;
        }

        try
        {
            // ── Step 1: snapshot config from current DB ───────────────────────
            // When a source AY is selected, its name is carried into the new DB.
            // When no AY is selected (edge case), institution-wide settings are still
            // transferred; the user-supplied FirstAcademicYearName is used instead.
            TpConfigData? config = null;
            string ayName = string.Empty;
            if (TransferConfig)
            {
                var sourceAy  = _semesterContext.SelectedAcademicYear;
                var sourceAyId = sourceAy?.Id ?? string.Empty;
                config = _shareVm.SnapshotConfig(sourceAyId);
                ayName = sourceAy?.Name ?? ExpandAyName(FirstAcademicYearName.Trim());
            }

            // ── Step 2: persist backup folder (SwitchDatabaseAsync will save) ─
            AppSettings.Current.BackupFolderPath = BackupFolder.Trim();

            // ── Step 3: switch to the new DB ──────────────────────────────────
            // DatabaseContext creates the file on first connection.
            // After this call, App.Services points to the new DB, and the main
            // window DataContext has been replaced — the flyout is gone.
            await SwitchDatabaseAsync!(newDbPath);

            // ── Step 4: apply config to the new DB ────────────────────────────
            if (config is not null)
                ApplyConfig(config, ayName);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "NewDatabaseViewModel.CreateDatabase failed");
            ErrorMessage = $"An error occurred: {ex.Message}";
            IsCreating   = false;
        }
        // IsCreating is intentionally not reset to false on success — the flyout
        // has already disappeared after the DataContext switch.
    }

    // ── Config application ────────────────────────────────────────────────────

    /// <summary>
    /// Inserts the snapshotted configuration into the newly created database.
    /// Uses <see cref="App.Services"/> directly so it gets repos bound to the new
    /// DB connection (the constructor-injected repos point to the old DB).
    /// </summary>
    /// <param name="config">The configuration snapshot from the source database.</param>
    /// <param name="ayName">Name for the first academic year to create (e.g. "2025-2026").</param>
    private void ApplyConfig(TpConfigData config, string ayName)
    {
        var campusRepo  = App.Services.GetRequiredService<ICampusRepository>();
        var prefixRepo  = App.Services.GetRequiredService<ISectionPrefixRepository>();
        var patternRepo = App.Services.GetRequiredService<IBlockPatternRepository>();
        var ayRepo      = App.Services.GetRequiredService<IAcademicYearRepository>();
        var semRepo     = App.Services.GetRequiredService<ISemesterRepository>();
        var db          = App.Services.GetRequiredService<IDatabaseContext>();
        var semCtx      = App.Services.GetRequiredService<SemesterContext>();

        // ── Campuses ─────────────────────────────────────────────────────────
        var campusNameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int campusSortOrder = 0;
        foreach (var name in config.Campuses)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var campus = new Campus { Name = name.Trim(), SortOrder = campusSortOrder++ };
            campusRepo.Insert(campus);
            campusNameToId[campus.Name] = campus.Id;
        }

        // ── Section prefixes ─────────────────────────────────────────────────
        foreach (var spDef in config.SectionPrefixes)
        {
            if (string.IsNullOrWhiteSpace(spDef.Prefix)) continue;
            var prefix = new SectionPrefix
            {
                Prefix   = spDef.Prefix.Trim(),
                CampusId = spDef.CampusName is not null
                           && campusNameToId.TryGetValue(spDef.CampusName, out var cid) ? cid : null
            };
            prefixRepo.Insert(prefix);
        }

        // ── Block patterns ────────────────────────────────────────────────────
        foreach (var bp in config.BlockPatterns)
        {
            if (string.IsNullOrWhiteSpace(bp.Name)) continue;
            patternRepo.Insert(new BlockPattern { Name = bp.Name, Days = bp.Days });
        }

        // ── First academic year ───────────────────────────────────────────────
        var ay = new AcademicYear { Name = ayName };
        ayRepo.Insert(ay);

        // ── Legal start times (block lengths) ────────────────────────────────
        if (config.BlockLengths.Count > 0)
        {
            var seedData = config.BlockLengths
                .Select(bl => (bl.Hours, bl.StartTimes))
                .ToList();
            SeedData.SeedWizardLegalStartTimes(db.Connection, ay.Id, seedData);
        }
        else
        {
            SeedData.SeedDefaultLegalStartTimes(db.Connection, ay.Id);
        }

        // ── Semesters ─────────────────────────────────────────────────────────
        int sortOrder = 0;
        foreach (var def in config.SemesterDefs)
        {
            if (string.IsNullOrWhiteSpace(def.Name)) continue;
            semRepo.Insert(new Semester
            {
                AcademicYearId = ay.Id,
                Name           = def.Name.Trim(),
                Color          = def.Color,
                SortOrder      = sortOrder++
            });
        }

        // Reload the semester context so the top-bar dropdowns populate immediately.
        semCtx.Reload(ayRepo, semRepo);
    }
}

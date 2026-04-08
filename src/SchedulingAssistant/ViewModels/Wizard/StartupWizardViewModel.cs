using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Wizard.Steps;

namespace SchedulingAssistant.ViewModels.Wizard;

/// <summary>
/// Orchestrates the multi-step startup wizard.
///
/// Step index map:
///   0  — Welcome
///   1  — License Agreement                    ← new
///   2  — Existing-DB check (Step1a)           (was 1)
///   3  — Institution                          (was 2)
///   4  — Database location + filename         (was 3)
///   5  — TpConfig import / ExitNow            (was 4)
///   6  — Campuses                             (was 5)
///   7  — Legal start times                    (was 6)
///   8  — Block patterns                       (was 7)
///   9  — Section prefixes                     (was 8)
///  10  — Academic year                        (was 9)
///  11  — Semester colors                      (was 10)
///  12  — Closing panel                        (was 11)
///
/// Routing variants:
///   Existing-DB path  (step 2, Yes):  steps 0–2 → finish immediately
///   ExitNow path      (step 5):       steps 0–5 → finish
///   Import path       (step 5):       skips 6–9 and 11 → 10 → 12
///   Manual path:                      all steps → 12
///
/// IsInitialSetupComplete is set to true only inside <see cref="FinishAsync"/>.
/// The window closes when <see cref="IsComplete"/> becomes true.
/// If closed before that, the app shuts down.
/// </summary>
public partial class StartupWizardViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly WizardServices _services;

    // ── Step tracking ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private WizardStepViewModel _currentStep = null!;

    private int _stepIndex = -1;

    /// <summary>True when the wizard has been completed successfully. The window should close.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>True when there is a previous step to return to.</summary>
    public bool CanGoBack => _stepIndex > 0;

    /// <summary>True on the welcome step — allows the user to exit before committing to anything.</summary>
    public bool CanCancel => _stepIndex == 0;

    /// <summary>Label for the Next/Finish button.</summary>
    public string NextButtonText => IsLastStep() ? "Finish" : "Next";

    // ── Shared state flowing across steps ────────────────────────────────────

    private string _institutionName   = string.Empty;
    private string _institutionAbbrev = string.Empty;
    private string _acUnitName        = string.Empty;
    private string _acUnitAbbrev      = string.Empty;
    private string _dbFolder          = string.Empty;
    private string _backupFolder      = string.Empty;

    /// <summary>True when the user connected to an existing DB at step 1a.</summary>
    private bool _isExistingDbPath;

    /// <summary>True when the import path was chosen in step 4 and a .tpconfig was loaded.</summary>
    private bool _isImportPath;

    // ── Step VM cache (prevent rebuild on Back) ──────────────────────────────

    private readonly Dictionary<int, WizardStepViewModel> _stepCache = new();

    /// <param name="window">The wizard window; used for file-picker dialogs and to close on completion.</param>
    /// <param name="services">Lazily-resolved dependencies; see <see cref="WizardServices"/>.</param>
    public StartupWizardViewModel(Window window, WizardServices services)
    {
        _window   = window;
        _services = services;
        NavigateTo(0);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Next()
    {
        if (!await ValidateCurrentStep()) return;
        await CommitCurrentStep();

        // Existing-DB path finishes immediately after step 2
        if (_isExistingDbPath)
        {
            await FinishAsync();
            return;
        }

        var next = _stepIndex + 1;

        // Import path skips manual-config steps (6–9) and the colors step (11)
        if (_isImportPath && next >= 6 && next <= 9) next = 10;
        if (_isImportPath && next == 11)              next = 12;

        if (IsLastStep())
            await FinishAsync();
        else
            NavigateTo(next);
    }

    /// <summary>Closes the wizard window (and consequently shuts down the app) without completing setup.</summary>
    [RelayCommand]
    private void Cancel() => _window?.Close();

    [RelayCommand]
    private void Back()
    {
        var prev = _stepIndex - 1;

        // Import path: stepping back skips manual-config steps (6–9) and colors (11)
        if (_isImportPath && prev >= 6 && prev <= 9) prev = 5;
        if (_isImportPath && prev == 11)              prev = 10;

        if (prev >= 0)
            NavigateTo(prev);
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void NavigateTo(int index)
    {
        if (!_stepCache.TryGetValue(index, out var vm))
        {
            vm = BuildStep(index);
            _stepCache[index] = vm;
        }

        _stepIndex  = index;
        CurrentStep = vm;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(NextButtonText));
    }

    private WizardStepViewModel BuildStep(int index) => index switch
    {
        0  => new Step0WelcomeViewModel(),
        1  => new StepLicenseViewModel(),                           // license agreement
        2  => BuildStep2a(),                                        // existing-DB check
        3  => new Step1InstitutionViewModel(),
        4  => new Step2DatabaseViewModel(_acUnitAbbrev, _window),
        5  => BuildStep5(),                                         // TpConfig / ExitNow
        // Manual-path config steps (6–9):
        6  => new Step4CampusesViewModel(_services.CampusListVm()),
        7  => new Step5SchedulingViewModel(),
        8  => new Step6BlockPatternsViewModel(_services.BlockPatternListVm()),
        9  => new Step7SectionPrefixesViewModel(_services.SectionPrefixListVm()),
        // Academic year + colors (10–11):
        10 => BuildStep10(),
        11 => BuildStep11(),
        // Closing panel (both normal paths):
        12 => new Step10ClosingViewModel(),
        _  => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>
    /// Builds the existing-DB check VM (step 2) and wires a PropertyChanged handler so that the
    /// Next/Finish button label updates immediately when the choice changes.
    /// </summary>
    private Step1aExistingDbViewModel BuildStep2a()
    {
        var vm = new Step1aExistingDbViewModel(_window);
        vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NextButtonText));
        return vm;
    }

    /// <summary>
    /// Builds the TpConfig VM (step 5) and wires a PropertyChanged handler so that the
    /// Next/Finish button label updates immediately when ExitNow is toggled.
    /// </summary>
    private Step3TpConfigViewModel BuildStep5()
    {
        var vm = new Step3TpConfigViewModel(_window);
        vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NextButtonText));
        return vm;
    }

    /// <summary>Builds the Academic Year step (step 10), pre-populating semesters on the import path.</summary>
    private Step5AcademicYearViewModel BuildStep10()
    {
        var vm = new Step5AcademicYearViewModel();

        if (_isImportPath && _stepCache.TryGetValue(5, out var s5vm) && s5vm is Step3TpConfigViewModel s5)
        {
            if (s5.ImportedConfig?.SemesterDefs is { Count: > 0 } defs)
                vm.LoadFromConfig(defs);
        }

        return vm;
    }

    /// <summary>Builds the Semester Colors step (step 11), seeding rows from the Academic Year step.</summary>
    private Step6SemesterColorsViewModel BuildStep11()
    {
        var vm = new Step6SemesterColorsViewModel();
        if (_stepCache.TryGetValue(10, out var s10vm) && s10vm is Step5AcademicYearViewModel s10)
            vm.LoadFromSemesters(s10.Semesters);
        return vm;
    }

    private bool IsLastStep()
    {
        // Existing-DB path: wizard finishes immediately after step 2
        if (_stepIndex == 2 && CurrentStep is Step1aExistingDbViewModel { IsExistingDbChoice: true })
            return true;
        // ExitNow at step 5 (TpConfig)
        if (_stepIndex == 5 && CurrentStep is Step3TpConfigViewModel { IsExitNowChoice: true })
            return true;
        // Both normal paths converge on the closing panel
        return _stepIndex == 12;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs step-specific validation. Returns true when the user may advance.
    /// </summary>
    private async Task<bool> ValidateCurrentStep()
    {
        if (!CurrentStep.CanAdvance)
            return false;

        if (_stepIndex == 2)
            return await ValidateStep1a();

        if (_stepIndex == 4)
            return await ValidateStep3();

        if (_stepIndex == 5 && CurrentStep is Step3TpConfigViewModel s4)
        {
            if (!s4.ValidateAndImport()) return false;
            _isImportPath = s4.HasTpConfig && s4.ImportedConfig is not null;
        }

        return true;
    }

    /// <summary>
    /// Step 1a validation: existing-DB path only.
    /// Opens the chosen database with InitializeServices, saves paths, and acquires the write lock
    /// so the wizard finishes in write mode.
    /// Returns true on both the "Yes, I have a DB" and "No, fresh install" paths.
    /// </summary>
    private async Task<bool> ValidateStep1a()
    {
        if (CurrentStep is not Step1aExistingDbViewModel s1a) return false;
        s1a.ErrorMessage = string.Empty;

        if (!s1a.HasExistingDb) return true;   // fresh-install path — nothing to validate here

        var dbPath = s1a.DbPath;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            s1a.ErrorMessage = "Please choose an existing database file.";
            return false;
        }

        try
        {
            _services.InitializeServices(dbPath);

            // Acquire the write lock so the wizard has write access to the database.
            // TryAcquire() is atomic; it will fail if another instance holds the lock
            // (collision-detection handled by WriteLockService).
            App.LockService.TryAcquire(dbPath);

            var settings = AppSettings.Current;
            settings.DatabasePath     = dbPath;
            settings.BackupFolderPath = s1a.BackupFolder;
            // IsInitialSetupComplete is set in FinishAsync, not here
            settings.Save();

            _isExistingDbPath = true;
            return true;
        }
        catch (Exception ex)
        {
            s1a.ErrorMessage = $"Could not open the database: {ex.Message}";
            App.Logger.LogError(ex, "StartupWizard: step 1a existing-DB validation failed");
            return false;
        }

        // Suppress CS1998 — async signature kept for consistency with other Validate methods
        await Task.CompletedTask;
    }

    /// <summary>
    /// Step 3 validation: creates the database and calls InitializeServices.
    /// Persists the database and backup paths but does NOT yet set IsInitialSetupComplete.
    /// Acquires the write lock so subsequent wizard steps (Campuses, Block Patterns, etc.)
    /// have write access to manage these entities.
    /// On failure, sets the step's ErrorMessage and returns false.
    /// </summary>
    private async Task<bool> ValidateStep3()
    {
        if (CurrentStep is not Step2DatabaseViewModel s3) return false;
        s3.ErrorMessage = string.Empty;

        var dbPath = s3.DbFullPath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            s3.ErrorMessage = "Could not determine the database path.";
            return false;
        }

        try
        {
            var folder = Path.GetDirectoryName(dbPath)!;
            Directory.CreateDirectory(folder);

            _services.InitializeServices(dbPath);

            // Acquire the write lock so wizard steps that manage entities (Campuses, Block Patterns, etc.)
            // have write access. TryAcquire() is atomic; it succeeds here because no other instance
            // holds the lock yet.
            App.LockService.TryAcquire(dbPath);

            var settings = AppSettings.Current;
            settings.DatabasePath     = dbPath;
            settings.BackupFolderPath = s3.BackupFolder;
            settings.Save();

            return true;
        }
        catch (Exception ex)
        {
            s3.ErrorMessage = $"Could not create the database: {ex.Message}";
            App.Logger.LogError(ex, "StartupWizard: step 3 DB creation failed");
            return false;
        }
    }

    // ── Commit ───────────────────────────────────────────────────────────────

    /// <summary>Reads values from the current step into shared state for later steps.</summary>
    private Task CommitCurrentStep()
    {
        switch (_stepIndex)
        {
            case 3 when CurrentStep is Step1InstitutionViewModel s2:
                _institutionName   = s2.InstitutionName.Trim();
                _institutionAbbrev = s2.InstitutionAbbrev.Trim();
                _acUnitName        = s2.AcUnitName.Trim();
                _acUnitAbbrev      = s2.AcUnitAbbrev.Trim();
                // Step 4 (Database) needs the abbrev to suggest a filename — invalidate its cache
                _stepCache.Remove(4);
                break;

            case 4 when CurrentStep is Step2DatabaseViewModel s3:
                _dbFolder     = s3.DbFolder;
                _backupFolder = s3.BackupFolder;
                break;
        }

        return Task.CompletedTask;
    }

    // ── Finish ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the user clicks Finish on the last step.
    ///
    /// Existing-DB path (step 1a, Yes):  DB already open — just sets the flag and closes.
    /// ExitNow path     (step 4):        DB created — sets flag and closes without writing records.
    /// Normal paths     (step 11):       writes DB records + .tpconfig, sets flag, closes.
    ///
    /// IsInitialSetupComplete is set to true here and nowhere else.
    /// </summary>
    private async Task FinishAsync()
    {
        // Existing-DB path — DB is already initialized; no records to write.
        if (_isExistingDbPath)
        {
            AppSettings.Current.IsInitialSetupComplete = true;
            AppSettings.Current.Save();
            IsComplete = true;
            _window?.Close();
            return;
        }

        // ExitNow path — DB created, wizard config skipped.
        if (_stepIndex == 5 && CurrentStep is Step3TpConfigViewModel { IsExitNowChoice: true })
        {
            AppSettings.Current.IsInitialSetupComplete = true;
            AppSettings.Current.Save();
            IsComplete = true;
            _window?.Close();
            return;
        }

        // Normal finish — user clicked Finish on the closing panel (step 11).
        try
        {
            await WriteDbRecordsAsync();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "StartupWizard: FinishAsync failed");
            // Non-fatal: DB is created and paths are saved. Log and proceed.
        }

        AppSettings.Current.IsInitialSetupComplete = true;
        AppSettings.Current.Save();
        IsComplete = true;
        _window?.Close();
    }

    // ── Write records ─────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the institution/academic unit, first academic year, and semesters to the database.
    /// </summary>
    private async Task WriteDbRecordsAsync()
    {
        // --- Academic Unit ---
        var auRepo = _services.AcademicUnits();
        var units  = auRepo.GetAll();
        var unit   = units.FirstOrDefault() ?? new AcademicUnit();
        unit.Name         = _acUnitName;
        unit.Abbreviation = _acUnitAbbrev;

        if (units.Count == 0) auRepo.Insert(unit);
        else                  auRepo.Update(unit);

        AppSettings.Current.InstitutionName   = _institutionName;
        AppSettings.Current.InstitutionAbbrev = _institutionAbbrev;
        AppSettings.Current.Save();

        // --- First Academic Year ---
        if (!_stepCache.TryGetValue(10, out var s9vm) || s9vm is not Step5AcademicYearViewModel s9) return;

        var ayRepo  = _services.AcademicYears();
        var semRepo = _services.Semesters();

        var ay = new AcademicYear { Name = s9.ExpandedAcademicYearName };
        ayRepo.Insert(ay);

        // Resolve the imported config (null on the manual path).
        TpConfigData? importedCfg = null;
        if (_isImportPath && _stepCache.TryGetValue(5, out var s4CfgVm) && s4CfgVm is Step3TpConfigViewModel s4Cfg)
            importedCfg = s4Cfg.ImportedConfig;

        // --- Import path: campuses and section prefixes from .tpconfig ---
        if (importedCfg is not null)
        {
            var campusRepo = _services.Campuses();
            var prefixRepo = _services.SectionPrefixes();

            // Insert campuses in order; build name→ID map for prefix resolution.
            var campusNameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int campusSortOrder = 0;
            foreach (var campusName in importedCfg.Campuses)
            {
                if (string.IsNullOrWhiteSpace(campusName)) continue;
                var campus = new Campus { Name = campusName.Trim(), SortOrder = campusSortOrder++ };
                campusRepo.Insert(campus);
                campusNameToId[campus.Name] = campus.Id;
            }

            // Insert section prefixes, resolving campus name → ID.
            foreach (var spDef in importedCfg.SectionPrefixes)
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

            // Insert block patterns (e.g. MWF, TR) from .tpconfig.
            var blockPatternRepo = _services.BlockPatterns();
            foreach (var bp in importedCfg.BlockPatterns)
            {
                if (string.IsNullOrWhiteSpace(bp.Name)) continue;
                blockPatternRepo.Insert(new BlockPattern { Name = bp.Name, Days = bp.Days });
            }
        }

        // Seed legal start times:
        //   import path  → block lengths from .tpconfig
        //   manual path  → wizard step 6 data (pre-populated from AppDefaults, user-editable)
        var db = _services.Database();
        if (importedCfg?.BlockLengths is { Count: > 0 } importBlockLengths)
        {
            var seedData = importBlockLengths
                .Select(bl => (bl.Hours, bl.StartTimes))
                .ToList();
            SchedulingAssistant.Data.SeedData.SeedWizardLegalStartTimes(db.Connection, ay.Id, seedData);

            // Persist weekend-day choices from the imported .tpconfig into settings.
            AppSettings.Current.IncludeSaturday = importedCfg.IncludeSaturday;
            AppSettings.Current.IncludeSunday   = importedCfg.IncludeSunday;
        }
        else if (_stepCache.TryGetValue(7, out var s6lstVm) && s6lstVm is Step5SchedulingViewModel s6lst)
        {
            var seedData = s6lst.GetSeedData();
            if (seedData.Count > 0)
                SchedulingAssistant.Data.SeedData.SeedWizardLegalStartTimes(db.Connection, ay.Id, seedData);
            // else: user intentionally cleared all block lengths — seed nothing.

            // Persist weekend-day choices from the wizard into settings.
            AppSettings.Current.IncludeSaturday = s6lst.IncludeSaturday;
            AppSettings.Current.IncludeSunday   = s6lst.IncludeSunday;
        }
        // else: step 6 was not visited (should not happen on the manual path) — seed nothing.

        // --- Semesters with colors ---
        // Manual path: colors from step 10.  Import path: colors from step 4 (.tpconfig).
        var colorBySemesterName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_isImportPath && _stepCache.TryGetValue(11, out var s10vm) && s10vm is Step6SemesterColorsViewModel s10)
        {
            foreach (var row in s10.Rows)
                colorBySemesterName[row.Name] = row.HexColor;
        }
        else if (_isImportPath && _stepCache.TryGetValue(5, out var s4vm) && s4vm is Step3TpConfigViewModel s4)
        {
            foreach (var def in s4.ImportedConfig?.SemesterDefs ?? [])
                colorBySemesterName[def.Name] = def.Color;
        }

        int sortOrder = 0;
        foreach (var semDef in s9.Semesters)
        {
            var color = colorBySemesterName.TryGetValue(semDef.Name, out var c) ? c : string.Empty;
            var sem = new Semester
            {
                AcademicYearId = ay.Id,
                Name           = semDef.Name.Trim(),
                SortOrder      = sortOrder++,
                Color          = color
            };
            semRepo.Insert(sem);
        }

        var semesterContext = _services.SemesterContext();
        semesterContext.Reload(ayRepo, semRepo);

        await Task.CompletedTask;
    }

}

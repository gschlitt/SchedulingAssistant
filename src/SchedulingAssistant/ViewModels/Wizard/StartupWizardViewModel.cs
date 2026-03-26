using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
///   1  — Existing-DB check (Step1a)          ← new
///   2  — Institution                          (was 1)
///   3  — Database location + filename         (was 2)
///   4  — TpConfig import / ExitNow            (was 3)
///   5  — Campuses                             (was 4)
///   6  — Legal start times                    (was 5)
///   7  — Block patterns                       (was 6)
///   8  — Section prefixes                     (was 7)
///   9  — Academic year                        (was 8)
///  10  — Semester colors                      (was 9)
///  11  — Closing panel                        (was 10)
///
/// Routing variants:
///   Existing-DB path  (step 1, Yes):  steps 0–1 → finish immediately
///   ExitNow path      (step 4):       steps 0–4 → finish
///   Import path       (step 4):       skips 5–8 and 10 → 9 → 11
///   Manual path:                      all steps → 11
///
/// IsInitialSetupComplete is set to true only inside <see cref="FinishAsync"/>.
/// The window closes when <see cref="IsComplete"/> becomes true.
/// If closed before that, the app shuts down.
/// </summary>
public partial class StartupWizardViewModel : ViewModelBase
{
    private readonly Window _window;

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

    public StartupWizardViewModel(Window window)
    {
        _window = window;
        NavigateTo(0);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Next()
    {
        if (!await ValidateCurrentStep()) return;
        await CommitCurrentStep();

        // Existing-DB path finishes immediately after step 1a
        if (_isExistingDbPath)
        {
            await FinishAsync();
            return;
        }

        var next = _stepIndex + 1;

        // Import path skips manual-config steps (5–8) and the colors step (10)
        if (_isImportPath && next >= 5 && next <= 8) next = 9;
        if (_isImportPath && next == 10)              next = 11;

        if (IsLastStep())
            await FinishAsync();
        else
            NavigateTo(next);
    }

    [RelayCommand]
    private void Back()
    {
        var prev = _stepIndex - 1;

        // Import path: stepping back skips manual-config steps (5–8) and colors (10)
        if (_isImportPath && prev >= 5 && prev <= 8) prev = 4;
        if (_isImportPath && prev == 10)              prev = 9;

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
        OnPropertyChanged(nameof(NextButtonText));
    }

    private WizardStepViewModel BuildStep(int index) => index switch
    {
        0  => new Step0WelcomeViewModel(),
        1  => BuildStep1a(),                                        // existing-DB check
        2  => new Step1InstitutionViewModel(),
        3  => new Step2DatabaseViewModel(_acUnitAbbrev, _window),
        4  => BuildStep4(),                                         // TpConfig / ExitNow
        // Manual-path config steps (5–8):
        5  => new Step4CampusesViewModel(),
        6  => new Step5LegalStartTimesViewModel(),
        7  => new Step6BlockPatternsViewModel(),
        8  => new Step7SectionPrefixesViewModel(),
        // Academic year + colors (9–10):
        9  => BuildStep9(),
        10 => BuildStep10(),
        // Closing panel (both normal paths):
        11 => new Step10ClosingViewModel(),
        _  => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>
    /// Builds the step-1a VM and wires a PropertyChanged handler so that the
    /// Next/Finish button label updates immediately when the choice changes.
    /// </summary>
    private Step1aExistingDbViewModel BuildStep1a()
    {
        var vm = new Step1aExistingDbViewModel(_window);
        vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NextButtonText));
        return vm;
    }

    /// <summary>
    /// Builds the TpConfig VM and wires a PropertyChanged handler so that the
    /// Next/Finish button label updates immediately when ExitNow is toggled.
    /// </summary>
    private Step3TpConfigViewModel BuildStep4()
    {
        var vm = new Step3TpConfigViewModel(_window);
        vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NextButtonText));
        return vm;
    }

    /// <summary>Builds the Academic Year step, pre-populating semesters on the import path.</summary>
    private Step5AcademicYearViewModel BuildStep9()
    {
        var vm = new Step5AcademicYearViewModel();

        if (_isImportPath && _stepCache.TryGetValue(4, out var s4vm) && s4vm is Step3TpConfigViewModel s4)
        {
            if (s4.ImportedConfig?.SemesterDefs is { Count: > 0 } defs)
                vm.LoadFromConfig(defs);
        }

        return vm;
    }

    /// <summary>Builds the Semester Colors step, seeding rows from the Academic Year step.</summary>
    private Step6SemesterColorsViewModel BuildStep10()
    {
        var vm = new Step6SemesterColorsViewModel();
        if (_stepCache.TryGetValue(9, out var s9vm) && s9vm is Step5AcademicYearViewModel s9)
            vm.LoadFromSemesters(s9.Semesters);
        return vm;
    }

    private bool IsLastStep()
    {
        // Existing-DB path: wizard finishes immediately after step 1a
        if (_stepIndex == 1 && CurrentStep is Step1aExistingDbViewModel { IsExistingDbChoice: true })
            return true;
        // ExitNow at step 4 (TpConfig)
        if (_stepIndex == 4 && CurrentStep is Step3TpConfigViewModel { IsExitNowChoice: true })
            return true;
        // Both normal paths converge on the closing panel
        return _stepIndex == 11;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs step-specific validation. Returns true when the user may advance.
    /// </summary>
    private async Task<bool> ValidateCurrentStep()
    {
        if (!CurrentStep.CanAdvance)
            return false;

        if (_stepIndex == 1)
            return await ValidateStep1a();

        if (_stepIndex == 3)
            return await ValidateStep3();

        if (_stepIndex == 4 && CurrentStep is Step3TpConfigViewModel s4)
        {
            if (!s4.ValidateAndImport()) return false;
            _isImportPath = s4.HasTpConfig && s4.ImportedConfig is not null;
        }

        return true;
    }

    /// <summary>
    /// Step 1a validation: existing-DB path only.
    /// Opens the chosen database with InitializeServices, saves paths, and sets
    /// IsInitialSetupComplete = true so the wizard finishes immediately.
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
            App.InitializeServices(dbPath);

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

            App.InitializeServices(dbPath);

            var settings = AppSettings.Current;
            settings.DatabasePath     = dbPath;
            settings.BackupFolderPath = _backupFolder;
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
            case 2 when CurrentStep is Step1InstitutionViewModel s2:
                _institutionName   = s2.InstitutionName.Trim();
                _institutionAbbrev = s2.InstitutionAbbrev.Trim();
                _acUnitName        = s2.AcUnitName.Trim();
                _acUnitAbbrev      = s2.AcUnitAbbrev.Trim();
                // Step 3 (Database) needs the abbrev to suggest a filename — invalidate its cache
                _stepCache.Remove(3);
                break;

            case 3 when CurrentStep is Step2DatabaseViewModel s3:
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
            _window.Close();
            return;
        }

        // ExitNow path — DB created, wizard config skipped.
        if (_stepIndex == 4 && CurrentStep is Step3TpConfigViewModel { IsExitNowChoice: true })
        {
            AppSettings.Current.IsInitialSetupComplete = true;
            AppSettings.Current.Save();
            IsComplete = true;
            _window.Close();
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
        _window.Close();
    }

    // ── Write records ─────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the institution/academic unit, first academic year, and semesters to the database.
    /// </summary>
    private async Task WriteDbRecordsAsync()
    {
        // --- Academic Unit ---
        var auRepo = App.Services.GetRequiredService<IAcademicUnitRepository>();
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
        if (!_stepCache.TryGetValue(9, out var s9vm) || s9vm is not Step5AcademicYearViewModel s9) return;

        var ayRepo  = App.Services.GetRequiredService<IAcademicYearRepository>();
        var semRepo = App.Services.GetRequiredService<ISemesterRepository>();

        var ay = new AcademicYear { Name = s9.ExpandedAcademicYearName };
        ayRepo.Insert(ay);

        // Resolve the imported config (null on the manual path).
        TpConfigData? importedCfg = null;
        if (_isImportPath && _stepCache.TryGetValue(4, out var s4CfgVm) && s4CfgVm is Step3TpConfigViewModel s4Cfg)
            importedCfg = s4Cfg.ImportedConfig;

        // --- Import path: campuses and section prefixes from .tpconfig ---
        if (importedCfg is not null)
        {
            var campusRepo = App.Services.GetRequiredService<ICampusRepository>();
            var prefixRepo = App.Services.GetRequiredService<ISectionPrefixRepository>();

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
            var blockPatternRepo = App.Services.GetRequiredService<IBlockPatternRepository>();
            foreach (var bp in importedCfg.BlockPatterns)
            {
                if (string.IsNullOrWhiteSpace(bp.Name)) continue;
                blockPatternRepo.Insert(new BlockPattern { Name = bp.Name, Days = bp.Days });
            }
        }

        // Seed legal start times:
        //   import path  → block lengths from .tpconfig
        //   manual path  → wizard step 6 data (or hardcoded defaults if step was skipped)
        var db = App.Services.GetRequiredService<IDatabaseContext>();
        if (importedCfg?.BlockLengths is { Count: > 0 } importBlockLengths)
        {
            var seedData = importBlockLengths
                .Select(bl => (bl.Hours, bl.StartTimes))
                .ToList();
            SchedulingAssistant.Data.SeedData.SeedWizardLegalStartTimes(db.Connection, ay.Id, seedData);
        }
        else if (_stepCache.TryGetValue(6, out var s6lstVm) && s6lstVm is Step5LegalStartTimesViewModel s6lst)
        {
            var seedData = s6lst.GetSeedData();
            if (seedData.Count > 0)
                SchedulingAssistant.Data.SeedData.SeedWizardLegalStartTimes(db.Connection, ay.Id, seedData);
            else
                SchedulingAssistant.Data.SeedData.SeedDefaultLegalStartTimes(db.Connection, ay.Id);
        }
        else
        {
            SchedulingAssistant.Data.SeedData.SeedDefaultLegalStartTimes(db.Connection, ay.Id);
        }

        // --- Semesters with colors ---
        // Manual path: colors from step 10.  Import path: colors from step 4 (.tpconfig).
        var colorBySemesterName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_isImportPath && _stepCache.TryGetValue(10, out var s10vm) && s10vm is Step6SemesterColorsViewModel s10)
        {
            foreach (var row in s10.Rows)
                colorBySemesterName[row.Name] = row.HexColor;
        }
        else if (_isImportPath && _stepCache.TryGetValue(4, out var s4vm) && s4vm is Step3TpConfigViewModel s4)
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

        var semesterContext = App.Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(ayRepo, semRepo);

        await Task.CompletedTask;
    }

}

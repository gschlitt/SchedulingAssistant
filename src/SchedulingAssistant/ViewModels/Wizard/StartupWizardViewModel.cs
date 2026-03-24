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
/// Steps 0–2 run before DI is initialized (before App.InitializeServices is called).
/// Step 2 creates the database and calls InitializeServices (IsInitialSetupComplete is NOT set here).
/// Step 3 either exits early (ExitNow) or routes to the manual/import config path.
/// IsInitialSetupComplete is set to true only when the wizard is fully finished (or ExitNow is chosen).
/// Steps 3–6 use App.Services to resolve existing management ViewModels.
///
/// The window hosting this VM should close when <see cref="IsComplete"/> becomes true.
/// If the window is closed before that, <see cref="IsComplete"/> remains false and the
/// app should shut down.
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

    // ── Shared state flowing across steps ───────────────────────────────────

    private string _institutionName    = string.Empty;
    private string _institutionAbbrev  = string.Empty;
    private string _acUnitName         = string.Empty;
    private string _acUnitAbbrev       = string.Empty;
    private string _dbFolder           = string.Empty;
    private string _backupFolder       = string.Empty;

    /// <summary>True when the import path was chosen in step 3 and a .tpconfig was loaded.</summary>
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

        var next = _stepIndex + 1;
        // Import path skips the four manual-config steps (4–7) and the colors step (9).
        if (_isImportPath && next >= 4 && next <= 7) next = 8;
        if (_isImportPath && next == 9) next = int.MaxValue; // → finish

        if (IsLastStep() || next == int.MaxValue)
        {
            await FinishAsync();
        }
        else
        {
            NavigateTo(next);
        }
    }

    [RelayCommand]
    private void Back()
    {
        var prev = _stepIndex - 1;
        // Import path: stepping back from the Academic Year step (8) goes to TpConfig (3),
        // skipping the manual-config steps.
        if (_isImportPath && prev >= 4 && prev <= 7) prev = 3;
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
        0 => new Step0WelcomeViewModel(),
        1 => new Step1InstitutionViewModel(),
        2 => new Step2DatabaseViewModel(_acUnitAbbrev, _window),
        3 => new Step3TpConfigViewModel(_window),
        // Manual-path config steps (4–7):
        4 => new Step4CampusesViewModel(),
        5 => new Step5LegalStartTimesViewModel(),
        6 => new Step6BlockPatternsViewModel(),
        7 => new Step7SectionPrefixesViewModel(),
        // Academic year + colors (8–9):
        8 => BuildStep8(),
        9 => BuildStep9(),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private Step5AcademicYearViewModel BuildStep8()
    {
        var vm = new Step5AcademicYearViewModel();

        if (_isImportPath && _stepCache.TryGetValue(3, out var s3vm) && s3vm is Step3TpConfigViewModel s3)
        {
            // Import path — pre-populate semesters from the .tpconfig
            if (s3.ImportedConfig?.SemesterDefs is { Count: > 0 } defs)
                vm.LoadFromConfig(defs);
        }

        return vm;
    }

    private Step6SemesterColorsViewModel BuildStep9()
    {
        var vm = new Step6SemesterColorsViewModel();
        if (_stepCache.TryGetValue(8, out var s8vm) && s8vm is Step5AcademicYearViewModel s8)
            vm.LoadFromSemesters(s8.Semesters);
        return vm;
    }

    private bool IsLastStep()
    {
        // ExitNow at step 3 terminates the wizard immediately
        if (_stepIndex == 3 && CurrentStep is Step3TpConfigViewModel { IsExitNowChoice: true })
            return true;
        // Import path ends at step 8 (Academic Year); manual path ends at step 9 (Semester Colors)
        return (_isImportPath && _stepIndex == 8) || (!_isImportPath && _stepIndex == 9);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs step-specific validation. For step 3, calls ValidateAndImport.
    /// Returns true when the user may advance.
    /// </summary>
    private async Task<bool> ValidateCurrentStep()
    {
        if (!CurrentStep.CanAdvance)
            return false;

        if (_stepIndex == 2)
            return await ValidateStep2();

        if (_stepIndex == 3 && CurrentStep is Step3TpConfigViewModel s3)
        {
            if (!s3.ValidateAndImport()) return false;
            _isImportPath = s3.HasTpConfig && s3.ImportedConfig is not null;
        }

        return true;
    }

    /// <summary>
    /// Step 2 validation: creates the database and calls InitializeServices.
    /// Persists the database and backup paths but does NOT yet set IsInitialSetupComplete —
    /// that flag is deferred to a later step so the wizard can be exited cleanly at step 3.
    /// On failure, sets the step's ErrorMessage and returns false.
    /// </summary>
    private async Task<bool> ValidateStep2()
    {
        if (CurrentStep is not Step2DatabaseViewModel s2) return false;
        s2.ErrorMessage = string.Empty;

        var dbPath = s2.DbFullPath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            s2.ErrorMessage = "Could not determine the database path.";
            return false;
        }

        try
        {
            // Create parent folder if needed
            var folder = Path.GetDirectoryName(dbPath)!;
            Directory.CreateDirectory(folder);

            // InitializeServices creates the SQLite file and applies the schema
            App.InitializeServices(dbPath);

            // Persist the paths so a subsequent launch can find the database.
            // IsInitialSetupComplete is intentionally left false here — it is set
            // either when the user chooses ExitNow in step 3, or at FinishAsync.
            var settings = AppSettings.Current;
            settings.DatabasePath     = dbPath;
            settings.BackupFolderPath = _backupFolder;
            settings.Save();

            return true;
        }
        catch (Exception ex)
        {
            s2.ErrorMessage = $"Could not create the database: {ex.Message}";
            App.Logger.LogError(ex, "StartupWizard: step 2 DB creation failed");
            return false;
        }
    }

    // ── Commit ───────────────────────────────────────────────────────────────

    /// <summary>Reads values from the current step into shared state for later steps.</summary>
    private Task CommitCurrentStep()
    {
        switch (_stepIndex)
        {
            case 1 when CurrentStep is Step1InstitutionViewModel s1:
                _institutionName   = s1.InstitutionName.Trim();
                _institutionAbbrev = s1.InstitutionAbbrev.Trim();
                _acUnitName        = s1.AcUnitName.Trim();
                _acUnitAbbrev      = s1.AcUnitAbbrev.Trim();
                // Step 2 needs the abbrev to suggest a filename — invalidate its cache
                _stepCache.Remove(2);
                break;

            case 2 when CurrentStep is Step2DatabaseViewModel s2:
                _dbFolder     = s2.DbFolder;
                _backupFolder = s2.BackupFolder;
                break;
        }

        return Task.CompletedTask;
    }

    // ── Finish ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the user clicks Finish on the last step.
    /// If the user chose ExitNow at step 3, just sets the completion flag and closes.
    /// Otherwise writes DB records, persists semester colors, and writes the .tpconfig.
    /// Closes the wizard window on success.
    /// </summary>
    private async Task FinishAsync()
    {
        // ExitNow path — DB is created but wizard configuration is skipped.
        // Mark setup complete so the next launch opens the main window directly.
        if (_stepIndex == 3 && CurrentStep is Step3TpConfigViewModel { IsExitNowChoice: true })
        {
            AppSettings.Current.IsInitialSetupComplete = true;
            AppSettings.Current.Save();
            IsComplete = true;
            _window.Close();
            return;
        }

        try
        {
            await WriteDbRecordsAsync();
            WriteTpConfig();
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

    /// <summary>
    /// Writes the institution/academic unit, first academic year, and semesters to the database.
    /// </summary>
    private async Task WriteDbRecordsAsync()
    {
        // --- Academic Unit (name + abbreviation) ---
        var auRepo = App.Services.GetRequiredService<IAcademicUnitRepository>();
        var units  = auRepo.GetAll();
        var unit   = units.FirstOrDefault() ?? new AcademicUnit();
        unit.Name         = _acUnitName;
        unit.Abbreviation = _acUnitAbbrev;

        if (units.Count == 0)
            auRepo.Insert(unit);
        else
            auRepo.Update(unit);

        // --- Institution name stored as an AppConfig key ---
        // (Institution name lives in DB config if an IAppConfigRepository exists.
        //  For now store in AppSettings — lightweight and immediately available.)
        AppSettings.Current.InstitutionName   = _institutionName;
        AppSettings.Current.InstitutionAbbrev = _institutionAbbrev;
        AppSettings.Current.Save();

        // --- First Academic Year ---
        if (!_stepCache.TryGetValue(8, out var s5vm) || s5vm is not Step5AcademicYearViewModel s5) return;

        var ayRepo  = App.Services.GetRequiredService<IAcademicYearRepository>();
        var semRepo = App.Services.GetRequiredService<ISemesterRepository>();

        var ay = new AcademicYear { Name = s5.ExpandedAcademicYearName };
        ayRepo.Insert(ay);

        // Seed legal start times — use wizard step 5 data if the user configured any,
        // otherwise fall back to the built-in defaults.
        var db = App.Services.GetRequiredService<IDatabaseContext>();
        if (_stepCache.TryGetValue(5, out var s5lstVm) && s5lstVm is Step5LegalStartTimesViewModel s5lst)
        {
            var seedData = s5lst.GetSeedData();
            if (seedData.Count > 0)
                SchedulingAssistant.Data.SeedData.SeedWizardLegalStartTimes(db.Connection, ay.Id, seedData);
            else
                SchedulingAssistant.Data.SeedData.SeedDefaultLegalStartTimes(db.Connection, ay.Id);
        }
        else
        {
            // Import path skips step 5 — seed the defaults.
            SchedulingAssistant.Data.SeedData.SeedDefaultLegalStartTimes(db.Connection, ay.Id);
        }

        // --- Semesters with colors ---
        // If we're on the import path, colors come from step 3 (.tpconfig SemesterDefs).
        // If manual path, colors come from step 9.
        var colorBySemesterName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_isImportPath && _stepCache.TryGetValue(9, out var s6vm) && s6vm is Step6SemesterColorsViewModel s6)
        {
            foreach (var row in s6.Rows)
                colorBySemesterName[row.Name] = row.HexColor;
        }
        else if (_isImportPath && _stepCache.TryGetValue(3, out var s3vm) && s3vm is Step3TpConfigViewModel s3)
        {
            foreach (var def in s3.ImportedConfig?.SemesterDefs ?? [])
                colorBySemesterName[def.Name] = def.Color;
        }

        int sortOrder = 0;
        foreach (var semDef in s5.Semesters)
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

        // Reload the semester context so the main window sees the new data
        var semesterContext = App.Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(ayRepo, semRepo);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Writes the .tpconfig file (manual path only). Non-fatal on failure.
    /// </summary>
    private void WriteTpConfig()
    {
        if (_isImportPath) return; // import path — no point in writing back the same file

        if (!_stepCache.TryGetValue(9, out var s6vm) || s6vm is not Step6SemesterColorsViewModel s6) return;
        if (!_stepCache.TryGetValue(8, out var s5vm) || s5vm is not Step5AcademicYearViewModel s5) return;

        var semDefs = s5.Semesters.Zip(s6.Rows, (sem, row) =>
            new TpConfigSemesterDef { Name = sem.Name, Color = row.HexColor }
        ).ToList();

        var config = new TpConfigData { SemesterDefs = semDefs };
        TpConfigService.Write(_dbFolder, config, _acUnitAbbrev);
    }
}

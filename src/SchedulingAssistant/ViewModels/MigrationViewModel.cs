// ─────────────────────────────────────────────────────────────────────────────
// ONE-TIME MIGRATION UTILITY — DELETE AFTER USE
// ─────────────────────────────────────────────────────────────────────────────

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Threading.Tasks;

#if DEBUG
using Avalonia.Platform.Storage;
using SchedulingAssistant.Data;
using SchedulingAssistant.Migration;
#endif

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// ViewModel for the one-time migration flyout.
///
/// Phase 1 — CSV to JSON:
///   1. XYZ_FLOW_YEARS CSV  →  one .json per academic year
///   2. XYZ_UNITS      CSV  →  one .json per academic unit (PermanentProperties column)
///
/// Phase 2 — JSON to new schema:
///   Select both json-output folders, run a dry run to preview, then import.
///
/// Only functional in DEBUG builds; the class shell is compiled in all
/// configurations so the ViewLocator can still resolve it without DI issues.
/// </summary>
public partial class MigrationViewModel : ViewModelBase
{
#if DEBUG
    private readonly MainWindowViewModel _mainVm;
    private readonly DatabaseContext     _db;

    // ── Phase 1 — Years section ───────────────────────────────────────────────

    /// <summary>Path to the XYZ_FLOW_YEARS CSV chosen by the user.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertYearsCommand))]
    private string? _yearsCsvPath;

    /// <summary>Output folder for year JSON files (auto-derived from CSV path).</summary>
    [ObservableProperty] private string? _yearsOutputDir;

    /// <summary>Conversion result shown below the Years section.</summary>
    [ObservableProperty] private string? _yearsStatusMessage;

    /// <summary>True while the years CSV conversion is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertYearsCommand))]
    private bool _isYearsRunning;

    // ── Phase 1 — Units section ───────────────────────────────────────────────

    /// <summary>Path to the XYZ_UNITS CSV chosen by the user.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertUnitsCommand))]
    private string? _unitsCsvPath;

    /// <summary>Output folder for unit JSON files (auto-derived from CSV path).</summary>
    [ObservableProperty] private string? _unitsOutputDir;

    /// <summary>Conversion result shown below the Units section.</summary>
    [ObservableProperty] private string? _unitsStatusMessage;

    /// <summary>True while the units CSV conversion is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertUnitsCommand))]
    private bool _isUnitsRunning;

    // ── Phase 2 — JSON → new schema ───────────────────────────────────────────

    /// <summary>Folder containing unit JSON files produced by Phase 1.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Phase2DryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(Phase2ImportCommand))]
    private string? _phase2UnitsJsonDir;

    /// <summary>Folder containing year JSON files produced by Phase 1.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Phase2DryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(Phase2ImportCommand))]
    private string? _phase2YearsJsonDir;

    /// <summary>Report text from the most recent dry run or import.</summary>
    [ObservableProperty] private string? _phase2StatusMessage;

    /// <summary>True while Phase 2 is running (disables both buttons).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Phase2DryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(Phase2ImportCommand))]
    private bool _isPhase2Running;

    /// <summary>
    /// Unlocked after a successful dry run; enables the Import button.
    /// Reset to false whenever either folder path changes, so the user must
    /// re-run dry run before they can import.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Phase2ImportCommand))]
    private bool _phase2DryRunReviewed;

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <param name="mainVm">Provides the StorageProvider for file/folder pickers.</param>
    /// <param name="db">The active database context, used by the Phase 2 importer.</param>
    public MigrationViewModel(MainWindowViewModel mainVm, DatabaseContext db)
    {
        _mainVm = mainVm;
        _db     = db;
    }

    // ── Phase 1 — Years commands ──────────────────────────────────────────────

    /// <summary>
    /// Opens a .csv file picker and sets <see cref="YearsCsvPath"/>.
    /// Auto-derives <see cref="YearsOutputDir"/> as a "json-output" subfolder
    /// beside the chosen file.
    /// </summary>
    [RelayCommand]
    private async Task BrowseYearsCsv()
        => await BrowseCsvAsync(
            "Select XYZ_FLOW_YEARS CSV export",
            path => { YearsCsvPath = path; YearsOutputDir = OutputDirFor(path); YearsStatusMessage = null; });

    /// <summary>
    /// Converts the selected XYZ_FLOW_YEARS CSV into one .json file per row
    /// and populates <see cref="YearsStatusMessage"/> with the result.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConvertYears))]
    private async Task ConvertYears()
    {
        if (YearsCsvPath is null || YearsOutputDir is null) return;
        IsYearsRunning     = true;
        YearsStatusMessage = "Converting…";
        try   { YearsStatusMessage = await MigrationRunner.ConvertYearsCsvToJsonAsync(YearsCsvPath, YearsOutputDir); }
        finally { IsYearsRunning = false; }
    }

    private bool CanConvertYears() => !IsYearsRunning && !string.IsNullOrWhiteSpace(YearsCsvPath);

    // ── Phase 1 — Units commands ──────────────────────────────────────────────

    /// <summary>
    /// Opens a .csv file picker and sets <see cref="UnitsCsvPath"/>.
    /// Auto-derives <see cref="UnitsOutputDir"/> as a "json-output" subfolder
    /// beside the chosen file.
    /// </summary>
    [RelayCommand]
    private async Task BrowseUnitsCsv()
        => await BrowseCsvAsync(
            "Select XYZ_UNITS CSV export",
            path => { UnitsCsvPath = path; UnitsOutputDir = OutputDirFor(path); UnitsStatusMessage = null; });

    /// <summary>
    /// Converts the selected XYZ_UNITS CSV (PermanentProperties column) into
    /// one .json file per row and populates <see cref="UnitsStatusMessage"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConvertUnits))]
    private async Task ConvertUnits()
    {
        if (UnitsCsvPath is null || UnitsOutputDir is null) return;
        IsUnitsRunning     = true;
        UnitsStatusMessage = "Converting…";
        try   { UnitsStatusMessage = await MigrationRunner.ConvertUnitsCsvToJsonAsync(UnitsCsvPath, UnitsOutputDir); }
        finally { IsUnitsRunning = false; }
    }

    private bool CanConvertUnits() => !IsUnitsRunning && !string.IsNullOrWhiteSpace(UnitsCsvPath);

    // ── Phase 2 commands ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens a folder picker and sets <see cref="Phase2UnitsJsonDir"/>.
    /// Resets the dry-run-reviewed flag so the user must re-run dry run
    /// after changing either folder.
    /// </summary>
    [RelayCommand]
    private async Task BrowsePhase2UnitsDir()
    {
        var dir = await BrowseFolderAsync("Select units JSON folder (Phase 1 XYZ_UNITS output)");
        if (dir is not null)
        {
            Phase2UnitsJsonDir   = dir;
            Phase2DryRunReviewed = false;
            Phase2StatusMessage  = null;
        }
    }

    /// <summary>
    /// Opens a folder picker and sets <see cref="Phase2YearsJsonDir"/>.
    /// Resets the dry-run-reviewed flag so the user must re-run dry run
    /// after changing either folder.
    /// </summary>
    [RelayCommand]
    private async Task BrowsePhase2YearsDir()
    {
        var dir = await BrowseFolderAsync("Select years JSON folder (Phase 1 XYZ_FLOW_YEARS output)");
        if (dir is not null)
        {
            Phase2YearsJsonDir   = dir;
            Phase2DryRunReviewed = false;
            Phase2StatusMessage  = null;
        }
    }

    /// <summary>
    /// Runs <see cref="Phase2Importer"/> in dry-run mode: reads and translates
    /// all data but writes nothing.  Populates <see cref="Phase2StatusMessage"/>
    /// with the preview report.  On success, sets
    /// <see cref="Phase2DryRunReviewed"/> to unlock the Import button.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunPhase2))]
    private async Task Phase2DryRun()
    {
        if (string.IsNullOrWhiteSpace(Phase2UnitsJsonDir) ||
            string.IsNullOrWhiteSpace(Phase2YearsJsonDir)) return;

        IsPhase2Running      = true;
        Phase2DryRunReviewed = false;
        Phase2StatusMessage  = "Running dry run…";
        try
        {
            var importer         = new Phase2Importer(_db);
            Phase2StatusMessage  = await importer.RunAsync(Phase2UnitsJsonDir, Phase2YearsJsonDir, dryRun: true);
            Phase2DryRunReviewed = true;
        }
        catch (Exception ex)
        {
            Phase2StatusMessage  = $"Dry run failed:\n{ex.Message}";
            Phase2DryRunReviewed = false;
        }
        finally { IsPhase2Running = false; }
    }

    /// <summary>
    /// Runs <see cref="Phase2Importer"/> for real, writing data to the database.
    /// Only enabled after a dry run has completed successfully.
    /// After importing, the dry-run-reviewed flag is reset so the user must
    /// re-run dry run before importing a second time.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportPhase2))]
    private async Task Phase2Import()
    {
        if (string.IsNullOrWhiteSpace(Phase2UnitsJsonDir) ||
            string.IsNullOrWhiteSpace(Phase2YearsJsonDir)) return;

        IsPhase2Running      = true;
        Phase2DryRunReviewed = false;   // require re-dry-run before next import
        Phase2StatusMessage  = "Importing…";
        try
        {
            var importer        = new Phase2Importer(_db);
            Phase2StatusMessage = await importer.RunAsync(Phase2UnitsJsonDir, Phase2YearsJsonDir, dryRun: false);
        }
        catch (Exception ex)
        {
            Phase2StatusMessage = $"Import failed:\n{ex.Message}";
        }
        finally { IsPhase2Running = false; }
    }

    private bool CanRunPhase2()    => !IsPhase2Running
                                   && !string.IsNullOrWhiteSpace(Phase2UnitsJsonDir)
                                   && !string.IsNullOrWhiteSpace(Phase2YearsJsonDir);

    private bool CanImportPhase2() => !IsPhase2Running
                                   && Phase2DryRunReviewed
                                   && !string.IsNullOrWhiteSpace(Phase2UnitsJsonDir)
                                   && !string.IsNullOrWhiteSpace(Phase2YearsJsonDir);

    // ── Shared helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a .csv file picker and invokes <paramref name="onChosen"/>
    /// with the selected local path if the user did not cancel.
    /// </summary>
    /// <param name="title">Title shown in the dialog.</param>
    /// <param name="onChosen">Action invoked with the chosen path.</param>
    private async Task BrowseCsvAsync(string title, System.Action<string> onChosen)
    {
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = title,
            AllowMultiple  = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*"  } }
            }
        });

        if (files.Count > 0)
            onChosen(files[0].Path.LocalPath);
    }

    /// <summary>
    /// Opens a folder picker and returns the chosen local path, or null if cancelled.
    /// </summary>
    /// <param name="title">Title shown in the dialog.</param>
    private async Task<string?> BrowseFolderAsync(string title)
    {
        var window = _mainVm.MainWindowReference;
        if (window is null) return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    /// <summary>
    /// Returns a "json-output" subfolder path beside the given CSV file.
    /// </summary>
    /// <param name="csvPath">Full path of the source CSV.</param>
    private static string OutputDirFor(string csvPath)
        => Path.Combine(Path.GetDirectoryName(csvPath) ?? string.Empty, "json-output");

#endif
}

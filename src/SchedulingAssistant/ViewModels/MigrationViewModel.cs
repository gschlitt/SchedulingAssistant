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
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Migration;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
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
    private readonly IDatabaseContext    _db;
    private readonly SemesterContext     _semesterContext;

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
    /// <param name="semesterContext">Singleton semester context; reloaded after a real import.</param>
    public MigrationViewModel(MainWindowViewModel mainVm, IDatabaseContext db, SemesterContext semesterContext)
    {
        _mainVm          = mainVm;
        _db              = db;
        _semesterContext = semesterContext;
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

            // Refresh the top-bar academic year and semester dropdowns so the
            // imported data is immediately visible without restarting the app.
            _semesterContext.Reload(new AcademicYearRepository(_db), new SemesterRepository(_db));
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

    // ── Section 4: Meeting-time validator ─────────────────────────────────────

    /// <summary>Sections with meetings that violate the legal block-length/start-time matrix.</summary>
    [ObservableProperty] private ObservableCollection<InvalidMeetingItemViewModel> _invalidMeetingItems = new();

    /// <summary>Status message for the meeting scan or apply pass.</summary>
    [ObservableProperty] private string? _meetingScanStatus;

    /// <summary>True while a scan or apply operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanInvalidMeetingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyMeetingFixesCommand))]
    private bool _isMeetingOpRunning;

    /// <summary>
    /// Scans every section in the current database against its academic year's legal
    /// block-length / start-time matrix.  Populates <see cref="InvalidMeetingItems"/>
    /// with any sections that have meetings that don't fit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunMeetingOp))]
    private async Task ScanInvalidMeetings()
    {
        IsMeetingOpRunning = true;
        MeetingScanStatus  = "Scanning…";
        InvalidMeetingItems.Clear();

        try
        {
            var results = await Task.Run(() =>
            {
                var semRepo     = new SemesterRepository(_db);
                var sectionRepo = new SectionRepository(_db);
                var legalRepo   = new LegalStartTimeRepository(_db);
                var courseRepo  = new CourseRepository(_db);

                // Build a CalendarCode lookup so we can show "FLOW 101" in the label.
                var codeById = courseRepo.GetAll()
                    .ToDictionary(c => c.Id, c => c.CalendarCode);

                // Load all semesters so we know each semester's parent AY.
                var semesters = semRepo.GetAll();

                // Cache legal times per AY to avoid repeated DB hits.
                var legalByAy      = new Dictionary<string, List<LegalStartTime>>();
                var skippedAyNames = new List<string>();   // AYs with no legal times defined

                var found = new List<InvalidMeetingItemViewModel>();

                foreach (var sem in semesters)
                {
                    if (!legalByAy.TryGetValue(sem.AcademicYearId, out var legal))
                    {
                        legal = legalRepo.GetAll(sem.AcademicYearId);
                        legalByAy[sem.AcademicYearId] = legal;

                        if (legal.Count == 0)
                        {
                            // Track by AY id so we report the name only once.
                            skippedAyNames.Add(sem.AcademicYearId);
                        }
                    }

                    if (legal.Count == 0) continue;  // no matrix defined — skip this semester

                    // Build the validity matrix for fast lookup.
                    var matrix = legal.ToDictionary(
                        lt => lt.BlockLength,
                        lt => new HashSet<int>(lt.StartTimes));

                    foreach (var section in sectionRepo.GetAll(sem.Id))
                    {
                        if (section.Schedule.Count == 0) continue;

                        var bad = section.Schedule
                            .Where(m =>
                            {
                                var bl = m.DurationMinutes / 60.0;
                                return !matrix.TryGetValue(bl, out var valid) || !valid.Contains(m.StartMinutes);
                            })
                            .ToList();

                        if (bad.Count == 0) continue;

                        var courseCode = section.CourseId is not null
                            && codeById.TryGetValue(section.CourseId, out var cc) ? cc : "?";
                        var label = $"{courseCode} {section.SectionCode}  ({sem.Name})";

                        found.Add(new InvalidMeetingItemViewModel(section, bad, legal, label));
                    }
                }

                return (found, skippedAyNames);
            });

            foreach (var item in results.found)
                InvalidMeetingItems.Add(item);

            var sb = new System.Text.StringBuilder();
            if (results.skippedAyNames.Count > 0)
                sb.AppendLine(
                    $"⚠  {results.skippedAyNames.Count} academic year(s) were skipped because " +
                    "no legal start-time matrix is defined for them yet. " +
                    "Use 'Seed Legal Start Times' below to add the defaults, then re-scan.");

            if (results.found.Count == 0)
                sb.Append(results.skippedAyNames.Count == 0 ? "✓  No invalid meetings found." : "");
            else
                sb.Append($"Found {results.found.Count} section(s) with invalid meetings. " +
                           "Choose an action for each, then click Apply Fixes.");

            MeetingScanStatus = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            MeetingScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsMeetingOpRunning = false;
        }
    }

    /// <summary>
    /// Applies the chosen remediation action for each item in <see cref="InvalidMeetingItems"/>:
    /// <list type="bullet">
    ///   <item><description><b>DiscardBadMeetings</b> — removes only the offending meetings from the section.</description></item>
    ///   <item><description><b>ReviseTime</b> — replaces all offending meetings with the chosen block length and start time (days preserved).</description></item>
    ///   <item><description><b>DiscardSection</b> — deletes the entire section from the database.</description></item>
    /// </list>
    /// Clears <see cref="InvalidMeetingItems"/> on success.  Re-run Scan to verify.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunMeetingOp))]
    private async Task ApplyMeetingFixes()
    {
        if (InvalidMeetingItems.Count == 0) return;

        IsMeetingOpRunning = true;
        MeetingScanStatus  = "Applying fixes…";

        try
        {
            var snapshot = InvalidMeetingItems.ToList();

            await Task.Run(() =>
            {
                var sectionRepo     = new SectionRepository(_db);
                // Guard against a section appearing more than once (e.g. multiple bad meetings
                // each creating a row) after a DiscardSection action has already removed it.
                var deletedIds = new HashSet<string>();

                foreach (var item in snapshot)
                {
                    if (deletedIds.Contains(item.Section.Id)) continue;

                    switch (item.SelectedAction)
                    {
                        case InvalidMeetingItemViewModel.ActionChoice.DiscardSection:
                            sectionRepo.Delete(item.Section.Id);
                            deletedIds.Add(item.Section.Id);
                            break;

                        case InvalidMeetingItemViewModel.ActionChoice.DiscardBadMeetings:
                            // Use identity comparison — these are the same object references
                            // collected during the scan.
                            item.Section.Schedule.RemoveAll(m => item.BadMeetings.Contains(m));
                            sectionRepo.Update(item.Section);
                            break;

                        case InvalidMeetingItemViewModel.ActionChoice.ReviseTime:
                            if (item.SelectedBlockLength is null || item.SelectedStartTime is null) break;
                            var newDuration = (int)(item.SelectedBlockLength.Value * 60);
                            foreach (var m in item.BadMeetings)
                            {
                                m.StartMinutes    = item.SelectedStartTime.Value;
                                m.DurationMinutes = newDuration;
                            }
                            sectionRepo.Update(item.Section);
                            break;
                    }
                }
            });

            InvalidMeetingItems.Clear();
            MeetingScanStatus = "✓  Fixes applied. Re-scan to verify there are no remaining issues.";
        }
        catch (Exception ex)
        {
            MeetingScanStatus = $"Apply failed: {ex.Message}";
        }
        finally
        {
            IsMeetingOpRunning = false;
        }
    }

    private bool CanRunMeetingOp() => !IsMeetingOpRunning;

    /// <summary>
    /// Seeds the default legal block-length / start-time matrix for every academic
    /// year that currently has no entries in the LegalStartTimes table.
    /// This is needed for databases that were imported before automatic seeding was
    /// added to the importer.  Safe to run multiple times — uses INSERT OR IGNORE.
    /// After seeding, re-run Scan so the validator has a matrix to check against.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunMeetingOp))]
    private async Task SeedMissingLegalStartTimes()
    {
        IsMeetingOpRunning = true;
        MeetingScanStatus  = "Seeding legal start times…";

        try
        {
            var seeded = await Task.Run(() =>
            {
                var ayRepo    = new AcademicYearRepository(_db);
                var legalRepo = new LegalStartTimeRepository(_db);
                int count     = 0;

                foreach (var ay in ayRepo.GetAll())
                {
                    var existing = legalRepo.GetAll(ay.Id);
                    if (existing.Count > 0) continue;  // already has a matrix — skip

                    SeedData.SeedDefaultLegalStartTimes(_db.Connection, ay.Id);
                    count++;
                }

                return count;
            });

            MeetingScanStatus = seeded == 0
                ? "✓  All academic years already have legal start times defined."
                : $"✓  Seeded defaults for {seeded} academic year(s). Re-scan to validate meetings.";
        }
        catch (Exception ex)
        {
            MeetingScanStatus = $"Seeding failed: {ex.Message}";
        }
        finally
        {
            IsMeetingOpRunning = false;
        }
    }

#endif
}

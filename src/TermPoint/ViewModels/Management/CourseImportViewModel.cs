using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;

#if !BROWSER
using Avalonia.Platform.Storage;
#endif

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Sub-ViewModel for the course section of the CSV Import flyout.
/// Handles file selection, subject mapping confirmation, course matching
/// against existing records, and transactional import.
/// </summary>
public partial class CourseImportViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainVm;
    private readonly ICourseRepository _courseRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IDatabaseContext _db;
    private readonly CsvImportParser _parser;
    private readonly CsvImportMatcher _matcher;
    private readonly Action<string> _addLog;

    private List<CourseRow> _parsedRows = new();

    /// <summary>Display name of the chosen CSV file.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMappingsCommand))]
    private string? _fileName;

    /// <summary>True after a successful import.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseFileCommand))]
    private bool _isImported;

    /// <summary>True after the operator confirms subject mappings — shows the course preview.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMappingsCommand))]
    private bool _isMappingConfirmed;

    /// <summary>Parse or import error text shown as a warning banner.</summary>
    [ObservableProperty] private string? _errorBanner;

    /// <summary>Summary text shown after import completes.</summary>
    [ObservableProperty] private string? _importSummary;

    /// <summary>True when a file has been loaded and subject mappings are shown.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMappingsCommand))]
    private bool _hasFile;

    /// <summary>Subject mapping rows — one per distinct SubjectCode in the CSV.</summary>
    public ObservableCollection<MappingEntryViewModel> SubjectMappings { get; } = new();

    /// <summary>Course preview rows — populated after subject mappings are confirmed.</summary>
    public ObservableCollection<CoursePreviewRow> PreviewRows { get; } = new();

    [ObservableProperty] private int _newCount;
    [ObservableProperty] private int _matchedCount;
    [ObservableProperty] private int _rejectedCount;

    public CourseImportViewModel(
        MainWindowViewModel mainVm,
        ICourseRepository courseRepo,
        ISubjectRepository subjectRepo,
        IDatabaseContext db,
        CsvImportParser parser,
        CsvImportMatcher matcher,
        Action<string> addLog)
    {
        _mainVm = mainVm;
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;
        _db = db;
        _parser = parser;
        _matcher = matcher;
        _addLog = addLog;
    }

    /// <summary>
    /// Opens a file picker, parses the CSV, scans distinct SubjectCode values,
    /// and builds the subject mapping table.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChooseFile))]
    private async Task ChooseFile()
    {
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        await Task.Yield();

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select course CSV",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        FileName = Path.GetFileName(path);
        ErrorBanner = null;
        SubjectMappings.Clear();
        PreviewRows.Clear();
        IsMappingConfirmed = false;
        _parsedRows.Clear();

        string csvText;
        try
        {
            // Deadline-bounded read: the picked file may live on a network share, and a
            // raw read against a dead share silently hangs the import for the SMB
            // redirector timeout. Onboarding CSVs are small, so the standard deadline
            // is generous.
            var (completed, text) = await NetworkFileOps.ReadAllTextAsync(path);
            if (!completed || text is null)
            {
                ErrorBanner = "Could not read file: the location is not responding. " +
                              "Copy the file to a local folder and try again.";
                return;
            }
            csvText = text;
        }
        catch (Exception ex)
        {
            ErrorBanner = $"Could not read file: {ex.Message}";
            return;
        }

        var parseResult = _parser.ParseCourses(csvText);

        if (parseResult.Errors.Count > 0)
        {
            var errorLines = parseResult.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}");
            ErrorBanner = string.Join("\n", errorLines);
        }

        if (parseResult.Rows.Count == 0 && parseResult.Errors.Count > 0)
            return;

        _parsedRows = parseResult.Rows;

        // Build subject mapping table from distinct SubjectCode values.
        var subjects = _subjectRepo.GetAll();
        var subjectIndex = subjects
            .Where(s => !string.IsNullOrWhiteSpace(s.CalendarAbbreviation))
            .ToDictionary(
                s => NormalizeWhitespace(s.CalendarAbbreviation),
                s => s,
                StringComparer.OrdinalIgnoreCase);

        // Build EnvironmentTarget list from subjects for the ComboBox.
        var subjectOptions = subjects
            .Select(s => new EnvironmentTarget { Id = s.Id, DisplayName = $"{s.CalendarAbbreviation} — {s.Name}" })
            .ToList();

        var distinctCodes = _parsedRows
            .Select(r => r.SubjectCode.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var code in distinctCodes)
        {
            var match = _matcher.MatchSubject(code, subjectIndex);
            var envMatch = new MatchResult<EnvironmentTarget>
            {
                CsvValue = code,
                Status = match.Status,
                Resolved = match.Resolved is not null
                    ? new EnvironmentTarget { Id = match.Resolved.Id, DisplayName = $"{match.Resolved.CalendarAbbreviation} — {match.Resolved.Name}" }
                    : null
            };
            SubjectMappings.Add(new MappingEntryViewModel(code, envMatch, subjectOptions));
        }

        HasFile = true;

        _addLog($"Loaded {_parsedRows.Count} course(s) from {FileName}" +
                (parseResult.Errors.Count > 0 ? $" ({parseResult.Errors.Count} parse error(s))" : ""));
#endif
    }

    private bool CanChooseFile() => !IsImported;

    /// <summary>
    /// Locks subject mappings and builds the course preview list. Courses whose
    /// SubjectCode has no mapping are marked as rejected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirmMappings))]
    private void ConfirmMappings()
    {
        PreviewRows.Clear();

        // Build a lookup from CSV SubjectCode → resolved Subject ID.
        var subjectMap = new Dictionary<string, (string? Id, string Display)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SubjectMappings)
        {
            var target = entry.ResolvedTarget;
            subjectMap[entry.CsvValue] = target is not null
                ? (target.Id, target.DisplayName)
                : (null, "(unmapped)");
        }

        // Match courses against existing DB courses.
        var existingCourses = _courseRepo.GetAll();
        var courseIndex = existingCourses
            .ToDictionary(
                c => NormalizeWhitespace(c.CalendarCode),
                c => c,
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in _parsedRows)
        {
            var courseMatch = _matcher.MatchCourse(row.CalendarCode, courseIndex);

            var code = row.SubjectCode.Trim();
            string? subjectId = null;
            var subjectDisplay = "(none)";

            if (!string.IsNullOrEmpty(code) && subjectMap.TryGetValue(code, out var mapped))
            {
                subjectId = mapped.Id;
                subjectDisplay = mapped.Display;
            }
            else if (string.IsNullOrEmpty(code))
            {
                subjectDisplay = "(blank in CSV)";
            }

            PreviewRows.Add(new CoursePreviewRow(row, courseMatch, subjectId, subjectDisplay));
        }

        IsMappingConfirmed = true;
        RecalculateCounts();

        _addLog($"Subject mappings confirmed. {NewCount} new, {MatchedCount} matched, {RejectedCount} rejected.");
    }

    private bool CanConfirmMappings() => HasFile && !IsMappingConfirmed;

    /// <summary>
    /// Imports new courses into the database. Skips matched and rejected rows.
    /// Checks <see cref="ICourseRepository.ExistsByCalendarCode"/> for dedup.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        var created = 0;
        var skipped = 0;
        var rejected = 0;

        void DoInserts()
        {
            foreach (var preview in PreviewRows)
            {
                if (preview.Status == MatchStatus.Exact)
                {
                    skipped++;
                    continue;
                }

                if (preview.IsRejected)
                {
                    rejected++;
                    continue;
                }

                // Final dedup check against the DB.
                if (_courseRepo.ExistsByCalendarCode(preview.CalendarCode))
                {
                    skipped++;
                    continue;
                }

                var course = new Course
                {
                    Id = Guid.NewGuid().ToString(),
                    SubjectId = preview.SubjectId!,
                    CalendarCode = preview.CalendarCode,
                    Title = preview.Title,
                    Level = CourseLevelParser.ParseLevel(preview.CalendarCode) ?? string.Empty,
                    IsActive = true
                };

                _courseRepo.Insert(course);
                created++;
            }
        }

        try
        {
            if (_db.SupportsTransactions)
            {
                using var tx = _db.Connection.BeginTransaction();
                try
                {
                    DoInserts();
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            else
            {
                DoInserts();
            }

            IsImported = true;
            ImportSummary = $"Imported {created} new, {skipped} skipped, {rejected} rejected.";
            _addLog($"Course import complete: {created} created, {skipped} skipped, {rejected} rejected.");
        }
        catch (Exception ex)
        {
            ErrorBanner = $"Import failed: {ex.Message}";
            _addLog($"Course import FAILED: {ex.Message}");
        }
    }

    private bool CanImport() => IsMappingConfirmed && !IsImported;

    private void RecalculateCounts()
    {
        NewCount = PreviewRows.Count(r => r.Status == MatchStatus.Unmatched && !r.IsRejected);
        MatchedCount = PreviewRows.Count(r => r.Status == MatchStatus.Exact);
        RejectedCount = PreviewRows.Count(r => r.IsRejected);
    }

    private static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

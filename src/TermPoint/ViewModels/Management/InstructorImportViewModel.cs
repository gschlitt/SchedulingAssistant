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
/// Sub-ViewModel for the instructor section of the CSV Import flyout.
/// Handles file selection, parsing, matching against existing instructors,
/// ambiguity resolution, and transactional import.
/// </summary>
public partial class InstructorImportViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainVm;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IDatabaseContext _db;
    private readonly CsvImportParser _parser;
    private readonly CsvImportMatcher _matcher;
    private readonly Action<string> _addLog;

    /// <summary>Display name of the chosen CSV file.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string? _fileName;

    /// <summary>True after a successful import — disables the import button and shows a summary.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseFileCommand))]
    private bool _isImported;

    /// <summary>Parse error text shown as a warning banner. Null when no errors.</summary>
    [ObservableProperty] private string? _errorBanner;

    /// <summary>Summary text shown after import completes (e.g. "Imported 22 instructors").</summary>
    [ObservableProperty] private string? _importSummary;

    /// <summary>Preview rows populated after file selection and matching.</summary>
    public ObservableCollection<InstructorPreviewRow> PreviewRows { get; } = new();

    /// <summary>Number of rows that will be created (Unmatched status).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _newCount;

    /// <summary>Number of rows that matched existing instructors (Exact status).</summary>
    [ObservableProperty] private int _matchedCount;

    /// <summary>Number of rows with ambiguous matches still unresolved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _ambiguousCount;

    /// <summary>Number of rows the operator chose to skip.</summary>
    [ObservableProperty] private int _skippedCount;

    /// <summary>True when a file has been loaded (preview rows are populated).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _hasFile;

    public InstructorImportViewModel(
        MainWindowViewModel mainVm,
        IInstructorRepository instructorRepo,
        IDatabaseContext db,
        CsvImportParser parser,
        CsvImportMatcher matcher,
        Action<string> addLog)
    {
        _mainVm = mainVm;
        _instructorRepo = instructorRepo;
        _db = db;
        _parser = parser;
        _matcher = matcher;
        _addLog = addLog;
    }

    /// <summary>
    /// Opens a file picker for a CSV file, parses it, matches each row against
    /// existing instructors, and populates <see cref="PreviewRows"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChooseFile))]
    private async Task ChooseFile()
    {
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        // Yield so the menu popup closes before the native picker runs.
        await Task.Yield();

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select instructor CSV",
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
        PreviewRows.Clear();

        string csvText;
        try
        {
            // Deadline-bounded read — see CourseImportViewModel.ChooseFile for rationale.
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

        var parseResult = _parser.ParseInstructors(csvText);

        if (parseResult.Errors.Count > 0)
        {
            var errorLines = parseResult.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}");
            ErrorBanner = string.Join("\n", errorLines);
        }

        if (parseResult.Rows.Count == 0 && parseResult.Errors.Count > 0)
            return;

        // Match each parsed row against existing instructors.
        var existing = _instructorRepo.GetAll();
        var index = _matcher.BuildInstructorIndex(existing);

        foreach (var row in parseResult.Rows)
        {
            var match = _matcher.MatchInstructor(row, index);
            var previewRow = new InstructorPreviewRow(row, match);

            // Listen for ambiguity resolution changes to update counts.
            previewRow.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(InstructorPreviewRow.ResolvedMatch)
                    or nameof(InstructorPreviewRow.Skip))
                    RecalculateCounts();
            };

            PreviewRows.Add(previewRow);
        }

        HasFile = true;
        RecalculateCounts();

        _addLog($"Loaded {parseResult.Rows.Count} instructor(s) from {FileName}" +
                (parseResult.Errors.Count > 0 ? $" ({parseResult.Errors.Count} parse error(s))" : ""));
#endif
    }

    private bool CanChooseFile() => !IsImported;

    /// <summary>
    /// Imports new instructors into the database. Skips matched/skipped rows.
    /// Ambiguous rows with a resolved match are treated as matched (reuse existing).
    /// Wraps all inserts in a single transaction.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        var created = 0;
        var skipped = 0;

        void DoInserts()
        {
            foreach (var preview in PreviewRows)
            {
                // Skip matched rows — the instructor already exists.
                if (preview.Status == MatchStatus.Exact)
                {
                    skipped++;
                    continue;
                }

                // Skip rows the operator chose to skip.
                if (preview.Skip)
                {
                    skipped++;
                    continue;
                }

                // Ambiguous row resolved to an existing instructor — skip.
                if (preview.Status == MatchStatus.Ambiguous && preview.ResolvedMatch is not null)
                {
                    skipped++;
                    continue;
                }

                // Create a new instructor from the parsed row.
                var row = preview.Row;
                var instructor = new Instructor
                {
                    Id = Guid.NewGuid().ToString(),
                    LastName = row.LastName.Trim(),
                    FirstName = row.FirstName.Trim(),
                    Email = row.Email.Trim(),
                    IsActive = true,
                    Initials = ResolveInitials(row)
                };

                _instructorRepo.Insert(instructor);
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
            ImportSummary = $"Imported {created} new, {skipped} skipped.";
            _addLog($"Instructor import complete: {created} created, {skipped} skipped.");
        }
        catch (Exception ex)
        {
            ErrorBanner = $"Import failed: {ex.Message}";
            _addLog($"Instructor import FAILED: {ex.Message}");
        }
    }

    private bool CanImport() => HasFile && !IsImported && AmbiguousCount == 0;

    /// <summary>
    /// Resolves initials for a new instructor. Uses the CSV value if provided,
    /// otherwise auto-generates from first/last name with collision avoidance.
    /// </summary>
    private string ResolveInitials(InstructorRow row)
    {
        var csvInitials = row.Initials.Trim();
        if (!string.IsNullOrEmpty(csvInitials) && !_instructorRepo.ExistsByInitials(csvInitials))
            return csvInitials;

        // Auto-generate: first letter of first name + first letter of last name.
        var first = row.FirstName.Trim();
        var last = row.LastName.Trim();

        string baseInitials;
        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
            baseInitials = $"{char.ToUpper(first[0])}{char.ToUpper(last[0])}";
        else if (!string.IsNullOrEmpty(last) && last.Length >= 2)
            baseInitials = $"{char.ToUpper(last[0])}{char.ToUpper(last[1])}";
        else if (!string.IsNullOrEmpty(last))
            baseInitials = $"{char.ToUpper(last[0])}";
        else
            baseInitials = "XX";

        if (!_instructorRepo.ExistsByInitials(baseInitials))
            return baseInitials;

        // Append digits until unique.
        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseInitials}{i}";
            if (!_instructorRepo.ExistsByInitials(candidate))
                return candidate;
        }

        return $"{baseInitials}{Guid.NewGuid().ToString()[..4]}";
    }

    /// <summary>Recalculates summary counts from the current preview rows.</summary>
    private void RecalculateCounts()
    {
        var newCount = 0;
        var matchedCount = 0;
        var ambiguousCount = 0;
        var skippedCount = 0;

        foreach (var row in PreviewRows)
        {
            if (row.Skip)
            {
                skippedCount++;
                continue;
            }

            switch (row.Status)
            {
                case MatchStatus.Exact:
                    matchedCount++;
                    break;
                case MatchStatus.Unmatched:
                    newCount++;
                    break;
                case MatchStatus.Ambiguous:
                    if (row.ResolvedMatch is not null)
                        matchedCount++;
                    else
                        ambiguousCount++;
                    break;
            }
        }

        NewCount = newCount;
        MatchedCount = matchedCount;
        AmbiguousCount = ambiguousCount;
        SkippedCount = skippedCount;
    }
}

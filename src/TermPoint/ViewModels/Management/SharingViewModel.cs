using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Flyout for shared schedule import/export operations.
/// Provides Import, Export, Set Shared Folder, and Dismiss All actions.
/// </summary>
public partial class SharingViewModel : ViewModelBase
{
    private readonly SharedScheduleService _sharedScheduleService;
    private readonly SharedScheduleCsvParser _parser;
    private readonly SharedScheduleCsvExporter _exporter;
    private readonly ImportResolver _resolver;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ICampusRepository _campusRepo;
    private readonly ISchedulingEnvironmentRepository _envRepo;
    private readonly SemesterContext _semesterContext;
    private readonly AcademicUnitService _academicUnitService;
    private readonly SectionStore _sectionStore;
    private readonly MainWindowViewModel _mainVm;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _sharedFolderDisplay;
    [ObservableProperty] private string _exportSourceLabel = string.Empty;

    public SharingViewModel(
        SharedScheduleService sharedScheduleService,
        SharedScheduleCsvParser parser,
        SharedScheduleCsvExporter exporter,
        ImportResolver resolver,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IRoomRepository roomRepo,
        ICampusRepository campusRepo,
        ISchedulingEnvironmentRepository envRepo,
        SemesterContext semesterContext,
        AcademicUnitService academicUnitService,
        SectionStore sectionStore,
        MainWindowViewModel mainVm)
    {
        _sharedScheduleService = sharedScheduleService;
        _parser = parser;
        _exporter = exporter;
        _resolver = resolver;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _campusRepo = campusRepo;
        _envRepo = envRepo;
        _semesterContext = semesterContext;
        _academicUnitService = academicUnitService;
        _sectionStore = sectionStore;
        _mainVm = mainVm;

        UpdateSharedFolderDisplay();
        UpdateExportSourceLabel();
        _sharedScheduleService.Changed += () => OnPropertyChanged(nameof(HasLoadedSchedules));
        _semesterContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SemesterContext.SelectedSemesters))
            {
                OnPropertyChanged(nameof(IsSingleSemester));
                ExportSharedScheduleCommand.NotifyCanExecuteChanged();
            }
        };
    }

    /// <summary>
    /// True when exactly one semester is selected. Import/export require a single semester
    /// so shared sections can be stamped with a definite semester ID.
    /// </summary>
    public bool IsSingleSemester => _semesterContext.SelectedSemesters.Count == 1;

    private bool CanExportSharedSchedule() => !string.IsNullOrWhiteSpace(ExportSourceLabel) && IsSingleSemester;

    partial void OnExportSourceLabelChanged(string value) => ExportSharedScheduleCommand.NotifyCanExecuteChanged();

    /// <summary>True when at least one shared schedule is loaded.</summary>
    public bool HasLoadedSchedules => _sharedScheduleService.HasAny;

    /// <summary>Summary of loaded shared schedules for display.</summary>
    public string LoadedSummary
    {
        get
        {
            if (!_sharedScheduleService.HasAny) return "No shared schedules loaded.";
            var sets = _sharedScheduleService.Sets;
            var parts = sets.Select(s => $"{s.SourceLabel} ({s.Sections.Count})");
            return $"Loaded: {string.Join(" · ", parts)}";
        }
    }

    public bool SupportsFileDialogs => PlatformCapabilities.SupportsFileDialogs;

    [RelayCommand]
    private async Task ImportSharedSchedule()
    {
        if (!PlatformCapabilities.SupportsFileDialogs)
        {
            StatusMessage = "File import is not available in the browser demo.";
            return;
        }

        StatusMessage = null;
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        var storageProvider = window.StorageProvider;
        var startFolder = await GetStartFolder(storageProvider);

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Shared Schedule",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } } },
            SuggestedStartLocation = startFolder
        });

        if (files.Count == 0) return;

        var file = files[0];
        var fallbackLabel = System.IO.Path.GetFileNameWithoutExtension(file.Name);

        // Shared-schedule CSVs live on a network folder by design, and the parser
        // consumes its stream synchronously on the UI thread — a share dying mid-read
        // would freeze the app for the SMB redirector timeout. Read the raw bytes
        // deadline-bounded first, then parse from memory (bytes, not text, so the
        // parser's BOM/encoding detection still applies).
        var localPath = file.TryGetLocalPath();
        System.IO.Stream stream;
        if (!string.IsNullOrEmpty(localPath))
        {
            var (completed, bytes) = await Services.NetworkFileOps.RunAsync(
                () => System.IO.File.ReadAllBytes(localPath), "SharedSchedule.Read");
            if (!completed || bytes is null)
            {
                StatusMessage = "The file's location is not responding. Check your network connection and try again.";
                return;
            }
            stream = new System.IO.MemoryStream(bytes);
        }
        else
        {
            // Non-filesystem storage provider — no UNC path to stall on.
            stream = await file.OpenReadAsync();
        }

        Services.ImportResult result;
        await using (stream)
            result = _parser.Parse(stream, fallbackLabel);

        if (result.FileError is not null)
        {
            StatusMessage = result.FileError;
            return;
        }

        var set = result.Set!;
        var sections = set.Sections;

        // Resolve imported names against local entities
        var index = ImportResolutionIndex.Build(
            _instructorRepo.GetAll(),
            _roomRepo.GetAll(),
            _campusRepo.GetAll(),
            _envRepo.GetAll("sectionType"),
            _envRepo.GetAll("tag"),
            _envRepo.GetAll("meetingType"));

        var summary = _resolver.Resolve(sections, index);
        set.ResolutionSummary = summary;

        // Stamp active semester ID on all imported sections
        var activeSemester = _semesterContext.SelectedSemesters.FirstOrDefault();
        if (activeSemester is not null)
        {
            var semesterId = activeSemester.Semester.Id;
            foreach (var s in sections)
                s.SemesterId = semesterId;
        }

        _sharedScheduleService.Add(set);
        OnPropertyChanged(nameof(LoadedSummary));

        // Build multi-line status message
        var lines = new List<string>();

        if (result.SkippedRows > 0)
            lines.Add($"Imported {result.TotalRows - result.SkippedRows} of {result.TotalRows} rows ({result.SkippedRows} skipped) from {set.SourceLabel}.");
        else
            lines.Add($"Imported {sections.Count} sections from {set.SourceLabel}.");

        var matched = new List<string>();
        if (summary.ResolvedInstructorCount > 0)
            matched.Add($"{summary.ResolvedInstructorCount} instructor{(summary.ResolvedInstructorCount == 1 ? "" : "s")}");
        if (summary.ResolvedRoomCount > 0)
            matched.Add($"{summary.ResolvedRoomCount} room{(summary.ResolvedRoomCount == 1 ? "" : "s")}");
        if (summary.ResolvedTagCount > 0)
            matched.Add($"{summary.ResolvedTagCount} tag{(summary.ResolvedTagCount == 1 ? "" : "s")}");
        if (matched.Count > 0)
            lines.Add($"Matched: {string.Join(", ", matched)}.");

        // Semester mismatch warning
        if (result.SemesterName is not null && activeSemester is not null
            && !string.Equals(result.SemesterName, activeSemester.Semester.Name, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Note: file was exported for \"{result.SemesterName}\" but the active semester is \"{activeSemester.Semester.Name}\".");
        }

        foreach (var w in summary.Warnings)
            lines.Add(w);

        StatusMessage = string.Join("\n", lines);
#endif
    }

    [RelayCommand(CanExecute = nameof(CanExportSharedSchedule))]
    private async Task ExportSharedSchedule()
    {
        if (!PlatformCapabilities.SupportsFileDialogs)
        {
            StatusMessage = "File export is not available in the browser demo.";
            return;
        }

        StatusMessage = null;
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        var semesters = _semesterContext.SelectedSemesters;
        if (semesters.Count == 0)
        {
            StatusMessage = "No semester selected.";
            return;
        }

        // Get sections in the current semester, restricted to the active grid filter if any
        var semesterIds = semesters.Select(s => s.Semester.Id).ToHashSet();
        var sections = _sectionRepo.GetAll()
            .Where(s => !string.IsNullOrEmpty(s.SemesterId) && semesterIds.Contains(s.SemesterId))
            .ToList();

        var filteredIds = _sectionStore.FilteredSectionIds;
        bool isFiltered = filteredIds is not null;
        if (isFiltered)
            sections = sections.Where(s => filteredIds!.Contains(s.Id)).ToList();

        if (sections.Count == 0)
        {
            StatusMessage = isFiltered
                ? "No sections match the current filter."
                : "No sections to export in the current semester.";
            return;
        }

        // Build course lookup
        var courses = _courseRepo.GetAll().ToDictionary(c => c.Id, c => c.CalendarCode ?? c.Id);

        // Default filename: derive from the source description, sanitized for the filesystem
        var sourceLabel = ExportSourceLabel.Trim();
        var ayName = _semesterContext.SelectedAcademicYear?.Name ?? "";
        var semName = semesters.First().Semester.Name;
        var safeName = SanitizeFileName($"{sourceLabel} {ayName} {semName}");
        var defaultName = string.IsNullOrWhiteSpace(safeName) ? "Shared Schedule.csv" : $"{safeName}.csv";

        var storageProvider = window.StorageProvider;
        var startFolder = await GetStartFolder(storageProvider);

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Shared Schedule",
            SuggestedFileName = defaultName,
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } } },
            SuggestedStartLocation = startFolder
        });

        if (file is null) return;

        var lookups = new ExportLookups(
            InstructorsById:  _instructorRepo.GetAll().ToDictionary(i => i.Id),
            RoomsById:        _roomRepo.GetAll().ToDictionary(r => r.Id),
            CampusesById:     _campusRepo.GetAll().ToDictionary(c => c.Id),
            SectionTypesById: _envRepo.GetAll("sectionType").ToDictionary(v => v.Id),
            TagsById:         _envRepo.GetAll("tag").ToDictionary(v => v.Id),
            MeetingTypesById: _envRepo.GetAll("meetingType").ToDictionary(v => v.Id));

        await using var stream = await file.OpenWriteAsync();
        var error = _exporter.Export(stream, sourceLabel, semName, sections,
            id => courses.GetValueOrDefault(id, id), lookups);

        if (error is not null)
            StatusMessage = error;
        else
            StatusMessage = $"Exported {sections.Count} sections{(isFiltered ? " (filtered)" : "")} to {file.Name}.";
#endif
    }

    [RelayCommand]
    private async Task SetSharedFolder()
    {
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        var storageProvider = window.StorageProvider;
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Set Shared Schedule Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (path is not null)
        {
            AppSettings.Current.SharedScheduleFolder = path;
            AppSettings.Current.Save();
            UpdateSharedFolderDisplay();
        }
#endif
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void DismissAll()
    {
        _sharedScheduleService.DismissAll();
        OnPropertyChanged(nameof(LoadedSummary));
        OnPropertyChanged(nameof(HasLoadedSchedules));
        StatusMessage = "All shared schedules dismissed.";
    }

    private void UpdateSharedFolderDisplay()
    {
        var folder = AppSettings.Current.SharedScheduleFolder;
        SharedFolderDisplay = string.IsNullOrEmpty(folder) ? "(not set)" : folder;
    }

    private void UpdateExportSourceLabel()
    {
        ExportSourceLabel = string.Empty;
    }

    /// <summary>
    /// Strips characters that are illegal in Windows/macOS filenames and collapses
    /// runs of whitespace into a single space.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        bool prevSpace = false;
        foreach (var ch in name)
        {
            if (Array.IndexOf(invalid, ch) >= 0 || ch == '~')
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

#if !BROWSER
    /// <summary>
    /// Resolves the configured shared-schedule folder as the picker's start location.
    /// The folder is typically on a network share, so the reachability probe is
    /// deadline-bounded (<see cref="Services.StorageProviderExtensions.TryGetReachableStartFolderAsync"/>) —
    /// an unreachable share degrades to the picker's default location instead of
    /// freezing the UI for the SMB redirector timeout.
    /// </summary>
    private Task<IStorageFolder?> GetStartFolder(IStorageProvider storageProvider)
        => storageProvider.TryGetReachableStartFolderAsync(AppSettings.Current.SharedScheduleFolder);
#endif
}

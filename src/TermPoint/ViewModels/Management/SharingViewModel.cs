using System.Collections.ObjectModel;
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
        RefreshRecentImports();
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

    /// <summary>
    /// The last few imported shared-schedule files (most recent first), for one-click
    /// re-import. Populated from <see cref="AppSettings.RecentSharedImports"/>; no
    /// existence check is performed here — validation is deferred to click time so an
    /// unreachable share never stalls the flyout.
    /// </summary>
    public ObservableCollection<RecentImportItem> RecentImports { get; } = new();

    /// <summary>True when at least one recent import is available to show.</summary>
    public bool HasRecentImports => RecentImports.Count > 0;

    /// <summary>
    /// Rebuilds <see cref="RecentImports"/> from persisted settings, wiring each item's
    /// load command. Called at construction and after any change to the recent list.
    /// </summary>
    private void RefreshRecentImports()
    {
        RecentImports.Clear();
        foreach (var path in AppSettings.Current.RecentSharedImports)
        {
            // Capture in a local so the command closes over this iteration's path.
            var capturedPath = path;
            RecentImports.Add(new RecentImportItem
            {
                Path = capturedPath,
                DisplayName = System.IO.Path.GetFileName(capturedPath),
                LoadCommand = new AsyncRelayCommand(() => LoadRecentImport(capturedPath))
            });
        }
        OnPropertyChanged(nameof(HasRecentImports));
    }

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
        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            // Filesystem path — read deadline-bounded and record for one-click re-import.
            await TryLoadFromLocalPathAsync(localPath);
        }
        else
        {
            // Non-filesystem storage provider — no UNC path to stall on, and nothing
            // re-loadable to record in the recent list.
            var fallbackLabel = System.IO.Path.GetFileNameWithoutExtension(file.Name);
            var stream = await file.OpenReadAsync();
            await using (stream)
                ProcessImportStream(stream, fallbackLabel);
        }
#endif
    }

#if !BROWSER
    /// <summary>
    /// Loads a shared schedule from a local (possibly network) file path, then records it
    /// in the recent-imports list on success. Shared-schedule CSVs live on a network folder
    /// by design, so the raw bytes are read deadline-bounded first (a share dying mid-read
    /// would otherwise freeze the app for the SMB redirector timeout) and parsed from memory.
    /// </summary>
    /// <param name="localPath">Full filesystem path to the CSV to import.</param>
    /// <returns>True when the file was read and parsed without a file-level error.</returns>
    private async Task<bool> TryLoadFromLocalPathAsync(string localPath)
    {
        var (completed, bytes) = await Services.NetworkFileOps.RunAsync(
            () => System.IO.File.ReadAllBytes(localPath), "SharedSchedule.Read");
        if (!completed || bytes is null)
        {
            StatusMessage = "The file's location is not responding. Check your network connection and try again.";
            return false;
        }

        // Bytes, not text, so the parser's BOM/encoding detection still applies.
        var fallbackLabel = System.IO.Path.GetFileNameWithoutExtension(localPath);
        bool success;
        using (var stream = new System.IO.MemoryStream(bytes))
            success = ProcessImportStream(stream, fallbackLabel);

        if (success)
        {
            // Move-to-front (cap 5) and refresh the visible list.
            AppSettings.Current.AddRecentSharedImport(localPath);
            RefreshRecentImports();
        }
        return success;
    }
#endif

    /// <summary>
    /// Parses an already-read shared-schedule stream, resolves imported names against local
    /// entities, stamps the active semester, registers the set, and builds the status
    /// message. Shared by the file-picker and recent-import paths. The stream is consumed
    /// synchronously; the caller owns its lifetime.
    /// </summary>
    /// <param name="stream">Open stream positioned at the start of the CSV.</param>
    /// <param name="fallbackSourceLabel">Label used when the CSV omits a source description.</param>
    /// <returns>True on a successful parse; false when the file had a parse-level error.</returns>
    private bool ProcessImportStream(System.IO.Stream stream, string fallbackSourceLabel)
    {
        var result = _parser.Parse(stream, fallbackSourceLabel);

        if (result.FileError is not null)
        {
            StatusMessage = result.FileError;
            return false;
        }

        var set = result.Set!;
        var sections = set.Sections;

        // Semester guard — runs before anything is added, resolved, or stamped. Sections are
        // stamped with the ACTIVE semester below regardless of where the file came from, so a
        // cross-semester import does not merely mislead, it mis-attributes the data. Refuse
        // outright rather than warn.
        var activeSemester = _semesterContext.SelectedSemesters.FirstOrDefault();
        var (refusal, semesterWarning) = CheckSemesterIdentity(
            result,
            activeSemester?.Semester.Name,
            _semesterContext.SelectedAcademicYear?.Name);

        if (refusal is not null)
        {
            StatusMessage = refusal;
            return false;
        }

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

        // A confident mismatch was already refused above; this only fires when the file does
        // not carry enough identity to verify (files predating the academic-year header).
        if (semesterWarning is not null)
            lines.Add(semesterWarning);

        foreach (var w in summary.Warnings)
            lines.Add(w);

        StatusMessage = string.Join("\n", lines);
        return true;
    }

    /// <summary>
    /// Decides whether a parsed file may be imported into the currently open semester.
    ///
    /// <para>Semester names are bare ("Fall", "Winter") with the year held separately, so the
    /// semester name alone cannot distinguish Fall 2026 from Fall 2027 — the academic year is
    /// the deciding field. The rule is "enforce when we can, warn when we can't": a mismatch we
    /// can positively establish is refused; a file that simply lacks the identity to check
    /// (exported before the academic year was recorded) is allowed with a visible warning.</para>
    ///
    /// <para>Only human-readable identity is compared. The file's semester ID belongs to the
    /// sending database and is meaningless here.</para>
    /// </summary>
    /// <param name="result">Parsed import result carrying the file's recorded identity.</param>
    /// <param name="activeSemesterName">Name of the open semester, or null if none is open.</param>
    /// <param name="activeAcademicYearName">Name of the open academic year, if any.</param>
    /// <returns>
    /// <c>Refusal</c> non-null when the import must be rejected (the message explains the way
    /// out); otherwise <c>Warning</c> non-null when the import may proceed but could not be
    /// verified. Both null when the file positively matches.
    /// </returns>
    internal static (string? Refusal, string? Warning) CheckSemesterIdentity(
        Services.ImportResult result, string? activeSemesterName, string? activeAcademicYearName)
    {
        // No semester open — the UI gates import on a single selected semester, so there is
        // nothing to compare against and nothing to protect.
        if (string.IsNullOrWhiteSpace(activeSemesterName))
            return (null, null);

        if (result.SemesterName is null)
            return (null, "Could not confirm which semester this file came from — check that it matches the semester you have open.");

        if (!Same(result.SemesterName, activeSemesterName))
            return (Refuse(), null);

        // Semester names match; the academic year decides whether it is the same year's term.
        if (result.AcademicYearName is null || string.IsNullOrWhiteSpace(activeAcademicYearName))
            return (null, $"This file is for \"{result.SemesterName}\" but does not record its academic year, so it could not be confirmed to match the year you have open.");

        if (!Same(result.AcademicYearName, activeAcademicYearName))
            return (Refuse(), null);

        return (null, null);

        static bool Same(string a, string b)
            => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        string Refuse()
        {
            var fileTerm = Describe(result.SemesterName, result.AcademicYearName);
            var openTerm = Describe(activeSemesterName, activeAcademicYearName);
            return $"This share is for {fileTerm}, but {openTerm} is open. "
                 + $"Switch to {fileTerm} to import it, or ask the sender to export from {openTerm}.";
        }

        static string Describe(string? semester, string? academicYear)
        {
            var term = (semester ?? "an unknown semester").Trim();
            return string.IsNullOrWhiteSpace(academicYear) ? term : $"{term} {academicYear.Trim()}";
        }
    }

    /// <summary>
    /// Re-imports a file from the recent list. Validation is lazy: the file is probed only
    /// now (not when the list is built), so an unreachable share never stalls the flyout.
    /// A genuinely missing file is reported and dropped from the list; an unreachable
    /// location is reported but kept (it may come back).
    /// </summary>
    /// <param name="path">Full path recorded in <see cref="AppSettings.RecentSharedImports"/>.</param>
    [RelayCommand]
    private async Task LoadRecentImport(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        StatusMessage = null;
#if !BROWSER
        // Deadline-bounded, tri-state probe: distinguishes "genuinely gone" from
        // "share down" so a dead link can't freeze the flyout and doesn't purge the entry.
        var probe = await Services.NetworkFileOps.ProbeFileAsync(path);
        if (probe == Services.FileProbeResult.Unreachable)
        {
            StatusMessage = "The file's location is not responding. Check your network connection and try again.";
            return;
        }
        if (probe == Services.FileProbeResult.Missing)
        {
            StatusMessage = $"\"{System.IO.Path.GetFileName(path)}\" is no longer available.";
            AppSettings.Current.RemoveRecentSharedImport(path);
            RefreshRecentImports();
            return;
        }

        await TryLoadFromLocalPathAsync(path);
#endif
        await Task.CompletedTask;
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
        // Academic year and semester ID travel with the file so the importer can tell Fall 2026
        // from Fall 2027 — the semester name alone cannot.
        var error = _exporter.Export(stream, sourceLabel, semName, sections,
            id => courses.GetValueOrDefault(id, id), lookups,
            academicYearName: ayName, semesterId: semesters.First().Semester.Id);

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

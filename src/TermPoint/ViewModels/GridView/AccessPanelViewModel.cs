using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// ViewModel for the Access toolbar group in the Schedule Grid. Manages the list of
/// <see cref="ProgramWatch"/> entries for the current semester and exposes conflict
/// summary data for the collapsed badge.
/// </summary>
public partial class AccessPanelViewModel : ObservableObject
{
    private readonly IProgramWatchRepository _repo;
    private readonly SemesterContext _semesterContext;
    private readonly GridChangeNotifier _changeNotifier;

    /// <summary>The watch list for the current semester.</summary>
    public ObservableCollection<ProgramWatchItemViewModel> Watches { get; } = [];

    /// <summary>Non-conflict prefix for the collapsed badge, e.g. "2 watches, " or "0 watches".</summary>
    [ObservableProperty] private string _summaryPrefix = "0 watches";

    /// <summary>Conflict portion of the badge, e.g. "3 conflicts". Empty when zero.</summary>
    [ObservableProperty] private string _summaryConflictText = string.Empty;

    /// <summary>Total conflict count across all enabled watches.</summary>
    [ObservableProperty] private int _totalConflictCount;

    /// <summary>
    /// The most recently computed program conflicts, set by the grid pipeline after
    /// calling <see cref="ProgramConflictService.DetectConflicts"/>. The grid pipeline
    /// writes this and the badge reads it.
    /// </summary>
    private IReadOnlyList<ProgramConflict> _lastConflicts = [];

    /// <summary>The inline creation form VM. Visible when the user clicks "New Watch".</summary>
    public WatchCreationViewModel Creation { get; }

    public AccessPanelViewModel(
        IProgramWatchRepository repo,
        SemesterContext semesterContext,
        GridChangeNotifier changeNotifier,
        ISchedulingEnvironmentRepository envRepo,
        ICourseRepository courseRepo)
    {
        _repo = repo;
        _semesterContext = semesterContext;
        _changeNotifier = changeNotifier;

        Creation = new WatchCreationViewModel(envRepo, courseRepo, OnCreationSave, OnCreationCancel);

        _semesterContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SemesterContext.SelectedSemesterDisplay)
                              or nameof(SemesterContext.SelectedSemesters))
                LoadWatches();
        };

        // Load watches for the semester that may already be selected at construction time.
        LoadWatches();
    }

    /// <summary>
    /// Returns only the enabled watches — used by the grid pipeline to feed
    /// <see cref="ProgramConflictService.DetectConflicts"/>.
    /// </summary>
    public IReadOnlyList<ProgramWatch> GetEnabledWatches() =>
        [.. Watches.Where(w => w.IsEnabled).Select(w => w.Watch)];

    /// <summary>
    /// Called by the grid pipeline after conflict detection to update per-watch conflict
    /// counts, section-pair details, and the summary badge.
    /// </summary>
    /// <param name="conflicts">The conflicts computed by <see cref="ProgramConflictService"/>.</param>
    /// <param name="sectionLabels">Section ID → display label (e.g. "MATH340 AB1") for involved sections.</param>
    public void UpdateConflictCounts(IReadOnlyList<ProgramConflict> conflicts,
                                     IReadOnlyDictionary<string, string> sectionLabels)
    {
        _lastConflicts = conflicts;

        var countByWatch = new Dictionary<string, int>();
        var detailsByWatch = new Dictionary<string, List<string>>();
        foreach (var c in conflicts)
        {
            countByWatch.TryGetValue(c.WatchId, out var n);
            countByWatch[c.WatchId] = n + 1;

            var labelA = sectionLabels.GetValueOrDefault(c.MeetingA.SectionId, c.MeetingA.SectionId);
            var labelB = sectionLabels.GetValueOrDefault(c.MeetingB.SectionId, c.MeetingB.SectionId);
            var pair = $"{labelA} / {labelB}";
            if (!detailsByWatch.TryGetValue(c.WatchId, out var list))
                detailsByWatch[c.WatchId] = list = [];
            if (!list.Contains(pair))
                list.Add(pair);
        }

        foreach (var item in Watches)
        {
            item.ConflictCount = countByWatch.GetValueOrDefault(item.Watch.Id, 0);
            item.ConflictDetails = detailsByWatch.GetValueOrDefault(item.Watch.Id, []);
        }

        TotalConflictCount = conflicts.Count;
        RefreshSummary();
    }

    /// <summary>
    /// Creates a new watch from the given definition, persists it, and triggers a grid reload.
    /// </summary>
    /// <param name="watch">The watch to create (should have a fresh ID).</param>
    public void CreateWatch(ProgramWatch watch)
    {
        var semesterId = GetCurrentSemesterId();
        if (semesterId is null) return;

        _repo.Save(semesterId, watch);
        LoadWatches();
        _changeNotifier.NotifyGridContentChanged();
    }

    /// <summary>Opens the inline creation form.</summary>
    [RelayCommand]
    private void BeginCreateWatch() => Creation.Show();

    private void OnCreationSave(ProgramWatch watch) => CreateWatch(watch);

    private void OnCreationCancel() { }

    /// <summary>Reloads watches from the repository for the current semester.</summary>
    public void LoadWatches()
    {
        Watches.Clear();

        var semesterId = GetCurrentSemesterId();
        if (semesterId is null)
        {
            RefreshSummary();
            return;
        }

        var watches = _repo.GetAll(semesterId);
        foreach (var w in watches)
        {
            var summary = FormatModeSummary(w);
            Watches.Add(new ProgramWatchItemViewModel(w, summary, OnWatchChanged, OnWatchDeleteRequested));
        }

        RefreshSummary();
    }

    /// <summary>
    /// Builds a compact description of the watch definition for display in the list.
    /// Tag names and course names are not resolved here — the caller can enrich later
    /// once name lookups are available.
    /// </summary>
    private static string FormatModeSummary(ProgramWatch watch)
    {
        if (watch.Mode == ProgramWatchMode.Tag)
        {
            var tagCount = watch.TagIds.Count;
            var levelCount = watch.LevelIds.Count;

            var tagPart = tagCount switch
            {
                0 => null,
                1 => "1 tag",
                _ => $"{tagCount} tags"
            };
            var levelPart = levelCount switch
            {
                0 => null,
                1 => "1 level",
                _ => $"{levelCount} levels"
            };

            var detail = (tagPart, levelPart) switch
            {
                (not null, not null) => $"{tagPart}, {levelPart}",
                (not null, null) => tagPart,
                (null, not null) => levelPart,
                _ => "(none)"
            };

            return $"Tag/Level: {detail}";
        }
        else
        {
            var count = watch.CourseIds.Count;
            return count switch
            {
                0 => "Courses: (none)",
                1 => "Courses: 1 course",
                _ => $"Courses: {count} courses"
            };
        }
    }

    private void OnWatchChanged(ProgramWatchItemViewModel item)
    {
        var semesterId = GetCurrentSemesterId();
        if (semesterId is null) return;

        _repo.Save(semesterId, item.Watch);
        RefreshSummary();
        _changeNotifier.NotifyGridContentChanged();
    }

    private void OnWatchDeleteRequested(ProgramWatchItemViewModel item)
    {
        _repo.Delete(item.Watch.Id);
        Watches.Remove(item);
        RefreshSummary();
        _changeNotifier.NotifyGridContentChanged();
    }

    private void RefreshSummary()
    {
        var enabledCount = Watches.Count(w => w.IsEnabled);
        var watchWord = enabledCount == 1 ? "watch" : "watches";

        if (TotalConflictCount > 0)
        {
            SummaryPrefix = $"{enabledCount} {watchWord}, ";
            SummaryConflictText = $"{TotalConflictCount} {(TotalConflictCount == 1 ? "conflict" : "conflicts")}";
        }
        else
        {
            SummaryPrefix = $"{enabledCount} {watchWord}";
            SummaryConflictText = string.Empty;
        }
    }

    private string? GetCurrentSemesterId()
    {
        var selected = _semesterContext.SelectedSemesters;
        return selected.Count > 0 ? selected[0].Semester.Id : null;
    }
}

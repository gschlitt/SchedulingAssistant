using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary>
    /// Summary text for the collapsed badge, e.g. "2 watches, 3 conflicts" or
    /// "No active watches".
    /// </summary>
    [ObservableProperty] private string _summaryText = "No active watches";

    /// <summary>Total conflict count across all enabled watches.</summary>
    [ObservableProperty] private int _totalConflictCount;

    /// <summary>
    /// The most recently computed program conflicts, set by the grid pipeline after
    /// calling <see cref="ProgramConflictService.DetectConflicts"/>. The grid pipeline
    /// writes this and the badge reads it.
    /// </summary>
    private IReadOnlyList<ProgramConflict> _lastConflicts = [];

    public AccessPanelViewModel(
        IProgramWatchRepository repo,
        SemesterContext semesterContext,
        GridChangeNotifier changeNotifier)
    {
        _repo = repo;
        _semesterContext = semesterContext;
        _changeNotifier = changeNotifier;

        _semesterContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SemesterContext.SelectedSemesterDisplay)
                              or nameof(SemesterContext.SelectedSemesters))
                LoadWatches();
        };
    }

    /// <summary>
    /// Returns only the enabled watches — used by the grid pipeline to feed
    /// <see cref="ProgramConflictService.DetectConflicts"/>.
    /// </summary>
    public IReadOnlyList<ProgramWatch> GetEnabledWatches() =>
        [.. Watches.Where(w => w.IsEnabled).Select(w => w.Watch)];

    /// <summary>
    /// Called by the grid pipeline after conflict detection to update per-watch conflict
    /// counts and the summary badge.
    /// </summary>
    /// <param name="conflicts">The conflicts computed by <see cref="ProgramConflictService"/>.</param>
    public void UpdateConflictCounts(IReadOnlyList<ProgramConflict> conflicts)
    {
        _lastConflicts = conflicts;

        var countByWatch = new Dictionary<string, int>();
        foreach (var c in conflicts)
        {
            countByWatch.TryGetValue(c.WatchId, out var n);
            countByWatch[c.WatchId] = n + 1;
        }

        foreach (var item in Watches)
            item.ConflictCount = countByWatch.GetValueOrDefault(item.Watch.Id, 0);

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
            var count = watch.TagIds.Count;
            return count switch
            {
                0 => "Tags: (none)",
                1 => "Tags: 1 tag",
                _ => $"Tags: {count} tags"
            };
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
        if (enabledCount == 0)
        {
            SummaryText = Watches.Count == 0
                ? "No active watches"
                : $"{Watches.Count} watch{(Watches.Count == 1 ? "" : "es")} (all disabled)";
            return;
        }

        var watchWord = enabledCount == 1 ? "watch" : "watches";
        if (TotalConflictCount == 0)
        {
            SummaryText = $"{enabledCount} {watchWord}, no conflicts";
        }
        else
        {
            var conflictWord = TotalConflictCount == 1 ? "conflict" : "conflicts";
            SummaryText = $"{enabledCount} {watchWord}, {TotalConflictCount} {conflictWord}";
        }
    }

    private string? GetCurrentSemesterId()
    {
        var selected = _semesterContext.SelectedSemesters;
        return selected.Count > 0 ? selected[0].Semester.Id : null;
    }
}

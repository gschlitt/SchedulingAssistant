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
///
/// <para><b>Read-only sessions.</b> A watch is a viewing aid, so a reader (reader / observer mode, or after the
/// write lock was lost) keeps using the panel. For a session that cannot write, creating, toggling, renaming and
/// deleting a watch change only the in-memory <see cref="Watches"/> list (remembered per semester for the life of
/// the session in <c>_sessionWatches</c>); the repository is never called, because its database is a throw-away
/// copy and a refused write would only confuse. The grid reads <see cref="GetEnabledWatches"/>, not the database,
/// so conflicts update exactly as for a writer. A writer's watches are saved to the database as before.</para>
/// </summary>
public partial class AccessPanelViewModel : ObservableObject
{
    private readonly IProgramWatchRepository _repo;
    private readonly SemesterContext _semesterContext;
    private readonly GridChangeNotifier _changeNotifier;

    /// <summary>
    /// Tells whether this session may write to the database (the write lock is ours). Asked at the moment of
    /// each change, so a lock lost mid-session switches the panel to in-memory behaviour at once.
    /// </summary>
    private readonly Func<bool> _canWrite;

    /// <summary>
    /// The watches of each semester as a read-only session has changed them, keyed by semester id. Consulted by
    /// <see cref="LoadWatches"/> in place of the repository, so edits made without write access survive a
    /// switch of semester within the session. Empty (and unused) for a writer.
    /// </summary>
    private readonly Dictionary<string, List<ProgramWatch>> _sessionWatches = [];

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

    /// <param name="repo">Where a writer's watches are saved.</param>
    /// <param name="semesterContext">The selected semester(s); watches belong to the first one.</param>
    /// <param name="changeNotifier">Told when the grid must redraw after a watch change.</param>
    /// <param name="envRepo">Section-property values for the creation form.</param>
    /// <param name="courseRepo">Courses for the creation form.</param>
    /// <param name="canWrite">True while the session may write to the database (normally the write lock's <c>IsWriter</c>).</param>
    public AccessPanelViewModel(
        IProgramWatchRepository repo,
        SemesterContext semesterContext,
        GridChangeNotifier changeNotifier,
        ISchedulingEnvironmentRepository envRepo,
        ICourseRepository courseRepo,
        Func<bool> canWrite)
    {
        _repo = repo;
        _semesterContext = semesterContext;
        _changeNotifier = changeNotifier;
        _canWrite = canWrite;

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
    /// Creates a new watch from the given definition and triggers a grid reload. A writer's watch is saved to the
    /// database; in a read-only session it is only added to the in-memory list (see the class remarks).
    /// </summary>
    /// <param name="watch">The watch to create (should have a fresh ID).</param>
    public void CreateWatch(ProgramWatch watch)
    {
        var semesterId = GetCurrentSemesterId();
        if (semesterId is null) return;

        if (_canWrite())
        {
            _repo.Save(semesterId, watch);
            LoadWatches();
        }
        else
        {
            Watches.Add(NewItem(watch));
            RememberSessionWatches(semesterId);
            RefreshSummary();
        }
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

        // A read-only session shows its own edits for this semester, if it has made any; everyone else reads the database.
        IReadOnlyList<ProgramWatch> watches =
            !_canWrite() && _sessionWatches.TryGetValue(semesterId, out var kept) ? kept : _repo.GetAll(semesterId);
        foreach (var w in watches)
            Watches.Add(NewItem(w));

        RefreshSummary();
    }

    /// <summary>Wraps <paramref name="watch"/> in a list item wired to this panel's change and delete handlers.</summary>
    /// <param name="watch">The watch to show.</param>
    /// <returns>The new list item.</returns>
    private ProgramWatchItemViewModel NewItem(ProgramWatch watch)
        => new(watch, FormatModeSummary(watch), OnWatchChanged, OnWatchDeleteRequested);

    /// <summary>
    /// Remembers the watches now shown for <paramref name="semesterId"/> as this read-only session's version of
    /// that semester's watches, so <see cref="LoadWatches"/> shows them again after a semester switch.
    /// </summary>
    /// <param name="semesterId">The semester whose list changed.</param>
    private void RememberSessionWatches(string semesterId)
        => _sessionWatches[semesterId] = [.. Watches.Select(w => w.Watch)];

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

        // Writer: save. Read-only session: the item has already updated its model; just remember the list.
        if (_canWrite())
            _repo.Save(semesterId, item.Watch);
        else
            RememberSessionWatches(semesterId);
        RefreshSummary();
        _changeNotifier.NotifyGridContentChanged();
    }

    private void OnWatchDeleteRequested(ProgramWatchItemViewModel item)
    {
        var writer = _canWrite();
        if (writer)
            _repo.Delete(item.Watch.Id);
        Watches.Remove(item);

        // Read-only session: the list itself is the record of the deletion.
        if (!writer && GetCurrentSemesterId() is { } semesterId)
            RememberSessionWatches(semesterId);
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

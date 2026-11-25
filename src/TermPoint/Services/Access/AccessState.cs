using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services.Access;

/// <summary>
/// The one registered Access service (decision D29: the engine stays pure and static). Holds, for every loaded
/// semester, the engine's inputs and the results of every watched tag (its analysis and its conflict marks), and
/// answers the grid and, later, the Access tab. Each semester is analysed on its own (Q43), over the whole
/// semester and not just what the grid shows (rule 29).
///
/// <para><b>Lazy.</b> Nothing is computed when something changes: <see cref="SectionStore.SectionsChanged"/>,
/// <see cref="GridChangeNotifier.GridContentChanged"/> and <see cref="Invalidate"/> only mark the state stale and
/// raise <see cref="Changed"/>. <see cref="Current"/> recomputes when stale, so a burst of changes costs one
/// recompute, and only when somebody asks. The suggestion query
/// (<see cref="ISectionRepository.GetSectionTypeIdsByCourse"/>) runs once per recompute.</para>
///
/// <para><b>Event order.</b> <c>ScheduleGridViewModel</c> takes this class as a constructor dependency, so DI builds it
/// first and its stale-marking handlers run before the grid's reload handlers (the same pattern as
/// <see cref="FocusService"/>). Without that, the grid would read a snapshot of the previous load.</para>
///
/// <para><b>Database switches and reader refreshes.</b> A database switch builds a new service provider, so a new
/// instance: it can never serve another database's results. A writer's save and a reader's Refresh both end in
/// <see cref="SectionStore.Reload"/>, which raises <c>SectionsChanged</c>. Edits to tags, courses or section types
/// reach the grid through <c>GridContentChanged</c> or a section reload, so they mark the state stale too. Anything
/// that changes what the engine reads without either event must call <see cref="Invalidate"/>.</para>
///
/// <para><b>Notices (rules 31 and 32).</b> A section reload (<see cref="SectionStore.SectionsChanged"/>) or a save of
/// components (<see cref="InvalidateAfterComponentsChange"/>) also asks the next recompute to compare the previous
/// results with the new ones, <i>per semester present in both</i> (so a semester switch never makes a notice), with
/// <see cref="AccessNotices.Compare"/>. Each change becomes a session-only <see cref="AccessNotice"/> in
/// <see cref="Notices"/> (newest first, one per watch and semester: a new one replaces the older). After every
/// recompute notices that stopped being true are dropped or narrowed. A plain <see cref="Invalidate"/> (the eye, Mark
/// all conflicts, the Multi-campus box) and <c>GridContentChanged</c> never compare. Both snapshots' lazy analyses are
/// forced when a comparison is made (about 25 ms for a large database).</para>
///
/// <para>Runs on the UI thread; not thread-safe. Lives as long as the services of one database.</para>
/// </summary>
public sealed class AccessState
{
    private readonly SectionStore _sectionStore;
    private readonly GridChangeNotifier _changeNotifier;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly ISchedulingEnvironmentRepository _envRepo;
    private readonly IAccessChecksSetupRepository _setupRepo;

    private AccessSnapshot _snapshot = AccessSnapshot.Empty;
    private bool _stale = true;

    // Notices: a comparison is pending after a section reload or a components save; the courses are known only for the latter.
    private bool _compareNext;
    private IReadOnlyList<string>? _componentCourseIds;
    private readonly List<AccessNotice> _notices = [];
    private int _noticeId;

    /// <summary>Raised, after the state is marked stale, whenever what <see cref="Current"/> would answer may have changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Creates the service and subscribes to the store and the notifier. Nothing is read from the database until
    /// <see cref="Current"/> is first asked for.
    /// </summary>
    /// <param name="sectionStore">The loaded semesters' sections.</param>
    /// <param name="changeNotifier">Tells when grid content (tags, courses, commitments) changed.</param>
    /// <param name="sectionRepo">For the suggestion source.</param>
    /// <param name="courseRepo">For the courses.</param>
    /// <param name="envRepo">For the section types and the watched tags.</param>
    /// <param name="setupRepo">For the access-checks set-up flag.</param>
    public AccessState(
        SectionStore sectionStore,
        GridChangeNotifier changeNotifier,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        ISchedulingEnvironmentRepository envRepo,
        IAccessChecksSetupRepository setupRepo)
    {
        _sectionStore = sectionStore;
        _changeNotifier = changeNotifier;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _envRepo = envRepo;
        _setupRepo = setupRepo;
        // Never unsubscribed: the instance lives as long as the services of one database.
        _sectionStore.SectionsChanged += OnSectionsChanged;
        _changeNotifier.GridContentChanged += Invalidate;
    }

    /// <summary>
    /// Whether Mark all conflicts is on (per computer, <see cref="AppSettings.MarkAllAccessConflicts"/>). Setting it
    /// saves the setting, marks the state stale, raises <see cref="Changed"/> and asks the grid to redraw.
    /// </summary>
    public bool MarkAllConflicts
    {
        get => AppSettings.Current.MarkAllAccessConflicts;
        set
        {
            if (AppSettings.Current.MarkAllAccessConflicts == value) return;
            AppSettings.Current.MarkAllAccessConflicts = value;
            AppSettings.Current.Save();
            Invalidate();
            _changeNotifier.NotifyGridContentChanged();
        }
    }

    /// <summary>
    /// The results for every loaded semester, recomputed first when stale.
    /// </summary>
    /// <returns>The snapshot; never null. Empty when no semester is loaded.</returns>
    /// <exception cref="Exception">A repository read failed; the state stays stale so the next call tries again.</exception>
    public AccessSnapshot Current
    {
        get
        {
            if (_stale)
            {
                var previous = _snapshot;
                var next = Compute();
                _snapshot = next;
                _stale = false;
                UpdateNotices(previous, next);
            }
            return _snapshot;
        }
    }

    /// <summary>
    /// Marks the state stale and raises <see cref="Changed"/>; the next <see cref="Current"/> recomputes. Call it
    /// when something the engine reads changed without a section reload or a grid-content notification (a tag's
    /// eye, the Multi-campus box, Mark all conflicts). It never makes notices: only a section reload and
    /// <see cref="InvalidateAfterComponentsChange"/> compare the old results with the new.
    /// </summary>
    public void Invalidate()
    {
        _stale = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// The store reloaded its sections (a save, a reader's refresh, a semester switch): the next recompute compares
    /// the old results with the new, per semester present in both, and makes notices (rule 32).
    /// </summary>
    private void OnSectionsChanged()
    {
        _compareNext = true;
        Invalidate();
    }

    /// <summary>
    /// Like <see cref="Invalidate"/>, for a save of components or of the set-up (the wizard, the Component boxes, a
    /// course's components): the next recompute compares the old results with the new and makes notices, naming the
    /// courses as the cause.
    /// </summary>
    /// <param name="courseIds">The courses whose components were written; empty when only section types or the set-up mark changed (the cause is then the general "latest changes").</param>
    public void InvalidateAfterComponentsChange(IReadOnlyList<string> courseIds)
    {
        _compareNext = true;
        _componentCourseIds = (_componentCourseIds ?? []).Union(courseIds).ToList();
        Invalidate();
    }

    // ── Notices (rules 31 and 32) ────────────────────────────────────────────────────────────────────

    /// <summary>The notices of this session, newest first. Reading it first brings the results up to date, which is when a pending comparison is made.</summary>
    public IReadOnlyList<AccessNotice> Notices
    {
        get
        {
            _ = Current;
            return _notices;
        }
    }

    /// <summary>Raised when <see cref="Notices"/> changed (one added or replaced, one dismissed, one dropped for no longer being true).</summary>
    public event Action? NoticesChanged;

    /// <summary>
    /// Removes a notice from the list (the notice's ✕). Does nothing when it is already gone.
    /// </summary>
    /// <param name="id">The notice's <see cref="AccessNotice.Id"/>.</param>
    public void DismissNotice(int id)
    {
        if (_notices.RemoveAll(n => n.Id == id) > 0) NoticesChanged?.Invoke();
    }

    /// <summary>
    /// After a recompute: makes the notices of a pending comparison, then drops those that stopped being true. A
    /// failure is logged and leaves the notices as they were: notices are an aid and must never break the reload
    /// that raised the change.
    /// </summary>
    /// <param name="previous">The results before the recompute.</param>
    /// <param name="next">The results now.</param>
    private void UpdateNotices(AccessSnapshot previous, AccessSnapshot next)
    {
        bool compare = _compareNext;
        var courses = _componentCourseIds;
        _compareNext = false;
        _componentCourseIds = null;
        bool changed = false;
        try
        {
            if (compare)
                foreach (var (semesterId, after) in next.Semesters)
                {
                    if (!previous.Semesters.TryGetValue(semesterId, out var before)) continue;
                    foreach (var change in AccessNotices.Compare(before, after))
                    {
                        // A new notice for a watch replaces that watch's older one (in the same semester).
                        _notices.RemoveAll(n => n.WatchId == change.WatchId && n.SemesterId == semesterId);
                        var cause = AccessNotices.CauseOf(before, after, change, courses);
                        _notices.Insert(0, new AccessNotice(++_noticeId, change.WatchId, semesterId, change, cause));
                        changed = true;
                    }
                }
            changed |= DropNoticesNoLongerTrue(next);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessState.UpdateNotices");
        }
        if (changed) NoticesChanged?.Invoke();
    }

    /// <summary>
    /// Drops notices that stopped being true after a recompute, and narrows those that are partly true. A notice of
    /// a semester that is not loaded is kept (nothing can be said about it). One of a watch that is gone or no
    /// longer watched is dropped.
    /// </summary>
    /// <param name="next">The results now.</param>
    /// <returns><c>true</c> when the list changed.</returns>
    private bool DropNoticesNoLongerTrue(AccessSnapshot next)
    {
        bool changed = false;
        for (int i = _notices.Count - 1; i >= 0; i--)
        {
            var notice = _notices[i];
            if (!next.Semesters.TryGetValue(notice.SemesterId, out var semester)) continue;
            var watch = semester.Watches.FirstOrDefault(w => w.Tag.Id == notice.WatchId);
            var refreshed = watch is null || watch.Analysis.Health == AccessHealth.TooLarge
                ? null
                : AccessNotices.Refresh(notice, watch.Analysis);
            if (ReferenceEquals(refreshed, notice)) continue;
            changed = true;
            if (refreshed is null) _notices.RemoveAt(i); else _notices[i] = refreshed;
        }
        return changed;
    }

    /// <summary>Reads what the engine needs and runs it for every loaded semester and watched tag.</summary>
    private AccessSnapshot Compute()
    {
        var bySemester = _sectionStore.SectionsBySemester;
        if (bySemester.Count == 0) return AccessSnapshot.Empty;

        var courses = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var types = _envRepo.GetAll(SchedulingEnvironmentTypes.SectionType);
        var tags = _envRepo.GetAll(SchedulingEnvironmentTypes.Tag).Where(t => t.IsAccessWatch).ToList();
        bool setUp = _setupRepo.IsSetUp();
        var suggestion = _sectionRepo.GetSectionTypeIdsByCourse();
        bool markAll = MarkAllConflicts;

        var result = new Dictionary<string, SemesterAccess>();
        foreach (var (semesterId, sections) in bySemester)
        {
            var inputs = new AccessInputs(sections, courses, types, setUp, suggestion);
            var watches = new List<WatchAccess>();
            foreach (var tag in tags)
            {
                var watch = AccessWatch.FromTag(tag);
                watches.Add(new WatchAccess(tag, watch, ConflictMarks.For(inputs, watch, markAll),
                    () => AccessAnalyzer.Analyze(inputs, watch)));
            }
            result[semesterId] = new SemesterAccess(semesterId, inputs, watches);
        }
        return new AccessSnapshot(result);
    }
}

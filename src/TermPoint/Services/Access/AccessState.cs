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
        _sectionStore.SectionsChanged += Invalidate;
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
                _snapshot = Compute();
                _stale = false;
            }
            return _snapshot;
        }
    }

    /// <summary>
    /// Marks the state stale and raises <see cref="Changed"/>; the next <see cref="Current"/> recomputes. Call it
    /// when something the engine reads changed without a section reload or a grid-content notification (a tag's
    /// eye, later a component edit).
    /// </summary>
    public void Invalidate()
    {
        _stale = true;
        Changed?.Invoke();
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

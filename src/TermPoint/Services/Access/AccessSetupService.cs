using System.Data.Common;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services.Access;

/// <summary>
/// Every write of the access setup (Access Analyzer revision 8f): the wizard's two answers, marking and unmarking a
/// section type as a component, and saving several courses' component lists. One singleton, registered for the desktop
/// and the WASM demo, that also relays the requests to open the wizard (the eye before setup, the Access tab's Set up,
/// a newly marked type) to the main window.
///
/// <para><b>The four rules for the shared database.</b> Each write is ONE transaction (nullable-tx pattern, as
/// <c>SemesterContentsService.RunInTransaction</c>) through the repositories' ordinary <c>Update</c>/<c>MarkSetUp</c>,
/// so each marks the database dirty and the checkout service writes it back by its normal atomic rename (rule 1). A
/// write is refused unless this computer holds the write lock (<see cref="CanSave"/>), and the repositories refuse
/// again if the lock was lost meanwhile (rule 2). Nothing is deleted. A failure rolls everything back, so the
/// wizard never leaves a half-saved setup (rule 3), and the exception goes to the caller, which words it (rule 4).
/// Only rows that change are written, so a "These are right" that changes nothing writes only the set-up mark.</para>
///
/// <para>After a successful write the access state is made stale and the grid told its content changed.</para>
/// </summary>
public sealed class AccessSetupService
{
    private readonly IDatabaseContext _db;
    private readonly ISchedulingEnvironmentRepository _envRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly IAccessChecksSetupRepository _setupRepo;
    private readonly AccessState _state;
    private readonly GridChangeNotifier _notifier;
    private readonly Func<bool> _canWrite;

    /// <summary>Raised when <see cref="CanSave"/> may have changed (the write lock was gained or lost).</summary>
    public event Action? CanSaveChanged;

    /// <summary>
    /// Raised by <see cref="RequestSetupWizard"/>: the main window opens the wizard. The argument is the work to do
    /// once the wizard has saved (for example turning on the watch that was waiting), or null.
    /// </summary>
    public event Action<Action?>? SetupWizardRequested;

    /// <summary>Raised by <see cref="RequestNewTypeWizard"/>: the main window opens the wizard in "new type" mode for this section type id.</summary>
    public event Action<string>? NewTypeWizardRequested;

    /// <summary>Creates the service for the application.</summary>
    /// <param name="db">Supplies the write transaction.</param>
    /// <param name="envRepo">Reads and saves the section types.</param>
    /// <param name="courseRepo">Reads and saves the courses.</param>
    /// <param name="sectionRepo">Reads the suggestion source for <see cref="InputsFor"/>.</param>
    /// <param name="setupRepo">Reads and writes the set-up mark.</param>
    /// <param name="state">Made stale after a change.</param>
    /// <param name="notifier">Tells the grid its content changed.</param>
    /// <param name="lockService">Says whether this computer holds the write lock.</param>
    public AccessSetupService(
        IDatabaseContext db, ISchedulingEnvironmentRepository envRepo, ICourseRepository courseRepo,
        ISectionRepository sectionRepo, IAccessChecksSetupRepository setupRepo, AccessState state, GridChangeNotifier notifier, WriteLockService lockService)
        : this(db, envRepo, courseRepo, sectionRepo, setupRepo, state, notifier, () => lockService.IsWriter)
    {
        lockService.LockStateChanged += () => CanSaveChanged?.Invoke();
    }

    /// <summary>Creates the service with its own write test (the tests' seam).</summary>
    /// <param name="db">Supplies the write transaction.</param>
    /// <param name="envRepo">Reads and saves the section types.</param>
    /// <param name="courseRepo">Reads and saves the courses.</param>
    /// <param name="sectionRepo">Reads the suggestion source for <see cref="InputsFor"/>.</param>
    /// <param name="setupRepo">Reads and writes the set-up mark.</param>
    /// <param name="state">Made stale after a change.</param>
    /// <param name="notifier">Tells the grid its content changed.</param>
    /// <param name="canWrite">Whether this computer may save now.</param>
    internal AccessSetupService(
        IDatabaseContext db, ISchedulingEnvironmentRepository envRepo, ICourseRepository courseRepo,
        ISectionRepository sectionRepo, IAccessChecksSetupRepository setupRepo, AccessState state, GridChangeNotifier notifier, Func<bool> canWrite)
    {
        _db = db;
        _envRepo = envRepo;
        _courseRepo = courseRepo;
        _sectionRepo = sectionRepo;
        _setupRepo = setupRepo;
        _state = state;
        _notifier = notifier;
        _canWrite = canWrite;
    }

    /// <summary>Whether the access-checks setup has been finished (the stored mark).</summary>
    public bool IsSetUp => _setupRepo.IsSetUp();

    /// <summary>Whether the setup may be changed now: this computer holds the write lock.</summary>
    public bool CanSave => _canWrite();

    /// <summary>The engine's inputs over a given set of sections (a loaded semester, or a copy source), read now.</summary>
    /// <param name="sections">The sections to treat as the semester view.</param>
    /// <returns>The inputs; they answer components and mismatches by the stored or suggested rules.</returns>
    public AccessInputs InputsFor(IEnumerable<Section> sections) =>
        AccessInputsFactory.Build(sections, _courseRepo, _envRepo, _setupRepo, _sectionRepo);

    /// <summary>The names of the section types that look like components (the invitation names them), in section type order.</summary>
    /// <returns>The names; empty when none looks like one.</returns>
    public IReadOnlyList<string> LikelyComponentNames() =>
        SectionTypes().Where(t => ComponentNames.LooksLikeComponent(t.Name)).Select(t => t.Name).ToList();

    /// <summary>The ids of the tags that are access watches.</summary>
    /// <returns>The watched tags' ids.</returns>
    public IReadOnlySet<string> WatchedTagIds() =>
        _envRepo.GetAll(SchedulingEnvironmentTypes.Tag).Where(t => t.IsAccessWatch).Select(t => t.Id).ToHashSet();

    /// <summary>Asks the main window to open the setup wizard.</summary>
    /// <param name="then">Work to run once the wizard has saved, or null.</param>
    public void RequestSetupWizard(Action? then = null) => SetupWizardRequested?.Invoke(then);

    /// <summary>Asks the main window to open the wizard in "new type" mode.</summary>
    /// <param name="typeId">The section type that was just marked as a component.</param>
    public void RequestNewTypeWizard(string typeId) => NewTypeWizardRequested?.Invoke(typeId);

    /// <summary>
    /// The wizard's answer No: every section type that is marked is unmarked, and the set-up mark is written.
    /// </summary>
    /// <exception cref="InvalidOperationException">The write lock is not ours; nothing is saved.</exception>
    /// <exception cref="Exception">Any database failure, after the rollback.</exception>
    public void SaveNoComponents() =>
        Write(tx =>
        {
            foreach (var type in SectionTypes().Where(t => t.IsComponent))
            {
                type.IsComponent = false;
                _envRepo.Update(type, tx);
            }
            _setupRepo.MarkSetUp(tx);
        });

    /// <summary>
    /// The wizard's answer Yes: the section types' Component flags are set as given, each listed course's component
    /// list is set (lists are stored in section type order), and the set-up mark is written. A type or a course is
    /// written only when its stored value differs; a course whose list was never set (null) counts as different from
    /// an empty list.
    /// </summary>
    /// <param name="componentTypeIds">The section type ids that are components; every other section type is unmarked.</param>
    /// <param name="componentsByCourse">For each course id, its component type ids. Courses not listed are left alone.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours; nothing is saved.</exception>
    /// <exception cref="Exception">Any database failure, after the rollback.</exception>
    public void SaveSetup(
        IReadOnlySet<string> componentTypeIds, IReadOnlyDictionary<string, IReadOnlyList<string>> componentsByCourse) =>
        Write((tx, written) =>
        {
            var types = SectionTypes();
            foreach (var type in types)
            {
                bool want = componentTypeIds.Contains(type.Id);
                if (type.IsComponent == want) continue;
                type.IsComponent = want;
                _envRepo.Update(type, tx);
            }
            WriteCourseLists(componentsByCourse, types, tx, written);
            _setupRepo.MarkSetUp(tx);
        });

    /// <summary>Marks one section type as a component (the Component box in Scheduling Environment). Nothing is written when it already is, or is gone.</summary>
    /// <param name="typeId">The section type id.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours; nothing is saved.</exception>
    public void MarkComponent(string typeId) =>
        Write(tx =>
        {
            var type = SectionTypes().FirstOrDefault(t => t.Id == typeId);
            if (type is null || type.IsComponent) return;
            type.IsComponent = true;
            _envRepo.Update(type, tx);
        });

    /// <summary>
    /// The section type ids the Component boxes show before the setup: the types whose name looks like a component,
    /// the same guess the analyzer counts by until the setup is done.
    /// </summary>
    /// <returns>The ids of the guessed component types.</returns>
    public IReadOnlySet<string> GuessedComponentTypeIds() => ComponentSuggestion.PreTickedTypes(SectionTypes());

    /// <summary>
    /// A Component box clicked before the setup (Greg, 2026-11-24: the boxes work without the wizard, so a user who
    /// never makes a watch can still mark section types). The boxes were showing <see cref="GuessedComponentTypeIds"/>,
    /// so that guess with this one change becomes every type's stored flag, and the set-up mark is written, in one
    /// transaction. Courses' lists stay unset (null), so the analyzer keeps suggesting each course's components.
    /// </summary>
    /// <param name="typeId">The section type whose box was clicked.</param>
    /// <param name="isComponent">The box's new state.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours; nothing is saved.</exception>
    /// <exception cref="Exception">Any database failure, after the rollback.</exception>
    public void SetComponentBeforeSetup(string typeId, bool isComponent) =>
        Write(tx =>
        {
            var types = SectionTypes();
            var wanted = ComponentSuggestion.PreTickedTypes(types).ToHashSet();
            if (isComponent) wanted.Add(typeId); else wanted.Remove(typeId);
            foreach (var type in types)
            {
                bool want = wanted.Contains(type.Id);
                if (type.IsComponent == want) continue;
                type.IsComponent = want;
                _envRepo.Update(type, tx);
            }
            _setupRepo.MarkSetUp(tx);
        });

    /// <summary>
    /// Whether any course has its components set (a stored list, even an empty one). When none has, a newly ticked
    /// component type needs no "which courses have it?" step: every course still takes its components from its
    /// sections' types.
    /// </summary>
    /// <returns>True when at least one course's list is set.</returns>
    public bool AnyCourseComponentsSet() => _courseRepo.GetAll().Any(c => c.ComponentTypeIds is not null);

    /// <summary>The courses whose stored component list contains a section type, sorted by calendar code.</summary>
    /// <param name="typeId">The section type id.</param>
    /// <returns>The courses; empty when none lists it.</returns>
    public IReadOnlyList<Course> CoursesListing(string typeId) =>
        _courseRepo.GetAll()
            .Where(c => c.ComponentTypeIds?.Contains(typeId) == true)
            .OrderBy(c => c.CalendarCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Stops counting a section type as a component: its flag goes off and it is removed from every course's stored
    /// list, in one transaction. Only courses that list it are written.
    /// </summary>
    /// <param name="typeId">The section type id.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours; nothing is saved.</exception>
    /// <exception cref="Exception">Any database failure, after the rollback.</exception>
    public void UnmarkComponent(string typeId) =>
        Write((tx, written) =>
        {
            var type = SectionTypes().FirstOrDefault(t => t.Id == typeId);
            if (type is not null && type.IsComponent)
            {
                type.IsComponent = false;
                _envRepo.Update(type, tx);
            }
            foreach (var course in _courseRepo.GetAll().Where(c => c.ComponentTypeIds?.Contains(typeId) == true))
            {
                course.ComponentTypeIds!.RemoveAll(id => id == typeId);
                _courseRepo.Update(course, tx);
                written.Add(course.Id);
            }
        });

    /// <summary>Saves several courses' component lists in one transaction (the "new type" step). Only changed courses are written.</summary>
    /// <param name="componentsByCourse">For each course id, its component type ids.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours; nothing is saved.</exception>
    /// <exception cref="Exception">Any database failure, after the rollback.</exception>
    public void SaveCourseComponents(IReadOnlyDictionary<string, IReadOnlyList<string>> componentsByCourse) =>
        Write((tx, written) => WriteCourseLists(componentsByCourse, SectionTypes(), tx, written));

    /// <summary>The section types, in section type order.</summary>
    private List<SchedulingEnvironmentValue> SectionTypes() =>
        _envRepo.GetAll(SchedulingEnvironmentTypes.SectionType).OrderBy(t => t.SortOrder).ToList();

    /// <summary>Writes the listed courses whose stored list differs from the given one (null differs from empty).</summary>
    /// <param name="componentsByCourse">For each course id, its component type ids.</param>
    /// <param name="typesInOrder">The section types in order; lists are stored in this order.</param>
    /// <param name="tx">The open transaction, or null.</param>
    /// <param name="written">Receives the id of every course written, for the notices' cause.</param>
    private void WriteCourseLists(
        IReadOnlyDictionary<string, IReadOnlyList<string>> componentsByCourse,
        IReadOnlyList<SchedulingEnvironmentValue> typesInOrder, DbTransaction? tx, List<string> written)
    {
        var order = typesInOrder.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        int Rank(string id) => order.TryGetValue(id, out var i) ? i : int.MaxValue;
        foreach (var course in _courseRepo.GetAll().Where(c => componentsByCourse.ContainsKey(c.Id)))
        {
            var wanted = componentsByCourse[course.Id].Distinct().OrderBy(Rank).ToList();
            if (course.ComponentTypeIds is not null && course.ComponentTypeIds.SequenceEqual(wanted)) continue;
            course.ComponentTypeIds = wanted;   // the record read back keeps unknown JSON fields (PersistedRecord)
            _courseRepo.Update(course, tx);
            written.Add(course.Id);
        }
    }

    /// <summary>Runs writes that do not write any course's list (see <see cref="Write(Action{DbTransaction?, List{string}})"/>).</summary>
    /// <param name="work">The writes; receives the transaction, or null when the context has none.</param>
    private void Write(Action<DbTransaction?> work) => Write((tx, _) => work(tx));

    /// <summary>
    /// Runs the writes in one transaction after the write-lock check, then makes the access state stale (naming the
    /// courses written, so a notice can say whose components changed) and tells the grid. A failure rolls back (a
    /// failed rollback never masks the original exception) and rethrows, and nothing is notified.
    /// </summary>
    /// <param name="work">The writes; receives the transaction, or null when the context has none, and a list to add the id of every course it writes to.</param>
    private void Write(Action<DbTransaction?, List<string>> work)
    {
        if (!CanSave)
            throw new InvalidOperationException("The access setup can only be changed while this computer can save.");
        var writtenCourseIds = new List<string>();
        using (var tx = _db.BeginWriteTransaction())
        {
            try
            {
                work(tx, writtenCourseIds);
                tx?.Commit();
            }
            catch
            {
                try { tx?.Rollback(); } catch { /* the original exception is rethrown below */ }
                throw;
            }
        }
        _state.InvalidateAfterComponentsChange(writtenCourseIds);
        _notifier.NotifyGridContentChanged();
    }
}

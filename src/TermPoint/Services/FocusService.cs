using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Holds the user's subject focus (spec item 20, "load by subject / focus"): which subjects the section list, grid,
/// filter lists, choosers and workload views should show. One focus per user per database, remembered in
/// <see cref="AppSettings.FocusByDatabase"/>; nothing is written to the database. The service only DECIDES what is
/// in focus; every view that hides or offers things reads its answer from here, so they all agree.
///
/// <para><b>Lifetime.</b> A singleton in the per-database service provider, so a database switch (which builds a
/// fresh provider) gets a fresh instance whose starting value is resolved once, in the constructor. Everything runs
/// on the UI thread; the class is not thread-safe.</para>
///
/// <para><b>Starting value.</b> The demo (null database key) always starts at All and never touches settings. A
/// database seen for the first time starts at All when it has <see cref="MaxSubjectsForAllStart"/> subjects or
/// fewer, otherwise None, and the result is saved at once so the rule runs only once. A remembered focus is reused
/// after dropping any subject that no longer exists (a Subjects focus left with none becomes None).</para>
///
/// <para><b>Rule 3 (never lose unsaved work).</b> <see cref="CanChangeFocus"/> is false while a section editor is
/// open and <see cref="SetFocus"/> refuses to act, so no focus change can rebuild the section list underneath an
/// editor with unapplied edits. The guarantee lives here, not in each control.</para>
///
/// <para><b>Event ordering.</b> This service invalidates its caches from <see cref="SectionStore.SectionsChanged"/>.
/// A consumer that asks <see cref="Matches"/> or <see cref="IsInFocus"/> from its own SectionsChanged handler must
/// take this service as a constructor dependency: dependency injection then constructs it first, so its handler is
/// subscribed first and runs first, and the answer is never stale.</para>
/// </summary>
public sealed class FocusService : ObservableObject, IDisposable
{
    /// <summary>
    /// A database seen for the first time starts at All when it has this many subjects or fewer (a department never
    /// notices the feature) and at None when it has more (the dean's-office case, which shows the empty state).
    /// The rule runs once per database; a department that later grows past this stays at All.
    /// </summary>
    public const int MaxSubjectsForAllStart = 3;

    private readonly ISubjectRepository _subjectRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly IAcademicYearRepository _academicYearRepo;
    private readonly SemesterContext _semesterContext;
    private readonly SectionStore _sectionStore;

    /// <summary>
    /// The settings key for this database (<see cref="CheckoutService.NormalizeSourcePath"/> of the SHARED database),
    /// or null in the demo, whose focus is never read from or written to settings.
    /// </summary>
    private readonly string? _databaseKey;

    /// <summary>Course id to subject id, built lazily; null after <see cref="Invalidate"/>.</summary>
    private Dictionary<string, string>? _subjectByCourseId;

    /// <summary>The database-wide teaching history, built lazily and only when a Subjects focus needs it; null after <see cref="Invalidate"/>.</summary>
    private TeachingHistory? _history;

    private bool _canChangeFocus = true;

    /// <summary>
    /// Creates the service and resolves the starting focus (see the class remarks). Reads
    /// <see cref="ISubjectRepository.GetAll"/> unless <paramref name="databaseKey"/> is null.
    /// </summary>
    /// <param name="subjectRepo">Subjects; used to count them and to drop deleted ones from a remembered focus.</param>
    /// <param name="courseRepo">Courses; maps a section to its subject.</param>
    /// <param name="sectionRepo">Sections; <see cref="ISectionRepository.GetAll()"/> feeds the teaching history.</param>
    /// <param name="semesterRepo">Semesters; maps a section to its academic year.</param>
    /// <param name="academicYearRepo">Academic years; ordered to define "the year before".</param>
    /// <param name="semesterContext">The viewed academic year (Y) for <see cref="IsInFocus"/>.</param>
    /// <param name="sectionStore">Raises <see cref="SectionStore.SectionsChanged"/>, which drops the caches.</param>
    /// <param name="databaseKey">
    /// <c>CheckoutService.NormalizeSourcePath(D)</c> for the shared database, or null for the demo.
    /// </param>
    public FocusService(
        ISubjectRepository subjectRepo,
        ICourseRepository courseRepo,
        ISectionRepository sectionRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        SemesterContext semesterContext,
        SectionStore sectionStore,
        string? databaseKey)
    {
        _subjectRepo = subjectRepo;
        _courseRepo = courseRepo;
        _sectionRepo = sectionRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _semesterContext = semesterContext;
        _sectionStore = sectionStore;
        _databaseKey = databaseKey;

        Current = ResolveStartingFocus();

        _sectionStore.SectionsChanged += Invalidate;
        _semesterContext.PropertyChanged += OnSemesterContextPropertyChanged;
    }

    // ── State ──────────────────────────────────────────────────────────────────

    /// <summary>The focus in force. Change it only through <see cref="SetFocus"/>.</summary>
    public SubjectFocus Current { get; private set; }

    /// <summary>
    /// True unless a section editor is open. Every control that changes the focus binds its enabled state to this
    /// (tooltip when false: "Finish editing first"). Observable.
    /// </summary>
    public bool CanChangeFocus
    {
        get => _canChangeFocus;
        private set => SetProperty(ref _canChangeFocus, value);
    }

    /// <summary>Raised, after <see cref="Current"/> has changed and been saved, once per real change.</summary>
    public event Action? FocusChanged;

    /// <summary>
    /// Tells the service whether a section editor is open. The section list calls it when an editor opens and when
    /// it closes (by Apply, Cancel or any collapse); while open, <see cref="CanChangeFocus"/> is false.
    /// </summary>
    /// <param name="isOpen">True when an editor has just opened, false when none is open any more.</param>
    public void SetEditorOpen(bool isOpen) => CanChangeFocus = !isOpen;

    /// <summary>
    /// Changes the focus. Refuses (returns false; nothing saved, no event) when <see cref="CanChangeFocus"/> is
    /// false. Setting a focus equal to the current one succeeds without a save or an event.
    /// </summary>
    /// <param name="focus">The new focus.</param>
    /// <returns>True when the service now holds <paramref name="focus"/>; false when the change was refused.</returns>
    public bool SetFocus(SubjectFocus focus)
    {
        if (!CanChangeFocus) return false;
        if (focus.Equals(Current)) return true;

        Current = focus;
        Persist(focus);
        FocusChanged?.Invoke();
        return true;
    }

    // ── Questions the views ask ────────────────────────────────────────────────

    /// <summary>
    /// Whether a section is in focus: All is true, None false, and a Subjects focus is true when the section's
    /// course exists and its subject is one of the focused subjects.
    /// </summary>
    /// <param name="section">The section to test.</param>
    /// <returns>True when the section is in focus.</returns>
    public bool Matches(Section section)
    {
        if (Current.Kind == FocusKind.All) return true;
        if (Current.Kind == FocusKind.None) return false;
        return section.CourseId is not null
               && SubjectByCourseId.TryGetValue(section.CourseId, out var subjectId)
               && Current.ContainsSubject(subjectId);
    }

    /// <summary>
    /// Whether a course is in focus: All is true, None false, and a Subjects focus is true when the course's
    /// <see cref="Course.SubjectId"/> is one of the focused subjects.
    /// </summary>
    /// <param name="course">The course to test.</param>
    /// <returns>True when the course is in focus.</returns>
    public bool IsCourseInFocus(Course course) => Current.Kind switch
    {
        FocusKind.All => true,
        FocusKind.None => false,
        _ => Current.ContainsSubject(course.SubjectId),
    };

    /// <summary>
    /// Whether an instructor is in focus, by <see cref="InstructorFocusRule"/> over the database-wide teaching
    /// history and the academic year currently viewed. Under All or None the history is not even built (the rule's
    /// first two steps need none), so a department using All never pays for the whole-database read.
    /// </summary>
    /// <param name="instructor">The instructor to test.</param>
    /// <returns>True when the instructor is in focus.</returns>
    public bool IsInFocus(Instructor instructor)
    {
        var history = Current.Kind == FocusKind.Subjects ? History : TeachingHistory.Empty;
        return InstructorFocusRule.IsInFocus(
            Current, instructor, history, _semesterContext.SelectedAcademicYear?.Id);
    }

    /// <summary>
    /// Drops the cached lookups (course to subject, teaching history) so they are rebuilt from the repositories on
    /// next use. Called automatically when the section cache reloads and when the viewed academic year changes.
    /// Anything else that changes which subject a course has, or who teaches what, without reloading the section
    /// store should call it too.
    /// </summary>
    public void Invalidate()
    {
        _subjectByCourseId = null;
        _history = null;
    }

    /// <summary>Unsubscribes from the section store and semester context. Safe to call more than once.</summary>
    public void Dispose()
    {
        _sectionStore.SectionsChanged -= Invalidate;
        _semesterContext.PropertyChanged -= OnSemesterContextPropertyChanged;
    }

    // ── Caches ─────────────────────────────────────────────────────────────────

    /// <summary>Course id to subject id, rebuilt from <see cref="ICourseRepository.GetAll"/> when absent.</summary>
    private Dictionary<string, string> SubjectByCourseId =>
        _subjectByCourseId ??= BuildSubjectByCourseId();

    /// <summary>The teaching history, rebuilt from the repositories when absent.</summary>
    private TeachingHistory History => _history ??= BuildHistory();

    /// <summary>Reads every course into a course-id to subject-id map (first wins on a duplicate id).</summary>
    private Dictionary<string, string> BuildSubjectByCourseId()
    {
        var map = new Dictionary<string, string>();
        foreach (var course in _courseRepo.GetAll()) map.TryAdd(course.Id, course.SubjectId);
        return map;
    }

    /// <summary>
    /// Builds the teaching history from ALL sections (<see cref="SectionStore"/> holds only the viewed semesters, so
    /// it cannot be used), courses, semesters and academic years.
    /// </summary>
    private TeachingHistory BuildHistory() => TeachingHistory.Build(
        _sectionRepo.GetAll(), _courseRepo.GetAll(), _semesterRepo.GetAll(), _academicYearRepo.GetAll());

    /// <summary>Drops the caches when the viewed academic year changes (the history is rebuilt for the new view).</summary>
    private void OnSemesterContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedAcademicYear)) Invalidate();
    }

    // ── Starting value and persistence ─────────────────────────────────────────

    /// <summary>
    /// Works out the focus this database starts with (see the class remarks) and saves it when it is new or was
    /// pruned. If the subjects cannot be read, falls back to All in memory only: a rule outcome based on a failed
    /// read must never be saved as "the first-seen decision".
    /// </summary>
    /// <returns>The starting focus.</returns>
    private SubjectFocus ResolveStartingFocus()
    {
        if (_databaseKey is null) return SubjectFocus.All;

        HashSet<string> existingSubjectIds;
        try
        {
            existingSubjectIds = _subjectRepo.GetAll().Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[FocusService] Could not read subjects; starting at All without saving: {ex.Message}");
            return SubjectFocus.All;
        }

        var remembered = AppSettings.Current.FocusByDatabase;
        if (remembered.TryGetValue(_databaseKey, out var saved) && saved is not null)
        {
            var focus = SubjectFocus.FromSaved(saved);

            // Subjects deleted since the focus was saved drop out silently; none left means None.
            if (focus.Kind == FocusKind.Subjects)
            {
                var pruned = SubjectFocus.ForSubjects(focus.SubjectIds.Where(existingSubjectIds.Contains));
                if (!pruned.Equals(focus))
                {
                    Persist(pruned);
                    return pruned;
                }
            }
            return focus;
        }

        // First time this database path is seen: apply the size rule once and remember the answer.
        var starting = existingSubjectIds.Count <= MaxSubjectsForAllStart ? SubjectFocus.All : SubjectFocus.None;
        Persist(starting);
        return starting;
    }

    /// <summary>
    /// Saves <paramref name="focus"/> under this database's key. Does nothing in the demo (null key). A failed
    /// save is logged by <see cref="AppSettings.Save"/>; the in-memory focus stays correct.
    /// </summary>
    /// <param name="focus">The focus to remember.</param>
    private void Persist(SubjectFocus focus)
    {
        if (_databaseKey is null) return;
        var settings = AppSettings.Current;
        settings.FocusByDatabase[_databaseKey] = focus.ToSaved();
        settings.Save();
    }
}

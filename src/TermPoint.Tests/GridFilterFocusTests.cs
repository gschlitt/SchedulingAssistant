using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests that the schedule grid's filter lists follow the subject focus (spec item 20, step 20.4): the Subjects,
/// Courses and Instructors lists (and the overlay instructor list that mirrors the Instructors list) offer only what
/// is in the focus, ticks on items that drop out are dropped, the filter then reads as inactive when nothing is left
/// ticked, and the lists rebuild when the focus or the viewed academic year changes.
/// <para>
/// These run the real <see cref="ScheduleGridViewModel"/> over a temp database, wired as dependency injection wires
/// it: one shared <see cref="FocusService"/>, <see cref="SemesterContext"/> and <see cref="SectionStore"/>, plus a
/// <see cref="SectionListViewModel"/> that (as in the app) reloads the store when the semester selection changes.
/// </para>
/// </summary>
public sealed class GridFilterFocusTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Protects settings.json: reloading the semester context saves it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly RoomRepository _roomRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    private readonly Subject _biol = new() { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Subject _chem = new() { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" };
    private readonly Subject _phys = new() { Id = "subj-phys", Name = "Physics", CalendarAbbreviation = "PHYS" };
    private readonly Course _biolCourse = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chemCourse = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };
    private readonly Course _physCourse = new() { Id = "c-phys", SubjectId = "subj-phys", CalendarCode = "PHYS101", Title = "Phys" };

    private readonly string _year1Id;
    private readonly string _fall1;
    private readonly string _year2Id;
    private readonly string _fall2;

    /// <summary>Instructor ids by last name: Baker teaches BIOL, Adams CHEM, Dunn PHYS (all in year 1).</summary>
    private readonly Dictionary<string, string> _instructors = new();

    private readonly FocusService _focus;
    private readonly SectionListViewModel _list;
    private readonly ScheduleGridViewModel _grid;

    /// <summary>
    /// Seeds three subjects with a course each, three instructors who each teach one subject in year 1 (Adams also
    /// teaches Biology in year 2), a room, and builds the focus service, the section list and the grid.
    /// </summary>
    public GridFilterFocusTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _roomRepo = new RoomRepository(db);

        foreach (var s in new[] { _biol, _chem, _phys }) _subjectRepo.Insert(s);
        foreach (var c in new[] { _biolCourse, _chemCourse, _physCourse }) _courseRepo.Insert(c);
        _roomRepo.Insert(new Room { Building = "Main", RoomNumber = "101" });

        var year1 = new AcademicYear { Name = "2025-2026" };
        var year2 = new AcademicYear { Name = "2026-2027" };
        _ayRepo.Insert(year1);
        _ayRepo.Insert(year2);
        _year1Id = year1.Id;
        _year2Id = year2.Id;
        var fall1 = new Semester { AcademicYearId = year1.Id, Name = "Fall", SortOrder = 0 };
        var fall2 = new Semester { AcademicYearId = year2.Id, Name = "Fall", SortOrder = 0 };
        _semesterRepo.Insert(fall1);
        _semesterRepo.Insert(fall2);
        _fall1 = fall1.Id;
        _fall2 = fall2.Id;

        foreach (var name in new[] { "Adams", "Baker", "Dunn" })
        {
            var i = new Instructor { LastName = name, FirstName = "Pat", Initials = name[..1] };
            _instructorRepo.Insert(i);
            _instructors[name] = i.Id;
        }

        Teach(_biolCourse, _fall1, "B1", "Baker");
        Teach(_chemCourse, _fall1, "C1", "Adams");
        Teach(_physCourse, _fall1, "P1", "Dunn");
        Teach(_biolCourse, _fall2, "B1", "Adams");   // Adams moves to Biology in year 2

        _focus = new FocusService(
            _subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo, _semesterContext, _sectionStore, null);

        var envRepo = new SchedulingEnvironmentRepository(db);
        var campusRepo = new CampusRepository(db);
        var meetingRepo = new MeetingRepository(db);
        var notifier = new GridChangeNotifier();

        _list = new SectionListViewModel(
            _sectionRepo, _courseRepo, _subjectRepo, _instructorRepo, _roomRepo, new LegalStartTimeRepository(db),
            _semesterRepo, new BlockPatternRepository(db), new SectionCodePatternRepository(db), _semesterContext,
            _sectionStore, envRepo, campusRepo, meetingRepo, new NullDialog(), _fx.Lock, _focus);

        _grid = new ScheduleGridViewModel(
            _sectionRepo, _courseRepo, _instructorRepo, _roomRepo, _subjectRepo, envRepo, campusRepo,
            _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)), _sectionStore, new MeetingStore(),
            meetingRepo, notifier, new InstructorCommitmentRepository(db), _fx.Lock, new SharedScheduleService(),
            new AccessPanelViewModel(new ProgramWatchRepository(db), _semesterContext, notifier, envRepo, _courseRepo, () => true),
            _focus);

        // Viewing year 1's fall semester, as the user would after opening the database.
        _semesterContext.Reload(_ayRepo, _semesterRepo, _year1Id, new HashSet<string> { _fall1 });
    }

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _list.Dispose();
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    private sealed class NullDialog : IDialogService
    {
        public System.Threading.Tasks.Task<bool> Confirm(string message, string confirmLabel = "Delete") =>
            System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task ShowError(string message) => System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Inserts a section of the course, taught by the named instructor.</summary>
    private void Teach(Course course, string semesterId, string code, string instructor) =>
        _sectionRepo.Insert(new Section
        {
            CourseId = course.Id,
            SemesterId = semesterId,
            SectionCode = code,
            InstructorAssignments = [new InstructorAssignment { InstructorId = _instructors[instructor] }],
        });

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>The ids of a filter list, sorted, sentinels excluded.</summary>
    private static string[] Ids(IEnumerable<FilterItemViewModel> list) =>
        list.Where(i => !i.IsSentinel).Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>The instructor ids for the named instructors, sorted.</summary>
    private string[] InstructorIds(params string[] names) =>
        names.Select(n => _instructors[n]).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>Ticks the item with this id in the list (raising the filter's change event, as a click does).</summary>
    private static void Tick(IEnumerable<FilterItemViewModel> list, string id) =>
        list.Single(i => i.Id == id).IsSelected = true;

    // ── The lists ──────────────────────────────────────────────────────────────

    /// <summary>Under All every list is the full list, exactly as before the focus existed.</summary>
    [Fact]
    public void UnderAll_ListsAreFull()
    {
        Assert.Equal(["subj-biol", "subj-chem", "subj-phys"], Ids(_grid.Filter.Subjects));
        Assert.Equal(["c-biol", "c-chem", "c-phys"], Ids(_grid.Filter.Courses));
        Assert.Equal(InstructorIds("Adams", "Baker", "Dunn"), Ids(_grid.Filter.Instructors));
        Assert.Equal(InstructorIds("Adams", "Baker", "Dunn"), Ids(_grid.Filter.NamedInstructors));
    }

    /// <summary>Under one subject the three lists keep only that subject's items; the other lists are untouched.</summary>
    [Fact]
    public void UnderOneSubject_ListsAreNarrowed_OtherListsUnchanged()
    {
        var roomsBefore = Ids(_grid.Filter.Rooms);
        Assert.Single(roomsBefore);

        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        Assert.Equal(["subj-biol"], Ids(_grid.Filter.Subjects));
        Assert.Equal(["c-biol"], Ids(_grid.Filter.Courses));
        Assert.Equal(InstructorIds("Baker"), Ids(_grid.Filter.Instructors));
        Assert.Equal(roomsBefore, Ids(_grid.Filter.Rooms));
        Assert.Equal(CourseLevelParser.AllLevels.Count(), _grid.Filter.Levels.Count);
    }

    /// <summary>Under two subjects the lists hold both subjects' items and not the third's.</summary>
    [Fact]
    public void UnderTwoSubjects_ListsHoldBoth()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]));

        Assert.Equal(["subj-biol", "subj-chem"], Ids(_grid.Filter.Subjects));
        Assert.Equal(["c-biol", "c-chem"], Ids(_grid.Filter.Courses));
        Assert.Equal(InstructorIds("Adams", "Baker"), Ids(_grid.Filter.Instructors));
    }

    /// <summary>Under None the three lists are empty (the sentinel rows aside).</summary>
    [Fact]
    public void UnderNone_ListsAreEmpty()
    {
        _focus.SetFocus(SubjectFocus.None);

        Assert.Empty(_grid.Filter.Subjects);
        Assert.Empty(_grid.Filter.Courses);
        Assert.Empty(Ids(_grid.Filter.Instructors));
        Assert.Empty(_grid.Filter.NamedInstructors);
    }

    /// <summary>Widening the focus back to All restores the full lists.</summary>
    [Fact]
    public void WideningToAll_RestoresFullLists()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        _focus.SetFocus(SubjectFocus.All);

        Assert.Equal(["subj-biol", "subj-chem", "subj-phys"], Ids(_grid.Filter.Subjects));
        Assert.Equal(["c-biol", "c-chem", "c-phys"], Ids(_grid.Filter.Courses));
        Assert.Equal(InstructorIds("Adams", "Baker", "Dunn"), Ids(_grid.Filter.NamedInstructors));
    }

    /// <summary>The overlay instructor list (the Instructors list without its sentinels) follows the focus.</summary>
    [Fact]
    public void OverlayInstructorList_FollowsTheFocus()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));

        Assert.Equal(InstructorIds("Adams"), Ids(_grid.Filter.NamedInstructors));
        Assert.DoesNotContain(_grid.Filter.NamedInstructors, i => i.IsSentinel);
    }

    /// <summary>
    /// An instructor overlay whose instructor drops out of the narrowed list is cleared, like a filter tick, so the
    /// grid never keeps drawing an overlay whose summary is blank and whose instructor can't be clicked off.
    /// </summary>
    [Fact]
    public void InstructorOverlayOutsideNewFocus_IsCleared()
    {
        _grid.Filter.SetInstructorOverlay(_instructors["Baker"]);
        Assert.True(_grid.Filter.HasOverlay);

        _focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));   // Baker teaches only Biology

        Assert.False(_grid.Filter.HasOverlay);
        Assert.Equal(string.Empty, _grid.Filter.OverlaySummary);
    }

    /// <summary>An instructor overlay whose instructor is still in the narrowed list stays on, with its summary.</summary>
    [Fact]
    public void InstructorOverlayInsideNewFocus_IsKept()
    {
        _grid.Filter.SetInstructorOverlay(_instructors["Adams"]);

        _focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));   // Adams teaches Chemistry

        Assert.True(_grid.Filter.HasOverlay);
        Assert.Contains("Adams", _grid.Filter.OverlaySummary);
    }

    // ── Ticks on items that drop out ───────────────────────────────────────────

    /// <summary>
    /// A ticked course that falls outside a new focus is unticked; when it was the only tick the filter reads as
    /// inactive, its summary no longer mentions the course, and no sections are highlighted as "filtered".
    /// </summary>
    [Fact]
    public void TickedCourseOutsideNewFocus_IsDropped_AndFilterBecomesInactive()
    {
        Tick(_grid.Filter.Courses, "c-chem");
        Assert.True(_grid.Filter.IsActive);
        Assert.Contains("CHEM101", _grid.Filter.ActiveSummary);

        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        Assert.Empty(_grid.Filter.SelectedCourseIds);
        Assert.False(_grid.Filter.IsActive);
        Assert.False(_grid.Filter.HasRegularFilter);
        Assert.DoesNotContain("CHEM101", _grid.Filter.ActiveSummary);
        Assert.Null(_sectionStore.FilteredSectionIds);
    }

    /// <summary>
    /// With two ticks, the one still in the focus survives and the other is dropped; the summary names only the
    /// survivor.
    /// </summary>
    [Fact]
    public void TickedCourses_OnlyOutOfFocusOneIsDropped()
    {
        Tick(_grid.Filter.Courses, "c-biol");
        Tick(_grid.Filter.Courses, "c-chem");

        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        Assert.Equal(["c-biol"], _grid.Filter.SelectedCourseIds.OrderBy(x => x).ToArray());
        Assert.True(_grid.Filter.IsActive);
        Assert.Contains("BIOL101", _grid.Filter.ActiveSummary);
        Assert.DoesNotContain("CHEM101", _grid.Filter.ActiveSummary);
    }

    /// <summary>A ticked subject and a ticked instructor that drop out are dropped too, and the header summary clears.</summary>
    [Fact]
    public void TickedSubjectAndInstructorOutsideNewFocus_AreDropped()
    {
        Tick(_grid.Filter.Subjects, "subj-chem");
        Tick(_grid.Filter.Instructors, _instructors["Adams"]);
        Assert.Equal("Chemistry", _grid.SubjectFilterSummary);

        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        Assert.Empty(_grid.Filter.SelectedSubjectIds);
        Assert.Empty(_grid.Filter.SelectedInstructorIds);
        Assert.False(_grid.Filter.IsActive);
        Assert.Equal(string.Empty, _grid.SubjectFilterSummary);
        Assert.Equal("No filters active", _grid.Filter.ActiveSummary);
    }

    // ── Rebuilds ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Viewing a different academic year rebuilds the Instructors list: Adams teaches Chemistry in year 1 (out of a
    /// Biology focus) but Biology in year 2 (in it).
    /// </summary>
    [Fact]
    public void ChangingTheViewedYear_RebuildsTheInstructorList()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        Assert.Equal(InstructorIds("Baker"), Ids(_grid.Filter.Instructors));

        _semesterContext.Reload(_ayRepo, _semesterRepo, _year2Id, new HashSet<string> { _fall2 });

        Assert.Equal(InstructorIds("Adams", "Baker"), Ids(_grid.Filter.Instructors));
    }

    /// <summary>Under All the narrowing helper hands back the very same lookups (nothing is copied or filtered).</summary>
    [Fact]
    public void NarrowFilterLookups_UnderAll_ReturnsTheSameLookups()
    {
        var courses = new Dictionary<string, Course> { [_biolCourse.Id] = _biolCourse };
        var lookups = new GridLookups(
            [], courses, courses, new Dictionary<string, Instructor>(), new Dictionary<string, Instructor>(),
            new Dictionary<string, Room>(), new Dictionary<string, Subject>(), new Dictionary<string, Campus>(),
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.Same(lookups, ScheduleGridViewModel.NarrowFilterLookups(lookups, _focus));
    }
}

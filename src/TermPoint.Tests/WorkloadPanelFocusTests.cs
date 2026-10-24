using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the Workload View under a subject focus (spec item 20, step 20.6): which instructors get a row, that a
/// row still lists and totals the instructor's whole load with out-of-focus sections flagged, the hints under None
/// and under an empty Subjects focus, and the reload on a focus change, in single- and multi-semester mode.
/// <para>
/// Real repositories over a temp database (<see cref="ExtensionDataDbFixture"/>); the focus service uses a null
/// database key (in memory only) and is built BEFORE the panel and shares its section store and semester context,
/// exactly as dependency injection arranges in the app.
/// </para>
/// </summary>
public sealed class WorkloadPanelFocusTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Protects settings.json: reloading the semester context saves it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly SchedulingNoteRepository _noteRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    private readonly Subject _biol = new() { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Subject _chem = new() { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" };
    private readonly Subject _phys = new() { Id = "subj-phys", Name = "Physics", CalendarAbbreviation = "PHYS" };
    private readonly Course _biolCourse = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chemCourse = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };

    private readonly string _yearId;
    private readonly string _fall;
    private readonly string _winter;

    /// <summary>
    /// Seeds three subjects (two with a course), one year with two semesters and four active instructors; the
    /// sections are added per test (see <see cref="SeedFall"/>).
    /// </summary>
    public WorkloadPanelFocusTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _releaseRepo = new ReleaseRepository(db);
        _noteRepo = new SchedulingNoteRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);

        _subjectRepo.Insert(_biol);
        _subjectRepo.Insert(_chem);
        _subjectRepo.Insert(_phys);
        _courseRepo.Insert(_biolCourse);
        _courseRepo.Insert(_chemCourse);

        var year = new AcademicYear { Name = "2025-2026" };
        _ayRepo.Insert(year);
        _yearId = year.Id;
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        var winter = new Semester { AcademicYearId = year.Id, Name = "Winter", SortOrder = 1 };
        _semesterRepo.Insert(fall);
        _semesterRepo.Insert(winter);
        _fall = fall.Id;
        _winter = winter.Id;

        foreach (var name in new[] { "Ann", "Bob", "Cy", "Dee" })
            _instructorRepo.Insert(new Instructor { Id = "i-" + name, LastName = name, Initials = name[..1], IsActive = true });
    }

    /// <summary>Releases the database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>A focus service over the real repositories (null key: in memory only), built before the panel.</summary>
    private FocusService NewFocus() =>
        new(_subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo, _semesterContext, _sectionStore, null);

    /// <summary>A real workload panel over the fixture, using <paramref name="focus"/>.</summary>
    private WorkloadPanelViewModel NewPanel(FocusService focus) =>
        new(_instructorRepo, _sectionRepo, _courseRepo, _releaseRepo, _noteRepo, _semesterRepo,
            _semesterContext, _sectionStore, focus);

    /// <summary>Inserts a section of the course into the semester, taught by the instructors (workload 1 each).</summary>
    private void Teach(Course course, string semesterId, string code, params string[] instructorIds) =>
        _sectionRepo.Insert(new Section
        {
            CourseId = course.Id,
            SemesterId = semesterId,
            SectionCode = code,
            InstructorAssignments = instructorIds.Select(i => new InstructorAssignment { InstructorId = i }).ToList(),
        });

    /// <summary>Seeds Ann: Biology; Bob: Chemistry; Cy: Biology and Chemistry in Fall. Dee has nothing.</summary>
    private void SeedFall()
    {
        Teach(_biolCourse, _fall, "B1", "i-Ann");
        Teach(_chemCourse, _fall, "C1", "i-Bob");
        Teach(_biolCourse, _fall, "B2", "i-Cy");
        Teach(_chemCourse, _fall, "C2", "i-Cy");
    }

    /// <summary>
    /// Selects the semesters and reloads the section store, as the section list does in the app (the store raises
    /// SectionsChanged, which rebuilds the panel).
    /// </summary>
    private void Select(params string[] semesterIds)
    {
        _semesterContext.Reload(_ayRepo, _semesterRepo, _yearId, semesterIds.ToHashSet());
        _sectionStore.Reload(_sectionRepo, semesterIds);
    }

    /// <summary>The instructor ids of the rows, in order.</summary>
    private static string[] RowIds(WorkloadPanelViewModel vm) =>
        vm.Rows.Select(r => r.InstructorId).ToArray();

    /// <summary>The row of the instructor with this last name.</summary>
    private static WorkloadRowViewModel Row(WorkloadPanelViewModel vm, string name) =>
        vm.Rows.Single(r => r.InstructorId == "i-" + name);

    // ── Which instructors get a row ────────────────────────────────────────────

    /// <summary>Control: under All every active instructor has a row and nothing is dimmed or hinted.</summary>
    [Fact]
    public void UnderAll_EveryActiveInstructorHasARow_NothingDimmed()
    {
        SeedFall();
        var vm = NewPanel(NewFocus());

        Select(_fall);

        Assert.Equal(["i-Ann", "i-Bob", "i-Cy", "i-Dee"], RowIds(vm));
        Assert.All(vm.Rows.SelectMany(r => r.Items), i => Assert.False(i.IsOutOfFocus));
        Assert.Null(vm.HintMessage);
    }

    /// <summary>An inactive instructor never gets a row, as before the focus existed.</summary>
    [Fact]
    public void InactiveInstructor_HasNoRow()
    {
        _instructorRepo.Insert(new Instructor { Id = "i-Zed", LastName = "Zed", IsActive = false });
        var vm = NewPanel(NewFocus());

        Select(_fall);

        Assert.DoesNotContain("i-Zed", RowIds(vm));
    }

    /// <summary>Under one subject: its teachers and the new hire; the other subject's teacher has no row.</summary>
    [Fact]
    public void UnderOneSubject_OnlyInFocusInstructorsAndTheNewHireGetRows()
    {
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewPanel(focus);

        Select(_fall);

        Assert.Equal(["i-Ann", "i-Cy", "i-Dee"], RowIds(vm));
        Assert.Null(vm.HintMessage);
    }

    /// <summary>Under two subjects every teacher of either has a row.</summary>
    [Fact]
    public void UnderTwoSubjects_TeachersOfEitherGetRows()
    {
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]));
        var vm = NewPanel(focus);

        Select(_fall);

        Assert.Equal(["i-Ann", "i-Bob", "i-Cy", "i-Dee"], RowIds(vm));
    }

    /// <summary>A new hire with no sections appears under any subject focus (the rule's step 6).</summary>
    [Fact]
    public void NewHireWithNoSections_AppearsUnderAnySubjectFocus()
    {
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_phys.Id]));
        var vm = NewPanel(focus);

        Select(_fall);

        Assert.Equal(["i-Dee"], RowIds(vm));
        Assert.Empty(Row(vm, "Dee").Items);
    }

    // ── Hints ──────────────────────────────────────────────────────────────────

    /// <summary>Under None there are no rows and the panel says how to choose subjects.</summary>
    [Fact]
    public void UnderNone_NoRows_ShowsTheNoSubjectsHint()
    {
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var vm = NewPanel(focus);

        Select(_fall);

        Assert.Empty(vm.Rows);
        Assert.Equal(
            "No subjects chosen. Choose subjects in the top bar to see instructors' workloads.", vm.HintMessage);
    }

    /// <summary>A Subjects focus matching no instructor at all says so instead of showing a blank panel.</summary>
    [Fact]
    public void SubjectsFocusMatchingNoInstructor_ShowsTheNoInstructorsHint()
    {
        // Only teachers of other subjects exist (no new hire), so nobody is in focus under Physics.
        _instructorRepo.Delete("i-Dee");
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_phys.Id]));
        var vm = NewPanel(focus);

        Select(_fall);

        Assert.Empty(vm.Rows);
        Assert.Equal("No instructors teach the chosen subjects.", vm.HintMessage);
    }

    /// <summary>Under All with no instructors at all there is no hint: it is simply an empty list.</summary>
    [Fact]
    public void UnderAll_WithNoInstructors_HasNoHint()
    {
        foreach (var name in new[] { "Ann", "Bob", "Cy", "Dee" }) _instructorRepo.Delete("i-" + name);
        var vm = NewPanel(NewFocus());

        Select(_fall);

        Assert.Empty(vm.Rows);
        Assert.Null(vm.HintMessage);
    }

    // ── The whole load ─────────────────────────────────────────────────────────

    /// <summary>
    /// An in-focus instructor who also teaches outside the chosen subjects: the outside section is listed, flagged
    /// out of focus, and counted in the semester and academic-year totals; the in-focus one is not flagged.
    /// </summary>
    [Fact]
    public void InFocusInstructor_ListsAndCountsOutOfFocusSections_Flagged()
    {
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewPanel(focus);

        Select(_fall);

        var cy = Row(vm, "Cy");
        Assert.Equal(2, cy.Items.Count);
        Assert.False(cy.Items.Single(i => i.Label.StartsWith("BIOL101 B2")).IsOutOfFocus);
        Assert.True(cy.Items.Single(i => i.Label.StartsWith("CHEM101 C2")).IsOutOfFocus);
        Assert.Equal(2m, cy.SemesterTotal);
        Assert.Equal(2m, cy.AcademicYearTotal);
    }

    /// <summary>Releases are listed and counted but never flagged out of focus.</summary>
    [Fact]
    public void Releases_AreNeverOutOfFocus()
    {
        SeedFall();
        _releaseRepo.Insert(new Release
        {
            SemesterId = _fall, InstructorId = "i-Cy", Title = "Chair", WorkloadValue = 0.5m,
        });
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewPanel(focus);

        Select(_fall);

        var release = Row(vm, "Cy").Items.Single(i => i.IsRelease);
        Assert.False(release.IsOutOfFocus);
        Assert.Equal(2.5m, Row(vm, "Cy").SemesterTotal);
    }

    // ── Reload ─────────────────────────────────────────────────────────────────

    /// <summary>The panel rebuilds when the focus changes, both ways, including the hint and the dimming.</summary>
    [Fact]
    public void FocusChange_ReloadsThePanel()
    {
        SeedFall();
        var focus = NewFocus();
        var vm = NewPanel(focus);
        Select(_fall);
        Assert.Equal(4, vm.Rows.Count);

        focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));
        Assert.Equal(["i-Bob", "i-Cy", "i-Dee"], RowIds(vm));
        Assert.True(Row(vm, "Cy").Items.Single(i => i.Label.StartsWith("BIOL101")).IsOutOfFocus);

        focus.SetFocus(SubjectFocus.None);
        Assert.Empty(vm.Rows);
        Assert.NotNull(vm.HintMessage);

        focus.SetFocus(SubjectFocus.All);
        Assert.Equal(4, vm.Rows.Count);
        Assert.Null(vm.HintMessage);
        Assert.All(vm.Rows.SelectMany(r => r.Items), i => Assert.False(i.IsOutOfFocus));
    }

    /// <summary>The existing reload paths still work: a section change and a semester change rebuild the rows.</summary>
    [Fact]
    public void SectionsChangedAndSemesterChange_StillReload()
    {
        var vm = NewPanel(NewFocus());
        Select(_fall);
        Assert.Empty(Row(vm, "Ann").Items);

        Teach(_biolCourse, _fall, "B1", "i-Ann");
        _sectionStore.Reload(_sectionRepo, [_fall]);
        Assert.Single(Row(vm, "Ann").Items);

        Select(_winter);
        Assert.Empty(Row(vm, "Ann").Items);
    }

    // ── Multi-semester mode ────────────────────────────────────────────────────

    /// <summary>
    /// In multi-semester mode the same row rule applies, every semester group lists the whole load, and the
    /// out-of-focus sections inside the groups are flagged.
    /// </summary>
    [Fact]
    public void MultiSemester_SameRowRuleAndDimmingInsideGroups()
    {
        SeedFall();
        Teach(_chemCourse, _winter, "C3", "i-Cy");
        Teach(_biolCourse, _winter, "B3", "i-Cy");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewPanel(focus);

        Select(_fall, _winter);

        Assert.Equal(["i-Ann", "i-Cy", "i-Dee"], RowIds(vm));
        var cy = Row(vm, "Cy");
        Assert.True(cy.IsMultiSemesterMode);
        Assert.Equal(2, cy.SemesterGroups.Count);
        var winterItems = cy.SemesterGroups.Single(g => g.SemesterId == _winter).Items;
        Assert.Equal(2, winterItems.Count);
        Assert.True(winterItems.Single(i => i.Label.StartsWith("CHEM101")).IsOutOfFocus);
        Assert.False(winterItems.Single(i => i.Label.StartsWith("BIOL101")).IsOutOfFocus);
        Assert.Equal(2m, cy.SemesterGroups.Single(g => g.SemesterId == _fall).SemesterTotal);
        Assert.Equal(4m, cy.AcademicYearTotal);
    }

    /// <summary>Under None in multi-semester mode there are no rows and the hint shows.</summary>
    [Fact]
    public void MultiSemester_UnderNone_ShowsTheHint()
    {
        SeedFall();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var vm = NewPanel(focus);

        Select(_fall, _winter);

        Assert.Empty(vm.Rows);
        Assert.Equal(WorkloadPanelViewModel.NoSubjectsChosenMessage, vm.HintMessage);
    }
}

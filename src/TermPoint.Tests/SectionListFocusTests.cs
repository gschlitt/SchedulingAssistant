using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the section list under a subject focus (spec item 20, step 20.2): out-of-focus sections get no card
/// (on load, on a focus change, in multi-semester mode and when an Apply moves a section out of the focus), the
/// empty state (<see cref="SectionListViewModel.IsFocusEmpty"/>), and the editor guard
/// (<see cref="FocusService.CanChangeFocus"/> is false while an inline editor is open).
/// <para>
/// Real repositories over a temp database (<see cref="ExtensionDataDbFixture"/>); the focus service uses a null
/// database key so nothing is read from or written to settings, and shares the section store and semester
/// context with the list, as dependency injection arranges in the app.
/// </para>
/// </summary>
public sealed class SectionListFocusTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Protects settings.json: reloading the semester context saves it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly LegalStartTimeRepository _legalRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly BlockPatternRepository _blockPatternRepo;
    private readonly SectionCodePatternRepository _codePatternRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly CampusRepository _campusRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly MeetingRepository _meetingRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    private readonly Subject _biol = new() { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Subject _chem = new() { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" };
    private readonly Course _biolCourse = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chemCourse = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };

    private readonly string _yearId;
    private readonly string _fall;
    private readonly string _winter;

    /// <summary>Builds the repositories and seeds two subjects with a course each, and one year with two semesters.</summary>
    public SectionListFocusTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _roomRepo = new RoomRepository(db);
        _legalRepo = new LegalStartTimeRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _blockPatternRepo = new BlockPatternRepository(db);
        _codePatternRepo = new SectionCodePatternRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        _campusRepo = new CampusRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _meetingRepo = new MeetingRepository(db);

        _subjectRepo.Insert(_biol);
        _subjectRepo.Insert(_chem);
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
    }

    /// <summary>Releases the database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    private sealed class NullDialog : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(false);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>
    /// A focus service over the real repositories (null key: in memory only), built BEFORE the list view model,
    /// as dependency injection does, so its section-store handler runs first.
    /// </summary>
    private FocusService NewFocus() =>
        new(_subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo, _semesterContext, _sectionStore, null);

    /// <summary>
    /// A fake dialog service that records every question asked (message and confirm label) and answers each with
    /// <see cref="Answer"/>.
    /// </summary>
    private sealed class RecordingDialog : IDialogService
    {
        /// <summary>What Confirm returns: true for the confirm button, false for Cancel.</summary>
        public bool Answer { get; set; }

        /// <summary>Every (message, confirmLabel) pair Confirm was called with, in order.</summary>
        public List<(string Message, string ConfirmLabel)> Asked { get; } = new();

        public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        {
            Asked.Add((message, confirmLabel));
            return Task.FromResult(Answer);
        }

        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>A real section list view model over the fixture, using <paramref name="focus"/>.</summary>
    private SectionListViewModel NewList(FocusService focus, IDialogService? dialog = null) =>
        new(_sectionRepo, _courseRepo, _subjectRepo, _instructorRepo, _roomRepo, _legalRepo, _semesterRepo,
            _blockPatternRepo, _codePatternRepo, _semesterContext, _sectionStore, _envRepo, _campusRepo,
            _meetingRepo, dialog ?? new NullDialog(), _fx.Lock, focus);

    /// <summary>Inserts a section of the course into the semester and returns it.</summary>
    private Section AddSection(Course course, string semesterId, string code)
    {
        var s = new Section { CourseId = course.Id, SemesterId = semesterId, SectionCode = code };
        _sectionRepo.Insert(s);
        return s;
    }

    /// <summary>Selects the semesters; the store reloads and the list rebuilds, as in the app.</summary>
    private void Select(params string[] semesterIds) =>
        _semesterContext.Reload(_ayRepo, _semesterRepo, _yearId, semesterIds.ToHashSet());

    /// <summary>The section cards of the list (no banners).</summary>
    private static List<SectionListItemViewModel> Cards(SectionListViewModel vm) =>
        vm.SectionItems.OfType<SectionListItemViewModel>().ToList();

    /// <summary>The section codes of the cards, sorted, for compact assertions.</summary>
    private static string[] Codes(SectionListViewModel vm) =>
        Cards(vm).Select(c => c.Section.SectionCode).OrderBy(c => c).ToArray();

    // ── Hiding ─────────────────────────────────────────────────────────────────

    /// <summary>Control: under All every section has a card, exactly as before the focus existed.</summary>
    [Fact]
    public void UnderAll_EverySectionHasACard()
    {
        AddSection(_biolCourse, _fall, "B1");
        AddSection(_chemCourse, _fall, "C1");
        var vm = NewList(NewFocus());

        Select(_fall);

        Assert.Equal(["B1", "C1"], Codes(vm));
        Assert.False(vm.IsFocusEmpty);
    }

    /// <summary>Under a Subjects focus only sections of focused subjects get cards.</summary>
    [Fact]
    public void UnderSubjects_OnlyInFocusSectionsGetCards()
    {
        AddSection(_biolCourse, _fall, "B1");
        AddSection(_chemCourse, _fall, "C1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);

        Select(_fall);

        Assert.Equal(["B1"], Codes(vm));
        Assert.False(vm.IsFocusEmpty);
    }

    /// <summary>Changing the focus rebuilds the list: the cards follow it both ways.</summary>
    [Fact]
    public void FocusChange_RebuildsTheList()
    {
        AddSection(_biolCourse, _fall, "B1");
        AddSection(_chemCourse, _fall, "C1");
        var focus = NewFocus();
        var vm = NewList(focus);
        Select(_fall);
        Assert.Equal(["B1", "C1"], Codes(vm));

        focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));
        Assert.Equal(["C1"], Codes(vm));

        focus.SetFocus(SubjectFocus.All);
        Assert.Equal(["B1", "C1"], Codes(vm));
    }

    /// <summary>In multi-semester mode the focus applies to every semester, and a banner stays even with no card under it.</summary>
    [Fact]
    public void MultiSemester_FocusAppliesToEach_AndBannerStaysWhenNoneInFocus()
    {
        AddSection(_biolCourse, _fall, "B1");
        AddSection(_chemCourse, _winter, "C1");     // winter has nothing in a Biology focus
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);

        Select(_fall, _winter);

        Assert.Equal(["B1"], Codes(vm));
        Assert.Equal(2, vm.SectionItems.OfType<SemesterBannerViewModel>().Count());
    }

    /// <summary>
    /// Applying an edit that moves a section to a course outside the focus removes its card in place (no crash,
    /// no full rebuild needed), and the editor-open guard is released.
    /// </summary>
    [Fact]
    public async Task Apply_MovingSectionOutOfFocus_RemovesItsCard()
    {
        AddSection(_biolCourse, _fall, "B1");
        AddSection(_biolCourse, _fall, "B2");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);
        Assert.Equal(["B1", "B2"], Codes(vm));

        vm.SelectedItem = Cards(vm).Single(c => c.Section.SectionCode == "B1");
        vm.EditCommand.Execute(null);
        vm.EditVm!.SelectedCourseId = _chemCourse.Id;
        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["B2"], Codes(vm));
        Assert.Equal(_chemCourse.Id, _sectionRepo.GetAll(_fall).Single(s => s.SectionCode == "B1").CourseId);
        Assert.True(focus.CanChangeFocus);
    }

    /// <summary>Applying an edit that keeps the section in the focus keeps its card (control for the removal path).</summary>
    [Fact]
    public async Task Apply_KeepingSectionInFocus_KeepsItsCard()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);

        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Notes = "edited";
        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["B1"], Codes(vm));
        Assert.Equal("edited", Cards(vm).Single().Section.Notes);
    }

    /// <summary>
    /// A brand-new section saved outside the focus simply gets no card (it cannot happen once the course chooser
    /// is restricted, but it must not crash): the list rebuilds without it.
    /// </summary>
    [Fact]
    public async Task Add_NewSectionOutsideFocus_GetsNoCard()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);
        vm.EditVm!.SelectedCourseId = _chemCourse.Id;
        vm.EditVm.SectionCode = "C9";
        vm.EditVm.CommitSectionCode();
        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["B1"], Codes(vm));
        Assert.Contains(_sectionRepo.GetAll(_fall), s => s.SectionCode == "C9");
        Assert.True(focus.CanChangeFocus);
    }

    // ── Empty state ────────────────────────────────────────────────────────────

    /// <summary>Under None the empty state shows, with the "choose subjects" prompt.</summary>
    [Fact]
    public void FocusNone_ShowsEmptyState_WithPrompt()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var vm = NewList(focus);

        Select(_fall);

        Assert.Empty(Cards(vm));
        Assert.True(vm.IsFocusEmpty);
        Assert.Contains("No subjects chosen", vm.FocusEmptyMessage);
    }

    /// <summary>A Subjects focus matching nothing that is loaded shows the "no sections in the chosen subjects" message.</summary>
    [Fact]
    public void SubjectsFocus_MatchingNothing_ShowsEmptyState_WithOtherMessage()
    {
        AddSection(_chemCourse, _fall, "C1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);

        Select(_fall);

        Assert.Empty(Cards(vm));
        Assert.True(vm.IsFocusEmpty);
        Assert.Equal("No sections in the chosen subjects in this semester.", vm.FocusEmptyMessage);
    }

    /// <summary>An empty semester under All (or under a Subjects focus) is an ordinary empty semester, not a focus problem.</summary>
    [Fact]
    public void EmptySemester_UnderAllOrSubjects_IsNotFocusEmpty()
    {
        var focus = NewFocus();
        var vm = NewList(focus);

        Select(_fall);
        Assert.False(vm.IsFocusEmpty);

        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        Assert.False(vm.IsFocusEmpty);
    }

    /// <summary>With no semester selected there is nothing to explain, even under None.</summary>
    [Fact]
    public void NoSemesterSelected_IsNotFocusEmpty()
    {
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var vm = NewList(focus);

        Select();

        Assert.False(vm.IsFocusEmpty);
    }

    /// <summary>IsFocusEmpty raises PropertyChanged when a focus change flips it, and clears when the focus widens.</summary>
    [Fact]
    public void FocusChange_RaisesIsFocusEmpty_AndClearsIt()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var vm = NewList(focus);
        Select(_fall);
        Assert.True(vm.IsFocusEmpty);
        var names = new List<string?>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        focus.SetFocus(SubjectFocus.All);

        Assert.Contains(nameof(SectionListViewModel.IsFocusEmpty), names);
        Assert.False(vm.IsFocusEmpty);
        Assert.Equal(["B1"], Codes(vm));
    }

    // ── Editor guard ───────────────────────────────────────────────────────────

    /// <summary>CanChangeFocus is false while an editor is open, and true again after Apply and after Cancel.</summary>
    [Fact]
    public async Task CanChangeFocus_FalseWhileEditorOpen_TrueAfterApplyAndCancel()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        var vm = NewList(focus);
        Select(_fall);
        Assert.True(focus.CanChangeFocus);

        // Edit, then Cancel.
        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);
        Assert.False(focus.CanChangeFocus);
        Assert.False(focus.SetFocus(SubjectFocus.None));          // refused while editing
        vm.EditVm!.CancelCommand.Execute(null);
        Assert.True(focus.CanChangeFocus);

        // Edit, then Apply.
        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);
        Assert.False(focus.CanChangeFocus);
        await vm.EditVm!.SaveCommand.ExecuteAsync(null);
        Assert.True(focus.CanChangeFocus);

        // Add, then Cancel (the placeholder path).
        await vm.AddCommand.ExecuteAsync(null);
        Assert.False(focus.CanChangeFocus);
        vm.EditVm!.CancelCommand.Execute(null);
        Assert.True(focus.CanChangeFocus);
    }

    /// <summary>A focus change that arrives while an editor is open is refused, so the open editor survives.</summary>
    [Fact]
    public void FocusChangeRefused_WhileEditorOpen_EditorSurvives()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        var vm = NewList(focus);
        Select(_fall);
        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);

        Assert.False(focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id])));

        Assert.NotNull(vm.EditVm);
        Assert.Equal(["B1"], Codes(vm));
    }

    /// <summary>
    /// A store selection of a section that has no card (outside the focus, for example clicked in a grid overlay)
    /// does not wipe the store's selection by pushing the list's cleared selection back.
    /// </summary>
    [Fact]
    public void StoreSelection_OfSectionWithoutCard_IsKept()
    {
        AddSection(_biolCourse, _fall, "B1");
        var hidden = AddSection(_chemCourse, _fall, "C1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);

        _sectionStore.SetSelection(hidden.Id);

        Assert.Contains(hidden.Id, _sectionStore.SelectedSectionIds);
        Assert.Null(vm.SelectedItem);
    }

    // ── Section editor: Subject chooser follows the focus (step 20.5) ──────────

    /// <summary>Adds a third subject (Physics) with a course, so a two-subject focus is not "everything".</summary>
    private (Subject Subject, Course Course) AddPhysics()
    {
        var phys = new Subject { Id = "subj-phys", Name = "Physics", CalendarAbbreviation = "PHYS" };
        var course = new Course { Id = "c-phys", SubjectId = phys.Id, CalendarCode = "PHYS101", Title = "Phys" };
        _subjectRepo.Insert(phys);
        _courseRepo.Insert(course);
        return (phys, course);
    }

    /// <summary>The subject ids the open Add editor offers, sorted.</summary>
    private static string[] OfferedSubjectIds(SectionListViewModel vm) =>
        vm.EditVm!.Subjects.Select(s => s.Id).OrderBy(x => x).ToArray();

    /// <summary>Under All the editor's Subject chooser lists every subject.</summary>
    [Fact]
    public async Task Editor_UnderAll_ListsEverySubject()
    {
        AddPhysics();
        var vm = NewList(NewFocus());
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(["subj-biol", "subj-chem", "subj-phys"], OfferedSubjectIds(vm));
    }

    /// <summary>Under a one-subject focus the chooser lists just that subject, and a new section starts on it.</summary>
    [Fact]
    public async Task Editor_UnderOneSubject_ListsOnlyThatSubject()
    {
        AddPhysics();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(["subj-chem"], OfferedSubjectIds(vm));
        Assert.Equal(_chem.Id, vm.EditVm!.SelectedSubject!.Id);
    }

    /// <summary>Under a two-subject focus the chooser lists those two, not the third.</summary>
    [Fact]
    public async Task Editor_UnderTwoSubjects_ListsThoseTwo()
    {
        AddPhysics();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]));
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(["subj-biol", "subj-chem"], OfferedSubjectIds(vm));
    }

    /// <summary>
    /// Under None no cards exist but Add still works, and (None meaning "nothing chosen yet") it lists every
    /// subject.
    /// </summary>
    [Fact]
    public async Task Editor_UnderNone_AddStillWorks_ListsEverySubject()
    {
        AddPhysics();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.NotNull(vm.EditVm);
        Assert.Equal(["subj-biol", "subj-chem", "subj-phys"], OfferedSubjectIds(vm));
    }

    /// <summary>The Course Number list holds only the chosen subject's courses, whatever the focus.</summary>
    [Fact]
    public async Task Editor_CourseNumbers_OnlyChosenSubjectsCourses()
    {
        _courseRepo.Insert(new Course { Id = "c-biol2", SubjectId = _biol.Id, CalendarCode = "BIOL202", Title = "Bio 2" });
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]));
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);
        vm.EditVm!.SelectedSubject = vm.EditVm.Subjects.Single(s => s.Id == _biol.Id);
        Assert.Equal(["101", "202"], vm.EditVm.CourseNumbers.ToArray());

        vm.EditVm.SelectedSubject = vm.EditVm.Subjects.Single(s => s.Id == _chem.Id);
        Assert.Equal(["101"], vm.EditVm.CourseNumbers.ToArray());
    }

    /// <summary>
    /// An untouched existing section saves with its course unchanged under a focus (the item 3 family of bug: a
    /// chooser that lacked the section's subject would blank its course).
    /// </summary>
    [Fact]
    public async Task Editor_UntouchedSection_SavesWithCourseUnchanged()
    {
        AddSection(_biolCourse, _fall, "B1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);

        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);
        Assert.Equal(_biol.Id, vm.EditVm!.SelectedSubject!.Id);
        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(_biolCourse.Id, _sectionRepo.GetAll(_fall).Single().CourseId);
    }

    /// <summary>
    /// The chooser keeps the section's CURRENT subject when it is outside the focus (an edit or copy of such a
    /// section), in the input order, and drops other out-of-focus subjects.
    /// </summary>
    [Fact]
    public void SubjectsForChooser_KeepsCurrentOutOfFocusSubject()
    {
        var (phys, _) = AddPhysics();
        var all = _subjectRepo.GetAll();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        var offered = focus.SubjectsForChooser(all, currentSubjectId: phys.Id).Select(s => s.Id).OrderBy(x => x);

        Assert.Equal(["subj-biol", "subj-phys"], offered);
        Assert.Equal(["subj-biol"], focus.SubjectsForChooser(all, null).Select(s => s.Id));
    }

    /// <summary>Under All and None the chooser service returns every subject, current or not.</summary>
    [Fact]
    public void SubjectsForChooser_UnderAllOrNone_ReturnsEverySubject()
    {
        AddPhysics();
        var all = _subjectRepo.GetAll();
        var focus = NewFocus();

        Assert.Equal(all.Count, focus.SubjectsForChooser(all, null).Count);

        focus.SetFocus(SubjectFocus.None);
        Assert.Equal(all.Count, focus.SubjectsForChooser(all, null).Count);
    }

    // ── Section editor: Instructor chooser follows the focus (step 20.5) ───────

    /// <summary>
    /// Seeds four instructors and a BIOL and a CHEM section in the viewed year: Baker and Clark teach BIOL (in a
    /// BIOL focus), Adams and Dunn teach only CHEM (out of it). Returns the ids by name.
    /// </summary>
    private Dictionary<string, string> SeedInstructors()
    {
        var ids = new Dictionary<string, string>();
        foreach (var name in new[] { "Adams", "Baker", "Clark", "Dunn" })
        {
            var i = new Instructor { LastName = name, FirstName = "X", Initials = name[..1] };
            _instructorRepo.Insert(i);
            ids[name] = i.Id;
        }
        _sectionRepo.Insert(new Section
        {
            CourseId = _biolCourse.Id, SemesterId = _fall, SectionCode = "B1",
            InstructorAssignments = [new() { InstructorId = ids["Baker"] }, new() { InstructorId = ids["Clark"] }],
        });
        _sectionRepo.Insert(new Section
        {
            CourseId = _chemCourse.Id, SemesterId = _fall, SectionCode = "C1",
            InstructorAssignments = [new() { InstructorId = ids["Adams"] }, new() { InstructorId = ids["Dunn"] }],
        });
        return ids;
    }

    /// <summary>The instructor ids of the open editor's chooser, in display order.</summary>
    private static string[] ChooserIds(SectionListViewModel vm) =>
        vm.EditVm!.InstructorSelections.Select(r => r.Value.Id).ToArray();

    /// <summary>Under a BIOL focus the in-focus instructors come first, then the rest; flags and heading are right.</summary>
    [Fact]
    public async Task InstructorChooser_UnderFocus_ListsInFocusFirstThenOthers()
    {
        var ids = SeedInstructors();
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);

        var original = _instructorRepo.GetAll().Select(i => i.Id).ToList();
        var inFocusIds = new[] { ids["Baker"], ids["Clark"] };
        var expected = original.Where(inFocusIds.Contains).Concat(original.Where(id => !inFocusIds.Contains(id))).ToArray();
        Assert.Equal(expected, ChooserIds(vm));

        var rows = vm.EditVm!.InstructorSelections;
        Assert.Equal([false, false, true, true], rows.Select(r => r.IsOutOfFocus).ToArray());
        Assert.Equal([false, false, true, false], rows.Select(r => r.ShowOthersHeading).ToArray());
    }

    /// <summary>Under All and under None the list keeps its original order with no flags and no heading.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructorChooser_UnderAllOrNone_IsUnchanged(bool none)
    {
        SeedInstructors();
        var focus = NewFocus();
        if (none) focus.SetFocus(SubjectFocus.None);
        var vm = NewList(focus);
        Select(_fall);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(_instructorRepo.GetAll().Select(i => i.Id).ToArray(), ChooserIds(vm));
        Assert.All(vm.EditVm!.InstructorSelections, r =>
        {
            Assert.False(r.IsOutOfFocus);
            Assert.False(r.ShowOthersHeading);
        });
    }

    /// <summary>When every instructor is in focus, or every one is out, there is no heading.</summary>
    [Fact]
    public void InstructorChooser_OneGroupOnly_HasNoHeading()
    {
        var instructors = new List<Instructor> { new() { Id = "a" }, new() { Id = "b" } };
        Section blank() => new();

        SectionEditViewModel Build(IReadOnlySet<string> demoted) => EditVmFor(blank(), instructors, demoted);

        var allIn = Build(new HashSet<string>());
        Assert.DoesNotContain(allIn.InstructorSelections, r => r.ShowOthersHeading || r.IsOutOfFocus);

        var allOut = Build(new HashSet<string> { "a", "b" });
        Assert.All(allOut.InstructorSelections, r => Assert.True(r.IsOutOfFocus));
        Assert.DoesNotContain(allOut.InstructorSelections, r => r.ShowOthersHeading);
    }

    /// <summary>A bare editor view model over the given instructors, with everything else empty.</summary>
    private SectionEditViewModel EditVmFor(Section section, IReadOnlyList<Instructor> instructors, IReadOnlySet<string> demoted) =>
        new(section, isNew: true, isCopy: false, [], [], instructors, [], [], false, false, [], [], [], [], [], [], [],
            (_, _) => false, _ => Task.CompletedTask, _blockPatternRepo, [], outOfFocusInstructorIds: demoted);

    /// <summary>
    /// A section whose current instructor is out of focus (inactive) still lists them, ticked, in the "Other
    /// instructors" group, and an untouched Apply saves identical assignments (ids and workloads).
    /// </summary>
    [Fact]
    public async Task InstructorChooser_CurrentOutOfFocusInstructor_StaysTickedAndSaves()
    {
        var retired = new Instructor { LastName = "Retired", FirstName = "R", Initials = "RR", IsActive = false };
        var active = new Instructor { LastName = "Active", FirstName = "A", Initials = "AA" };
        _instructorRepo.Insert(retired);
        _instructorRepo.Insert(active);
        var section = new Section
        {
            CourseId = _biolCourse.Id, SemesterId = _fall, SectionCode = "B1",
            InstructorAssignments =
            [
                new() { InstructorId = retired.Id, Workload = 0.5m },
                new() { InstructorId = active.Id, Workload = 1m },
            ],
        };
        _sectionRepo.Insert(section);
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var vm = NewList(focus);
        Select(_fall);

        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);

        var rows = vm.EditVm!.InstructorSelections;
        Assert.Equal([active.Id, retired.Id], rows.Select(r => r.Value.Id).ToArray());
        var retiredRow = rows.Single(r => r.Value.Id == retired.Id);
        Assert.True(retiredRow.IsSelected);
        Assert.True(retiredRow.IsOutOfFocus);
        Assert.True(retiredRow.ShowOthersHeading);

        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        var saved = _sectionRepo.GetAll(_fall).Single().InstructorAssignments
            .OrderBy(a => a.InstructorId).Select(a => (a.InstructorId, a.Workload)).ToArray();
        var before = section.InstructorAssignments
            .OrderBy(a => a.InstructorId).Select(a => (a.InstructorId, a.Workload)).ToArray();
        Assert.Equal(before, saved);
    }

    // ── Double-click on a tile whose section has no card (outside the chosen subjects) ─────────

    /// <summary>
    /// Sets up a Biology focus with a hidden Chemistry section (C1) and returns the pieces the tests need. The
    /// dialog answers <paramref name="answer"/> to every question.
    /// </summary>
    private (FocusService Focus, SectionListViewModel List, RecordingDialog Dialog, Section Hidden) HiddenChemSetup(bool answer)
    {
        AddSection(_biolCourse, _fall, "B1");
        var hidden = AddSection(_chemCourse, _fall, "C1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        var dialog = new RecordingDialog { Answer = answer };
        var vm = NewList(focus, dialog);
        Select(_fall);
        Assert.Equal(["B1"], Codes(vm));
        return (focus, vm, dialog, hidden);
    }

    /// <summary>Adds a course with no subject plus one section of it, and refreshes the store so it holds the section.</summary>
    private Section AddOrphanSection()
    {
        var orphanCourse = new Course { Id = "c-orphan", SubjectId = string.Empty, CalendarCode = "ORPH100", Title = "Orphan" };
        _courseRepo.Insert(orphanCourse);
        var orphan = AddSection(orphanCourse, _fall, "O1");
        _sectionStore.Reload(_sectionRepo, [_fall]);
        return orphan;
    }

    /// <summary>A section that has a card opens its editor with no question asked (unchanged behaviour).</summary>
    [Fact]
    public async Task DoubleClick_SectionWithCard_OpensEditorWithoutAsking()
    {
        var (_, vm, dialog, _) = HiddenChemSetup(answer: true);
        var card = Cards(vm).Single();

        await vm.EditSectionByIdAsync(card.Section.Id);

        Assert.Empty(dialog.Asked);
        Assert.NotNull(vm.EditVm);
        Assert.Equal(card.Section.Id, vm.ExpandedItem!.Section.Id);
    }

    /// <summary>A section the store does not hold is ignored: no question, no editor.</summary>
    [Fact]
    public async Task DoubleClick_UnknownSection_DoesNothing()
    {
        var (_, vm, dialog, _) = HiddenChemSetup(answer: true);

        await vm.EditSectionByIdAsync("no-such-section");

        Assert.Empty(dialog.Asked);
        Assert.Null(vm.EditVm);
    }

    /// <summary>
    /// No card: the question names the section and its subject, and offers "Add Chemistry and edit". Cancel changes
    /// nothing at all: the focus, the saved sections, the list, and no editor opens.
    /// </summary>
    [Fact]
    public async Task DoubleClick_NoCard_Cancel_ChangesNothing()
    {
        var (focus, vm, dialog, hidden) = HiddenChemSetup(answer: false);
        var focusBefore = focus.Current;
        var sectionsBefore = _sectionRepo.GetAll(_fall).Count;
        var raised = 0;
        focus.FocusChanged += () => raised++;

        await vm.EditSectionByIdAsync(hidden.Id);

        var asked = Assert.Single(dialog.Asked);
        Assert.Equal("CHEM101 C1 is in Chemistry, which isn't one of your chosen subjects.", asked.Message);
        Assert.Equal("Add Chemistry and edit", asked.ConfirmLabel);
        Assert.Equal(focusBefore, focus.Current);
        Assert.Equal(0, raised);
        Assert.Equal(sectionsBefore, _sectionRepo.GetAll(_fall).Count);
        Assert.Equal(["B1"], Codes(vm));
        Assert.Null(vm.EditVm);
        Assert.True(focus.CanChangeFocus);
    }

    /// <summary>
    /// No card + confirm: the subject joins the chosen subjects, the card appears, and the editor opens for that
    /// section.
    /// </summary>
    [Fact]
    public async Task DoubleClick_NoCard_Confirm_AddsSubject_AndOpensEditor()
    {
        var (focus, vm, dialog, hidden) = HiddenChemSetup(answer: true);

        await vm.EditSectionByIdAsync(hidden.Id);

        Assert.Single(dialog.Asked);
        Assert.Equal(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]), focus.Current);
        Assert.Equal(["B1", "C1"], Codes(vm));
        Assert.NotNull(vm.EditVm);
        Assert.Equal(hidden.Id, vm.ExpandedItem!.Section.Id);
        Assert.False(focus.CanChangeFocus);   // an editor is open
    }

    /// <summary>Under None, confirming gives a one-subject focus on the section's subject and opens its editor.</summary>
    [Fact]
    public async Task DoubleClick_NoCard_Confirm_UnderNone_GivesOneSubjectFocus()
    {
        var hidden = AddSection(_chemCourse, _fall, "C1");
        var focus = NewFocus();
        focus.SetFocus(SubjectFocus.None);
        var dialog = new RecordingDialog { Answer = true };
        var vm = NewList(focus, dialog);
        Select(_fall);
        Assert.Empty(Cards(vm));

        await vm.EditSectionByIdAsync(hidden.Id);

        Assert.Equal(SubjectFocus.ForSubjects([_chem.Id]), focus.Current);
        Assert.Equal(["C1"], Codes(vm));
        Assert.Equal(hidden.Id, vm.ExpandedItem!.Section.Id);
    }

    /// <summary>
    /// An open editor blocks the whole thing exactly as for any other edit request: no question is shown, the
    /// focus does not change, the open editor survives and the usual "finish editing" message appears.
    /// </summary>
    [Fact]
    public async Task DoubleClick_NoCard_WhileEditorOpen_IsBlocked_WithoutAsking()
    {
        var (focus, vm, dialog, hidden) = HiddenChemSetup(answer: true);
        vm.SelectedItem = Cards(vm).Single();
        vm.EditCommand.Execute(null);
        var focusBefore = focus.Current;
        var openEditor = vm.EditVm;
        Assert.NotNull(openEditor);

        await vm.EditSectionByIdAsync(hidden.Id);

        Assert.Empty(dialog.Asked);
        Assert.Equal(focusBefore, focus.Current);
        Assert.Same(openEditor, vm.EditVm);
        Assert.Contains("You are currently editing", vm.LastErrorMessage);
    }

    /// <summary>
    /// A section whose course has no subject cannot be placed in one, so the question offers to show all subjects;
    /// confirming sets All and opens the editor.
    /// </summary>
    [Fact]
    public async Task DoubleClick_NoCard_UnknownSubject_ConfirmShowsAllSubjects()
    {
        var (focus, vm, dialog, _) = HiddenChemSetup(answer: true);
        var orphan = AddOrphanSection();
        Assert.Equal(["B1"], Codes(vm));

        await vm.EditSectionByIdAsync(orphan.Id);

        var asked = Assert.Single(dialog.Asked);
        Assert.Equal("ORPH100 O1 isn't in any of your chosen subjects.", asked.Message);
        Assert.Equal("Show all subjects and edit", asked.ConfirmLabel);
        Assert.Equal(SubjectFocus.All, focus.Current);
        Assert.Equal(["B1", "C1", "O1"], Codes(vm));
        Assert.Equal(orphan.Id, vm.ExpandedItem!.Section.Id);
    }

    /// <summary>The unknown-subject question, answered Cancel, changes nothing.</summary>
    [Fact]
    public async Task DoubleClick_NoCard_UnknownSubject_Cancel_ChangesNothing()
    {
        var (focus, vm, dialog, _) = HiddenChemSetup(answer: false);
        var orphan = AddOrphanSection();
        var focusBefore = focus.Current;

        await vm.EditSectionByIdAsync(orphan.Id);

        Assert.Single(dialog.Asked);
        Assert.Equal(focusBefore, focus.Current);
        Assert.Equal(["B1"], Codes(vm));
        Assert.Null(vm.EditVm);
    }

    /// <summary>
    /// FocusService.AddSubject: joins an existing Subjects focus, becomes the only subject under None, is a no-op
    /// under All, raises FocusChanged once per real change, and is refused while an editor is open.
    /// </summary>
    [Fact]
    public void AddSubject_FollowsTheFocusRules()
    {
        var focus = NewFocus();
        var raised = 0;
        focus.FocusChanged += () => raised++;

        Assert.True(focus.AddSubject(_biol.Id));                       // under All: nothing to add
        Assert.Equal(SubjectFocus.All, focus.Current);
        Assert.Equal(0, raised);

        focus.SetFocus(SubjectFocus.None);
        raised = 0;
        Assert.True(focus.AddSubject(_biol.Id));                       // under None: just that subject
        Assert.Equal(SubjectFocus.ForSubjects([_biol.Id]), focus.Current);
        Assert.Equal(1, raised);

        Assert.True(focus.AddSubject(_chem.Id));                       // joins the others
        Assert.Equal(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]), focus.Current);
        Assert.Equal(2, raised);

        Assert.True(focus.AddSubject(_chem.Id));                       // already there: no change, no event
        Assert.Equal(2, raised);

        focus.SetEditorOpen(true);
        Assert.False(focus.AddSubject("subj-other"));                  // refused while an editor is open
        Assert.Equal(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]), focus.Current);
        Assert.Equal(2, raised);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the Instructors flyout's list and the Workload Mailer's instructor list under a subject focus (spec item
/// 20, step 6): in-focus instructors first, an "Other instructors" heading on the first of the rest, the rest dimmed,
/// the existing sort order kept inside each group, nobody hidden, and (for the mailer) default ticks that follow the
/// focus. All at view-model level over a real temp database; the focus service uses a null database key (in memory
/// only) and is built over the same semester context, as the app arranges.
/// <para>
/// World: subjects BIOL and CHEM; a Biology and a Chemistry course; one academic year with a Fall semester. Adams
/// teaches BIOL, Baker CHEM, Clark both, Davis nothing (so in focus under every Subjects focus), Evans is inactive
/// and taught BIOL. Last-name order is Adams, Baker, Clark, Davis, Evans; initials order is VE, WD, XC, YB, ZA.
/// </para>
/// </summary>
public sealed class InstructorChooserFocusTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Protects settings.json: sort mode, filters and the semester context all save it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SchedulingNoteRepository _noteRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly GridChangeNotifier _changeNotifier = new();

    private readonly Course _biolCourse = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chemCourse = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };

    private readonly string _yearId;
    private readonly string _fall;

    /// <summary>Seeds the world described on the class and views the year, with the sort mode at last name.</summary>
    public InstructorChooserFocusTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        _releaseRepo = new ReleaseRepository(db);
        _commitmentRepo = new InstructorCommitmentRepository(db);
        _noteRepo = new SchedulingNoteRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);

        _subjectRepo.Insert(new Subject { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" });
        _subjectRepo.Insert(new Subject { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" });
        _courseRepo.Insert(_biolCourse);
        _courseRepo.Insert(_chemCourse);

        var year = new AcademicYear { Name = "2025-2026" };
        _ayRepo.Insert(year);
        _yearId = year.Id;
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        _semesterRepo.Insert(fall);
        _fall = fall.Id;

        //                        id      first  last     initials email           active
        AddInstructor("i-Ann",  "Ann",  "Adams", "ZA", "ann@example.edu",  true);
        AddInstructor("i-Bob",  "Bob",  "Baker", "YB", "bob@example.edu",  true);
        AddInstructor("i-Cy",   "Cy",   "Clark", "XC", "cy@example.edu",   true);
        AddInstructor("i-Dee",  "Dee",  "Davis", "WD", "",                 true);
        AddInstructor("i-Eve",  "Eve",  "Evans", "VE", "eve@example.edu",  false);

        Teach(_biolCourse, "B1", "i-Ann");
        Teach(_chemCourse, "C1", "i-Bob");
        Teach(_biolCourse, "B2", "i-Cy");
        Teach(_chemCourse, "C2", "i-Cy");
        Teach(_biolCourse, "B3", "i-Eve");

        _semesterContext.Reload(_ayRepo, _semesterRepo, _yearId, new HashSet<string> { _fall });
        AppSettings.Current.InstructorSortMode = InstructorSortMode.LastName;
    }

    /// <summary>Releases the database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>Inserts one instructor.</summary>
    private void AddInstructor(string id, string first, string last, string initials, string email, bool active) =>
        _instructorRepo.Insert(new Instructor
        {
            Id = id, FirstName = first, LastName = last, Initials = initials, Email = email, IsActive = active,
        });

    /// <summary>Inserts a section of the course in the Fall semester, taught by the instructors.</summary>
    private void Teach(Course course, string code, params string[] instructorIds) =>
        _sectionRepo.Insert(new Section
        {
            CourseId = course.Id,
            SemesterId = _fall,
            SectionCode = code,
            InstructorAssignments = instructorIds.Select(i => new InstructorAssignment { InstructorId = i }).ToList(),
        });

    /// <summary>A focus service over the real repositories (null key: in memory only), set to the given subjects.</summary>
    /// <param name="subjectIds">The subjects to choose; none means the focus stays at All.</param>
    private FocusService NewFocus(params string[] subjectIds)
    {
        var focus = new FocusService(_subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo,
            _semesterContext, _sectionStore, null);
        if (subjectIds.Length > 0) focus.SetFocus(SubjectFocus.ForSubjects(subjectIds));
        return focus;
    }

    /// <summary>A mailer over the fixture using <paramref name="focus"/>.</summary>
    private WorkloadMailerViewModel NewMailer(FocusService focus) =>
        new(_ayRepo, _semesterRepo, _instructorRepo, _sectionRepo, _releaseRepo, _courseRepo, focus);

    /// <summary>The flyout over the fixture, with "show only active" set as asked and the dialog answering as given.</summary>
    private InstructorListViewModel NewFlyout(FocusService focus, bool showOnlyActive = true, bool dialogAnswer = false)
    {
        var vm = new InstructorListViewModel(_instructorRepo, _envRepo, _sectionRepo, _courseRepo, _releaseRepo,
            _commitmentRepo, _noteRepo, _semesterRepo, _ayRepo, _semesterContext, _changeNotifier,
            new FixedAnswerDialog(dialogAnswer), _fx.Lock, NewMailer(focus), focus);
        vm.ShowOnlyActive = showOnlyActive;
        return vm;
    }

    /// <summary>A dialog service that never shows anything and answers every confirmation the same way.</summary>
    private sealed class FixedAnswerDialog(bool answer) : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(answer);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>The first names of the flyout's rows, in order (each seeded instructor's first name is its id without "i-").</summary>
    private static string[] Ids(InstructorListViewModel vm) =>
        vm.InstructorRows.Select(r => r.Value.FirstName).ToArray();

    /// <summary>The ids of the flyout's out-of-focus rows, in order.</summary>
    private static string[] DimmedIds(InstructorListViewModel vm) =>
        vm.InstructorRows.Where(r => r.IsOutOfFocus).Select(r => r.Value.FirstName).ToArray();

    /// <summary>The ids of the flyout's rows that carry the heading (there should be at most one).</summary>
    private static string[] HeadingIds(InstructorListViewModel vm) =>
        vm.InstructorRows.Where(r => r.ShowOthersHeading).Select(r => r.Value.FirstName).ToArray();

    // ══════════════════════════════════════════════════════════════════════════
    // Instructors flyout
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Under one subject: in-focus first, then the rest dimmed, with one heading on the first of them.</summary>
    [Fact]
    public void Flyout_UnderOneSubject_GroupsInFocusFirst_WithOneHeadingOnTheFirstOther()
    {
        var vm = NewFlyout(NewFocus("subj-biol"));

        // Active only: Evans is hidden by the filter, as always. Adams, Clark teach BIOL; Davis is new; Baker is CHEM only.
        Assert.Equal(["Ann", "Cy", "Dee", "Bob"], Ids(vm));
        Assert.Equal(["Bob"], DimmedIds(vm));
        Assert.Equal(["Bob"], HeadingIds(vm));
        Assert.Equal(vm.Instructors.Select(i => i.Id), vm.InstructorRows.Select(r => r.Value.Id));
    }

    /// <summary>Everyone stays in the list, in focus or not; the dimmed rows can be selected and edited.</summary>
    [Fact]
    public void Flyout_NobodyIsHidden_AndADimmedRowCanBeSelectedAndEdited()
    {
        var vm = NewFlyout(NewFocus("subj-biol"));
        Assert.Equal(4, vm.Instructors.Count);

        vm.SelectedRow = vm.InstructorRows.Single(r => r.Value.Id == "i-Bob");

        Assert.Equal("i-Bob", vm.SelectedInstructor?.Id);
        Assert.True(vm.EditCommand.CanExecute(null));
        vm.EditCommand.Execute(null);
        Assert.NotNull(vm.EditVm);
        Assert.Equal("Bob", vm.EditVm!.FirstName);
    }

    /// <summary>The sort mode orders inside each group: last name, then initials.</summary>
    [Fact]
    public void Flyout_KeepsTheSortOrderInsideEachGroup_ForTwoSortModes()
    {
        var vm = NewFlyout(NewFocus("subj-biol"), showOnlyActive: false);

        // Last name: Adams, Baker, Clark, Davis, Evans -> in focus Adams, Clark, Davis; the rest Baker, Evans.
        vm.SetSortMode(InstructorSortMode.LastName);
        Assert.Equal(["Ann", "Cy", "Dee", "Bob", "Eve"], Ids(vm));
        Assert.Equal(["Bob", "Eve"], DimmedIds(vm));
        Assert.Equal(["Bob"], HeadingIds(vm));

        // Initials: VE, WD, XC, YB, ZA -> in focus Dee (WD), Cy (XC), Ann (ZA); the rest Eve (VE), Bob (YB).
        vm.SetSortMode(InstructorSortMode.Initials);
        Assert.Equal(["Dee", "Cy", "Ann", "Eve", "Bob"], Ids(vm));
        Assert.Equal(["Eve", "Bob"], DimmedIds(vm));
        Assert.Equal(["Eve"], HeadingIds(vm));
    }

    /// <summary>"Show only active" applies first: ticking it off adds inactive instructors to the second group.</summary>
    [Fact]
    public void Flyout_ShowOnlyActive_AppliesBeforeTheGrouping()
    {
        var vm = NewFlyout(NewFocus("subj-biol"), showOnlyActive: true);
        Assert.DoesNotContain("Eve", Ids(vm));

        vm.ShowOnlyActive = false;

        // Inactive Evans is never in focus (rule step 3), so she joins the dimmed group, after Baker.
        Assert.Equal(["Ann", "Cy", "Dee", "Bob", "Eve"], Ids(vm));
        Assert.Equal(["Bob", "Eve"], DimmedIds(vm));
        Assert.Equal(["Bob"], HeadingIds(vm));
    }

    /// <summary>Under All and under None the list is exactly today's: sorted, no heading, nothing dimmed.</summary>
    [Fact]
    public void Flyout_UnderAllAndNone_IsUnchanged()
    {
        var focus = NewFocus();
        var vm = NewFlyout(focus);

        Assert.Equal(["Ann", "Bob", "Cy", "Dee"], Ids(vm));
        Assert.Empty(DimmedIds(vm));
        Assert.Empty(HeadingIds(vm));

        focus.SetFocus(SubjectFocus.None);

        Assert.Equal(["Ann", "Bob", "Cy", "Dee"], Ids(vm));
        Assert.Empty(DimmedIds(vm));
        Assert.Empty(HeadingIds(vm));
    }

    /// <summary>A focus change regroups the open flyout and keeps the selected instructor selected.</summary>
    [Fact]
    public void Flyout_FocusChange_RegroupsAndKeepsTheSelection()
    {
        var focus = NewFocus("subj-biol");
        var vm = NewFlyout(focus);
        vm.SelectedRow = vm.InstructorRows.Single(r => r.Value.Id == "i-Cy");

        focus.SetFocus(SubjectFocus.ForSubjects(["subj-chem"]));

        // CHEM: Baker and Clark teach it; Davis is new; Adams is BIOL only.
        Assert.Equal(["Bob", "Cy", "Dee", "Ann"], Ids(vm));
        Assert.Equal(["Ann"], DimmedIds(vm));
        Assert.Equal(["Ann"], HeadingIds(vm));
        Assert.Equal("i-Cy", vm.SelectedInstructor?.Id);
        Assert.Equal("i-Cy", vm.SelectedRow?.Value.Id);

        // And a row of the new list can still be selected.
        vm.SelectedRow = vm.InstructorRows.Single(r => r.Value.Id == "i-Ann");
        Assert.Equal("i-Ann", vm.SelectedInstructor?.Id);
    }

    /// <summary>Selecting an instructor in code selects the matching row, and the other way round.</summary>
    [Fact]
    public void Flyout_SelectedInstructorAndSelectedRow_StayInStep()
    {
        var vm = NewFlyout(NewFocus("subj-biol"));

        vm.SelectedInstructor = vm.Instructors.Single(i => i.Id == "i-Dee");
        Assert.Equal("i-Dee", vm.SelectedRow?.Value.Id);

        vm.SelectedRow = null;
        Assert.Null(vm.SelectedInstructor);
    }

    /// <summary>
    /// A newly added instructor has no sections, so the focus rule puts them in the top group under any Subjects
    /// focus; they are selected after the save, and the list is regrouped around them.
    /// </summary>
    [Fact]
    public void Flyout_ANewInstructor_LandsInTheTopGroup_AndIsSelected()
    {
        var vm = NewFlyout(NewFocus("subj-biol"));

        vm.AddCommand.Execute(null);
        vm.EditVm!.FirstName = "Nia";
        vm.EditVm.LastName = "Nolan";
        Assert.True(vm.EditVm.SaveCommand.CanExecute(null));
        vm.EditVm.SaveCommand.Execute(null);

        // Last-name order inside the top group: Adams, Clark, Davis, Nolan; Baker stays below the heading.
        Assert.Equal(["Ann", "Cy", "Dee", "Nia", "Bob"], Ids(vm));
        Assert.Equal(["Bob"], DimmedIds(vm));
        Assert.Equal("Nia", vm.SelectedInstructor?.FirstName);
        Assert.Equal("Nia", vm.SelectedRow?.Value.FirstName);
    }

    /// <summary>Deleting an instructor still works from the grouped list.</summary>
    [Fact]
    public async Task Flyout_Delete_StillWorksFromTheGroupedList()
    {
        var vm = NewFlyout(NewFocus("subj-biol"), dialogAnswer: true);
        vm.AddCommand.Execute(null);
        vm.EditVm!.FirstName = "Tmp";
        vm.EditVm.LastName = "Temp";
        vm.EditVm.SaveCommand.Execute(null);
        Assert.Contains("Tmp", Ids(vm));
        Assert.Equal("Tmp", vm.SelectedRow?.Value.FirstName);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Tmp", Ids(vm));
        Assert.Equal(["Ann", "Cy", "Dee", "Bob"], Ids(vm));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Workload Mailer
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>The mailer's first names in list order.</summary>
    private static string[] MailerIds(WorkloadMailerViewModel vm) =>
        vm.InstructorItems.Select(i => i.Instructor.FirstName).ToArray();

    /// <summary>The mailer's ticked ids, in list order.</summary>
    private static string[] Ticked(WorkloadMailerViewModel vm) =>
        vm.InstructorItems.Where(i => i.IsSelected).Select(i => i.Instructor.FirstName).ToArray();

    /// <summary>Under one subject the mailer groups like the flyout; everyone is listed, inactive ones included.</summary>
    [Fact]
    public void Mailer_UnderOneSubject_GroupsInFocusFirst_WithOneHeading()
    {
        var vm = NewMailer(NewFocus("subj-biol"));

        // Inactive Evans is listed (the mailer lists everyone) and is out of focus. Last-name order inside each group.
        Assert.Equal(["Ann", "Cy", "Dee", "Bob", "Eve"], MailerIds(vm));
        Assert.Equal(["Bob", "Eve"], vm.InstructorItems.Where(i => i.IsOutOfFocus).Select(i => i.Instructor.FirstName));
        Assert.Equal(["Bob"], vm.InstructorItems.Where(i => i.ShowOthersHeading).Select(i => i.Instructor.FirstName));
    }

    /// <summary>Under a Subjects focus only in-focus instructors (active, with an email) start ticked.</summary>
    [Fact]
    public void Mailer_UnderOneSubject_OnlyInFocusInstructorsWithAnEmailStartTicked()
    {
        var vm = NewMailer(NewFocus("subj-biol"));

        // Davis is in focus but has no email; Baker has an email but is out of focus; Evans is inactive.
        Assert.Equal(["Ann", "Cy"], Ticked(vm));
    }

    /// <summary>Under All and under None the grouping and the default ticks are as they always were.</summary>
    [Fact]
    public void Mailer_UnderAllAndNone_DefaultsAreUnchanged()
    {
        var focus = NewFocus();
        var vm = NewMailer(focus);

        Assert.Equal(["Ann", "Bob", "Cy", "Dee", "Eve"], MailerIds(vm));
        Assert.Equal(["Ann", "Bob", "Cy"], Ticked(vm));          // active with an email
        Assert.DoesNotContain(vm.InstructorItems, i => i.IsOutOfFocus || i.ShowOthersHeading);

        focus.SetFocus(SubjectFocus.None);
        vm = NewMailer(focus);

        Assert.Equal(["Ann", "Bob", "Cy", "Dee", "Eve"], MailerIds(vm));
        Assert.Equal(["Ann", "Bob", "Cy"], Ticked(vm));
        Assert.DoesNotContain(vm.InstructorItems, i => i.IsOutOfFocus || i.ShowOthersHeading);
    }

    /// <summary>"All" and "None" buttons behave as today: All ticks everyone with an email, out of focus or not.</summary>
    [Fact]
    public void Mailer_SelectAllAndSelectNone_BehaveAsToday()
    {
        var vm = NewMailer(NewFocus("subj-biol"));

        vm.SelectAllInstructorsCommand.Execute(null);
        Assert.Equal(["Ann", "Cy", "Bob", "Eve"], Ticked(vm));   // list order; Davis has no email

        vm.DeselectAllInstructorsCommand.Execute(null);
        Assert.Empty(Ticked(vm));
    }

    /// <summary>The open flyout rebuilds the mailer's list when the subjects change, and the ticks follow.</summary>
    [Fact]
    public void Mailer_FollowsAFocusChangeMadeWhileTheFlyoutIsOpen()
    {
        var focus = NewFocus("subj-biol");
        var flyout = NewFlyout(focus);
        Assert.Equal(["Ann", "Cy"], Ticked(flyout.WorkloadMailerVm));

        focus.SetFocus(SubjectFocus.ForSubjects(["subj-chem"]));

        Assert.Equal(["Bob", "Cy", "Dee", "Ann", "Eve"], MailerIds(flyout.WorkloadMailerVm));
        Assert.Equal(["Bob", "Cy"], Ticked(flyout.WorkloadMailerVm));

        focus.SetFocus(SubjectFocus.All);
        Assert.Equal(["Ann", "Bob", "Cy"], Ticked(flyout.WorkloadMailerVm));
    }

    /// <summary>The emails still describe the whole load: Clark's email lists both subjects under a BIOL focus.</summary>
    [Fact]
    public void Mailer_TheEmailStillDescribesTheWholeLoad_UnderAFocus()
    {
        var vm = NewMailer(NewFocus("subj-biol"));
        vm.BodyTemplate = "{Workload}";
        vm.SubjectTemplate = "x";
        vm.DeselectAllInstructorsCommand.Execute(null);
        vm.InstructorItems.Single(i => i.Instructor.Id == "i-Cy").IsSelected = true;

        vm.BeginSendingCommand.Execute(null);

        Assert.Contains("BIOL101 B2", vm.RenderedBody);
        Assert.Contains("CHEM101 C2", vm.RenderedBody);
    }
}

using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.Services.SectionImport;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Access Analyzer piece E, the screens that carry the setup: the Access tab's invitation, the eye before setup,
/// Scheduling Environment's Component boxes, Courses and Edit Course, Copy Semester's two lines and the import's
/// pre-fill of new courses. Real repositories over a temp database (<see cref="AccessSetupRig"/>).
/// </summary>
public sealed class AccessSetupScreensTests : IDisposable
{
    private readonly AccessSetupRig _rig = new();

    /// <inheritdoc/>
    public void Dispose() => _rig.Dispose();

    private sealed class NullDialog : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(false);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Lists(params (string Course, string[] Types)[] rows) =>
        rows.ToDictionary(r => r.Course, r => (IReadOnlyList<string>)r.Types);

    /// <summary>Does the setup the way the wizard would: Lecture and Lab are components; BIOL101 has both, CHEM201 and PHYS100 one, HIST101 none.</summary>
    private void DoSetup() =>
        _rig.Service().SaveSetup(new HashSet<string> { "st-lec", "st-lab" },
            Lists((_rig.Biol.Id, ["st-lec", "st-lab"]), (_rig.Chem.Id, ["st-lec"]), (_rig.Phys.Id, ["st-lec"]), (_rig.Hist.Id, [])));

    // ── Access tab ────────────────────────────────────────────────────────────

    private AccessTabViewModel NewTab(AccessSetupService service) =>
        AccessStateTestSupport.TabFor(_rig.Fx.Db, _rig.State, new SemesterContext(), _rig.Store,
            new AccessWatchToggler(_rig.Env, _rig.State, _rig.Notifier, () => _rig.CanWrite), service, out _);

    /// <summary>Before the setup the tab invites it (naming the likely components); a read-only computer gets the quiet line; after the setup neither shows.</summary>
    [Fact]
    public void Tab_InvitesTheSetup_UntilItIsDone()
    {
        var service = _rig.Service();
        var tab = NewTab(service);

        Assert.True(tab.ShowSetupInvite);
        Assert.False(tab.ShowSetupReadOnlyLine);
        Assert.Equal("Two quick questions before using access watches", tab.SetupInviteTitle);
        Assert.Equal("Set up", tab.SetupInviteButton);
        Assert.Equal("What's an access watch?", tab.WhatIsAWatchLink);
        Assert.False(tab.IsSetUp);   // hides Add a watch and Mark all conflicts

        _rig.CanWrite = false;
        service.CanSaveChanged += () => { };
        tab.Refresh();
        Assert.False(tab.ShowSetupInvite);
        Assert.True(tab.ShowSetupReadOnlyLine);
        Assert.Contains("Until someone editing it sets them up", tab.SetupReadOnlyText);
        Assert.False(tab.SetUpCommand.CanExecute(null));

        _rig.CanWrite = true;
        service.SaveNoComponents();   // the state change refreshes the tab
        Assert.True(tab.IsSetUp);
        Assert.False(tab.ShowSetupInvite);
        Assert.False(tab.ShowSetupReadOnlyLine);
    }

    /// <summary>Set up asks the main window to open the wizard (no continuation).</summary>
    [Fact]
    public void Tab_SetUp_RequestsTheWizard()
    {
        var service = _rig.Service();
        var tab = NewTab(service);
        var asked = 0;
        Action? then = () => { };
        service.SetupWizardRequested += t => { asked++; then = t; };

        tab.SetUpCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.Null(then);
    }

    // ── The eye before setup ──────────────────────────────────────────────────

    private GridFilterViewModel NewFilter(AccessSetupService service)
    {
        var vm = new GridFilterViewModel
        {
            WatchToggler = new AccessWatchToggler(_rig.Env, _rig.State, _rig.Notifier, () => _rig.CanWrite),
            SetupService = service,
        };
        vm.PopulateOptions(
            new Dictionary<string, Instructor>(), new Dictionary<string, Room>(), new Dictionary<string, Subject>(),
            new Dictionary<string, Campus>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            _rig.Env.GetAll(SchedulingEnvironmentTypes.Tag).ToDictionary(t => t.Id),
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, string>(),
            new Dictionary<string, Course>());
        return vm;
    }

    /// <summary>The first eye before setup opens the wizard and makes no watch until the wizard finishes; cancelling makes none.</summary>
    [Fact]
    public void Filter_FirstEye_OpensTheWizard_AndTheWatchWaitsForIt()
    {
        var service = _rig.Service();
        Action? then = null;
        service.SetupWizardRequested += t => then = t;
        var vm = NewFilter(service);
        var cohort = vm.Tags.Single(t => t.Name == "Cohort");

        vm.ToggleTagWatchCommand.Execute(cohort);

        Assert.NotNull(then);
        Assert.False(cohort.IsWatched);
        Assert.False(_rig.Env.GetById(_rig.CohortTag.Id)!.IsAccessWatch);

        then!();   // the wizard finished
        Assert.True(cohort.IsWatched);
        Assert.True(_rig.Env.GetById(_rig.CohortTag.Id)!.IsAccessWatch);
    }

    /// <summary>After the setup the eye just toggles; turning a watch off never asks.</summary>
    [Fact]
    public void Filter_Eye_AfterSetup_JustToggles()
    {
        _rig.Service().SaveNoComponents();
        var service = _rig.Service();
        var asked = 0;
        service.SetupWizardRequested += _ => asked++;
        var vm = NewFilter(service);

        vm.ToggleTagWatchCommand.Execute(vm.Tags.Single(t => t.Name == "Cohort"));
        vm.ToggleTagWatchCommand.Execute(vm.Tags.Single(t => t.Name == "Watched"));

        Assert.Equal(0, asked);
        Assert.True(_rig.Env.GetById(_rig.CohortTag.Id)!.IsAccessWatch);
        Assert.False(_rig.Env.GetById(_rig.WatchedTag.Id)!.IsAccessWatch);
    }

    private SchedulingEnvironmentListViewModel NewEnvList(string type, string name, AccessSetupService service) =>
        new(type, name, _rig.Env, _rig.Sections, _rig.Courses, new InstructorRepository(_rig.Fx.Db), _rig.Fx.Db,
            null!, new NullDialog(), _rig.Fx.Lock,
            watchToggler: new AccessWatchToggler(_rig.Env, _rig.State, _rig.Notifier, () => _rig.CanWrite),
            changeNotifier: _rig.Notifier, setupService: service);

    /// <summary>In Scheduling Environment, Tags, the first eye before setup also opens the wizard first.</summary>
    [Fact]
    public void SchedulingEnvironmentTags_FirstEye_OpensTheWizard()
    {
        var service = _rig.Service();
        Action? then = null;
        service.SetupWizardRequested += t => then = t;
        var list = NewEnvList(SchedulingEnvironmentTypes.Tag, "Tags", service);

        list.ToggleWatchCommand.Execute(list.Items.Single(i => i.Name == "Cohort"));

        Assert.NotNull(then);
        Assert.False(_rig.Env.GetById(_rig.CohortTag.Id)!.IsAccessWatch);
        then!();
        Assert.True(_rig.Env.GetById(_rig.CohortTag.Id)!.IsAccessWatch);
    }

    // ── Scheduling Environment, Section Type: the Component box ───────────────

    /// <summary>Before the setup the boxes work and show TermPoint's guess (Lecture and Lab), with nothing stored.</summary>
    [Fact]
    public void SectionTypeBoxes_BeforeSetup_ShowTheGuess_AndWork()
    {
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", _rig.Service());

        Assert.True(list.ShowComponentColumn);
        Assert.True(list.CanChangeComponents);
        Assert.True(list.ComponentRows.Single(r => r.Name == "Lecture").IsEnabled);
        Assert.Equal(new[] { true, true, false, false }, list.Items.Select(v => v.ComponentBoxChecked));
        Assert.Equal(new[] { true, true, false, false }, list.ComponentRows.Select(r => r.IsComponent));
        Assert.False(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.StartsWith("Ticked: a course can have this type as one of its components", list.ComponentHeadingTip);
        Assert.True(list.ShowComponentGuessNote);
        Assert.Equal("The Component ticks are TermPoint's guess, based on the type names. Change any that are wrong.",
            list.ComponentGuessNote);
    }

    /// <summary>
    /// Ticking a box before the setup saves the guess plus that type and counts as the setup; courses' lists stay
    /// unset, so there is no "which courses have it?" step and the user stays in the list.
    /// </summary>
    [Fact]
    public void SectionTypeBox_TickBeforeSetup_SavesTheGuessPlusTheType_AndCountsAsSetup()
    {
        var service = _rig.Service();
        string? asked = null;
        service.NewTypeWizardRequested += id => asked = id;
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", service);

        list.ComponentRows.Single(r => r.Name == "Thesis").IsComponent = true;

        Assert.True(service.IsSetUp);
        Assert.True(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.True(_rig.Stored(_rig.Lab).IsComponent);
        Assert.True(_rig.Stored(_rig.Thesis).IsComponent);
        Assert.False(_rig.Stored(_rig.Online).IsComponent);
        Assert.Null(_rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Null(asked);
        Assert.Equal(new[] { true, true, false, true }, list.Items.Select(v => v.ComponentBoxChecked));
        Assert.False(list.ShowComponentGuessNote);
    }

    /// <summary>Unticking a guessed box before the setup saves the rest of the guess, counts as the setup, and asks nothing.</summary>
    [Fact]
    public void SectionTypeBox_UntickBeforeSetup_SavesTheRestOfTheGuess()
    {
        var service = _rig.Service();
        string? asked = null;
        service.NewTypeWizardRequested += id => asked = id;
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", service);
        var lab = list.ComponentRows.Single(r => r.Name == "Lab");

        lab.IsComponent = false;

        Assert.True(service.IsSetUp);
        Assert.True(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.False(_rig.Stored(_rig.Lab).IsComponent);
        Assert.Null(list.PendingAsk);
        Assert.Null(asked);
    }

    /// <summary>The description of the Section Type category carries the Component sentence.</summary>
    [Fact]
    public void SectionTypeDescription_MentionsComponent() =>
        Assert.Contains("\n\nSelect 'Component' to indicate those section types", AccessText.SectionTypeDescription);

    /// <summary>Ticking marks the type and asks the main window for the new-type step.</summary>
    [Fact]
    public void SectionTypeBox_Tick_MarksTheType_AndRequestsTheNewTypeWizard()
    {
        DoSetup();
        var service = _rig.Service();
        string? asked = null;
        service.NewTypeWizardRequested += id => asked = id;
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", service);
        Assert.True(list.CanChangeComponents);

        list.ComponentRows.Single(r => r.Name == "Online").IsComponent = true;

        Assert.True(_rig.Stored(_rig.Online).IsComponent);
        Assert.Equal("st-onl", asked);
    }

    /// <summary>After the setup, ticking a type when no course has its components set saves without the new-type step.</summary>
    [Fact]
    public void SectionTypeBox_Tick_WithNoCourseListsSet_DoesNotAsk()
    {
        _rig.Service().SaveSetup(new HashSet<string> { _rig.Lecture.Id }, new Dictionary<string, IReadOnlyList<string>>());
        var service = _rig.Service();
        string? asked = null;
        service.NewTypeWizardRequested += id => asked = id;
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", service);

        list.ComponentRows.Single(r => r.Name == "Online").IsComponent = true;

        Assert.True(_rig.Stored(_rig.Online).IsComponent);
        Assert.Null(asked);
        Assert.False(list.ShowComponentGuessNote);
    }

    /// <summary>Unticking a type that no course lists takes it off at once, without asking.</summary>
    [Fact]
    public void SectionTypeBox_Untick_WithNoCourses_UnmarksAtOnce()
    {
        DoSetup();
        _rig.Service().MarkComponent(_rig.Online.Id);
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", _rig.Service());
        var row = list.ComponentRows.Single(r => r.Name == "Online");

        row.IsComponent = false;

        Assert.False(row.IsAsking);
        Assert.False(_rig.Stored(_rig.Online).IsComponent);
    }

    /// <summary>Unticking a type that courses list asks first, naming them; Cancel puts the box back and saves nothing.</summary>
    [Fact]
    public void SectionTypeBox_Untick_WithCourses_AsksFirst_AndCancelPutsItBack()
    {
        DoSetup();
        _rig.Service().SaveCourseComponents(Lists((_rig.Phys.Id, ["st-lec", "st-lab"])));
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", _rig.Service());
        var row = list.ComponentRows.Single(r => r.Name == "Lab");
        var before = _rig.TotalChanges();

        row.IsComponent = false;

        Assert.True(row.IsAsking);
        Assert.Equal("Stop counting Lab as a component? It comes off BIOL101 and PHYS100.", row.AskText);
        Assert.Equal(before, _rig.TotalChanges());

        row.CancelUnmarkCommand.Execute(null);

        Assert.False(row.IsAsking);
        Assert.True(row.IsComponent);
        Assert.True(_rig.Stored(_rig.Lab).IsComponent);
        Assert.Equal(before, _rig.TotalChanges());
    }

    /// <summary>"Stop counting it" unmarks the type and removes it from the courses that list it.</summary>
    [Fact]
    public void SectionTypeBox_Untick_Confirm_UnmarksAndRemovesFromCourses()
    {
        DoSetup();
        _rig.Service().SaveCourseComponents(Lists((_rig.Phys.Id, ["st-lec", "st-lab"])));
        var list = NewEnvList(SchedulingEnvironmentTypes.SectionType, "Section Type", _rig.Service());
        var row = list.ComponentRows.Single(r => r.Name == "Lab");
        row.IsComponent = false;

        row.ConfirmUnmarkCommand.Execute(null);

        Assert.False(_rig.Stored(_rig.Lab).IsComponent);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Phys).ComponentTypeIds);
        Assert.All(list.ComponentRows, r => Assert.False(r.IsAsking));
    }

    /// <summary>More than four courses are counted, not named.</summary>
    [Fact]
    public void StopCountingAsk_CountsWhenMoreThanFourCourses()
    {
        Assert.Equal("Stop counting Lab as a component? It comes off 5 courses.",
            AccessText.StopCountingAsk("Lab", ["A", "B", "C", "D", "E"]));
        Assert.Equal("Stop counting Lab as a component? It comes off A, B, C and D.",
            AccessText.StopCountingAsk("Lab", ["A", "B", "C", "D"]));
    }

    // ── Courses and Edit Course ───────────────────────────────────────────────

    private sealed record CourseRig(CourseListViewModel Vm, Subject Biology);

    private CourseListViewModel NewCourseList(AccessSetupService service)
    {
        var db = _rig.Fx.Db;
        var subjects = new SubjectRepository(db);
        if (subjects.GetAll().Count == 0)
        {
            var biology = new Subject { Id = "s-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
            subjects.Insert(biology);
            var biol = _rig.Courses.GetById(_rig.Biol.Id)!;
            biol.SubjectId = biology.Id;
            _rig.Courses.Update(biol);
        }
        AppSettings.Current.ComponentsColumnOpen = null;
        return new CourseListViewModel(_rig.Courses, subjects, _rig.Env, new NullDialog(), _rig.Sections,
            new SemesterRepository(db), new AcademicYearRepository(db), new InstructorRepository(db), _rig.Fx.Lock, _rig.Store,
            new SubjectColorStore(new[] { "#B7E79D" }, () => new AppSettings()),
            FocusTestSupport.NoColours(_rig.Courses, subjects, _rig.Store), new FakeSectionCardState(), service, _rig.State);
    }

    /// <summary>Before the setup the Components column is hidden and the rows carry no texts.</summary>
    [Fact]
    public void Courses_BeforeSetup_HaveNoComponentsColumn()
    {
        var vm = NewCourseList(_rig.Service());

        Assert.False(vm.ShowComponentsColumn);
        Assert.All(vm.Courses, c => Assert.Equal(string.Empty, c.ComponentsText));
        Assert.All(vm.Courses, c => Assert.Null(c.MismatchText));
    }

    /// <summary>After the setup each course names its components (empty for none), and a component-type section the course does not list is spoken of.</summary>
    [Fact]
    public void Courses_AfterSetup_ShowComponentsText_AndMismatchText()
    {
        DoSetup();
        var vm = NewCourseList(_rig.Service());

        Assert.True(vm.ShowComponentsColumn);
        string Text(Course c) => vm.Courses.Single(x => x.Id == c.Id).ComponentsText;
        string? Mismatch(Course c) => vm.Courses.Single(x => x.Id == c.Id).MismatchText;
        Assert.Equal("Lecture, Lab", Text(_rig.Biol));
        Assert.Equal("Lecture", Text(_rig.Chem));
        Assert.Equal(string.Empty, Text(_rig.Hist));
        Assert.Equal("Lecture", Text(_rig.Phys));
        Assert.Null(Mismatch(_rig.Biol));
        Assert.Null(Mismatch(_rig.Hist));
        Assert.Equal("PHYS100 L1 is a Lab section, but Lab isn't one of PHYS100's components.", Mismatch(_rig.Phys));
        Assert.Equal(AccessText.ComponentsColumnTip, vm.ComponentsColumnTip);
    }

    /// <summary>The mismatch sentence's plural form names several sections.</summary>
    [Fact]
    public void MismatchSentence_Plural()
    {
        Assert.Equal("BIOL305 L1 and L2 are Lab sections, but Lab isn't one of BIOL305's components.",
            AccessText.MismatchSentence("BIOL305", "Lab", ["L1", "L2"]));
        Assert.Equal("BIOL305 X1 is an Online section, but Online isn't one of BIOL305's components.",
            AccessText.MismatchSentence("BIOL305", "Online", ["X1"]));
    }

    /// <summary>The column starts open when some course has two or more components, follows the user's choice after that, and the choice is stored per computer.</summary>
    [Fact]
    public void Courses_ComponentsColumn_StartsOpenWhenAnyCourseHasTwo_AndRemembersTheChoice()
    {
        DoSetup();
        var vm = NewCourseList(_rig.Service());
        Assert.True(vm.ComponentsColumnOpen);

        vm.ComponentsColumnOpen = false;

        Assert.False(vm.ComponentsColumnOpen);
        Assert.False(AppSettings.Current.ComponentsColumnOpen);

        // No course with two components: starts folded when the user has not chosen.
        _rig.Service().SaveCourseComponents(Lists((_rig.Biol.Id, ["st-lec"])));
        AppSettings.Current.ComponentsColumnOpen = null;
        Assert.False(NewCourseList(_rig.Service()).ComponentsColumnOpen);
    }

    /// <summary>Before the setup Edit Course shows the hint and a save never touches the stored list.</summary>
    [Fact]
    public async Task EditCourse_BeforeSetup_ShowsTheHint_AndLeavesTheStoredListAlone()
    {
        var biol = _rig.Courses.GetById(_rig.Biol.Id)!;
        biol.ComponentTypeIds = ["st-lec"];
        _rig.Courses.Update(biol);
        var vm = NewCourseList(_rig.Service());
        vm.SelectedCourse = vm.Courses.Single(c => c.Id == _rig.Biol.Id);
        vm.EditCommand.Execute(null);

        Assert.False(vm.EditVm!.HasComponentGroup);
        Assert.True(vm.EditVm.ShowComponentsHint);
        Assert.Equal("Components: set up access watches first (Access → Set up).", vm.EditVm.ComponentsHint);

        vm.EditVm.CourseTitle = "Biology I";
        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal("Biology I", _rig.Stored(_rig.Biol).Title);
    }

    /// <summary>
    /// After the setup the group offers the component types (counts "this term"), shows rule 8 and 8a warnings for the
    /// ticked list, saves the ticks in type order, and a save makes the access state stale.
    /// </summary>
    [Fact]
    public async Task EditCourse_AfterSetup_OffersTypes_WarnsAndSaves()
    {
        DoSetup();
        _rig.Add(_rig.Biol, "X01", _rig.Online);
        _rig.Add(_rig.Biol, "X02", null);
        _rig.Store.Reload(_rig.Sections, [_rig.Fall.Id]);
        var vm = NewCourseList(_rig.Service());
        var invalidated = 0;
        _rig.State.Changed += () => invalidated++;
        vm.SelectedCourse = vm.Courses.Single(c => c.Id == _rig.Biol.Id);
        vm.EditCommand.Execute(null);
        var edit = vm.EditVm!;

        Assert.True(edit.HasComponentGroup);
        Assert.Equal(new[] { "Lecture", "Lab" }, edit.ComponentOptions.Select(o => o.Name));
        Assert.Equal(new[] { true, true }, edit.ComponentOptions.Select(o => o.IsSelected));
        Assert.Equal(new[] { "1 this term", "1 this term" }, edit.ComponentOptions.Select(o => o.CountText));
        Assert.Equal("Not counted with these components: X01 (Online), X02 (no section type).", edit.NotCountedText);
        Assert.Empty(edit.ComponentMismatchLines);

        edit.ComponentOptions[1].IsSelected = false;   // untick Lab

        Assert.Null(edit.NotCountedText);   // fewer than two ticked
        Assert.Equal("BIOL101 L1 is a Lab section, but Lab isn't one of BIOL101's components.",
            Assert.Single(edit.ComponentMismatchLines));
        Assert.Equal("Lab", edit.ComponentOptions[1].Name);
        Assert.True(edit.HasUnappliedChanges);

        await edit.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.True(invalidated >= 1);
    }

    /// <summary>A type the course lists that is no longer a component is still offered (so it can be cleared).</summary>
    [Fact]
    public void EditCourse_OffersAStaleListedType()
    {
        DoSetup();
        _rig.Service().SaveCourseComponents(Lists((_rig.Chem.Id, ["st-lec", "st-onl"])));   // Online is not a component
        var vm = NewCourseList(_rig.Service());
        vm.SelectedCourse = vm.Courses.Single(c => c.Id == _rig.Chem.Id);
        vm.EditCommand.Execute(null);

        Assert.Equal(new[] { "Lecture", "Lab", "Online" }, vm.EditVm!.ComponentOptions.Select(o => o.Name));
        Assert.Equal(new[] { true, false, true }, vm.EditVm.ComponentOptions.Select(o => o.IsSelected));
    }

    /// <summary>A course added after the setup starts with an empty list (ticking nothing), not with null.</summary>
    [Fact]
    public async Task AddCourse_AfterSetup_StartsWithAnEmptyList()
    {
        DoSetup();
        var vm = NewCourseList(_rig.Service());
        vm.AddCommand.Execute(null);
        var edit = vm.EditVm!;
        Assert.All(edit.ComponentOptions, o => Assert.False(o.IsSelected));
        edit.SelectedSubject = vm.Subjects.Single();
        edit.CourseNumber = "999";

        await edit.SaveCommand.ExecuteAsync(null);

        var added = _rig.Courses.GetAll().Single(c => c.CalendarCode == "BIOL999");
        Assert.NotNull(added.ComponentTypeIds);
        Assert.Empty(added.ComponentTypeIds!);
    }

    /// <summary>A course added before the setup keeps null (never set).</summary>
    [Fact]
    public async Task AddCourse_BeforeSetup_LeavesTheListNull()
    {
        var vm = NewCourseList(_rig.Service());
        vm.AddCommand.Execute(null);
        vm.EditVm!.SelectedSubject = vm.Subjects.Single();
        vm.EditVm.CourseNumber = "999";

        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Null(_rig.Courses.GetAll().Single(c => c.CalendarCode == "BIOL999").ComponentTypeIds);
    }

    // ── Copy Semester ─────────────────────────────────────────────────────────

    private CopySemesterViewModel NewCopy(bool andMore, bool includeTypes, bool includeTags)
    {
        var db = _rig.Fx.Db;
        return new CopySemesterViewModel(new AcademicYearRepository(db), new SemesterRepository(db), _rig.Sections, db,
            _rig.Fx.Lock, _rig.Service())
        {
            FromAcademicYear = _rig.Year, FromSemester = _rig.Fall, ToAcademicYear = _rig.Year, ToSemester = _rig.Winter,
            CopyAndMore = andMore, IncludeSectionType = includeTypes, IncludeAllTags = includeTags,
        };
    }

    private Section CopiedSection(string courseId, string code) =>
        _rig.Sections.GetAll(_rig.Winter.Id).Single(s => s.CourseId == courseId && s.SectionCode == code);

    /// <summary>
    /// With the section types and tags options off, section types are still copied for courses with two or more
    /// components and watched tags are still copied; other tags and other courses' types are not. The lines say so.
    /// </summary>
    [Fact]
    public void CopySemester_KeepsComponentTypesAndWatchedTags_WhenTheOptionsAreOff()
    {
        DoSetup();
        _rig.Add(_rig.Biol, "C", _rig.Lecture, _rig.WatchedTag.Id, _rig.PlainTag.Id);
        var vm = NewCopy(andMore: true, includeTypes: false, includeTags: false);

        Assert.Equal("Section types are still copied for BIOL101, which has more than one component (such as Lecture and Laboratory), so access watches can count their sections.",
            vm.ComponentTypesLine);
        Assert.Equal("Watched tags are still copied, so access watches carry over.", vm.WatchedTagsLine);

        vm.CopyCommand.Execute(null);

        Assert.Equal("st-lec", CopiedSection(_rig.Biol.Id, "A").SectionTypeId);
        Assert.Equal("st-lab", CopiedSection(_rig.Biol.Id, "L1").SectionTypeId);
        Assert.Null(CopiedSection(_rig.Chem.Id, "A").SectionTypeId);   // one component: not kept
        Assert.Null(CopiedSection(_rig.Phys.Id, "A").SectionTypeId);   // PHYS100 stores one component
        Assert.Equal(new[] { "tag-watch" }, CopiedSection(_rig.Biol.Id, "C").TagIds);
    }

    /// <summary>Before the setup the engine's suggestion counts: courses whose sections have two component-like types keep their types.</summary>
    [Fact]
    public void CopySemester_BeforeSetup_TheSuggestionCounts()
    {
        var vm = NewCopy(andMore: true, includeTypes: false, includeTags: true);

        Assert.Equal("Section types are still copied for BIOL101 and PHYS100, which have more than one component (such as Lecture and Laboratory), so access watches can count their sections.",
            vm.ComponentTypesLine);
        Assert.Null(vm.WatchedTagsLine);   // the tags option is on

        vm.CopyCommand.Execute(null);

        Assert.Equal("st-lab", CopiedSection(_rig.Biol.Id, "L1").SectionTypeId);
        Assert.Equal("st-lab", CopiedSection(_rig.Phys.Id, "L1").SectionTypeId);
        Assert.Null(CopiedSection(_rig.Chem.Id, "B").SectionTypeId);
    }

    /// <summary>"Copy section designations only" also keeps component types and watched tags; the section types option on silences the types line.</summary>
    [Fact]
    public void CopySemester_DesignationsOnly_StillKeepsThem_AndTheLinesFollowTheOptions()
    {
        DoSetup();
        _rig.Add(_rig.Biol, "C", _rig.Lecture, _rig.WatchedTag.Id, _rig.PlainTag.Id);
        var vm = NewCopy(andMore: false, includeTypes: false, includeTags: false);
        Assert.NotNull(vm.ComponentTypesLine);
        Assert.NotNull(vm.WatchedTagsLine);

        vm.CopyCommand.Execute(null);

        Assert.Equal("st-lab", CopiedSection(_rig.Biol.Id, "L1").SectionTypeId);
        Assert.Equal(new[] { "tag-watch" }, CopiedSection(_rig.Biol.Id, "C").TagIds);

        var other = NewCopy(andMore: true, includeTypes: false, includeTags: false);
        other.IncludeSectionType = true;
        other.IncludeAllTags = true;
        Assert.Null(other.ComponentTypesLine);
        Assert.Null(other.WatchedTagsLine);
    }

    /// <summary>With no watched tag in the source and no multi-component course, both lines are null.</summary>
    [Fact]
    public void CopySemester_LinesAreNull_WhenTheyDoNotApply()
    {
        DoSetup();
        _rig.Service().SaveCourseComponents(Lists((_rig.Biol.Id, ["st-lec"])));
        // PHYS100 stays at one component; nothing carries the watched tag.
        var vm = NewCopy(andMore: true, includeTypes: false, includeTags: false);

        Assert.Null(vm.ComponentTypesLine);
        Assert.Null(vm.WatchedTagsLine);
    }

    // ── Import (rule 8b) ──────────────────────────────────────────────────────

    private ImportReport PlanImport(bool setUp)
    {
        var biol = Subj("BIOL");
        var chem = Subj("CHEM");
        var existing = Crs(chem, "CHEM201");
        var lecture = new SchedulingEnvironmentValue { Id = "t-lec", Name = "Lecture", SortOrder = 0, IsComponent = true };
        var lab = new SchedulingEnvironmentValue { Id = "t-lab", Name = "Lab", SortOrder = 1, IsComponent = true };
        var online = new SchedulingEnvironmentValue { Id = "t-onl", Name = "Online", SortOrder = 2 };
        return SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "BIOL", "101", "A", (C.SectionType, "Lecture")),
                Sec(3, "BIOL", "101", "L1", (C.SectionType, "lab")),
                Sec(4, "BIOL", "101", "X1", (C.SectionType, "Online")),
                Sec(5, "BIOL", "101", "S1", (C.SectionType, "Seminar")),   // a type this import creates: never a component
                Sec(6, "BIOL", "102", "A", (C.SectionType, "Online")),
                Sec(7, "CHEM", "201", "A", (C.SectionType, "Lecture")),
                Sec(8, "CHEM", "201", "L1", (C.SectionType, "Lab"))),
            Snapshot(subjects: [biol, chem], courses: [existing], sectionTypes: [lab, lecture, online], accessSetUp: setUp));
    }

    /// <summary>After the setup each new course gets its existing component types (type order); an existing course is never changed; new types never count.</summary>
    [Fact]
    public void Import_AfterSetup_PreFillsNewCoursesOnly()
    {
        var report = PlanImport(setUp: true);

        Assert.Empty(report.Problems);
        var byCode = report.Plan.NewCourses.ToDictionary(c => c.Record.CalendarCode, c => c.Record.ComponentTypeIds);
        Assert.Equal(new[] { "t-lec", "t-lab" }, byCode["BIOL101"]);
        Assert.NotNull(byCode["BIOL102"]);
        Assert.Empty(byCode["BIOL102"]!);   // Online is not a component: an empty list, not null
        Assert.DoesNotContain("CHEM201", byCode.Keys);   // an existing course is not in the plan at all
    }

    /// <summary>Before the setup the import leaves the new courses' lists null.</summary>
    [Fact]
    public void Import_BeforeSetup_LeavesNewCoursesNull()
    {
        var report = PlanImport(setUp: false);

        Assert.All(report.Plan.NewCourses, c => Assert.Null(c.Record.ComponentTypeIds));
    }

    /// <summary>The summary line names up to three new courses with two or more components, otherwise counts them.</summary>
    [Fact]
    public void ImportComponentsLine_NamesUpToThree_ThenCounts()
    {
        Assert.Equal("Components were filled in for BMEG230 and CHEM201 from their sections' types. You can change them under Courses.",
            AccessText.ImportComponentsLine(["BMEG230", "CHEM201"]));
        Assert.Equal("Components were filled in for 4 new courses from their sections' types. You can change them under Courses.",
            AccessText.ImportComponentsLine(["A", "B", "C", "D"]));
    }

    /// <summary>The builder reads the set-up mark into the snapshot the planner works from.</summary>
    [Fact]
    public void SnapshotBuilder_ReadsTheSetUpMark()
    {
        var db = _rig.Fx.Db;
        ImportSetupSnapshotBuilder Builder() => new(new SemesterRepository(db), new AcademicYearRepository(db),
            new SubjectRepository(db), _rig.Courses, new InstructorRepository(db), _rig.Sections, _rig.Env,
            new CampusRepository(db), new RoomRepository(db), new LegalStartTimeRepository(db), _rig.Setup);

        Assert.False(Builder().Build(_rig.Fall.Id).AccessSetUp);

        _rig.Service().SaveNoComponents();

        Assert.True(Builder().Build(_rig.Fall.Id).AccessSetUp);
    }
}

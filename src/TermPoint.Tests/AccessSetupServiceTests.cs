using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Access Analyzer piece E: <see cref="AccessSetupService"/> (every write of the setup, one transaction each, only
/// changed rows, refused without the write lock), <see cref="ComponentSuggestion"/> and the wizard view model's steps.
/// Real repositories over a temp database (<see cref="AccessSetupRig"/>).
/// </summary>
public sealed class AccessSetupServiceTests : IDisposable
{
    private readonly AccessSetupRig _rig = new();

    /// <inheritdoc/>
    public void Dispose() => _rig.Dispose();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Lists(params (string Course, string[] Types)[] rows) =>
        rows.ToDictionary(r => r.Course, r => (IReadOnlyList<string>)r.Types);

    // ── Suggestions ───────────────────────────────────────────────────────────

    /// <summary>The wizard pre-ticks the section types whose name looks like a component, and no others.</summary>
    [Fact]
    public void PreTickedTypes_AreTheOnesThatLookLikeComponents()
    {
        var ticked = ComponentSuggestion.PreTickedTypes([_rig.Lecture, _rig.Lab, _rig.Online, _rig.Thesis]);

        Assert.Equal(new[] { "st-lab", "st-lec" }, ticked.OrderBy(x => x));
    }

    /// <summary>The pre-fill is the course's section types in every semester, limited to the chosen ones, in section type order; no sections gives none.</summary>
    [Fact]
    public void Prefill_IsSectionTypesInEverySemester_IntersectedWithChosen_InTypeOrder()
    {
        _rig.Sections.Insert(new TermPoint.Models.Section
        {
            SemesterId = _rig.Winter.Id, CourseId = _rig.Chem.Id, SectionCode = "L1", SectionTypeId = _rig.Lab.Id,
        });
        var seen = _rig.Sections.GetSectionTypeIdsByCourse();
        var types = new[] { _rig.Lab, _rig.Lecture, _rig.Online };   // deliberately not in order

        var chem = ComponentSuggestion.Prefill(_rig.Chem.Id, seen, new HashSet<string> { "st-lec", "st-lab" }, types);
        var chemLabNotChosen = ComponentSuggestion.Prefill(_rig.Chem.Id, seen, new HashSet<string> { "st-lec" }, types);
        var hist = ComponentSuggestion.Prefill(_rig.Hist.Id, seen, new HashSet<string> { "st-lec", "st-lab" }, types);

        Assert.Equal(new[] { "st-lec", "st-lab" }, chem);
        Assert.Equal(new[] { "st-lec" }, chemLabNotChosen);
        Assert.Empty(hist);
    }

    // ── Wizard saves ──────────────────────────────────────────────────────────

    /// <summary>Answer No: marked types are unmarked, the set-up mark is written, courses are not touched.</summary>
    [Fact]
    public void SaveNoComponents_UnmarksTypes_AndWritesTheMark()
    {
        _rig.Service().MarkComponent(_rig.Lecture.Id);
        Assert.True(_rig.Stored(_rig.Lecture).IsComponent);
        var before = _rig.TotalChanges();

        _rig.Service().SaveNoComponents();

        Assert.False(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.True(_rig.Setup.IsSetUp());
        Assert.Null(_rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal(2, _rig.TotalChanges() - before);   // the one marked type and the mark; nothing else
    }

    /// <summary>Answer Yes: flags as given, listed courses' lists (in type order), the mark; unlisted courses and unchanged rows stay untouched.</summary>
    [Fact]
    public void SaveSetup_WritesFlagsListsAndTheMark_AndOnlyChangedCourses()
    {
        _rig.Service().SaveSetup(new HashSet<string> { "st-lec", "st-lab" },
            Lists((_rig.Biol.Id, ["st-lab", "st-lec"]), (_rig.Chem.Id, ["st-lec"]), (_rig.Hist.Id, [])));

        Assert.True(_rig.Setup.IsSetUp());
        Assert.True(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.True(_rig.Stored(_rig.Lab).IsComponent);
        Assert.False(_rig.Stored(_rig.Online).IsComponent);
        Assert.Equal(new[] { "st-lec", "st-lab" }, _rig.Stored(_rig.Biol).ComponentTypeIds);   // stored in type order
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Chem).ComponentTypeIds);
        Assert.Empty(_rig.Stored(_rig.Hist).ComponentTypeIds!);                                  // null -> [] is a change
        Assert.Null(_rig.Stored(_rig.Phys).ComponentTypeIds);                                    // not listed: left alone

        // The same answer again changes nothing but rewrites the (idempotent) mark: no type and no course is rewritten.
        var before = _rig.TotalChanges();
        _rig.Service().SaveSetup(new HashSet<string> { "st-lec", "st-lab" },
            Lists((_rig.Biol.Id, ["st-lec", "st-lab"]), (_rig.Chem.Id, ["st-lec"]), (_rig.Hist.Id, [])));
        Assert.Equal(1, _rig.TotalChanges() - before);

        // One course changes: exactly that course and the mark are written.
        before = _rig.TotalChanges();
        _rig.Service().SaveSetup(new HashSet<string> { "st-lec", "st-lab" },
            Lists((_rig.Biol.Id, ["st-lec"]), (_rig.Chem.Id, ["st-lec"]), (_rig.Hist.Id, [])));
        Assert.Equal(2, _rig.TotalChanges() - before);
    }

    /// <summary>A failure part-way (the mark, the last write, fails) leaves nothing written: types, courses and the mark are as before.</summary>
    [Fact]
    public void SaveSetup_WhenALaterWriteFails_LeavesNothingWritten()
    {
        var service = _rig.Service(new FailingSetupRepository(_rig.Setup));
        var notified = 0;
        _rig.State.Changed += () => notified++;

        Assert.Throws<InvalidOperationException>(() => service.SaveSetup(
            new HashSet<string> { "st-lec" }, Lists((_rig.Biol.Id, ["st-lec"]))));

        Assert.False(_rig.Setup.IsSetUp());
        Assert.False(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.Null(_rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal(0, notified);
    }

    /// <summary>The same for the No answer.</summary>
    [Fact]
    public void SaveNoComponents_WhenTheMarkFails_UnmarksNothing()
    {
        _rig.Service().MarkComponent(_rig.Lecture.Id);

        Assert.Throws<InvalidOperationException>(() => _rig.Service(new FailingSetupRepository(_rig.Setup)).SaveNoComponents());

        Assert.True(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.False(_rig.Setup.IsSetUp());
    }

    /// <summary>Without the write lock every write is refused before anything is written.</summary>
    [Fact]
    public void EveryWrite_IsRefusedWithoutTheWriteLock()
    {
        _rig.CanWrite = false;
        var service = _rig.Service();
        var before = _rig.TotalChanges();

        Assert.False(service.CanSave);
        Assert.Throws<InvalidOperationException>(() => service.SaveNoComponents());
        Assert.Throws<InvalidOperationException>(() => service.SaveSetup(new HashSet<string>(), Lists()));
        Assert.Throws<InvalidOperationException>(() => service.MarkComponent(_rig.Lecture.Id));
        Assert.Throws<InvalidOperationException>(() => service.UnmarkComponent(_rig.Lecture.Id));
        Assert.Throws<InvalidOperationException>(() => service.SaveCourseComponents(Lists()));

        Assert.Equal(before, _rig.TotalChanges());
        Assert.False(_rig.Setup.IsSetUp());
    }

    /// <summary>A write makes the access state stale (so the Access tab refreshes) and tells the grid.</summary>
    [Fact]
    public void AWrite_InvalidatesTheAccessState_AndNotifiesTheGrid()
    {
        var invalidated = 0;
        var notified = 0;
        _rig.State.Changed += () => invalidated++;
        _rig.Notifier.GridContentChanged += () => notified++;

        _rig.Service().MarkComponent(_rig.Lab.Id);

        Assert.True(invalidated >= 1);
        Assert.True(notified >= 1);
    }

    // ── Marking and unmarking ─────────────────────────────────────────────────

    /// <summary>Marking a type sets its flag; the listing is the courses whose stored list holds it, by calendar code.</summary>
    [Fact]
    public void MarkComponent_SetsTheFlag_AndTheListingFollowsTheCourses()
    {
        var service = _rig.Service();
        service.MarkComponent(_rig.Lab.Id);

        Assert.True(_rig.Stored(_rig.Lab).IsComponent);
        Assert.Empty(service.CoursesListing(_rig.Lab.Id));

        service.SaveCourseComponents(Lists((_rig.Phys.Id, ["st-lab"]), (_rig.Biol.Id, ["st-lab"]), (_rig.Chem.Id, ["st-lec"])));

        Assert.Equal(new[] { "BIOL101", "PHYS100" }, service.CoursesListing(_rig.Lab.Id).Select(c => c.CalendarCode));
    }

    /// <summary>Marking a type that already is one writes nothing.</summary>
    [Fact]
    public void MarkComponent_WhenAlreadyMarked_WritesNothing()
    {
        var service = _rig.Service();
        service.MarkComponent(_rig.Lab.Id);
        var before = _rig.TotalChanges();

        service.MarkComponent(_rig.Lab.Id);

        Assert.Equal(before, _rig.TotalChanges());
    }

    /// <summary>Unmarking switches the flag off and takes the type off every course's list, in one write; courses that do not list it are not rewritten.</summary>
    [Fact]
    public void UnmarkComponent_RemovesTheTypeFromAffectedCourses_Only()
    {
        var service = _rig.Service();
        service.SaveSetup(new HashSet<string> { "st-lec", "st-lab" },
            Lists((_rig.Biol.Id, ["st-lec", "st-lab"]), (_rig.Chem.Id, ["st-lec"]), (_rig.Phys.Id, ["st-lec", "st-lab"])));
        var before = _rig.TotalChanges();

        service.UnmarkComponent(_rig.Lab.Id);

        Assert.False(_rig.Stored(_rig.Lab).IsComponent);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Phys).ComponentTypeIds);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Chem).ComponentTypeIds);
        Assert.Equal(3, _rig.TotalChanges() - before);   // the type, BIOL101 and PHYS100; not CHEM201
    }

    /// <summary>Saving courses' components rewrites only courses whose list differs; an empty list for a never-set course is a change.</summary>
    [Fact]
    public void SaveCourseComponents_WritesOnlyChangedCourses()
    {
        var service = _rig.Service();
        var before = _rig.TotalChanges();
        service.SaveCourseComponents(Lists((_rig.Chem.Id, [])));
        Assert.Equal(1, _rig.TotalChanges() - before);
        Assert.Empty(_rig.Stored(_rig.Chem).ComponentTypeIds!);

        before = _rig.TotalChanges();
        service.SaveCourseComponents(Lists((_rig.Chem.Id, [])));
        Assert.Equal(0, _rig.TotalChanges() - before);
    }

    // ── The wizard view model ─────────────────────────────────────────────────

    private AccessSetupWizardViewModel NewWizard(AccessSetupService? service = null, Action? close = null, Action? onFinished = null,
        string? newTypeId = null) =>
        new(service ?? _rig.Service(), _rig.Env, _rig.Courses, _rig.Sections, close ?? (() => { }), onFinished, newTypeId);

    /// <summary>Question 1 offers Next only for Yes and Finish only for No; Next goes to the types, pre-ticked from the name list.</summary>
    [Fact]
    public void Wizard_Question1_ThenTypesPreTicked()
    {
        var vm = NewWizard();

        Assert.Equal(AccessWizardStep.Question, vm.Step);
        Assert.Equal("Setting up access watches: question 1 of 2", vm.Title);
        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.False(vm.ShowFinish);

        vm.IsNo = true;
        Assert.True(vm.ShowFinish);
        Assert.False(vm.ShowNext);

        vm.IsYes = true;
        Assert.False(vm.IsNo);
        Assert.True(vm.ShowNext);
        Assert.True(vm.NextCommand.CanExecute(null));
        vm.NextCommand.Execute(null);

        Assert.Equal(AccessWizardStep.Types, vm.Step);
        Assert.Equal("Setting up access watches: question 2 of 2", vm.Title);
        Assert.Equal(new[] { "Lecture", "Lab" }, vm.TypeRows.Where(t => t.IsTicked).Select(t => t.Name));
        Assert.Equal(4, vm.TypeRows.Count);
    }

    /// <summary>Next on question 2 is disabled while nothing is ticked, and says why.</summary>
    [Fact]
    public void Wizard_Types_NextNeedsAtLeastOneTickedType()
    {
        var vm = NewWizard();
        vm.IsYes = true;
        vm.NextCommand.Execute(null);
        foreach (var t in vm.TypeRows) t.IsTicked = false;

        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.Equal("Tick at least one, or go Back and answer No.", vm.NextDisabledTip);

        vm.TypeRows[0].IsTicked = true;
        Assert.True(vm.NextCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.NextDisabledTip);
    }

    /// <summary>The course check lists every course by calendar code, one column per chosen type, pre-filled; fewer than two ticks show the hint.</summary>
    [Fact]
    public void Wizard_CourseCheck_ListsEveryCourse_PreFilled()
    {
        var vm = NewWizard();
        vm.IsYes = true;
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.Equal(AccessWizardStep.Courses, vm.Step);
        Assert.Equal("Check your courses' components", vm.Title);
        Assert.Equal(new[] { "Lecture", "Lab" }, vm.ColumnHeadings);
        Assert.Equal(new[] { "BIOL101", "CHEM201", "HIST101", "PHYS100" }, vm.CourseRows.Select(r => r.Code));
        var biol = vm.CourseRows[0];
        Assert.Equal(new[] { true, true }, biol.Cells.Select(c => c.IsTicked));
        Assert.Equal(new[] { true, false }, vm.CourseRows[1].Cells.Select(c => c.IsTicked));
        Assert.Equal(new[] { false, false }, vm.CourseRows[2].Cells.Select(c => c.IsTicked));   // HIST101: no sections

    }

    /// <summary>Back from the course check keeps the user's ticks when they come forward again.</summary>
    [Fact]
    public void Wizard_Back_KeepsTheCourseTicks()
    {
        var vm = NewWizard();
        vm.IsYes = true;
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);
        vm.CourseRows[2].Cells[0].IsTicked = true;

        vm.BackCommand.Execute(null);
        Assert.Equal(AccessWizardStep.Types, vm.Step);
        vm.NextCommand.Execute(null);

        Assert.True(vm.CourseRows[2].Cells[0].IsTicked);
    }

    /// <summary>These are right: one transaction writes the chosen types, every course and the mark, then calls onFinished and closes.</summary>
    [Fact]
    public void Wizard_TheseAreRight_SavesEverything_ThenFinishesAndCloses()
    {
        int finished = 0, closed = 0;
        var vm = NewWizard(close: () => closed++, onFinished: () => finished++);
        vm.IsYes = true;
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        vm.TheseAreRightCommand.Execute(null);

        Assert.True(_rig.Setup.IsSetUp());
        Assert.True(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.True(_rig.Stored(_rig.Lab).IsComponent);
        Assert.Equal(new[] { "st-lec", "st-lab" }, _rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Chem).ComponentTypeIds);
        Assert.Empty(_rig.Stored(_rig.Hist).ComponentTypeIds!);
        Assert.Equal((1, 1), (finished, closed));
        Assert.Null(vm.LastErrorMessage);
    }

    /// <summary>A failed save keeps the wizard open with a message, calls neither onFinished nor close, and wrote nothing.</summary>
    [Fact]
    public void Wizard_WhenTheSaveFails_StaysOpen_WithAMessage()
    {
        int finished = 0, closed = 0;
        var vm = NewWizard(_rig.Service(new FailingSetupRepository(_rig.Setup)), () => closed++, () => finished++);
        vm.IsYes = true;
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        vm.TheseAreRightCommand.Execute(null);

        Assert.Equal((0, 0), (finished, closed));
        Assert.Equal(AccessText.WizardSaveFailed, vm.LastErrorMessage);
        Assert.False(_rig.Setup.IsSetUp());
        Assert.False(_rig.Stored(_rig.Lecture).IsComponent);
        Assert.Null(_rig.Stored(_rig.Biol).ComponentTypeIds);
    }

    /// <summary>Answer No then Finish: the mark is written and nothing marked remains.</summary>
    [Fact]
    public void Wizard_No_Finish_SavesTheMark()
    {
        int finished = 0, closed = 0;
        var vm = NewWizard(close: () => closed++, onFinished: () => finished++);
        vm.IsNo = true;

        vm.FinishCommand.Execute(null);

        Assert.True(_rig.Setup.IsSetUp());
        Assert.Equal((1, 1), (finished, closed));
    }

    /// <summary>Cancel anywhere saves nothing and closes without calling onFinished.</summary>
    [Fact]
    public void Wizard_Cancel_SavesNothing()
    {
        int finished = 0, closed = 0;
        var vm = NewWizard(close: () => closed++, onFinished: () => finished++);
        vm.IsYes = true;
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);
        var before = _rig.TotalChanges();

        vm.CancelCommand.Execute(null);

        Assert.Equal(before, _rig.TotalChanges());
        Assert.False(_rig.Setup.IsSetUp());
        Assert.Equal((0, 1), (finished, closed));
    }

    /// <summary>"My section types need revising first" ends on the types-first step with only Close; nothing is saved.</summary>
    [Fact]
    public void Wizard_TypesFirst_HasOnlyClose_AndSavesNothing()
    {
        var vm = NewWizard();
        vm.IsYes = true;
        vm.NextCommand.Execute(null);

        vm.TypesFirstCommand.Execute(null);

        Assert.Equal(AccessWizardStep.TypesFirst, vm.Step);
        Assert.Equal("Set up your section types first", vm.Title);
        Assert.True(vm.ShowClose);
        Assert.False(vm.ShowNext || vm.ShowBack || vm.ShowCancel || vm.ShowTheseAreRight);
        Assert.False(_rig.Setup.IsSetUp());
    }

    /// <summary>With no section types at all, Yes goes straight to the types-first step.</summary>
    [Fact]
    public void Wizard_WithNoSectionTypes_YesGoesStraightToTypesFirst()
    {
        foreach (var t in new[] { _rig.Lecture, _rig.Lab, _rig.Online, _rig.Thesis })
            _rig.Env.Delete(t.Id);
        var vm = NewWizard();

        vm.IsYes = true;
        vm.NextCommand.Execute(null);

        Assert.Equal(AccessWizardStep.TypesFirst, vm.Step);
    }

    /// <summary>
    /// New-type mode: columns are all component types; rows are pre-ticked from stored lists plus the new type for
    /// courses that have sections of it (highlighted); Save writes only changed courses; Skip saves nothing.
    /// </summary>
    [Fact]
    public void Wizard_NewTypeMode_PreTicksAndHighlights_ThenSaves()
    {
        var service = _rig.Service();
        service.SaveSetup(new HashSet<string> { "st-lec" },
            Lists((_rig.Biol.Id, ["st-lec"]), (_rig.Chem.Id, ["st-lec"]), (_rig.Phys.Id, ["st-lec"]), (_rig.Hist.Id, [])));
        service.MarkComponent(_rig.Lab.Id);
        int finished = 0, closed = 0;

        var vm = NewWizard(service, () => closed++, () => finished++, newTypeId: _rig.Lab.Id);

        Assert.True(vm.IsNewTypeMode);
        Assert.Equal(AccessWizardStep.NewType, vm.Step);
        Assert.Equal("Lab is now a component: which courses have it?", vm.Title);
        Assert.Equal(new[] { "Lecture", "Lab" }, vm.ColumnHeadings);
        var biol = vm.CourseRows.Single(r => r.Code == "BIOL101");
        Assert.True(biol.IsHighlighted);
        Assert.Equal(new[] { true, true }, biol.Cells.Select(c => c.IsTicked));
        var chem = vm.CourseRows.Single(r => r.Code == "CHEM201");
        Assert.False(chem.IsHighlighted);
        Assert.Equal(new[] { true, false }, chem.Cells.Select(c => c.IsTicked));
        Assert.True(vm.ShowSave);
        Assert.True(vm.ShowSkip);

        var before = _rig.TotalChanges();
        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { "st-lec", "st-lab" }, _rig.Stored(_rig.Biol).ComponentTypeIds);
        Assert.Equal(new[] { "st-lec", "st-lab" }, _rig.Stored(_rig.Phys).ComponentTypeIds);
        Assert.Equal(new[] { "st-lec" }, _rig.Stored(_rig.Chem).ComponentTypeIds);
        Assert.Equal(2, _rig.TotalChanges() - before);   // BIOL101 and PHYS100 only
        Assert.Equal((1, 1), (finished, closed));
    }

    /// <summary>Skip for now (Cancel) in new-type mode saves nothing.</summary>
    [Fact]
    public void Wizard_NewTypeMode_Skip_SavesNothing()
    {
        var service = _rig.Service();
        service.MarkComponent(_rig.Lab.Id);
        var vm = NewWizard(service, newTypeId: _rig.Lab.Id);
        var before = _rig.TotalChanges();

        vm.CancelCommand.Execute(null);

        Assert.Equal(before, _rig.TotalChanges());
        Assert.Null(_rig.Stored(_rig.Biol).ComponentTypeIds);
    }
}

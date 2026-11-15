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
/// Tests of <see cref="FocusViewModel"/> (spec item 20, step 20.3): the indicator text and visibility, the picker's
/// pre-ticks, how Apply maps ticks to a focus, All / None / Cancel, and that every focus-changing command is
/// disabled while a section editor is open.
/// <para>
/// A real subject repository over a temp database; the focus service uses a null database key (in memory only),
/// which is enough because the view model only needs the subjects and the service's focus and guard.
/// </para>
/// </summary>
public sealed class FocusViewModelTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly SubjectRepository _subjectRepo;
    private readonly List<IDisposable> _disposables = new();

    /// <summary>Opens the temp database.</summary>
    public FocusViewModelTests()
    {
        _subjectRepo = new SubjectRepository(_fx.Db);
    }

    /// <summary>Disposes everything and restores the settings file.</summary>
    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>Inserts a subject and returns it.</summary>
    private Subject AddSubject(string abbreviation, string name)
    {
        var s = new Subject { Name = name, CalendarAbbreviation = abbreviation };
        _subjectRepo.Insert(s);
        return s;
    }

    /// <summary>Inserts <paramref name="count"/> filler subjects S1..Sn.</summary>
    private List<Subject> AddSubjects(int count) =>
        Enumerable.Range(1, count).Select(i => AddSubject("S" + i, "Subject " + i)).ToList();

    /// <summary>A focus service (null key, All) and the view model over it.</summary>
    private (FocusService Service, FocusViewModel Vm) Make()
    {
        var svc = new FocusService(_subjectRepo, null!, null!, null!, null!, new SemesterContext(), new SectionStore(), null);
        var vm = new FocusViewModel(svc, _subjectRepo);
        _disposables.Add(vm);
        _disposables.Add(svc);
        return (svc, vm);
    }

    // ── Indicator ──────────────────────────────────────────────────────────────

    /// <summary>Under All the indicator is hidden and has no text.</summary>
    [Fact]
    public void Indicator_UnderAll_IsHidden()
    {
        AddSubjects(4);
        var (_, vm) = Make();

        Assert.False(vm.IsIndicatorVisible);
        Assert.Equal(string.Empty, vm.IndicatorText);
    }

    /// <summary>Under None the statement reads "Subjects: none".</summary>
    [Fact]
    public void Indicator_UnderNone_SaysNoSubjects()
    {
        AddSubjects(4);
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.None);

        Assert.True(vm.IsIndicatorVisible);
        Assert.Equal("Subjects: none", vm.IndicatorText);
    }

    /// <summary>One focused subject: its abbreviation, with no count.</summary>
    [Fact]
    public void Indicator_OneSubject()
    {
        var list = AddSubjects(10);
        list[0] = AddSubject("BIOL", "Biology");           // 11 subjects in all
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([list[0].Id]));

        Assert.True(vm.IsIndicatorVisible);
        Assert.Equal("Subjects: BIOL", vm.IndicatorText);
    }

    /// <summary>Two focused subjects are listed sorted by abbreviation, whatever order they were ticked in.</summary>
    [Fact]
    public void Indicator_TwoSubjects_SortedByAbbreviation()
    {
        AddSubjects(8);
        var chem = AddSubject("CHEM", "Chemistry");
        var biol = AddSubject("BIOL", "Biology");
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([chem.Id, biol.Id]));

        Assert.Equal("Subjects: BIOL, CHEM", vm.IndicatorText);
    }

    /// <summary>A subject with a blank abbreviation is named by its name instead.</summary>
    [Fact]
    public void Indicator_BlankAbbreviation_FallsBackToName()
    {
        AddSubjects(4);
        var odd = AddSubject("", "Interdisciplinary");
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([odd.Id]));

        Assert.Equal("Subjects: Interdisciplinary", vm.IndicatorText);
    }

    /// <summary>The indicator follows a focus change made elsewhere (for example a tour starting).</summary>
    [Fact]
    public void Indicator_FollowsExternalFocusChange()
    {
        var list = AddSubjects(4);
        var (svc, vm) = Make();
        svc.SetFocus(SubjectFocus.ForSubjects([list[1].Id]));
        Assert.Equal("Subjects: S2", vm.IndicatorText);

        svc.SetFocus(SubjectFocus.All);

        Assert.False(vm.IsIndicatorVisible);
    }

    // ── Chips and the "+N" cap (1.2.4 item 2.14) ───────────────────────────────

    /// <summary>Focuses the first <paramref name="count"/> of <paramref name="subjects"/>.</summary>
    private static void FocusFirst(FocusService svc, List<Subject> subjects, int count) =>
        svc.SetFocus(SubjectFocus.ForSubjects(subjects.Take(count).Select(s => s.Id).ToList()));

    /// <summary>At the cap every subject has a chip and there is no "+N".</summary>
    [Fact]
    public void Chips_AtCap_AllShown()
    {
        var list = AddSubjects(12);
        var (svc, vm) = Make();

        FocusFirst(svc, list, FocusViewModel.IndicatorMaxChips);

        Assert.Equal(FocusViewModel.IndicatorMaxChips, vm.IndicatorChips.Count);
        Assert.False(vm.HasIndicatorMore);
        Assert.Equal(string.Empty, vm.IndicatorMoreText);
    }

    /// <summary>One over the cap still shows every chip: a "+1" chip would take as much room as the one it hides.</summary>
    [Fact]
    public void Chips_OneOverCap_AllShown()
    {
        var list = AddSubjects(12);
        var (svc, vm) = Make();

        FocusFirst(svc, list, FocusViewModel.IndicatorMaxChips + 1);

        Assert.Equal(FocusViewModel.IndicatorMaxChips + 1, vm.IndicatorChips.Count);
        Assert.False(vm.HasIndicatorMore);
        Assert.Equal("S1, S2, S3, S4, S5, S6, S7", vm.ShortSubjectsText);
    }

    /// <summary>
    /// Well over the cap: the first chips (sorted), a "+N" for the rest, the full list in the tooltip text, and the
    /// same cap in the short title-bar text.
    /// </summary>
    [Fact]
    public void Chips_OverCap_FoldsRestIntoPlusN()
    {
        var list = AddSubjects(12);
        var (svc, vm) = Make();

        FocusFirst(svc, list, 9);

        Assert.Equal(["S1", "S2", "S3", "S4", "S5", "S6"], vm.IndicatorChips.Select(c => c.Label));
        Assert.True(vm.HasIndicatorMore);
        Assert.Equal(3, vm.IndicatorMoreCount);
        Assert.Equal("+3", vm.IndicatorMoreText);
        Assert.Equal("Subjects: S1, S2, S3, S4, S5, S6, S7, S8, S9", vm.IndicatorText);
        Assert.Equal("S1, S2, S3, S4, S5, S6 +3", vm.ShortSubjectsText);
        Assert.False(vm.HasIndicatorFallback);
    }

    /// <summary>Under None there are no chips; the statement reads "Subjects: none".</summary>
    [Fact]
    public void Chips_UnderNone_ShowFallbackNone()
    {
        AddSubjects(4);
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.None);

        Assert.Empty(vm.IndicatorChips);
        Assert.False(vm.HasIndicatorMore);
        Assert.True(vm.HasIndicatorFallback);
        Assert.Equal("none", vm.IndicatorFallbackText);
        Assert.Equal("none", vm.ShortSubjectsText);
    }

    /// <summary>Going back to All clears every part of the statement, including a "+N".</summary>
    [Fact]
    public void Chips_BackToAll_ClearsEverything()
    {
        var list = AddSubjects(12);
        var (svc, vm) = Make();
        FocusFirst(svc, list, 10);

        svc.SetFocus(SubjectFocus.All);

        Assert.Empty(vm.IndicatorChips);
        Assert.False(vm.HasIndicatorMore);
        Assert.False(vm.HasIndicatorFallback);
        Assert.Equal(string.Empty, vm.ShortSubjectsText);
    }

    /// <summary>Without a colour presenter the chips carry no markers.</summary>
    [Fact]
    public void Chips_WithoutColours_HaveNoMarkers()
    {
        var list = AddSubjects(3);
        var (svc, vm) = Make();

        FocusFirst(svc, list, 2);

        Assert.All(vm.IndicatorChips, c => Assert.False(c.HasColor));
    }

    /// <summary>The cap helper itself: below, at, one over and well over the cap, and a cap below 1.</summary>
    [Fact]
    public void CapChips_Rules()
    {
        int[] Items(int n) => Enumerable.Range(1, n).ToArray();

        Assert.Equal((3, 0), Count(FocusViewModel.CapChips(Items(3), 6)));
        Assert.Equal((6, 0), Count(FocusViewModel.CapChips(Items(6), 6)));
        Assert.Equal((7, 0), Count(FocusViewModel.CapChips(Items(7), 6)));
        Assert.Equal((6, 2), Count(FocusViewModel.CapChips(Items(8), 6)));
        Assert.Equal((6, 19), Count(FocusViewModel.CapChips(Items(25), 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => FocusViewModel.CapChips(Items(3), 0));

        static (int, int) Count((IReadOnlyList<int> Shown, int More) r) => (r.Shown.Count, r.More);
    }

    // ── Subjects label (top-bar button) ────────────────────────────────────────

    /// <summary>Under All the top-bar Subjects button reads "All".</summary>
    [Fact]
    public void SubjectsLabel_UnderAll_IsAll()
    {
        AddSubjects(4);
        var (_, vm) = Make();

        Assert.Equal("All", vm.SubjectsLabel);
    }

    /// <summary>Under None the button invites a choice.</summary>
    [Fact]
    public void SubjectsLabel_UnderNone_IsChoose()
    {
        AddSubjects(4);
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.None);

        Assert.Equal("Choose…", vm.SubjectsLabel);
    }

    /// <summary>One chosen subject: just its abbreviation.</summary>
    [Fact]
    public void SubjectsLabel_OneSubject()
    {
        AddSubjects(4);
        var biol = AddSubject("BIOL", "Biology");
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([biol.Id]));

        Assert.Equal("BIOL", vm.SubjectsLabel);
    }

    /// <summary>Two chosen subjects are listed sorted, whatever order they were ticked in.</summary>
    [Fact]
    public void SubjectsLabel_TwoSubjects_Sorted()
    {
        AddSubjects(4);
        var chem = AddSubject("CHEM", "Chemistry");
        var biol = AddSubject("BIOL", "Biology");
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([chem.Id, biol.Id]));

        Assert.Equal("BIOL, CHEM", vm.SubjectsLabel);
    }

    /// <summary>A subject with a blank abbreviation is named by its name instead.</summary>
    [Fact]
    public void SubjectsLabel_BlankAbbreviation_FallsBackToName()
    {
        AddSubjects(4);
        var odd = AddSubject("", "Interdisciplinary");
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([odd.Id]));

        Assert.Equal("Interdisciplinary", vm.SubjectsLabel);
    }

    /// <summary>The label follows a change made elsewhere, and the tooltip names the subjects or says to finish editing.</summary>
    [Fact]
    public void SubjectsLabel_FollowsExternalChange_AndToolTipTracksGuard()
    {
        var list = AddSubjects(4);
        var (svc, vm) = Make();

        svc.SetFocus(SubjectFocus.ForSubjects([list[1].Id]));
        Assert.Equal("S2", vm.SubjectsLabel);
        Assert.Equal("Subjects: S2", vm.SubjectsToolTip);

        svc.SetEditorOpen(true);
        Assert.Equal("Finish editing first", vm.SubjectsToolTip);
        svc.SetEditorOpen(false);

        svc.SetFocus(SubjectFocus.All);
        Assert.Equal("All", vm.SubjectsLabel);
    }

    // ── Picker ─────────────────────────────────────────────────────────────────

    /// <summary>Opening the picker lists every subject sorted by abbreviation and opens the popup.</summary>
    [Fact]
    public void OpenPicker_ListsSubjectsSorted_AndOpens()
    {
        AddSubject("CHEM", "Chemistry");
        AddSubject("BIOL", "Biology");
        AddSubject("ENGL", "English");
        var (_, vm) = Make();

        vm.OpenPickerCommand.Execute(null);

        Assert.True(vm.IsPickerOpen);
        Assert.Equal(["BIOL", "CHEM", "ENGL"], vm.SubjectItems.Select(i => i.Label).ToArray());
    }

    /// <summary>Under All every box is pre-ticked; under None none is.</summary>
    [Fact]
    public void OpenPicker_PreTicks_AllAndNone()
    {
        AddSubjects(4);
        var (svc, vm) = Make();

        vm.OpenPickerCommand.Execute(null);
        Assert.All(vm.SubjectItems, i => Assert.True(i.IsTicked));

        vm.CancelCommand.Execute(null);
        svc.SetFocus(SubjectFocus.None);
        vm.OpenPickerCommand.Execute(null);
        Assert.All(vm.SubjectItems, i => Assert.False(i.IsTicked));
    }

    /// <summary>Under a Subjects focus exactly the focused subjects are pre-ticked.</summary>
    [Fact]
    public void OpenPicker_PreTicks_FocusedSubjects()
    {
        var list = AddSubjects(5);
        var (svc, vm) = Make();
        svc.SetFocus(SubjectFocus.ForSubjects([list[0].Id, list[3].Id]));

        vm.OpenPickerCommand.Execute(null);

        Assert.Equal(["S1", "S4"], vm.SubjectItems.Where(i => i.IsTicked).Select(i => i.Label).OrderBy(l => l).ToArray());
    }

    /// <summary>The picker rebuilds on every open, so a subject added since appears.</summary>
    [Fact]
    public void OpenPicker_RebuildsList_PicksUpNewSubject()
    {
        AddSubjects(2);
        var (_, vm) = Make();
        vm.OpenPickerCommand.Execute(null);
        Assert.Equal(2, vm.SubjectItems.Count);
        vm.CancelCommand.Execute(null);

        AddSubject("NEW", "New subject");
        vm.OpenPickerCommand.Execute(null);

        Assert.Equal(3, vm.SubjectItems.Count);
    }

    /// <summary>Apply with some ticked gives a Subjects focus on exactly those, and closes the picker.</summary>
    [Fact]
    public void Apply_SomeTicked_GivesSubjectsFocus()
    {
        var list = AddSubjects(5);
        var (svc, vm) = Make();
        svc.SetFocus(SubjectFocus.None);
        vm.OpenPickerCommand.Execute(null);

        vm.SubjectItems.Single(i => i.SubjectId == list[1].Id).IsTicked = true;
        vm.SubjectItems.Single(i => i.SubjectId == list[4].Id).IsTicked = true;
        vm.ApplyCommand.Execute(null);

        Assert.Equal(SubjectFocus.ForSubjects([list[1].Id, list[4].Id]), svc.Current);
        Assert.False(vm.IsPickerOpen);
    }

    /// <summary>Apply with every box ticked gives All.</summary>
    [Fact]
    public void Apply_AllTicked_GivesAll()
    {
        AddSubjects(5);
        var (svc, vm) = Make();
        svc.SetFocus(SubjectFocus.None);
        vm.OpenPickerCommand.Execute(null);

        foreach (var item in vm.SubjectItems) item.IsTicked = true;
        vm.ApplyCommand.Execute(null);

        Assert.Equal(SubjectFocus.All, svc.Current);
    }

    /// <summary>Apply with no box ticked gives None.</summary>
    [Fact]
    public void Apply_NoneTicked_GivesNone()
    {
        AddSubjects(5);
        var (svc, vm) = Make();                                  // All
        vm.OpenPickerCommand.Execute(null);

        foreach (var item in vm.SubjectItems) item.IsTicked = false;
        vm.ApplyCommand.Execute(null);

        Assert.Equal(SubjectFocus.None, svc.Current);
    }

    /// <summary>Cancel closes the picker and leaves the focus alone, whatever was ticked.</summary>
    [Fact]
    public void Cancel_ClosesWithoutChange()
    {
        AddSubjects(5);
        var (svc, vm) = Make();
        vm.OpenPickerCommand.Execute(null);
        foreach (var item in vm.SubjectItems) item.IsTicked = false;

        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsPickerOpen);
        Assert.Equal(SubjectFocus.All, svc.Current);
    }

    /// <summary>The picker's All and None buttons apply at once and close it.</summary>
    [Fact]
    public void AllAndNone_ApplyAtOnce_AndClose()
    {
        AddSubjects(5);
        var (svc, vm) = Make();
        vm.OpenPickerCommand.Execute(null);

        vm.NoneCommand.Execute(null);
        Assert.Equal(SubjectFocus.None, svc.Current);
        Assert.False(vm.IsPickerOpen);

        vm.OpenPickerCommand.Execute(null);
        vm.AllCommand.Execute(null);
        Assert.Equal(SubjectFocus.All, svc.Current);
        Assert.False(vm.IsPickerOpen);
    }

    // ── Editor guard ───────────────────────────────────────────────────────────

    /// <summary>
    /// While an editor is open every focus-changing command is disabled (Cancel is not), with the "Finish editing
    /// first" tooltip; closing the editor re-enables them and raises CanExecuteChanged.
    /// </summary>
    [Fact]
    public void Commands_AreDisabledWhileEditorOpen()
    {
        AddSubjects(5);
        var (svc, vm) = Make();
        svc.SetFocus(SubjectFocus.None);
        var canExecuteChanged = 0;
        vm.OpenPickerCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
        Assert.True(vm.OpenPickerCommand.CanExecute(null));

        svc.SetEditorOpen(true);

        Assert.False(vm.OpenPickerCommand.CanExecute(null));
        Assert.False(vm.AllCommand.CanExecute(null));
        Assert.False(vm.NoneCommand.CanExecute(null));
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.Equal(FocusViewModel.FinishEditingFirst, vm.ChangeFocusToolTip);
        Assert.True(canExecuteChanged > 0);

        svc.SetEditorOpen(false);

        Assert.True(vm.OpenPickerCommand.CanExecute(null));
        Assert.True(vm.ApplyCommand.CanExecute(null));
        Assert.NotEqual(FocusViewModel.FinishEditingFirst, vm.ChangeFocusToolTip);
    }

    // ── Deleting a focused subject (step 20.3) ─────────────────────────────────

    /// <summary>A dialog service that never shows anything.</summary>
    private sealed class NullDialog : IDialogService
    {
        public System.Threading.Tasks.Task<bool> Confirm(string message, string confirmLabel = "Delete") =>
            System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task ShowError(string message) => System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Deleting a focused subject in the subject list drops it from the focus at once, so the indicator never names
    /// a deleted subject; deleting the only focused subject leaves None.
    /// </summary>
    [Fact]
    public void DeletingFocusedSubject_PrunesFocus_AndIndicator()
    {
        var list = AddSubjects(5);
        var (svc, vm) = Make();
        svc.SetFocus(SubjectFocus.ForSubjects([list[0].Id, list[1].Id]));
        var subjects = new TermPoint.ViewModels.Management.SubjectListViewModel(_subjectRepo, new NullDialog(), _fx.Lock, svc);

        subjects.SelectedSubject = subjects.Subjects.Single(s => s.Id == list[1].Id);
        subjects.DeleteCommand.Execute(null);

        Assert.Equal(SubjectFocus.ForSubjects([list[0].Id]), svc.Current);
        Assert.Equal("Subjects: S1", vm.IndicatorText);

        subjects.SelectedSubject = subjects.Subjects.Single(s => s.Id == list[0].Id);
        subjects.DeleteCommand.Execute(null);

        Assert.Equal(SubjectFocus.None, svc.Current);
        Assert.Equal("Subjects: none", vm.IndicatorText);
    }
}

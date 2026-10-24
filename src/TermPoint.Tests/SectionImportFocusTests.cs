using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the section import's summary under a subject focus (spec item 20, step 7): after an import, the second
/// line counts the sections just created that sit outside the chosen subjects, and offers [Show all subjects].
/// View-model level: preview rows are added directly (as the mapping step would) and the real Import command runs
/// against a temp database; the focus service uses a null database key (in memory only) and needs no repositories,
/// because the import asks it only about courses.
/// </summary>
public sealed class SectionImportFocusTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;

    private readonly Course _biol = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chem = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };

    private readonly string _semesterId;
    private int _codeCounter;

    /// <summary>Seeds two subjects, a course in each, and a semester.</summary>
    public SectionImportFocusTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        new SubjectRepository(db).Insert(new Subject { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" });
        new SubjectRepository(db).Insert(new Subject { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" });
        _courseRepo.Insert(_biol);
        _courseRepo.Insert(_chem);

        var ayRepo = new AcademicYearRepository(db);
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var semester = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        new SemesterRepository(db).Insert(semester);
        _semesterId = semester.Id;
    }

    /// <summary>Releases the temp database and lock.</summary>
    public void Dispose() => _fx.Dispose();

    /// <summary>A focus service fixed at All, then set to the given subjects when any are named.</summary>
    /// <param name="subjectIds">The subjects to choose; none leaves the focus at All.</param>
    private static FocusService NewFocus(params string[] subjectIds)
    {
        var focus = FocusTestSupport.AllFocus();
        if (subjectIds.Length > 0) focus.SetFocus(SubjectFocus.ForSubjects(subjectIds));
        return focus;
    }

    /// <summary>
    /// A section import view model over the fixture's real section and course repositories (every other dependency is
    /// unused by the Import command and left null).
    /// </summary>
    private SectionImportViewModel NewImport(FocusService focus) =>
        new(null!, _sectionRepo, _courseRepo, null!, null!, null!, null!, null!, null!, _fx.Db, null!, null!,
            _ => { }, focus);

    /// <summary>A preview row for a new section of the course (or a rejected one when a reason is given).</summary>
    private SectionPreviewRow Row(Course course, string? rejectionReason = null) =>
        new(new SectionRow { CourseCode = course.CalendarCode, SectionCode = "S" + (++_codeCounter) },
            _semesterId, course.Id, "100", string.Empty, string.Empty,
            new List<InstructorAssignment>(), new List<SectionDaySchedule>(), new List<string>(), rejectionReason);

    /// <summary>Adds the rows to the view model, marks the mappings confirmed, and runs the Import command.</summary>
    private static void RunImport(SectionImportViewModel vm, params SectionPreviewRow[] rows)
    {
        foreach (var row in rows) vm.PreviewRows.Add(row);
        vm.IsMappingConfirmed = true;
        vm.ImportCommand.Execute(null);
        Assert.True(vm.IsImported, vm.ErrorBanner);
    }

    /// <summary>Some of the new sections are outside the chosen subjects: the count is "n of them".</summary>
    [Fact]
    public void Summary_CountsTheSectionsOutsideTheChosenSubjects()
    {
        var vm = NewImport(NewFocus("subj-biol"));

        RunImport(vm, Row(_biol), Row(_biol), Row(_chem), Row(_chem), Row(_chem));

        Assert.Equal("Imported 5 new, 0 rejected.", vm.ImportSummary);
        Assert.Equal("3 of them are in subjects you haven't chosen, so they're not shown.", vm.OutsideChosenSubjectsNote);
        Assert.True(vm.HasOutsideChosenSubjectsNote);
    }

    /// <summary>One section outside reads as a single section, not "1 of them are".</summary>
    [Fact]
    public void Summary_OneSectionOutside_UsesTheSingularWording()
    {
        var vm = NewImport(NewFocus("subj-biol"));

        RunImport(vm, Row(_biol), Row(_biol), Row(_chem));

        Assert.Equal("1 of them is in a subject you haven't chosen, so it's not shown.", vm.OutsideChosenSubjectsNote);
    }

    /// <summary>Every new section is outside: the sentence says none of them are shown.</summary>
    [Fact]
    public void Summary_AllOutside_SaysNoneOfThemAreShown()
    {
        var vm = NewImport(NewFocus("subj-biol"));

        RunImport(vm, Row(_chem), Row(_chem));

        Assert.Equal("None of them are in the subjects you've chosen, so they're not shown.", vm.OutsideChosenSubjectsNote);
        Assert.True(vm.HasOutsideChosenSubjectsNote);
    }

    /// <summary>Under None nothing is shown at all, so every new section is outside.</summary>
    [Fact]
    public void Summary_UnderNone_SaysNoneOfThemAreShown()
    {
        var vm = NewImport(NoneFocus());

        RunImport(vm, Row(_biol), Row(_chem));

        Assert.Equal("None of them are in the subjects you've chosen, so they're not shown.", vm.OutsideChosenSubjectsNote);
    }

    /// <summary>A focus fixed at None.</summary>
    private static FocusService NoneFocus()
    {
        var focus = FocusTestSupport.AllFocus();
        focus.SetFocus(SubjectFocus.None);
        return focus;
    }

    /// <summary>Every new section is in the chosen subjects: nothing extra is said.</summary>
    [Fact]
    public void Summary_NoneOutside_AddsNothing()
    {
        var vm = NewImport(NewFocus("subj-biol"));

        RunImport(vm, Row(_biol), Row(_biol));

        Assert.Equal("Imported 2 new, 0 rejected.", vm.ImportSummary);
        Assert.Null(vm.OutsideChosenSubjectsNote);
        Assert.False(vm.HasOutsideChosenSubjectsNote);
    }

    /// <summary>Under All nothing is hidden, so nothing extra is said.</summary>
    [Fact]
    public void Summary_UnderAll_AddsNothing()
    {
        var vm = NewImport(NewFocus());

        RunImport(vm, Row(_biol), Row(_chem));

        Assert.Null(vm.OutsideChosenSubjectsNote);
        Assert.False(vm.HasOutsideChosenSubjectsNote);
    }

    /// <summary>Rejected rows create no section, so they are not counted as outside.</summary>
    [Fact]
    public void Summary_RejectedRowsAreNotCounted()
    {
        var vm = NewImport(NewFocus("subj-biol"));

        RunImport(vm, Row(_biol), Row(_chem, rejectionReason: "duplicate"));

        Assert.Equal("Imported 1 new, 1 rejected.", vm.ImportSummary);
        Assert.Null(vm.OutsideChosenSubjectsNote);
    }

    /// <summary>[Show all subjects] sets the focus to All and the note goes away.</summary>
    [Fact]
    public void ShowAllSubjects_SetsTheFocusToAll_AndClearsTheNote()
    {
        var focus = NewFocus("subj-biol");
        var vm = NewImport(focus);
        RunImport(vm, Row(_biol), Row(_chem));
        Assert.True(vm.HasOutsideChosenSubjectsNote);
        Assert.True(vm.ShowAllSubjectsCommand.CanExecute(null));

        vm.ShowAllSubjectsCommand.Execute(null);

        Assert.Equal(FocusKind.All, focus.Current.Kind);
        Assert.Null(vm.OutsideChosenSubjectsNote);
        Assert.False(vm.HasOutsideChosenSubjectsNote);
    }

    /// <summary>
    /// While a section editor is open the button is disabled with the "Finish editing first" tooltip and the focus
    /// does not change; closing the editor enables it again.
    /// </summary>
    [Fact]
    public void ShowAllSubjects_HonoursTheEditorGuard()
    {
        var focus = NewFocus("subj-biol");
        var vm = NewImport(focus);
        RunImport(vm, Row(_biol), Row(_chem));

        focus.SetEditorOpen(true);

        Assert.False(vm.CanChangeFocus);
        Assert.False(vm.ShowAllSubjectsCommand.CanExecute(null));
        Assert.Equal("Finish editing first", vm.ShowAllSubjectsToolTip);
        vm.ShowAllSubjectsCommand.Execute(null);
        Assert.Equal(FocusKind.Subjects, focus.Current.Kind);
        Assert.True(vm.HasOutsideChosenSubjectsNote);

        focus.SetEditorOpen(false);

        Assert.True(vm.ShowAllSubjectsCommand.CanExecute(null));
        Assert.Null(vm.ShowAllSubjectsToolTip);
    }
}

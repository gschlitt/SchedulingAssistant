using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="OutsideChosenSubjectsNote.Build"/> (moved here from the Dev Tools section import, which
/// keeps its own behaviour: <see cref="SectionImportFocusTests"/> still runs it through that view model until
/// it is retired in step 24.10). The sentences are the ones the Dev Tools import has always used.
/// </summary>
public sealed class OutsideChosenSubjectsNoteTests : IDisposable
{
    private readonly ImportRig _rig = new();
    private readonly Course _biol = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chem = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };

    /// <summary>Seeds two subjects and a course in each.</summary>
    public OutsideChosenSubjectsNoteTests()
    {
        _rig.Subjects.Insert(new Subject { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" });
        _rig.Subjects.Insert(new Subject { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" });
        _rig.Courses.Insert(_biol);
        _rig.Courses.Insert(_chem);
    }

    /// <summary>Releases the temporary database.</summary>
    public void Dispose() => _rig.Dispose();

    /// <summary>A focus fixed at All, or set to the named subjects when any are given.</summary>
    private static FocusService Focus(params string[] subjectIds)
    {
        var focus = FocusTestSupport.AllFocus();
        if (subjectIds.Length > 0) focus.SetFocus(SubjectFocus.ForSubjects(subjectIds));
        return focus;
    }

    /// <summary>Builds the note for sections created in the given courses.</summary>
    private string? Note(FocusService focus, params Course[] created) =>
        OutsideChosenSubjectsNote.Build(focus, _rig.Courses, created.Select(c => c.Id).ToList());

    /// <summary>Some of the new sections are outside the chosen subjects: the count is "n of them".</summary>
    [Fact]
    public void SomeOutside_CountsThem()
    {
        Assert.Equal(
            "3 of them are in subjects you haven't chosen, so they're not shown.",
            Note(Focus("subj-biol"), _biol, _biol, _chem, _chem, _chem));
    }

    /// <summary>One section outside reads as a single section, not "1 of them are".</summary>
    [Fact]
    public void OneOutside_UsesTheSingularWording()
    {
        Assert.Equal(
            "1 of them is in a subject you haven't chosen, so it's not shown.",
            Note(Focus("subj-biol"), _biol, _biol, _chem));
    }

    /// <summary>Every new section is outside: the sentence says none of them are shown.</summary>
    [Fact]
    public void AllOutside_SaysNoneOfThemAreShown()
    {
        Assert.Equal(
            "None of them are in the subjects you've chosen, so they're not shown.",
            Note(Focus("subj-biol"), _chem, _chem));
    }

    /// <summary>Under None nothing is shown at all, so every new section is outside.</summary>
    [Fact]
    public void UnderNone_SaysNoneOfThemAreShown()
    {
        var focus = FocusTestSupport.AllFocus();
        focus.SetFocus(SubjectFocus.None);

        Assert.Equal(
            "None of them are in the subjects you've chosen, so they're not shown.",
            Note(focus, _biol, _chem));
    }

    /// <summary>Every new section is in the chosen subjects: nothing extra is said.</summary>
    [Fact]
    public void NoneOutside_SaysNothing()
    {
        Assert.Null(Note(Focus("subj-biol"), _biol, _biol));
    }

    /// <summary>Under All nothing is hidden, so nothing is said.</summary>
    [Fact]
    public void UnderAll_SaysNothing()
    {
        Assert.Null(Note(Focus(), _biol, _chem));
    }

    /// <summary>Nothing created, nothing to say.</summary>
    [Fact]
    public void NothingCreated_SaysNothing()
    {
        Assert.Null(Note(Focus("subj-biol")));
    }

    /// <summary>A course that cannot be found counts as outside: no view could show its section.</summary>
    [Fact]
    public void AnUnknownCourse_CountsAsOutside()
    {
        var focus = Focus("subj-biol");

        var note = OutsideChosenSubjectsNote.Build(focus, _rig.Courses, new[] { _biol.Id, "no-such-course" });

        Assert.Equal("1 of them is in a subject you haven't chosen, so it's not shown.", note);
    }

    /// <summary>
    /// Never throws: a failure to read the courses (after the import has committed) gives no sentence, and is
    /// logged without being reported as a failed import.
    /// </summary>
    [Fact]
    public void WhenTheCoursesCannotBeRead_ReturnsNullAndLogs()
    {
        var (courses, _) = RecordingRepositoryProxy<ICourseRepository>.Wrap(_rig.Courses, (name, n) => name == "GetAll");
        var logger = new RecordingImportLogger();

        var note = OutsideChosenSubjectsNote.Build(Focus("subj-biol"), courses, new[] { _chem.Id }, logger);

        Assert.Null(note);
        Assert.Single(logger.Lines);
        Assert.Empty(logger.Errors);
    }
}

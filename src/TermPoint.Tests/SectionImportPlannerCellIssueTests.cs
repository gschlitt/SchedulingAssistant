using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests of how the planner treats the cells the file reader flagged (<see cref="CellIssue"/>): an Excel error
/// cell or a cell Excel turned into a date. Each such cell is exactly one problem, worded by
/// <see cref="ImportMessages.CellIssueProblem"/>, and it replaces any other problem that the same cell would have
/// caused (an "is empty" message, say). A cell in one of the always-required (key) columns is checked on every
/// row, like a blank key cell; a cell in any other column is checked only for the rows of a section that is going
/// to be added, so a section already in the semester is left alone, exactly as its other cells are.
/// </summary>
public class SectionImportPlannerCellIssueTests
{
    /// <summary>The message for an Excel error cell, spelled out so a change to the wording is noticed.</summary>
    private const string ErrorMessage =
        "This cell shows an Excel error (#N/A). Fix the formula in Excel, or type the value instead.";

    /// <summary>The message for a date cell, spelled out so a change to the wording is noticed.</summary>
    private const string DateMessage =
        "Excel has turned this into a date (2 Jan 2026). Format the column as Text in Excel and type the value again.";

    /// <summary>The columns that are always required (the key columns).</summary>
    public static IEnumerable<object[]> KeyColumns() =>
        SectionImportColumns.All.Where(c => c.AlwaysRequired).Select(c => new object[] { c.Name });

    /// <summary>A few columns that are not required, section-level and meeting-level.</summary>
    public static IEnumerable<object[]> OptionalColumns() =>
        new[] { C.CourseTitle, C.SectionType, C.Instructors, C.Tags, C.Capacity, C.MeetingDays, C.Begin, C.End,
                C.Frequency, C.MeetingType, C.Building, C.Room, C.Campus }
            .Select(c => new object[] { c });

    // ── Key columns ───────────────────────────────────────────────────────────

    /// <summary>
    /// An error cell in a key column is one problem on that row and column with the error message (not "is empty"),
    /// and the row is left out of grouping, like a blank key cell.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeyColumns))]
    public void ErrorCellInAKeyColumn_IsOneProblemWithTheErrorMessage_AndTheRowIsNotGrouped(string column)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((column, ErrorIssue())), Sec(3, "CHEM", "101", "B")),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal(ErrorMessage, problem.Message);
        Assert.Equal("B", Assert.Single(report.Plan.NewSections).Key.SectionCode);
    }

    /// <summary>A date cell in a key column is one problem with the date message, and the row is not grouped.</summary>
    [Theory]
    [MemberData(nameof(KeyColumns))]
    public void DateCellInAKeyColumn_IsOneProblemWithTheDateMessage_AndTheRowIsNotGrouped(string column)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((column, DateIssue())), Sec(3, "CHEM", "101", "B")),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal(DateMessage, problem.Message);
        Assert.Equal("B", Assert.Single(report.Plan.NewSections).Key.SectionCode);
    }

    /// <summary>
    /// A bad Academic Year or Term cell is not also reported as a row for the wrong semester, and does not take
    /// part in the whole-file semester check: a file for another semester with one such row is still one file problem.
    /// </summary>
    [Fact]
    public void BadSemesterCell_IsNotAlsoAWrongSemesterProblem()
    {
        var oneBadRow = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((C.Term, ErrorIssue()))), Snapshot());
        var problem = Assert.Single(oneBadRow.Problems);
        Assert.Equal(C.Term, problem.Column);

        var otherSemester = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Term, "Spring")),
                Sec(3, "CHEM", "101", "B").WithIssues((C.Term, DateIssue()))),
            Snapshot());
        var wholeFile = Assert.Single(otherSemester.Problems);
        Assert.Null(wholeFile.Row);
    }

    // ── Other columns of a section that will be added ────────────────────────

    /// <summary>
    /// An error cell in any other template column of a section being added is exactly one problem on that row
    /// and column, with the error message.
    /// </summary>
    [Theory]
    [MemberData(nameof(OptionalColumns))]
    public void ErrorCellInAnOptionalColumn_IsExactlyOneProblem(string column)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((column, ErrorIssue()))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal(ErrorMessage, problem.Message);
    }

    /// <summary>A date cell in any other template column of a section being added is exactly one problem, with the date message.</summary>
    [Theory]
    [MemberData(nameof(OptionalColumns))]
    public void DateCellInAnOptionalColumn_IsExactlyOneProblem(string column)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((column, DateIssue()))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal(DateMessage, problem.Message);
    }

    /// <summary>Several bad cells on one row give one problem each, in template column order, on their own columns.</summary>
    [Fact]
    public void SeveralBadCellsOnARow_GiveOneProblemEach()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues(
                (C.Capacity, ErrorIssue()), (C.Instructors, ErrorIssue()), (C.SectionType, DateIssue()))),
            Snapshot());

        Assert.Equal(new[] { C.SectionType, C.Instructors, C.Capacity }, report.Problems.Select(p => p.Column).ToArray());
        Assert.Equal(new[] { DateMessage, ErrorMessage, ErrorMessage }, report.Problems.Select(p => p.Message).ToArray());
    }

    /// <summary>
    /// The bad-cell problem replaces what a blank cell would have caused: an error in Meeting Days beside a
    /// Begin and End is not also "Meeting Days is empty", and an error in Room beside a Building is not also
    /// "Building has no Room".
    /// </summary>
    [Fact]
    public void BadCell_ReplacesTheProblemItsBlankTextWouldHaveCaused()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "0900", "1000").WithIssues((C.MeetingDays, ErrorIssue())),
                Sec(3, "CHEM", "102", "A", (C.Building, "SCI")).WithIssues((C.Room, DateIssue()))),
            Snapshot());

        Assert.Equal(2, report.Problems.Count);
        Assert.Equal((2, C.MeetingDays, ErrorMessage), (report.Problems[0].Row!.Value, report.Problems[0].Column, report.Problems[0].Message));
        Assert.Equal((3, C.Room, DateMessage), (report.Problems[1].Row!.Value, report.Problems[1].Column, report.Problems[1].Message));
    }

    /// <summary>A new course whose only Course Title cell is bad has one problem on that cell, not two.</summary>
    [Fact]
    public void BadCourseTitleOfANewCourse_IsOneProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((C.CourseTitle, ErrorIssue()))), Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal((2, C.CourseTitle, ErrorMessage), (problem.Row!.Value, problem.Column, problem.Message));
    }

    /// <summary>A bad cell on a later row of a section being added is reported on that row.</summary>
    [Fact]
    public void BadCellOnALaterRowOfASection_IsReportedOnThatRow()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Mtg(2, "CHEM", "101", "A", "M", "0900", "1000"),
                Mtg(3, "CHEM", "101", "A", "W", "0900", "1000").WithIssues((C.Room, ErrorIssue()))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal((3, C.Room, ErrorMessage), (problem.Row!.Value, problem.Column, problem.Message));
    }

    // ── Sections already in the semester ──────────────────────────────────────

    /// <summary>
    /// A section already in the semester is skipped and only its key columns are checked, so a bad cell in any other
    /// column of its rows is not a problem (the cells are not looked at, as with every other problem a skipped
    /// section's cells could have).
    /// </summary>
    [Fact]
    public void BadCellInANonKeyColumnOfASkippedSection_IsNotAProblem()
    {
        var chem = Subj("CHEM");
        var crs = Crs(chem, "CHEM101");
        var report = SectionImportPlanner.Plan(
            Sheet(Mtg(2, "CHEM", "101", "A", "M", "0900", "1000")
                .WithIssues((C.Instructors, ErrorIssue()), (C.Capacity, DateIssue()), (C.CourseTitle, ErrorIssue()), (C.Room, ErrorIssue()))),
            Snapshot(subjects: new[] { chem }, courses: new[] { crs }, sections: new[] { Sect(crs, "A") }));

        Assert.Empty(report.Problems);
        var skipped = Assert.Single(report.SkippedSections);
        Assert.Equal("A", skipped.Key.SectionCode);
    }

    /// <summary>A bad cell in a key column of a row of a skipped section's rows is still a problem, as a blank key cell would be.</summary>
    [Fact]
    public void BadCellInAKeyColumnOfARowOfASkippedSection_IsStillAProblem()
    {
        var chem = Subj("CHEM");
        var crs = Crs(chem, "CHEM101");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A").WithIssues((C.Instructors, ErrorIssue())),
                Sec(3, "CHEM", "101", "A").WithIssues((C.Term, ErrorIssue()))),
            Snapshot(subjects: new[] { chem }, courses: new[] { crs }, sections: new[] { Sect(crs, "A") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal((3, C.Term, ErrorMessage), (problem.Row!.Value, problem.Column, problem.Message));
    }

    /// <summary>
    /// A section whose subject cannot be resolved is not examined further: its one problem is the subject's, and a
    /// bad cell in another column is reported only after that is fixed and the file checked again.
    /// </summary>
    [Fact]
    public void BadCellInASectionWhoseSubjectIsAmbiguous_IsNotReportedYet()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A").WithIssues((C.Capacity, ErrorIssue()))),
            Snapshot(subjects: new[] { Subj("CHEM", "Chemistry"), Subj("CHEM", "Chemistry again") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(C.Subject, problem.Column);
    }

    // ── Rows without issues ───────────────────────────────────────────────────

    /// <summary>A row built without issues behaves as before (no problem, section planned).</summary>
    [Fact]
    public void RowWithoutIssues_IsUnaffected()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.Empty(report.Problems);
        Assert.Single(report.Plan.NewSections);
    }
}

using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the section-level validation of <see cref="SectionImportPlanner"/> (step 24.4): a
/// section's rows agree (invariant 4), a new course has one title (invariant 5), Capacity, and the
/// final section values (D1 capacity, D2 tags, level). Pure: sheets and snapshots are built by hand.
/// </summary>
public class SectionImportPlannerValidationTests
{
    // ── Invariant 4: a section's rows agree ───────────────────────────────────

    /// <summary>A later row that fills a section-level column differently is a problem on that row, naming the first row.</summary>
    [Theory]
    [InlineData(C.SectionType, "Lecture", "Lab")]
    [InlineData(C.Instructors, "Smith, John", "Jones, Ann")]
    [InlineData(C.Tags, "Writing", "Honours")]
    [InlineData(C.Capacity, "30", "40")]
    [InlineData(C.Campus, "Abbotsford", "Chilliwack")]
    public void RowsThatDisagree_AreAProblemOnTheLaterRow(string column, string first, string second)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (column, first)), Sec(3, "CHEM", "101", "A", (column, second))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(3, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal($"CHEM101 A has a different {column} than row 2.", problem.Message);
    }

    /// <summary>Spelling differences in case and spacing are not a difference.</summary>
    [Fact]
    public void RowsThatAgreeIgnoringCaseAndSpacing_AreFine()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.SectionType, "Lecture"), (C.Campus, "Abbotsford")),
                Sec(3, "CHEM", "101", "A", (C.SectionType, " LECTURE "), (C.Campus, "abbotsford"))),
            Snapshot());

        Assert.Empty(report.Problems);
    }

    /// <summary>Instructors and tags listed in a different order are the same set, so not a difference.</summary>
    [Fact]
    public void InstructorsAndTagsInADifferentOrder_AreNotADifference()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John; Jones, Ann"), (C.Tags, "Writing; Honours")),
                Sec(3, "CHEM", "101", "A", (C.Instructors, "jones, ann ;Smith, John"), (C.Tags, "honours;WRITING"))),
            Snapshot());

        Assert.Empty(report.Problems);
    }

    /// <summary>A shorter or longer list is a difference even when it shares entries.</summary>
    [Fact]
    public void InstructorListsWithDifferentMembers_AreADifference()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Instructors, "Smith, John; Jones, Ann")),
                Sec(3, "CHEM", "101", "A", (C.Instructors, "Smith, John"))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(3, problem.Row);
        Assert.Equal(C.Instructors, problem.Column);
    }

    /// <summary>A blank later cell is fine: the section's value is the one on the row that fills it.</summary>
    [Fact]
    public void ABlankLaterCell_IsFine()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.SectionType, "Lecture"), (C.Capacity, "30")),
                Sec(3, "CHEM", "101", "A")),
            Snapshot());

        Assert.Empty(report.Problems);
    }

    /// <summary>When the first row is blank, the first row that fills the column is the one later rows are compared with.</summary>
    [Fact]
    public void TheComparisonIsWithTheFirstRowThatFillsTheColumn()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A"),
                Sec(3, "CHEM", "101", "A", (C.SectionType, "Lecture")),
                Sec(4, "CHEM", "101", "A", (C.SectionType, "Lab"))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(4, problem.Row);
        Assert.Equal("CHEM101 A has a different Section Type than row 3.", problem.Message);
    }

    /// <summary>Every disagreeing column and row is reported, not just the first.</summary>
    [Fact]
    public void AllDisagreementsAreReported()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.SectionType, "Lecture"), (C.Capacity, "30")),
                Sec(3, "CHEM", "101", "A", (C.SectionType, "Lab"), (C.Capacity, "40")),
                Sec(4, "CHEM", "101", "A", (C.Capacity, "50"))),
            Snapshot());

        Assert.Equal(
            new[] { "3|Capacity", "3|Section Type", "4|Capacity" },
            report.Problems.Select(p => $"{p.Row}|{p.Column}").OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    /// <summary>The section is named as typed on its first row (subject and number run together).</summary>
    [Fact]
    public void TheMessageNamesTheSectionAsTypedOnItsFirstRow()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "chem", " 101", "a", (C.Capacity, "30")),
                Sec(3, "CHEM", "101", "A", (C.Capacity, "40"))),
            Snapshot());

        Assert.Equal("chem101 a has a different Capacity than row 2.", Assert.Single(report.Problems).Message);
    }

    /// <summary>A section already in the semester is skipped whole: its other cells are never compared or checked.</summary>
    [Fact]
    public void ASkippedSection_ProducesNoProblemsOrWarningsFromItsOtherCells()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "General Chemistry");
        course.IsActive = false;
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Capacity, "lots"), (C.MeetingDays, "M"), (C.Begin, "0700"),
                    (C.Tags, "Writng"), (C.Instructors, "Smith, John"), (C.Building, "SCIENCE"), (C.Room, "1"),
                    (C.CourseTitle, "A different title")),
                Sec(3, "CHEM", "101", "A", (C.Capacity, "40"), (C.Tags, "Other"), (C.CourseTitle, "Yet another"))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }, sections: new[] { Sect(course, "A") },
                tags: new[] { Val("Writing") }, instructors: new[] { Inst("Smith", "Jon", "JS") },
                rooms: new[] { Rm("SCI", "100") }));

        Assert.Empty(report.Problems);
        Assert.Empty(report.Warnings);
        Assert.Empty(report.Plan.NewSections);
        Assert.Single(report.SkippedSections);
    }

    // ── Invariant 5: a new course has one title ───────────────────────────────

    /// <summary>Two sections of one new course that give different titles: a problem on the first row giving the other title.</summary>
    [Fact]
    public void ANewCourseWithTwoTitles_IsAProblemOnTheFirstDifferingRow()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(4, "CHEM", "199", "A", (C.CourseTitle, "Intro Chemistry")),
                Sec(5, "CHEM", "199", "B", (C.CourseTitle, "Special Topics")),
                Sec(6, "CHEM", "199", "B", (C.CourseTitle, "Special Topics"))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(5, problem.Row);
        Assert.Equal(C.CourseTitle, problem.Column);
        Assert.Equal("CHEM199's Course Title differs from row 4 (Intro Chemistry).", problem.Message);
    }

    /// <summary>Titles that differ only in case and spacing are the same title; blanks are ignored.</summary>
    [Fact]
    public void ATitleDifferingOnlyInCaseOrSpacing_IsFine_AndBlanksAreIgnored()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "199", "A", (C.CourseTitle, "Intro Chemistry")),
                Sec(3, "CHEM", "199", "B", (C.CourseTitle, "  intro   CHEMISTRY ")),
                Sec(4, "CHEM", "199", "C", (C.CourseTitle, ""))),
            Snapshot());

        Assert.Empty(report.Problems);
    }

    /// <summary>Each distinct extra title is reported once, on the first row that gives it.</summary>
    [Fact]
    public void EachDistinctExtraTitle_IsReportedOnce()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "199", "A", (C.CourseTitle, "One")),
                Sec(3, "CHEM", "199", "B", (C.CourseTitle, "Two")),
                Sec(4, "CHEM", "199", "C", (C.CourseTitle, "Two")),
                Sec(5, "CHEM", "199", "D", (C.CourseTitle, "Three"))),
            Snapshot());

        Assert.Equal(new[] { 3, 5 }, report.Problems.Select(p => p.Row!.Value).ToArray());
    }

    /// <summary>Two different new courses may of course have different titles.</summary>
    [Fact]
    public void DifferentNewCourses_MayHaveDifferentTitles()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "199", "A", (C.CourseTitle, "One")),
                Sec(3, "CHEM", "200", "A", (C.CourseTitle, "Two"))),
            Snapshot());

        Assert.Empty(report.Problems);
    }

    /// <summary>An existing course's differing title is a warning, never a problem (the stored title is kept).</summary>
    [Fact]
    public void AnExistingCoursesDifferingTitle_IsNotAProblem()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101", "General Chemistry");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.CourseTitle, "One")),
                Sec(3, "CHEM", "101", "B", (C.CourseTitle, "Two"))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }));

        Assert.Empty(report.Problems);
    }

    // ── Capacity ──────────────────────────────────────────────────────────────

    /// <summary>A whole number of zero or more is accepted as it is.</summary>
    [Theory]
    [InlineData("30", 30)]
    [InlineData("0", 0)]
    [InlineData("120", 120)]
    public void Capacity_WholeNumber_IsAccepted(string text, int expected)
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A", (C.Capacity, text))), Snapshot());

        Assert.Empty(report.Problems);
        Assert.Equal(expected, report.Plan.NewSections.Single().Capacity);
    }

    /// <summary>A blank capacity is not a problem.</summary>
    [Fact]
    public void Capacity_Blank_IsNotAProblem()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.Empty(report.Problems);
        Assert.Null(report.Plan.NewSections.Single().Capacity);
    }

    /// <summary>Decimals, negatives and words are problems, on the row that gives the capacity.</summary>
    [Theory]
    [InlineData("12.5")]
    [InlineData("-3")]
    [InlineData("lots")]
    [InlineData("1,000")]
    [InlineData("+30")]
    public void Capacity_NotAWholeNumber_IsAProblem(string text)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "101", "A", (C.Capacity, text))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(3, problem.Row);
        Assert.Equal(C.Capacity, problem.Column);
        Assert.Equal("Capacity must be a whole number, like 30.", problem.Message);
    }

    // ── D1: the final capacity ────────────────────────────────────────────────

    /// <summary>The file's capacity wins over the course's and the default.</summary>
    [Fact]
    public void D1_TheFilesCapacity_Wins()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101");
        course.Capacity = 50;
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Capacity, "30"))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }, defaultSectionCapacity: 25));

        Assert.Equal(30, report.Plan.NewSections.Single().Capacity);
    }

    /// <summary>A blank capacity takes the existing course's capacity.</summary>
    [Fact]
    public void D1_Blank_TakesTheCoursesCapacity()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101");
        course.Capacity = 50;
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A")),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }, defaultSectionCapacity: 25));

        Assert.Equal(50, report.Plan.NewSections.Single().Capacity);
    }

    /// <summary>With no course capacity (a new course has none), the default section capacity applies.</summary>
    [Fact]
    public void D1_Blank_AndNoCourseCapacity_TakesTheDefault()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A")), Snapshot(defaultSectionCapacity: 25));

        Assert.Equal(25, report.Plan.NewSections.Single().Capacity);
    }

    /// <summary>An explicit zero is a capacity, not a blank.</summary>
    [Fact]
    public void D1_ExplicitZero_IsNotTreatedAsBlank()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Capacity, "0"))), Snapshot(defaultSectionCapacity: 25));

        Assert.Equal(0, report.Plan.NewSections.Single().Capacity);
    }

    /// <summary>With nothing anywhere, the capacity is null.</summary>
    [Fact]
    public void D1_NothingAnywhere_IsNull()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.Null(report.Plan.NewSections.Single().Capacity);
    }

    // ── D2: tags, and the level ───────────────────────────────────────────────

    /// <summary>The file's tags come first, then the course's own tags, with no duplicates.</summary>
    [Fact]
    public void D2_FileTagsFirst_ThenCourseTags_NoDuplicates()
    {
        var subject = Subj("CHEM");
        var writing = Val("Writing");
        var lab = Val("Lab");
        var course = Crs(subject, "CHEM101");
        course.TagIds = new List<string> { lab.Id, "course-only-tag" };

        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Tags, "Writing; Lab; Brand New"))),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }, tags: new[] { writing, lab }));

        Assert.Empty(report.Problems);
        var brandNew = Assert.Single(report.Plan.NewTags);
        Assert.Equal(new[] { writing.Id, lab.Id, brandNew.Record.Id, "course-only-tag" },
            report.Plan.NewSections.Single().TagIds.ToArray());
    }

    /// <summary>A blank Tags cell still gives the course's tags; a new course gives none.</summary>
    [Fact]
    public void D2_BlankTags_GiveTheCoursesTags_AndANewCourseGivesNone()
    {
        var subject = Subj("CHEM");
        var course = Crs(subject, "CHEM101");
        course.TagIds = new List<string> { "t1", "t2" };

        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "102", "A")),
            Snapshot(subjects: new[] { subject }, courses: new[] { course }));

        Assert.Equal(new[] { "t1", "t2" }, report.Plan.NewSections[0].TagIds.ToArray());
        Assert.Empty(report.Plan.NewSections[1].TagIds);
    }

    /// <summary>
    /// The section's level is its course's stored level, as the section editor copies it. An existing
    /// course's level is used as it is, even when blank or unlike its number; a new course's level is
    /// read from its number.
    /// </summary>
    [Fact]
    public void Level_IsTheCoursesLevel_AndParsedForANewCourse()
    {
        var subject = Subj("CHEM");
        var set = Crs(subject, "CHEM101");
        set.Level = "200";   // deliberately not what the number suggests
        var blank = Crs(subject, "CHEM103");

        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "103", "A"), Sec(4, "CHEM", "302", "A")),
            Snapshot(subjects: new[] { subject }, courses: new[] { set, blank }));

        Assert.Equal("200", report.Plan.NewSections[0].Level);
        Assert.Equal("", report.Plan.NewSections[1].Level);
        Assert.Equal("300", report.Plan.NewSections[2].Level);
    }
}

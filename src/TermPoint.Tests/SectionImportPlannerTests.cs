using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the first part of <see cref="SectionImportPlanner"/> (step 24.3), the structural half:
/// required cells, the semester checks, grouping, subject and course resolution, skipped sections,
/// and the plan-level bookkeeping. Setup values, rooms and instructors are in
/// <see cref="SectionImportPlannerValueTests"/>. All tests are pure: sheets and snapshots are built by hand.
/// </summary>
public class SectionImportPlannerTests
{
    // ── Required key cells (invariant 6) ──────────────────────────────────────

    /// <summary>A blank required cell is a problem on that row and column, and the row is left out of grouping.</summary>
    [Theory]
    [InlineData(C.AcademicYear)]
    [InlineData(C.Term)]
    [InlineData(C.Subject)]
    [InlineData(C.CourseNumber)]
    [InlineData(C.SectionCode)]
    public void BlankRequiredCell_IsAProblemOnThatRowAndColumn_AndTheRowIsNotGrouped(string column)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (column, "  ")), Sec(3, "CHEM", "101", "B")),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(column, problem.Column);
        Assert.Equal($"{column} is empty.", problem.Message);
        var section = Assert.Single(report.Plan.NewSections);
        Assert.Equal("B", section.Key.SectionCode);
    }

    /// <summary>Every blank required cell of a row is reported, not just the first.</summary>
    [Fact]
    public void SeveralBlankRequiredCells_AreAllReported()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Row(2, (C.Subject, "CHEM"))), Snapshot());

        Assert.Equal(
            new[] { C.CourseNumber, C.SectionCode },
            report.Problems.Select(p => p.Column).ToArray());
    }

    // ── Semester (A2) ─────────────────────────────────────────────────────────

    /// <summary>A file that names one other semester throughout gets exactly one file-wide problem and nothing else.</summary>
    [Fact]
    public void WholeFileForAnotherSemester_IsExactlyOneFileWideProblem_AndNoPlan()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Term, "Spring"), (C.AcademicYear, "2027-2028")),
                Sec(3, "CHEM", "101", "B", (C.Term, " spring "), (C.AcademicYear, "2027-2028")),
                Sec(4, "CHEM", "101", "", (C.Term, "Spring"), (C.AcademicYear, "2027-2028"))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Null(problem.Row);
        Assert.Null(problem.Column);
        Assert.Equal(
            "This file is for Spring (2027-2028), but Fall (2026-2027) is open. " +
            "Open Spring to import it, or correct the Academic Year and Term columns.",
            problem.Message);
        Assert.Empty(report.Plan.NewSections);
        Assert.Empty(report.SkippedSections);
        Assert.Empty(report.Warnings);
    }

    /// <summary>One row naming another semester is a row problem; the other rows are still grouped.</summary>
    [Fact]
    public void OneWrongRow_IsARowProblem_AndTheOtherRowsAreStillGrouped()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A"),
                Sec(3, "CHEM", "101", "B", (C.Term, "Winter")),
                Sec(4, "CHEM", "101", "C")),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(3, problem.Row);
        Assert.Equal("This row is for Winter (2026-2027), but Fall (2026-2027) is open.", problem.Message);
        Assert.Equal(new[] { "A", "C" }, report.Plan.NewSections.Select(s => s.Key.SectionCode).ToArray());
    }

    /// <summary>The semester is matched ignoring case and spacing.</summary>
    [Fact]
    public void SemesterNames_AreComparedIgnoringCaseAndSpacing()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.Term, " FALL "), (C.AcademicYear, "2026-2027 "))),
            Snapshot());

        Assert.Empty(report.Problems);
        Assert.Single(report.Plan.NewSections);
    }

    // ── Grouping ──────────────────────────────────────────────────────────────

    /// <summary>Two rows of one section become one draft with both row numbers and one meeting row each.</summary>
    [Fact]
    public void TwoRowsOfOneSection_AreOneDraft()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.MeetingDays, "M,W"), (C.Begin, "1000"), (C.End, "1100")),
                Sec(3, "CHEM", "101", "A", (C.MeetingDays, "F"), (C.Begin, "1400"), (C.End, "1500"), (C.Frequency, "odd"))),
            Snapshot());

        var draft = Assert.Single(report.Plan.NewSections);
        Assert.Equal(new[] { 2, 3 }, draft.RowNumbers);
        Assert.Equal(2, draft.MeetingRows.Count);
        Assert.Equal("M,W", draft.MeetingRows[0].RawDays);
        Assert.Equal("1000", draft.MeetingRows[0].RawBegin);
        Assert.Equal("1100", draft.MeetingRows[0].RawEnd);
        Assert.Equal("", draft.MeetingRows[0].RawFrequency);
        Assert.Equal("odd", draft.MeetingRows[1].RawFrequency);
        Assert.Equal(3, draft.MeetingRows[1].RowNumber);
    }

    /// <summary>Rows of one section need not be adjacent; groups come in order of first appearance and rows keep file order.</summary>
    [Fact]
    public void NonAdjacentRowsOfOneSection_AreOneDraft_InFileOrder()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A"),
                Sec(3, "CHEM", "101", "B"),
                Sec(4, "CHEM", "101", "A"),
                Sec(5, "CHEM", "101", "B")),
            Snapshot());

        Assert.Equal(new[] { "A", "B" }, report.Plan.NewSections.Select(s => s.Key.SectionCode).ToArray());
        Assert.Equal(new[] { 2, 4 }, report.Plan.NewSections[0].RowNumbers);
        Assert.Equal(new[] { 3, 5 }, report.Plan.NewSections[1].RowNumbers);
    }

    /// <summary>Case and spacing differences in the key do not split a section; the first row's spelling names it.</summary>
    [Fact]
    public void CaseAndSpacingDifferencesInTheKey_StillGroup()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A  1"),
                Sec(3, " chem ", "10 1", "a 1")),
            Snapshot());

        var draft = Assert.Single(report.Plan.NewSections);
        Assert.Equal(new[] { 2, 3 }, draft.RowNumbers);
        Assert.Equal("A  1", draft.Key.SectionCode);
        Assert.Single(report.Plan.NewCourses);
        Assert.Single(report.Plan.NewSubjects);
    }

    /// <summary>The key's Equals and GetHashCode agree on normalised values.</summary>
    [Fact]
    public void SectionKey_ComparesNormalised()
    {
        var a = new SectionKey("CHEM", "101", "A B");
        var b = new SectionKey(" chem", "1 0 1", "a   b");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new SectionKey("CHEM", "101", "AB"));
    }

    // ── Subject ───────────────────────────────────────────────────────────────

    /// <summary>A subject matches by calendar abbreviation, ignoring case, and is the existing record.</summary>
    [Fact]
    public void Subject_Existing_IsMatchedByAbbreviation()
    {
        var chem = Subj("CHEM");
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "chem", "101", "A")), Snapshot(subjects: new[] { chem }));

        Assert.Empty(report.Problems);
        Assert.Empty(report.Plan.NewSubjects);
        var course = Assert.Single(report.Plan.NewCourses);
        Assert.Same(chem, course.Subject!.Record);
        Assert.False(course.Subject.IsNew);
        Assert.Equal(chem.Id, course.Record.SubjectId);
    }

    /// <summary>An unknown subject is planned once, with abbreviation and name both as first typed.</summary>
    [Fact]
    public void Subject_New_HasAbbreviationAndNameAsFirstTyped()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "chem", "102", "A")), Snapshot());

        Assert.Empty(report.Problems);
        var subject = Assert.Single(report.Plan.NewSubjects);
        Assert.True(subject.IsNew);
        Assert.Equal(2, subject.FirstRow);
        Assert.Equal("CHEM", subject.Record.CalendarAbbreviation);
        Assert.Equal("CHEM", subject.Record.Name);
        Assert.All(report.Plan.NewCourses, c => Assert.Same(subject, c.Subject));
        Assert.All(report.Plan.NewCourses, c => Assert.Equal(subject.Record.Id, c.Record.SubjectId));
    }

    /// <summary>Two subjects with the same abbreviation cannot be told apart: one problem, however many sections use it.</summary>
    [Fact]
    public void Subject_Ambiguous_IsOneProblemNamingTheValue()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "102", "A")),
            Snapshot(subjects: new[] { Subj("CHEM"), Subj("Chem") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Subject, problem.Column);
        Assert.Equal("There are two subjects with the abbreviation CHEM in TermPoint.", problem.Message);
        Assert.Empty(report.Plan.NewSections);
    }

    /// <summary>A new subject whose name is another subject's name is a problem that says which abbreviation to use.</summary>
    [Fact]
    public void Subject_NewWithTheNameOfAnotherSubject_IsAProblem()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "101", "B")),
            Snapshot(subjects: new[] { Subj("CHM", name: "Chem") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.Subject, problem.Column);
        Assert.Equal("CHEM is the name of subject CHM. Use CHM in the Subject column.", problem.Message);
        Assert.Empty(report.Plan.NewSubjects);
        Assert.Empty(report.Plan.NewSections);
    }

    // ── Course ────────────────────────────────────────────────────────────────

    /// <summary>An existing course matches ignoring spaces and case; nothing new is planned for it.</summary>
    [Fact]
    public void Course_Existing_MatchesIgnoringSpaces()
    {
        var chem = Subj("CHEM");
        var course = Crs(chem, "CHEM 101");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "chem", "10 1", "B")),
            Snapshot(subjects: new[] { chem }, courses: new[] { course }));

        Assert.Empty(report.Problems);
        Assert.Empty(report.Plan.NewCourses);
        Assert.All(report.Plan.NewSections, s => Assert.Same(course, s.Course.Record));
        Assert.All(report.Plan.NewSections, s => Assert.False(s.Course.IsNew));
        Assert.Same(report.Plan.NewSections[0].Course, report.Plan.NewSections[1].Course);
    }

    /// <summary>Two existing courses whose codes differ only in spacing cannot be told apart; both codes are named.</summary>
    [Fact]
    public void Course_Ambiguous_NamesBothCodesAsStored()
    {
        var chem = Subj("CHEM");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "101", "B")),
            Snapshot(subjects: new[] { chem }, courses: new[] { Crs(chem, "CHEM101"), Crs(chem, "CHEM 101") }));

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.CourseNumber, problem.Column);
        Assert.Equal(
            "There are two courses in TermPoint with the code CHEM101: CHEM101 and CHEM 101.",
            problem.Message);
        Assert.Empty(report.Plan.NewSections);
    }

    /// <summary>
    /// A new course gets the app's conventions: no spaces in the code, active, no tags or capacity, and
    /// the level the course editor would suggest from its number ("1 99" is 199, so "100").
    /// </summary>
    [Fact]
    public void Course_New_IsFullyBuilt()
    {
        var chem = Subj("CHEM");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "1 99", "A", (C.CourseTitle, "  Special topics "))),
            Snapshot(subjects: new[] { chem }));

        Assert.Empty(report.Problems);
        var course = Assert.Single(report.Plan.NewCourses);
        Assert.True(course.IsNew);
        Assert.Equal(2, course.FirstRow);
        Assert.Equal("CHEM199", course.Record.CalendarCode);
        Assert.Equal("Special topics", course.Record.Title);
        Assert.Equal(chem.Id, course.Record.SubjectId);
        Assert.True(course.Record.IsActive);
        Assert.Equal("100", course.Record.Level);
        Assert.Empty(course.Record.TagIds);
        Assert.Null(course.Record.Capacity);
        Assert.False(string.IsNullOrEmpty(course.Record.Id));
    }

    /// <summary>
    /// A new course's level is read from its Course Number as the course editor suggests one: the
    /// hundreds band of a clean three-digit block, spaces ignored; empty when there is no such block.
    /// Without it, the level filter never shows the course's sections.
    /// </summary>
    /// <param name="number">The Course Number cell.</param>
    /// <param name="expected">The level the new course should get.</param>
    [Theory]
    [InlineData("485", "400")]
    [InlineData("485L", "400")]
    [InlineData("485 L", "400")]
    [InlineData("099", "0")]
    [InlineData("4851", "")]
    [InlineData("SEM", "")]
    public void Course_New_LevelComesFromTheCourseNumber(string number, string expected)
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHBE", number, "A", (C.CourseTitle, "Process design"))),
            Snapshot(subjects: new[] { Subj("CHBE") }));

        Assert.Empty(report.Problems);
        Assert.Equal(expected, Assert.Single(report.Plan.NewCourses).Record.Level);
        Assert.Equal(expected, Assert.Single(report.Plan.NewSections).Level);
    }

    /// <summary>A new course named on many rows and sections is planned once and shared.</summary>
    [Fact]
    public void Course_New_IsPlannedOnce_AndShared()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "101", "B"), Sec(4, "CHEM", " 101", "A")),
            Snapshot());

        var course = Assert.Single(report.Plan.NewCourses);
        Assert.All(report.Plan.NewSections, s => Assert.Same(course, s.Course));
    }

    /// <summary>A new course with no title on any row is one problem, on its first row, in the Course Title column.</summary>
    [Fact]
    public void Course_NewWithoutTitle_IsOneProblemOnItsFirstRow()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "199", "A", (C.CourseTitle, "")),
                Sec(3, "CHEM", "199", "B", (C.CourseTitle, " "))),
            Snapshot());

        var problem = Assert.Single(report.Problems);
        Assert.Equal(2, problem.Row);
        Assert.Equal(C.CourseTitle, problem.Column);
        Assert.Equal("CHEM199 is a new course, so it needs a Course Title.", problem.Message);
    }

    /// <summary>The title of a new course is the first non-blank title of any of its rows, even a later one.</summary>
    [Fact]
    public void Course_NewTitle_ComesFromALaterRowWhenTheFirstIsBlank()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "199", "A", (C.CourseTitle, "")),
                Sec(3, "CHEM", "199", "B", (C.CourseTitle, "Topics")),
                Sec(4, "CHEM", "199", "C", (C.CourseTitle, "topics"))),
            Snapshot());

        Assert.Empty(report.Problems);
        Assert.Equal("Topics", Assert.Single(report.Plan.NewCourses).Record.Title);
    }

    /// <summary>An existing course needs no title: a blank one is not a problem.</summary>
    [Fact]
    public void Course_Existing_NeedsNoTitle()
    {
        var chem = Subj("CHEM");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A", (C.CourseTitle, ""))),
            Snapshot(subjects: new[] { chem }, courses: new[] { Crs(chem, "CHEM101") }));

        Assert.Empty(report.Problems);
    }

    // ── Section already exists (P3, invariant 3) ──────────────────────────────

    /// <summary>
    /// A section already in the semester is skipped, and nothing on its rows is resolved: an unknown room,
    /// tag, instructor, section type, meeting type and campus create nothing and report nothing.
    /// </summary>
    [Fact]
    public void ExistingSection_IsSkipped_AndItsOtherCellsAreNeverLookedAt()
    {
        var chem = Subj("CHEM");
        var course = Crs(chem, "CHEM101");
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "a",
                    (C.SectionType, "Mystery"), (C.Instructors, "Zed, Zed; , Nobody"), (C.Tags, "Nope; Nope"),
                    (C.Campus, "Moon"), (C.MeetingType, "Odd"), (C.Building, "XX"), (C.Room, ""),
                    (C.Capacity, "lots")),
                Sec(3, "CHEM", "101", "A", (C.Building, "XX"), (C.Room, "9"))),
            Snapshot(subjects: new[] { chem }, courses: new[] { course }, sections: new[] { Sect(course, "A") }));

        Assert.Empty(report.Problems);
        var skipped = Assert.Single(report.SkippedSections);
        Assert.True(skipped.AlreadyExists);
        Assert.Equal(new[] { 2, 3 }, skipped.RowNumbers);
        Assert.Same(course, skipped.Course.Record);
        Assert.Empty(skipped.Instructors);
        Assert.Empty(skipped.Tags);
        Assert.Null(skipped.SectionType);
        Assert.Null(skipped.Campus);
        Assert.Empty(skipped.MeetingRows);

        var plan = report.Plan;
        Assert.Empty(plan.NewSections);
        Assert.Empty(plan.NewSubjects);
        Assert.Empty(plan.NewCourses);
        Assert.Empty(plan.NewInstructors);
        Assert.Empty(plan.NewSectionTypes);
        Assert.Empty(plan.NewMeetingTypes);
        Assert.Empty(plan.NewTags);
        Assert.Empty(plan.NewCampuses);
        Assert.Empty(plan.NewRooms);
    }

    /// <summary>Only the same course and section code skip; another course's section with the same code is new.</summary>
    [Fact]
    public void ExistingSection_OnlyMatchesTheSameCourse()
    {
        var chem = Subj("CHEM");
        var c101 = Crs(chem, "CHEM101");
        var c102 = Crs(chem, "CHEM102");
        var report = SectionImportPlanner.Plan(
            Sheet(Sec(2, "CHEM", "101", "A"), Sec(3, "CHEM", "102", "A"), Sec(4, "CHEM", "101", "B")),
            Snapshot(subjects: new[] { chem }, courses: new[] { c101, c102 }, sections: new[] { Sect(c101, "A") }));

        Assert.Equal(new[] { 2 }, report.SkippedSections.Single().RowNumbers);
        Assert.Equal(new[] { "A", "B" }, report.Plan.NewSections.Select(s => s.Key.SectionCode).ToArray());
        Assert.Same(c102, report.Plan.NewSections[0].Course.Record);
    }

    // ── Report bookkeeping ────────────────────────────────────────────────────

    /// <summary>SemesterWasEmpty reflects whether the snapshot had sections in the open semester.</summary>
    [Fact]
    public void SemesterWasEmpty_ReflectsTheSnapshot()
    {
        var chem = Subj("CHEM");
        var course = Crs(chem, "CHEM101");
        var sheet = Sheet(Sec(2, "CHEM", "101", "A"));

        Assert.True(SectionImportPlanner.Plan(sheet, Snapshot(subjects: new[] { chem }, courses: new[] { course })).SemesterWasEmpty);
        Assert.False(SectionImportPlanner.Plan(
            sheet, Snapshot(subjects: new[] { chem }, courses: new[] { course }, sections: new[] { Sect(course, "Z") })).SemesterWasEmpty);
    }

    /// <summary>A clean file has no problems, no warnings (24.4 fills those) and HasProblems is false.</summary>
    [Fact]
    public void CleanFile_HasNoProblemsAndNoWarnings()
    {
        var report = SectionImportPlanner.Plan(Sheet(Sec(2, "CHEM", "101", "A")), Snapshot());

        Assert.False(report.HasProblems);
        Assert.Empty(report.Warnings);
    }

    /// <summary>Problems come back ordered by row, with file-wide ones first, whatever order they were found in.</summary>
    [Fact]
    public void Problems_AreOrderedByRow()
    {
        var report = SectionImportPlanner.Plan(
            Sheet(
                Sec(2, "CHEM", "101", "A", (C.Instructors, ", Bad")),
                Sec(3, "CHEM", "101", "B", (C.Term, "Winter")),
                Sec(4, "CHEM", "101", "C", (C.SectionCode, ""))),
            Snapshot());

        var rows = report.Problems.Select(p => p.Row).ToArray();
        Assert.Equal(rows.OrderBy(r => r ?? 0).ToArray(), rows);
        Assert.Equal(3, rows.Length);
    }
}

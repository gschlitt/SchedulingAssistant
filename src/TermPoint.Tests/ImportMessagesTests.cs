using TermPoint.Models;
using TermPoint.Services.SectionImport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the exact wording the Import Sections flyout builds from counts and names
/// (<see cref="ImportMessages"/>) and of the problem lines and copied list (<see cref="ImportProblemText"/>).
/// </summary>
public sealed class ImportMessagesTests
{
    // ── Summary ───────────────────────────────────────────────────────────────

    /// <summary>Zero courses and instructors are left out; sections are always named; singular and plural follow the count.</summary>
    [Theory]
    [InlineData(37, 5, 8, "Will add 37 sections, 5 new courses and 8 new instructors.")]
    [InlineData(1, 1, 1, "Will add 1 section, 1 new course and 1 new instructor.")]
    [InlineData(1, 0, 0, "Will add 1 section.")]
    [InlineData(12, 0, 0, "Will add 12 sections.")]
    [InlineData(3, 2, 0, "Will add 3 sections and 2 new courses.")]
    [InlineData(3, 0, 1, "Will add 3 sections and 1 new instructor.")]
    [InlineData(2, 1, 4, "Will add 2 sections, 1 new course and 4 new instructors.")]
    public void Summary_OmitsZeroParts_AndPluralisesCorrectly(int sections, int courses, int instructors, string expected)
    {
        Assert.Equal(expected, ImportMessages.Summary(sections, courses, instructors));
    }

    /// <summary>The skipped line names the term and uses the right plural.</summary>
    [Theory]
    [InlineData(12, "Already in Fall 2026, not changed: 12 sections.")]
    [InlineData(1, "Already in Fall 2026, not changed: 1 section.")]
    public void Skipped_NamesTheTerm(int skipped, string expected)
    {
        Assert.Equal(expected, ImportMessages.Skipped("Fall 2026", skipped));
    }

    /// <summary>The target and nothing-to-add lines name the term with its academic year, or just the term when the year is unknown.</summary>
    [Fact]
    public void TargetAndNothingToAdd_NameTheTermAndYear()
    {
        Assert.Equal("Sections will be added to Fall 2026 (2026-2027).", ImportMessages.TargetLine("Fall 2026", "2026-2027"));
        Assert.Equal("Every section in this file is already in Fall 2026 (2026-2027).", ImportMessages.NothingToAdd("Fall 2026", "2026-2027"));
        Assert.Equal("Sections will be added to Fall 2026.", ImportMessages.TargetLine("Fall 2026", ""));
    }

    // ── Done ──────────────────────────────────────────────────────────────────

    private static ImportCounts Counts(int sections = 0, int courses = 0, int instructors = 0, int subjects = 0,
        int campuses = 0, int sectionTypes = 0, int meetingTypes = 0, int tags = 0, int rooms = 0) =>
        new(subjects, campuses, sectionTypes, meetingTypes, tags, rooms, courses, instructors, sections);

    /// <summary>The done line omits zero courses and instructors and pluralises.</summary>
    [Theory]
    [InlineData(37, 5, 8, "Added 37 sections, 5 courses and 8 instructors to Fall 2026 (2026-2027).")]
    [InlineData(1, 1, 1, "Added 1 section, 1 course and 1 instructor to Fall 2026 (2026-2027).")]
    [InlineData(2, 0, 0, "Added 2 sections to Fall 2026 (2026-2027).")]
    [InlineData(2, 0, 3, "Added 2 sections and 3 instructors to Fall 2026 (2026-2027).")]
    [InlineData(2, 1, 0, "Added 2 sections and 1 course to Fall 2026 (2026-2027).")]
    public void Done_OmitsZeroCoursesAndInstructors(int sections, int courses, int instructors, string expected)
    {
        Assert.Equal(expected, ImportMessages.Done(Counts(sections, courses, instructors), "Fall 2026", "2026-2027"));
    }

    /// <summary>The setup line names only the kinds added, joined naturally; the rooms sentence follows only when rooms were added.</summary>
    [Fact]
    public void SetupLine_NamesOnlyTheKindsAdded()
    {
        Assert.Null(ImportMessages.SetupLine(Counts(sections: 5, courses: 2)));
        Assert.Equal(
            "Also added 2 subjects, 3 tags and 14 rooms. New rooms need their capacity and room type: see Rooms.",
            ImportMessages.SetupLine(Counts(subjects: 2, tags: 3, rooms: 14)));
        Assert.Equal("Also added 1 campus.", ImportMessages.SetupLine(Counts(campuses: 1)));
        Assert.Equal("Also added 2 campuses and 1 section type.", ImportMessages.SetupLine(Counts(campuses: 2, sectionTypes: 1)));
        Assert.Equal(
            "Also added 1 subject, 2 campuses, 3 section types, 4 meeting types, 5 tags and 6 rooms. "
            + "New rooms need their capacity and room type: see Rooms.",
            ImportMessages.SetupLine(Counts(subjects: 1, campuses: 2, sectionTypes: 3, meetingTypes: 4, tags: 5, rooms: 6)));
        Assert.Equal("Also added 1 room. New rooms need their capacity and room type: see Rooms.",
            ImportMessages.SetupLine(Counts(rooms: 1)));
    }

    // ── Display lines ─────────────────────────────────────────────────────────

    /// <summary>The display line of each new record.</summary>
    [Fact]
    public void DisplayLines_FollowTheFormats()
    {
        Assert.Equal("CHEM199 — Intro Chemistry", ImportMessages.CourseLine(new Course { CalendarCode = "CHEM199", Title = "Intro Chemistry" }));
        Assert.Equal("CHEM199", ImportMessages.CourseLine(new Course { CalendarCode = "CHEM199", Title = "" }));
        Assert.Equal("Smith, Jon (JS2)", ImportMessages.InstructorLine(new Instructor { LastName = "Smith", FirstName = "Jon", Initials = "JS2" }));
        Assert.Equal("Smith (JS)", ImportMessages.InstructorLine(new Instructor { LastName = "Smith", Initials = "JS" }));
        Assert.Equal("CHEM (add its full name in Subjects)", ImportMessages.SubjectLine(new Subject { CalendarAbbreviation = "CHEM", Name = "CHEM" }));
        Assert.Equal("SCI 210", ImportMessages.RoomLine(new Room { Building = "SCI", RoomNumber = "210" }));
    }

    /// <summary>With no setup value to create, "New in TermPoint" has no groups.</summary>
    [Fact]
    public void NewInTermPoint_WithNothingNew_IsEmpty()
    {
        Assert.Empty(ImportMessages.NewInTermPoint(ImportPlan.Empty));
    }

    // ── Problem lines and the copied list ─────────────────────────────────────

    /// <summary>A problem is "Row n, Column: message", "Row n: message", or just the message.</summary>
    [Fact]
    public void ProblemLine_HasThreeShapes()
    {
        Assert.Equal("Row 12, Room: \"210\" isn't in TermPoint yet.",
            ImportProblemText.Line(new ImportProblem(12, "Room", "\"210\" isn't in TermPoint yet.")));
        Assert.Equal("Row 15: CHEM101 A has a different Instructor(s) than row 14.",
            ImportProblemText.Line(new ImportProblem(15, null, "CHEM101 A has a different Instructor(s) than row 14.")));
        Assert.Equal("This file has no sheet named Sections.",
            ImportProblemText.Line(new ImportProblem(null, null, "This file has no sheet named Sections.")));
        Assert.Equal("Row 3: message", ImportProblemText.Line(new ImportProblem(3, "", "message")));
    }

    /// <summary>The copied list is a header plus one tab-separated line per problem, with blanks for a missing row or column.</summary>
    [Fact]
    public void CopyText_IsTabSeparatedWithAHeader()
    {
        var text = ImportProblemText.CopyText(new[]
        {
            new ImportProblem(null, null, "File-wide problem."),
            new ImportProblem(12, "Room", "Room \"210\" isn't there."),
            new ImportProblem(15, null, "Two\tlines\r\nin one."),
        });

        Assert.Equal(
            "Row\tColumn\tProblem\r\n"
            + "\t\tFile-wide problem.\r\n"
            + "12\tRoom\tRoom \"210\" isn't there.\r\n"
            + "15\t\tTwo lines in one.",
            text);
    }
}

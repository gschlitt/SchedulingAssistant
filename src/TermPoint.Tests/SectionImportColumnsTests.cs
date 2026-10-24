using TermPoint.Services;
using TermPoint.Services.SectionImport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Pins the section import template: the columns, their order, their level and which cells are always
/// required (design Phase 1 and Phase 2 invariant 6). A change to the template must change these tests.
/// </summary>
public class SectionImportColumnsTests
{
    /// <summary>The template's column names in template order, exactly as shown in the header row.</summary>
    private static readonly string[] ExpectedNames =
    {
        "Academic Year", "Term", "Subject", "Course Number", "Course Title", "Section Code", "Section Type",
        "Instructor(s)", "Tags", "Capacity", "Meeting Days", "Begin", "End", "Frequency", "Meeting Type",
        "Building", "Room", "Campus",
    };

    /// <summary>The columns hold the template's 18 names in template order.</summary>
    [Fact]
    public void All_HasTheTemplateColumnsInOrder() =>
        Assert.Equal(ExpectedNames, SectionImportColumns.All.Select(c => c.Name));

    /// <summary>Exactly the meeting-level columns are Meeting Days, Begin, End, Frequency, Meeting Type, Building and Room.</summary>
    [Fact]
    public void All_MeetingLevelColumnsAreTheMeetingOnes()
    {
        var meetingLevel = SectionImportColumns.All.Where(c => c.Level == ImportColumnLevel.Meeting).Select(c => c.Name);
        Assert.Equal(
            new[] { "Meeting Days", "Begin", "End", "Frequency", "Meeting Type", "Building", "Room" },
            meetingLevel);
    }

    /// <summary>Exactly Academic Year, Term, Subject, Course Number and Section Code may never be blank.</summary>
    [Fact]
    public void All_AlwaysRequiredColumnsAreTheFiveKeys()
    {
        var required = SectionImportColumns.All.Where(c => c.AlwaysRequired).Select(c => c.Name);
        Assert.Equal(new[] { "Academic Year", "Term", "Subject", "Course Number", "Section Code" }, required);
    }

    /// <summary>The named constants are the same strings as the list, so code never needs a literal.</summary>
    [Fact]
    public void Constants_MatchTheList()
    {
        string[] constants =
        {
            SectionImportColumns.AcademicYear, SectionImportColumns.Term, SectionImportColumns.Subject,
            SectionImportColumns.CourseNumber, SectionImportColumns.CourseTitle, SectionImportColumns.SectionCode,
            SectionImportColumns.SectionType, SectionImportColumns.Instructors, SectionImportColumns.Tags,
            SectionImportColumns.Capacity, SectionImportColumns.MeetingDays, SectionImportColumns.Begin,
            SectionImportColumns.End, SectionImportColumns.Frequency, SectionImportColumns.MeetingType,
            SectionImportColumns.Building, SectionImportColumns.Room, SectionImportColumns.Campus,
        };
        Assert.Equal(SectionImportColumns.All.Select(c => c.Name), constants);
    }

    /// <summary>No two columns are the same name once case and spacing are ignored (header matching depends on it).</summary>
    [Fact]
    public void Names_AreDistinctUnderNameMatch() =>
        Assert.Equal(SectionImportColumns.All.Count,
            SectionImportColumns.All.Select(c => c.Name).Distinct(NameMatch.Comparer).Count());
}

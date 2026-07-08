using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class CsvImportParserTests
{
    private readonly CsvImportParser _parser = new();

    [Fact]
    public void ParseInstructors_WellFormedFile_ParsesAllRows()
    {
        var csv = """
            LastName,FirstName,Initials,Email
            Smith,John,JS,jsmith@example.edu
            MacDonald,Alice,AM,amac@example.edu
            """;

        var result = _parser.ParseInstructors(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Smith", result.Rows[0].LastName);
        Assert.Equal("John", result.Rows[0].FirstName);
        Assert.Equal("JS", result.Rows[0].Initials);
        Assert.Equal("jsmith@example.edu", result.Rows[0].Email);
    }

    [Fact]
    public void ParseInstructors_BlankOptionalFields_Allowed()
    {
        var csv = """
            LastName,FirstName,Initials,Email
            Chen,,,
            """;

        var result = _parser.ParseInstructors(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("Chen", row.LastName);
        Assert.Equal("", row.FirstName);
    }

    [Fact]
    public void ParseInstructors_MissingLastName_ReportsErrorWithLineNumber()
    {
        var csv = """
            LastName,FirstName,Initials,Email
            Smith,John,JS,jsmith@example.edu
            ,Alice,AM,amac@example.edu
            """;

        var result = _parser.ParseInstructors(csv);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Smith", row.LastName);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains("LastName", error.Message);
    }

    [Fact]
    public void ParseInstructors_MissingColumnInHeader_TreatedAsBlank()
    {
        // No Initials or Email columns at all — still parses, not an error.
        var csv = """
            LastName,FirstName
            Smith,John
            """;

        var result = _parser.ParseInstructors(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("", row.Initials);
        Assert.Equal("", row.Email);
    }

    [Fact]
    public void ParseInstructors_QuotedFieldWithCommaAndEmbeddedNewline_ParsesCorrectly()
    {
        var csv = "LastName,FirstName,Initials,Email\n\"O'Brien, Jr.\",\"John\nQ.\",JO,jo@example.edu\n";

        var result = _parser.ParseInstructors(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("O'Brien, Jr.", row.LastName);
        Assert.Equal("John\nQ.", row.FirstName);
    }

    [Fact]
    public void ParseCourses_WellFormedFile_ParsesAllRows()
    {
        var csv = """
            SubjectCode,CalendarCode,Title
            CHEM,CHEM 101,Introductory Chemistry
            BIOL,BIOL 201,Cell Biology
            """;

        var result = _parser.ParseCourses(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("CHEM", result.Rows[0].SubjectCode);
        Assert.Equal("CHEM 101", result.Rows[0].CalendarCode);
        Assert.Equal("Introductory Chemistry", result.Rows[0].Title);
    }

    [Fact]
    public void ParseCourses_MissingCalendarCode_ReportsError()
    {
        var csv = """
            SubjectCode,CalendarCode,Title
            CHEM,,Introductory Chemistry
            """;

        var result = _parser.ParseCourses(csv);

        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.LineNumber);
        Assert.Contains("CalendarCode", error.Message);
    }

    [Fact]
    public void ParseSections_SingleMeetingSection_ParsesCorrectly()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,Monday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("CHEM 101", row.CourseCode);
        Assert.Equal("A", row.SectionCode);
        Assert.Equal("John Smith", row.Instructors);
        var meeting = Assert.Single(row.Meetings);
        Assert.Equal("Monday", meeting.Day);
        Assert.Equal("8:00 AM", meeting.StartTime);
        Assert.Equal("8:50 AM", meeting.EndTime);
        Assert.Equal("50", meeting.DurationMin);
        Assert.Equal("Science 101", meeting.Room);
    }

    [Fact]
    public void ParseSections_ContinuationRows_GroupedIntoOneSectionWithMultipleMeetings()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,Monday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            ,,,,,,,,,,,Wednesday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            ,,,,,,,,,,,Friday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(3, row.Meetings.Count);
        Assert.Equal("Monday", row.Meetings[0].Day);
        Assert.Equal("Wednesday", row.Meetings[1].Day);
        Assert.Equal("Friday", row.Meetings[2].Day);
    }

    [Fact]
    public void ParseSections_UnscheduledSection_NoMeetingsIsValid()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,,,,,,,
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Empty(row.Meetings);
    }

    [Fact]
    public void ParseSections_MissingRequiredField_ReportsErrorAndOrphansContinuation()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,Monday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            ,,,,,,,,,,,Wednesday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Rows);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Semester", result.Errors[0].Message);
        Assert.Contains("Continuation row has no preceding section", result.Errors[1].Message);
    }

    [Theory]
    [InlineData("Monday")]
    [InlineData("Mon")]
    [InlineData("M")]
    [InlineData("1")]
    public void ParseSections_FlexibleDayFormats_AllRecognized(string dayText)
    {
        var csv = $"""
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,{dayText},8:00 AM,8:50 AM,50,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Errors);
        var meeting = Assert.Single(Assert.Single(result.Rows).Meetings);
        Assert.Equal(dayText, meeting.Day);
    }

    [Fact]
    public void ParseSections_UnrecognizedDay_ReportsError()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,Someday,8:00 AM,8:50 AM,50,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Rows.Single().Meetings);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Unrecognized day", error.Message);
    }

    [Fact]
    public void ParseSections_EndTimeDerivedFromStartTimeAndDuration()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,Monday,8:00 AM,,50,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Errors);
        var meeting = Assert.Single(Assert.Single(result.Rows).Meetings);
        Assert.Equal("8:50 AM", meeting.EndTime);
    }

    [Fact]
    public void ParseSections_DurationDerivedFromStartAndEndTime()
    {
        var csv = """
            AcademicYear,Semester,CourseCode,CourseTitle,SectionCode,Instructors,SectionType,Campus,Tags,Resources,Reserves,Day,StartTime,EndTime,DurationMin,Room,Frequency,MeetingType
            2025-2026,Fall 2025,CHEM 101,Introductory Chemistry,A,John Smith,Lecture,Main,,,,Monday,10:00 AM,11:20 AM,,Science 101,,Lecture
            """;

        var result = _parser.ParseSections(csv);

        Assert.Empty(result.Errors);
        var meeting = Assert.Single(Assert.Single(result.Rows).Meetings);
        Assert.Equal("80", meeting.DurationMin);
    }
}

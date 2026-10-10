using System.Diagnostics;
using System.Text;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

public class CsvImportParserTests
{
    /// <summary>
    /// Generous ceiling for the large-file unterminated-quote test: a tripwire for
    /// order-of-magnitude (quadratic) regressions, not a micro-benchmark.
    /// </summary>
    private const int UnterminatedQuoteCeilingMs = 2000;

    private readonly CsvImportParser _parser = new();
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the fixture; <paramref name="output"/> receives timing measurements.</summary>
    public CsvImportParserTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>
    /// Regression (Bug B): a quoted field containing an odd number of escaped quotes
    /// (<c>"12"" ruler"</c>) must not be mistaken for an unterminated quote. The reader
    /// used to glue the next physical line onto this record, swallowing the next row.
    /// </summary>
    [Theory]
    [InlineData("\"12\"\" ruler\"", "12\" ruler")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    public void ParseInstructors_FieldWithOddNumberOfEscapedQuotes_DoesNotSwallowNextRow(string quotedField, string expectedValue)
    {
        var csv = $"LastName,FirstName,Initials,Email\n{quotedField},John,JS,js@example.edu\nChen,Alice,AC,ac@example.edu\n";

        var result = _parser.ParseInstructors(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(expectedValue, result.Rows[0].LastName);
        Assert.Equal("js@example.edu", result.Rows[0].Email);
        Assert.Equal("Chen", result.Rows[1].LastName);
        Assert.Equal("Alice", result.Rows[1].FirstName);
        Assert.Equal("ac@example.edu", result.Rows[1].Email);
    }

    /// <summary>
    /// Regression (Bug B): an empty quoted field (<c>""</c>) is a complete, balanced field;
    /// it must not cause the following row to be joined onto this one.
    /// </summary>
    [Fact]
    public void ParseInstructors_EmptyQuotedField_DoesNotSwallowNextRow()
    {
        var csv = "LastName,FirstName,Initials,Email\nSmith,\"\",JS,js@example.edu\nChen,Alice,AC,ac@example.edu\n";

        var result = _parser.ParseInstructors(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Smith", result.Rows[0].LastName);
        Assert.Equal("", result.Rows[0].FirstName);
        Assert.Equal("JS", result.Rows[0].Initials);
        Assert.Equal("Chen", result.Rows[1].LastName);
        Assert.Equal("Alice", result.Rows[1].FirstName);
    }

    /// <summary>
    /// Regression (Bug A): a single unmatched quote near the top of a large file used to
    /// glue every later line onto one record while rescanning the growing text each time
    /// (quadratic), freezing the UI. It must now fail fast, report the line where the
    /// unmatched quote opened, and return no rows (never the joined garbage).
    /// </summary>
    [Fact]
    public void ParseInstructors_UnterminatedQuoteInLargeFile_FailsFastWithOpeningLine()
    {
        var sb = new StringBuilder();
        sb.Append("LastName,FirstName,Initials,Email\n");              // line 1
        sb.Append("Smith,John,JS,js@example.edu\n");                   // line 2 (fine)
        sb.Append("\"Broken,Alice,AB,ab@example.edu\n");               // line 3: opens a quote that never closes
        for (int i = 0; i < 10_000; i++)                               // lines 4..10003
            sb.Append($"Name{i},First{i},N{i},n{i}@example.edu\n");

        var stopwatch = Stopwatch.StartNew();
        var result = _parser.ParseInstructors(sb.ToString());
        stopwatch.Stop();
        _output.WriteLine($"Unterminated quote in 10,003-line file: {stopwatch.ElapsedMilliseconds} ms");

        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.LineNumber);
        // The import dialogs render "Line {LineNumber}: {Message}", so the message must not repeat the line.
        Assert.Equal(CsvFormatException.Reason, error.Message);
        Assert.True(stopwatch.ElapsedMilliseconds < UnterminatedQuoteCeilingMs,
            $"Parsing took {stopwatch.ElapsedMilliseconds} ms (ceiling {UnterminatedQuoteCeilingMs} ms)");
    }

    /// <summary>
    /// Regression: a lone <c>"</c> inside an UNQUOTED field (an inch mark) is a literal, exactly
    /// as <c>ParseCsvRow</c> treats it. Two such marks on different lines used to "balance" each
    /// other, merging lines 3-7 into one record and silently losing the rows in between.
    /// </summary>
    [Fact]
    public void ParseCourses_UnquotedInchMarksOnDifferentLines_AllRowsReturnedSeparately()
    {
        var csv = "SubjectCode,CalendarCode,Title\n"        // line 1
                + "CHEM,CHEM 101,Intro\n"                    // line 2
                + "CHEM,CHEM 102,Measuring with a 5\" ruler\n"   // line 3: stray quote #1
                + "CHEM,CHEM 103,Titration\n"                // line 4
                + "CHEM,CHEM 104,Spectroscopy\n"             // line 5
                + "CHEM,CHEM 105,Lab safety\n"               // line 6
                + "CHEM,CHEM 106,Handling a 3\" tube\n"      // line 7: stray quote #2
                + "CHEM,CHEM 107,Capstone\n";                // line 8

        var result = _parser.ParseCourses(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(
            new[] { "CHEM 101", "CHEM 102", "CHEM 103", "CHEM 104", "CHEM 105", "CHEM 106", "CHEM 107" },
            result.Rows.Select(r => r.CalendarCode).ToArray());
        Assert.Equal("Measuring with a 5\" ruler", result.Rows[1].Title);
        Assert.Equal("Handling a 3\" tube", result.Rows[5].Title);
        Assert.Equal("Titration", result.Rows[2].Title);
    }

    /// <summary>
    /// All three CSV Import formats share <c>ReadHeaderAndLines</c>, so an unmatched quote
    /// (here in the header row itself) yields the same single-error, no-rows result from
    /// each public entry point.
    /// </summary>
    [Fact]
    public void ParseAll_UnterminatedQuoteInHeader_ReportsErrorOnLineOneAndNoRows()
    {
        const string csv = "\"LastName,FirstName\nSmith,John\n";

        var instructors = _parser.ParseInstructors(csv);
        var courses = _parser.ParseCourses(csv);
        var sections = _parser.ParseSections(csv);

        Assert.Empty(instructors.Rows);
        Assert.Empty(courses.Rows);
        Assert.Empty(sections.Rows);
        foreach (var errors in new[] { instructors.Errors, courses.Errors, sections.Errors })
        {
            var error = Assert.Single(errors);
            Assert.Equal(1, error.LineNumber);
            Assert.Equal(CsvFormatException.Reason, error.Message);
        }
    }

    /// <summary>
    /// A quoted field containing an embedded newline is one record (the field keeps its
    /// newline), and the row after it is reported at its true physical line number
    /// (line 4 here, although it is only the third data record).
    /// </summary>
    [Fact]
    public void ParseInstructors_EmbeddedNewlineInQuotedField_NextRowReportsPhysicalLineNumber()
    {
        var csv = "LastName,FirstName,Initials,Email\n"      // line 1
                + "Smith,\"John\n"                            // line 2: record starts here...
                + "Q.\",JS,js@example.edu\n"                  // line 3: ...and ends here
                + ",Alice,AC,ac@example.edu\n";               // line 4: missing LastName

        var result = _parser.ParseInstructors(csv);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Smith", row.LastName);
        Assert.Equal("John\nQ.", row.FirstName);
        var error = Assert.Single(result.Errors);
        Assert.Equal(4, error.LineNumber);
        Assert.Contains("LastName", error.Message);
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

using TermPoint.Services;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

public class SharedScheduleCsvParserTests
{
    /// <summary>
    /// Generous ceiling for the large-file unterminated-quote test: a tripwire for
    /// order-of-magnitude (quadratic) regressions, not a micro-benchmark.
    /// </summary>
    private const int UnterminatedQuoteCeilingMs = 2000;

    private readonly SharedScheduleCsvParser _parser = new();
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the fixture; <paramref name="output"/> receives timing measurements.</summary>
    public SharedScheduleCsvParserTests(ITestOutputHelper output) => _output = output;

    private ImportResult Parse(string csv, string fallback = "TestFile")
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return _parser.Parse(stream, fallback);
    }

    private ImportResult ParseWithBom(string csv, string fallback = "TestFile")
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bom.Concat(content).ToArray());
        return _parser.Parse(stream, fallback);
    }

    [Fact]
    public void WellFormedFile_WithHeader_ParsesCorrectly()
    {
        var csv = """
            #TermPoint Schedule Overlay,Chemistry Dept,2026-05-16
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,
            CHEM201,B,Lab goggles,Tuesday,10:00 AM,11:20 AM,80,600,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Chemistry Dept", result.Set!.SourceLabel);
        Assert.Equal(new DateTime(2026, 5, 16), result.Set.ExportedAt);
        Assert.Equal(2, result.Set.Sections.Count);

        var chem101 = result.Set.Sections.First(s => s.CourseCode == "CHEM101");
        Assert.Equal("A", chem101.SectionCode);
        Assert.Equal(2, chem101.Meetings.Count);
        Assert.Equal(1, chem101.Meetings[0].Day); // Monday
        Assert.Equal(3, chem101.Meetings[1].Day); // Wednesday

        var chem201 = result.Set.Sections.First(s => s.CourseCode == "CHEM201");
        Assert.Equal("Lab goggles", chem201.Notes);
        Assert.Equal(600, chem201.Meetings[0].StartMinutes);
        Assert.Equal(80, chem201.Meetings[0].DurationMinutes);
    }

    [Fact]
    public void FileWithoutHeader_UseFallbackLabel()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            BIO101,A,,Monday,9:00 AM,9:50 AM,50,540,
            """;

        var result = Parse(csv, "Biology Export");

        Assert.Null(result.FileError);
        Assert.Equal("Biology Export", result.Set!.SourceLabel);
        Assert.Null(result.Set.ExportedAt);
    }

    [Fact]
    public void MalformedHeader_TreatedAsNoHeader()
    {
        var csv = """
            #Some random comment that is not in the right format
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            BIO101,A,,Monday,9:00 AM,9:50 AM,50,540,
            """;

        var result = Parse(csv, "Fallback");

        Assert.Null(result.FileError);
        Assert.Equal("Fallback", result.Set!.SourceLabel);
    }

    [Fact]
    public void WrongColumnHeaders_RejectsFile()
    {
        var csv = """
            Name,Age,City,Country
            Alice,30,NYC,USA
            """;

        var result = Parse(csv);

        Assert.NotNull(result.FileError);
        Assert.Contains("Column headers", result.FileError);
    }

    [Fact]
    public void MixedValidAndInvalidRows_PartialSuccess()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            ,B,,Monday,8:00 AM,8:50 AM,50,480,
            CHEM201,B,,BadDay,10:00 AM,11:20 AM,80,600,
            CHEM301,C,,Friday,1:00 PM,1:50 PM,50,780,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal(2, result.Set!.Sections.Count);
        Assert.Equal(2, result.SkippedRows);
        Assert.Equal(4, result.TotalRows);
    }

    [Fact]
    public void AllRowsInvalid_RejectsFile()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            ,,,,,,,
            ,B,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.NotNull(result.FileError);
        Assert.Contains("No valid sections", result.FileError);
    }

    [Fact]
    public void ExceedsMaxRows_RejectsFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency");
        for (int i = 0; i <= 3000; i++)
            sb.AppendLine($"COURSE{i},A,,Monday,8:00 AM,8:50 AM,50,480,");

        var result = Parse(sb.ToString());

        Assert.NotNull(result.FileError);
        Assert.Contains("3000", result.FileError);
    }

    [Fact]
    public void QuotedFieldWithCommas_ParsesCorrectly()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,"Lab requires goggles, gloves",Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Lab requires goggles, gloves", result.Set!.Sections[0].Notes);
    }

    [Fact]
    public void QuotedFieldWithNewlines_ParsesCorrectly()
    {
        var csv = "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\nCHEM101,A,\"Line 1\nLine 2\",Monday,8:00 AM,8:50 AM,50,480,\n";

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Line 1\nLine 2", result.Set!.Sections[0].Notes);
    }

    [Fact]
    public void EscapedQuotes_ParsesCorrectly()
    {
        var csv = "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\nCHEM101,A,\"She said \"\"hello\"\"\",Monday,8:00 AM,8:50 AM,50,480,\n";

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("She said \"hello\"", result.Set!.Sections[0].Notes);
    }

    /// <summary>
    /// Regression (Bug B): a quoted field containing an odd number of escaped quotes
    /// (<c>"12"" ruler"</c>) must not be mistaken for an unterminated quote. The reader
    /// used to glue the next physical line onto this record, swallowing the next row.
    /// </summary>
    [Theory]
    [InlineData("\"12\"\" ruler\"", "12\" ruler")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    public void FieldWithOddNumberOfEscapedQuotes_DoesNotSwallowNextRow(string quotedField, string expectedNotes)
    {
        var csv = "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\n"
                + $"CHEM101,A,{quotedField},Monday,8:00 AM,8:50 AM,50,480,\n"
                + "CHEM201,B,,Tuesday,10:00 AM,11:20 AM,80,600,\n";

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(0, result.SkippedRows);
        Assert.Equal(2, result.Set!.Sections.Count);
        var chem101 = result.Set.Sections.First(s => s.CourseCode == "CHEM101");
        Assert.Equal(expectedNotes, chem101.Notes);
        var chem201 = result.Set.Sections.First(s => s.CourseCode == "CHEM201");
        Assert.Equal(600, chem201.Meetings[0].StartMinutes);
    }

    /// <summary>
    /// Regression (Bug B): an empty quoted field (<c>""</c>) is a complete, balanced field;
    /// it must not cause the following row to be joined onto this one.
    /// </summary>
    [Fact]
    public void EmptyQuotedField_DoesNotSwallowNextRow()
    {
        var csv = "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\n"
                + "CHEM101,A,\"\",Monday,8:00 AM,8:50 AM,50,480,\n"
                + "CHEM201,B,,Tuesday,10:00 AM,11:20 AM,80,600,\n";

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(0, result.SkippedRows);
        Assert.Equal(2, result.Set!.Sections.Count);
        Assert.Null(result.Set.Sections.First(s => s.CourseCode == "CHEM101").Notes);
        Assert.Equal(600, result.Set.Sections.First(s => s.CourseCode == "CHEM201").Meetings[0].StartMinutes);
    }

    /// <summary>
    /// Regression (Bug A): a single unmatched quote near the top of a large file used to
    /// glue every later line onto one record while rescanning the growing text each time
    /// (quadratic), freezing the UI. It must now fail fast with a file-level error that
    /// names the line where the unmatched quote opened (never the joined garbage).
    /// </summary>
    [Fact]
    public void UnterminatedQuoteInLargeFile_FailsFastWithOpeningLine()
    {
        var sb = new StringBuilder();
        sb.Append("CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\n");  // line 1
        sb.Append("CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,\n");                                              // line 2 (fine)
        sb.Append("CHEM102,A,\"Broken note,Monday,8:00 AM,8:50 AM,50,480,\n");                                 // line 3: opens a quote that never closes
        for (int i = 0; i < 10_000; i++)                                                                        // lines 4..10003
            sb.Append($"COURSE{i},A,,Monday,8:00 AM,8:50 AM,50,480,\n");

        var stopwatch = Stopwatch.StartNew();
        var result = Parse(sb.ToString());
        stopwatch.Stop();
        _output.WriteLine($"Unterminated quote in 10,003-line file: {stopwatch.ElapsedMilliseconds} ms");

        Assert.Null(result.Set);
        Assert.NotNull(result.FileError);
        // The sharing UI shows FileError verbatim (no "Line N:" prefix of its own), so it carries the line.
        Assert.Equal($"Line 3: {CsvFormatException.Reason}", result.FileError);
        Assert.True(stopwatch.ElapsedMilliseconds < UnterminatedQuoteCeilingMs,
            $"Parsing took {stopwatch.ElapsedMilliseconds} ms (ceiling {UnterminatedQuoteCeilingMs} ms)");
    }

    /// <summary>
    /// Regression: a lone <c>"</c> inside an UNQUOTED field (an inch mark) is a literal, exactly
    /// as <c>ParseCsvRow</c> treats it. Two such marks on different lines used to "balance" each
    /// other, merging lines 3-7 into one record and silently losing the rows in between.
    /// </summary>
    [Fact]
    public void UnquotedInchMarksOnDifferentLines_AllRowsParsedSeparately()
    {
        var csv = "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\n"  // line 1
                + "CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,\n"                                              // line 2
                + "CHEM102,A,Bring a 5\" ruler,Monday,9:00 AM,9:50 AM,50,540,\n"                             // line 3: stray quote #1
                + "CHEM103,A,,Monday,10:00 AM,10:50 AM,50,600,\n"                                           // line 4
                + "CHEM104,A,,Monday,11:00 AM,11:50 AM,50,660,\n"                                           // line 5
                + "CHEM105,A,,Monday,12:00 PM,12:50 PM,50,720,\n"                                           // line 6
                + "CHEM106,A,Bring a 3\" tube,Monday,1:00 PM,1:50 PM,50,780,\n"                              // line 7: stray quote #2
                + "CHEM107,A,,Monday,2:00 PM,2:50 PM,50,840,\n";                                            // line 8

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal(7, result.TotalRows);
        Assert.Equal(0, result.SkippedRows);
        Assert.Empty(result.Warnings);
        var byCode = result.Set!.Sections.ToDictionary(s => s.CourseCode);
        Assert.Equal(7, byCode.Count);
        Assert.Equal("Bring a 5\" ruler", byCode["CHEM102"].Notes);
        Assert.Equal("Bring a 3\" tube", byCode["CHEM106"].Notes);
        Assert.Equal(660, byCode["CHEM104"].Meetings[0].StartMinutes);   // a row that used to be swallowed
    }

    /// <summary>
    /// With a <c>#</c> header comment the data rows start on physical line 3, not 2, so an
    /// unmatched quote on the first data row must report line 3 (and line 2 without one).
    /// </summary>
    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public void UnterminatedQuoteOnFirstDataRow_ReportsPhysicalLineAccountingForHeaderComment(bool withHeaderComment, int expectedLine)
    {
        var csv = (withHeaderComment ? "#TermPoint Schedule Overlay,Chemistry Dept,2026-05-16\n" : "")
                + "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\n"
                + "CHEM101,A,\"Broken note,Monday,8:00 AM,8:50 AM,50,480,\n"
                + "CHEM201,B,,Tuesday,10:00 AM,11:20 AM,80,600,\n";

        var result = Parse(csv);

        Assert.Null(result.Set);
        Assert.NotNull(result.FileError);
        Assert.Equal($"Line {expectedLine}: {CsvFormatException.Reason}", result.FileError);
    }

    /// <summary>
    /// A quoted field containing an embedded newline is one record (the field keeps its
    /// newline), and a later bad row is reported at its true physical line number: line 4
    /// without a header comment (line 5 with one), although it is only the second record.
    /// </summary>
    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 5)]
    public void EmbeddedNewlineInQuotedField_NextRowReportsPhysicalLineNumber(bool withHeaderComment, int expectedWarningLine)
    {
        var csv = (withHeaderComment ? "#TermPoint Schedule Overlay,Chemistry Dept,2026-05-16\n" : "")
                + "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\n"
                + "CHEM101,A,\"Line 1\n"                                     // record starts on this line...
                + "Line 2\",Monday,8:00 AM,8:50 AM,50,480,\n"                // ...and ends on the next
                + "CHEM201,B,,BadDay,10:00 AM,11:20 AM,80,600,\n";           // bad day -> warning

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.SkippedRows);
        var section = Assert.Single(result.Set!.Sections);
        Assert.Equal("Line 1\nLine 2", section.Notes);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(expectedWarningLine, warning.LineNumber);
        Assert.Contains("Unrecognized day", warning.Reason);
    }

    [Fact]
    public void BomPresent_StrippedTransparently()
    {
        var csv = """
            #TermPoint Schedule Overlay,Test,2026-01-01
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            BIO101,A,,Monday,9:00 AM,9:50 AM,50,540,
            """;

        var result = ParseWithBom(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Test", result.Set!.SourceLabel);
    }

    [Fact]
    public void EmptyFile_RejectsFile()
    {
        var result = Parse("");
        Assert.NotNull(result.FileError);
        Assert.Contains("empty", result.FileError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnscheduledSection_AllTimeFieldsBlank_Valid()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,Unscheduled,,,,,,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Single(result.Set!.Sections);
        Assert.Empty(result.Set.Sections[0].Meetings);
    }

    [Fact]
    public void PartialTimeFields_RowSkipped()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,,,50,,
            """;

        var result = Parse(csv);

        Assert.NotNull(result.FileError);
        Assert.Contains("No valid sections", result.FileError);
    }

    [Fact]
    public void FrequencyValues_ParseCorrectly()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,odd
            CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,even
            CHEM201,B,,Tuesday,10:00 AM,11:20 AM,80,600,"1,6,7"
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var chem101 = result.Set!.Sections.First(s => s.CourseCode == "CHEM101");
        Assert.Equal("odd", chem101.Meetings[0].Frequency);
        Assert.Equal("even", chem101.Meetings[1].Frequency);

        var chem201 = result.Set.Sections.First(s => s.CourseCode == "CHEM201");
        Assert.Equal("1,6,7", chem201.Meetings[0].Frequency);
    }

    [Fact]
    public void InvalidFrequency_TreatedAsWeeklyWithWarning()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,badvalue
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Null(result.Set!.Sections[0].Meetings[0].Frequency);
        Assert.Single(result.Warnings);
        Assert.Contains("badvalue", result.Warnings[0].Reason);
    }

    [Theory]
    [InlineData("Monday", 1)]
    [InlineData("Mon", 1)]
    [InlineData("M", 1)]
    [InlineData("Tuesday", 2)]
    [InlineData("T", 2)]
    [InlineData("Wednesday", 3)]
    [InlineData("W", 3)]
    [InlineData("Thursday", 4)]
    [InlineData("Th", 4)]
    [InlineData("R", 4)]
    [InlineData("Friday", 5)]
    [InlineData("F", 5)]
    [InlineData("Saturday", 6)]
    [InlineData("Sa", 6)]
    [InlineData("Sunday", 7)]
    [InlineData("Su", 7)]
    public void DayNameVariants_ParseToCorrectInt(string dayName, int expected)
    {
        var csv = $"CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency\nTEST,A,,{dayName},8:00 AM,8:50 AM,50,480,\n";

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal(expected, result.Set!.Sections[0].Meetings[0].Day);
    }

    [Fact]
    public void AmbiguousS_RowSkipped()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,S,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.NotNull(result.FileError);
        Assert.Contains("No valid sections", result.FileError);
    }

    [Fact]
    public void CaseInsensitiveColumnMatching()
    {
        var csv = """
            coursecode,sectioncode,notes,day,starttime,endtime,durationmin,startminutes,frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Single(result.Set!.Sections);
    }

    [Fact]
    public void CaseInsensitiveSectionGrouping()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            chem101,a,,Wednesday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Single(result.Set!.Sections);
        Assert.Equal(2, result.Set.Sections[0].Meetings.Count);
    }
}

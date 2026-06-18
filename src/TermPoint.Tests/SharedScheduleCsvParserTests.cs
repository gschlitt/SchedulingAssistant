using TermPoint.Services;
using System.Text;
using Xunit;

namespace TermPoint.Tests;

public class SharedScheduleCsvParserTests
{
    private readonly SharedScheduleCsvParser _parser = new();

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

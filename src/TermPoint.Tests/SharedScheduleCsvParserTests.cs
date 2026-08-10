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

        var chem101 = result.Set.Sections.First(s => s.DisplayCourseCode == "CHEM101");
        Assert.Equal("A", chem101.SectionCode);
        Assert.True(chem101.IsShared);
        Assert.Null(chem101.CourseId);
        Assert.Equal(2, chem101.Schedule.Count);
        Assert.Equal(1, chem101.Schedule[0].Day); // Monday
        Assert.Equal(3, chem101.Schedule[1].Day); // Wednesday

        var chem201 = result.Set.Sections.First(s => s.DisplayCourseCode == "CHEM201");
        Assert.Equal("Lab goggles", chem201.Notes);
        Assert.Equal(600, chem201.Schedule[0].StartMinutes);
        Assert.Equal(80, chem201.Schedule[0].DurationMinutes);
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
        Assert.Empty(result.Set.Sections[0].Schedule);
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
        var chem101 = result.Set!.Sections.First(s => s.DisplayCourseCode == "CHEM101");
        Assert.Equal("odd", chem101.Schedule[0].Frequency);
        Assert.Equal("even", chem101.Schedule[1].Frequency);

        var chem201 = result.Set.Sections.First(s => s.DisplayCourseCode == "CHEM201");
        Assert.Equal("1,6,7", chem201.Schedule[0].Frequency);
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
        Assert.Null(result.Set!.Sections[0].Schedule[0].Frequency);
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
        Assert.Equal(expected, result.Set!.Sections[0].Schedule[0].Day);
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
        Assert.Equal(2, result.Set.Sections[0].Schedule.Count);
    }

    [Fact]
    public void NewHeaderFormat_ParsesSemesterName()
    {
        var csv = """
            #TermPoint Schedule Overlay,Chemistry Dept,Fall 2026,2026-07-15
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Chemistry Dept", result.Set!.SourceLabel);
        Assert.Equal("Fall 2026", result.SemesterName);
        Assert.Equal(new DateTime(2026, 7, 15), result.Set.ExportedAt);
    }

    [Fact]
    public void SixFieldHeader_ParsesAcademicYearAndSemesterId()
    {
        var csv = """
            #TermPoint Schedule Overlay,Chemistry Dept,Fall,2026-07-15,2026-2027,sem-abc-123
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Fall", result.SemesterName);
        Assert.Equal("2026-2027", result.AcademicYearName);
        Assert.Equal("sem-abc-123", result.SemesterId);
        Assert.Equal(new DateTime(2026, 7, 15), result.Set!.ExportedAt);
    }

    /// <summary>
    /// Files written before the academic year was recorded must still parse; the new fields
    /// simply come back null so the importer can warn instead of refusing.
    /// </summary>
    [Fact]
    public void FourFieldHeader_AcademicYearAndSemesterIdAreNull()
    {
        var csv = """
            #TermPoint Schedule Overlay,Chemistry Dept,Fall 2026,2026-07-15
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Fall 2026", result.SemesterName);
        Assert.Null(result.AcademicYearName);
        Assert.Null(result.SemesterId);
    }

    /// <summary>
    /// The source label is free text typed by the user. An unescaped comma in it used to shift
    /// every following field, handing back a fragment of the label as the semester name — which
    /// silently defeated the semester guard. The label is now CSV-escaped on export and read
    /// back with the RFC-4180 row parser.
    /// </summary>
    [Fact]
    public void HeaderWithCommaInSourceLabel_DoesNotCorruptLaterFields()
    {
        var csv = """
            #TermPoint Schedule Overlay,"Chemistry, 2nd yr courses",Fall,2026-07-15,2026-2027,sem-1
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Chemistry, 2nd yr courses", result.Set!.SourceLabel);
        Assert.Equal("Fall", result.SemesterName);
        Assert.Equal("2026-2027", result.AcademicYearName);
        Assert.Equal("sem-1", result.SemesterId);
        Assert.Equal(new DateTime(2026, 7, 15), result.Set.ExportedAt);
    }

    [Fact]
    public void LegacyHeaderFormat_SemesterNameIsNull()
    {
        var csv = """
            #TermPoint Schedule Overlay,Chemistry Dept,2026-05-16
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Null(result.SemesterName);
        Assert.Equal(new DateTime(2026, 5, 16), result.Set!.ExportedAt);
    }

    [Fact]
    public void ParsedSections_AreShared()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.True(section.IsShared);
        Assert.Null(section.CourseId);
        Assert.Equal("CHEM101", section.DisplayCourseCode);
    }

    // ── 18-column enriched format tests ─────────────────────────────────────

    private const string EnrichedHeader =
        "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency,Instructor,Initials,Building,RoomNumber,Campus,SectionType,Tags,MeetingType,Level";

    [Fact]
    public void EnrichedFormat_ParsesInstructorsAndInitials()
    {
        var csv = $"""
            #TermPoint Schedule Overlay,Chemistry Dept,Fall 2026,2026-07-15
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,204,Main Campus,Lecture,Upper Level|Pre-Med Required,In Person,100
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.NotNull(section.DisplayInstructors);
        Assert.Equal(2, section.DisplayInstructors!.Count);
        Assert.Equal("Smith, John", section.DisplayInstructors[0].Name);
        Assert.Equal("JRS", section.DisplayInstructors[0].Initials);
        Assert.Equal("Doe, Jane", section.DisplayInstructors[1].Name);
        Assert.Equal("JD", section.DisplayInstructors[1].Initials);
    }

    [Fact]
    public void EnrichedFormat_ParsesSingleInstructor()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM201,B,,Tuesday,1:00 PM,2:20 PM,80,780,,"Doe, Jane",JD,Arts Building,301,Main Campus,Lecture,Upper Level,,200
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.NotNull(section.DisplayInstructors);
        Assert.Single(section.DisplayInstructors!);
        Assert.Equal("Doe, Jane", section.DisplayInstructors[0].Name);
        Assert.Equal("JD", section.DisplayInstructors[0].Initials);
    }

    [Fact]
    public void EnrichedFormat_ParsesBuildingAndRoomPerMeeting()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John",JRS,Science Building,204,Main Campus,Lecture,,In Person,100
            CHEM101,A,,Friday,8:00 AM,8:50 AM,50,480,,"Smith, John",JRS,Science Building,110,Main Campus,Lecture,,Lab,100
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.Equal(2, section.Schedule.Count);

        Assert.Equal("Science Building", section.Schedule[0].ImportedBuilding);
        Assert.Equal("204", section.Schedule[0].ImportedRoomNumber);
        Assert.Equal("In Person", section.Schedule[0].ImportedMeetingTypeName);

        Assert.Equal("Science Building", section.Schedule[1].ImportedBuilding);
        Assert.Equal("110", section.Schedule[1].ImportedRoomNumber);
        Assert.Equal("Lab", section.Schedule[1].ImportedMeetingTypeName);
    }

    [Fact]
    public void EnrichedFormat_ParsesCampusAndSectionType()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,,,Science Building,204,Main Campus,Lecture,,,100
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.Equal("Main Campus", section.ImportedCampusName);
        Assert.Equal("Lecture", section.ImportedSectionTypeName);
    }

    [Fact]
    public void EnrichedFormat_ParsesPipeDelimitedTags()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,,,,,,,"Upper Level|Pre-Med Required",,100
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.NotNull(section.ImportedTagNames);
        Assert.Equal(2, section.ImportedTagNames!.Count);
        Assert.Equal("Upper Level", section.ImportedTagNames[0]);
        Assert.Equal("Pre-Med Required", section.ImportedTagNames[1]);
    }

    [Fact]
    public void EnrichedFormat_ParsesLevel()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,,,,,,,,,300
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("300", result.Set!.Sections[0].Level);
    }

    [Fact]
    public void EnrichedFormat_PerSectionFieldsFromFirstRowOnly()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John",JRS,Science Building,204,Main Campus,Lecture,Upper Level,In Person,100
            CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,,"Different, Person",DP,Arts Building,301,Downtown,Seminar,Graduate,Online,200
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];

        // Per-section fields from first row
        Assert.Equal("Smith, John", section.DisplayInstructors![0].Name);
        Assert.Equal("Main Campus", section.ImportedCampusName);
        Assert.Equal("Lecture", section.ImportedSectionTypeName);
        Assert.Equal("Upper Level", section.ImportedTagNames![0]);
        Assert.Equal("100", section.Level);

        // Per-meeting fields vary by row
        Assert.Equal("Science Building", section.Schedule[0].ImportedBuilding);
        Assert.Equal("204", section.Schedule[0].ImportedRoomNumber);
        Assert.Equal("In Person", section.Schedule[0].ImportedMeetingTypeName);

        Assert.Equal("Arts Building", section.Schedule[1].ImportedBuilding);
        Assert.Equal("301", section.Schedule[1].ImportedRoomNumber);
        Assert.Equal("Online", section.Schedule[1].ImportedMeetingTypeName);
    }

    [Fact]
    public void EnrichedFormat_EmptyEnrichedFields_AllNull()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,,,,,,,,,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.Null(section.DisplayInstructors);
        Assert.Null(section.ImportedCampusName);
        Assert.Null(section.ImportedSectionTypeName);
        Assert.Null(section.ImportedTagNames);
        Assert.Null(section.Level);
        Assert.Null(section.Schedule[0].ImportedBuilding);
        Assert.Null(section.Schedule[0].ImportedRoomNumber);
        Assert.Null(section.Schedule[0].ImportedMeetingTypeName);
    }

    [Fact]
    public void EnrichedFormat_InstructorsWithMoreNamesThanInitials()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS,,,,,,,,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var instructors = result.Set!.Sections[0].DisplayInstructors!;
        Assert.Equal(2, instructors.Count);
        Assert.Equal("JRS", instructors[0].Initials);
        Assert.Equal("", instructors[1].Initials);
    }

    [Fact]
    public void EnrichedFormat_InstructorsWithNoInitialsColumn()
    {
        // Only Instructor column present, no Initials column
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency,Instructor
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane"
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var instructors = result.Set!.Sections[0].DisplayInstructors!;
        Assert.Equal(2, instructors.Count);
        Assert.Equal("Smith, John", instructors[0].Name);
        Assert.Equal("", instructors[0].Initials);
        Assert.Equal("Doe, Jane", instructors[1].Name);
        Assert.Equal("", instructors[1].Initials);
    }

    [Fact]
    public void EnrichedFormat_SingleTag()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,,,,,,,"Upper Level",,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var tags = result.Set!.Sections[0].ImportedTagNames!;
        Assert.Single(tags);
        Assert.Equal("Upper Level", tags[0]);
    }

    [Fact]
    public void EnrichedFormat_UnscheduledSectionStillGetsEnrichedFields()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM101,A,TBD,,,,,,,"Smith, John",JRS,,,Main Campus,Lecture,Upper Level,,100
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.Empty(section.Schedule);
        Assert.Equal("Smith, John", section.DisplayInstructors![0].Name);
        Assert.Equal("Main Campus", section.ImportedCampusName);
        Assert.Equal("Lecture", section.ImportedSectionTypeName);
        Assert.Equal("Upper Level", section.ImportedTagNames![0]);
        Assert.Equal("100", section.Level);
    }

    [Fact]
    public void EnrichedFormat_FullExampleFromSpec()
    {
        var csv = """
            #TermPoint Schedule Overlay,Chemistry Department,Fall 2026,2026-07-15
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency,Instructor,Initials,Building,RoomNumber,Campus,SectionType,Tags,MeetingType,Level
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,204,Main Campus,Lecture,"Upper Level|Pre-Med Required",In Person,100
            CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,204,Main Campus,Lecture,"Upper Level|Pre-Med Required",In Person,100
            CHEM101,A,,Friday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,110,Main Campus,Lecture,"Upper Level|Pre-Med Required",Lab,100
            CHEM201,B,Prereq: CHEM101,Tuesday,1:00 PM,2:20 PM,80,780,,"Doe, Jane",JD,Arts Building,301,Main Campus,Lecture,Upper Level,,200
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Equal("Chemistry Department", result.Set!.SourceLabel);
        Assert.Equal("Fall 2026", result.SemesterName);
        Assert.Equal(2, result.Set.Sections.Count);

        var chem101 = result.Set.Sections.First(s => s.DisplayCourseCode == "CHEM101");
        Assert.Equal(3, chem101.Schedule.Count);
        Assert.Equal(2, chem101.DisplayInstructors!.Count);
        Assert.Equal("Main Campus", chem101.ImportedCampusName);
        Assert.Equal("Lecture", chem101.ImportedSectionTypeName);
        Assert.Equal(2, chem101.ImportedTagNames!.Count);
        Assert.Equal("100", chem101.Level);

        // Friday meeting has different room and meeting type
        var fridayMeeting = chem101.Schedule[2];
        Assert.Equal("110", fridayMeeting.ImportedRoomNumber);
        Assert.Equal("Lab", fridayMeeting.ImportedMeetingTypeName);

        var chem201 = result.Set.Sections.First(s => s.DisplayCourseCode == "CHEM201");
        Assert.Single(chem201.DisplayInstructors!);
        Assert.Equal("Prereq: CHEM101", chem201.Notes);
        Assert.Null(chem201.ImportedTagNames![0] == "Upper Level" ? null : "unexpected tag");
        Assert.Equal("200", chem201.Level);
    }

    [Fact]
    public void LegacyNineColumnFormat_EnrichedFieldsAllNull()
    {
        var csv = """
            CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
            CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        var section = result.Set!.Sections[0];
        Assert.Null(section.DisplayInstructors);
        Assert.Null(section.ImportedCampusName);
        Assert.Null(section.ImportedSectionTypeName);
        Assert.Null(section.ImportedTagNames);
        Assert.Null(section.Level);
        Assert.Null(section.Schedule[0].ImportedBuilding);
        Assert.Null(section.Schedule[0].ImportedRoomNumber);
        Assert.Null(section.Schedule[0].ImportedMeetingTypeName);
    }

    [Fact]
    public void EnrichedFormat_MeetingTypeEmptyForOneRow()
    {
        var csv = $"""
            {EnrichedHeader}
            CHEM201,B,,Tuesday,1:00 PM,2:20 PM,80,780,,"Doe, Jane",JD,Arts Building,301,Main Campus,Lecture,Upper Level,,200
            """;

        var result = Parse(csv);

        Assert.Null(result.FileError);
        Assert.Null(result.Set!.Sections[0].Schedule[0].ImportedMeetingTypeName);
    }
}

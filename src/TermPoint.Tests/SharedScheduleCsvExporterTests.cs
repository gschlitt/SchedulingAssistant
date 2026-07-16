using TermPoint.Models;
using TermPoint.Services;
using System.Text;
using Xunit;

namespace TermPoint.Tests;

public class SharedScheduleCsvExporterTests
{
    private readonly SharedScheduleCsvExporter _exporter = new();

    /// <summary>Empty lookups — no enriched data resolves. Used by legacy-style tests.</summary>
    private static readonly ExportLookups EmptyLookups = new(
        InstructorsById: new Dictionary<string, Instructor>(),
        RoomsById: new Dictionary<string, Room>(),
        CampusesById: new Dictionary<string, Campus>(),
        SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
        TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
        MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

    private string Export(IReadOnlyList<Section> sections, string sourceLabel = "Test Dept",
                          string semesterName = "Fall 2026",
                          Func<string, string>? courseCodeLookup = null,
                          ExportLookups? lookups = null)
    {
        courseCodeLookup ??= id => id;
        lookups ??= EmptyLookups;
        using var stream = new MemoryStream();
        var error = _exporter.Export(stream, sourceLabel, semesterName, sections, courseCodeLookup, lookups);
        Assert.Null(error);
        stream.Position = 0;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string[] Lines(string csv)
        => csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    // ── Existing tests (updated for 18-column format) ──────────────────────────

    [Fact]
    public void StandardSections_ProducesCorrectCsv()
    {
        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                Schedule = new()
                {
                    new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 },
                    new() { Day = 3, StartMinutes = 480, DurationMinutes = 50 },
                }
            }
        };

        var output = Export(sections);
        var lines = Lines(output);

        // Header comment (4-field format with semester)
        Assert.StartsWith("#TermPoint Schedule Overlay,Test Dept,Fall 2026,", lines[0]);
        // Column header (18 columns)
        Assert.Equal(
            "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency," +
            "Instructor,Initials,Building,RoomNumber,Campus,SectionType,Tags,MeetingType,Level",
            lines[1]);
        // Data rows — first 9 fields match, remaining 9 are empty (no lookups)
        Assert.StartsWith("CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,", lines[2]);
        Assert.StartsWith("CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,", lines[3]);
    }

    [Fact]
    public void NotesWithCommas_QuotedField()
    {
        var sections = new List<Section>
        {
            new()
            {
                CourseId = "BIO101", SectionCode = "A", Notes = "Bring goggles, gloves",
                Schedule = new() { new() { Day = 1, StartMinutes = 540, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections);

        Assert.Contains("\"Bring goggles, gloves\"", output);
    }

    [Fact]
    public void NotesWithNewlines_QuotedField()
    {
        var sections = new List<Section>
        {
            new()
            {
                CourseId = "BIO101", SectionCode = "A", Notes = "Line 1\nLine 2",
                Schedule = new() { new() { Day = 1, StartMinutes = 540, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections);

        Assert.Contains("\"Line 1\nLine 2\"", output);
    }

    [Fact]
    public void ZeroMeetingSection_EmitsOneRowWithBlankTimeFields()
    {
        var sections = new List<Section>
        {
            new() { CourseId = "HIST101", SectionCode = "B", Schedule = new() }
        };

        var output = Export(sections);
        var lines = Lines(output);

        Assert.Equal(3, lines.Length);
        // 18 fields: CourseCode,SectionCode, then 16 commas (all blank)
        Assert.Equal("HIST101,B,,,,,,,,,,,,,,,," , lines[2]);
    }

    [Fact]
    public void SourceLabelInHeaderComment()
    {
        var sections = new List<Section>
        {
            new() { CourseId = "X", SectionCode = "A", Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } } }
        };

        var output = Export(sections, "Chemistry Department");

        Assert.StartsWith("#TermPoint Schedule Overlay,Chemistry Department,Fall 2026,",
            output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0]);
    }

    [Fact]
    public void SemesterNameInHeaderComment()
    {
        var sections = new List<Section>
        {
            new() { CourseId = "X", SectionCode = "A", Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } } }
        };

        var output = Export(sections, semesterName: "Spring 2027");
        var headerLine = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0];

        Assert.StartsWith("#TermPoint Schedule Overlay,Test Dept,Spring 2027,", headerLine);
    }

    [Fact]
    public void RowOrdering_AlphabeticalThenDayThenStart()
    {
        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM201", SectionCode = "A",
                Schedule = new() { new() { Day = 3, StartMinutes = 600, DurationMinutes = 50 } }
            },
            new()
            {
                CourseId = "BIO101", SectionCode = "A",
                Schedule = new()
                {
                    new() { Day = 5, StartMinutes = 780, DurationMinutes = 50 },
                    new() { Day = 1, StartMinutes = 540, DurationMinutes = 50 },
                }
            }
        };

        var output = Export(sections);
        var dataLines = Lines(output).Skip(2).ToList();

        Assert.Contains("BIO101", dataLines[0]);
        Assert.Contains("Monday", dataLines[0]);
        Assert.Contains("BIO101", dataLines[1]);
        Assert.Contains("Friday", dataLines[1]);
        Assert.Contains("CHEM201", dataLines[2]);
    }

    [Fact]
    public void OutputStartsWithBom()
    {
        var sections = new List<Section>
        {
            new() { CourseId = "X", SectionCode = "A", Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } } }
        };

        using var stream = new MemoryStream();
        _exporter.Export(stream, "Test", "Fall 2026", sections, id => id, EmptyLookups);
        var bytes = stream.ToArray();

        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void FrequencyAnnotation_PassesThrough()
    {
        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM310", SectionCode = "A",
                Schedule = new() { new() { Day = 1, StartMinutes = 780, DurationMinutes = 50, Frequency = "odd" } }
            }
        };

        var output = Export(sections);

        Assert.Contains(",odd,", output);
    }

    // ── New enriched-column tests ──────────────────────────────────────────────

    [Fact]
    public void EnrichedColumns_InstructorFormatting()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>
            {
                ["i1"] = new() { Id = "i1", LastName = "Smith", FirstName = "John", Initials = "JRS" },
                ["i2"] = new() { Id = "i2", LastName = "Doe", FirstName = "Jane", Initials = "JD" },
            },
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>(),
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                InstructorAssignments = new()
                {
                    new() { InstructorId = "i1" },
                    new() { InstructorId = "i2" },
                },
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];

        // Instructor column: pipe-delimited "LastName, FirstName"
        Assert.Contains("\"Smith, John|Doe, Jane\"", dataLine);
        // Initials column: pipe-delimited
        Assert.Contains("JRS|JD", dataLine);
    }

    [Fact]
    public void EnrichedColumns_BuildingAndRoomPerMeeting()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>(),
            RoomsById: new Dictionary<string, Room>
            {
                ["r1"] = new() { Id = "r1", Building = "Science", RoomNumber = "204" },
                ["r2"] = new() { Id = "r2", Building = "Arts", RoomNumber = "101" },
            },
            CampusesById: new Dictionary<string, Campus>(),
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                Schedule = new()
                {
                    new() { Day = 1, StartMinutes = 480, DurationMinutes = 50, RoomId = "r1" },
                    new() { Day = 3, StartMinutes = 480, DurationMinutes = 50, RoomId = "r2" },
                }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLines = Lines(output).Skip(2).ToList();

        // Monday meeting → Science / 204
        Assert.Contains(",Science,204,", dataLines[0]);
        // Wednesday meeting → Arts / 101
        Assert.Contains(",Arts,101,", dataLines[1]);
    }

    [Fact]
    public void EnrichedColumns_CampusSectionTypeLevel()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>(),
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>
            {
                ["c1"] = new() { Id = "c1", Name = "Main Campus" }
            },
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["st1"] = new() { Id = "st1", Name = "Lecture" }
            },
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                CampusId = "c1", SectionTypeId = "st1", Level = "100",
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];

        Assert.Contains(",Main Campus,Lecture,", dataLine);
        Assert.EndsWith(",100", dataLine);
    }

    [Fact]
    public void EnrichedColumns_PipeDelimitedTags()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>(),
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>(),
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["t1"] = new() { Id = "t1", Name = "Upper Level" },
                ["t2"] = new() { Id = "t2", Name = "Pre-Med Required" },
            },
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                TagIds = new() { "t1", "t2" },
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];

        Assert.Contains("Upper Level|Pre-Med Required", dataLine);
    }

    [Fact]
    public void EnrichedColumns_MeetingType()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>(),
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>(),
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["mt1"] = new() { Id = "mt1", Name = "Lab" }
            });

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50, MeetingTypeId = "mt1" } }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];

        Assert.Contains(",Lab,", dataLine);
    }

    [Fact]
    public void EnrichedColumns_MissingData_NullIdsProduceEmpty()
    {
        // Section with no instructor assignments, no campus, no section type, no tags, no level,
        // and meetings with no room and no meeting type → all enriched columns are empty
        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections);
        var dataLine = Lines(output)[2];

        // 18 fields total. After the 9 original fields, all enriched fields should be empty.
        // Count commas: 17 commas for 18 fields
        var fields = ParseCsvLine(dataLine);
        Assert.Equal(18, fields.Count);
        // Fields 9–17 (0-indexed) should all be empty
        for (int i = 9; i < 18; i++)
            Assert.Equal("", fields[i]);
    }

    [Fact]
    public void EnrichedColumns_DeletedInstructorSkipped()
    {
        // Instructor i2 is assigned but not in the lookup (deleted)
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>
            {
                ["i1"] = new() { Id = "i1", LastName = "Smith", FirstName = "John", Initials = "JRS" },
            },
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>(),
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                InstructorAssignments = new()
                {
                    new() { InstructorId = "i1" },
                    new() { InstructorId = "i2" }, // not in lookup
                },
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];

        // Only i1 should appear — no pipe separator
        Assert.Contains("\"Smith, John\"", dataLine);
        Assert.DoesNotContain("|", dataLine);
        Assert.Contains(",JRS,", dataLine);
    }

    [Fact]
    public void EnrichedColumns_DeletedTagSkipped()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>(),
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>(),
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["t1"] = new() { Id = "t1", Name = "Active Tag" },
                // t2 is not in the lookup (deleted)
            },
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                TagIds = new() { "t1", "t2" },
                Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];

        Assert.Contains("Active Tag", dataLine);
        Assert.DoesNotContain("|", dataLine);
    }

    [Fact]
    public void UnscheduledSection_HasEnrichedPerSectionColumns()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>
            {
                ["i1"] = new() { Id = "i1", LastName = "Smith", FirstName = "John", Initials = "JRS" },
            },
            RoomsById: new Dictionary<string, Room>(),
            CampusesById: new Dictionary<string, Campus>
            {
                ["c1"] = new() { Id = "c1", Name = "Main Campus" }
            },
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>(),
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>());

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                InstructorAssignments = new() { new() { InstructorId = "i1" } },
                CampusId = "c1", Level = "100",
                Schedule = new() // no meetings
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLine = Lines(output)[2];
        var fields = ParseCsvLine(dataLine);

        Assert.Equal(18, fields.Count);
        // Time fields (3–8) are blank
        for (int i = 3; i <= 8; i++)
            Assert.Equal("", fields[i]);
        // Per-section enriched fields are populated
        Assert.Equal("Smith, John", fields[9]);  // Instructor
        Assert.Equal("JRS", fields[10]);          // Initials
        Assert.Equal("", fields[11]);             // Building (no meeting)
        Assert.Equal("", fields[12]);             // RoomNumber (no meeting)
        Assert.Equal("Main Campus", fields[13]);  // Campus
        Assert.Equal("", fields[14]);             // SectionType
        Assert.Equal("", fields[15]);             // Tags
        Assert.Equal("", fields[16]);             // MeetingType (no meeting)
        Assert.Equal("100", fields[17]);          // Level
    }

    [Fact]
    public void EnrichedColumns_PerSectionFieldsRepeatAcrossMeetings()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>
            {
                ["i1"] = new() { Id = "i1", LastName = "Smith", FirstName = "John", Initials = "JRS" },
            },
            RoomsById: new Dictionary<string, Room>
            {
                ["r1"] = new() { Id = "r1", Building = "Science", RoomNumber = "204" },
                ["r2"] = new() { Id = "r2", Building = "Arts", RoomNumber = "101" },
            },
            CampusesById: new Dictionary<string, Campus>
            {
                ["c1"] = new() { Id = "c1", Name = "Main Campus" }
            },
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["st1"] = new() { Id = "st1", Name = "Lecture" }
            },
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["mt1"] = new() { Id = "mt1", Name = "In Person" },
                ["mt2"] = new() { Id = "mt2", Name = "Lab" },
            });

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                InstructorAssignments = new() { new() { InstructorId = "i1" } },
                CampusId = "c1", SectionTypeId = "st1", Level = "100",
                Schedule = new()
                {
                    new() { Day = 1, StartMinutes = 480, DurationMinutes = 50, RoomId = "r1", MeetingTypeId = "mt1" },
                    new() { Day = 3, StartMinutes = 600, DurationMinutes = 120, RoomId = "r2", MeetingTypeId = "mt2" },
                }
            }
        };

        var output = Export(sections, lookups: lookups);
        var dataLines = Lines(output).Skip(2).ToList();
        var fields0 = ParseCsvLine(dataLines[0]);
        var fields1 = ParseCsvLine(dataLines[1]);

        // Per-section fields identical on both rows
        Assert.Equal("Smith, John", fields0[9]);
        Assert.Equal("Smith, John", fields1[9]);
        Assert.Equal("JRS", fields0[10]);
        Assert.Equal("JRS", fields1[10]);
        Assert.Equal("Main Campus", fields0[13]);
        Assert.Equal("Main Campus", fields1[13]);
        Assert.Equal("Lecture", fields0[14]);
        Assert.Equal("Lecture", fields1[14]);
        Assert.Equal("100", fields0[17]);
        Assert.Equal("100", fields1[17]);

        // Per-meeting fields differ
        Assert.Equal("Science", fields0[11]);
        Assert.Equal("Arts", fields1[11]);
        Assert.Equal("204", fields0[12]);
        Assert.Equal("101", fields1[12]);
        Assert.Equal("In Person", fields0[16]);
        Assert.Equal("Lab", fields1[16]);
    }

    // ── Round-trip test ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ExportThenParse_PreservesEnrichedData()
    {
        var lookups = new ExportLookups(
            InstructorsById: new Dictionary<string, Instructor>
            {
                ["i1"] = new() { Id = "i1", LastName = "Smith", FirstName = "John", Initials = "JRS" },
                ["i2"] = new() { Id = "i2", LastName = "Doe", FirstName = "Jane", Initials = "JD" },
            },
            RoomsById: new Dictionary<string, Room>
            {
                ["r1"] = new() { Id = "r1", Building = "Science", RoomNumber = "204" },
            },
            CampusesById: new Dictionary<string, Campus>
            {
                ["c1"] = new() { Id = "c1", Name = "Main Campus" }
            },
            SectionTypesById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["st1"] = new() { Id = "st1", Name = "Lecture" }
            },
            TagsById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["t1"] = new() { Id = "t1", Name = "Upper Level" },
                ["t2"] = new() { Id = "t2", Name = "Pre-Med" },
            },
            MeetingTypesById: new Dictionary<string, SchedulingEnvironmentValue>
            {
                ["mt1"] = new() { Id = "mt1", Name = "Lab" }
            });

        var sections = new List<Section>
        {
            new()
            {
                CourseId = "CHEM101", SectionCode = "A",
                InstructorAssignments = new()
                {
                    new() { InstructorId = "i1" },
                    new() { InstructorId = "i2" },
                },
                CampusId = "c1", SectionTypeId = "st1",
                TagIds = new() { "t1", "t2" },
                Level = "300",
                Schedule = new()
                {
                    new() { Day = 1, StartMinutes = 480, DurationMinutes = 50, RoomId = "r1", MeetingTypeId = "mt1" },
                    new() { Day = 3, StartMinutes = 480, DurationMinutes = 50, RoomId = "r1", MeetingTypeId = "mt1" },
                }
            }
        };

        // Export
        using var exportStream = new MemoryStream();
        var error = _exporter.Export(exportStream, "Chemistry", "Fall 2026", sections, id => id, lookups);
        Assert.Null(error);

        // Parse
        exportStream.Position = 0;
        var parser = new SharedScheduleCsvParser();
        var result = parser.Parse(exportStream, "fallback");

        Assert.Null(result.FileError);
        Assert.NotNull(result.Set);
        Assert.Single(result.Set!.Sections);

        var parsed = result.Set.Sections[0];

        // Source label from header
        Assert.Equal("Chemistry", result.Set.SourceLabel);

        // Course code and section code
        Assert.Equal("CHEM101", parsed.DisplayCourseCode);
        Assert.Equal("A", parsed.SectionCode);

        // Instructors
        Assert.NotNull(parsed.DisplayInstructors);
        Assert.Equal(2, parsed.DisplayInstructors!.Count);
        Assert.Equal("Smith, John", parsed.DisplayInstructors[0].Name);
        Assert.Equal("JRS", parsed.DisplayInstructors[0].Initials);
        Assert.Equal("Doe, Jane", parsed.DisplayInstructors[1].Name);
        Assert.Equal("JD", parsed.DisplayInstructors[1].Initials);

        // Campus, section type, tags, level
        Assert.Equal("Main Campus", parsed.ImportedCampusName);
        Assert.Equal("Lecture", parsed.ImportedSectionTypeName);
        Assert.NotNull(parsed.ImportedTagNames);
        Assert.Equal(new[] { "Upper Level", "Pre-Med" }, parsed.ImportedTagNames);
        Assert.Equal("300", parsed.Level);

        // Meetings
        Assert.Equal(2, parsed.Schedule.Count);
        Assert.Equal("Science", parsed.Schedule[0].ImportedBuilding);
        Assert.Equal("204", parsed.Schedule[0].ImportedRoomNumber);
        Assert.Equal("Lab", parsed.Schedule[0].ImportedMeetingTypeName);
    }

    // ── CSV parsing helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses a single CSV line respecting RFC-4180 quoting.
    /// Used by tests to inspect individual fields in the output.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}

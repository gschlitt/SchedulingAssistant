using TermPoint.Models;
using TermPoint.Services;
using System.Text;
using Xunit;

namespace TermPoint.Tests;

public class SharedScheduleCsvExporterTests
{
    private readonly SharedScheduleCsvExporter _exporter = new();

    private string Export(IReadOnlyList<Section> sections, string sourceLabel = "Test Dept",
                          Func<string, string>? courseCodeLookup = null)
    {
        courseCodeLookup ??= id => id;
        using var stream = new MemoryStream();
        var error = _exporter.Export(stream, sourceLabel, sections, courseCodeLookup);
        Assert.Null(error);
        stream.Position = 0;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

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
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Header comment
        Assert.StartsWith("#TermPoint Schedule Overlay,Test Dept,", lines[0]);
        // Column header
        Assert.Equal("CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency", lines[1]);
        // Data rows
        Assert.Contains("CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,", lines[2]);
        Assert.Contains("CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,", lines[3]);
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
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Should have header comment, column header, and one data row
        Assert.Equal(3, lines.Length);
        // 9 fields, all blank after SectionCode = 6 trailing commas
        Assert.Equal("HIST101,B,,,,,,,", lines[2]);
    }

    [Fact]
    public void SourceLabelInHeaderComment()
    {
        var sections = new List<Section>
        {
            new() { CourseId = "X", SectionCode = "A", Schedule = new() { new() { Day = 1, StartMinutes = 480, DurationMinutes = 50 } } }
        };

        var output = Export(sections, "Chemistry Department");

        Assert.StartsWith("#TermPoint Schedule Overlay,Chemistry Department,", output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0]);
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
        var dataLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(2).ToList();

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
        _exporter.Export(stream, "Test", sections, id => id);
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

        Assert.Contains(",odd", output);
    }
}

using System.Text;
using ExcelDataReader;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Guards the shipped section import template workbook
/// (<c>Assets/Templates/TermPointSectionsTemplate.xlsx</c>, made by <c>tools/section-import/make_template.py</c>):
/// its header row matches <see cref="SectionImportColumns"/>, its sample rows import cleanly and show every
/// pattern, its code and time columns are Text, and it carries the Instructions sheet.
/// The workbook is copied beside the test assembly by a linked item in the test project.
/// </summary>
public class TemplateWorkbookTests
{
    /// <summary>The workbook's file name; the test project copies it into <c>TestData</c>.</summary>
    private const string FileName = "TermPointSectionsTemplate.xlsx";

    /// <summary>The columns whose cells the template formats as Text, so Excel keeps leading zeros.</summary>
    private static readonly string[] TextColumns = [C.CourseNumber, C.SectionCode, C.Begin, C.End];

    /// <summary>The only warning kinds the sample may produce: an empty test database has no legal start times.</summary>
    private static readonly HashSet<string> ExpectedWarningKinds = [ImportWarningKinds.NoLegalStartTimes];

    /// <summary>ExcelDataReader looks up code page 1252 when it is created; register the provider once.</summary>
    static TemplateWorkbookTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Reads the shipped workbook's bytes from the test output folder.</summary>
    /// <returns>The .xlsx file's bytes.</returns>
    private static byte[] ShippedBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", FileName));

    /// <summary>Reads the shipped workbook through the real reader, failing the test with its message if it can't.</summary>
    /// <returns>The sheet the import would see.</returns>
    private static ImportSheet ReadSheet()
    {
        var result = new ImportFileReader().Read(ShippedBytes(), FileName);
        Assert.True(result.IsSuccess, result.Problem);
        return result.Sheet!;
    }

    /// <summary>
    /// Reads every row of the named worksheet with ExcelDataReader, each cell as the raw value
    /// (string, double, DateTime or null) the library reports, so cell types can be checked.
    /// </summary>
    /// <param name="sheetName">The worksheet's tab name.</param>
    /// <returns>The rows in order, row 1 first.</returns>
    private static List<object?[]> ReadRawRows(string sheetName)
    {
        using var stream = new MemoryStream(ShippedBytes());
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        do
        {
            if (reader.Name == sheetName)
            {
                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var cells = new object?[reader.FieldCount];
                    for (int i = 0; i < cells.Length; i++)
                        cells[i] = reader.GetValue(i);
                    rows.Add(cells);
                }
                return rows;
            }
        }
        while (reader.NextResult());

        throw new InvalidOperationException($"The template has no sheet named {sheetName}.");
    }

    /// <summary>The Sections sheet's header row has exactly the template columns, in template order, and nothing more.</summary>
    [Fact]
    public void SectionsSheet_HeaderRow_EqualsSectionImportColumnsInOrder()
    {
        var header = ReadRawRows("Sections")[0];

        Assert.Equal(SectionImportColumns.All.Count, header.Length);
        Assert.Equal(
            SectionImportColumns.All.Select(c => c.Name),
            header.Select(v => v as string));
    }

    /// <summary>The first sheet is the one the importer reads, and it reads without a problem.</summary>
    [Fact]
    public void ImportFileReader_ReadsTheShippedWorkbook()
    {
        using var stream = new MemoryStream(ShippedBytes());
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        Assert.Equal("Sections", reader.Name);

        var sheet = ReadSheet();
        Assert.NotEmpty(sheet.Rows);
        Assert.Equal(2, sheet.Rows[0].RowNumber);
    }

    /// <summary>The sample rows plan with no problems, and every warning is of a kind expected on an empty database.</summary>
    [Fact]
    public void SampleRows_Plan_WithNoProblems()
    {
        using var rig = new ImportRig();

        var report = rig.Plan(ReadSheet());

        Assert.True(report.Problems.Count == 0,
            "The sample rows have problems:" + Environment.NewLine +
            string.Join(Environment.NewLine, report.Problems.Select(p => $"Row {p.Row}, {p.Column}: {p.Message}")));
        Assert.Empty(report.SkippedSections);
        Assert.All(report.Warnings, w => Assert.Contains(w.Kind, ExpectedWarningKinds));
        Assert.NotEmpty(report.Plan.NewSections);
    }

    /// <summary>The sample shows each pattern: two instructors, two rows for one section, odd weeks, and no meetings.</summary>
    [Fact]
    public void SampleRows_ShowEveryPattern()
    {
        using var rig = new ImportRig();

        var sections = rig.Plan(ReadSheet()).Plan.NewSections;

        Assert.Contains(sections, s => s.Instructors.Count == 2);
        Assert.Contains(sections, s => s.RowNumbers.Count == 2);
        Assert.Contains(sections, s => s.Meetings.Any(m => m.Frequency == "odd"));
        Assert.Contains(sections, s => s.Meetings.Count == 0);

        // The two-row section really has two time patterns.
        var twoRows = sections.First(s => s.RowNumbers.Count == 2);
        Assert.True(twoRows.Meetings.Select(m => (m.StartMinutes, m.DurationMinutes)).Distinct().Count() >= 2);

        // Some section carries a capacity, a campus, two tags, a meeting type and a room.
        Assert.Contains(sections, s => s.RawCapacity is not null);
        Assert.Contains(sections, s => s.Campus is not null);
        Assert.Contains(sections, s => s.Tags.Count == 2);
        Assert.Contains(sections, s => s.Meetings.Any(m => m.MeetingType is not null && m.Room is not null));
    }

    /// <summary>
    /// Course Number, Section Code, Begin and End hold text in every sample row (never a number), and
    /// their leading zeros survive; the cells below the sample carry no number either.
    /// </summary>
    [Fact]
    public void SampleRows_CodeAndTimeCells_AreText()
    {
        var rows = ReadRawRows("Sections");
        var header = rows[0].Select(v => (string?)v).ToList();
        var columnIndexes = TextColumns.Select(c => header.IndexOf(c)).ToList();
        Assert.DoesNotContain(-1, columnIndexes);

        foreach (var row in rows.Skip(1))
            foreach (int col in columnIndexes)
                Assert.True(col >= row.Length || row[col] is null or string,
                    $"A {header[col]} cell holds {row[col]?.GetType().Name}, not text.");

        // The online sample section's code is the proof Excel did not strip a leading zero.
        int codeCol = header.IndexOf(C.SectionCode);
        Assert.Contains(rows.Skip(1), r => r[codeCol] is "001");
        int beginCol = header.IndexOf(C.Begin);
        Assert.Contains(rows.Skip(1), r => r[beginCol] is "0900");
    }

    /// <summary>The second sheet is Instructions, and it has text in it.</summary>
    [Fact]
    public void InstructionsSheet_Exists_AndHasText()
    {
        var rows = ReadRawRows("Instructions");

        var text = rows
            .Select(r => r.Length > 0 ? r[0] as string : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        Assert.NotEmpty(text);
        Assert.Contains(text, t => t!.Contains("Import Sections"));
    }

    /// <summary>The Sections sheet comes first and the Instructions sheet second.</summary>
    [Fact]
    public void Workbook_HasSectionsThenInstructions()
    {
        using var stream = new MemoryStream(ShippedBytes());
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);

        var names = new List<string>();
        do { names.Add(reader.Name); } while (reader.NextResult());

        Assert.Equal(["Sections", "Instructions"], names);
    }
}

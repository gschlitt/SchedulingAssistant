using System.Globalization;
using System.Text;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ImportFileReader"/>, the section import file reader: header matching (D9), blank
/// rows, the file limits (E4), the cell-to-text rules, and the format-specific behaviour of CSV and
/// <c>.xlsx</c> (workbooks are built in memory by <see cref="XlsxTestWorkbook"/>).
/// </summary>
public class ImportFileReaderTests
{
    /// <summary>The two file formats the reader accepts; format-neutral tests run once for each.</summary>
    public enum Format { Csv, Xlsx }

    /// <summary>The template's column names in template order.</summary>
    private static readonly string[] Headers = SectionImportColumns.All.Select(c => c.Name).ToArray();

    private readonly ImportFileReader _reader = new();

    // ── Building test files ───────────────────────────────────────────────────

    /// <summary>
    /// A data row with a value for every template column, in template order. Only the five required
    /// cells and the title are filled by default; <paramref name="extra"/> sets (or overrides) others.
    /// </summary>
    /// <param name="subject">Subject cell.</param>
    /// <param name="number">Course Number cell.</param>
    /// <param name="code">Section Code cell.</param>
    /// <param name="extra">Further (column name, value) pairs.</param>
    private static object?[] MakeRowFor(string subject, string number, string code,
        params (string Column, object? Value)[] extra)
    {
        var cells = new object?[Headers.Length];
        void Set(string column, object? value) => cells[Array.IndexOf(Headers, column)] = value;

        Set(SectionImportColumns.AcademicYear, "2026-2027");
        Set(SectionImportColumns.Term, "Fall");
        Set(SectionImportColumns.Subject, subject);
        Set(SectionImportColumns.CourseNumber, number);
        Set(SectionImportColumns.CourseTitle, "Intro");
        Set(SectionImportColumns.SectionCode, code);
        foreach (var (column, value) in extra)
            Set(column, value);
        return cells;
    }

    /// <summary>A data row for CHEM 101 section A with the given extra (column name, value) pairs; see <see cref="MakeRowFor"/>.</summary>
    /// <param name="extra">Further (column name, value) pairs.</param>
    private static object?[] MakeRow(params (string Column, object? Value)[] extra) =>
        MakeRowFor("CHEM", "101", "A", extra);

    /// <summary>Quotes a CSV field when it needs it; null becomes empty.</summary>
    private static string CsvField(object? value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    /// <summary>Joins cells into one CSV record.</summary>
    private static string CsvLine(IEnumerable<object?> cells) => string.Join(",", cells.Select(CsvField));

    /// <summary>UTF-8 bytes (no BOM) of the lines, CRLF separated.</summary>
    private static byte[] CsvBytes(params string[] lines) =>
        new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n");

    /// <summary>
    /// Builds a file in the given format: the header cells (null or empty is a blank header cell) then the rows,
    /// rows on consecutive row numbers starting at 2. An all-null row is a blank row.
    /// </summary>
    /// <returns>The bytes and a matching file name.</returns>
    private static (byte[] Bytes, string Name) Build(Format format, IReadOnlyList<string?> headers, IReadOnlyList<object?[]> rows)
    {
        if (format == Format.Csv)
        {
            var lines = new List<string> { CsvLine(headers) };
            lines.AddRange(rows.Select(CsvLine));
            return (CsvBytes(lines.ToArray()), "sections.csv");
        }

        var bytes = new XlsxTestWorkbook().Sheet("Sections", sheet =>
        {
            sheet.Row(headers.Cast<object?>().ToArray());
            foreach (var row in rows)
                sheet.Row(row);
        }).ToBytes();
        return (bytes, "sections.xlsx");
    }

    /// <summary>Reads a file built by <see cref="Build"/> with the standard headers and the given rows.</summary>
    private ImportReadResult ReadBuilt(Format format, params object?[][] rows)
    {
        var (bytes, name) = Build(format, Headers, rows);
        return _reader.Read(bytes, name);
    }

    /// <summary>Asserts the read succeeded and returns the sheet.</summary>
    private static ImportSheet Ok(ImportReadResult result)
    {
        Assert.True(result.IsSuccess, "Expected success but got: " + result.Problem);
        Assert.Null(result.Problem);
        return Assert.IsType<ImportSheet>(result.Sheet);
    }

    /// <summary>Asserts the read failed and returns the problem message.</summary>
    private static string Problem(ImportReadResult result)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Sheet);
        return Assert.IsType<string>(result.Problem);
    }

    // ── Format-neutral behaviour (CSV and .xlsx) ──────────────────────────────

    /// <summary>A normal file reads into rows with every template column present.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void NormalFile_ReadsRowsWithEveryColumn(Format format)
    {
        var sheet = Ok(ReadBuilt(format,
            MakeRowFor("CHEM", "101", "A", (SectionImportColumns.MeetingDays, "M,W"), (SectionImportColumns.Begin, "1000")),
            MakeRowFor("CHEM", "101", "B")));

        Assert.Equal(new[] { 2, 3 }, sheet.Rows.Select(r => r.RowNumber));
        var first = sheet.Rows[0];
        Assert.Equal(Headers.OrderBy(h => h), first.Cells.Keys.OrderBy(k => k));
        Assert.Equal("CHEM", first.Cells[SectionImportColumns.Subject]);
        Assert.Equal("M,W", first.Cells[SectionImportColumns.MeetingDays]);
        Assert.Equal("1000", first.Cells[SectionImportColumns.Begin]);
        Assert.Equal("", first.Cells[SectionImportColumns.Capacity]);   // empty cell -> empty string, not missing
        Assert.Equal("B", sheet.Rows[1].Cells[SectionImportColumns.SectionCode]);
    }

    /// <summary>Cell lookups ignore case and spacing.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void Cells_AreLookedUpIgnoringCaseAndSpacing(Format format)
    {
        var sheet = Ok(ReadBuilt(format, MakeRow()));
        Assert.Equal("CHEM", sheet.Rows[0].Cells["subject"]);
        Assert.Equal("101", sheet.Rows[0].Cells["course   number"]);
    }

    /// <summary>The sheet carries the file's display name, with any folder path removed.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void FileName_IsTheDisplayNameWithoutPath(Format format)
    {
        var (bytes, name) = Build(format, Headers, new[] { MakeRow() });
        var sheet = Ok(_reader.Read(bytes, Path.Combine("some", "folder", name)));
        Assert.Equal(name, sheet.FileName);
    }

    /// <summary>Columns may be in any order; the cells follow their headers.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void Headers_InAnotherOrder_AreMatchedByName(Format format)
    {
        var row = MakeRowFor("BIOL", "210", "C", (SectionImportColumns.Building, "SCI"));
        var (bytes, name) = Build(format, Headers.Reverse().ToArray(), new[] { row.Reverse().ToArray() });

        var cells = Ok(_reader.Read(bytes, name)).Rows.Single().Cells;

        Assert.Equal("BIOL", cells[SectionImportColumns.Subject]);
        Assert.Equal("210", cells[SectionImportColumns.CourseNumber]);
        Assert.Equal("C", cells[SectionImportColumns.SectionCode]);
        Assert.Equal("SCI", cells[SectionImportColumns.Building]);
    }

    /// <summary>Header case and spacing do not matter.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void Headers_IgnoreCaseAndSpacing(Format format)
    {
        var headers = Headers.ToArray();
        headers[0] = "academic  year";
        headers[1] = "  TERM ";
        headers[3] = "COURSE NUMBER";
        var (bytes, name) = Build(format, headers, new[] { MakeRow() });

        var cells = Ok(_reader.Read(bytes, name)).Rows.Single().Cells;

        Assert.Equal("2026-2027", cells[SectionImportColumns.AcademicYear]);
        Assert.Equal("Fall", cells[SectionImportColumns.Term]);
        Assert.Equal("101", cells[SectionImportColumns.CourseNumber]);
    }

    /// <summary>
    /// Columns that are not in the template (named, or with a blank header) are ignored, and a row that
    /// holds only a note in such a column counts as blank.
    /// </summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void ExtraAndBlankHeaderColumns_AreIgnored(Format format)
    {
        var headers = new List<string?>(Headers) { "My notes", "" };
        var withNotes = MakeRow().Concat(new object?[] { "call Pat", "scratch" }).ToArray();
        var noteOnly = new object?[Headers.Length].Concat(new object?[] { "just a note", "x" }).ToArray();
        var (bytes, name) = Build(format, headers, new[] { withNotes, noteOnly });

        var sheet = Ok(_reader.Read(bytes, name));

        var row = Assert.Single(sheet.Rows);
        Assert.Equal(Headers.Length, row.Cells.Count);
        Assert.DoesNotContain("My notes", row.Cells.Keys);
    }

    /// <summary>A template column that appears twice is a problem.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void DuplicateHeader_IsReported(Format format)
    {
        var headers = new List<string?>(Headers) { "tags" };   // Tags again, different case
        var (bytes, name) = Build(format, headers, new[] { MakeRow().Append(null).ToArray() });

        Assert.Equal("The Tags column appears more than once.", Problem(_reader.Read(bytes, name)));
    }

    /// <summary>One missing column gives the singular message.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void OneMissingColumn_IsNamed(Format format)
    {
        int tags = Array.IndexOf(Headers, SectionImportColumns.Tags);
        var headers = Headers.Where(h => h != SectionImportColumns.Tags).ToArray();
        var row = MakeRow().Where((_, i) => i != tags).ToArray();
        var (bytes, name) = Build(format, headers, new[] { row });

        Assert.Equal("The Tags column is missing.", Problem(_reader.Read(bytes, name)));
    }

    /// <summary>Several missing columns are one message, in template order.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void SeveralMissingColumns_AreOneMessageInTemplateOrder(Format format)
    {
        var dropped = new[] { SectionImportColumns.Campus, SectionImportColumns.Tags };   // given out of order
        var keep = Headers.Select((h, i) => (h, i)).Where(p => !dropped.Contains(p.h)).ToArray();
        var row = MakeRow();
        var (bytes, name) = Build(format, keep.Select(p => p.h).ToArray(), new[] { keep.Select(p => row[p.i]).ToArray() });

        Assert.Equal("These columns are missing: Tags, Campus.", Problem(_reader.Read(bytes, name)));
    }

    /// <summary>
    /// Blank rows (every template cell empty or whitespace) are skipped silently, and the row numbers of the
    /// rows after them are unaffected.
    /// </summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void BlankRows_AreSkippedAndLaterRowNumbersUnchanged(Format format)
    {
        var whitespaceOnly = MakeRow().Select(_ => (object?)"  ").ToArray();
        var sheet = Ok(ReadBuilt(format,
            MakeRowFor("CHEM", "101", "A"),
            new object?[Headers.Length],
            whitespaceOnly,
            MakeRowFor("CHEM", "101", "B")));

        Assert.Equal(new[] { 2, 5 }, sheet.Rows.Select(r => r.RowNumber));
    }

    /// <summary>A valid header with no data rows is a valid sheet with no rows.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void HeaderOnly_IsAValidSheetWithNoRows(Format format)
    {
        var sheet = Ok(ReadBuilt(format));
        Assert.Empty(sheet.Rows);
    }

    /// <summary>Exactly 5,000 data rows is allowed.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void ExactlyTheRowLimit_IsAllowed(Format format)
    {
        var rows = Enumerable.Range(0, ImportFileReader.MaxDataRows).Select(i => MakeRowFor("CHEM", "101", "S" + i)).ToArray();
        Assert.Equal(ImportFileReader.MaxDataRows, Ok(ReadBuilt(format, rows)).Rows.Count);
    }

    /// <summary>More than 5,000 data rows gives the limit message.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void OverTheRowLimit_GivesTheLimitMessage(Format format)
    {
        var rows = Enumerable.Range(0, ImportFileReader.MaxDataRows + 1).Select(i => MakeRowFor("CHEM", "101", "S" + i)).ToArray();
        Assert.Equal(ImportFileReader.TooLargeMessage, Problem(ReadBuilt(format, rows)));
    }

    /// <summary>Blank rows do not count toward the row limit.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void BlankRows_DoNotCountTowardTheRowLimit(Format format)
    {
        var rows = Enumerable.Range(0, ImportFileReader.MaxDataRows).Select(i => MakeRowFor("CHEM", "101", "S" + i))
            .Concat(Enumerable.Range(0, 50).Select(_ => new object?[Headers.Length]))
            .ToArray();
        Assert.Equal(ImportFileReader.MaxDataRows, Ok(ReadBuilt(format, rows)).Rows.Count);
    }

    /// <summary>Content over 10 MB is refused before anything is parsed.</summary>
    [Theory]
    [InlineData("big.csv")]
    [InlineData("big.xlsx")]
    public void OverTheSizeLimit_GivesTheLimitMessage(string fileName)
    {
        var content = new byte[ImportFileReader.MaxFileBytes + 1];
        Assert.Equal(ImportFileReader.TooLargeMessage, Problem(_reader.Read(content, fileName)));
    }

    /// <summary>Only .xlsx and .csv are accepted (case-insensitive); anything else, including .xls, gets the type message.</summary>
    [Theory]
    [InlineData("old.xls")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    [InlineData("sections.xlsm")]
    public void UnsupportedExtension_IsRefused(string fileName) =>
        Assert.Equal("TermPoint can import Excel workbooks (.xlsx) and CSV files (.csv).",
            Problem(_reader.Read(new byte[] { 1, 2, 3 }, fileName)));

    /// <summary>The extension check ignores case.</summary>
    [Theory]
    [InlineData("SECTIONS.CSV")]
    [InlineData("Sections.Csv")]
    public void Extension_IsCaseInsensitive(string fileName) =>
        Ok(_reader.Read(CsvBytes(CsvLine(Headers)), fileName));

    // ── CSV only ──────────────────────────────────────────────────────────────

    /// <summary>A CSV with a UTF-8 byte-order mark reads normally (the mark is not part of the first header).</summary>
    [Fact]
    public void Csv_Utf8Bom_IsHonoured()
    {
        var body = CsvBytes(CsvLine(Headers), CsvLine(MakeRow()));
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();

        var sheet = Ok(_reader.Read(bytes, "bom.csv"));

        Assert.Equal("2026-2027", sheet.Rows.Single().Cells[SectionImportColumns.AcademicYear]);
    }

    /// <summary>Bytes that are not valid UTF-8 are read as Latin-1, so an accented name stays readable.</summary>
    [Fact]
    public void Csv_Latin1Bytes_AreDecodedAsLatin1()
    {
        var text = string.Join("\r\n", CsvLine(Headers),
            CsvLine(MakeRow((SectionImportColumns.Instructors, "Côté, Jean")))) + "\r\n";
        var bytes = Encoding.Latin1.GetBytes(text);
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(bytes));   // really not UTF-8

        var sheet = Ok(_reader.Read(bytes, "latin1.csv"));

        Assert.Equal("Côté, Jean", sheet.Rows.Single().Cells[SectionImportColumns.Instructors]);
    }

    /// <summary>Valid UTF-8 with accents is read as UTF-8, not Latin-1.</summary>
    [Fact]
    public void Csv_Utf8Accents_AreKept()
    {
        var bytes = CsvBytes(CsvLine(Headers), CsvLine(MakeRow((SectionImportColumns.Instructors, "Côté, Jean"))));
        Assert.Equal("Côté, Jean", Ok(_reader.Read(bytes, "utf8.csv")).Rows.Single().Cells[SectionImportColumns.Instructors]);
    }

    /// <summary>
    /// Quoted fields keep their commas and quotes, and a quoted line break stays inside the field; the next
    /// row's number counts the physical lines used, and the row's own number is the line it starts on.
    /// </summary>
    [Fact]
    public void Csv_QuotedFields_KeepCommasAndLineBreaks_AndRowNumbersAreStartLines()
    {
        var bytes = CsvBytes(
            CsvLine(Headers),                                                                   // line 1
            CsvLine(MakeRowFor("CHEM", "101", "A", (SectionImportColumns.Instructors, "Smith, John; Lee, Ann"))),   // line 2
            CsvLine(MakeRowFor("CHEM", "101", "B", (SectionImportColumns.CourseTitle, "Line one\nline \"two\""))),  // lines 3-4
            CsvLine(MakeRowFor("CHEM", "101", "C")));                                                       // line 5

        var sheet = Ok(_reader.Read(bytes, "quoted.csv"));

        Assert.Equal(new[] { 2, 3, 5 }, sheet.Rows.Select(r => r.RowNumber));
        Assert.Equal("Smith, John; Lee, Ann", sheet.Rows[0].Cells[SectionImportColumns.Instructors]);
        Assert.Equal("Line one\nline \"two\"", sheet.Rows[1].Cells[SectionImportColumns.CourseTitle]);
        Assert.Equal("C", sheet.Rows[2].Cells[SectionImportColumns.SectionCode]);
    }

    /// <summary>An unclosed quotation mark is one problem naming the line where that record starts.</summary>
    [Fact]
    public void Csv_UnclosedQuote_ReportsTheStartLine()
    {
        var bytes = CsvBytes(
            CsvLine(Headers),
            CsvLine(MakeRowFor("CHEM", "101", "A")),
            "2026-2027,Fall,CHEM,\"102,Intro,B");   // line 3: the quote opens and never closes

        Assert.Equal(
            "Line 3 has a quotation mark that is never closed, so TermPoint can't tell where that row ends.",
            Problem(_reader.Read(bytes, "unclosed.csv")));
    }

    /// <summary>A truly empty line is a blank row: skipped, and later line numbers are unchanged.</summary>
    [Fact]
    public void Csv_EmptyLine_IsSkipped()
    {
        var bytes = CsvBytes(CsvLine(Headers), CsvLine(MakeRowFor("CHEM", "101", "A")), "", CsvLine(MakeRowFor("CHEM", "101", "B")));
        Assert.Equal(new[] { 2, 4 }, Ok(_reader.Read(bytes, "gap.csv")).Rows.Select(r => r.RowNumber));
    }

    /// <summary>A record with fewer fields than the header gets empty cells for the rest.</summary>
    [Fact]
    public void Csv_ShortRecord_GetsEmptyCells()
    {
        var bytes = CsvBytes(CsvLine(Headers), "2026-2027,Fall,CHEM,101");
        var cells = Ok(_reader.Read(bytes, "short.csv")).Rows.Single().Cells;
        Assert.Equal("101", cells[SectionImportColumns.CourseNumber]);
        Assert.Equal("", cells[SectionImportColumns.Campus]);
    }

    /// <summary>An empty file has no header row at all, so it gets the not-a-template-header message.</summary>
    [Fact]
    public void Csv_EmptyFile_GivesTheNoTemplateHeaderMessage() =>
        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, Problem(_reader.Read(Array.Empty<byte>(), "empty.csv")));

    /// <summary>The not-a-template-header message is exactly the agreed wording.</summary>
    [Fact]
    public void NoTemplateHeaderMessage_HasTheAgreedWording() =>
        Assert.Equal(
            "The first row doesn't have the column names from the TermPoint template. Put the template's column names in the first row (Save template… gives you a copy).",
            ImportFileReader.NoTemplateHeaderMessage);

    /// <summary>A header row holding only the user's own column names matches nothing in the template.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void HeaderWithOnlyNonTemplateNames_GivesTheNoTemplateHeaderMessage(Format format)
    {
        var (bytes, name) = Build(format, new string?[] { "Name", "Notes", "" }, new[] { new object?[] { "a", "b", "c" } });
        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, Problem(_reader.Read(bytes, name)));
    }

    /// <summary>A file whose first row is data (no header at all) gets the not-a-template-header message.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void FirstRowIsData_GivesTheNoTemplateHeaderMessage(Format format)
    {
        // Build() always writes a header first, so build the file with the data row as row 1.
        var row = MakeRowFor("CHEM", "101", "A", (SectionImportColumns.MeetingDays, "M,W"));
        byte[] bytes = format == Format.Csv
            ? CsvBytes(CsvLine(row))
            : new XlsxTestWorkbook().Sheet("Sections", s => s.Row(row)).ToBytes();

        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage,
            Problem(_reader.Read(bytes, format == Format.Csv ? "data.csv" : "data.xlsx")));
    }

    /// <summary>A header with only a few template columns still lists the missing ones, in template order.</summary>
    [Theory]
    [InlineData(Format.Csv)]
    [InlineData(Format.Xlsx)]
    public void HeaderWithAFewTemplateColumns_StillListsTheMissingOnes(Format format)
    {
        var keep = new[] { SectionImportColumns.Subject, SectionImportColumns.CourseNumber, SectionImportColumns.SectionCode };
        var headers = keep.Append("My notes").ToArray();
        var (bytes, name) = Build(format, headers, new[] { new object?[] { "CHEM", "101", "A", "x" } });

        Assert.Equal(
            "These columns are missing: Academic Year, Term, Course Title, Section Type, Instructor(s), Tags, Capacity, "
            + "Meeting Days, Begin, End, Frequency, Meeting Type, Building, Room, Campus.",
            Problem(_reader.Read(bytes, name)));
    }

    // ── .xlsx only ────────────────────────────────────────────────────────────

    /// <summary>The Sections sheet is found among several sheets, whatever its case.</summary>
    [Theory]
    [InlineData("Sections")]
    [InlineData("sections")]
    [InlineData("SECTIONS")]
    public void Xlsx_SectionsSheet_IsFoundAmongSeveralIgnoringCase(string sheetName)
    {
        var bytes = new XlsxTestWorkbook()
            .Sheet("Instructions", s => s.Row("Read me first"))
            .Sheet(sheetName, s => s.Row(Headers.Cast<object?>().ToArray()).Row(MakeRowFor("CHEM", "101", "Z")))
            .Sheet("Other", s => s.Row("x"))
            .ToBytes();

        var sheet = Ok(_reader.Read(bytes, "book.xlsx"));

        Assert.Equal("Z", sheet.Rows.Single().Cells[SectionImportColumns.SectionCode]);
    }

    /// <summary>A workbook with no sheet called Sections gets the plain message.</summary>
    [Fact]
    public void Xlsx_NoSectionsSheet_IsReported()
    {
        var bytes = new XlsxTestWorkbook()
            .Sheet("Sheet1", s => s.Row(Headers.Cast<object?>().ToArray()))
            .ToBytes();

        Assert.Equal(
            "This workbook has no sheet named Sections. Put the sections on a sheet with that name, as in the TermPoint template.",
            Problem(_reader.Read(bytes, "book.xlsx")));
    }

    /// <summary>Bytes that are not a workbook give the generic message, without any exception text.</summary>
    [Fact]
    public void Xlsx_RandomBytes_GiveTheCouldntReadMessage()
    {
        var bytes = new byte[5000];
        new Random(42).NextBytes(bytes);

        Assert.Equal(
            "TermPoint couldn't read this file as an Excel workbook (.xlsx). Check that it opens in Excel, then save it again as .xlsx.",
            Problem(_reader.Read(bytes, "fake.xlsx")));
    }

    /// <summary>Empty content named .xlsx is also just unreadable.</summary>
    [Fact]
    public void Xlsx_EmptyContent_GivesTheCouldntReadMessage() =>
        Assert.Equal(ImportFileReader.UnreadableWorkbookMessage, Problem(_reader.Read(Array.Empty<byte>(), "empty.xlsx")));

    /// <summary>A Sections sheet with no rows has no header, so it gets the not-a-template-header message.</summary>
    [Fact]
    public void Xlsx_EmptySheet_GivesTheNoTemplateHeaderMessage()
    {
        var bytes = new XlsxTestWorkbook().Sheet("Sections", _ => { }).ToBytes();
        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, Problem(_reader.Read(bytes, "empty.xlsx")));
    }

    /// <summary>
    /// Row numbers are Excel's own even when the sheet skips rows: data on rows 2, 3 and 7 reads as rows 2, 3 and 7
    /// (ExcelDataReader returns an empty row for each row number the file does not store).
    /// </summary>
    [Fact]
    public void Xlsx_RowGap_KeepsExcelRowNumbers()
    {
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s => s
            .RowAt(1, Headers.Cast<object?>().ToArray())
            .RowAt(2, MakeRowFor("CHEM", "101", "A"))
            .RowAt(3, MakeRowFor("CHEM", "101", "B"))
            .RowAt(7, MakeRowFor("CHEM", "101", "C"))).ToBytes();

        var sheet = Ok(_reader.Read(bytes, "gap.xlsx"));

        Assert.Equal(new[] { 2, 3, 7 }, sheet.Rows.Select(r => r.RowNumber));
        Assert.Equal(new[] { "A", "B", "C" }, sheet.Rows.Select(r => r.Cells[SectionImportColumns.SectionCode]));
    }

    /// <summary>Row numbers stay right when the gap is at the very start (header on row 1, first data on row 4).</summary>
    [Fact]
    public void Xlsx_GapAfterHeader_KeepsExcelRowNumbers()
    {
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s => s
            .RowAt(1, Headers.Cast<object?>().ToArray())
            .RowAt(4, MakeRowFor("CHEM", "101", "A"))).ToBytes();

        Assert.Equal(new[] { 4 }, Ok(_reader.Read(bytes, "gap.xlsx")).Rows.Select(r => r.RowNumber));
    }

    /// <summary>Numeric cells become plain text: 101 not 101.0, and 1.5 stays 1.5.</summary>
    [Fact]
    public void Xlsx_NumericCells_BecomePlainText()
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRowFor("CHEM", "101", "A", (SectionImportColumns.CourseNumber, 101), (SectionImportColumns.Capacity, 30.0),
                (SectionImportColumns.Frequency, 1.5))));

        var cells = sheet.Rows.Single().Cells;
        Assert.Equal("101", cells[SectionImportColumns.CourseNumber]);
        Assert.Equal("30", cells[SectionImportColumns.Capacity]);
        Assert.Equal("1.5", cells[SectionImportColumns.Frequency]);
    }

    /// <summary>A time-formatted cell becomes HH:mm text.</summary>
    [Fact]
    public void Xlsx_TimeCell_BecomesHoursAndMinutes()
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow(new (string, object?)[]
            {
                (SectionImportColumns.Begin, TimeSpan.FromHours(10)),
                (SectionImportColumns.End, TimeSpan.FromHours(13) + TimeSpan.FromMinutes(50)),
            })));

        var cells = sheet.Rows.Single().Cells;
        Assert.Equal("10:00", cells[SectionImportColumns.Begin]);
        Assert.Equal("13:50", cells[SectionImportColumns.End]);
    }

    /// <summary>
    /// A time Excel stores a hair under 10:00 (its binary fractions of a day cannot hold 10:00 exactly) still
    /// reads as 10:00, not 09:59:59.9999 or 09:59.
    /// </summary>
    [Fact]
    public void Xlsx_TimeCellJustUnderTheMinute_RoundsToTheMinute()
    {
        double justUnderTen = (TimeSpan.FromHours(10).TotalSeconds - 0.0001) / 86400.0;   // 09:59:59.9999
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow((SectionImportColumns.Begin, new XlsxTestWorkbook.TimeFraction(justUnderTen)))));

        Assert.Equal("10:00", sheet.Rows.Single().Cells[SectionImportColumns.Begin]);
    }

    /// <summary>Booleans and text cells come through as TRUE/FALSE and as written (not trimmed).</summary>
    [Fact]
    public void Xlsx_BooleanAndTextCells_ComeThrough()
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow(new (string, object?)[]
            {
                (SectionImportColumns.Tags, true),
                (SectionImportColumns.Campus, "  Main  "),
            })));

        var cells = sheet.Rows.Single().Cells;
        Assert.Equal("TRUE", cells[SectionImportColumns.Tags]);
        Assert.Equal("  Main  ", cells[SectionImportColumns.Campus]);
    }

    // ── CellToText ────────────────────────────────────────────────────────────

    /// <summary>Null and empty become an empty string; text is kept exactly.</summary>
    [Fact]
    public void CellToText_NullEmptyAndText()
    {
        Assert.Equal("", ImportFileReader.CellToText(null));
        Assert.Equal("", ImportFileReader.CellToText(""));
        Assert.Equal("abc", ImportFileReader.CellToText("abc"));
        Assert.Equal(" padded ", ImportFileReader.CellToText(" padded "));
    }

    /// <summary>Whole numbers become integer text, never with a decimal point or an exponent.</summary>
    [Theory]
    [InlineData(101.0, "101")]
    [InlineData(1000.0, "1000")]
    [InlineData(1234567.0, "1234567")]
    [InlineData(-3.0, "-3")]
    [InlineData(0.0, "0")]
    [InlineData(1e15, "1000000000000000")]
    public void CellToText_WholeNumbers(double value, string expected) =>
        Assert.Equal(expected, ImportFileReader.CellToText(value));

    /// <summary>Negative zero is just 0.</summary>
    [Fact]
    public void CellToText_NegativeZero_IsZero() => Assert.Equal("0", ImportFileReader.CellToText(-0.0));

    /// <summary>Other numbers use the shortest text that round-trips.</summary>
    [Theory]
    [InlineData(1.5, "1.5")]
    [InlineData(0.1, "0.1")]
    [InlineData(-2.25, "-2.25")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    public void CellToText_OtherNumbers(double value, string expected) =>
        Assert.Equal(expected, ImportFileReader.CellToText(value));

    /// <summary>Numbers are formatted with the invariant culture whatever the machine's culture.</summary>
    [Fact]
    public void CellToText_Numbers_IgnoreTheCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");   // decimal comma, space thousands separator
            Assert.Equal("1.5", ImportFileReader.CellToText(1.5));
            Assert.Equal("1234567", ImportFileReader.CellToText(1234567.0));
            Assert.Equal("1.5", ImportFileReader.CellToText(1.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>A time with no date (either of Excel's time-only dates) becomes HH:mm, or HH:mm:ss when the seconds are not zero.</summary>
    [Theory]
    [InlineData(1899, 12, 30, 10, 0, 0, "10:00")]
    [InlineData(1899, 12, 31, 10, 0, 0, "10:00")]
    [InlineData(1899, 12, 30, 0, 0, 0, "00:00")]
    [InlineData(1899, 12, 30, 8, 5, 0, "08:05")]
    [InlineData(1899, 12, 30, 22, 0, 0, "22:00")]
    [InlineData(1899, 12, 30, 10, 30, 15, "10:30:15")]
    public void CellToText_TimeOnly(int y, int mo, int d, int h, int mi, int s, string expected) =>
        Assert.Equal(expected, ImportFileReader.CellToText(new DateTime(y, mo, d, h, mi, s)));

    /// <summary>A time a hair under the minute rounds to it; the rounding is to the nearest second and can wrap past midnight.</summary>
    [Fact]
    public void CellToText_TimeOnly_RoundsToTheNearestSecond()
    {
        var date = new DateTime(1899, 12, 30);
        Assert.Equal("10:00", ImportFileReader.CellToText(date.AddHours(10).AddTicks(-1000)));            // 09:59:59.9999
        Assert.Equal("10:00", ImportFileReader.CellToText(date.AddHours(10).AddMilliseconds(400)));       // 10:00:00.4
        Assert.Equal("10:00:01", ImportFileReader.CellToText(date.AddHours(10).AddMilliseconds(600)));    // 10:00:00.6
        Assert.Equal("00:00", ImportFileReader.CellToText(date.AddDays(1).AddTicks(-1000)));              // 23:59:59.9999
    }

    /// <summary>Any other date becomes yyyy-MM-dd, with HH:mm added only when it has a time of day.</summary>
    [Fact]
    public void CellToText_Dates()
    {
        Assert.Equal("2026-10-24", ImportFileReader.CellToText(new DateTime(2026, 10, 24)));
        Assert.Equal("2026-10-24 14:30", ImportFileReader.CellToText(new DateTime(2026, 10, 24, 14, 30, 0)));
    }

    /// <summary>Booleans become TRUE and FALSE.</summary>
    [Fact]
    public void CellToText_Booleans()
    {
        Assert.Equal("TRUE", ImportFileReader.CellToText(true));
        Assert.Equal("FALSE", ImportFileReader.CellToText(false));
    }

    /// <summary>Anything else uses invariant Convert.ToString.</summary>
    [Fact]
    public void CellToText_OtherTypes_UseInvariantToString()
    {
        Assert.Equal("7", ImportFileReader.CellToText(7));
        Assert.Equal("9000000000", ImportFileReader.CellToText(9000000000L));
        Assert.Equal("x", ImportFileReader.CellToText('x'));
    }
}

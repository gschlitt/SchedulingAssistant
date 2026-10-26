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

    /// <summary>The Windows ANSI code page (Excel's plain "CSV" on a Western Windows machine).</summary>
    private static Encoding Windows1252 => Encoding.GetEncoding(1252);

    /// <summary>
    /// Bytes that are not valid UTF-8 are read as Windows-1252, so an accented name stays readable (the
    /// accented letters are the same bytes as in Latin-1).
    /// </summary>
    [Fact]
    public void Csv_NonUtf8Bytes_AreDecodedAsWindows1252()
    {
        var text = string.Join("\r\n", CsvLine(Headers),
            CsvLine(MakeRow((SectionImportColumns.Instructors, "Côté, Jean")))) + "\r\n";
        var bytes = Windows1252.GetBytes(text);
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(bytes));   // really not UTF-8

        var sheet = Ok(_reader.Read(bytes, "ansi.csv"));

        Assert.Equal("Côté, Jean", sheet.Rows.Single().Cells[SectionImportColumns.Instructors]);
    }

    /// <summary>
    /// Excel's plain "CSV" saves a typographic apostrophe, quotation marks, a dash, an ellipsis and a euro sign
    /// in Windows-1252's 0x80 to 0x9F range: they must come through as those characters, not as the invisible
    /// control characters Latin-1 would give.
    /// </summary>
    [Fact]
    public void Csv_Windows1252Punctuation_ComesThroughAsTheCharactersTyped()
    {
        var text = string.Join("\r\n", CsvLine(Headers), CsvLine(MakeRow(
            (SectionImportColumns.Instructors, "O’Brien, Pat"),
            (SectionImportColumns.CourseTitle, "Intro – “Part 1”… 5€")))) + "\r\n";
        var bytes = Windows1252.GetBytes(text);
        Assert.Contains((byte)0x92, bytes);   // the apostrophe is the single byte 0x92 in this code page
        Assert.Contains((byte)0x96, bytes);   // and the en dash is 0x96

        var cells = Ok(_reader.Read(bytes, "ansi.csv")).Rows.Single().Cells;

        Assert.Equal("O’Brien, Pat", cells[SectionImportColumns.Instructors]);
        Assert.Equal("Intro – “Part 1”… 5€", cells[SectionImportColumns.CourseTitle]);
    }

    /// <summary>A UTF-16 little-endian CSV with its byte-order mark (Excel's "Unicode Text" kind) is read, names exactly as written.</summary>
    [Fact]
    public void Csv_Utf16LittleEndian_IsRead()
    {
        var text = string.Join("\r\n", CsvLine(Headers),
            CsvLine(MakeRow((SectionImportColumns.Instructors, "Dvořák, Antonín")))) + "\r\n";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        var sheet = Ok(_reader.Read(bytes, "utf16.csv"));

        Assert.Equal("Dvořák, Antonín", sheet.Rows.Single().Cells[SectionImportColumns.Instructors]);
        Assert.Equal("2026-2027", sheet.Rows.Single().Cells[SectionImportColumns.AcademicYear]);   // the mark is not part of the first header
    }

    /// <summary>A UTF-16 big-endian CSV with its byte-order mark is read too.</summary>
    [Fact]
    public void Csv_Utf16BigEndian_IsRead()
    {
        var text = string.Join("\r\n", CsvLine(Headers),
            CsvLine(MakeRow((SectionImportColumns.Instructors, "Dvořák, Antonín")))) + "\r\n";
        var bytes = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray();

        var sheet = Ok(_reader.Read(bytes, "utf16be.csv"));

        Assert.Equal("Dvořák, Antonín", sheet.Rows.Single().Cells[SectionImportColumns.Instructors]);
    }

    /// <summary>A tab-separated file is still refused with the not-a-template-header message, UTF-16 or not (a separate, later item).</summary>
    [Fact]
    public void Csv_TabSeparatedUtf16_IsStillRefusedAsHavingNoTemplateHeader()
    {
        var text = string.Join("\r\n", string.Join('\t', Headers), string.Join('\t', MakeRow().Select(c => c?.ToString()))) + "\r\n";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, Problem(_reader.Read(bytes, "tabs.csv")));
    }

    // ── DecodeCsv ─────────────────────────────────────────────────────────────

    /// <summary>A UTF-16 little-endian mark (FF FE) selects UTF-16 LE and is dropped.</summary>
    [Fact]
    public void DecodeCsv_Utf16LittleEndianBom_DecodesAndDropsTheMark()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Ané,€")).ToArray();

        Assert.Equal("Ané,€", ImportFileReader.DecodeCsv(bytes));
    }

    /// <summary>A UTF-16 big-endian mark (FE FF) selects UTF-16 BE and is dropped.</summary>
    [Fact]
    public void DecodeCsv_Utf16BigEndianBom_DecodesAndDropsTheMark()
    {
        var bytes = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("Ané,€")).ToArray();

        Assert.Equal("Ané,€", ImportFileReader.DecodeCsv(bytes));
    }

    /// <summary>A UTF-8 mark (EF BB BF) is dropped and the rest read as UTF-8.</summary>
    [Fact]
    public void DecodeCsv_Utf8Bom_DecodesAndDropsTheMark()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(new UTF8Encoding(false).GetBytes("Ané,€")).ToArray();

        Assert.Equal("Ané,€", ImportFileReader.DecodeCsv(bytes));
    }

    /// <summary>Valid UTF-8 with no mark is read as UTF-8 (the 1252 fallback is only for bytes that are not UTF-8).</summary>
    [Fact]
    public void DecodeCsv_Utf8WithoutBom_IsReadAsUtf8() =>
        Assert.Equal("Ané,€ ’ –", ImportFileReader.DecodeCsv(new UTF8Encoding(false).GetBytes("Ané,€ ’ –")));

    /// <summary>
    /// Bytes that are not UTF-8 are Windows-1252: 0x92 is the right single quotation mark and 0x96 the en dash
    /// (Latin-1 would give the control characters U+0092 and U+0096).
    /// </summary>
    [Fact]
    public void DecodeCsv_Windows1252Bytes_GiveTheTypographicCharacters()
    {
        var bytes = new byte[] { (byte)'O', 0x92, (byte)'B', (byte)' ', 0x96, (byte)' ', 0x80, (byte)' ', 0xE9 };

        Assert.Equal("O’B – € é", ImportFileReader.DecodeCsv(bytes));
    }

    /// <summary>No bytes decode to no text (and a lone mark to no text either).</summary>
    [Fact]
    public void DecodeCsv_EmptyOrOnlyAMark_IsEmpty()
    {
        Assert.Equal("", ImportFileReader.DecodeCsv(Array.Empty<byte>()));
        Assert.Equal("", ImportFileReader.DecodeCsv(new byte[] { 0xFF, 0xFE }));
        Assert.Equal("", ImportFileReader.DecodeCsv(new byte[] { 0xFE, 0xFF }));
        Assert.Equal("", ImportFileReader.DecodeCsv(new byte[] { 0xEF, 0xBB, 0xBF }));
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

    // ── Cell issues: Excel error cells and date cells ─────────────────────────

    /// <summary>The issue recorded on a row for a column, or null.</summary>
    private static CellIssue? IssueOf(ImportRow row, string column) => row.Issue(column);

    /// <summary>
    /// A cell showing an Excel error in a workbook is recorded on its row as an error issue (the error as Excel
    /// shows it) and reads as empty text, in a required and in an optional column alike.
    /// </summary>
    [Theory]
    [InlineData("#N/A")]
    [InlineData("#REF!")]
    [InlineData("#DIV/0!")]
    [InlineData("#NAME?")]
    [InlineData("#NULL!")]
    [InlineData("#NUM!")]
    [InlineData("#VALUE!")]
    public void Xlsx_ErrorCell_IsRecordedOnTheRow_AndReadsAsEmpty(string code)
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow(
                (SectionImportColumns.SectionCode, new XlsxTestWorkbook.ErrorCell(code)),
                (SectionImportColumns.Instructors, new XlsxTestWorkbook.ErrorCell(code)))));

        var row = sheet.Rows.Single();
        Assert.Equal(new CellIssue(CellIssueKind.ExcelError, code), IssueOf(row, SectionImportColumns.SectionCode));
        Assert.Equal(new CellIssue(CellIssueKind.ExcelError, code), IssueOf(row, SectionImportColumns.Instructors));
        Assert.Equal("", row.Cells[SectionImportColumns.SectionCode]);
        Assert.Equal("", row.Cells[SectionImportColumns.Instructors]);
        Assert.Equal(2, row.Issues!.Count);
        Assert.Null(IssueOf(row, SectionImportColumns.Subject));   // the other cells are fine
        Assert.Equal("CHEM", row.Cells[SectionImportColumns.Subject]);
    }

    /// <summary>
    /// An error the library has no name for (a newer Excel error such as #SPILL!, which ExcelDataReader 3.9.0 reads as
    /// a null value with no error, just like an empty cell) is still an error issue, found from the sheet's own XML: the
    /// cell is never read as blank, and the issue has no error text to show.
    /// </summary>
    [Theory]
    [InlineData("#SPILL!")]
    [InlineData("#CALC!")]
    [InlineData("#BLOCKED!")]
    public void Xlsx_NewerErrorCell_IsStillAnErrorIssue_WithNoNameToShow(string code)
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow((SectionImportColumns.Capacity, new XlsxTestWorkbook.ErrorCell(code)))));

        var row = sheet.Rows.Single();
        Assert.Equal(new CellIssue(CellIssueKind.ExcelError, null), IssueOf(row, SectionImportColumns.Capacity));
        Assert.Equal("", row.Cells[SectionImportColumns.Capacity]);
        Assert.Single(row.Issues!);
    }

    /// <summary>
    /// The error is found in the right column even when the data does not start in column A: the sheet's XML refers to
    /// cells by their lettered position, and the reader's column numbers must agree with it.
    /// </summary>
    [Fact]
    public void Xlsx_NewerErrorCell_IsFoundInTheRightColumn_WhenTheDataStartsAfterColumnA()
    {
        var header = new object?[] { null, null }.Concat(Headers).ToArray();
        var data = MakeRow((SectionImportColumns.Instructors, new XlsxTestWorkbook.ErrorCell("#SPILL!")));
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s => s.Row(header).Row(new object?[] { null, null }.Concat(data).ToArray())).ToBytes();

        var row = Ok(_reader.Read(bytes, "offset.xlsx")).Rows.Single();

        Assert.Equal(new[] { SectionImportColumns.Instructors }, row.Issues!.Keys.ToArray());
        Assert.Equal("CHEM", row.Cells[SectionImportColumns.Subject]);
    }

    /// <summary>An error cell on another sheet of the workbook does not touch the Sections sheet's rows.</summary>
    [Fact]
    public void Xlsx_ErrorCellsOnAnotherSheet_AreIgnored()
    {
        var bytes = new XlsxTestWorkbook()
            .Sheet("Notes", s => s.Row(new XlsxTestWorkbook.ErrorCell("#SPILL!"), new XlsxTestWorkbook.ErrorCell("#N/A")))
            .Sheet("Sections", s => s.Row(Headers.Cast<object?>().ToArray()).Row(MakeRow()))
            .ToBytes();

        Assert.All(Ok(_reader.Read(bytes, "two-sheets.xlsx")).Rows, r => Assert.Null(r.Issues));
    }

    /// <summary>An error cell in one of the user's own columns is ignored, as that column is.</summary>
    [Fact]
    public void Xlsx_ErrorCellInANonTemplateColumn_IsIgnored()
    {
        var headers = Headers.Append("My notes").Select(h => (string?)h).ToArray();
        var row = MakeRow().Append(new XlsxTestWorkbook.ErrorCell("#REF!")).ToArray();
        var (bytes, name) = Build(Format.Xlsx, headers, new[] { row });

        var read = Ok(_reader.Read(bytes, name)).Rows.Single();

        Assert.Null(read.Issues);
    }

    /// <summary>
    /// A row whose only content is an error cell is not a blank row: it is kept, so that the planner reports it
    /// instead of the row vanishing.
    /// </summary>
    [Fact]
    public void Xlsx_RowHoldingOnlyAnErrorCell_IsKept()
    {
        var onlyError = new object?[Headers.Length];
        onlyError[Array.IndexOf(Headers, SectionImportColumns.Instructors)] = new XlsxTestWorkbook.ErrorCell("#N/A");

        var sheet = Ok(ReadBuilt(Format.Xlsx, onlyError, MakeRow()));

        Assert.Equal(new[] { 2, 3 }, sheet.Rows.Select(r => r.RowNumber));
        Assert.NotNull(IssueOf(sheet.Rows[0], SectionImportColumns.Instructors));
    }

    /// <summary>
    /// A cell Excel turned into a date (the user typed <c>1-2</c> as a section code) is recorded as a date issue
    /// showing the date as <c>2 Jan 2026</c> (a time of day on it is not shown), and reads as empty text.
    /// </summary>
    [Fact]
    public void Xlsx_DateCell_IsRecordedAsADateIssue_AndReadsAsEmpty()
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow(
                (SectionImportColumns.SectionCode, new DateTime(2026, 1, 2)),
                (SectionImportColumns.Capacity, new DateTime(2025, 12, 31, 14, 30, 0)))));

        var row = sheet.Rows.Single();
        Assert.Equal(new CellIssue(CellIssueKind.Date, "2 Jan 2026"), IssueOf(row, SectionImportColumns.SectionCode));
        Assert.Equal(new CellIssue(CellIssueKind.Date, "31 Dec 2025"), IssueOf(row, SectionImportColumns.Capacity));
        Assert.Equal("", row.Cells[SectionImportColumns.SectionCode]);
        Assert.Equal("", row.Cells[SectionImportColumns.Capacity]);
    }

    /// <summary>The date is shown the same way whatever the machine's culture.</summary>
    [Fact]
    public void ReadCell_Date_IsShownInInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var (text, issue) = ImportFileReader.ReadCell(new DateTime(2026, 2, 3), null);

            Assert.Equal("", text);
            Assert.Equal(new CellIssue(CellIssueKind.Date, "3 Feb 2026"), issue);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>Times in Begin and End (Excel's time-only dates) are not date issues: they read as HH:mm as before.</summary>
    [Fact]
    public void Xlsx_TimeCells_AreNotDateIssues()
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow(
                (SectionImportColumns.Begin, TimeSpan.FromHours(9)),
                (SectionImportColumns.End, new XlsxTestWorkbook.TimeFraction(0.41666666665)))));

        var row = sheet.Rows.Single();
        Assert.Null(row.Issues);
        Assert.Equal("09:00", row.Cells[SectionImportColumns.Begin]);
        Assert.Equal("10:00", row.Cells[SectionImportColumns.End]);
    }

    /// <summary>A date cell in one of the user's own columns is ignored.</summary>
    [Fact]
    public void Xlsx_DateCellInANonTemplateColumn_IsIgnored()
    {
        var headers = Headers.Append("Printed on").Select(h => (string?)h).ToArray();
        var row = MakeRow().Append(new DateTime(2026, 1, 2)).ToArray();
        var (bytes, name) = Build(Format.Xlsx, headers, new[] { row });

        Assert.Null(Ok(_reader.Read(bytes, name)).Rows.Single().Issues);
    }

    /// <summary>A workbook of ordinary cells has no issues on any row.</summary>
    [Fact]
    public void Xlsx_OrdinaryCells_HaveNoIssues()
    {
        var sheet = Ok(ReadBuilt(Format.Xlsx,
            MakeRow((SectionImportColumns.Capacity, 30.0), (SectionImportColumns.Tags, true))));

        Assert.All(sheet.Rows, r => Assert.Null(r.Issues));
    }

    /// <summary>
    /// A CSV cell whose whole trimmed text is an Excel error literal (any case) is an error issue, and reads as
    /// empty; the literal is shown the way Excel spells it.
    /// </summary>
    [Theory]
    [InlineData("#N/A", "#N/A")]
    [InlineData("#REF!", "#REF!")]
    [InlineData("#DIV/0!", "#DIV/0!")]
    [InlineData("#NAME?", "#NAME?")]
    [InlineData("#NULL!", "#NULL!")]
    [InlineData("#NUM!", "#NUM!")]
    [InlineData("#VALUE!", "#VALUE!")]
    [InlineData("#SPILL!", "#SPILL!")]
    [InlineData("#n/a", "#N/A")]
    [InlineData("  #Div/0!  ", "#DIV/0!")]
    public void Csv_ErrorLiteral_IsAnErrorIssue_AndReadsAsEmpty(string cell, string shown)
    {
        var sheet = Ok(_reader.Read(
            CsvBytes(CsvLine(Headers), CsvLine(MakeRow(
                (SectionImportColumns.SectionCode, cell), (SectionImportColumns.Instructors, cell)))),
            "errors.csv"));

        var row = sheet.Rows.Single();
        Assert.Equal(new CellIssue(CellIssueKind.ExcelError, shown), IssueOf(row, SectionImportColumns.SectionCode));
        Assert.Equal(new CellIssue(CellIssueKind.ExcelError, shown), IssueOf(row, SectionImportColumns.Instructors));
        Assert.Equal("", row.Cells[SectionImportColumns.SectionCode]);
        Assert.Equal("", row.Cells[SectionImportColumns.Instructors]);
        Assert.Equal("CHEM", row.Cells[SectionImportColumns.Subject]);
    }

    /// <summary>Text that only contains or starts like an error literal is ordinary text, not an issue.</summary>
    [Theory]
    [InlineData("#N/A in the file")]
    [InlineData("Section #REF!")]
    [InlineData("#")]
    [InlineData("#hashtag")]
    [InlineData("#N/A!")]
    [InlineData("N/A")]
    public void Csv_TextThatIsNotWhollyAnErrorLiteral_IsNotAnIssue(string cell)
    {
        var sheet = Ok(_reader.Read(
            CsvBytes(CsvLine(Headers), CsvLine(MakeRow((SectionImportColumns.Instructors, cell)))), "text.csv"));

        var row = sheet.Rows.Single();
        Assert.Null(row.Issues);
        Assert.Equal(cell, row.Cells[SectionImportColumns.Instructors]);
    }

    /// <summary>An error literal in one of the user's own CSV columns is ignored, and a CSV never has a date issue.</summary>
    [Fact]
    public void Csv_ErrorLiteralInANonTemplateColumn_IsIgnored_AndDatesAreJustText()
    {
        var headers = Headers.Append("My notes").ToArray();
        var line = CsvLine(MakeRow((SectionImportColumns.SectionCode, "1-2"), (SectionImportColumns.Campus, "2026-01-02"))) + ",#REF!";

        var row = Ok(_reader.Read(CsvBytes(CsvLine(headers), line), "extra.csv")).Rows.Single();

        Assert.Null(row.Issues);
        Assert.Equal("1-2", row.Cells[SectionImportColumns.SectionCode]);
        Assert.Equal("2026-01-02", row.Cells[SectionImportColumns.Campus]);
    }

    /// <summary>A CSV row whose only content is an error literal is kept, like the same row in a workbook.</summary>
    [Fact]
    public void Csv_RowHoldingOnlyAnErrorLiteral_IsKept()
    {
        var onlyError = new object?[Headers.Length];
        onlyError[Array.IndexOf(Headers, SectionImportColumns.Capacity)] = "#N/A";

        var sheet = Ok(_reader.Read(CsvBytes(CsvLine(Headers), CsvLine(onlyError)), "only.csv"));

        Assert.Equal(new[] { 2 }, sheet.Rows.Select(r => r.RowNumber));
        Assert.NotNull(IssueOf(sheet.Rows.Single(), SectionImportColumns.Capacity));
    }

    /// <summary>Every error the library names has the text Excel shows; an unknown one has none.</summary>
    [Theory]
    [InlineData(ExcelDataReader.CellError.NULL, "#NULL!")]
    [InlineData(ExcelDataReader.CellError.DIV0, "#DIV/0!")]
    [InlineData(ExcelDataReader.CellError.VALUE, "#VALUE!")]
    [InlineData(ExcelDataReader.CellError.REF, "#REF!")]
    [InlineData(ExcelDataReader.CellError.NAME, "#NAME?")]
    [InlineData(ExcelDataReader.CellError.NUM, "#NUM!")]
    [InlineData(ExcelDataReader.CellError.NA, "#N/A")]
    [InlineData((ExcelDataReader.CellError)200, null)]
    public void ReadCell_Error_IsShownAsExcelShowsIt(ExcelDataReader.CellError error, string? shown)
    {
        var (text, issue) = ImportFileReader.ReadCell(null, error);

        Assert.Equal("", text);
        Assert.Equal(new CellIssue(CellIssueKind.ExcelError, shown), issue);
    }

    /// <summary>An error wins over whatever value came with it, and an ordinary value has no issue.</summary>
    [Fact]
    public void ReadCell_OrdinaryValues_HaveNoIssue()
    {
        Assert.Equal(("abc", (CellIssue?)null), ImportFileReader.ReadCell("abc", null));
        Assert.Equal(("101", (CellIssue?)null), ImportFileReader.ReadCell(101.0, null));
        Assert.Equal(("", (CellIssue?)null), ImportFileReader.ReadCell(null, null));
        Assert.Equal(("10:00", (CellIssue?)null), ImportFileReader.ReadCell(new DateTime(1899, 12, 30, 10, 0, 0), null));
        Assert.Equal(("", new CellIssue(CellIssueKind.ExcelError, "#N/A")),
            ImportFileReader.ReadCell("stale text", ExcelDataReader.CellError.NA));
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

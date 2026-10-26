#if !BROWSER
using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Reads a section import file (an <c>.xlsx</c> workbook or a <c>.csv</c>) into an
/// <see cref="ImportSheet"/>: checks the header row against <see cref="SectionImportColumns"/>,
/// turns every cell into text so both formats look the same to the planner, drops blank rows and
/// enforces the file limits. A cell that shows an Excel error, or that Excel turned into a date, is not
/// turned into text: it is recorded on its row as a <see cref="CellIssue"/> for the planner to report,
/// because reading it as blank (or as a date's text) would let a section be imported without something the
/// user meant it to have.
///
/// <para>The class is pure: the caller reads the file's bytes (off the UI thread, with a deadline)
/// and this class does no file I/O and keeps no state, so it can be registered in DI and shared.
/// Any problem with the file as a whole comes back as one plain-English message in
/// <see cref="ImportReadResult.Problem"/>; nothing here throws for bad input.</para>
///
/// <para>Desktop only: ExcelDataReader is referenced only by the desktop build.</para>
/// </summary>
public sealed class ImportFileReader
{
    // ── Limits (design decision E4) ───────────────────────────────────────────

    /// <summary>The largest file, in bytes, that will be read (10 MB).</summary>
    internal const int MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>The most non-blank data rows a file may hold.</summary>
    internal const int MaxDataRows = 5000;

    // ── Messages ──────────────────────────────────────────────────────────────

    /// <summary>Shown for any extension other than <c>.xlsx</c> or <c>.csv</c>.</summary>
    internal const string UnsupportedTypeMessage = "TermPoint can import Excel workbooks (.xlsx) and CSV files (.csv).";

    /// <summary>Shown when the file is over the size or row limit.</summary>
    internal const string TooLargeMessage = "This file is larger than TermPoint can import at once. Split it by subject.";

    /// <summary>Shown when a workbook has no sheet named Sections.</summary>
    internal const string NoSectionsSheetMessage =
        "This workbook has no sheet named Sections. Put the sections on a sheet with that name, as in the TermPoint template.";

    /// <summary>Shown when a workbook cannot be opened or read (not a zip, corrupt, password-protected).</summary>
    internal const string UnreadableWorkbookMessage =
        "TermPoint couldn't read this file as an Excel workbook (.xlsx). Check that it opens in Excel, then save it again as .xlsx.";

    /// <summary>Shown when the header row matches none of the template columns (empty file, no header, or only the user's own columns).</summary>
    internal const string NoTemplateHeaderMessage =
        "The first row doesn't have the column names from the TermPoint template. Put the template's column names in the first row (Save template… gives you a copy).";

    /// <summary>The name of the sheet the sections must be on.</summary>
    private const string SectionsSheetName = "Sections";

    /// <summary>The time-only dates Excel gives a cell that holds a time with no date.</summary>
    private static readonly DateTime ExcelTimeOnlyDate1 = new(1899, 12, 30);
    private static readonly DateTime ExcelTimeOnlyDate2 = new(1899, 12, 31);

    /// <summary>
    /// The errors Excel shows in a cell, spelled as Excel spells them. A CSV saved from Excel holds an error cell
    /// as exactly this text, so a CSV cell whose whole trimmed text is one of them (in any case) is an error cell
    /// (see <see cref="CsvCellIssue"/>). Maps any casing to the canonical spelling, which is what the user is shown.
    /// </summary>
    private static readonly Dictionary<string, string> ExcelErrorLiterals = new[]
    {
        "#N/A", "#REF!", "#DIV/0!", "#NAME?", "#NULL!", "#NUM!", "#VALUE!",
        "#SPILL!", "#CALC!", "#FIELD!", "#BLOCKED!", "#CONNECT!", "#UNKNOWN!",
    }.ToDictionary(text => text, text => text, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One row as read from the file, before the header is applied: its row number, every cell as text and, when
    /// a cell cannot be used (an Excel error, a date), what is wrong with it.
    /// </summary>
    /// <param name="RowNumber">The row number as the user sees it.</param>
    /// <param name="Cells">Every cell as text; a cell with an issue is an empty string.</param>
    /// <param name="Issues">
    /// The issue of each cell, at the same position as in <paramref name="Cells"/> (null for a cell that is
    /// fine); null for the whole row when no cell has one, which is nearly every row.
    /// </param>
    private readonly record struct RawRow(int RowNumber, IReadOnlyList<string> Cells, IReadOnlyList<CellIssue?>? Issues = null);

    /// <summary>
    /// Registers the Windows code pages once per process. ExcelDataReader's configuration object looks up
    /// code page 1252 (its fallback for legacy text) as soon as it is created, and without a registered
    /// provider that lookup throws even for a modern .xlsx. The provider ships in the .NET shared framework,
    /// so no package is needed; registering it is idempotent and has no other effect on the app.
    /// </summary>
    static ImportFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the file's bytes into an <see cref="ImportSheet"/>, or reports one problem with the file.
    /// The format is chosen by the file name's extension (case-insensitive).
    /// </summary>
    /// <param name="content">The whole file.</param>
    /// <param name="fileName">The file's name (a path is accepted; only the name is kept). Decides the format.</param>
    /// <returns>
    /// A result holding the sheet (possibly with zero rows) or a single plain-English problem. Never throws
    /// for bad file content; a null argument is the caller's bug.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> or <paramref name="fileName"/> is null.</exception>
    public ImportReadResult Read(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        string displayName = Path.GetFileName(fileName);
        string extension = Path.GetExtension(fileName);
        bool isXlsx = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
        bool isCsv = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);

        if (!isXlsx && !isCsv)
            return ImportReadResult.Failure(UnsupportedTypeMessage);

        // Checked before parsing so a huge file is never decompressed or decoded.
        if (content.Length > MaxFileBytes)
            return ImportReadResult.Failure(TooLargeMessage);

        return isXlsx ? ReadXlsx(content, displayName) : ReadCsv(content, displayName);
    }

    // ── .xlsx ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the sheet named Sections (case and spacing ignored) from a workbook.
    /// Any exception from the library (not a zip, corrupt, password-protected) becomes the generic
    /// unreadable-workbook message; the exception text is never shown to the user.
    /// </summary>
    /// <param name="content">The workbook bytes.</param>
    /// <param name="displayName">The file's display name.</param>
    /// <returns>The sheet, or the problem.</returns>
    private static ImportReadResult ReadXlsx(byte[] content, string displayName)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);

            // The reader starts positioned on the first sheet; NextResult moves to the next.
            bool found;
            do
            {
                found = NameMatch.Equal(reader.Name, SectionsSheetName);
            }
            while (!found && reader.NextResult());

            if (!found)
                return ImportReadResult.Failure(NoSectionsSheetMessage);

            // The library cannot see an error it has no name for (it reads such a cell as empty); the sheet's own
            // XML lists every error cell, so those the library lost are found there.
            var errorCells = XlsxErrorCells.Find(content, reader.Name);
            return BuildSheet(EnumerateWorksheetRows(reader, errorCells), displayName);
        }
        catch (Exception)
        {
            return ImportReadResult.Failure(UnreadableWorkbookMessage);
        }
    }

    /// <summary>
    /// Lazily yields the rows of the sheet the reader is positioned on, each cell turned into text by
    /// <see cref="ReadCell"/> (which also flags an Excel error cell and a date cell). ExcelDataReader yields an
    /// empty row for every row number Excel does not store, so counting calls to <c>Read()</c> gives the row
    /// number Excel shows (the header is row 1).
    /// </summary>
    /// <param name="reader">An open reader positioned on the sheet to read.</param>
    /// <param name="errorCells">
    /// The sheet's error cells as (row, zero-based column), from <see cref="XlsxErrorCells"/>: used to catch an
    /// error the library does not report.
    /// </param>
    /// <returns>The rows in sheet order.</returns>
    private static IEnumerable<RawRow> EnumerateWorksheetRows(IExcelDataReader reader, HashSet<(int Row, int Column)> errorCells)
    {
        int rowNumber = 0;
        while (reader.Read())
        {
            rowNumber++;
            var cells = new string[reader.FieldCount];
            CellIssue?[]? issues = null;   // allocated only for the rare row that has a bad cell
            for (int i = 0; i < cells.Length; i++)
            {
                var (text, issue) = ReadCell(reader.GetValue(i), reader.GetCellError(i),
                    errorCells.Count > 0 && errorCells.Contains((rowNumber, i)));
                cells[i] = text;
                if (issue is not null)
                    (issues ??= new CellIssue?[cells.Length])[i] = issue;
            }
            yield return new RawRow(rowNumber, cells, issues);
        }
    }

    /// <summary>
    /// Reads one worksheet cell: its text (see <see cref="CellToText"/>) and, when the cell cannot be used as a
    /// value, what is wrong with it. A cell that shows an Excel error is an <see cref="CellIssueKind.ExcelError"/>
    /// (shown the way Excel shows it, or with no text when the library cannot name the error). A date (any date
    /// except Excel's time-only dates, which are times such as 09:00) is a <see cref="CellIssueKind.Date"/>,
    /// shown as <c>2 Jan 2026</c>: it is what Excel makes of a section code like <c>1-2</c> typed into a column
    /// that is not formatted as Text. A cell with an issue has empty text, so nothing downstream reads an error or
    /// a date as something the user typed.
    /// </summary>
    /// <param name="value">The value ExcelDataReader returned for the cell; may be null.</param>
    /// <param name="error">The cell's Excel error from <see cref="IExcelDataReader.GetCellError"/>; null when it holds none.</param>
    /// <param name="isMarkedAsError">
    /// True when the sheet's own XML marks the cell as an error (see <see cref="XlsxErrorCells"/>). The library gives
    /// a null value and no error for an error it has no name for (<c>#SPILL!</c>), so a null value together with this
    /// mark is an error the library lost; it is reported without a name.
    /// </param>
    /// <returns>The cell's text (never null) and its issue (null when the cell is fine).</returns>
    internal static (string Text, CellIssue? Issue) ReadCell(object? value, CellError? error, bool isMarkedAsError = false)
    {
        if (error is { } code)
            return (string.Empty, new CellIssue(CellIssueKind.ExcelError, ErrorText(code)));
        if (isMarkedAsError && value is null)
            return (string.Empty, new CellIssue(CellIssueKind.ExcelError, null));
        if (value is DateTime dateTime && !IsTimeOnlyDate(dateTime))
            return (string.Empty, new CellIssue(CellIssueKind.Date, dateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture)));
        return (CellToText(value), null);
    }

    /// <summary>
    /// The text Excel shows for an error the library names.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The text, such as <c>#N/A</c>; null for an error this method does not know.</returns>
    private static string? ErrorText(CellError error) => error switch
    {
        CellError.NULL => "#NULL!",
        CellError.DIV0 => "#DIV/0!",
        CellError.VALUE => "#VALUE!",
        CellError.REF => "#REF!",
        CellError.NAME => "#NAME?",
        CellError.NUM => "#NUM!",
        CellError.NA => "#N/A",
        CellError.GETTING_DATA => "#GETTING_DATA",
        _ => null,
    };

    // ── .csv ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a CSV: decodes it, joins physical lines into records with <see cref="CsvRecordReader"/>
    /// (so quoted line breaks are handled and each record knows its starting line) and splits each
    /// record into fields with <see cref="SharedScheduleCsvParser.ParseCsvRow"/>.
    /// </summary>
    /// <param name="content">The CSV bytes.</param>
    /// <param name="displayName">The file's display name.</param>
    /// <returns>The sheet, or the problem (including an unclosed quotation mark, with its line).</returns>
    private static ImportReadResult ReadCsv(byte[] content, string displayName)
    {
        try
        {
            var reader = new CsvRecordReader(new StringReader(DecodeCsv(content)));
            return BuildSheet(EnumerateCsvRows(reader), displayName);
        }
        catch (CsvFormatException ex)
        {
            return ImportReadResult.Failure(
                $"Line {ex.LineNumber} has a quotation mark that is never closed, so TermPoint can't tell where that row ends.");
        }
    }

    /// <summary>
    /// Decodes CSV bytes into text. A byte-order mark decides first: <c>FF FE</c> is UTF-16 little-endian (what
    /// Excel's "Unicode Text" writes) and <c>FE FF</c> is UTF-16 big-endian; a UTF-8 mark (<c>EF BB BF</c>) is
    /// removed and the rest read as UTF-8. With no mark the bytes are read as UTF-8, and when they are not valid
    /// UTF-8 as Windows-1252 instead: Excel's plain "CSV (Comma delimited)" on Windows writes that code page, so
    /// its typographic apostrophes, quotation marks and dashes (bytes 0x80 to 0x9F, which Latin-1 would turn into
    /// invisible control characters) come through as the characters the user typed. The mark is never part of
    /// the returned text. Bad bytes are replaced rather than thrown on, so this never throws for bad input.
    /// </summary>
    /// <param name="content">The CSV bytes.</param>
    /// <returns>The decoded text.</returns>
    internal static string DecodeCsv(byte[] content)
    {
        ReadOnlySpan<byte> bytes = content;
        ReadOnlySpan<byte> utf16LittleEndianBom = [0xFF, 0xFE];
        ReadOnlySpan<byte> utf16BigEndianBom = [0xFE, 0xFF];
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];

        if (bytes.StartsWith(utf16LittleEndianBom))
            return Encoding.Unicode.GetString(bytes[utf16LittleEndianBom.Length..]);
        if (bytes.StartsWith(utf16BigEndianBom))
            return Encoding.BigEndianUnicode.GetString(bytes[utf16BigEndianBom.Length..]);
        if (bytes.StartsWith(utf8Bom))
            bytes = bytes[utf8Bom.Length..];

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Code page 1252 is available because the static constructor registered the code-pages provider.
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>
    /// Lazily yields the records of a CSV as rows, flagging any cell whose whole text is an Excel error
    /// (<see cref="CsvCellIssue"/>). Throws <see cref="CsvFormatException"/> while being enumerated when a
    /// quoted field is never closed.
    /// </summary>
    /// <param name="reader">The record reader over the decoded text.</param>
    /// <returns>The records in file order; each row number is the line the record starts on.</returns>
    private static IEnumerable<RawRow> EnumerateCsvRows(CsvRecordReader reader)
    {
        while (reader.ReadRecord() is { } record)
        {
            var cells = SharedScheduleCsvParser.ParseCsvRow(record.Text);
            CellIssue?[]? issues = null;   // allocated only for the rare record that has an error cell
            for (int i = 0; i < cells.Length; i++)
            {
                if (CsvCellIssue(cells[i]) is not { } issue)
                    continue;
                (issues ??= new CellIssue?[cells.Length])[i] = issue;
                cells[i] = string.Empty;   // a cell with an issue reads as empty, as in a workbook
            }
            yield return new RawRow(record.LineNumber, cells, issues);
        }
    }

    /// <summary>
    /// The issue of one CSV field: Excel writes a cell that shows an error into a CSV as its literal text, so a
    /// field whose whole trimmed text is one of Excel's error literals (compared ignoring case) is an
    /// <see cref="CellIssueKind.ExcelError"/>. Text that merely contains such a literal is not. A CSV holds only
    /// text, so it can never have a date issue.
    /// </summary>
    /// <param name="field">The field's text.</param>
    /// <returns>The issue, or null when the field is not an error literal.</returns>
    private static CellIssue? CsvCellIssue(string field)
    {
        var text = field.Trim();
        return text.Length > 1 && text[0] == '#' && ExcelErrorLiterals.TryGetValue(text, out var shown)
            ? new CellIssue(CellIssueKind.ExcelError, shown)
            : null;
    }

    // ── Shared: header check and row building ─────────────────────────────────

    /// <summary>
    /// Turns raw rows (header first) into an <see cref="ImportSheet"/> in one pass: validates the header,
    /// then builds an <see cref="ImportRow"/> for each non-blank row, stopping as soon as the row limit is passed.
    /// </summary>
    /// <param name="rawRows">The file's rows in order; the first is the header row.</param>
    /// <param name="displayName">The file's display name.</param>
    /// <returns>The sheet, or a header or limit problem. Exceptions from enumerating <paramref name="rawRows"/> propagate.</returns>
    private static ImportReadResult BuildSheet(IEnumerable<RawRow> rawRows, string displayName)
    {
        using var enumerator = rawRows.GetEnumerator();

        // A file with no rows at all has no header; MapHeaders then reports that no template column was found.
        IReadOnlyList<string> headerCells = enumerator.MoveNext() ? enumerator.Current.Cells : Array.Empty<string>();

        int[]? sourceIndexes = MapHeaders(headerCells, out string? headerProblem);
        if (sourceIndexes is null)
            return ImportReadResult.Failure(headerProblem!);

        var rows = new List<ImportRow>();
        while (enumerator.MoveNext())
        {
            var raw = enumerator.Current;
            if (IsBlank(raw, sourceIndexes))
                continue;

            // Stop reading the moment the limit is passed; the rest of the file is never looked at.
            if (rows.Count >= MaxDataRows)
                return ImportReadResult.Failure(TooLargeMessage);

            rows.Add(new ImportRow(raw.RowNumber, ToCellMap(raw.Cells, sourceIndexes), ToIssueMap(raw, sourceIndexes)));
        }

        return ImportReadResult.Success(new ImportSheet(displayName, rows));
    }

    /// <summary>
    /// Matches the header cells to the template columns (case and spacing ignored, any order). Blank
    /// headers and headers that are not template columns are ignored, so users can keep their own columns.
    /// </summary>
    /// <param name="headerCells">The header row's cells as text.</param>
    /// <param name="problem">
    /// Set when the header is unusable: a template column that appears twice, or one or more that are
    /// missing (one message naming all of them, in template order), or none of the template columns
    /// found at all (<see cref="NoTemplateHeaderMessage"/>). Null on success.
    /// </param>
    /// <returns>
    /// For each template column in <see cref="SectionImportColumns.All"/> order, the index of its cell in a row;
    /// or null when <paramref name="problem"/> is set.
    /// </returns>
    private static int[]? MapHeaders(IReadOnlyList<string> headerCells, out string? problem)
    {
        var columns = SectionImportColumns.All;
        var indexByName = new Dictionary<string, int>(NameMatch.Comparer);   // canonical name -> position in All
        for (int i = 0; i < columns.Count; i++)
            indexByName[columns[i].Name] = i;

        var sourceIndexes = new int[columns.Count];
        Array.Fill(sourceIndexes, -1);
        var duplicated = new bool[columns.Count];

        for (int cell = 0; cell < headerCells.Count; cell++)
        {
            string header = NameMatch.Normalize(headerCells[cell]);
            if (header.Length == 0 || !indexByName.TryGetValue(header, out int column))
                continue;   // blank or the user's own column

            if (sourceIndexes[column] >= 0)
                duplicated[column] = true;
            else
                sourceIndexes[column] = cell;
        }

        var duplicateMessages = Enumerable.Range(0, columns.Count)
            .Where(c => duplicated[c])
            .Select(c => $"The {columns[c].Name} column appears more than once.")
            .ToList();
        if (duplicateMessages.Count > 0)
        {
            problem = string.Join(" ", duplicateMessages);
            return null;
        }

        var missing = Enumerable.Range(0, columns.Count)
            .Where(c => sourceIndexes[c] < 0)
            .Select(c => columns[c].Name)
            .ToList();
        if (missing.Count == columns.Count)
        {
            // Nothing matched: listing all 18 names would not help; the row is not a template header at all.
            problem = NoTemplateHeaderMessage;
            return null;
        }

        if (missing.Count > 0)
        {
            problem = missing.Count == 1
                ? $"The {missing[0]} column is missing."
                : $"These columns are missing: {string.Join(", ", missing)}.";
            return null;
        }

        problem = null;
        return sourceIndexes;
    }

    /// <summary>
    /// True when every template-column cell of the row is empty or whitespace and none has an issue. Cells in
    /// the user's own columns are ignored, so a row holding only a note is blank. A cell showing an Excel error
    /// or a date has empty text but is not blank: it is content the user has to deal with, so a row holding only
    /// such cells is kept (and reported) rather than silently dropped.
    /// </summary>
    /// <param name="raw">The row.</param>
    /// <param name="sourceIndexes">The template columns' positions, from <see cref="MapHeaders"/>.</param>
    /// <returns>True when the row has no content in any template column.</returns>
    private static bool IsBlank(RawRow raw, int[] sourceIndexes) =>
        sourceIndexes.All(index =>
            index >= raw.Cells.Count
            || (string.IsNullOrWhiteSpace(raw.Cells[index]) && raw.Issues?[index] is null));

    /// <summary>
    /// Builds the canonical-name-to-text map of one row. A row shorter than the header gets empty strings
    /// for the cells it does not have; the values are otherwise exactly as read (not trimmed).
    /// </summary>
    /// <param name="cells">The row's cells.</param>
    /// <param name="sourceIndexes">The template columns' positions, from <see cref="MapHeaders"/>.</param>
    /// <returns>A map with an entry for every template column.</returns>
    private static Dictionary<string, string> ToCellMap(IReadOnlyList<string> cells, int[] sourceIndexes)
    {
        var columns = SectionImportColumns.All;
        var map = new Dictionary<string, string>(columns.Count, NameMatch.Comparer);
        for (int c = 0; c < columns.Count; c++)
        {
            int index = sourceIndexes[c];
            map[columns[c].Name] = index < cells.Count ? cells[index] : string.Empty;
        }
        return map;
    }

    /// <summary>
    /// Collects the issues of one row's template-column cells, keyed by canonical template column name (the
    /// user's own columns are never read, so their issues are ignored).
    /// </summary>
    /// <param name="raw">The row.</param>
    /// <param name="sourceIndexes">The template columns' positions, from <see cref="MapHeaders"/>.</param>
    /// <returns>The map, or null when no template-column cell of the row has an issue.</returns>
    private static Dictionary<string, CellIssue>? ToIssueMap(RawRow raw, int[] sourceIndexes)
    {
        if (raw.Issues is null)
            return null;

        var columns = SectionImportColumns.All;
        Dictionary<string, CellIssue>? map = null;
        for (int c = 0; c < columns.Count; c++)
        {
            int index = sourceIndexes[c];
            if (index < raw.Issues.Count && raw.Issues[index] is { } issue)
                (map ??= new Dictionary<string, CellIssue>(NameMatch.Comparer))[columns[c].Name] = issue;
        }
        return map;
    }

    // ── Cell to text ──────────────────────────────────────────────────────────

    /// <summary>
    /// True when a date-time is one of Excel's time-only dates: what Excel gives a cell that holds a time with
    /// no date (a time of day such as 09:00). Any other date is a calendar date.
    /// </summary>
    /// <param name="dateTime">The cell's date-time.</param>
    /// <returns>True for a time with no date.</returns>
    private static bool IsTimeOnlyDate(DateTime dateTime) =>
        dateTime.Date == ExcelTimeOnlyDate1 || dateTime.Date == ExcelTimeOnlyDate2;

    /// <summary>
    /// Turns one worksheet cell value into the text a CSV would hold for it, so .xlsx and CSV look the same
    /// to everything downstream:
    /// null or empty gives an empty string; text is kept as is; a whole number becomes plain integer text
    /// (<c>101</c>, never <c>101.0</c> or <c>1E+3</c>); any other number becomes its shortest round-trip text
    /// (<c>1.5</c>); a time with no date becomes <c>HH:mm</c> (or <c>HH:mm:ss</c> when the seconds are not zero,
    /// after rounding to the nearest second, since Excel's binary fractions can give 09:59:59.9999);
    /// any other date becomes <c>yyyy-MM-dd</c> (plus <c> HH:mm</c> when it has a time of day); a boolean
    /// becomes <c>TRUE</c> or <c>FALSE</c>. Everything else uses invariant <see cref="Convert.ToString(object, IFormatProvider)"/>.
    /// All numbers and dates are formatted with the invariant culture. (The reader itself never turns a calendar
    /// date into text: <see cref="ReadCell"/> records it as an issue first, so only times reach the date rules here.)
    /// </summary>
    /// <param name="value">The value ExcelDataReader returned for the cell; may be null.</param>
    /// <returns>The cell's text; never null.</returns>
    internal static string CellToText(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case double number:
                return NumberToText(number);
            case DateTime dateTime:
                return DateTimeToText(dateTime);
            case bool flag:
                return flag ? "TRUE" : "FALSE";
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    /// <summary>Formats a number: whole numbers as integer text, others as shortest round-trip text, both invariant.</summary>
    /// <param name="number">The cell's number.</param>
    /// <returns>The invariant text.</returns>
    private static string NumberToText(double number)
    {
        if (number == 0)
            return "0";   // also covers negative zero

        bool isWhole = double.IsFinite(number) && number == Math.Floor(number);
        return isWhole
            ? number.ToString("F0", CultureInfo.InvariantCulture)
            : number.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a date or time cell (see <see cref="CellToText"/> for the rules).</summary>
    /// <param name="dateTime">The cell's date-time.</param>
    /// <returns>The invariant text.</returns>
    private static string DateTimeToText(DateTime dateTime)
    {
        if (IsTimeOnlyDate(dateTime))
        {
            // A time with no date. Work in whole seconds, wrapping at midnight, so 23:59:59.9999 becomes 00:00.
            long totalSeconds = (dateTime.TimeOfDay.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond;
            totalSeconds %= 24 * 60 * 60;
            long hours = totalSeconds / 3600;
            long minutes = totalSeconds % 3600 / 60;
            long seconds = totalSeconds % 60;
            return seconds == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}");
        }

        return dateTime.TimeOfDay == TimeSpan.Zero
            ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
#endif

#if !BROWSER
using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Reads a section import file (an <c>.xlsx</c> workbook or a <c>.csv</c>) into an
/// <see cref="ImportSheet"/>: checks the header row against <see cref="SectionImportColumns"/>,
/// turns every cell into text so both formats look the same to the planner, drops blank rows and
/// enforces the file limits.
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

    /// <summary>One row as read from the file, before the header is applied: its row number and every cell as text.</summary>
    private readonly record struct RawRow(int RowNumber, IReadOnlyList<string> Cells);

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

            return BuildSheet(EnumerateWorksheetRows(reader), displayName);
        }
        catch (Exception)
        {
            return ImportReadResult.Failure(UnreadableWorkbookMessage);
        }
    }

    /// <summary>
    /// Lazily yields the rows of the sheet the reader is positioned on, each cell turned into text by
    /// <see cref="CellToText"/>. ExcelDataReader yields an empty row for every row number Excel does not
    /// store, so counting calls to <c>Read()</c> gives the row number Excel shows (the header is row 1).
    /// </summary>
    /// <param name="reader">An open reader positioned on the sheet to read.</param>
    /// <returns>The rows in sheet order.</returns>
    private static IEnumerable<RawRow> EnumerateWorksheetRows(IExcelDataReader reader)
    {
        int rowNumber = 0;
        while (reader.Read())
        {
            rowNumber++;
            var cells = new string[reader.FieldCount];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = CellToText(reader.GetValue(i));
            yield return new RawRow(rowNumber, cells);
        }
    }

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
    /// Decodes CSV bytes as UTF-8 (a byte-order mark is honoured and removed). Bytes that are not valid
    /// UTF-8 are decoded as Latin-1 instead: Excel's plain "CSV" save uses the Windows code page, and
    /// Latin-1 keeps accented names readable without the code-pages package.
    /// </summary>
    /// <param name="content">The CSV bytes.</param>
    /// <returns>The decoded text.</returns>
    internal static string DecodeCsv(byte[] content)
    {
        ReadOnlySpan<byte> bytes = content;
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        if (bytes.StartsWith(utf8Bom))
            bytes = bytes[utf8Bom.Length..];

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>
    /// Lazily yields the records of a CSV as rows. Throws <see cref="CsvFormatException"/> while being
    /// enumerated when a quoted field is never closed.
    /// </summary>
    /// <param name="reader">The record reader over the decoded text.</param>
    /// <returns>The records in file order; each row number is the line the record starts on.</returns>
    private static IEnumerable<RawRow> EnumerateCsvRows(CsvRecordReader reader)
    {
        while (reader.ReadRecord() is { } record)
            yield return new RawRow(record.LineNumber, SharedScheduleCsvParser.ParseCsvRow(record.Text));
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
            if (IsBlank(raw.Cells, sourceIndexes))
                continue;

            // Stop reading the moment the limit is passed; the rest of the file is never looked at.
            if (rows.Count >= MaxDataRows)
                return ImportReadResult.Failure(TooLargeMessage);

            rows.Add(new ImportRow(raw.RowNumber, ToCellMap(raw.Cells, sourceIndexes)));
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
    /// True when every template-column cell of the row is empty or whitespace. Cells in the user's own
    /// columns are ignored, so a row holding only a note is blank.
    /// </summary>
    /// <param name="cells">The row's cells.</param>
    /// <param name="sourceIndexes">The template columns' positions, from <see cref="MapHeaders"/>.</param>
    /// <returns>True when the row has no content in any template column.</returns>
    private static bool IsBlank(IReadOnlyList<string> cells, int[] sourceIndexes) =>
        sourceIndexes.All(index => index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]));

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

    // ── Cell to text ──────────────────────────────────────────────────────────

    /// <summary>
    /// Turns one worksheet cell value into the text a CSV would hold for it, so .xlsx and CSV look the same
    /// to everything downstream:
    /// null or empty gives an empty string; text is kept as is; a whole number becomes plain integer text
    /// (<c>101</c>, never <c>101.0</c> or <c>1E+3</c>); any other number becomes its shortest round-trip text
    /// (<c>1.5</c>); a time with no date becomes <c>HH:mm</c> (or <c>HH:mm:ss</c> when the seconds are not zero,
    /// after rounding to the nearest second, since Excel's binary fractions can give 09:59:59.9999);
    /// any other date becomes <c>yyyy-MM-dd</c> (plus <c> HH:mm</c> when it has a time of day); a boolean
    /// becomes <c>TRUE</c> or <c>FALSE</c>. Everything else uses invariant <see cref="Convert.ToString(object, IFormatProvider)"/>.
    /// All numbers and dates are formatted with the invariant culture.
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
        if (dateTime.Date == ExcelTimeOnlyDate1 || dateTime.Date == ExcelTimeOnlyDate2)
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

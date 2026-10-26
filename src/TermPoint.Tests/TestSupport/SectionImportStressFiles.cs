using System.Globalization;
using System.Text;
using TermPoint.Services.SectionImport;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests.TestSupport;

/// <summary>The file formats a <see cref="StressFile"/> can be written in.</summary>
public enum StressFormat
{
    /// <summary>An Excel workbook (<c>.xlsx</c>), built by <see cref="XlsxTestWorkbook"/>.</summary>
    Xlsx,

    /// <summary>A comma-separated (or, with <see cref="StressFile.Delimiter"/>, otherwise separated) text file.</summary>
    Csv,
}

/// <summary>The text encodings a stress CSV can be written in (what Excel's "Save As" variants produce, and one it does not).</summary>
internal enum StressCsvEncoding
{
    /// <summary>UTF-8 with a byte-order mark: Excel's "CSV UTF-8".</summary>
    Utf8WithBom,

    /// <summary>UTF-8 without a byte-order mark: most scripts and tools.</summary>
    Utf8,

    /// <summary>UTF-16 little-endian with a byte-order mark: Excel's "Unicode Text" (normally tab separated), here with the file's delimiter.</summary>
    Utf16LeWithBom,

    /// <summary>UTF-16 big-endian with a byte-order mark (<c>FE FF</c>): not written by Excel, but a legal UTF-16 file.</summary>
    Utf16BeWithBom,

    /// <summary>The Windows ANSI code page 1252: Excel's plain "CSV" on a Western Windows machine. Characters it lacks become <c>?</c>.</summary>
    Windows1252,
}

/// <summary>How a begin or end time is written into a cell.</summary>
internal enum StressTimeStyle
{
    /// <summary>Four-digit text, <c>1000</c> (what the template's Text-formatted columns hold).</summary>
    Text4Digit,

    /// <summary>Text with a colon, <c>10:00</c>.</summary>
    TextColon,

    /// <summary>A time cell: a <see cref="TimeSpan"/>, which <see cref="XlsxTestWorkbook"/> writes as a numeric time (what Excel stores when a user types <c>10:00</c>); a CSV gets <c>HH:mm</c>.</summary>
    ExcelTime,
}

/// <summary>
/// One column of a stress file as written: the header text exactly as the file shows it, and where its cells
/// come from. A column with a <see cref="Key"/> takes its cell from the row's entry for that template column;
/// a column without one (a user's own notes column, say) holds the same <see cref="Constant"/> on every row.
/// </summary>
/// <param name="HeaderText">The header cell's text, spacing and case included (<c>"  term "</c> is allowed).</param>
/// <param name="Key">The template column (see <see cref="SectionImportColumns"/>) whose value fills the cells, or null.</param>
/// <param name="Constant">The value of every cell of a column with no key.</param>
internal sealed record StressColumn(string HeaderText, string? Key, object? Constant = null);

/// <summary>
/// One meeting pattern of a <see cref="ImportSectionSpec"/>: some days sharing one time, one frequency,
/// meeting type and room, written as one file row.
/// </summary>
/// <param name="Days">The day numbers (1 = Monday ... 7 = Sunday).</param>
/// <param name="Begin">Begin, in minutes from midnight.</param>
/// <param name="End">End, in minutes from midnight.</param>
/// <param name="Frequency">The frequency as stored and as written: null (weekly), <c>odd</c>, <c>even</c> or <c>1,6,7</c>.</param>
/// <param name="MeetingType">The meeting type name, or null.</param>
/// <param name="Building">The room's building, or null (written blank).</param>
/// <param name="Room">The room number, or null for no room.</param>
internal sealed record StressMeeting(
    IReadOnlyList<int> Days, int Begin, int End,
    string? Frequency = null, string? MeetingType = null, string? Building = null, string? Room = null)
{
    /// <summary>The meeting as the importer should store it: one entry per day.</summary>
    /// <returns>(day, start, duration, frequency, meeting type, building, room) for every day.</returns>
    public IEnumerable<(int Day, int Start, int Duration, string? Frequency, string? MeetingType, string? Building, string? Room)> PerDay() =>
        Days.Select(d => (d, Begin, End - Begin, Frequency, MeetingType, Building, Room));
}

/// <summary>
/// A section as a person would describe it, in the importer's own terms: the course, the code, what is
/// attached to it, and its meeting patterns. It is the neutral form used three ways: the generators build
/// specs and <see cref="ToRows"/> turns them into file rows; the exporter turns real sections into specs; and
/// <see cref="StressVerifier"/> checks that what the importer wrote matches a spec.
/// </summary>
internal sealed class ImportSectionSpec
{
    /// <summary>The names of the days in the form the template uses; index 0 is unused.</summary>
    private static readonly string[] DayText = ["", "M", "T", "W", "Th", "F", "Sa", "Su"];

    /// <summary>The Subject cell.</summary>
    public required string Subject { get; init; }

    /// <summary>The Course Number cell.</summary>
    public required string CourseNumber { get; init; }

    /// <summary>The Course Title cell.</summary>
    public string? CourseTitle { get; init; }

    /// <summary>The Section Code cell.</summary>
    public required string SectionCode { get; init; }

    /// <summary>The Section Type cell, or null for blank.</summary>
    public string? SectionType { get; init; }

    /// <summary>The instructors, each <c>Last, First</c>.</summary>
    public IReadOnlyList<string> Instructors { get; init; } = Array.Empty<string>();

    /// <summary>The tag names.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>The Capacity cell as a number, or null for blank.</summary>
    public int? Capacity { get; init; }

    /// <summary>The Campus cell, or null for blank.</summary>
    public string? Campus { get; init; }

    /// <summary>The meeting patterns; none means a section with no meeting time.</summary>
    public IReadOnlyList<StressMeeting> Meetings { get; init; } = Array.Empty<StressMeeting>();

    /// <summary>The section's key as the importer labels it (<c>SML101 A</c>).</summary>
    public string Label => $"{Subject}{CourseNumber} {SectionCode}";

    /// <summary>A copy of this section with another section code (so it is a different section of the same course).</summary>
    /// <param name="code">The new section code.</param>
    /// <returns>The copy.</returns>
    public ImportSectionSpec WithCode(string code) => new()
    {
        Subject = Subject, CourseNumber = CourseNumber, CourseTitle = CourseTitle, SectionCode = code,
        SectionType = SectionType, Instructors = Instructors, Tags = Tags, Capacity = Capacity, Campus = Campus,
        Meetings = Meetings,
    };

    /// <summary>
    /// A day list as the template writes it: comma-separated names (<c>M,W,Th</c>).
    /// </summary>
    /// <param name="days">The day numbers.</param>
    /// <returns>The Meeting Days cell text.</returns>
    public static string DaysText(IEnumerable<int> days) => string.Join(",", days.Select(d => DayText[d]));

    /// <summary>
    /// A time as a cell value in the requested style.
    /// </summary>
    /// <param name="minutes">Minutes from midnight.</param>
    /// <param name="style">How to write it.</param>
    /// <returns>A string or a <see cref="TimeSpan"/>.</returns>
    public static object TimeCell(int minutes, StressTimeStyle style) => style switch
    {
        StressTimeStyle.TextColon => $"{minutes / 60:00}:{minutes % 60:00}",
        StressTimeStyle.ExcelTime => TimeSpan.FromMinutes(minutes),
        _ => $"{minutes / 60:00}{minutes % 60:00}",
    };

    /// <summary>
    /// The file rows for this section: one per meeting pattern (one with blank meeting cells when there are
    /// none). The section's details go on the first row only, as the template's sample does, unless
    /// <paramref name="repeatSectionCells"/> is set.
    /// </summary>
    /// <param name="timeStyle">How Begin and End are written.</param>
    /// <param name="repeatSectionCells">Repeat the section-level cells on every row.</param>
    /// <returns>One dictionary per row, keyed by template column; the Academic Year and Term are not set.</returns>
    public List<Dictionary<string, object?>> ToRows(StressTimeStyle timeStyle = StressTimeStyle.Text4Digit, bool repeatSectionCells = false)
    {
        var rows = new List<Dictionary<string, object?>>();
        var patterns = Meetings.Count == 0 ? new StressMeeting?[] { null } : Meetings.Cast<StressMeeting?>().ToArray();
        for (int i = 0; i < patterns.Length; i++)
        {
            var row = new Dictionary<string, object?>
            {
                [C.Subject] = Subject,
                [C.CourseNumber] = CourseNumber,
                [C.SectionCode] = SectionCode,
            };
            if (i == 0 || repeatSectionCells)
            {
                row[C.CourseTitle] = CourseTitle;
                row[C.SectionType] = SectionType;
                row[C.Instructors] = Instructors.Count == 0 ? null : string.Join("; ", Instructors);
                row[C.Tags] = Tags.Count == 0 ? null : string.Join("; ", Tags);
                row[C.Capacity] = Capacity;
                row[C.Campus] = Campus;
            }

            if (patterns[i] is { } m)
            {
                row[C.MeetingDays] = DaysText(m.Days);
                row[C.Begin] = TimeCell(m.Begin, timeStyle);
                row[C.End] = TimeCell(m.End, timeStyle);
                row[C.Frequency] = m.Frequency;
                row[C.MeetingType] = m.MeetingType;
                row[C.Building] = m.Building;
                row[C.Room] = m.Room;
            }
            rows.Add(row);
        }
        return rows;
    }
}

/// <summary>
/// A file for the section import, held as data so that one description can be written as an <c>.xlsx</c> or
/// a CSV in any encoding, and bent out of shape in the ways real files are: other column orders, extra
/// columns, title rows above the header, blank and hidden rows, a Sections sheet that is not the first.
/// Rows are dictionaries keyed by template column (an absent key is a blank cell); how they are laid out is
/// decided by <see cref="Layout"/> when the file is written.
/// </summary>
internal sealed class StressFile
{
    /// <summary>The template's column names in template order.</summary>
    public static IReadOnlyList<string> TemplateColumnNames { get; } = C.All.Select(c => c.Name).ToArray();

    /// <summary>Registers the Windows code pages so Windows-1252 can be used (the importer's reader does the same).</summary>
    static StressFile() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Creates an empty file for a semester, laid out as the template.</summary>
    /// <param name="academicYear">The Academic Year every row names.</param>
    /// <param name="term">The Term every row names.</param>
    public StressFile(string academicYear = "2026-2027", string term = "Fall")
    {
        AcademicYear = academicYear;
        Term = term;
        Layout = TemplateColumnNames.Select(n => new StressColumn(n, n)).ToList();
    }

    /// <summary>The Academic Year new rows name.</summary>
    public string AcademicYear { get; private set; }

    /// <summary>The Term new rows name.</summary>
    public string Term { get; private set; }

    /// <summary>The columns as written, in order. Defaults to the template's columns in template order.</summary>
    public List<StressColumn> Layout { get; set; }

    /// <summary>The data rows, in file order. An empty dictionary is a blank row.</summary>
    public List<Dictionary<string, object?>> Rows { get; } = new();

    // ── Format ────────────────────────────────────────────────────────────────

    /// <summary>The format <see cref="ToBytes"/> writes.</summary>
    public StressFormat Format { get; set; } = StressFormat.Xlsx;

    /// <summary>The extension that goes with <see cref="Format"/>, with its dot.</summary>
    public string Extension => Format == StressFormat.Xlsx ? ".xlsx" : ".csv";

    // ── .xlsx options ─────────────────────────────────────────────────────────

    /// <summary>The name of the sheet holding the rows.</summary>
    public string SheetName { get; set; } = "Sections";

    /// <summary>Sheets written before the data sheet (each a name and its rows), so the data sheet is not the first.</summary>
    public List<(string Name, object?[][] Rows)> SheetsBefore { get; } = new();

    /// <summary>Rows written above the header row (for a CSV, lines above it).</summary>
    public List<object?[]> TitleRows { get; } = new();

    /// <summary>How many rows are written after the data, with every cell <see cref="TrailingBlankCell"/>.</summary>
    public int TrailingBlankRows { get; set; }

    /// <summary>The value in every cell of a trailing blank row: null (no cells), an empty string, or spaces.</summary>
    public object? TrailingBlankCell { get; set; }

    /// <summary>Indexes (into <see cref="Rows"/>, from 0) of data rows written hidden.</summary>
    public HashSet<int> HiddenDataRows { get; } = new();

    /// <summary>Put an auto-filter over the header and data rows.</summary>
    public bool AutoFilter { get; set; }

    // ── CSV options ───────────────────────────────────────────────────────────

    /// <summary>The encoding of a CSV.</summary>
    public StressCsvEncoding CsvEncoding { get; set; } = StressCsvEncoding.Utf8;

    /// <summary>The field separator of a CSV.</summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>The line break between the lines of a CSV.</summary>
    public string LineBreak { get; set; } = "\r\n";

    // ── Rows ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a row naming this file's semester, with the given cells.
    /// </summary>
    /// <param name="cells">(template column, value) pairs; they override the semester cells.</param>
    /// <returns>The row, so a caller can adjust it.</returns>
    public Dictionary<string, object?> AddRow(params (string Column, object? Value)[] cells)
    {
        var row = new Dictionary<string, object?> { [C.AcademicYear] = AcademicYear, [C.Term] = Term };
        foreach (var (column, value) in cells)
            row[column] = value;
        Rows.Add(row);
        return row;
    }

    /// <summary>Adds a blank row.</summary>
    public void AddBlankRow() => Rows.Add(new Dictionary<string, object?>());

    /// <summary>
    /// Adds the rows of a section, naming this file's semester.
    /// </summary>
    /// <param name="spec">The section.</param>
    /// <param name="timeStyle">How Begin and End are written.</param>
    /// <param name="repeatSectionCells">Repeat the section-level cells on every row of the section.</param>
    /// <returns>The index (into <see cref="Rows"/>) of the section's first row.</returns>
    public int AddSection(ImportSectionSpec spec, StressTimeStyle timeStyle = StressTimeStyle.Text4Digit, bool repeatSectionCells = false)
    {
        int first = Rows.Count;
        foreach (var cells in spec.ToRows(timeStyle, repeatSectionCells))
        {
            var row = AddRow();
            foreach (var (column, value) in cells)
                row[column] = value;
        }
        return first;
    }

    /// <summary>Names another semester in every non-blank row, and in rows added after.</summary>
    /// <param name="academicYear">The Academic Year.</param>
    /// <param name="term">The Term.</param>
    public void Retarget(string academicYear, string term)
    {
        AcademicYear = academicYear;
        Term = term;
        foreach (var row in Rows.Where(r => r.Count > 0))
        {
            row[C.AcademicYear] = academicYear;
            row[C.Term] = term;
        }
    }

    /// <summary>
    /// The row number Excel shows for a data row of an <c>.xlsx</c> (for a CSV it is the line number, as long
    /// as no field holds a line break): the header is the row after the title rows.
    /// </summary>
    /// <param name="dataRowIndex">The index into <see cref="Rows"/>, from 0.</param>
    /// <returns>The 1-based row number.</returns>
    public int RowNumberOf(int dataRowIndex) => TitleRows.Count + 2 + dataRowIndex;

    // ── Writing ───────────────────────────────────────────────────────────────

    /// <summary>The cells of one row, in <see cref="Layout"/> order.</summary>
    private object?[] CellsOf(Dictionary<string, object?> row) =>
        Layout.Select(c => row.Count == 0 ? null : c.Key is null ? c.Constant : row.GetValueOrDefault(c.Key)).ToArray();

    /// <summary>The cells of a trailing blank row.</summary>
    private object?[] BlankRowCells() =>
        TrailingBlankCell is null ? new object?[] { null } : Enumerable.Repeat(TrailingBlankCell, Layout.Count).ToArray();

    /// <summary>Writes the file in <see cref="Format"/>.</summary>
    /// <returns>The file's bytes.</returns>
    public byte[] ToBytes() => Format == StressFormat.Xlsx ? ToXlsx() : ToCsv();

    /// <summary>Writes the file into a folder.</summary>
    /// <param name="folder">The folder; it must exist.</param>
    /// <param name="nameWithoutExtension">The file name without its extension (<see cref="Extension"/> is added).</param>
    /// <returns>The file's full path.</returns>
    public string WriteTo(string folder, string nameWithoutExtension)
    {
        var path = Path.Combine(folder, nameWithoutExtension + Extension);
        File.WriteAllBytes(path, ToBytes());
        return path;
    }

    /// <summary>Writes the file as an <c>.xlsx</c> workbook.</summary>
    /// <returns>The workbook's bytes.</returns>
    public byte[] ToXlsx()
    {
        var workbook = new XlsxTestWorkbook();
        foreach (var (name, rows) in SheetsBefore)
        {
            var sheetRows = rows;
            workbook.Sheet(name, s =>
            {
                for (int i = 0; i < sheetRows.Length; i++)
                    s.RowAt(i + 1, sheetRows[i]);
            });
        }

        workbook.Sheet(SheetName, s =>
        {
            int row = 1;
            foreach (var title in TitleRows)
                s.RowAt(row++, title);
            int headerRow = row;
            s.RowAt(row++, Layout.Select(c => (object?)c.HeaderText).ToArray());
            for (int i = 0; i < Rows.Count; i++)
            {
                s.RowAt(row, CellsOf(Rows[i]));
                if (HiddenDataRows.Contains(i))
                    s.HideRow(row);
                row++;
            }
            if (AutoFilter)
                s.AutoFilter($"A{headerRow}:{XlsxTestWorkbook.ColumnLetters(Layout.Count - 1)}{Math.Max(row - 1, headerRow)}");
            for (int i = 0; i < TrailingBlankRows; i++)
                s.RowAt(row++, BlankRowCells());
        });
        return workbook.ToBytes();
    }

    /// <summary>Writes the file as CSV text in <see cref="CsvEncoding"/> with <see cref="Delimiter"/>.</summary>
    /// <returns>The file's bytes.</returns>
    public byte[] ToCsv()
    {
        var sb = new StringBuilder();
        foreach (var title in TitleRows)
            sb.Append(CsvLine(title)).Append(LineBreak);
        sb.Append(CsvLine(Layout.Select(c => (object?)c.HeaderText))).Append(LineBreak);
        foreach (var row in Rows)
            sb.Append(CsvLine(CellsOf(row))).Append(LineBreak);
        for (int i = 0; i < TrailingBlankRows; i++)
            sb.Append(CsvLine(BlankRowCells())).Append(LineBreak);
        return Encode(sb.ToString(), CsvEncoding);
    }

    /// <summary>Joins cells into one CSV record with this file's delimiter.</summary>
    private string CsvLine(IEnumerable<object?> cells) => string.Join(Delimiter, cells.Select(CsvField));

    /// <summary>One cell as a CSV field, quoted when it holds the delimiter, a quote or a line break.</summary>
    private string CsvField(object? value)
    {
        var text = CsvText(value);
        return text.IndexOfAny(new[] { Delimiter, '"', '\r', '\n' }) >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    /// <summary>A cell value as the text a CSV holds for it.</summary>
    private static string CsvText(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "TRUE" : "FALSE",
        TimeSpan t => $"{(int)t.TotalHours:00}:{t.Minutes:00}",
        XlsxTestWorkbook.TimeFraction f => TimeSpan.FromDays(f.Fraction).ToString(@"hh\:mm", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
        XlsxTestWorkbook.ErrorCell e => e.Code,   // Excel writes an error cell into a CSV as its literal text
        double dbl => dbl == Math.Floor(dbl) ? dbl.ToString("F0", CultureInfo.InvariantCulture) : dbl.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>Encodes text as the bytes of a file in the given encoding, with its byte-order mark where it has one.</summary>
    /// <param name="text">The whole file's text.</param>
    /// <param name="encoding">The encoding.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Encode(string text, StressCsvEncoding encoding)
    {
        switch (encoding)
        {
            case StressCsvEncoding.Utf8WithBom:
                return new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
            case StressCsvEncoding.Utf16LeWithBom:
                return Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
            case StressCsvEncoding.Utf16BeWithBom:
                return Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray();
            case StressCsvEncoding.Windows1252:
                return Encoding.GetEncoding(1252).GetBytes(text);
            default:
                return new UTF8Encoding(false).GetBytes(text);
        }
    }
}

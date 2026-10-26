namespace TermPoint.Services.SectionImport;

/// <summary>
/// The kinds of cell the file reader cannot turn into the value the user meant, and so hands to the
/// planner as a <see cref="CellIssue"/> to be reported as a problem instead of being read as text.
/// </summary>
public enum CellIssueKind
{
    /// <summary>The cell shows an Excel error (<c>#N/A</c>, <c>#REF!</c>, <c>#DIV/0!</c>...), as a failed formula leaves behind.</summary>
    ExcelError,

    /// <summary>Excel turned what the user typed (a section code <c>1-2</c>, say) into a date because the column was not formatted as Text.</summary>
    Date,
}

/// <summary>
/// One template-column cell that cannot be used as it stands: what is wrong with it and what it showed. The
/// planner reports each as a problem on its row and column (see <see cref="ImportMessages.CellIssueProblem"/>).
/// </summary>
/// <param name="Kind">What is wrong with the cell.</param>
/// <param name="Shown">
/// What the cell showed, as Excel shows it, for the message: the error text (<c>#N/A</c>) or the date
/// (<c>2 Jan 2026</c>). Null or empty when it is not known (an error the reader cannot name).
/// </param>
public sealed record CellIssue(CellIssueKind Kind, string? Shown);

/// <summary>
/// One data row of an import file, with every template column as text.
/// </summary>
/// <param name="RowNumber">
/// The row number as the user sees it: for a workbook, the row number Excel shows (the header is
/// row 1); for a CSV, the line number on which the record starts.
/// </param>
/// <param name="Cells">
/// The row's cells keyed by canonical template column name (see <see cref="SectionImportColumns"/>).
/// Lookups ignore case and spacing (<see cref="NameMatch.Comparer"/>). Every template column has an
/// entry; the value is an empty string when the file's cell is empty or missing. Values are the raw
/// text after the cell-to-text rules and are not trimmed; the planner normalises them. A cell that has an
/// entry in <paramref name="Issues"/> reads as an empty string here, so nothing downstream mistakes an Excel
/// error or a date for something the user typed.
/// </param>
/// <param name="Issues">
/// The cells of this row that cannot be used (an Excel error, or a date where text was meant), keyed by
/// canonical template column name like <paramref name="Cells"/>; null when the row has none (nearly always).
/// Only template columns appear: the user's own columns are never read. Look one up with
/// <see cref="ImportRowText.Issue"/>.
/// </param>
public sealed record ImportRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> Cells,
    IReadOnlyDictionary<string, CellIssue>? Issues = null);

/// <summary>
/// The rows of an import file that passed the header check, in file order.
/// </summary>
/// <param name="FileName">The file's display name (no folder path).</param>
/// <param name="Rows">The non-blank data rows in file order; empty when the file has only a header row.</param>
public sealed record ImportSheet(string FileName, IReadOnlyList<ImportRow> Rows);

/// <summary>
/// The outcome of reading an import file: either an <see cref="ImportSheet"/> or one plain-English
/// problem that applies to the whole file. Exactly one of <see cref="Sheet"/> and <see cref="Problem"/> is set.
/// </summary>
public sealed class ImportReadResult
{
    /// <summary>The sheet that was read, or null when the file could not be read.</summary>
    public ImportSheet? Sheet { get; }

    /// <summary>The plain-English problem with the file as a whole, or null when it was read.</summary>
    public string? Problem { get; }

    /// <summary>True when the file was read and <see cref="Sheet"/> is set.</summary>
    public bool IsSuccess => Sheet is not null;

    private ImportReadResult(ImportSheet? sheet, string? problem)
    {
        Sheet = sheet;
        Problem = problem;
    }

    /// <summary>Creates a successful result.</summary>
    /// <param name="sheet">The sheet that was read.</param>
    /// <returns>A result whose <see cref="Sheet"/> is <paramref name="sheet"/>.</returns>
    public static ImportReadResult Success(ImportSheet sheet) => new(sheet, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="problem">One plain-English sentence (or two) telling the user what is wrong with the file.</param>
    /// <returns>A result whose <see cref="Problem"/> is <paramref name="problem"/>.</returns>
    public static ImportReadResult Failure(string problem) => new(null, problem);
}

namespace TermPoint.Services.SectionImport;

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
/// text after the cell-to-text rules and are not trimmed; the planner normalises them.
/// </param>
public sealed record ImportRow(int RowNumber, IReadOnlyDictionary<string, string> Cells);

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

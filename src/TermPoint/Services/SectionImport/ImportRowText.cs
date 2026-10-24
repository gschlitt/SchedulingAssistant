namespace TermPoint.Services.SectionImport;

/// <summary>
/// Small helpers for reading text out of <see cref="ImportRow"/>s, shared by the planner steps so
/// nobody repeats the "trim, and treat a missing cell as blank" rule.
/// </summary>
public static class ImportRowText
{
    /// <summary>
    /// The trimmed text of one cell. A column the row has no entry for reads as blank.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">A template column name (see <see cref="SectionImportColumns"/>).</param>
    /// <returns>The trimmed text; empty when the cell is blank or missing.</returns>
    public static string Cell(this ImportRow row, string column) =>
        row.Cells.TryGetValue(column, out var text) ? (text ?? string.Empty).Trim() : string.Empty;

    /// <summary>
    /// Finds the first row, in the order given, whose cell in <paramref name="column"/> is not blank.
    /// </summary>
    /// <param name="rows">The rows to look through, in file order.</param>
    /// <param name="column">A template column name.</param>
    /// <returns>The row and its trimmed text, or null when every row's cell is blank.</returns>
    public static (ImportRow Row, string Text)? FirstNonBlank(IEnumerable<ImportRow> rows, string column)
    {
        foreach (var row in rows)
        {
            var text = row.Cell(column);
            if (text.Length > 0)
                return (row, text);
        }
        return null;
    }
}

namespace TermPoint.Services.SectionImport;

/// <summary>
/// How an <see cref="ImportProblem"/> is written for the user: as one line in the problems list, and as one
/// line of the tab-separated text that "Copy list" puts on the clipboard. Kept apart from the view model so
/// the two formats are defined (and tested) in one place.
/// </summary>
public static class ImportProblemText
{
    /// <summary>The header line of the copied list: the three columns the user pastes into Excel.</summary>
    public const string CopyHeader = "Row\tColumn\tProblem";

    /// <summary>
    /// Separates the lines of the copied list. A carriage return and line feed, so the text pastes as rows
    /// in Excel, in Notepad and in an email on every platform.
    /// </summary>
    public const string CopyLineBreak = "\r\n";

    /// <summary>
    /// One problem as a line of the list: <c>Row 12, Room: message</c>, or <c>Row 12: message</c> when the
    /// problem names no column, or just the message when it is about the file as a whole.
    /// </summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The display line.</returns>
    public static string Line(ImportProblem problem)
    {
        if (problem.Row is null) return problem.Message;
        var where = string.IsNullOrEmpty(problem.Column)
            ? $"Row {problem.Row}"
            : $"Row {problem.Row}, {problem.Column}";
        return $"{where}: {problem.Message}";
    }

    /// <summary>
    /// The characters that make Excel run a pasted cell as a formula when the cell begins with one of them.
    /// </summary>
    private static readonly char[] FormulaStarts = { '=', '+', '-', '@' };

    /// <summary>
    /// The problems as tab-separated text: <see cref="CopyHeader"/>, then one line per problem with the row
    /// (blank for a file-wide problem), the column (blank when none) and the message. Tabs and line breaks
    /// inside a cell become spaces, so one problem is always one line of three cells. No trailing line break.
    ///
    /// <para>The text is meant to be pasted into Excel, which runs any cell that begins with <c>=</c>, <c>+</c>,
    /// <c>-</c> or <c>@</c> as a formula. Many messages begin with the user's own value (a section label, a
    /// person's name, a tag), so a value such as <c>=1+1</c> or <c>-Smith, Pat</c> would be evaluated instead of
    /// shown. Every cell is therefore passed through <see cref="Cell"/>, which puts Excel's own "this is text"
    /// marker (a single apostrophe, which Excel hides on paste) in front of such a cell. It is done for every
    /// cell in that one place so that messages written later are covered too. The lines shown on screen
    /// (<see cref="Line"/>) are not changed.</para>
    /// </summary>
    /// <param name="problems">The problems, in the order shown.</param>
    /// <returns>The text for the clipboard.</returns>
    public static string CopyText(IEnumerable<ImportProblem> problems)
    {
        var lines = new List<string> { CopyHeader };
        foreach (var p in problems)
            lines.Add(string.Join('\t', Cell(p.Row?.ToString()), Cell(p.Column), Cell(p.Message)));
        return string.Join(CopyLineBreak, lines);
    }

    /// <summary>
    /// One cell of the copied list: the text flattened onto one line (<see cref="Flatten"/>) and, when it begins
    /// with a formula character (<c>=</c>, <c>+</c>, <c>-</c> or <c>@</c>), with an apostrophe in front so
    /// Excel keeps it as text.
    /// </summary>
    /// <param name="text">The cell's text; null is treated as empty.</param>
    /// <returns>The text for the cell; an empty cell stays empty and a plain row number is unchanged.</returns>
    private static string Cell(string? text)
    {
        var flat = Flatten(text);
        return flat.Length > 0 && Array.IndexOf(FormulaStarts, flat[0]) >= 0 ? "'" + flat : flat;
    }

    /// <summary>Replaces tabs and line breaks with spaces so a value stays inside its spreadsheet cell.</summary>
    /// <param name="text">The text; null is treated as empty.</param>
    /// <returns>The text on one line.</returns>
    private static string Flatten(string? text) =>
        (text ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}

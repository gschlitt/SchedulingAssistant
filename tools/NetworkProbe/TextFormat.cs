using System.Text;

namespace NetworkProbe;

/// <summary>
/// Small text helpers shared by the console output and the Markdown report, so durations and tables read the same
/// in both. ASCII only, so the output survives any console code page.
/// </summary>
internal static class TextFormat
{
    /// <summary>
    /// Formats a duration as seconds with one decimal, or a placeholder when there is none.
    /// </summary>
    /// <param name="value">The duration, or null when it was not measured.</param>
    /// <param name="none">What to show for null.</param>
    /// <returns>For example <c>5.1 s</c>, or <paramref name="none"/>.</returns>
    public static string Seconds(TimeSpan? value, string none = "-")
        => value is null ? none : $"{value.Value.TotalSeconds:F1} s";

    /// <summary>
    /// Formats a duration as seconds with two decimals (for call-return times, which are usually milliseconds).
    /// </summary>
    /// <param name="value">The duration, or null when it was not measured.</param>
    /// <param name="none">What to show for null.</param>
    /// <returns>For example <c>0.01 s</c>, or <paramref name="none"/>.</returns>
    public static string Seconds2(TimeSpan? value, string none = "-")
        => value is null ? none : $"{value.Value.TotalSeconds:F2} s";

    /// <summary>
    /// Renders rows as a fixed-width plain-text table for the console: a header, a rule, then the rows.
    /// </summary>
    /// <param name="headers">Column titles.</param>
    /// <param name="rows">Rows; each must have as many cells as there are headers.</param>
    /// <returns>The table, one line per row, no trailing newline.</returns>
    public static string ConsoleTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = headers.Select(h => h.Length).ToArray();
        foreach (var row in rows)
            for (var i = 0; i < widths.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        var sb = new StringBuilder();
        AppendRow(sb, headers, widths);
        sb.AppendLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rows)
            AppendRow(sb, row, widths);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Appends one table row, each cell padded to its column width.</summary>
    /// <param name="sb">Where to append.</param>
    /// <param name="cells">The row's cells.</param>
    /// <param name="widths">Column widths.</param>
    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> cells, int[] widths)
        => sb.AppendLine(string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());

    /// <summary>
    /// Renders rows as a GitHub-style Markdown table.
    /// </summary>
    /// <param name="headers">Column titles.</param>
    /// <param name="rows">Rows; each must have as many cells as there are headers.</param>
    /// <returns>The table, one line per row, no trailing newline.</returns>
    public static string MarkdownTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", headers.Select(MarkdownCell)) + " |");
        sb.AppendLine("|" + string.Join("|", headers.Select(_ => "---")) + "|");
        foreach (var row in rows)
            sb.AppendLine("| " + string.Join(" | ", row.Select(MarkdownCell)) + " |");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Makes text safe inside a Markdown table cell (no pipes, no line breaks).</summary>
    /// <param name="text">The cell text.</param>
    /// <returns>The text on one line with pipes escaped.</returns>
    public static string MarkdownCell(string text)
        => text.Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
}

using System.Globalization;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Writes the row numbers a warning affects, in one shared form so a line never grows long:
/// "row 4", "rows 4 and 9", "rows 4, 9 and 12", and for more than five rows the first five followed
/// by a count ("rows 4, 9, 12, 15, 18 and 25 more").
/// </summary>
internal static class ImportRowList
{
    /// <summary>How many rows are named before the rest are counted.</summary>
    public const int MaxNamed = 5;

    /// <summary>Formats a list of row numbers.</summary>
    /// <param name="rows">The distinct row numbers in ascending order.</param>
    /// <returns>The text, starting with "row" or "rows"; empty when there are no rows.</returns>
    public static string Format(IReadOnlyList<int> rows)
    {
        if (rows.Count == 0)
            return string.Empty;
        if (rows.Count == 1)
            return $"row {Text(rows[0])}";

        if (rows.Count <= MaxNamed)
            return $"rows {ImportText.JoinWithAnd(rows.Select(Text).ToList())}";

        var named = string.Join(", ", rows.Take(MaxNamed).Select(Text));
        return $"rows {named} and {Text(rows.Count - MaxNamed)} more";
    }

    /// <summary>A number as text, culture-independent.</summary>
    /// <param name="number">The number.</param>
    private static string Text(int number) => number.ToString(CultureInfo.InvariantCulture);
}

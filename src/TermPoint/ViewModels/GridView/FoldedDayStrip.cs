namespace TermPoint.ViewModels.GridView;

/// <summary>
/// The pure rules behind a folded day's strip and the day headers' tooltips (collapse days,
/// <c>designs/collapse-days.md</c>, Phase 3): which letter the strip shows, how wide it is, and what the tooltips say.
/// No Avalonia types, so every rule is unit-tested; <c>ScheduleGridView</c> only measures and draws.
/// </summary>
public static class FoldedDayStrip
{
    /// <summary>
    /// The short name a strip shows in its header row (P3-2): the app's own one-letter set M T W R F, and "Sa" /
    /// "Su" for the weekend, because one letter cannot tell those two apart.
    /// </summary>
    /// <param name="day">Day number, 1 = Monday ... 7 = Sunday.</param>
    /// <returns>The letter(s); "?" for a number outside 1..7.</returns>
    public static string Letter(int day) => day switch
    {
        1 => "M",
        2 => "T",
        3 => "W",
        4 => "R",
        5 => "F",
        6 => "Sa",
        7 => "Su",
        _ => "?",
    };

    /// <summary>
    /// The width of a folded day's whole strip (all of its semester sub-columns together): the wider of the letter
    /// and the section count, plus the padding on each side.
    /// </summary>
    /// <param name="letterWidth">Measured width of the letter in the header font.</param>
    /// <param name="countWidth">Measured width of the section count in the tile text font.</param>
    /// <param name="padding">Padding on EACH side (resource <c>FoldedDayStripPadding</c>).</param>
    /// <returns>The strip width in pixels.</returns>
    public static double StripWidth(double letterWidth, double countWidth, double padding) =>
        Math.Max(letterWidth, countWidth) + padding * 2;

    /// <summary>
    /// Merges time spans into the bands a strip draws (R1-2): spans that OVERLAP (or nest, or repeat) become one
    /// span; spans that only TOUCH (one ends exactly where the next starts) stay separate, so the drawing can show a
    /// hairline between two back-to-back tiles. Input order does not matter; empty or backwards spans are ignored.
    /// </summary>
    /// <param name="spans">(start, end) pairs in minutes from midnight, end exclusive.</param>
    /// <returns>The merged spans, in time order, never overlapping.</returns>
    public static List<(int Start, int End)> MergeSpans(IEnumerable<(int Start, int End)> spans)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var span in spans.Where(s => s.End > s.Start).OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            if (merged.Count > 0 && span.Start < merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, span.End));
            else
                merged.Add(span);
        }
        return merged;
    }

    /// <summary>
    /// The tooltip lines of a folded day's strip: what is folded and what it holds, then how to open it. Plain words,
    /// no marks (those arrive with task 5).
    /// </summary>
    /// <param name="dayName">The day's full name, e.g. "Tuesday".</param>
    /// <param name="summary">The strip's summary; null reads as an empty day.</param>
    /// <returns>One string per tooltip line.</returns>
    public static IReadOnlyList<string> StripTooltip(string dayName, FoldedDaySummary? summary)
    {
        int count = summary?.SectionCount ?? 0;
        var lines = new List<string> { $"{dayName} is folded: {count} {(count == 1 ? "section" : "sections")}." };
        if (summary?.HasOtherThings == true) lines.Add("Also has meetings or commitments.");
        lines.Add($"Click to show {dayName}.");
        return lines;
    }

    /// <summary>
    /// The tooltip of an OPEN day's header. The last open day cannot be folded (P2-5), so it only advertises the
    /// right-click menu.
    /// </summary>
    /// <param name="dayName">The day's full name, e.g. "Tuesday".</param>
    /// <param name="canFold">Whether a click would fold this day (<c>ScheduleGridViewModel.CanFold</c>).</param>
    /// <returns>One string per tooltip line (always one line).</returns>
    public static IReadOnlyList<string> HeaderTooltip(string dayName, bool canFold) =>
        [canFold ? $"Click to fold {dayName} away. Right-click for more." : "Right-click for more."];
}

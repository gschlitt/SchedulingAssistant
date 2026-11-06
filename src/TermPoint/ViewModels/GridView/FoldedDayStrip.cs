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
    /// THE lane table (Revision 3, R3-1): which lane of a folded strip each kind of "other thing" is drawn in, left to
    /// right. Kinds sharing a lane number are merged together in that lane (overlay sections and commitments are one
    /// lane, same colour). A new <see cref="FoldedOtherKind"/> gets a lane by adding a row here.
    /// </summary>
    public static IReadOnlyList<(FoldedOtherKind Kind, int Lane)> OtherLanes { get; } =
    [
        (FoldedOtherKind.Meeting,        0),
        (FoldedOtherKind.OverlaySection, 1),
        (FoldedOtherKind.Commitment,     1),
        (FoldedOtherKind.SharedSchedule, 2),
    ];

    /// <summary>
    /// What each lane holds, in plain words for the strip's tooltip, by lane number (see <see cref="OtherLanes"/>).
    /// Lane 1 says "overlays", not "commitments": it holds every tile an overlay adds (an instructor's sections and
    /// commitments, a room's sections, …). Add a word here when a lane is added.
    /// </summary>
    public static IReadOnlyList<string> LaneWords { get; } = ["meetings", "overlays", "shared schedules"];

    /// <summary>
    /// The tooltip line naming what else a folded day holds besides its sections, e.g.
    /// "Also shows meetings and overlays.", in lane order, each group once; null when there is nothing else.
    /// </summary>
    /// <param name="others">The day's other things (<see cref="FoldedDaySummary.OtherSpans"/>).</param>
    /// <returns>The line, or null.</returns>
    public static string? OtherThingsLine(IEnumerable<FoldedOtherSpan> others)
    {
        var words = others.Select(o => LaneOf(o.Kind)).Distinct().OrderBy(l => l)
                          .Select(l => l < LaneWords.Count ? LaneWords[l] : "other schedules")
                          .Distinct().ToList();
        if (words.Count == 0) return null;
        string list = words.Count == 1 ? words[0]
                    : string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1];
        return $"Also shows {list}.";
    }

    /// <summary>How many lanes the strip has: one more than the highest lane number in <see cref="OtherLanes"/>.</summary>
    public static int LaneCount => OtherLanes.Max(l => l.Lane) + 1;

    /// <summary>The lane of a kind; a kind with no row goes in the last lane.</summary>
    /// <param name="kind">The kind of other thing.</param>
    /// <returns>Lane number, 0 = leftmost.</returns>
    public static int LaneOf(FoldedOtherKind kind)
    {
        foreach (var (k, lane) in OtherLanes)
            if (k == kind) return lane;
        return LaneCount - 1;
    }

    /// <summary>
    /// Left edge of a lane, relative to the strip's left edge: the lanes (each <paramref name="laneWidth"/> wide with
    /// <paramref name="gap"/> between) as a group are centred on the strip's crease.
    /// </summary>
    /// <param name="lane">Lane number, 0 = leftmost.</param>
    /// <param name="stripWidth">The strip's width.</param>
    /// <param name="laneWidth">Width of one lane (resource <c>FoldedDayLaneWidth</c>).</param>
    /// <param name="gap">Gap between lanes (resource <c>FoldedDayLaneGap</c>).</param>
    /// <returns>X offset from the strip's left edge.</returns>
    public static double LaneLeft(int lane, double stripWidth, double laneWidth, double gap)
    {
        int n = LaneCount;
        double total = n * laneWidth + (n - 1) * gap;
        return (stripWidth - total) / 2 + lane * (laneWidth + gap);
    }

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
    /// The tooltip lines of a folded day's strip: what is folded and what it holds, then what is marked on it (if
    /// anything), then how to open it. Plain words.
    /// </summary>
    /// <param name="dayName">The day's full name, e.g. "Tuesday".</param>
    /// <param name="summary">The strip's summary; null reads as an empty day.</param>
    /// <param name="markLines">One line per kind of attention mark on the strip (<see cref="FoldedDayMarks.TooltipLines"/>); null or empty adds none.</param>
    /// <returns>One string per tooltip line.</returns>
    public static IReadOnlyList<string> StripTooltip(string dayName, FoldedDaySummary? summary,
                                                     IReadOnlyList<string>? markLines = null)
    {
        int count = summary?.SectionCount ?? 0;
        var lines = new List<string> { $"{dayName} is folded: {count} {(count == 1 ? "section" : "sections")}." };
        // Names what else is there (meetings, overlays, shared schedules), the same groups as the strip's lanes.
        if (summary is not null && OtherThingsLine(summary.OtherSpans) is { } otherLine) lines.Add(otherLine);
        if (markLines is not null) lines.AddRange(markLines);
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

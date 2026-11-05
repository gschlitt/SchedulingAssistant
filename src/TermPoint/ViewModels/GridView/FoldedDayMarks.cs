using TermPoint.Services;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// The kinds of attention mark a folded strip can carry (collapse days, <c>designs/collapse-days.md</c>, Revision 2:
/// "mark what the user pointed at"). Each kind has ONE row in <see cref="FoldedDayMarks.Kinds"/> giving its priority,
/// its brush resource and its tooltip words, so a new kind (the Slot Browser's "moving from", "moving to", "breaks")
/// is a new enum value plus a new row, with no change to the strip-drawing code.
/// </summary>
public enum MarkKind
{
    /// <summary>A selected section's meeting (single or multi selection).</summary>
    Selection,
    /// <summary>A proposed placement: the Room Browser's ghost slot (later the Slot Browser's).</summary>
    Proposal,
    /// <summary>One end of an Access-watch conflict (two meetings a student cannot attend both of).</summary>
    Conflict,
}

/// <summary>
/// One attention mark on a folded day: a time span the user pointed at, drawn as a bar on the strip.
/// </summary>
/// <param name="Kind">What the mark is (decides colour, priority and tooltip words).</param>
/// <param name="Start">Start time, minutes from midnight.</param>
/// <param name="End">End time, minutes from midnight (exclusive).</param>
/// <param name="SectionId">The section the mark is about, or null (a ghost slot belongs to no section).</param>
/// <param name="Label">Text for the tooltip, e.g. "MECH361 001" or a room; empty when not known.</param>
public sealed record AttentionMark(MarkKind Kind, int Start, int End, string? SectionId = null, string Label = "");

/// <summary>
/// The current pointers the user has set, from which a folded day's marks are worked out (Revision 2).
/// Filters, subject focus and flags are deliberately NOT here: they narrow the view and are never marked.
/// </summary>
/// <param name="SelectedSectionIds">Ids of the selected sections (the section store's selection).</param>
/// <param name="Ghosts">Ghost blocks currently shown (Room Browser, later Slot Browser).</param>
/// <param name="Conflicts">The Access-watch conflicts currently detected.</param>
public sealed record MarkInputs(
    IReadOnlyCollection<string> SelectedSectionIds,
    IReadOnlyList<GhostBlock> Ghosts,
    IReadOnlyList<ProgramConflict> Conflicts)
{
    /// <summary>No selection, no ghosts, no conflicts.</summary>
    public static MarkInputs None { get; } = new([], [], []);
}

/// <summary>One row of the kind table: how one <see cref="MarkKind"/> is ranked, coloured and worded.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="BrushKey">Resource key of the bar's brush (AppColors.axaml).</param>
/// <param name="SingleFormat">Tooltip line for one mark: {0} = label, {1} = time (HH:mm).</param>
/// <param name="ManyFormat">Tooltip line for several marks: {1} = earliest time (HH:mm), {2} = how many.</param>
public sealed record MarkKindInfo(MarkKind Kind, string BrushKey, string SingleFormat, string ManyFormat);

/// <summary>
/// Pure rules turning a folded day plus the user's current pointers into attention marks (task 5). No Avalonia types,
/// so everything is unit-tested; <c>ScheduleGridView</c> only draws what this returns.
/// </summary>
public static class FoldedDayMarks
{
    /// <summary>
    /// THE kind table, in priority order: when marks overlap in time the one earliest in this list is shown
    /// (selection, then a proposal, then a conflict). Add a row here to add a kind.
    /// </summary>
    public static IReadOnlyList<MarkKindInfo> Kinds { get; } =
    [
        new(MarkKind.Selection, "FoldedDayMarkSelection", "{0} selected at {1}",      "{2} sections selected, first at {1}"),
        new(MarkKind.Proposal,  "FoldedDayMarkProposal",  "Room Browser slot at {1}", "{2} Room Browser slots, first at {1}"),
        new(MarkKind.Conflict,  "FoldedDayMarkConflict",  "Access conflict at {1}",   "{2} access conflicts, first at {1}"),
    ];

    /// <summary>Wording used for a kind that has no row in <see cref="Kinds"/> (a kind added without its row still draws and reads sensibly).</summary>
    private static readonly MarkKindInfo FallbackInfo =
        new(MarkKind.Selection, "FoldedDayMarkSelection", "Marked at {1}", "{2} marks, first at {1}");

    /// <summary>The row for a kind, or a neutral fallback if the kind has none.</summary>
    /// <param name="kind">The kind to look up.</param>
    /// <returns>Its row.</returns>
    public static MarkKindInfo InfoFor(MarkKind kind) => Kinds.FirstOrDefault(k => k.Kind == kind) ?? FallbackInfo;

    /// <summary>The priority of a kind: its position in <see cref="Kinds"/> (0 = most important); a kind with no row ranks last.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A number where smaller wins.</returns>
    public static int Priority(MarkKind kind)
    {
        for (int i = 0; i < Kinds.Count; i++)
            if (Kinds[i].Kind == kind) return i;
        return int.MaxValue;
    }

    /// <summary>
    /// Works out the marks of one folded day: its selected section meetings, the ghosts on that day and the ends of
    /// conflicts on that day, with overlapping marks resolved to the most important one (<see cref="Resolve"/>).
    /// A selected section that is not on this day (or is hidden by a filter, so not in the summary) gives nothing.
    /// </summary>
    /// <param name="day">The folded day's number (1 = Monday ... 7 = Sunday).</param>
    /// <param name="summary">The strip's summary (its section meetings); null reads as an empty day.</param>
    /// <param name="inputs">The user's current pointers.</param>
    /// <returns>The day's marks in time order; empty when nothing was pointed at on this day.</returns>
    public static IReadOnlyList<AttentionMark> Build(int day, FoldedDaySummary? summary, MarkInputs inputs)
    {
        var raw = new List<AttentionMark>();
        var meetings = summary?.SectionMeetings ?? [];

        // Selection: each meeting of a selected section on this day.
        if (inputs.SelectedSectionIds.Count > 0)
            foreach (var m in meetings.Where(m => inputs.SelectedSectionIds.Contains(m.SectionId)))
                raw.Add(new AttentionMark(MarkKind.Selection, m.StartMinutes, m.EndMinutes, m.SectionId,
                                          m.Label.Length > 0 ? m.Label : "A section"));

        // Ghosts: each keeps the kind it carries (the Slot Browser's hook); other days are ignored.
        foreach (var g in inputs.Ghosts.Where(g => g.Day == day))
            raw.Add(new AttentionMark(g.Kind, g.StartMinutes, g.EndMinutes, null, g.RoomLabel));

        // Conflicts: only an END that lies on this day becomes a mark (a line between two open days is unchanged).
        foreach (var pc in inputs.Conflicts)
            foreach (var end in new[] { pc.MeetingA, pc.MeetingB }.Where(e => e.Day == day))
            {
                string label = meetings.FirstOrDefault(m => m.SectionId == end.SectionId)?.Label ?? "";
                raw.Add(new AttentionMark(MarkKind.Conflict, end.StartMinutes, end.EndMinutes, end.SectionId, label));
            }

        return Resolve(raw);
    }

    /// <summary>
    /// Resolves marks to one bar per stretch of time: wherever marks overlap, the one of highest priority
    /// (<see cref="Priority"/>) wins that stretch; ties go to the earlier start, then the section id and label, so the
    /// result never depends on input order. Neighbouring stretches won by the same mark are joined again. Empty or
    /// backwards spans are ignored. (Same idea as <see cref="FoldedDayStrip.MergeSpans"/>, with a winner.)
    /// </summary>
    /// <param name="marks">Any marks, possibly overlapping.</param>
    /// <returns>Non-overlapping marks in time order.</returns>
    public static IReadOnlyList<AttentionMark> Resolve(IEnumerable<AttentionMark> marks)
    {
        var all = marks.Where(m => m.End > m.Start).ToList();
        var result = new List<AttentionMark>();
        if (all.Count == 0) return result;

        var cuts = all.SelectMany(m => new[] { m.Start, m.End }).Distinct().Order().ToList();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            int a = cuts[i], b = cuts[i + 1];
            var winner = all.Where(m => m.Start <= a && m.End >= b)
                            .OrderBy(m => Priority(m.Kind)).ThenBy(m => m.Start)
                            .ThenBy(m => m.SectionId ?? "", StringComparer.Ordinal).ThenBy(m => m.Label, StringComparer.Ordinal)
                            .FirstOrDefault();
            if (winner is null) continue;   // a gap between two marks

            if (result.Count > 0 && result[^1].End == a && result[^1].Kind == winner.Kind
                && result[^1].SectionId == winner.SectionId && result[^1].Label == winner.Label)
                result[^1] = result[^1] with { End = b };
            else
                result.Add(winner with { Start = a, End = b });
        }
        return result;
    }

    /// <summary>
    /// The strip tooltip's lines for a day's marks: one line per kind present, in priority order, in plain words
    /// ("MECH361 001 selected at 14:00", "Room Browser slot at 19:00", "Access conflict at 10:00"). Several marks of one
    /// kind read "3 sections selected, first at 09:00".
    /// </summary>
    /// <param name="marks">The day's resolved marks.</param>
    /// <returns>Zero or more lines.</returns>
    public static IReadOnlyList<string> TooltipLines(IReadOnlyList<AttentionMark> marks)
    {
        var lines = new List<string>();
        foreach (var group in marks.GroupBy(m => m.Kind).OrderBy(g => Priority(g.Key)))
        {
            var ordered = group.OrderBy(m => m.Start).ToList();
            // A section with two meetings on the day is still one section.
            int count = ordered.Select(m => m.SectionId ?? $"{m.Start}-{m.End}").Distinct().Count();
            var info = InfoFor(group.Key);
            string time = FormatTime(ordered[0].Start);
            lines.Add(count == 1
                ? string.Format(info.SingleFormat, ordered[0].Label, time, count)
                : string.Format(info.ManyFormat, "", time, count));
        }
        return lines;
    }

    /// <summary>Formats minutes from midnight like the grid's time labels.</summary>
    /// <param name="minutes">Minutes from midnight.</param>
    /// <returns>"HH:mm", e.g. "14:00".</returns>
    public static string FormatTime(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
}

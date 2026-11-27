using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// What the Schedule Grid draws for the Access Analyzer's conflict marks: one <see cref="GridConflict"/> per
/// overlapping meeting pair (glow and connecting lines) and the hover lines per section and day. Built from an
/// <see cref="AccessSnapshot"/>, so it covers the whole semester (rule 29); the sections on the grid only decide
/// which partners get the "hidden by the filters" line (Q41).
/// </summary>
public sealed class GridAccessMarks
{
    /// <summary>No marks.</summary>
    public static GridAccessMarks None { get; } =
        new([], new Dictionary<(string, int), IReadOnlyList<string>>(), new HashSet<(string, int)>());

    /// <summary>One drawing record per overlapping meeting pair of every mark of every watched tag.</summary>
    public IReadOnlyList<GridConflict> Conflicts { get; }

    /// <summary>The hover lines of a section's line on a day, keyed by (section id, day number); absent when there is none.</summary>
    public IReadOnlyDictionary<(string SectionId, int Day), IReadOnlyList<string>> HoverLines { get; }

    /// <summary>
    /// The (section id, day number) lines on which the open watch's own hover repeats what its conflict marks say
    /// (<see cref="AccessTileLook.IsRepeatedBy"/>): the tile's hover leaves the look's line out there. Per day, since a
    /// section's other days have no mark lines and keep the look's line.
    /// </summary>
    public IReadOnlySet<(string SectionId, int Day)> RepeatedLooks { get; }

    private GridAccessMarks(IReadOnlyList<GridConflict> conflicts,
                            IReadOnlyDictionary<(string, int), IReadOnlyList<string>> hoverLines,
                            IReadOnlySet<(string, int)> repeatedLooks)
    {
        Conflicts = conflicts;
        HoverLines = hoverLines;
        RepeatedLooks = repeatedLooks;
    }

    /// <summary>
    /// Builds the grid's marks from a snapshot. Marks and lines are per section id, so in multi-semester mode each
    /// follows its own semester's sub-column.
    /// </summary>
    /// <param name="snapshot">The access results for the loaded semesters.</param>
    /// <param name="visibleSectionIds">The sections on the grid; a mark's partner outside it gets the hidden-partner hover line.</param>
    /// <param name="inSubjectFocus">Whether a section's subject is among the chosen subjects; null treats every section as in focus.</param>
    /// <param name="openWatchId">The tag id of the watch open in the picture; null when none (or a course checked alone).</param>
    /// <param name="lookOf">The open watch's look of a section, by id; null when none. Used with <paramref name="openWatchId"/> for <see cref="RepeatedLooks"/>.</param>
    /// <returns>The drawing records and hover lines; <see cref="None"/> when no watched tag has a mark.</returns>
    public static GridAccessMarks Build(AccessSnapshot snapshot, IReadOnlySet<string> visibleSectionIds,
                                        Func<Section, bool>? inSubjectFocus = null,
                                        string? openWatchId = null, Func<string, AccessTileLook?>? lookOf = null)
    {
        var conflicts = new List<GridConflict>();
        var hover = new Dictionary<(string, int), IReadOnlyList<string>>();
        var repeated = new HashSet<(string, int)>();

        foreach (var semester in snapshot.Semesters.Values)
        {
            var byLine = new Dictionary<(string SectionId, int Day), List<WatchMark>>();
            var sections = new Dictionary<string, Section>();
            foreach (var watch in semester.Watches)
                foreach (var mark in watch.Marks)
                {
                    // Mark all conflicts, with a watch open, adds only that watch's extra marks (Greg, 2026-11-26)
                    if (openWatchId is not null && watch.Tag.Id != openWatchId
                        && mark.Kind is ConflictMarkKind.Harmless or ConflictMarkKind.Plain) continue;
                    var wm = new WatchMark(watch.Tag.Id, watch.Tag.Name, mark);
                    sections[mark.A.Id] = mark.A;
                    sections[mark.B.Id] = mark.B;
                    foreach (var pair in mark.Meetings)
                    {
                        conflicts.Add(new GridConflict(watch.Tag.Id, watch.Tag.Name,
                            GridConflictMeeting.Of(mark.A, pair.A), GridConflictMeeting.Of(mark.B, pair.B)));
                        foreach (var key in new[] { (mark.A.Id, pair.A.Day), (mark.B.Id, pair.B.Day) })
                        {
                            if (!byLine.TryGetValue(key, out var list)) byLine[key] = list = [];
                            if (!list.Contains(wm)) list.Add(wm);
                        }
                    }
                }

            foreach (var ((sectionId, day), marks) in byLine)
            {
                var lines = AccessText.ConflictTips(semester.Inputs, sections[sectionId], day, marks,
                    id => visibleSectionIds.Contains(id) ? null
                        : inSubjectFocus is not null && sections.TryGetValue(id, out var hidden) && !inSubjectFocus(hidden)
                            ? HiddenReason.Subjects : HiddenReason.Filters);
                if (lines.Count > 0) hover[(sectionId, day)] = lines;
                if (openWatchId is not null && lookOf?.Invoke(sectionId) is { } look
                    && marks.Any(m => m.WatchId == openWatchId && look.IsRepeatedBy(m.Mark, sectionId)))
                    repeated.Add((sectionId, day));
            }
        }

        return conflicts.Count == 0 ? None : new GridAccessMarks(conflicts, hover, repeated);
    }
}

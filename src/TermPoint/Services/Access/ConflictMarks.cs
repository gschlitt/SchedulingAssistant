using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>What a marked conflict costs (rule 33, Q22, round 6b).</summary>
public enum ConflictMarkKind
{
    /// <summary>Ignoring this conflict makes a stranded section usable; <see cref="ConflictMark.X"/> is that section (one of the pair).</summary>
    Stranded,
    /// <summary>Ignoring this conflict frees a stranded section that is not in the pair; <see cref="ConflictMark.FreedSectionIds"/> lists them.</summary>
    Other,
    /// <summary>With no full slate: fixing this one conflict alone brings a full slate back.</summary>
    NoFull,
    /// <summary>With no full slate: a course that cannot fit is involved and no single conflict is to blame (Q22); <see cref="ConflictMark.X"/> and <see cref="ConflictMark.XConflictsWith"/> say what to look at.</summary>
    NoSingleFix,
    /// <summary>Costs nothing (other sections let students get around it); produced only when Mark all conflicts is on.</summary>
    Harmless,
}

/// <summary>One conflict between two sections of different components, with what it costs.</summary>
/// <param name="Kind">What the conflict costs.</param>
/// <param name="A">The first section of the pair.</param>
/// <param name="B">The second section of the pair.</param>
/// <param name="Meetings">The overlapping meeting pairs, for drawing.</param>
/// <param name="X">For <see cref="ConflictMarkKind.Stranded"/> and <see cref="ConflictMarkKind.NoSingleFix"/>: the section the mark is about; otherwise null.</param>
/// <param name="FreedSectionIds">For <see cref="ConflictMarkKind.Other"/>: the stranded sections this conflict keeps out of every slate.</param>
/// <param name="XConflictsWith">For <see cref="ConflictMarkKind.NoSingleFix"/>: every section of the other components that <see cref="X"/> conflicts with.</param>
public sealed record ConflictMark(
    ConflictMarkKind Kind,
    Section A,
    Section B,
    IReadOnlyList<MeetingPair> Meetings,
    Section? X,
    IReadOnlyList<string> FreedSectionIds,
    IReadOnlyList<Section> XConflictsWith);

/// <summary>
/// The conflicts to mark on the grid for one watch (a port of the wireframe's <c>conflictMarksFor</c>), always on
/// the whole watch with no subset and no pins. Only costly conflicts are marked; with Mark all conflicts on, the
/// harmless ones too. Pure and static.
/// </summary>
public static class ConflictMarks
{
    /// <summary>
    /// Marks for a watch (the sections carrying its tag, with its Multi-campus box).
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="markAll">Whether Mark all conflicts is on.</param>
    /// <returns>The marks, in group-pair order.</returns>
    public static IReadOnlyList<ConflictMark> For(AccessInputs inputs, AccessWatch watch, bool markAll) =>
        For(inputs, watch.CoveredSections(inputs), watch.MultiCampus, markAll);

    /// <summary>
    /// Marks for a set of covered sections. A conflict between sections of different components (different
    /// courses, or a lecture and a lab of one course) is marked when, with it ignored, a stranded section becomes
    /// usable (<see cref="ConflictMarkKind.Stranded"/>, <see cref="ConflictMarkKind.Other"/>); or, with no full
    /// slate, when it involves a course that cannot fit (<see cref="ConflictMarkKind.NoFull"/>,
    /// <see cref="ConflictMarkKind.NoSingleFix"/>). With no slate and the campus rule alone to blame, no time
    /// conflict is marked. Each stranded section is tested with an existence search with the pair ignored, not a
    /// full recount.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="covered">The sections under analysis.</param>
    /// <param name="multiCampus">Whether Multi-campus is on.</param>
    /// <param name="markAll">Whether Mark all conflicts is on (adds <see cref="ConflictMarkKind.Harmless"/> marks).</param>
    /// <returns>The marks, in group-pair order.</returns>
    public static IReadOnlyList<ConflictMark> For(
        AccessInputs inputs, IReadOnlyList<Section> covered, bool multiCampus, bool markAll)
    {
        var analysis = AccessAnalyzer.Analyze(inputs, covered, multiCampus, null, null);
        if (analysis.Health == AccessHealth.TooLarge) return [];
        var groups = analysis.Groups;
        var search = new SlateSearch(groups, !multiCampus);
        var marks = new List<ConflictMark>();
        bool stranded = analysis.HasSlate && analysis.StrandedSections.Count > 0;
        IReadOnlyList<string> cantFit = analysis.BestPartial?.CantFit ?? [];
        bool noFullSlate = analysis.BestPartial is not null && analysis.CampusReason is null;

        for (int i = 0; i < groups.Count; i++)
            for (int j = i + 1; j < groups.Count; j++)
                foreach (var s in groups[i].Candidates)
                    foreach (var t in groups[j].Candidates)
                    {
                        var meetings = SectionOverlap.OverlappingMeetings(s, t);
                        if (meetings.Count == 0) continue;
                        ConflictMark? mark = null;
                        if (stranded)
                        {
                            var freed = analysis.StrandedSections.Where(st => search.ExistsWith(st, s, t)).ToList();
                            var freedOfPair = freed.FirstOrDefault(f => f.Id == s.Id) ?? freed.FirstOrDefault(f => f.Id == t.Id);
                            if (freedOfPair is not null)
                                mark = new ConflictMark(ConflictMarkKind.Stranded, s, t, meetings, freedOfPair, [], []);
                            else if (freed.Count > 0)
                                mark = new ConflictMark(ConflictMarkKind.Other, s, t, meetings, null,
                                    freed.Select(f => f.Id).ToList(), []);
                        }
                        else if (noFullSlate && (cantFit.Contains(s.CourseId!) || cantFit.Contains(t.CourseId!)))
                        {
                            if (search.Exists(s, t))
                            {
                                mark = new ConflictMark(ConflictMarkKind.NoFull, s, t, meetings, null, [], []);
                            }
                            else
                            {
                                // Q22: no single conflict is to blame, so name everything the can't-fit section conflicts with
                                var culprit = cantFit.Contains(s.CourseId!) ? s : t;
                                var culpritGroup = groups.First(g => g.Candidates.Contains(culprit));
                                var hits = groups.Where(g => g != culpritGroup).SelectMany(g => g.Candidates)
                                    .Where(u => SectionOverlap.Conflicts(culprit, u)).ToList();
                                mark = new ConflictMark(ConflictMarkKind.NoSingleFix, s, t, meetings, culprit, [], hits);
                            }
                        }
                        if (mark is null && markAll)
                            mark = new ConflictMark(ConflictMarkKind.Harmless, s, t, meetings, null, [], []);
                        if (mark is not null) marks.Add(mark);
                    }
        return marks;
    }
}

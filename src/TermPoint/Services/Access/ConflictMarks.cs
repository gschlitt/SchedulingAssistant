using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>What a marked conflict costs (rule 33, round 6b; with no full slate, the fewest sections to move).</summary>
public enum ConflictMarkKind
{
    /// <summary>Ignoring this conflict makes a stranded section usable; <see cref="ConflictMark.X"/> is that section (one of the pair).</summary>
    Stranded,
    /// <summary>Ignoring this conflict frees a stranded section that is not in the pair; <see cref="ConflictMark.FreedSectionIds"/> lists them.</summary>
    Other,
    /// <summary>
    /// With no full slate: a conflict that moving one section of <see cref="ConflictMark.Problem"/> would clear (one
    /// end of the pair is that section; <see cref="MoveSuggestions"/>). Replaces Q22's "mark every conflict of a
    /// course that can't fit" (Greg, 2026-11-25: it suggested problems all over the grid when one was to blame).
    /// </summary>
    Move,
    /// <summary>Costs nothing (other sections let students get around it); produced only when Mark all conflicts is on.</summary>
    Harmless,
}

/// <summary>One conflict between two sections of different components, with what it costs.</summary>
/// <param name="Kind">What the conflict costs.</param>
/// <param name="A">The first section of the pair.</param>
/// <param name="B">The second section of the pair.</param>
/// <param name="Meetings">The overlapping meeting pairs, for drawing.</param>
/// <param name="X">For <see cref="ConflictMarkKind.Stranded"/>: the section the mark is about; otherwise null.</param>
/// <param name="FreedSectionIds">For <see cref="ConflictMarkKind.Other"/>: the stranded sections this conflict keeps out of every slate.</param>
/// <param name="Problem">For <see cref="ConflictMarkKind.Move"/>: the problem whose options include one end of the pair; otherwise null.</param>
/// <param name="ProblemCount">For <see cref="ConflictMarkKind.Move"/>: how many separate problems the watch has (1 or 2); otherwise 0.</param>
public sealed record ConflictMark(
    ConflictMarkKind Kind,
    Section A,
    Section B,
    IReadOnlyList<MeetingPair> Meetings,
    Section? X,
    IReadOnlyList<string> FreedSectionIds,
    MoveProblem? Problem = null,
    int ProblemCount = 0);

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
    /// <param name="keptInPlace">Ids of sections the user has said can't move (never suggested to move); null for none.</param>
    /// <returns>The marks, in group-pair order.</returns>
    public static IReadOnlyList<ConflictMark> For(AccessInputs inputs, AccessWatch watch, bool markAll,
        IReadOnlySet<string>? keptInPlace = null) =>
        For(inputs, watch.CoveredSections(inputs), watch.MultiCampus, markAll, keptInPlace);

    /// <summary>
    /// Marks for a set of covered sections. A conflict between sections of different components (different
    /// courses, or a lecture and a lab of one course) is marked when, with it ignored, a stranded section becomes
    /// usable (<see cref="ConflictMarkKind.Stranded"/>, <see cref="ConflictMarkKind.Other"/>); or, with no full
    /// slate, when moving one of its sections is among the fewest moves that bring a full slate back
    /// (<see cref="ConflictMarkKind.Move"/>, <see cref="MoveSuggestions"/>). With no slate and the campus rule alone
    /// to blame, no time conflict is marked. Each stranded section is tested with an existence search with the pair
    /// ignored, not a full recount.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="covered">The sections under analysis.</param>
    /// <param name="multiCampus">Whether Multi-campus is on.</param>
    /// <param name="markAll">Whether Mark all conflicts is on (adds <see cref="ConflictMarkKind.Harmless"/> marks).</param>
    /// <param name="keptInPlace">Ids of sections the user has said can't move; null for none.</param>
    /// <returns>The marks, in group-pair order.</returns>
    public static IReadOnlyList<ConflictMark> For(
        AccessInputs inputs, IReadOnlyList<Section> covered, bool multiCampus, bool markAll,
        IReadOnlySet<string>? keptInPlace = null)
    {
        var analysis = AccessAnalyzer.Analyze(inputs, covered, multiCampus, null, null);
        if (analysis.Health == AccessHealth.TooLarge) return [];
        var groups = analysis.Groups;
        var search = new SlateSearch(groups, !multiCampus);
        var marks = new List<ConflictMark>();
        bool stranded = analysis.HasSlate && analysis.StrandedSections.Count > 0;

        // With no full slate: the conflicts the fewest moves would clear, by pair (the first problem naming a pair wins).
        var moves = MoveSuggestions.For(analysis, keptInPlace);
        var movePairs = new Dictionary<(string, string), MoveProblem>();
        foreach (var problem in moves.Problems)
            foreach (var option in problem.Options)
                foreach (var partner in option.ConflictsWith)
                    movePairs.TryAdd(PairKey(option.Section, partner), problem);

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
                                mark = new ConflictMark(ConflictMarkKind.Stranded, s, t, meetings, freedOfPair, []);
                            else if (freed.Count > 0)
                                mark = new ConflictMark(ConflictMarkKind.Other, s, t, meetings, null,
                                    freed.Select(f => f.Id).ToList());
                        }
                        else if (movePairs.TryGetValue(PairKey(s, t), out var problem))
                        {
                            mark = new ConflictMark(ConflictMarkKind.Move, s, t, meetings, null, [],
                                problem, moves.Problems.Count);
                        }
                        if (mark is null && markAll)
                            mark = new ConflictMark(ConflictMarkKind.Harmless, s, t, meetings, null, []);
                        if (mark is not null) marks.Add(mark);
                    }
        return marks;
    }

    /// <summary>An order-free key for a pair of sections.</summary>
    private static (string, string) PairKey(Section a, Section b) =>
        string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
}

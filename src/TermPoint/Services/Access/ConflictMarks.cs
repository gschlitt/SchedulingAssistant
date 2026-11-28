using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>What a marked conflict costs (rule 33, round 6b; with no full slate, the fewest sections to move).</summary>
public enum ConflictMarkKind
{
    /// <summary>
    /// A conflict that is the reason a section is stranded: <see cref="ConflictMark.X"/> (one of the pair) is stranded
    /// because it conflicts with the other, which is in the whole remaining domain its narrative's last step rests on
    /// (<see cref="ConflictMark.Reason"/>). Marks follow the narrative's reasons, not what-ifs (Greg, 2026-11-27).
    /// </summary>
    Stranded,
    /// <summary>
    /// With no full slate: a conflict that moving one section of <see cref="ConflictMark.Problem"/> would clear (one
    /// end of the pair is that section; <see cref="MoveSuggestions"/>). Replaces Q22's "mark every conflict of a
    /// course that can't fit" (Greg, 2026-11-25: it suggested problems all over the grid when one was to blame).
    /// </summary>
    Move,
    /// <summary>Costs nothing (other sections let students get around it); produced only when Mark all conflicts is on.</summary>
    Harmless,
    /// <summary>
    /// With no full slate: a conflict outside the move suggestion; produced only when Mark all conflicts is on. Nothing
    /// is claimed about its cost (Greg, 2026-11-26): "other sections let students get around it" is false while no
    /// full slate exists, and in a crowded watch with nothing suggested it may be part of the trouble.
    /// </summary>
    Plain,
}

/// <summary>One conflict between two sections of different components, with what it costs.</summary>
/// <param name="Kind">What the conflict costs.</param>
/// <param name="A">The first section of the pair.</param>
/// <param name="B">The second section of the pair.</param>
/// <param name="Meetings">The overlapping meeting pairs, for drawing.</param>
/// <param name="X">For <see cref="ConflictMarkKind.Stranded"/>: the section the mark is about; otherwise null.</param>
/// <param name="Problem">For <see cref="ConflictMarkKind.Move"/>: the problem whose options include one end of the pair; otherwise null.</param>
/// <param name="ProblemCount">For <see cref="ConflictMarkKind.Move"/>: how many separate problems the watch has (1 or 2); otherwise 0.</param>
/// <param name="Reason">For <see cref="ConflictMarkKind.Stranded"/>: the narrative step (a clash) that ruled <see cref="X"/> out, whose <see cref="ReasonStep.Against"/> holds the other end of the pair; otherwise null.</param>
public sealed record ConflictMark(
    ConflictMarkKind Kind,
    Section A,
    Section B,
    IReadOnlyList<MeetingPair> Meetings,
    Section? X,
    MoveProblem? Problem = null,
    int ProblemCount = 0,
    ReasonStep? Reason = null);

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
    /// courses, or a lecture and a lab of one course) is marked, with no full slate, when moving one of its sections
    /// is among the fewest moves that bring a full slate back (<see cref="ConflictMarkKind.Move"/>,
    /// <see cref="MoveSuggestions"/>); otherwise when it is the reason a section is stranded
    /// (<see cref="ConflictMarkKind.Stranded"/>): the marks follow the narrative (<see cref="Analysis.Reasons"/>), one
    /// per section of a clash step's remaining domain that the stranded section overlaps in time. A section the
    /// narrative explains by a footprint, or not at all, gets no line (its hover says why). What-ifs ("if this
    /// conflict were removed, X would be usable") left the grid, Greg 2026-11-27: they were false when two conflicts
    /// each sufficed and misleading when the section had other clashes. With no slate and the campus rule alone to
    /// blame, no time conflict is marked.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="covered">The sections under analysis.</param>
    /// <param name="multiCampus">Whether Multi-campus is on.</param>
    /// <param name="markAll">Whether Mark all conflicts is on (adds <see cref="ConflictMarkKind.Harmless"/> marks, or
    /// <see cref="ConflictMarkKind.Plain"/> ones with no full slate).</param>
    /// <param name="keptInPlace">Ids of sections the user has said can't move; null for none.</param>
    /// <returns>The marks, in group-pair order.</returns>
    public static IReadOnlyList<ConflictMark> For(
        AccessInputs inputs, IReadOnlyList<Section> covered, bool multiCampus, bool markAll,
        IReadOnlySet<string>? keptInPlace = null)
    {
        var analysis = AccessAnalyzer.Analyze(inputs, covered, multiCampus, null, null);
        if (analysis.Health == AccessHealth.TooLarge) return [];
        var groups = analysis.Groups;
        var marks = new List<ConflictMark>();

        // Stranded sections are explained against the slates that count, full or (with no full slate) the most
        // complete partial ones (Greg, 2026-11-27), so the stranded marks show beside the move marks. Each clash
        // step names the sections the stranded one can't get round; the pairs they form are the marks.
        var reasons = analysis.Reasons;
        var strandedPairs = new Dictionary<(string, string), (Section X, ReasonStep Step)>();
        foreach (var x in analysis.StrandedSections)
            if (reasons.StepFor(x) is { Kind: ReasonStepKind.Clash } step)
                foreach (var y in step.Against.Where(y => y.Id != x.Id && SectionOverlap.Conflicts(x, y)))
                    strandedPairs.TryAdd(PairKey(x, y), (x, step));

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
                        if (movePairs.TryGetValue(PairKey(s, t), out var problem))
                        {
                            mark = new ConflictMark(ConflictMarkKind.Move, s, t, meetings, null,
                                problem, moves.Problems.Count);
                        }
                        else if (strandedPairs.TryGetValue(PairKey(s, t), out var why))
                        {
                            mark = new ConflictMark(ConflictMarkKind.Stranded, s, t, meetings, why.X, Reason: why.Step);
                        }
                        if (mark is null && markAll)
                            mark = new ConflictMark(analysis.HasSlate ? ConflictMarkKind.Harmless : ConflictMarkKind.Plain,
                                s, t, meetings, null);
                        if (mark is not null) marks.Add(mark);
                    }
        return marks;
    }

    /// <summary>An order-free key for a pair of sections.</summary>
    private static (string, string) PairKey(Section a, Section b) =>
        string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
}

using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>One section whose move would help, and the sections it conflicts with that the move would clear.</summary>
/// <param name="Section">The section to move.</param>
/// <param name="ConflictsWith">
/// The sections it conflicts with in the best filling of the rest (the fewest conflicts; every such filling, when
/// several tie). These are the conflict marks drawn from it.
/// </param>
public sealed record MoveOption(Section Section, IReadOnlyList<Section> ConflictsWith);

/// <summary>One problem keeping a watch from a full slate: moving any one of its options fixes it.</summary>
/// <param name="Options">The sections that could move, in group order then section order.</param>
public sealed record MoveProblem(IReadOnlyList<MoveOption> Options);

/// <summary>
/// The fewest sections to move to bring a full slate back (Greg, 2026-11-25: show what can be done about a problem,
/// not every conflict). Slates are cliques of sections that don't conflict, one per component; with no full slate,
/// the most components that can be filled together leave some out, and moving one section of each left-out component
/// to a free time would complete a slate. The options are the sections of the left-out components, across every way
/// of leaving components out, with the conflicts each would clear.
///
/// <para><b>One move:</b> one <see cref="MoveProblem"/> holding every option. <b>Two moves</b> that split into two
/// separate one-move problems (every working pair takes one component from each of two sets): two problems.
/// <b>Otherwise</b> (two tangled moves, or more): no problems, nothing singled out.</para>
///
/// <para>Sections the user keeps in place are never suggested to move. A move is assumed to find a free time: rooms,
/// instructors and allowed start times are not checked, which is why every option is offered. Applies only with no
/// full slate and no campus reason (with the campus rule to blame no time conflict is at fault, and a move changes
/// only a time). Pure and static.</para>
/// </summary>
/// <param name="Problems">The separate problems; empty when there is nothing to suggest.</param>
/// <param name="MovesNeeded">1 or 2 when known; null when more than two are needed, or none can work with the sections kept in place; 0 when this does not apply.</param>
public sealed record MoveSuggestions(IReadOnlyList<MoveProblem> Problems, int? MovesNeeded)
{
    /// <summary>Nothing to suggest: there is a full slate, the campus rule is to blame, or the watch is too large.</summary>
    public static MoveSuggestions None { get; } = new([], 0);

    /// <summary>
    /// Works out the suggestions for an analysis (with whatever ticks and pins it was made with).
    /// </summary>
    /// <param name="analysis">The analysis.</param>
    /// <param name="keptInPlace">Ids of sections the user has said can't move; null for none.</param>
    /// <returns>The suggestions; <see cref="None"/> when they do not apply.</returns>
    public static MoveSuggestions For(Analysis analysis, IReadOnlySet<string>? keptInPlace)
    {
        if (analysis.HasSlate || analysis.BestPartial is null || analysis.CampusReason is not null
            || analysis.Health == AccessHealth.TooLarge)
            return None;
        var groups = analysis.Groups;
        int n = groups.Count;
        var kept = keptInPlace ?? new HashSet<string>();
        var search = new SlateSearch(groups, analysis.OneCampusRule);
        bool Movable(int g) => groups[g].Candidates.Any(s => !kept.Contains(s.Id));

        // One move: every component that can be left out, with its options.
        var options = Enumerable.Range(0, n)
            .Where(g => Movable(g) && search.ExistsWithout([g]))
            .SelectMany(g => OptionsIn(g, [g]))
            .ToList();
        if (options.Count > 0) return new MoveSuggestions([new MoveProblem(options)], 1);

        // Two moves.
        var pairs = new List<(int A, int B)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (Movable(a) && Movable(b) && search.ExistsWithout([a, b])) pairs.Add((a, b));
        if (pairs.Count == 0) return new MoveSuggestions([], null);
        if (Split(pairs) is { } split)
        {
            // Each problem's options with one component of the other problem left out too, so a filling of the rest
            // exists and has at least one conflict with the mover (none would mean one move was enough).
            var first = new MoveProblem(split.A.SelectMany(a => OptionsIn(a, [a, split.B[0]])).ToList());
            var second = new MoveProblem(split.B.SelectMany(b => OptionsIn(b, [b, split.A[0]])).ToList());
            if (first.Options.Count > 0 && second.Options.Count > 0)
                return new MoveSuggestions([first, second], 2);
        }
        return new MoveSuggestions([], 2);

        // The sections of group g that could move, with the groups in skip left out of the filling.
        IEnumerable<MoveOption> OptionsIn(int g, int[] skip)
        {
            foreach (var s in groups[g].Candidates)
            {
                if (kept.Contains(s.Id)) continue;
                int fewest = search.FewestConflictsOf(s, skip, null, floor: 1);
                if (fewest < 1) continue;
                var partners = Enumerable.Range(0, n).Where(h => !skip.Contains(h))
                    .SelectMany(h => groups[h].Candidates)
                    .Where(t => SectionOverlap.Conflicts(s, t) && search.FewestConflictsOf(s, skip, t, fewest) == fewest)
                    .ToList();
                yield return new MoveOption(s, partners);
            }
        }
    }

    /// <summary>
    /// Whether the working pairs of components split into two separate one-move problems: two disjoint sets A and B
    /// with the pairs being exactly every (a, b), a from A and b from B.
    /// </summary>
    /// <param name="pairs">The pairs of components whose sections, moved, would bring a full slate back.</param>
    /// <returns>The two sets, or null when the pairs don't split.</returns>
    private static (int[] A, int[] B)? Split(IReadOnlyList<(int A, int B)> pairs)
    {
        var set = pairs.ToHashSet();
        bool Has(int x, int y) => set.Contains((Math.Min(x, y), Math.Max(x, y)));
        var involved = pairs.SelectMany(p => new[] { p.A, p.B }).Distinct().ToList();
        var (a0, b0) = pairs[0];
        var setA = involved.Where(x => x != b0 && Has(x, b0)).ToArray();
        var setB = involved.Where(y => y != a0 && Has(a0, y)).ToArray();
        if (setA.Intersect(setB).Any() || setA.Length * setB.Length != set.Count) return null;
        foreach (int x in setA)
            foreach (int y in setB)
                if (!Has(x, y)) return null;
        return (setA, setB);
    }
}

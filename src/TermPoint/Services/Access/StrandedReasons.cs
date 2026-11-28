using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>The two kinds of step in a narrative of why sections can't be used.</summary>
public enum ReasonStepKind
{
    /// <summary>
    /// The whole remaining domain of one group (one section or several) conflicts with the sections it rules out,
    /// so a student who fills that group has no way round it.
    /// </summary>
    Clash,
    /// <summary>Every way of filling two or three groups conflicts with the sections it rules out.</summary>
    Footprint,
}

/// <summary>A stretch of one day that every way of filling a set of groups occupies.</summary>
/// <param name="Day">The day number (1 = Monday), as stored on a meeting.</param>
/// <param name="StartMinutes">Start, in minutes from midnight.</param>
/// <param name="EndMinutes">End, in minutes from midnight (exclusive).</param>
public readonly record struct CommonTime(int Day, int StartMinutes, int EndMinutes);

/// <summary>
/// One step of a narrative: sections ruled out of every slate that counts, and why. Data only, no sentences
/// (the wording lives in <c>AccessText</c>).
/// </summary>
/// <param name="Index">The step's position in <see cref="StrandedReasons.Steps"/>, from zero.</param>
/// <param name="Kind">Which kind of step.</param>
/// <param name="Group">For <see cref="ReasonStepKind.Clash"/>: the index (in <see cref="Analysis.Groups"/>) of the group whose remaining sections are <see cref="Against"/>; otherwise -1.</param>
/// <param name="Against">For <see cref="ReasonStepKind.Clash"/>: the whole remaining domain of that group (one section, or several); otherwise empty.</param>
/// <param name="Narrowed">For <see cref="ReasonStepKind.Clash"/>: true when earlier steps removed sections from the group (so <see cref="Against"/> is what is left, not all its candidates).</param>
/// <param name="Groups">For <see cref="ReasonStepKind.Footprint"/>: the two or three groups, by index; otherwise empty.</param>
/// <param name="CommonTimes">For <see cref="ReasonStepKind.Footprint"/>: the times every combination occupies (in some week, whatever a meeting's frequency); empty when there is none.</param>
/// <param name="RuledOut">The sections this step rules out.</param>
/// <param name="After">The steps this one rests on: the ones that narrowed the groups involved, by index.</param>
public sealed record ReasonStep(
    int Index,
    ReasonStepKind Kind,
    int Group,
    IReadOnlyList<Section> Against,
    bool Narrowed,
    IReadOnlyList<int> Groups,
    IReadOnlyList<CommonTime> CommonTimes,
    IReadOnlyList<Section> RuledOut,
    IReadOnlyList<int> After);

/// <summary>Why a section of a move suggestion can't simply stay where it is: a group whose every remaining section conflicts with it.</summary>
/// <param name="Group">The group's index in <see cref="Analysis.Groups"/>.</param>
/// <param name="Against">The group's whole remaining domain, all of which conflict with the option.</param>
/// <param name="Narrowed">True when earlier steps removed sections from the group.</param>
/// <param name="Chain">
/// The steps that took the group's other sections away, ordered by step index, from the run of the option's own
/// counting slate kind (so its indices are not those of <see cref="StrandedReasons.Steps"/>); empty when nothing was
/// ruled out first. The Access panel lists them when the option is selected (Greg, 2026-11-27: "why not EOSC329's
/// other labs?" is the user's first question about a move).
/// </param>
public sealed record MoveReason(int Group, IReadOnlyList<Section> Against, bool Narrowed, IReadOnlyList<ReasonStep> Chain);

/// <summary>
/// The narrative behind an analysis's stranded sections and move options (Greg, 2026-11-27, "Clarity design"): a
/// propagation over the slates that count that records, for each section ruled out, the step that ruled it out and
/// the steps that step rests on. A section with a step is explained; the grid, the marks and the detail say the
/// step, not a what-if. Facts only: every step holds in every slate that counts, so it still holds after any
/// suggested move (a move changes only a time, and the slates judged leave the moved component out).
///
/// <para><b>Which slates:</b> one propagation with the groups left out by <i>any</i> counting slate left out
/// (the union of <see cref="Analysis.CountingSkipSets"/>), which is sound for every suggested move. A stranded
/// section of one of those groups is therefore not explained here. Move options are explained per skip set
/// (<see cref="MoveReasonFor"/>).</para>
///
/// <para><b>Steps:</b> a <see cref="ReasonStepKind.Clash"/> against the whole remaining domain of one group (one
/// section is the "forced" case), or a <see cref="ReasonStepKind.Footprint"/> over two or three groups. Steps found
/// by combining groups cap at <see cref="MaxCombinations"/> combinations; a cluster over the cap is skipped (never
/// guessed at: an unexplained section just keeps today's wording).</para>
///
/// <para>Empty (nothing explained) when no slate is judged, when pins narrow the groups, and for the single-section
/// and too-large cases. Pure; built lazily by <see cref="Analysis.Reasons"/>.</para>
/// </summary>
public sealed class StrandedReasons
{
    /// <summary>The most combinations of one cluster of groups the search enumerates before giving up on the cluster.</summary>
    public const int MaxCombinations = 5000;

    /// <summary>The narrative of an analysis that has none.</summary>
    public static StrandedReasons Empty { get; } = new(null, [], false);

    private readonly Analysis? _analysis;
    private readonly Dictionary<string, int> _stepOf = [];
    private readonly Dictionary<int, Run?> _moveRuns = [];
    private readonly object _gate = new();

    /// <summary>The steps, in the order they were found (a step only rests on earlier ones).</summary>
    public IReadOnlyList<ReasonStep> Steps { get; }

    /// <summary>True when the propagation stopped because a group had nothing left (no slate can exist; what was found so far is kept).</summary>
    public bool Contradiction { get; }

    /// <summary>Whether there is any narrative at all.</summary>
    public bool HasSteps => Steps.Count > 0;

    private StrandedReasons(Analysis? analysis, IReadOnlyList<ReasonStep> steps, bool contradiction)
    {
        _analysis = analysis;
        Steps = steps;
        Contradiction = contradiction;
        foreach (var step in steps)
            foreach (var s in step.RuledOut) _stepOf[s.Id] = step.Index;
    }

    // ── Queries ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The step that ruled a section out.</summary>
    /// <param name="section">Any section.</param>
    /// <returns>The step; null when the section was not ruled out (no narrative explains it).</returns>
    public ReasonStep? StepFor(Section section) =>
        _stepOf.TryGetValue(section.Id, out int i) ? Steps[i] : null;

    /// <summary>
    /// The steps that lead to a section being ruled out: its own step and every step it rests on, ordered by step
    /// index (so each line of a "Why" list can follow from the ones before it).
    /// </summary>
    /// <param name="section">Any section.</param>
    /// <returns>The chain, ending with the section's own step; empty when it was not ruled out.</returns>
    public IReadOnlyList<ReasonStep> ChainFor(Section section) =>
        StepFor(section) is { } own ? Ancestors(Steps, [own.Index]) : [];

    /// <summary>Some steps and every step they rest on, ordered by step index.</summary>
    /// <param name="steps">The steps of one run.</param>
    /// <param name="start">The indices to start from.</param>
    /// <returns>The chain; empty when <paramref name="start"/> is.</returns>
    private static IReadOnlyList<ReasonStep> Ancestors(IReadOnlyList<ReasonStep> steps, IEnumerable<int> start)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (!seen.Add(i)) continue;
            foreach (int d in steps[i].After) stack.Push(d);
        }
        return seen.Order().Select(i => steps[i]).ToList();
    }

    /// <summary>Whether any meeting of a section overlaps a <see cref="ReasonStepKind.Footprint"/> step's common times.</summary>
    /// <param name="step">The step.</param>
    /// <param name="section">The section.</param>
    /// <returns>True when the section's meetings fall (even partly) in the times every combination occupies.</returns>
    public static bool OverlapsCommonTimes(ReasonStep step, Section section) =>
        step.CommonTimes.Any(t => section.Schedule.Any(m =>
            m.Day == t.Day && m.StartMinutes < t.EndMinutes && t.StartMinutes < m.EndMinutes));

    /// <summary>
    /// Why a section that a move suggestion offers to move can't stay put: a group whose whole remaining domain, judged
    /// against the first counting slate kind that leaves the section's own group out, conflicts with it in time (a
    /// move changes only a time, so a campus split is not a reason to move). Only the one-group case is explained.
    /// </summary>
    /// <param name="option">A section offered by <see cref="MoveSuggestions"/>.</param>
    /// <returns>The reason; null when none (no such group, or no narrative for this analysis).</returns>
    public MoveReason? MoveReasonFor(Section option)
    {
        if (_analysis is not { } a) return null;
        int go = IndexOfGroup(a, option);
        if (go < 0) return null;
        int setIndex = -1;
        for (int i = 0; i < a.CountingSkipSets.Count; i++)
            if (a.CountingSkipSets[i].Contains(go)) { setIndex = i; break; }
        if (setIndex < 0) return null;
        Run? run;
        lock (_gate)
        {
            if (!_moveRuns.TryGetValue(setIndex, out run))
                _moveRuns[setIndex] = run = Propagate(a, a.CountingSkipSets[setIndex]);
        }
        if (run is null) return null;
        for (int h = 0; h < a.Groups.Count; h++)
        {
            if (!run.Dom.TryGetValue(h, out var dom) || dom.Count == 0) continue;
            if (dom.All(x => SectionOverlap.Conflicts(x, option)))
                return new MoveReason(h, dom.ToList(), dom.Count < a.Groups[h].Candidates.Count,
                    Ancestors(run.Steps, DepsOf(a, run, [h])));
        }
        return null;
    }

    // ── Building ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the narrative of an analysis (see the class remarks for the slates used and when it is empty).
    /// </summary>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The narrative; <see cref="Empty"/> when there is none to give.</returns>
    public static StrandedReasons Build(Analysis analysis)
    {
        if (analysis.CountingSkipSets.Count == 0 || analysis.PinnedSectionIds.Count > 0
            || analysis.Health == AccessHealth.TooLarge || analysis.IsSingleSectionCase)
            return Empty;
        var skip = analysis.CountingSkipSets.SelectMany(s => s).Distinct().ToList();
        var run = Propagate(analysis, skip);
        return new StrandedReasons(analysis, run.Steps, run.Contradiction);
    }

    /// <summary>The propagation's state: the steps, what each ruled out, and each filled group's remaining domain.</summary>
    private sealed class Run
    {
        public List<ReasonStep> Steps { get; } = [];
        public Dictionary<string, int> RemovedBy { get; } = [];
        public Dictionary<int, List<Section>> Dom { get; } = [];
        public bool Contradiction { get; set; }
    }

    /// <summary>Whether two sections can be in one slate: no time conflict and, under the one-campus rule, no campus split.</summary>
    private static bool Ok(Analysis a, Section x, Section y) =>
        !SectionOverlap.Conflicts(x, y) && (!a.OneCampusRule || !CampusRule.Splits(x, y));

    /// <summary>The index of the group holding a section among its candidates; -1 when none.</summary>
    private static int IndexOfGroup(Analysis a, Section s)
    {
        for (int g = 0; g < a.Groups.Count; g++)
            if (a.Groups[g].Candidates.Any(c => c.Id == s.Id)) return g;
        return -1;
    }

    /// <summary>The steps that narrowed the given groups' domains, ascending.</summary>
    private static List<int> DepsOf(Analysis a, Run r, IEnumerable<int> groups)
    {
        var deps = new HashSet<int>();
        foreach (int g in groups)
            foreach (var s in a.Groups[g].Candidates)
                if (r.RemovedBy.TryGetValue(s.Id, out int id)) deps.Add(id);
        return deps.Order().ToList();
    }

    /// <summary>
    /// Propagates over every group except those in <paramref name="skip"/>: a group with one section left rules out
    /// whatever conflicts with it; one group (size 1), or two or three groups together, whose every combination
    /// conflicts with a section rule it out. Repeats until nothing more is ruled out or a group is empty.
    /// </summary>
    private static Run Propagate(Analysis a, IReadOnlyCollection<int> skip)
    {
        var r = new Run();
        var filled = Enumerable.Range(0, a.Groups.Count).Where(g => !skip.Contains(g)).ToArray();
        foreach (int g in filled) r.Dom[g] = a.Groups[g].Candidates.ToList();

        void Add(ReasonStepKind kind, int group, IReadOnlyList<Section> against, bool narrowed, IReadOnlyList<int> groups,
            IReadOnlyList<CommonTime> times, List<Section> hit, List<int> after)
        {
            int index = r.Steps.Count;
            r.Steps.Add(new ReasonStep(index, kind, group, against, narrowed, groups, times, hit, after));
            foreach (var s in hit)
            {
                r.RemovedBy[s.Id] = index;
                foreach (int g in filled) r.Dom[g].Remove(s);
            }
        }

        bool changed = true;
        while (changed && !r.Contradiction)
        {
            changed = false;
            foreach (int g in filled)
                if (r.Dom[g].Count == 0) { r.Contradiction = true; return r; }

            // A group with one section left: whatever conflicts with that section is out.
            foreach (int g in filled.Where(g => r.Dom[g].Count == 1))
            {
                var f = r.Dom[g][0];
                var hit = filled.Where(h => h != g).SelectMany(h => r.Dom[h]).Where(x => !Ok(a, f, x)).ToList();
                if (hit.Count == 0) continue;
                Add(ReasonStepKind.Clash, g, [f], r.Dom[g].Count < a.Groups[g].Candidates.Count, [], [], hit, DepsOf(a, r, [g]));
                changed = true;
                break;
            }
            if (changed) continue;

            // Every combination of one, two or three unsettled groups conflicts with a section.
            var open = filled.Where(g => r.Dom[g].Count > 1).ToArray();
            for (int size = 1; size <= 3 && !changed; size++)
                foreach (var cluster in Subsets(open, size))
                {
                    var combos = Combos(a, r, cluster);
                    if (combos is null) continue;
                    if (combos.Count == 0) { r.Contradiction = true; return r; }
                    var hit = filled.Where(h => !cluster.Contains(h)).SelectMany(h => r.Dom[h])
                        .Where(x => combos.All(c => c.Any(m => !Ok(a, m, x)))).ToList();
                    if (hit.Count == 0) continue;
                    var after = DepsOf(a, r, cluster);
                    if (size == 1)
                    {
                        int g = cluster[0];
                        Add(ReasonStepKind.Clash, g, r.Dom[g].ToList(), r.Dom[g].Count < a.Groups[g].Candidates.Count,
                            [], [], hit, after);
                    }
                    else
                        Add(ReasonStepKind.Footprint, -1, [], false, cluster, CommonTimesOf(combos), hit, after);
                    changed = true;
                    break;
                }
        }
        return r;
    }

    /// <summary>
    /// Every combination (one section per group) of the given groups' current domains that fits together; null when
    /// there are more than <see cref="MaxCombinations"/> (the cluster is then not judged, as a part of the
    /// combinations could wrongly rule sections out).
    /// </summary>
    private static List<List<Section>>? Combos(Analysis a, Run r, int[] cluster)
    {
        var result = new List<List<Section>>();
        var pick = new List<Section>();
        bool tooMany = false;
        void Rec(int d)
        {
            if (tooMany) return;
            if (d == cluster.Length)
            {
                if (result.Count >= MaxCombinations) { tooMany = true; return; }
                result.Add(pick.ToList());
                return;
            }
            foreach (var s in r.Dom[cluster[d]])
            {
                if (!pick.All(p => Ok(a, p, s))) continue;
                pick.Add(s); Rec(d + 1); pick.RemoveAt(pick.Count - 1);
            }
        }
        Rec(0);
        return tooMany ? null : result;
    }

    /// <summary>Every subset of <paramref name="items"/> of the given size, in order.</summary>
    private static IEnumerable<int[]> Subsets(int[] items, int size)
    {
        if (size == 0) { yield return []; yield break; }
        for (int i = 0; i <= items.Length - size; i++)
            foreach (var rest in Subsets(items[(i + 1)..], size - 1))
                yield return [items[i], .. rest];
    }

    /// <summary>
    /// The times every combination occupies: per day, the intersection over the combinations of their meetings'
    /// merged intervals. A meeting counts whatever its frequency (as the probe did: "Mon, Tue and Wed 16:00-17:00"
    /// for labs that meet on alternate weeks), so "taken" means taken in some week; which sections are ruled out is
    /// always decided by the frequency-aware conflict test, never by these times, which only describe it.
    /// </summary>
    private static IReadOnlyList<CommonTime> CommonTimesOf(List<List<Section>> combos)
    {
        Dictionary<int, List<(int S, int E)>>? common = null;
        foreach (var combo in combos)
        {
            var occ = combo.SelectMany(s => s.Schedule)
                .GroupBy(m => m.Day)
                .ToDictionary(g => g.Key, g => Merge(g.Select(m => (m.StartMinutes, m.EndMinutes))));
            if (common is null) { common = occ; continue; }
            var next = new Dictionary<int, List<(int S, int E)>>();
            foreach (var (day, list) in common)
                if (occ.TryGetValue(day, out var other) && Intersect(list, other) is { Count: > 0 } both) next[day] = both;
            common = next;
            if (common.Count == 0) break;
        }
        return common is null ? [] : common.OrderBy(kv => kv.Key)
            .SelectMany(kv => kv.Value.Select(i => new CommonTime(kv.Key, i.S, i.E))).ToList();
    }

    /// <summary>Sorts intervals and merges those that touch or overlap.</summary>
    private static List<(int S, int E)> Merge(IEnumerable<(int S, int E)> intervals)
    {
        var merged = new List<(int S, int E)>();
        foreach (var (s, e) in intervals.OrderBy(i => i.S))
        {
            if (merged.Count > 0 && s <= merged[^1].E) merged[^1] = (merged[^1].S, Math.Max(merged[^1].E, e));
            else merged.Add((s, e));
        }
        return merged;
    }

    /// <summary>The overlap of two sorted lists of disjoint intervals.</summary>
    private static List<(int S, int E)> Intersect(List<(int S, int E)> x, List<(int S, int E)> y)
    {
        var result = new List<(int S, int E)>();
        int i = 0, j = 0;
        while (i < x.Count && j < y.Count)
        {
            int s = Math.Max(x[i].S, y[j].S), e = Math.Min(x[i].E, y[j].E);
            if (s < e) result.Add((s, e));
            if (x[i].E < y[j].E) i++; else j++;
        }
        return result;
    }
}

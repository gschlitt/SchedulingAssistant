using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The structured reasons the analyzer reports (self-fail, campus, stranded) and the best partial slate. Ported
/// from the r9 wireframe's <c>selfFailWhy</c>, <c>campusWhy</c>, <c>strandedReason</c> and <c>bestPartial</c>;
/// they return data, never sentences. Pure and static.
/// </summary>
internal static class AccessReasons
{
    /// <summary>
    /// Why a course cannot be taken even on its own, from its own sections: a single-section component whose
    /// partner component's sections all conflict with it in time, or (one-campus rule) are all at another campus.
    /// </summary>
    /// <param name="courseId">The course.</param>
    /// <param name="groups">The analysis's groups.</param>
    /// <param name="oneCampusRule">Whether the one-campus rule applies.</param>
    /// <returns>The reason; <see cref="SelfFailKind.NoFit"/> when no single section is to blame.</returns>
    internal static SelfFailReason SelfFail(string courseId, IReadOnlyList<ComponentGroup> groups, bool oneCampusRule)
    {
        var mine = groups.Where(g => g.CourseId == courseId).ToList();
        foreach (var g in mine.Where(g => g.Candidates.Count == 1))
        {
            var only = g.Candidates[0];
            foreach (var h in mine.Where(h => h != g))
                if (h.Candidates.All(t => SectionOverlap.Conflicts(only, t)))
                    return new SelfFailReason(SelfFailKind.TimeConflict, g, h);
            if (oneCampusRule)
                foreach (var h in mine.Where(h => h != g))
                    if (h.Candidates.All(t => CampusRule.Splits(only, t)))
                        return new SelfFailReason(SelfFailKind.Campus, g, h);
        }
        return new SelfFailReason(SelfFailKind.NoFit, null, null);
    }

    /// <summary>
    /// Why campus stands in the way: the in-person single-section components, grouped by campus. With two or more
    /// campuses it names the sections on the campus with the fewest (ties: fewer in-person sections overall) and
    /// on the campus with the most; otherwise the general "no slate keeps to one campus".
    /// </summary>
    /// <param name="groups">The analysis's groups.</param>
    /// <returns>The reason.</returns>
    internal static CampusReason Campus(IReadOnlyList<ComponentGroup> groups)
    {
        var only = groups.Where(g => g.Candidates.Count == 1).Select(g => g.Candidates[0])
            .Where(CampusRule.IsInPerson).ToList();
        var campuses = only.Select(s => s.CampusId!).Distinct().ToList();
        int Total(string campus) => groups.SelectMany(g => g.Candidates)
            .Count(s => CampusRule.IsInPerson(s) && s.CampusId == campus);
        var ordered = campuses
            .OrderBy(c => only.Count(s => s.CampusId == c))
            .ThenBy(Total)
            .ToList();
        if (ordered.Count >= 2)
        {
            var few = ordered[0];
            var many = ordered[^1];
            return new CampusReason(CampusReasonKind.OnlySectionsOnDifferentCampuses,
                few, only.Where(s => s.CampusId == few).ToList(),
                many, only.Where(s => s.CampusId == many).ToList());
        }
        return new CampusReason(CampusReasonKind.NoSlateKeepsToOneCampus, null, [], null, []);
    }

    /// <summary>
    /// Why a section is stranded: the first section it conflicts with that every slate uses (a different course
    /// first, then another component of the same course); failing that, with the one-campus rule, the first
    /// always-used section at another campus; failing that, no combination fits around it.
    /// </summary>
    /// <param name="section">The stranded section.</param>
    /// <param name="groups">The analysis's groups.</param>
    /// <param name="roles">The analysis's roles; "always used" means essential by timing or only section.</param>
    /// <param name="oneCampusRule">Whether the one-campus rule applies.</param>
    /// <returns>The reason.</returns>
    internal static StrandedReason Stranded(
        Section section, IReadOnlyList<ComponentGroup> groups,
        IReadOnlyDictionary<string, SectionRole> roles, bool oneCampusRule)
    {
        var own = groups.FirstOrDefault(g => g.Candidates.Any(s => s.Id == section.Id));
        var others = groups.Where(g => g != own).SelectMany(g => g.Candidates)
            .Where(t => roles.TryGetValue(t.Id, out var r) && r is SectionRole.EssentialByTiming or SectionRole.OnlySection)
            .ToList();
        var hit = others.FirstOrDefault(t => t.CourseId != section.CourseId && SectionOverlap.Conflicts(section, t))
                  ?? others.FirstOrDefault(t => t.CourseId == section.CourseId && SectionOverlap.Conflicts(section, t));
        if (hit is not null) return new StrandedReason(StrandedKind.ConflictsWithAlwaysUsed, hit);
        if (oneCampusRule)
        {
            var away = others.FirstOrDefault(t => CampusRule.Splits(section, t));
            if (away is not null) return new StrandedReason(StrandedKind.CampusConflictsWithAlwaysUsed, away);
        }
        return new StrandedReason(StrandedKind.NoCombination, null);
    }

    /// <summary>
    /// How many course subsets <see cref="Best"/> may test before it settles for the greedy answer (Q52, Greg
    /// 2026-11-24). The exact search is exponential in courses: measured on UBC Engineering (piece C.3), a watch of
    /// 14 courses with no full slate took 76 ms and 16 courses 0.8 s, against a 30 ms budget for recomputing every
    /// watch. One test costs roughly 30 µs there, so 300 keeps a large watch near 10 ms; every real program (at most
    /// 9 courses, so at most a few hundred subsets above its answer) is still searched exactly.
    /// </summary>
    internal const int BestPartialSubsetBudget = 300;

    /// <summary>
    /// The best partial slate (rule 23). Courses that fail alone are skipped first. A greedy pass (add each course
    /// in order while a slate still exists) gives a lower bound; then, for k from all courses down to that bound,
    /// every k-course subset is tested, and the first k with any slate wins, its left-out course lists being the
    /// alternatives. When the tests run out (<paramref name="subsetBudget"/>), the answer is the best found so far
    /// with <see cref="BestPartial.Complete"/> false: a larger subset may fit, or more alternatives may exist.
    /// </summary>
    /// <param name="groups">The analysis's groups.</param>
    /// <param name="search">A search over those groups with the analysis's campus rule.</param>
    /// <param name="skip">Courses that cannot be taken even alone: excluded from every subset, always in "can't fit".</param>
    /// <param name="subsetBudget">The most subsets to test after the greedy pass (see <see cref="BestPartialSubsetBudget"/>).</param>
    /// <returns>The best partial slate's summary.</returns>
    internal static BestPartial Best(IReadOnlyList<ComponentGroup> groups, SlateSearch search, IReadOnlyList<string> skip,
        int subsetBudget = BestPartialSubsetBudget)
    {
        var all = groups.Select(g => g.CourseId).Distinct().ToList();
        var courses = all.Where(c => !skip.Contains(c)).ToList();

        // Greedy lower bound: never wrong, maybe not the largest
        var greedy = new List<string>();
        foreach (var c in courses)
            if (search.ExistsForCourses([.. greedy, c])) greedy.Add(c);
        var greedyLeft = courses.Where(c => !greedy.Contains(c)).ToList();

        BestPartial Result(int covers, List<IReadOnlyList<string>> alternatives, bool complete) =>
            new(covers, all.Count, alternatives, skip.Concat(alternatives.SelectMany(a => a)).Distinct().ToList(), complete);

        int tests = 0;
        for (int k = courses.Count; k >= greedy.Count && k >= 1; k--)
        {
            var alternatives = new List<IReadOnlyList<string>>();
            bool any = false;
            foreach (var subset in Combinations(courses.Count, k))
            {
                if (++tests > subsetBudget)
                {
                    // Out of tests: what this level found, else the greedy answer
                    if (any) return Result(k, alternatives, complete: false);
                    return Result(greedy.Count, greedyLeft.Count > 0 ? [greedyLeft] : [], complete: false);
                }
                var chosen = subset.Select(i => courses[i]).ToList();
                if (!search.ExistsForCourses(chosen)) continue;
                any = true;
                var left = courses.Where(c => !chosen.Contains(c)).ToList();
                if (left.Count > 0) alternatives.Add(left);
            }
            if (any) return Result(k, alternatives, complete: true);
        }
        return new BestPartial(0, all.Count, [], all);
    }

    /// <summary>All k-element index combinations of 0..n-1, in lexicographic order.</summary>
    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        if (k > n) yield break;
        var idx = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])idx.Clone();
            int i = k - 1;
            while (i >= 0 && idx[i] == n - k + i) i--;
            if (i < 0) yield break;
            idx[i]++;
            for (int j = i + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }
}

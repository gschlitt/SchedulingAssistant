using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// The bound on the best-partial search (Q52): small watches are still searched exactly; a very large watch with
/// no full slate settles for the greedy answer, marked incomplete, instead of an exponential search.
/// </summary>
public sealed class AccessBestPartialBoundTests
{
    /// <summary>
    /// <paramref name="pairs"/> pairs of one-section courses; the two courses of each pair meet at the same hour
    /// (a different hour per pair), so the best partial slate takes one course of each pair.
    /// </summary>
    private static List<Section> ConflictingPairs(int pairs)
    {
        var sections = new List<Section>();
        for (int p = 0; p < pairs; p++)
        {
            sections.Add(Sec($"C{p:00}a", "1", Lecture, At(0, 8 + p, 9 + p)));
            sections.Add(Sec($"C{p:00}b", "1", Lecture, At(0, 8 + p, 9 + p)));
        }
        return sections;
    }

    [Fact]
    public void SmallWatch_IsSearchedExactly()
    {
        var a = AnalyzeAll(Simple(ConflictingPairs(2)));
        var best = a.BestPartial!;
        Assert.True(best.Complete);
        Assert.Equal((2, 4), (best.Covers, best.Of));
        Assert.Equal(4, best.Alternatives.Count);
    }

    [Fact]
    public void LargeWatch_SettlesForTheGreedyAnswer_MarkedIncomplete()
    {
        // 16 courses: the exact search would test tens of thousands of subsets above the answer (8)
        var a = AnalyzeAll(Simple(ConflictingPairs(8)));
        var best = a.BestPartial!;
        Assert.False(best.Complete);
        Assert.Equal((8, 16), (best.Covers, best.Of));
        // The greedy pass keeps the first course of each pair and leaves the second out
        Assert.Equal([Enumerable.Range(0, 8).Select(p => $"C{p:00}b").ToArray()],
            best.Alternatives.Select(x => x.ToArray()).ToArray());
        Assert.Equal(AccessHealth.Attention, a.Health);
    }

    [Fact]
    public void WithoutTheBound_TheSameWatchIsExact()
    {
        var inputs = Simple(ConflictingPairs(8));
        var courseIds = inputs.Sections.Select(s => s.CourseId!).Distinct().ToList();
        var groups = ComponentGrouping.Build(inputs, inputs.Sections, courseIds, new HashSet<string>()).Groups;
        var best = AccessReasons.Best(groups, new SlateSearch(groups, true), [], subsetBudget: int.MaxValue);
        Assert.True(best.Complete);
        Assert.Equal(8, best.Covers);
        Assert.Equal(256, best.Alternatives.Count);
    }

    [Fact]
    public void GreedyAnswer_IsAlwaysARealPartialSlate()
    {
        var inputs = Simple(ConflictingPairs(8));
        var a = AnalyzeAll(inputs);
        var left = a.BestPartial!.Alternatives.Single().ToHashSet();
        var kept = inputs.Sections.Select(s => s.CourseId!).Distinct().Where(c => !left.Contains(c)).ToList();
        Assert.True(new SlateSearch(a.Groups, true).ExistsForCourses(kept));
    }
}

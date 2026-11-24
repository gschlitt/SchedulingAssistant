using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// The course limit (Q53): more than <see cref="AccessAnalyzer.MaxCourses"/> chosen courses are not analysed and
/// get no conflict marks; a subset within the limit is analysed as usual.
/// </summary>
public sealed class AccessWatchSizeTests
{
    /// <summary>One-section courses, every one meeting Monday 9:00, so every pair conflicts.</summary>
    private static List<Section> Courses(int n) =>
        Enumerable.Range(0, n).Select(i => Sec($"C{i:00}", "1", Lecture, At(0, 9, 10))).ToList();

    [Fact]
    public void AtTheLimit_IsAnalysed()
    {
        var a = AnalyzeAll(Simple(Courses(AccessAnalyzer.MaxCourses)));
        Assert.Equal(AccessHealth.Attention, a.Health);
        Assert.NotNull(a.BestPartial);
    }

    [Fact]
    public void OverTheLimit_IsTooLarge_WithNothingElse()
    {
        var inputs = Simple(Courses(AccessAnalyzer.MaxCourses + 1));
        var a = AnalyzeAll(inputs);
        Assert.Equal(AccessHealth.TooLarge, a.Health);
        Assert.Equal(AccessAnalyzer.MaxCourses + 1, a.OfferedCourseIds.Count);
        Assert.Empty(a.Groups);
        Assert.Null(a.BestPartial);
        Assert.Empty(ConflictMarks.For(inputs, inputs.Sections, false, markAll: true));
    }

    [Fact]
    public void ASubsetWithinTheLimit_OfALargeWatch_IsAnalysed()
    {
        var inputs = Simple(Courses(AccessAnalyzer.MaxCourses + 5));
        var subset = Enumerable.Range(0, 3).Select(i => $"C{i:00}").ToHashSet();
        var a = AnalyzeAll(inputs, subset: subset);
        Assert.Equal(AccessHealth.Attention, a.Health);
        Assert.Equal(3, a.ChosenCourseIds.Count);
        Assert.Equal(1, a.BestPartial!.Covers);
    }
}

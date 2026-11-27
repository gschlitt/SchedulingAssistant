using TermPoint.Models;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// Mark all conflicts on the grid: with no watch open every watch adds its extra marks; with a watch open only that
/// watch does (the others keep the marks that cost students something). Greg, 2026-11-26.
/// </summary>
public sealed class GridAccessMarksScopeTests
{
    /// <summary>Two watches over the same two sections, which fit together except for one harmless-looking overlap.</summary>
    private static AccessSnapshot TwoWatches()
    {
        // A 1 and B 1 overlap, but A 2 and B 2 are free: students can avoid it, so it is only a Harmless mark
        var sections = new List<Section>
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(2, 9, 10)),
        };
        var inputs = Simple(sections);
        var watches = new[] { "t1", "t2" }.Select(id =>
        {
            var tag = new SchedulingEnvironmentValue { Id = id, Name = id, IsAccessWatch = true };
            var watch = AccessWatch.FromTag(tag);
            return new WatchAccess(tag, watch, ConflictMarks.For(inputs, sections, false, true), () => throw new NotSupportedException());
        }).ToList();
        return new AccessSnapshot(new Dictionary<string, SemesterAccess> { ["s"] = new("s", inputs, watches) });
    }

    private static HashSet<string> Seen(GridAccessMarks marks) => marks.Conflicts.Select(c => c.WatchId).ToHashSet();

    [Fact]
    public void NoWatchOpen_EveryWatchMarksItsExtraConflicts()
    {
        var marks = GridAccessMarks.Build(TwoWatches(), new HashSet<string>());
        Assert.Equal(new HashSet<string> { "t1", "t2" }, Seen(marks));
    }

    [Fact]
    public void AWatchOpen_OnlyThatWatchAddsExtraConflicts()
    {
        var marks = GridAccessMarks.Build(TwoWatches(), new HashSet<string>(), openWatchId: "t2");
        Assert.Equal(new HashSet<string> { "t2" }, Seen(marks));
    }
}

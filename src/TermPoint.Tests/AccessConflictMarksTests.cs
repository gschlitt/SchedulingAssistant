using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="ConflictMarks"/> (rule 33, Q22, round 6b): each kind of mark, Mark all conflicts, and the case where
/// the campus rule alone leaves no slate.
/// </summary>
public sealed class AccessConflictMarksTests
{
    private static IReadOnlyList<ConflictMark> Marks(IReadOnlyList<Section> sections, bool markAll = false, bool multiCampus = false) =>
        ConflictMarks.For(Simple(sections), Simple(sections).Sections, multiCampus, markAll);

    private static ConflictMark Between(IReadOnlyList<ConflictMark> marks, string idA, string idB) =>
        marks.Single(m => (m.A.Id == idA && m.B.Id == idB) || (m.A.Id == idB && m.B.Id == idA));

    [Fact]
    public void Stranded_IgnoringTheConflictMakesAStrandedSectionUsable()
    {
        var marks = Marks([
            Sec("A", "1", Lecture, At(0, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(1, 9, 10))]);
        var mark = Assert.Single(marks);
        Assert.Equal(ConflictMarkKind.Stranded, mark.Kind);
        Assert.Equal("B 1", mark.X!.Id);
        Assert.Equal(1, mark.Meetings.Count);
    }

    [Fact]
    public void Other_TheConflictFreesAStrandedSectionThatIsNotInThePair()
    {
        // x (B) is stranded; ignoring c2-d1 frees it although x is in neither
        var marks = Marks(AccessAnalyzerTests.ThreeCourseChain());
        var other = Between(marks, "C c2", "D d1");
        Assert.Equal(ConflictMarkKind.Other, other.Kind);
        Assert.Equal(["B x"], other.FreedSectionIds);
        Assert.Null(other.X);
        // the conflicts that involve x itself are Stranded marks about x
        Assert.Equal(ConflictMarkKind.Stranded, Between(marks, "B x", "C c1").Kind);
        Assert.Equal("B x", Between(marks, "B x", "D d2").X!.Id);
    }

    [Fact]
    public void NoFull_WithNoSlate_FixingThisOneConflictBringsOneBack()
    {
        var marks = Marks([Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(0, 9, 10))]);
        var mark = Assert.Single(marks);
        Assert.Equal(ConflictMarkKind.NoFull, mark.Kind);
    }

    [Fact]
    public void NoSingleFix_Q22ThreeCourseCase_NamesEverythingTheCantFitSectionConflictsWith()
    {
        // c conflicts with both a and b, which fit together: fixing either conflict alone is not enough
        var marks = Marks([
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("B", "1", Lecture, At(1, 9, 10)),
            Sec("C", "1", Lecture, At([0, 1], 9, 10))]);
        Assert.Equal(2, marks.Count);
        Assert.All(marks, m =>
        {
            Assert.Equal(ConflictMarkKind.NoSingleFix, m.Kind);
            Assert.Equal("C 1", m.X!.Id);
            Assert.Equal(["A 1", "B 1"], Ids(m.XConflictsWith).Order().ToArray());
        });
    }

    [Fact]
    public void Harmless_OnlyWhenMarkAllIsOn()
    {
        // every section is usable, so the conflict between a1 and b1 costs nothing
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(2, 9, 10)),
        };
        Assert.Empty(Marks(sections));
        var all = Marks(sections, markAll: true);
        var mark = Assert.Single(all);
        Assert.Equal(ConflictMarkKind.Harmless, mark.Kind);
        Assert.Equal(["A 1", "B 1"], new[] { mark.A.Id, mark.B.Id }.Order().ToArray());
    }

    [Fact]
    public void MarkAll_AddsHarmlessMarksBesideTheCostlyOnes()
    {
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10)), Sec("A", "2", Lecture, At(1, 9, 10)),
            Sec("B", "1", Lecture, At(0, 9, 10)), Sec("B", "2", Lecture, At(2, 9, 10)),
            Sec("C", "1", Lecture, At(3, 9, 10)), Sec("C", "2", Lecture, At(3, 9, 10)),
            Sec("D", "1", Lecture, At(3, 9, 10)),   // conflicts with both C sections: C and D cannot both fit, so no slate at all
        };
        var costly = Marks(sections);
        var all = Marks(sections, markAll: true);
        Assert.True(all.Count > costly.Count);
        Assert.Contains(all, m => m.Kind == ConflictMarkKind.Harmless);
        Assert.DoesNotContain(costly, m => m.Kind == ConflictMarkKind.Harmless);
    }

    [Fact]
    public void CampusRuleAlone_NoTimeConflictIsMarked_UnlessMarkAll()
    {
        // a (abb) and b (chil) don't conflict in time but cannot share a one-campus slate; c1 conflicts with a in time
        var sections = new[]
        {
            Sec("A", "1", Lecture, At(0, 9, 10), "abb"), Sec("B", "1", Lecture, At(1, 9, 10), "chil"),
            Sec("C", "1", Lecture, At(0, 9, 10), "abb"), Sec("C", "2", Lecture, At(2, 9, 10), "abb"),
        };
        var inputs = Simple(sections);
        var analysis = AnalyzeAll(inputs);
        Assert.False(analysis.HasSlate);
        Assert.NotNull(analysis.CampusReason);
        Assert.Empty(Marks(sections));
        Assert.Equal(ConflictMarkKind.Harmless, Assert.Single(Marks(sections, markAll: true)).Kind);
    }

    [Fact]
    public void MarksCarryTheOverlappingMeetingPairs()
    {
        var marks = Marks([
            Sec("A", "1", Lecture, At(Mwf, 9, 10)),
            Sec("B", "1", Lecture, At(Mw, 9, 10)), Sec("B", "2", Lecture, At(1, 9, 10))]);
        var mark = Assert.Single(marks);
        Assert.Equal(2, mark.Meetings.Count);
        Assert.All(mark.Meetings, p => Assert.Equal(p.A.Day, p.B.Day));
    }

    [Fact]
    public void WatchOverload_UsesTheTagAndTheWatchesMultiCampusBox()
    {
        var sections = new List<Section>
        {
            Sec("A", "1", Lecture, At(0, 9, 10), tags: ["w"]),
            Sec("B", "1", Lecture, At(0, 9, 10), tags: ["w"]),
            Sec("C", "1", Lecture, At(0, 9, 10)),   // untagged: not part of the watch
        };
        var inputs = Simple(sections);
        var marks = ConflictMarks.For(inputs, new AccessWatch("w", false), markAll: false);
        Assert.Equal(ConflictMarkKind.NoFull, Assert.Single(marks).Kind);
    }

    [Fact]
    public void NoGroups_NoMarks() => Assert.Empty(Marks([]));
}

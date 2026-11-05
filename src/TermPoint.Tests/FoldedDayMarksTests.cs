using System.Collections.Generic;
using System.Linq;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Collapse days, task 5: the pure mark rules in <see cref="FoldedDayMarks"/> and the flash rule in
/// <see cref="StripAttention"/>. Drawing and the animation are checked with tools/TileWidthProbe and by eye.
/// </summary>
public sealed class FoldedDayMarksTests
{
    private const int Day = 3; // Wednesday

    /// <summary>A summary whose section meetings are the given (id, start, end) triples.</summary>
    private static FoldedDaySummary Summary(params (string Id, int Start, int End)[] meetings) =>
        new(meetings.Select(m => m.Id).Distinct().Count(), false,
            meetings.Select(m => new FoldedSectionMeeting(m.Id, m.Start, m.End, $"LBL-{m.Id}")).ToList(), []);

    private static MarkInputs Inputs(string[]? selected = null, GhostBlock[]? ghosts = null, ProgramConflict[]? conflicts = null) =>
        new(selected ?? [], ghosts ?? [], conflicts ?? []);

    private static ConflictMeeting CM(string id, int day, int start, int end) => new(id, null, day, start, end, null);

    private static ProgramConflict Conflict(ConflictMeeting a, ConflictMeeting b) => new("w", "watch", a, b);

    /// <summary>A selected section on the folded day is a Selection mark at its meeting's span, with its label.</summary>
    [Fact]
    public void Selection_MarksTheSelectedMeeting()
    {
        var marks = FoldedDayMarks.Build(Day, Summary(("a", 600, 660), ("b", 700, 760)), Inputs(selected: ["a"]));
        var m = Assert.Single(marks);
        Assert.Equal(new AttentionMark(MarkKind.Selection, 600, 660, "a", "LBL-a"), m);
    }

    /// <summary>A selection with two meetings on the day gives two marks.</summary>
    [Fact]
    public void Selection_WithTwoMeetingsOnTheDay_GivesTwoMarks()
    {
        var marks = FoldedDayMarks.Build(Day, Summary(("a", 600, 660), ("a", 840, 900)), Inputs(selected: ["a"]));
        Assert.Equal(2, marks.Count);
        Assert.All(marks, m => Assert.Equal(MarkKind.Selection, m.Kind));
        Assert.Equal([600, 840], marks.Select(m => m.Start));
    }

    /// <summary>A selected section that is only on open days (not in this day's summary) gives no mark.</summary>
    [Fact]
    public void Selection_OnAnOpenDayOnly_GivesNothing()
    {
        var marks = FoldedDayMarks.Build(Day, Summary(("a", 600, 660)), Inputs(selected: ["elsewhere"]));
        Assert.Empty(marks);
    }

    /// <summary>With nothing pointed at, a busy day has no marks at all: filters and flags add none (Revision 2).</summary>
    [Fact]
    public void NoPointers_NoMarks()
    {
        Assert.Empty(FoldedDayMarks.Build(Day, Summary(("a", 600, 660), ("b", 700, 760)), MarkInputs.None));
        Assert.Empty(FoldedDayMarks.Build(Day, null, MarkInputs.None));
    }

    /// <summary>A ghost on the folded day is a Proposal mark carrying its room label; the default kind is Proposal.</summary>
    [Fact]
    public void Ghost_OnTheDay_IsAProposalMark()
    {
        var ghost = new GhostBlock(Day, 1140, 1215, "A 101", "19:00-20:15");
        Assert.Equal(MarkKind.Proposal, ghost.Kind);
        var m = Assert.Single(FoldedDayMarks.Build(Day, Summary(), Inputs(ghosts: [ghost])));
        Assert.Equal(new AttentionMark(MarkKind.Proposal, 1140, 1215, null, "A 101"), m);
    }

    /// <summary>Ghosts on other days are ignored.</summary>
    [Fact]
    public void Ghost_OnAnotherDay_IsIgnored()
    {
        var ghost = new GhostBlock(Day + 1, 1140, 1215, "A 101", "19:00-20:15");
        Assert.Empty(FoldedDayMarks.Build(Day, Summary(), Inputs(ghosts: [ghost])));
    }

    /// <summary>A ghost's own kind is kept (the Slot Browser's hook): another kind needs no change here.</summary>
    [Fact]
    public void Ghost_KeepsItsOwnKind()
    {
        var ghost = new GhostBlock(Day, 600, 660, "R", "t", Kind: MarkKind.Conflict);
        Assert.Equal(MarkKind.Conflict, Assert.Single(FoldedDayMarks.Build(Day, Summary(), Inputs(ghosts: [ghost]))).Kind);
    }

    /// <summary>Only a conflict's end that lies on the folded day becomes a mark; the other end (another day) gives none.</summary>
    [Fact]
    public void Conflict_OnlyTheEndOnTheFoldedDay()
    {
        var pc = Conflict(CM("a", Day, 600, 660), CM("b", Day + 1, 600, 660));
        var m = Assert.Single(FoldedDayMarks.Build(Day, Summary(("a", 600, 660)), Inputs(conflicts: [pc])));
        Assert.Equal(MarkKind.Conflict, m.Kind);
        Assert.Equal("a", m.SectionId);
        Assert.Equal("LBL-a", m.Label);
    }

    /// <summary>A conflict between two other days gives this day nothing.</summary>
    [Fact]
    public void Conflict_BetweenOtherDays_GivesNothing()
    {
        var pc = Conflict(CM("a", Day + 1, 600, 660), CM("b", Day + 2, 600, 660));
        Assert.Empty(FoldedDayMarks.Build(Day, Summary(), Inputs(conflicts: [pc])));
    }

    /// <summary>A conflict with both ends on the folded day gives two marks.</summary>
    [Fact]
    public void Conflict_BothEndsOnTheDay_GivesTwoMarks()
    {
        var pc = Conflict(CM("a", Day, 600, 660), CM("b", Day, 900, 960));
        Assert.Equal(2, FoldedDayMarks.Build(Day, Summary(), Inputs(conflicts: [pc])).Count);
    }

    /// <summary>Overlapping marks resolve to one bar showing the highest priority: selection beats proposal beats conflict.</summary>
    [Fact]
    public void Overlap_ShowsTheHighestPriority()
    {
        var ghost = new GhostBlock(Day, 600, 720, "A 101", "t");
        var pc = Conflict(CM("a", Day, 600, 660), CM("z", Day + 1, 600, 660));
        // Selection (600-660) over proposal (600-720) over conflict (600-660).
        var marks = FoldedDayMarks.Build(Day, Summary(("a", 600, 660)), Inputs(["a"], [ghost], [pc]));
        Assert.Equal(2, marks.Count);
        Assert.Equal((MarkKind.Selection, 600, 660), (marks[0].Kind, marks[0].Start, marks[0].End));
        Assert.Equal((MarkKind.Proposal, 660, 720), (marks[1].Kind, marks[1].Start, marks[1].End));
    }

    /// <summary>A conflict alone under a ghost loses the shared stretch but shows where it sticks out.</summary>
    [Fact]
    public void Resolve_LowerPriorityShowsOnlyWhereItSticksOut()
    {
        var marks = FoldedDayMarks.Resolve(
        [
            new AttentionMark(MarkKind.Proposal, 600, 660),
            new AttentionMark(MarkKind.Conflict, 630, 720, "c"),
        ]);
        Assert.Equal([(MarkKind.Proposal, 600, 660), (MarkKind.Conflict, 660, 720)],
                     marks.Select(m => (m.Kind, m.Start, m.End)));
    }

    /// <summary>Resolution does not depend on input order, and ignores empty or backwards spans.</summary>
    [Fact]
    public void Resolve_IsOrderIndependent_AndIgnoresEmptySpans()
    {
        var a = new AttentionMark(MarkKind.Selection, 600, 700, "a");
        var b = new AttentionMark(MarkKind.Selection, 650, 800, "b");
        var junk = new AttentionMark(MarkKind.Conflict, 900, 900);
        var one = FoldedDayMarks.Resolve([a, b, junk]);
        var two = FoldedDayMarks.Resolve([junk, b, a]);
        Assert.Equal(one, two);
        Assert.Equal([(600, 700), (700, 800)], one.Select(m => (m.Start, m.End)));
    }

    /// <summary>Separate marks with a gap stay separate bars.</summary>
    [Fact]
    public void Resolve_KeepsGapsBetweenMarks()
    {
        var marks = FoldedDayMarks.Resolve([new(MarkKind.Selection, 600, 660, "a"), new(MarkKind.Selection, 700, 760, "b")]);
        Assert.Equal(2, marks.Count);
    }

    /// <summary>The priority order is selection, proposal, conflict, and every kind has a brush key.</summary>
    [Fact]
    public void KindTable_PriorityAndBrushes()
    {
        Assert.True(FoldedDayMarks.Priority(MarkKind.Selection) < FoldedDayMarks.Priority(MarkKind.Proposal));
        Assert.True(FoldedDayMarks.Priority(MarkKind.Proposal) < FoldedDayMarks.Priority(MarkKind.Conflict));
        foreach (MarkKind k in System.Enum.GetValues<MarkKind>())
            Assert.False(string.IsNullOrEmpty(FoldedDayMarks.InfoFor(k).BrushKey));
    }

    /// <summary>The tooltip has one plain line per kind present, in priority order, with the time.</summary>
    [Fact]
    public void TooltipLines_OnePerKind()
    {
        var lines = FoldedDayMarks.TooltipLines(
        [
            new AttentionMark(MarkKind.Conflict, 600, 660, "c"),
            new AttentionMark(MarkKind.Selection, 840, 900, "a", "MECH361 001"),
            new AttentionMark(MarkKind.Proposal, 1140, 1215, null, "A 101"),
        ]);
        Assert.Equal(["MECH361 001 selected at 14:00", "Room Browser slot at 19:00", "Access conflict at 10:00"], lines);
    }

    /// <summary>Several marks of one kind read as a count and the earliest time; one section meeting twice is still one section.</summary>
    [Fact]
    public void TooltipLines_ManyOfOneKind()
    {
        var many = FoldedDayMarks.TooltipLines(
        [
            new AttentionMark(MarkKind.Selection, 900, 960, "b", "B"),
            new AttentionMark(MarkKind.Selection, 540, 600, "a", "A"),
        ]);
        Assert.Equal(["2 sections selected, first at 09:00"], many);

        var twice = FoldedDayMarks.TooltipLines(
        [
            new AttentionMark(MarkKind.Selection, 540, 600, "a", "A"),
            new AttentionMark(MarkKind.Selection, 900, 960, "a", "A"),
        ]);
        Assert.Equal(["A selected at 09:00"], twice);
    }

    /// <summary>The strip tooltip puts the mark lines before the "Click to show" line.</summary>
    [Fact]
    public void StripTooltip_PlacesMarkLinesBeforeTheClickHint()
    {
        var lines = FoldedDayStrip.StripTooltip("Wednesday", Summary(("a", 600, 660)), ["X at 10:00"]);
        Assert.Equal("X at 10:00", lines[^2]);
        Assert.Equal("Click to show Wednesday.", lines[^1]);
    }

    // ── StripAttention ────────────────────────────────────────────────────────────────────────────

    private static AttentionMark Sel(string id, int start = 600) => new(MarkKind.Selection, start, start + 60, id);

    private static IReadOnlyDictionary<int, IReadOnlyList<AttentionMark>> Days(params (int Day, AttentionMark[] Marks)[] days) =>
        days.ToDictionary(d => d.Day, d => (IReadOnlyList<AttentionMark>)d.Marks);

    /// <summary>Folding a day that already has marks seeds it: no flash.</summary>
    [Fact]
    public void Attention_FoldingADayWithMarks_DoesNotFlash()
    {
        var att = new StripAttention();
        Assert.Empty(att.Update(Days((3, [Sel("a")]))));
    }

    /// <summary>A new selection on an already folded day flashes that day.</summary>
    [Fact]
    public void Attention_NewSelection_Flashes()
    {
        var att = new StripAttention();
        att.Update(Days((3, [])));
        Assert.Equal([3], att.Update(Days((3, [Sel("a")]))));
    }

    /// <summary>A plain redraw with the same marks does not flash.</summary>
    [Fact]
    public void Attention_SameMarks_DoNotFlash()
    {
        var att = new StripAttention();
        att.Update(Days((3, [])));
        att.Update(Days((3, [Sel("a")])));
        Assert.Empty(att.Update(Days((3, [Sel("a")]))));
    }

    /// <summary>Removing a mark does not flash; marking it again does.</summary>
    [Fact]
    public void Attention_RemovedMark_DoesNotFlash_ButReturningDoes()
    {
        var att = new StripAttention();
        att.Update(Days((3, [Sel("a")])));
        Assert.Empty(att.Update(Days((3, []))));
        Assert.Equal([3], att.Update(Days((3, [Sel("a")]))));
    }

    /// <summary>A day that opens is forgotten: folding it again seeds it, with no flash.</summary>
    [Fact]
    public void Attention_DayOpenedThenRefolded_IsSeededAgain()
    {
        var att = new StripAttention();
        att.Update(Days((3, [])));
        att.Update(Days());                              // the day opened
        Assert.Empty(att.Update(Days((3, [Sel("a")])))); // refolded already marked
    }

    /// <summary>Only the days that gained a mark flash, in day order.</summary>
    [Fact]
    public void Attention_MultipleDays_OnlyTheGainersFlash()
    {
        var att = new StripAttention();
        att.Update(Days((1, [Sel("a")]), (3, []), (5, [])));
        var flash = att.Update(Days((1, [Sel("a")]), (3, [Sel("b")]), (5, [Sel("c")])));
        Assert.Equal([3, 5], flash);
    }

    /// <summary>A ghost that moves to another time on the same day is a new mark and flashes; the same ghost does not.</summary>
    [Fact]
    public void Attention_MovedGhost_Flashes()
    {
        var att = new StripAttention();
        att.Update(Days((3, [new AttentionMark(MarkKind.Proposal, 600, 660, null, "R")])));
        Assert.Empty(att.Update(Days((3, [new AttentionMark(MarkKind.Proposal, 600, 660, null, "R")]))));
        Assert.Equal([3], att.Update(Days((3, [new AttentionMark(MarkKind.Proposal, 700, 760, null, "R")]))));
    }
}

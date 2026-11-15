using System.Collections.Generic;
using System.Linq;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Collapse days, task 4: the pure strip rules in <see cref="FoldedDayStrip"/> (letter, width, tooltips).
/// Rendering itself is checked with tools/TileWidthProbe.
/// </summary>
public sealed class FoldedDayStripTests
{
    /// <summary>The letters are the app's own M T W R F, with two letters for the weekend (P3-2).</summary>
    [Theory]
    [InlineData(1, "M")]
    [InlineData(2, "T")]
    [InlineData(3, "W")]
    [InlineData(4, "R")]
    [InlineData(5, "F")]
    [InlineData(6, "Sa")]
    [InlineData(7, "Su")]
    [InlineData(0, "?")]
    public void Letter_IsTheAppsOwnSet(int day, string expected) => Assert.Equal(expected, FoldedDayStrip.Letter(day));

    /// <summary>The strip is as wide as the wider of the letter and the count, plus padding on both sides.</summary>
    [Theory]
    [InlineData(14.0, 8.0, 4.0, 22.0)]
    [InlineData(10.0, 20.0, 4.0, 28.0)]
    [InlineData(10.0, 10.0, 0.0, 10.0)]
    public void StripWidth_WiderOfLetterAndCount_PlusPaddingEachSide(double letter, double count, double pad, double expected) =>
        Assert.Equal(expected, FoldedDayStrip.StripWidth(letter, count, pad), 6);

    /// <summary>Overlapping spans merge into one band.</summary>
    [Fact]
    public void MergeSpans_OverlapMerges() =>
        Assert.Equal(new[] { (510, 700) }, FoldedDayStrip.MergeSpans([(510, 600), (550, 700)]).ToArray());

    /// <summary>Spans that only touch stay separate, so the strip can show a hairline between them.</summary>
    [Fact]
    public void MergeSpans_TouchingStaysSeparate() =>
        Assert.Equal(new[] { (510, 600), (600, 690) }, FoldedDayStrip.MergeSpans([(510, 600), (600, 690)]).ToArray());

    /// <summary>A span inside another, and identical spans, vanish into the outer one.</summary>
    [Fact]
    public void MergeSpans_NestedAndDuplicates()
    {
        Assert.Equal(new[] { (480, 720) }, FoldedDayStrip.MergeSpans([(480, 720), (510, 600), (510, 600)]).ToArray());
        // A chain where each span overlaps the next collapses to one.
        Assert.Equal(new[] { (480, 800) }, FoldedDayStrip.MergeSpans([(480, 600), (590, 700), (690, 800)]).ToArray());
    }

    /// <summary>Unsorted input gives the same answer, in time order.</summary>
    [Fact]
    public void MergeSpans_UnsortedInput()
    {
        var merged = FoldedDayStrip.MergeSpans([(800, 890), (510, 600), (550, 640), (600, 690)]);
        Assert.Equal(new[] { (510, 690), (800, 890) }, merged.ToArray());
    }

    /// <summary>No spans give no bands; empty or backwards spans are ignored.</summary>
    [Fact]
    public void MergeSpans_EmptyAndDegenerate()
    {
        Assert.Empty(FoldedDayStrip.MergeSpans([]));
        Assert.Empty(FoldedDayStrip.MergeSpans([(600, 600), (700, 650)]));
    }

    /// <summary>The strip tooltip names the day, counts the sections, mentions other things only when present, and says how to open it.</summary>
    [Fact]
    public void StripTooltip_PlainWords()
    {
        var plain = FoldedDayStrip.StripTooltip("Tuesday", new FoldedDaySummary(41, false, new List<FoldedSectionMeeting>(), new List<FoldedOtherSpan>()));
        Assert.Equal(new[] { "Tuesday is folded: 41 sections.", "Click to show Tuesday." }, plain.ToArray());

        var other = FoldedDayStrip.StripTooltip("Tuesday", new FoldedDaySummary(1, true, new List<FoldedSectionMeeting>(),
            new List<FoldedOtherSpan> { new(FoldedOtherKind.Commitment, 600, 660), new(FoldedOtherKind.Meeting, 780, 840) }));
        Assert.Equal(new[] { "Tuesday is folded: 1 section.", "Also shows meetings and overlays.", "Click to show Tuesday." }, other.ToArray());

        var none = FoldedDayStrip.StripTooltip("Thursday", null);
        Assert.Equal("Thursday is folded: 0 sections.", none[0]);
    }

    /// <summary>
    /// The "also shows" line names each lane group once, in lane order, calls commitments and overlay sections
    /// "overlays" (they share a lane), and is absent when there is nothing else.
    /// </summary>
    [Fact]
    public void OtherThingsLine_NamesLaneGroupsInOrder()
    {
        Assert.Null(FoldedDayStrip.OtherThingsLine([]));
        Assert.Equal("Also shows overlays.", FoldedDayStrip.OtherThingsLine(
            [new(FoldedOtherKind.Commitment, 600, 660), new(FoldedOtherKind.OverlaySection, 700, 760)]));
        Assert.Equal("Also shows meetings, overlays and shared schedules.", FoldedDayStrip.OtherThingsLine(
            [new(FoldedOtherKind.SharedSchedule, 500, 560), new(FoldedOtherKind.OverlaySection, 700, 760),
             new(FoldedOtherKind.Meeting, 800, 860)]));
    }

    /// <summary>An open header says how to fold; the last open day only advertises the menu.</summary>
    [Fact]
    public void HeaderTooltip_LastOpenDayOnlyMentionsMenu()
    {
        Assert.Equal("Click to fold Tuesday away. Right-click for more.", Assert.Single(FoldedDayStrip.HeaderTooltip("Tuesday", true)));
        Assert.Equal("Right-click for more.", Assert.Single(FoldedDayStrip.HeaderTooltip("Tuesday", false)));
    }
}

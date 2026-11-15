using System;
using System.Linq;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Collapse days, Revision 3 (R3-1): the lane table in <see cref="FoldedDayStrip"/> that decides where each kind of
/// "other thing" is drawn on a folded strip. Drawing itself is checked with tools/TileWidthProbe.
/// </summary>
public sealed class FoldedDayLaneTests
{
    /// <summary>The order is meetings | overlay sections and commitments (one lane) | shared schedules.</summary>
    [Fact]
    public void Lanes_AreMeetings_OverlayAndCommitments_Shared()
    {
        Assert.Equal(0, FoldedDayStrip.LaneOf(FoldedOtherKind.Meeting));
        Assert.Equal(1, FoldedDayStrip.LaneOf(FoldedOtherKind.OverlaySection));
        Assert.Equal(FoldedDayStrip.LaneOf(FoldedOtherKind.OverlaySection), FoldedDayStrip.LaneOf(FoldedOtherKind.Commitment));
        Assert.Equal(2, FoldedDayStrip.LaneOf(FoldedOtherKind.SharedSchedule));
        Assert.Equal(3, FoldedDayStrip.LaneCount);
    }

    /// <summary>Every kind has a row, so a new kind added to the enum without a lane is caught here.</summary>
    [Fact]
    public void EveryKind_HasALaneRow()
    {
        foreach (FoldedOtherKind kind in Enum.GetValues<FoldedOtherKind>())
            Assert.Contains(FoldedDayStrip.OtherLanes, l => l.Kind == kind);
    }

    /// <summary>The lanes, as a group, are centred on the strip and keep their gaps; the centre lane sits on the crease.</summary>
    [Fact]
    public void LaneLeft_CentresTheGroup()
    {
        // 3 lanes of 3 px with 1 px gaps = 11 px, in a 24 px strip: starts at 6.5.
        Assert.Equal(6.5, FoldedDayStrip.LaneLeft(0, 24, 3, 1), 6);
        Assert.Equal(10.5, FoldedDayStrip.LaneLeft(1, 24, 3, 1), 6);
        Assert.Equal(14.5, FoldedDayStrip.LaneLeft(2, 24, 3, 1), 6);
        // The middle lane's centre is the strip's centre.
        Assert.Equal(12.0, FoldedDayStrip.LaneLeft(1, 24, 3, 1) + 1.5, 6);
    }
}

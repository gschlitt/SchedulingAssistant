using SchedulingAssistant.ViewModels.GridView;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="ScheduleGridViewModel.ComputeTiles"/>.
///
/// ComputeTiles converts a flat list of <see cref="GridBlock"/> objects into a list
/// of positioned <see cref="GridTile"/> objects using a three-step algorithm:
///
///   1. Merge co-scheduled blocks (identical start+end) into a single tile with
///      multiple stacked entries.
///   2. Group merged tiles into overlap clusters: a new cluster starts whenever
///      a tile's start time is >= the maximum end time seen so far in all open clusters.
///   3. Within each cluster, assign column slots greedily (reuse the leftmost column
///      whose previous occupant has already ended). OverlapCount is set to the actual
///      number of columns used, not the cluster size.
///
/// Tests are organised by scenario: trivial cases, co-scheduling, overlapping,
/// cluster boundaries, and data propagation.
/// </summary>
public class ComputeTilesTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a basic <see cref="SectionMeetingBlock"/> for testing.
    /// Day is fixed at 1 (Monday) since ComputeTiles does not use the Day field.
    /// </summary>
    private static SectionMeetingBlock B(
        int start, int end,
        string label     = "X",
        string sectionId = "",
        bool isOverlay   = false) =>
        new(Day: 1, StartMinutes: start, EndMinutes: end,
            IsOverlay: isOverlay, Label: label, Initials: "", SectionId: sectionId);

    // ═════════════════════════════════════════════════════════════════════════
    // Trivial cases
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTiles_EmptyList_ReturnsEmpty()
    {
        var result = ScheduleGridViewModel.ComputeTiles([], "Fall 2025");
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeTiles_SingleBlock_ReturnsSingleTile()
    {
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600)]);
        Assert.Single(result);
        var tile = result[0];
        Assert.Equal(510, tile.StartMinutes);
        Assert.Equal(600, tile.EndMinutes);
        Assert.Equal(0,   tile.OverlapIndex);
        Assert.Equal(1,   tile.OverlapCount);
        Assert.Single(tile.Entries);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Non-overlapping blocks
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTiles_TwoNonOverlapping_EachTileFullWidth()
    {
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600), B(660, 750)]);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(1, t.OverlapCount));
        Assert.All(result, t => Assert.Equal(0, t.OverlapIndex));
    }

    [Fact]
    public void ComputeTiles_TouchingBlocks_TreatedAsNonOverlapping()
    {
        // A ends at 600, B starts at 600. The overlap test is (start < clusterMaxEnd),
        // so start==end is NOT an overlap — each tile gets its own full-width column.
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600), B(600, 690)]);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(1, t.OverlapCount));
    }

    [Fact]
    public void ComputeTiles_BlocksGivenOutOfOrder_SortedByStartTime()
    {
        // Providing blocks in reverse start order should produce the same layout
        // as providing them in forward order.
        var result = ScheduleGridViewModel.ComputeTiles([B(660, 750), B(510, 600)]);
        var ordered = result.OrderBy(t => t.StartMinutes).ToList();
        Assert.Equal(510, ordered[0].StartMinutes);
        Assert.Equal(660, ordered[1].StartMinutes);
        Assert.All(result, t => Assert.Equal(1, t.OverlapCount));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Co-scheduled blocks (identical start + end → merged into one tile)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTiles_TwoCoScheduled_SingleTileWithTwoEntries()
    {
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, "HIST101 A"), B(510, 600, "BIOL201 B")]);
        Assert.Single(result);
        var tile = result[0];
        Assert.Equal(2, tile.Entries.Count);
        Assert.Equal(0, tile.OverlapIndex);
        Assert.Equal(1, tile.OverlapCount);
    }

    [Fact]
    public void ComputeTiles_ThreeCoScheduled_SingleTileWithThreeEntries()
    {
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, "A"), B(510, 600, "B"), B(510, 600, "C")]);
        Assert.Single(result);
        Assert.Equal(3, result[0].Entries.Count);
    }

    [Fact]
    public void ComputeTiles_CoScheduledPlusTouching_TwoSeparateTiles()
    {
        // A&B are co-scheduled (510-600); C starts exactly when they end (600-690).
        // The merged A&B tile ends at 600; C.start == 600, so no overlap → separate cluster.
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, "A"), B(510, 600, "B"), B(600, 690, "C")]);
        Assert.Equal(2, result.Count);
        var coScheduled = result.Single(t => t.StartMinutes == 510);
        var separate    = result.Single(t => t.StartMinutes == 600);
        Assert.Equal(2, coScheduled.Entries.Count);
        Assert.Single(separate.Entries);
        Assert.Equal(1, coScheduled.OverlapCount);
        Assert.Equal(1, separate.OverlapCount);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Overlapping blocks
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTiles_TwoOverlapping_TwoTilesSideBySide()
    {
        // A: 8:30–10:00, B: 9:00–10:30 — B starts inside A.
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600), B(540, 630)]);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(2, t.OverlapCount));
        Assert.Contains(result, t => t.OverlapIndex == 0);
        Assert.Contains(result, t => t.OverlapIndex == 1);
    }

    [Fact]
    public void ComputeTiles_ThreeFullyOverlapping_ThreeColumns()
    {
        // All three overlap each other simultaneously — each needs its own column.
        // A: 8:30–10:00, B: 9:00–10:30, C: 9:30–11:00
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600), B(540, 630), B(570, 660)]);
        Assert.Equal(3, result.Count);
        Assert.All(result, t => Assert.Equal(3, t.OverlapCount));
        Assert.Contains(result, t => t.OverlapIndex == 0);
        Assert.Contains(result, t => t.OverlapIndex == 1);
        Assert.Contains(result, t => t.OverlapIndex == 2);
    }

    [Fact]
    public void ComputeTiles_ChainOverlap_ColumnIsReusedAndOverlapCountIsTwo()
    {
        // A: 8:30–10:00, B: 9:00–10:30, C: 10:00–11:30
        // A and C do not directly overlap, but both overlap B, so they end up in
        // one cluster. C starts exactly when A ends (600 >= 600) so it can reuse
        // A's column. Result: 2 actual columns even though the cluster has 3 tiles.
        //
        // This also verifies that the OverlapCount fix-up works: it is set to the
        // actual column count (2), not the raw cluster size (3).
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600), B(540, 630), B(600, 690)]);
        Assert.Equal(3, result.Count);
        Assert.All(result, t => Assert.Equal(2, t.OverlapCount));

        var sorted = result.OrderBy(t => t.StartMinutes).ToList();
        Assert.Equal(0, sorted[0].OverlapIndex); // A → column 0
        Assert.Equal(1, sorted[1].OverlapIndex); // B → column 1 (overlaps A)
        Assert.Equal(0, sorted[2].OverlapIndex); // C → column 0 reused (starts when A ends)
    }

    [Fact]
    public void ComputeTiles_CoScheduledBlockOverlapsThird_CorrectLayout()
    {
        // A and B are co-scheduled (merged into one tile); their tile overlaps C.
        // The merged tile and C should be placed side-by-side, each with OverlapCount=2.
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, "A"), B(510, 600, "B"), B(540, 630, "C")]);
        Assert.Equal(2, result.Count);
        var coScheduled = result.Single(t => t.StartMinutes == 510);
        var overlapping = result.Single(t => t.StartMinutes == 540);
        Assert.Equal(2, coScheduled.Entries.Count);
        Assert.Single(overlapping.Entries);
        Assert.Equal(2, coScheduled.OverlapCount);
        Assert.Equal(2, overlapping.OverlapCount);
        Assert.Equal(0, coScheduled.OverlapIndex);
        Assert.Equal(1, overlapping.OverlapIndex);
    }

    [Fact]
    public void ComputeTiles_MultipleClusters_EachHasIndependentOverlapCount()
    {
        // Cluster 1 (A+B overlap) → OverlapCount=2
        // Cluster 2 (C alone)     → OverlapCount=1
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600), B(540, 630), B(720, 810)]);
        var cluster1 = result.Where(t => t.StartMinutes < 700).ToList();
        var cluster2 = result.Where(t => t.StartMinutes >= 700).ToList();
        Assert.All(cluster1, t => Assert.Equal(2, t.OverlapCount));
        Assert.All(cluster2, t => Assert.Equal(1, t.OverlapCount));
        Assert.Equal(0, cluster2[0].OverlapIndex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Data propagation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTiles_SemesterName_PropagatedToAllTiles()
    {
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600), B(660, 750)], semesterName: "Fall 2025");
        Assert.All(result, t => Assert.Equal("Fall 2025", t.SemesterName));
    }

    [Fact]
    public void ComputeTiles_NoSemesterName_TilesHaveEmptyString()
    {
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600)]);
        Assert.Equal(string.Empty, result[0].SemesterName);
    }

    [Fact]
    public void ComputeTiles_EntryLabel_PreservedFromBlock()
    {
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, label: "HIST101 A", sectionId: "sec-1")]);
        var entry = result[0].Entries[0];
        Assert.Equal("HIST101 A", entry.Label);
        Assert.Equal("sec-1",     entry.SectionId);
    }

    [Fact]
    public void ComputeTiles_OverlayBlock_EntryHasIsOverlayTrue()
    {
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, isOverlay: true)]);
        Assert.True(result[0].Entries[0].IsOverlay);
    }

    [Fact]
    public void ComputeTiles_NonOverlayBlock_EntryHasIsOverlayFalse()
    {
        var result = ScheduleGridViewModel.ComputeTiles(
            [B(510, 600, isOverlay: false)]);
        Assert.False(result[0].Entries[0].IsOverlay);
    }

    [Fact]
    public void ComputeTiles_CommitmentBlock_EntryHasIsCommitmentTrueAndIsOverlayTrue()
    {
        // CommitmentBlocks always render as overlay (red) and suppress click interactions.
        var block  = new CommitmentBlock(1, 510, 600, "Dept Meeting", "commit-1");
        var result = ScheduleGridViewModel.ComputeTiles([block]);
        var entry  = result[0].Entries[0];
        Assert.True(entry.IsCommitment);
        Assert.True(entry.IsOverlay);
        Assert.Equal("Dept Meeting", entry.Label);
        Assert.Equal(string.Empty,   entry.SectionId);
    }

    [Fact]
    public void ComputeTiles_SectionMeetingBlock_EntryHasIsCommitmentFalse()
    {
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600)]);
        Assert.False(result[0].Entries[0].IsCommitment);
    }
}

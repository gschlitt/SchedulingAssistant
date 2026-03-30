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

    // ═════════════════════════════════════════════════════════════════════════
    // Block size extremes
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A 30-minute block is the shortest legal meeting length.
    /// Verifies the algorithm makes no assumption about a minimum tile size.
    /// </summary>
    [Fact]
    public void ComputeTiles_VeryShortBlock_30Min_ReturnsSingleFullWidthTile()
    {
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 540)]);
        Assert.Single(result);
        Assert.Equal(510, result[0].StartMinutes);
        Assert.Equal(540, result[0].EndMinutes);
        Assert.Equal(0,   result[0].OverlapIndex);
        Assert.Equal(1,   result[0].OverlapCount);
    }

    /// <summary>
    /// A 4-hour block (240 min) is the longest common meeting length.
    /// Verifies the algorithm handles a wide tile without splitting it.
    /// </summary>
    [Fact]
    public void ComputeTiles_VeryLongBlock_240Min_ReturnsSingleFullWidthTile()
    {
        // 8:30 AM – 12:30 PM (510–750)
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 750)]);
        Assert.Single(result);
        Assert.Equal(510, result[0].StartMinutes);
        Assert.Equal(750, result[0].EndMinutes);
        Assert.Equal(0,   result[0].OverlapIndex);
        Assert.Equal(1,   result[0].OverlapCount);
    }

    /// <summary>
    /// A short 30-min block that starts inside a 4-hour block must be placed
    /// side-by-side (two columns), not stacked.
    /// </summary>
    [Fact]
    public void ComputeTiles_ShortBlockStartsInsideLong_TwoColumns()
    {
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 750), B(540, 570)]);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(2, t.OverlapCount));
        Assert.Equal(0, result.Single(t => t.StartMinutes == 510).OverlapIndex);
        Assert.Equal(1, result.Single(t => t.StartMinutes == 540).OverlapIndex);
    }

    /// <summary>
    /// Two short blocks both inside a long block must share column 1 via column reuse —
    /// the long block holds column 0 throughout, but column 1 can be reused once the
    /// first short block has ended.  Peak concurrency never exceeds 2.
    /// </summary>
    [Fact]
    public void ComputeTiles_TwoShortBlocksInsideLong_ColumnIsReused()
    {
        // Long: 510–750. Short A: 540–570. Short B: 600–630.
        // A ends at 570; B starts at 600 — B can reuse A's column.
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 750), B(540, 570), B(600, 630)]);
        Assert.Equal(3, result.Count);
        var sorted = result.OrderBy(t => t.StartMinutes).ToList();
        Assert.Equal(0, sorted[0].OverlapIndex); // long    → col 0
        Assert.Equal(1, sorted[1].OverlapIndex); // short A → col 1
        Assert.Equal(1, sorted[2].OverlapIndex); // short B → col 1 reused
        Assert.All(result, t => Assert.Equal(2, t.OverlapCount)); // peak concurrency = 2
    }

    /// <summary>
    /// A block at the latest part of the day (8:30 PM–10:00 PM) should be handled
    /// identically to any other single block — no assumptions about time range.
    /// </summary>
    [Fact]
    public void ComputeTiles_LateEveningBlock_ReturnsSingleFullWidthTile()
    {
        // 8:30 PM – 10:00 PM (1230–1320)
        var result = ScheduleGridViewModel.ComputeTiles([B(1230, 1320)]);
        Assert.Single(result);
        Assert.Equal(1230, result[0].StartMinutes);
        Assert.Equal(1320, result[0].EndMinutes);
        Assert.Equal(1,    result[0].OverlapCount);
    }

    /// <summary>
    /// An early-morning block and a late-evening block on the same day share no overlap,
    /// so they must form independent clusters and each render full-width.
    /// </summary>
    [Fact]
    public void ComputeTiles_EarliestAndLatestBlocks_TwoIndependentFullWidthTiles()
    {
        // Earliest: 8:30 AM (510–600). Latest: 8:30 PM (1230–1320).
        var result = ScheduleGridViewModel.ComputeTiles([B(510, 600), B(1230, 1320)]);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(1, t.OverlapCount));
        Assert.All(result, t => Assert.Equal(0, t.OverlapIndex));
    }

    /// <summary>
    /// Six consecutive 30-minute blocks with no gaps between them.  Each block's start
    /// equals the previous block's end, which is the boundary condition for the overlap
    /// test — start == clusterMaxEnd is NOT an overlap, so each forms a new cluster.
    /// All tiles must be full-width.
    /// </summary>
    [Fact]
    public void ComputeTiles_SixBackToBack30MinBlocks_AllFullWidth()
    {
        var blocks = Enumerable.Range(0, 6)
            .Select(i => B(510 + i * 30, 540 + i * 30))
            .Cast<GridBlock>().ToList();
        var result = ScheduleGridViewModel.ComputeTiles(blocks);
        Assert.Equal(6, result.Count);
        Assert.All(result, t => Assert.Equal(1, t.OverlapCount));
        Assert.All(result, t => Assert.Equal(0, t.OverlapIndex));
    }

    /// <summary>
    /// Five sections all scheduled at exactly the same time with the same duration —
    /// the extreme co-scheduling case.  All five should merge into a single tile with
    /// five stacked entries and no side-by-side columns.
    /// </summary>
    [Fact]
    public void ComputeTiles_FiveCoScheduledSections_SingleTileWithFiveEntries()
    {
        var blocks = Enumerable.Range(1, 5).Select(i => B(510, 600, $"COURSE{i:D2}")).Cast<GridBlock>().ToList();
        var result = ScheduleGridViewModel.ComputeTiles(blocks);
        Assert.Single(result);
        Assert.Equal(5, result[0].Entries.Count);
        Assert.Equal(0, result[0].OverlapIndex);
        Assert.Equal(1, result[0].OverlapCount);
    }
}

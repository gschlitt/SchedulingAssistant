using SchedulingAssistant.ViewModels.GridView;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="ScheduleGridViewModel.ComputeGridlineOffsets"/>.
///
/// The method computes a cumulative Y-offset dictionary that pushes gridlines down
/// when tile content overflows the time-proportional height.  Expansion is distributed
/// proportionally across the 30-minute slots a tile spans, weighted by the overlap
/// in minutes between the tile and each slot.
///
/// Tests are organised by scenario:
///   • trivial / no-expansion cases
///   • 30-minute-aligned tiles (existing behaviour, must still pass)
///   • non-30-minute-aligned start or end times (the bug that was fixed)
///   • multi-tile interactions (independent slots, competing expansions)
///   • cumulative correctness (offsets accumulate over earlier slots)
///   • edge cases (zero-span grid, tile exactly at boundary, tile larger than grid)
/// </summary>
public class GridlineOffsetTests
{
    // ─── Constants ───────────────────────────────────────────────────────────

    /// <summary>Standard test grid: 08:30–10:00 gives gridlines at 510, 540, 570, 600.</summary>
    private const int First = 510;   // 08:30
    private const int Last  = 600;   // 10:00

    /// <summary>Tolerance for floating-point comparisons (1e-9 pixels).</summary>
    private const double Eps = 1e-9;

    // ─── Helper ──────────────────────────────────────────────────────────────

    /// <summary>Builds a tileHeightMap with a single entry.</summary>
    private static Dictionary<(int, int), (double, double)> Map(
        int start, int end, double timeBasedH, double actualH) =>
        new() { [(start, end)] = (timeBasedH, actualH) };

    /// <summary>Asserts that two double values agree within <see cref="Eps"/>.</summary>
    private static void Near(double expected, double actual, string label = "")
    {
        Assert.True(
            Math.Abs(expected - actual) < Eps,
            $"{label} expected {expected} but got {actual} (delta {Math.Abs(expected - actual)})");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Trivial / no-expansion
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Empty_TileHeightMap_AllOffsetsAreZero()
    {
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            new Dictionary<(int, int), (double, double)>(), First, Last);

        Assert.Equal(4, result.Count); // 510, 540, 570, 600
        foreach (var kv in result)
            Near(0, kv.Value, $"offset at {kv.Key}");
    }

    [Fact]
    public void SingleTile_NoOverflow_AllOffsetsAreZero()
    {
        // actualH == timeBasedH → no expansion whatsoever.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 10, 10), First, Last);

        foreach (var kv in result)
            Near(0, kv.Value, $"offset at {kv.Key}");
    }

    [Fact]
    public void SingleTile_ActualSmallerThanTimeBased_NoExpansion()
    {
        // actualH < timeBasedH is a degenerate case; must produce no expansion.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 20, 10), First, Last);

        foreach (var kv in result)
            Near(0, kv.Value, $"offset at {kv.Key}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 30-minute-aligned tiles — existing behaviour must be preserved
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Single30MinTile_Overflow_ExpansionAtFollowingGridlines()
    {
        // Tile 08:30–09:00.  expansion = 6.  Entire expansion lands in the 510 slot.
        // offsets: 510→0, 540→6, 570→6, 600→6.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 10, 16), First, Last);

        Near(0, result[510], "offset@510");
        Near(6, result[540], "offset@540");
        Near(6, result[570], "offset@570");
        Near(6, result[600], "offset@600");
    }

    [Fact]
    public void Single60MinTile_OverflowDistributedEvenlyAcross2Slots()
    {
        // Tile 08:30–09:30 (60 min).  expansion = 12.
        // Each slot gets 30/60 = ½ of 12 = 6.
        // offsets: 510→0, 540→6, 570→12, 600→12.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 570, 20, 32), First, Last);

        Near(0,  result[510], "offset@510");
        Near(6,  result[540], "offset@540");
        Near(12, result[570], "offset@570");
        Near(12, result[600], "offset@600");
    }

    [Fact]
    public void Single90MinTile_OverflowDistributedEvenlyAcross3Slots()
    {
        // Tile 08:30–10:00 (90 min).  expansion = 9.
        // Each slot gets 30/90 = ⅓ of 9 = 3.
        // offsets: 510→0, 540→3, 570→6, 600→9.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 600, 30, 39), First, Last);

        Near(0, result[510], "offset@510");
        Near(3, result[540], "offset@540");
        Near(6, result[570], "offset@570");
        Near(9, result[600], "offset@600");
    }

    [Fact]
    public void TileInMiddleSlot_OnlyLaterGridlinesMoved()
    {
        // Tile 09:00–09:30.  expansion = 8.
        // offsets: 510→0, 540→0, 570→8, 600→8.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(540, 570, 10, 18), First, Last);

        Near(0, result[510], "offset@510");
        Near(0, result[540], "offset@540");
        Near(8, result[570], "offset@570");
        Near(8, result[600], "offset@600");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Non-30-minute-aligned tiles — the fix being validated
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 45-minute tile starting ON a 30-minute boundary (08:30–09:15, i.e. 510–555).
    ///
    /// This was handled by the old condition <c>tile.StartMinutes &lt;= mins</c> but
    /// the fraction was wrong — it applied the same 2/3 weight to both slots, summing
    /// to 4/3 of the total expansion instead of 1.  The fix distributes proportionally:
    ///   08:30 slot: overlap = 30 min → fraction = 30/45 = ⅔  → slotExpansion = expansion × ⅔
    ///   09:00 slot: overlap = 15 min → fraction = 15/45 = ⅓  → slotExpansion = expansion × ⅓
    /// </summary>
    [Fact]
    public void Tile45Min_StartAligned_CorrectFractionPerSlot()
    {
        // Tile 510–555, expansion = 6.  Expected per-slot: 4 then 2.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 555, 20, 26), First, Last);

        Near(0, result[510], "offset@510");
        Near(4, result[540], "offset@540"); // 0 + 6*(30/45)
        Near(6, result[570], "offset@570"); // 4 + 6*(15/45)
        Near(6, result[600], "offset@600");
    }

    /// <summary>
    /// 45-minute tile NOT starting on a 30-minute boundary (08:45–09:30, i.e. 525–570).
    ///
    /// The old condition <c>tile.StartMinutes &lt;= mins</c> would silently SKIP the 08:30
    /// slot (525 &gt; 510) and place ALL weight on the 09:00 slot with fraction 2/3 — yielding
    /// only 4 px of expansion and misattributing it entirely to the wrong slot.
    ///
    /// The correct calculation:
    ///   08:30 slot: tile overlaps [510,540) by 15 min → fraction = 15/45 = ⅓ → +2
    ///   09:00 slot: tile overlaps [540,570) by 30 min → fraction = 30/45 = ⅔ → +4
    ///   09:30 slot: tile ends exactly at 570 → end &lt;= mins → not included
    /// </summary>
    [Fact]
    public void Tile45Min_StartNonAligned_PartialOverlapInPrecedingSlot()
    {
        // Tile 525–570, expansion = 6.  Expected: slot@510 gets 2, slot@540 gets 4.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 570, 20, 26), First, Last);

        Near(0, result[510], "offset@510");
        Near(2, result[540], "offset@540"); // 0 + 6*(15/45)
        Near(6, result[570], "offset@570"); // 2 + 6*(30/45)
        Near(6, result[600], "offset@600");
    }

    /// <summary>
    /// 15-minute tile that fits entirely within one 30-minute slot (08:45–09:00, i.e. 525–540).
    /// The tile is contained in the 08:30 slot: overlap = 15 min, span = 15 min → fraction = 1.
    /// The 09:00 slot boundary (mins=540) is excluded because end(540) &lt;= mins(540).
    /// </summary>
    [Fact]
    public void Tile15Min_EntirelyWithinOneSlot_FractionIsOne()
    {
        // Tile 525–540, expansion = 6.  All 6 px land in the 510 slot.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 540, 5, 11), First, Last);

        Near(0, result[510], "offset@510");
        Near(6, result[540], "offset@540"); // 0 + 6*(15/15)
        Near(6, result[570], "offset@570");
        Near(6, result[600], "offset@600");
    }

    /// <summary>
    /// 75-minute tile starting off-boundary (08:45–10:00, i.e. 525–600).
    ///   08:30 slot: overlap = 15 min → fraction = 15/75 = ⅕
    ///   09:00 slot: overlap = 30 min → fraction = 30/75 = 2/5
    ///   09:30 slot: overlap = 30 min → fraction = 30/75 = 2/5
    ///   10:00 gridline: end == mins → excluded
    /// Total fraction = 1/5 + 2/5 + 2/5 = 1 ✓
    /// </summary>
    [Fact]
    public void Tile75Min_StartNonAligned_DistributedAcross3Slots()
    {
        // expansion = 75.  Per-slot: 15, 30, 30.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 600, 50, 125), First, Last);

        Near(0,  result[510], "offset@510");
        Near(15, result[540], "offset@540"); // 0 + 75*(15/75)
        Near(45, result[570], "offset@570"); // 15 + 75*(30/75)
        Near(75, result[600], "offset@600"); // 45 + 75*(30/75)
    }

    /// <summary>
    /// Tile ending off a 30-minute boundary (08:30–09:45, i.e. 510–585).
    ///   08:30 slot: overlap = 30/75 = 2/5
    ///   09:00 slot: overlap = 30/75 = 2/5
    ///   09:30 slot: overlap = 15/75 = ⅕  (tile ends at 9:45, which is 15 min into the slot)
    /// Total fraction = 1 ✓
    /// </summary>
    [Fact]
    public void Tile75Min_EndNonAligned_DistributedAcross3Slots()
    {
        // expansion = 75.  Per-slot: 30, 30, 15.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 585, 50, 125), First, Last);

        Near(0,  result[510], "offset@510");
        Near(30, result[540], "offset@540"); // 0 + 75*(30/75)
        Near(60, result[570], "offset@570"); // 30 + 75*(30/75)
        Near(75, result[600], "offset@600"); // 60 + 75*(15/75)
    }

    /// <summary>
    /// Tile where both start AND end are off-boundary (08:45–09:45, i.e. 525–585).
    ///   08:30 slot: overlap = 15/60 = ¼
    ///   09:00 slot: overlap = 30/60 = ½
    ///   09:30 slot: overlap = 15/60 = ¼
    /// Total fraction = 1 ✓
    /// </summary>
    [Fact]
    public void Tile60Min_BothEndpointsNonAligned_DistributedAcross3Slots()
    {
        // expansion = 60.  Per-slot: 15, 30, 15.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 585, 40, 100), First, Last);

        Near(0,  result[510], "offset@510");
        Near(15, result[540], "offset@540"); // 0 + 60*(15/60)
        Near(45, result[570], "offset@570"); // 15 + 60*(30/60)
        Near(60, result[600], "offset@600"); // 45 + 60*(15/60)
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Multi-tile interactions
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoTiles_DifferentSlots_ExpansionsAreIndependent()
    {
        // Tile A: 510–540 (slot 510), expansion = 6.
        // Tile B: 570–600 (slot 570), expansion = 9.
        // offsets: 510→0, 540→6, 570→6, 600→15.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 540)] = (10, 16),
            [(570, 600)] = (10, 19),
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(6,  result[540], "offset@540");
        Near(6,  result[570], "offset@570");
        Near(15, result[600], "offset@600");
    }

    [Fact]
    public void TwoTiles_SameSlot_MaxExpansionWins()
    {
        // Both tiles cover the 510 slot but need different expansions.
        // Tile A: 510–540, expansion = 4.
        // Tile B: 510–570, expansion = 12 distributed: slot 510 gets 6, slot 540 gets 6.
        // At slot 510: max(4, 6) = 6.  At slot 540: only B → 6.
        // offsets: 510→0, 540→6, 570→12, 600→12.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 540)] = (10, 14),   // expansion = 4
            [(510, 570)] = (20, 32),   // expansion = 12
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(6,  result[540], "offset@540");
        Near(12, result[570], "offset@570");
        Near(12, result[600], "offset@600");
    }

    [Fact]
    public void TwoOverlappingTiles_CorrectMaxPerSlot()
    {
        // Tile A: 510–570 (60 min), expansion = 20.  Each slot gets 10.
        // Tile B: 540–600 (60 min), expansion = 24.  Each slot gets 12.
        // At slot 510: only A → 10.
        // At slot 540: max(A=10, B=12) = 12.
        // At slot 570: only B → 12.
        // offsets: 510→0, 540→10, 570→22, 600→34.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 570)] = (20, 40),   // expansion = 20
            [(540, 600)] = (20, 44),   // expansion = 24
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(10, result[540], "offset@540");
        Near(22, result[570], "offset@570");
        Near(34, result[600], "offset@600");
    }

    [Fact]
    public void NonAlignedTiles_TwoInSameSlot_MaxExpansionWins()
    {
        // Tile A: 525–570 (45 min), expansion = 45.
        //   slot@510 overlap = 15 → 15 px; slot@540 overlap = 30 → 30 px.
        // Tile B: 530–560 (30 min), expansion = 24.
        //   slot@510 overlap = 10 → 8 px; slot@540 overlap = 20 → 16 px.
        // At slot 510: max(A=15, B=8) = 15.
        // At slot 540: max(A=30, B=16) = 30.
        // offsets: 510→0, 540→15, 570→45, 600→45.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(525, 570)] = (50,  95),   // expansion = 45
            [(530, 560)] = (50,  74),   // expansion = 24
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(15, result[540], "offset@540");
        Near(45, result[570], "offset@570");
        Near(45, result[600], "offset@600");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cumulative correctness
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Offsets_AlwaysNonDecreasing()
    {
        // Verify the invariant: cumulative offsets only increase or stay flat.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 555)] = (20, 26),   // non-aligned 45-min tile
            [(560, 600)] = (15, 25),   // non-aligned 40-min tile
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        var ordered = result.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        for (int i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i] >= ordered[i - 1] - Eps,
                $"Offset at index {i} ({ordered[i]}) is less than previous ({ordered[i - 1]})");
    }

    [Fact]
    public void Cumulative_ThreeTiles_AddsCorrectly()
    {
        // Three non-overlapping 30-min tiles, each with different expansions.
        // Tile A: 510–540, expansion = 3.
        // Tile B: 540–570, expansion = 5.
        // Tile C: 570–600, expansion = 7.
        // offsets: 510→0, 540→3, 570→8, 600→15.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 540)] = (10, 13),
            [(540, 570)] = (10, 15),
            [(570, 600)] = (10, 17),
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(3,  result[540], "offset@540");
        Near(8,  result[570], "offset@570");
        Near(15, result[600], "offset@600");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleGridline_FirstEqualsLast_ReturnsOneEntry()
    {
        // Grid range is a single gridline: only one entry in the result.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 10, 20), 540, 540);

        Assert.Single(result);
        // The offset AT the gridline is stored before this slot's expansion is applied,
        // so it is always 0 for the first (and only) entry.
        Near(0, result[540], "offset@540");
    }

    [Fact]
    public void Tile_StartsExactlyAtGridlineEnd_NotIncludedInThatSlot()
    {
        // Tile starts at 09:00 (540).  The 08:30 slot covers [510, 540).
        // Because start(540) >= mins(510)+30(540), the tile is excluded from the 08:30 slot.
        // All expansion goes to the 09:00 slot.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(540, 570, 10, 20), First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(10, result[570], "offset@570");
        Near(10, result[600], "offset@600");
    }

    [Fact]
    public void Tile_EndsExactlyAtGridlineStart_NotIncludedInThatSlot()
    {
        // Tile ends at 09:00 (540).  The 09:00 slot covers [540, 570).
        // Because end(540) <= mins(540), the tile is excluded from the 09:00 slot.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 10, 20), First, Last);

        Near(0,  result[510], "offset@510");
        Near(10, result[540], "offset@540");  // expansion from the 08:30 slot
        Near(10, result[570], "offset@570");
        Near(10, result[600], "offset@600");
    }

    [Fact]
    public void Tile_SpanningEntireGrid_SpreadsExpansionEvenly()
    {
        // Tile 510–600 (90 min = 3 slots, each 30 min), expansion = 90.
        // Each slot gets 30 px.
        // offsets: 510→0, 540→30, 570→60, 600→90.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 600, 10, 100), First, Last);

        Near(0,  result[510], "offset@510");
        Near(30, result[540], "offset@540");
        Near(60, result[570], "offset@570");
        Near(90, result[600], "offset@600");
    }

    [Fact]
    public void Tile_LargerThanGrid_ClampedToGridBoundaries()
    {
        // Tile 480–630 extends before and after the [510, 600] grid window.
        // Each slot sees a 30-min overlap out of the 150-min tile span → fraction = 30/150 = ⅕.
        // expansion = 150.  Each of the 3 inner slots gets 30 px.
        // The 3 contributing slots (510, 540, 570) are the only ones inside the grid.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(480, 630, 50, 200), First, Last);

        Near(0,  result[510], "offset@510");
        Near(30, result[540], "offset@540"); // 0 + 150*(30/150)
        Near(60, result[570], "offset@570"); // 30 + 150*(30/150)
        Near(90, result[600], "offset@600"); // 60 + 150*(30/150)
    }

    [Fact]
    public void Tile_EntirelyBeforeGrid_ProducesNoExpansion()
    {
        // Tile 450–510 ends exactly at the grid start — excluded from all slots.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(450, 510, 10, 50), First, Last);

        foreach (var kv in result)
            Near(0, kv.Value, $"offset at {kv.Key}");
    }

    [Fact]
    public void Tile_EntirelyAfterGrid_ProducesNoExpansion()
    {
        // Tile starts at the last gridline — no slot within the grid is covered.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(600, 630, 10, 50), First, Last);

        foreach (var kv in result)
            Near(0, kv.Value, $"offset at {kv.Key}");
    }

    [Fact]
    public void ResultKeys_ExactlyMatchGridlineRange()
    {
        // Keys must be exactly {510, 540, 570, 600} for a [510, 600] range.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            new Dictionary<(int, int), (double, double)>(), First, Last);

        var expectedKeys = new HashSet<int> { 510, 540, 570, 600 };
        Assert.Equal(expectedKeys, result.Keys.ToHashSet());
    }

    [Fact]
    public void ResultKeys_CorrectForOddRange()
    {
        // Range 480–570 (3 gridlines: 480, 510, 540, 570).
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            new Dictionary<(int, int), (double, double)>(), 480, 570);

        var expectedKeys = new HashSet<int> { 480, 510, 540, 570 };
        Assert.Equal(expectedKeys, result.Keys.ToHashSet());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Regression: old algorithm vs new algorithm on known-wrong cases
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Documents the exact error the old algorithm made for a non-aligned 45-min tile.
    ///
    /// Old algorithm for tile 525–570, expansion = 6:
    ///   • Condition was <c>start &lt;= mins</c>: 525 &lt;= 510 is FALSE → 08:30 slot SKIPPED entirely.
    ///   • 09:00 slot: 525 &lt;= 540 is true, fraction = 1/(45/30) = ⅔ → slotExpansion = 4.
    ///   • Result: offsets 510→0, 540→0, 570→4, 600→4.
    ///
    /// Correct algorithm:
    ///   • 08:30 slot: overlap = 15 min → 6*(15/45) = 2.
    ///   • 09:00 slot: overlap = 30 min → 6*(30/45) = 4.
    ///   • Result: offsets 510→0, 540→2, 570→6, 600→6.
    /// </summary>
    [Fact]
    public void Regression_NonAligned45MinTile_OldAlgorithmWouldGiveWrongResult()
    {
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 570, 20, 26), First, Last);

        // Correct values (new algorithm):
        Near(0, result[510]);
        Near(2, result[540]);   // OLD would give 0 (missed the 08:30 slot entirely)
        Near(6, result[570]);   // OLD would give 4 (under-counted total expansion)
        Near(6, result[600]);   // OLD would give 4

        // Explicitly assert against what the old algorithm would return, to document the regression.
        Assert.NotEqual(0.0, result[540]); // old gave 0
        Assert.NotEqual(4.0, result[570]); // old gave 4
    }

    /// <summary>
    /// Documents the over-counting error the old algorithm made for a boundary-start 45-min tile.
    ///
    /// Old algorithm for tile 510–555, expansion = 6:
    ///   • 08:30 slot: 510 &lt;= 510 true, 510 &lt; 555 true → fraction = ⅔ → 4 px.
    ///   • 09:00 slot: 510 &lt;= 540 true, 540 &lt; 555 true → fraction = ⅔ → 4 px.
    ///   • Total distributed = 8 px (expansion was only 6 → over-counted by 33%).
    ///   • Result: offsets 510→0, 540→4, 570→8, 600→8.
    ///
    /// Correct algorithm gives 4 then 2 (total = 6 ✓).
    /// </summary>
    [Fact]
    public void Regression_BoundaryStart45MinTile_OldAlgorithmOverCountedExpansion()
    {
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 555, 20, 26), First, Last);

        // Correct values:
        Near(0, result[510]);
        Near(4, result[540]);
        Near(6, result[570]);   // OLD would give 8 (over-counted)
        Near(6, result[600]);   // OLD would give 8

        Assert.True(result[570] < 8 - Eps, $"offset@570 should be 6, old algorithm gave 8");
    }
}

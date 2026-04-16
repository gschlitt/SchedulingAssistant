using SchedulingAssistant.ViewModels.GridView;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="ScheduleGridViewModel.ComputeGridlineOffsets"/>.
///
/// The method computes a cumulative Y-offset dictionary that pushes gridlines down
/// when tile content overflows the time-proportional height.  Expansion is deferred
/// to the tile's last covered slot, crediting any expansion that earlier slots
/// already received from other tiles.  This "greedy defer-to-end" strategy
/// minimises total vertical expansion across the grid.
///
/// Tests are organised by scenario:
///   • trivial / no-expansion cases
///   • single-tile deferred expansion (30-min-aligned and non-aligned)
///   • multi-tile interactions (credit from earlier tiles, competing expansions)
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
    // Single-tile deferred expansion — 30-minute-aligned tiles
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Single30MinTile_Overflow_ExpansionAtFollowingGridlines()
    {
        // Tile 08:30–09:00.  overflow = 6.  Single slot → all expansion at 510.
        // offsets: 510→0, 540→6, 570→6, 600→6.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 10, 16), First, Last);

        Near(0, result[510], "offset@510");
        Near(6, result[540], "offset@540");
        Near(6, result[570], "offset@570");
        Near(6, result[600], "offset@600");
    }

    [Fact]
    public void Single60MinTile_OverflowDeferredToLastSlot()
    {
        // Tile 08:30–09:30 (60 min).  overflow = 12.
        // Greedy: all deferred to last slot (540).  No prior credit → deficit = 12.
        // offsets: 510→0, 540→0, 570→12, 600→12.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 570, 20, 32), First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(12, result[570], "offset@570");
        Near(12, result[600], "offset@600");
    }

    [Fact]
    public void Single90MinTile_OverflowDeferredToLastSlot()
    {
        // Tile 08:30–10:00 (90 min).  overflow = 9.
        // Last useful slot = 570.  All deferred there.
        // offsets: 510→0, 540→0, 570→0, 600→9.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 600, 30, 39), First, Last);

        Near(0, result[510], "offset@510");
        Near(0, result[540], "offset@540");
        Near(0, result[570], "offset@570");
        Near(9, result[600], "offset@600");
    }

    [Fact]
    public void TileInMiddleSlot_OnlyLaterGridlinesMoved()
    {
        // Tile 09:00–09:30.  overflow = 8.  Single slot at 540.
        // offsets: 510→0, 540→0, 570→8, 600→8.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(540, 570, 10, 18), First, Last);

        Near(0, result[510], "offset@510");
        Near(0, result[540], "offset@540");
        Near(8, result[570], "offset@570");
        Near(8, result[600], "offset@600");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Single-tile deferred expansion — non-30-minute-aligned tiles
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 45-minute tile starting ON a 30-minute boundary (08:30–09:15, i.e. 510–555).
    /// Covers slots 510 and 540.  Last slot = 540.  All overflow deferred there.
    /// </summary>
    [Fact]
    public void Tile45Min_StartAligned_DeferredToLastSlot()
    {
        // Tile 510–555, overflow = 6.  All at slot 540.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 555, 20, 26), First, Last);

        Near(0, result[510], "offset@510");
        Near(0, result[540], "offset@540");
        Near(6, result[570], "offset@570");
        Near(6, result[600], "offset@600");
    }

    /// <summary>
    /// 45-minute tile NOT starting on a 30-minute boundary (08:45–09:30, i.e. 525–570).
    /// Covers slots 510 and 540.  Last slot = 540.  All overflow deferred there.
    /// </summary>
    [Fact]
    public void Tile45Min_StartNonAligned_DeferredToLastSlot()
    {
        // Tile 525–570, overflow = 6.  All at slot 540.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 570, 20, 26), First, Last);

        Near(0, result[510], "offset@510");
        Near(0, result[540], "offset@540");
        Near(6, result[570], "offset@570");
        Near(6, result[600], "offset@600");
    }

    /// <summary>
    /// 15-minute tile entirely within one 30-minute slot (08:45–09:00, i.e. 525–540).
    /// Single slot at 510.  All overflow there.
    /// </summary>
    [Fact]
    public void Tile15Min_EntirelyWithinOneSlot_AllOverflowInThatSlot()
    {
        // Tile 525–540, overflow = 6.  All 6 px land in the 510 slot.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 540, 5, 11), First, Last);

        Near(0, result[510], "offset@510");
        Near(6, result[540], "offset@540");
        Near(6, result[570], "offset@570");
        Near(6, result[600], "offset@600");
    }

    /// <summary>
    /// 75-minute tile starting off-boundary (08:45–10:00, i.e. 525–600).
    /// Covers slots 510, 540, 570.  Last useful slot = 570 (= lastRowMinutes - 30).
    /// All overflow deferred to slot 570.
    /// </summary>
    [Fact]
    public void Tile75Min_StartNonAligned_DeferredToLastSlot()
    {
        // overflow = 75.  All at slot 570.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 600, 50, 125), First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(0,  result[570], "offset@570");
        Near(75, result[600], "offset@600");
    }

    /// <summary>
    /// 75-minute tile ending off a 30-minute boundary (08:30–09:45, i.e. 510–585).
    /// Covers slots 510, 540, 570.  Last slot = 570.  All overflow deferred there.
    /// </summary>
    [Fact]
    public void Tile75Min_EndNonAligned_DeferredToLastSlot()
    {
        // overflow = 75.  All at slot 570.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 585, 50, 125), First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(0,  result[570], "offset@570");
        Near(75, result[600], "offset@600");
    }

    /// <summary>
    /// 60-minute tile where both start AND end are off-boundary (08:45–09:45, i.e. 525–585).
    /// Covers slots 510, 540, 570.  Last slot = 570.  All overflow deferred there.
    /// </summary>
    [Fact]
    public void Tile60Min_BothEndpointsNonAligned_DeferredToLastSlot()
    {
        // overflow = 60.  All at slot 570.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(525, 585, 40, 100), First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(0,  result[570], "offset@570");
        Near(60, result[600], "offset@600");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Multi-tile interactions
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoTiles_DifferentSlots_ExpansionsAreIndependent()
    {
        // Tile A: 510–540 (slot 510), overflow = 6.
        // Tile B: 570–600 (slot 570), overflow = 9.
        // No shared slots → independent.
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
    public void TwoTiles_ShortEndingFirst_CreditReducesLongerTileDeficit()
    {
        // Tile A: 510–540, overflow = 4.  Last slot = 510.
        // Tile B: 510–570, overflow = 12. Last slot = 540.
        //   At slot 510: Tile A deficit = 4 → expansion = 4.
        //   At slot 540: Tile B prior = offset(540)-offset(510) = 4.
        //                deficit = 12 - 4 = 8 → expansion = 8.
        // offsets: 510→0, 540→4, 570→12, 600→12.
        // (Total = 12, same as overflow.  Old proportional gave 6+6=12 total
        //  but inflated slot 510 to 6, giving Tile A 2 px surplus.)
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 540)] = (10, 14),   // overflow = 4
            [(510, 570)] = (20, 32),   // overflow = 12
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(4,  result[540], "offset@540");
        Near(12, result[570], "offset@570");
        Near(12, result[600], "offset@600");
    }

    [Fact]
    public void TwoOverlappingTiles_CreditFromFirstReducesSecond()
    {
        // Tile A: 510–570 (last slot = 540), overflow = 20.
        // Tile B: 540–600 (last slot = 570), overflow = 24.
        //   Slot 540: Tile A deficit = 20 → expansion = 20.
        //   Slot 570: Tile B prior = offset(570)-offset(540) = 20.
        //             deficit = 24 - 20 = 4 → expansion = 4.
        // offsets: 510→0, 540→0, 570→20, 600→24.
        // (Old proportional: total = 34.  New greedy: total = 24.  10 px saved.)
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 570)] = (20, 40),   // overflow = 20
            [(540, 600)] = (20, 44),   // overflow = 24
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(20, result[570], "offset@570");
        Near(24, result[600], "offset@600");
    }

    [Fact]
    public void NonAlignedTiles_BothEndAtSameSlot_MaxDeficitWins()
    {
        // Tile A: 525–570 (45 min), overflow = 45.  Last slot = 540.
        // Tile B: 530–560 (30 min), overflow = 24.  Last slot = 540.
        // Both settle at slot 540.  No prior expansion → max(45, 24) = 45.
        // offsets: 510→0, 540→0, 570→45, 600→45.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(525, 570)] = (50,  95),   // overflow = 45
            [(530, 560)] = (50,  74),   // overflow = 24
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
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
        // Three non-overlapping 30-min tiles, each with different overflows.
        // Each is a single-slot tile → no credit interactions.
        // Tile A: 510–540, overflow = 3.
        // Tile B: 540–570, overflow = 5.
        // Tile C: 570–600, overflow = 7.
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
        // Tile's first covered slot = 540.  Last slot = 540.
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
        // Tile ends at 09:00 (540).  Last slot = ((540-1)/30)*30 = 510.
        // All expansion at slot 510.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 540, 10, 20), First, Last);

        Near(0,  result[510], "offset@510");
        Near(10, result[540], "offset@540");  // expansion from the 08:30 slot
        Near(10, result[570], "offset@570");
        Near(10, result[600], "offset@600");
    }

    [Fact]
    public void Tile_SpanningEntireGrid_DeferredToLastUsefulSlot()
    {
        // Tile 510–600 (90 min = 3 slots), overflow = 90.
        // Last useful slot = 570.  All deferred there.
        // offsets: 510→0, 540→0, 570→0, 600→90.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(510, 600, 10, 100), First, Last);

        Near(0,  result[510], "offset@510");
        Near(0,  result[540], "offset@540");
        Near(0,  result[570], "offset@570");
        Near(90, result[600], "offset@600");
    }

    [Fact]
    public void Tile_LargerThanGrid_ClampedToLastUsefulSlot()
    {
        // Tile 480–630 extends before and after the [510, 600] grid window.
        // First covered slot clamped to 510.  Last slot clamped to 570 (lastUsefulSlot).
        // All overflow deferred to slot 570.
        // overflow = 150.
        // offsets: 510→0, 540→0, 570→0, 600→150.
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            Map(480, 630, 50, 200), First, Last);

        Near(0,   result[510], "offset@510");
        Near(0,   result[540], "offset@540");
        Near(0,   result[570], "offset@570");
        Near(150, result[600], "offset@600");
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
        // Range 480–570 (4 gridlines: 480, 510, 540, 570).
        var result = ScheduleGridViewModel.ComputeGridlineOffsets(
            new Dictionary<(int, int), (double, double)>(), 480, 570);

        var expectedKeys = new HashSet<int> { 480, 510, 540, 570 };
        Assert.Equal(expectedKeys, result.Keys.ToHashSet());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Credit mechanism — verifies that earlier-tile expansion reduces
    // later-tile deficit, which is the core space-saving behaviour.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A 2-hour tile shares its first slot with a 30-min tile.  The 30-min tile's
    /// expansion at slot 510 becomes free credit for the 2-hour tile, reducing its
    /// deficit at slot 570 (its last slot).  Total grid expansion equals the larger
    /// tile's overflow — NOT the sum of both overflows.
    /// </summary>
    [Fact]
    public void CreditMechanism_ShortTileExpansionReducesLongTileDeficit()
    {
        // Tile A: 510–540, overflow = 50.  Last slot = 510.
        // Tile B: 510–630 (4 slots), overflow = 80.  Last useful slot = 570.
        //   At slot 510: Tile A → expansion = 50.
        //   At slot 570: Tile B prior = offset(570) - offset(510) = 50.
        //                deficit = 80 - 50 = 30.
        // Total expansion = 50 + 30 = 80 = Tile B's overflow.
        // Old proportional: slot 510 would get max(50, 20) = 50, slots 540/570 get 20 each,
        //   total = 50+20+20 = 90.
        var map = new Dictionary<(int, int), (double, double)>
        {
            [(510, 540)] = (10, 60),   // overflow = 50
            [(510, 630)] = (40, 120),  // overflow = 80
        };

        var result = ScheduleGridViewModel.ComputeGridlineOffsets(map, First, Last);

        Near(0,  result[510], "offset@510");
        Near(50, result[540], "offset@540");  // from Tile A
        Near(50, result[570], "offset@570");  // no new expansion
        Near(80, result[600], "offset@600");  // Tile B's remaining 30
    }
}

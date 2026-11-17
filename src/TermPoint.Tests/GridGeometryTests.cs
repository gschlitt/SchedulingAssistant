using System.Collections.Generic;
using System.Linq;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="GridGeometry"/>: minutes to Y and back (with and without expanded gridlines), and the pointer X to
/// day lookup (gutter, past the last day, folded days, several semesters per day).
/// </summary>
public sealed class GridGeometryTests
{
    private const int First = 8 * 60;
    private const int Last = 18 * 60;
    private const double Half = 30;

    /// <summary>Offsets that add 5 px for every half hour (a grid whose gridlines are all expanded).</summary>
    private static Dictionary<int, double> Expanded() =>
        Enumerable.Range(0, (Last - First) / 30 + 1).ToDictionary(i => First + i * 30, i => i * 5.0);

    /// <summary>Without expansion a half hour is exactly one half-hour height.</summary>
    [Fact]
    public void YOfMinutes_NoOffsets_IsLinear()
    {
        var none = new Dictionary<int, double>();
        Assert.Equal(0, GridGeometry.YOfMinutes(First, First, none, Half));
        Assert.Equal(30, GridGeometry.YOfMinutes(First + 30, First, none, Half));
        Assert.Equal(45, GridGeometry.YOfMinutes(First + 45, First, none, Half));
    }

    /// <summary>Offsets are added, interpolated between gridlines.</summary>
    [Fact]
    public void YOfMinutes_WithOffsets_AddsAndInterpolates()
    {
        var o = Expanded();
        Assert.Equal(35, GridGeometry.YOfMinutes(First + 30, First, o, Half));
        Assert.Equal(52.5, GridGeometry.YOfMinutes(First + 45, First, o, Half), 6);
    }

    /// <summary>Every minute maps to a Y and back to itself, with and without expansion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinutesAtY_RoundTrips(bool expanded)
    {
        var o = expanded ? Expanded() : new Dictionary<int, double>();
        for (int m = First; m <= Last; m++)
        {
            double y = GridGeometry.YOfMinutes(m, First, o, Half);
            Assert.Equal(m, GridGeometry.MinutesAtY(y, First, Last, o, Half));
        }
    }

    /// <summary>A Y between two minutes returns the nearer one.</summary>
    [Fact]
    public void MinutesAtY_BetweenMinutes_ReturnsNearer()
    {
        var none = new Dictionary<int, double>();
        // One minute is 1 px here: 10.4 px below the top is minute 10, 10.6 is minute 11.
        Assert.Equal(First + 10, GridGeometry.MinutesAtY(10.4, First, Last, none, Half));
        Assert.Equal(First + 11, GridGeometry.MinutesAtY(10.6, First, Last, none, Half));
    }

    /// <summary>Above the top and below the bottom clamp to the shown range.</summary>
    [Fact]
    public void MinutesAtY_Clamps()
    {
        var o = Expanded();
        Assert.Equal(First, GridGeometry.MinutesAtY(-100, First, Last, o, Half));
        Assert.Equal(Last, GridGeometry.MinutesAtY(100000, First, Last, o, Half));
    }

    /// <summary>Builds a grid of the given days (folded ones flagged) with <paramref name="sem"/> columns per day.</summary>
    private static GridData Grid(int sem, (int Day, bool Folded)[] days) =>
        new(First, Last,
            days.SelectMany(d => Enumerable.Range(0, sem)
                .Select(_ => new GridDayColumn("d", d.Day, [], IsFolded: d.Folded))).ToList(),
            sem);

    /// <summary>Column left edges of 100 px columns starting after a 52 px gutter.</summary>
    private static double[] Xs(int count, double width) => Enumerable.Range(0, count).Select(i => 52 + i * width).ToArray();

    /// <summary>Inside a day gives that day; the gutter gives the first; past the end gives the last.</summary>
    [Fact]
    public void NearestUnfoldedDay_InsideGutterAndPastEnd()
    {
        var g = Grid(1, [(1, false), (2, false), (3, false)]);
        var xs = Xs(3, 100);
        var ws = new[] { 100.0, 100, 100 };
        Assert.Equal(2, GridGeometry.NearestUnfoldedDay(170, g, xs, ws));
        Assert.Equal(1, GridGeometry.NearestUnfoldedDay(10, g, xs, ws));
        Assert.Equal(3, GridGeometry.NearestUnfoldedDay(1000, g, xs, ws));
    }

    /// <summary>Over a folded day the nearest unfolded day wins; day numbers with gaps are kept.</summary>
    [Fact]
    public void NearestUnfoldedDay_OverFolded_GoesToNearestUnfolded()
    {
        var g = Grid(1, [(1, false), (2, true), (5, false)]);
        var xs = Xs(3, 100);
        var ws = new[] { 100.0, 100, 100 };
        Assert.Equal(1, GridGeometry.NearestUnfoldedDay(190, g, xs, ws));   // 38 px from day 1, 62 from day 5
        Assert.Equal(5, GridGeometry.NearestUnfoldedDay(230, g, xs, ws));
        Assert.Equal(5, GridGeometry.NearestUnfoldedDay(260, g, xs, ws));
    }

    /// <summary>With two semester sub-columns a day spans both of its columns.</summary>
    [Fact]
    public void NearestUnfoldedDay_MultiSemester_SpansBothSubColumns()
    {
        var g = Grid(2, [(1, false), (2, false), (3, false)]);
        var xs = Xs(6, 50);
        var ws = Enumerable.Repeat(50.0, 6).ToArray();
        Assert.Equal(1, GridGeometry.NearestUnfoldedDay(60, g, xs, ws));
        Assert.Equal(1, GridGeometry.NearestUnfoldedDay(140, g, xs, ws));   // second sub-column of day 1
        Assert.Equal(2, GridGeometry.NearestUnfoldedDay(210, g, xs, ws));
        Assert.Equal(3, GridGeometry.NearestUnfoldedDay(340, g, xs, ws));
    }

    /// <summary>Nothing unfolded (or nothing shown) gives null.</summary>
    [Fact]
    public void NearestUnfoldedDay_AllFolded_IsNull()
    {
        var g = Grid(1, [(1, true), (2, true)]);
        Assert.Null(GridGeometry.NearestUnfoldedDay(100, g, Xs(2, 100), new[] { 100.0, 100 }));
        Assert.Null(GridGeometry.NearestUnfoldedDay(100, GridData.Empty, [], []));
    }

    /// <summary>Auto-scroll: none in the middle, a linear ramp inside each edge band, full speed at and past the edge.</summary>
    [Fact]
    public void AutoScrollSpeed_RampsInsideTheBands()
    {
        const double len = 400, band = 40, max = 1000;
        Assert.Equal(0, GridGeometry.AutoScrollSpeed(200, len, band, max));
        Assert.Equal(0, GridGeometry.AutoScrollSpeed(40, len, band, max));
        Assert.Equal(0, GridGeometry.AutoScrollSpeed(360, len, band, max));
        Assert.Equal(-500, GridGeometry.AutoScrollSpeed(20, len, band, max), 6);
        Assert.Equal(500, GridGeometry.AutoScrollSpeed(380, len, band, max), 6);
        Assert.Equal(-1000, GridGeometry.AutoScrollSpeed(0, len, band, max), 6);
        Assert.Equal(1000, GridGeometry.AutoScrollSpeed(400, len, band, max), 6);
        Assert.Equal(-1000, GridGeometry.AutoScrollSpeed(-50, len, band, max), 6);
        Assert.Equal(1000, GridGeometry.AutoScrollSpeed(900, len, band, max), 6);
    }

    /// <summary>Auto-scroll: a viewport narrower than two bands shrinks them so the middle stays still; no band or no viewport means no scrolling.</summary>
    [Fact]
    public void AutoScrollSpeed_SmallViewportAndDisabled()
    {
        Assert.Equal(0, GridGeometry.AutoScrollSpeed(30, 60, 40, 1000));
        Assert.True(GridGeometry.AutoScrollSpeed(10, 60, 40, 1000) < 0);
        Assert.True(GridGeometry.AutoScrollSpeed(50, 60, 40, 1000) > 0);
        Assert.Equal(0, GridGeometry.AutoScrollSpeed(0, 400, 0, 1000));
        Assert.Equal(0, GridGeometry.AutoScrollSpeed(0, 0, 40, 1000));
    }
}

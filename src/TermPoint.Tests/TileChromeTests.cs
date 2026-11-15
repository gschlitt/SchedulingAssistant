using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using TermPoint.Views.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="TileChrome"/>, the pure calculation of how much horizontal room a Schedule-grid tile
/// needs around its text (gap, borders, paddings, selection border, flag icon, measuring margin).
/// No Avalonia platform is needed; the parts are passed in directly. One test also reads <c>AppLayout.axaml</c>
/// so the shipping numbers are pinned: changing a tile resource there fails it on purpose, as a reminder to
/// re-run <c>tools/TileWidthProbe</c> (the clipping regression check) before shipping.
/// </summary>
public class TileChromeTests
{
    /// <summary>Tolerance for floating-point comparisons.</summary>
    private const double Eps = 1e-9;

    /// <summary>
    /// The parts as <c>AppLayout.axaml</c> ships them: gap 4, borders 1 / 3 / outer 4, content padding 3,2,
    /// row padding 1,0, selection border 3, flag 12 with margin 3,2,0,0, measuring margin 1.
    /// </summary>
    private static TileChrome Shipping() => new(
        Gap: 4, BorderThin: 1, BorderThick: 3, OverlayOuterBorder: 4,
        ContentPadding: new Thickness(3, 2), EntryRowPadding: new Thickness(1, 0),
        SelectionBorder: 3, FlagIconSize: 12, FlagIconMargin: new Thickness(3, 2, 0, 0), MeasuringMargin: 1);

    /// <summary>One semester shown, no overlay: gap 4 + borders 2 + padding 6 + row padding 2 + selection 6 + margin 1.</summary>
    [Fact]
    public void PlainTile_OverheadIsTheSumOfItsParts()
    {
        Assert.Equal(4 + 2 * 1 + 6 + 2 + 6 + 1, Shipping().TileOverhead(hasOverlay: false, hasSemesterColour: false), Eps);
    }

    /// <summary>Several semesters shown: the tile has a thick semester-colour border (3 per side instead of 1).</summary>
    [Fact]
    public void SemesterColourTile_HasThickBorderOnBothSides()
    {
        var chrome = Shipping();
        double plain = chrome.TileOverhead(false, false);
        double multi = chrome.TileOverhead(false, true);
        Assert.Equal(2 * (3 - 1), multi - plain, Eps);
        Assert.Equal(25, multi, Eps);
    }

    /// <summary>An overlay tile in single-semester mode has the same thick border as a semester-colour tile.</summary>
    [Fact]
    public void OverlayTile_SingleSemester_HasThickBorder()
    {
        var chrome = Shipping();
        Assert.Equal(chrome.TileOverhead(false, true), chrome.TileOverhead(true, false), Eps);
    }

    /// <summary>
    /// An overlay tile in multi-semester mode is drawn with a dual border: outer 4 plus inner 3 per side, so it
    /// is 2 * 4 = 8 px wider than the same tile with one thick border.
    /// </summary>
    [Fact]
    public void OverlayTile_MultiSemester_HasDualBorder()
    {
        var chrome = Shipping();
        double dual = chrome.TileOverhead(true, true);
        Assert.Equal(2 * 4, dual - chrome.TileOverhead(true, false), Eps);
        Assert.Equal(33, dual, Eps);
    }

    /// <summary>Plain tile: 2 borders of 1 plus content padding 2 + 2 = 6 px of vertical chrome.</summary>
    [Fact]
    public void ChromeHeight_PlainTile()
    {
        Assert.Equal(2 * 1 + 2 + 2, Shipping().ChromeHeight(false, false), Eps);
    }

    /// <summary>Semester colour and overlay tiles (single-semester) have a thick border: 2 * 3 + 4 = 10 px.</summary>
    [Fact]
    public void ChromeHeight_SemesterColourAndOverlay()
    {
        var chrome = Shipping();
        Assert.Equal(10, chrome.ChromeHeight(false, true), Eps);
        Assert.Equal(10, chrome.ChromeHeight(true, false), Eps);
    }

    /// <summary>Dual overlay (overlay in multi-semester mode): 2 * (4 + 3) + 4 = 18 px.</summary>
    [Fact]
    public void ChromeHeight_DualOverlay()
    {
        Assert.Equal(18, Shipping().ChromeHeight(true, true), Eps);
    }

    /// <summary>Width and height chrome move together when a border changes, and only horizontal or vertical padding moves its own.</summary>
    [Fact]
    public void ChromeHeight_FollowsBordersAndVerticalPaddingOnly()
    {
        var chrome = Shipping();
        Assert.Equal(chrome.ChromeHeight(false, false) + 2, (chrome with { BorderThin = 2 }).ChromeHeight(false, false), Eps);
        Assert.Equal(chrome.ChromeHeight(false, false) + 2, (chrome with { ContentPadding = new Thickness(3, 3) }).ChromeHeight(false, false), Eps);
        Assert.Equal(chrome.ChromeHeight(false, false), (chrome with { ContentPadding = new Thickness(9, 2) }).ChromeHeight(false, false), Eps);
    }

    /// <summary>A line is as tall as its text, the flag icon if that is taller, plus row padding top and bottom.</summary>
    [Fact]
    public void EntryHeight_TextFlagAndRowPadding()
    {
        var chrome = Shipping();
        Assert.Equal(14, chrome.EntryHeight(14, flagged: false), Eps);
        Assert.Equal(14, chrome.EntryHeight(14, flagged: true), Eps);   // flag 12 + margin 2 = 14
        Assert.Equal(14, chrome.EntryHeight(10, flagged: true), Eps);   // small text: the flag is taller
        Assert.Equal(10, chrome.EntryHeight(10, flagged: false), Eps);
        Assert.Equal(14 + 3, (chrome with { EntryRowPadding = new Thickness(1, 1.5) }).EntryHeight(14, false), Eps);
    }

    /// <summary>A flagged line needs the flag's size plus its left and right margin on top of its text.</summary>
    [Fact]
    public void FlaggedEntry_AddsTheFlagWidth()
    {
        var chrome = Shipping();
        Assert.Equal(15, chrome.FlagWidth, Eps);
        Assert.Equal(100, chrome.EntryWidth(100, markerWidth: 0, flagged: false), Eps);
        Assert.Equal(115, chrome.EntryWidth(100, markerWidth: 0, flagged: true), Eps);
    }

    /// <summary>A line with a subject marker and a flag counts both, in full, beside its text.</summary>
    [Fact]
    public void EntryWidth_CountsMarkerAndFlagTogether()
    {
        Assert.Equal(100 + 13 + 15, Shipping().EntryWidth(100, markerWidth: 13, flagged: true), Eps);
    }

    /// <summary>The flag width follows its margin on BOTH sides, not just the left.</summary>
    [Fact]
    public void FlagWidth_UsesBothMargins()
    {
        var chrome = Shipping() with { FlagIconMargin = new Thickness(3, 2, 5, 0) };
        Assert.Equal(12 + 3 + 5, chrome.FlagWidth, Eps);
    }

    /// <summary>Each part moves the overhead by exactly its own amount (gap 1:1, margin 1:1, paddings counted twice).</summary>
    [Fact]
    public void EachPart_MovesTheOverheadByItsOwnAmount()
    {
        var chrome = Shipping();
        double baseline = chrome.TileOverhead(false, false);
        Assert.Equal(baseline + 2, (chrome with { Gap = 6 }).TileOverhead(false, false), Eps);
        Assert.Equal(baseline + 3, (chrome with { MeasuringMargin = 4 }).TileOverhead(false, false), Eps);
        Assert.Equal(baseline + 2, (chrome with { SelectionBorder = 4 }).TileOverhead(false, false), Eps);
        Assert.Equal(baseline + 2, (chrome with { BorderThin = 2 }).TileOverhead(false, false), Eps);
        Assert.Equal(baseline + 2, (chrome with { ContentPadding = new Thickness(4, 2) }).TileOverhead(false, false), Eps);
        Assert.Equal(baseline + 2, (chrome with { EntryRowPadding = new Thickness(2, 0) }).TileOverhead(false, false), Eps);
    }

    /// <summary>Vertical-only values (top and bottom padding, flag top margin) never change a width.</summary>
    [Fact]
    public void VerticalParts_DoNotAffectWidth()
    {
        var chrome = Shipping();
        var taller = chrome with
        {
            ContentPadding = new Thickness(3, 9), EntryRowPadding = new Thickness(1, 7),
            FlagIconMargin = new Thickness(3, 8, 0, 4),
        };
        Assert.Equal(chrome.TileOverhead(false, false), taller.TileOverhead(false, false), Eps);
        Assert.Equal(chrome.FlagWidth, taller.FlagWidth, Eps);
    }

    /// <summary>
    /// Pins the shipping numbers in <c>AppLayout.axaml</c> to the values the other tests assume. If this fails
    /// after a deliberate change to a tile resource, update <see cref="Shipping"/> AND re-run
    /// <c>tools/TileWidthProbe</c> to confirm no label clips. Skips (passes) when the source tree is not present.
    /// </summary>
    [Fact]
    public void AppLayoutAxaml_HoldsTheShippingTileValues()
    {
        string? path = FindAppLayout();
        if (path is null) return; // source tree not next to the test binaries (for example a packaged run)

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var values = XDocument.Load(path).Descendants()
            .Where(e => e.Attribute(x + "Key") != null)
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value.Trim());

        double D(string key) => double.Parse(values[key], CultureInfo.InvariantCulture);
        Thickness T(string key) => Thickness.Parse(values[key]);

        var fromFile = new TileChrome(
            Gap: D("TilePaddingHorizontal"), BorderThin: D("TileBorderThickness"), BorderThick: D("TileThickBorderThickness"),
            OverlayOuterBorder: D("TileOverlayOuterBorderThickness"), ContentPadding: T("TileContentPadding"),
            EntryRowPadding: T("TileEntryRowPadding"), SelectionBorder: D("TileSelectionBorderThickness"),
            FlagIconSize: D("TileFlagIconSize"), FlagIconMargin: T("TileFlagIconMargin"), MeasuringMargin: D("TileMeasuringMargin"));
        Assert.Equal(Shipping(), fromFile);
        // The gap between days (spec collapse-days.md, task 1: Greg kept 16 px, as in 1.2.3).
        Assert.Equal(16, D("DayEdgeGap"));
    }

    // ─── Day-edge gap: slot and content insets ───────────────────────────────

    /// <summary>Gap between tiles (P), the day-edge gap (G) and the internal semester pad used by the inset tests.</summary>
    private const double P = 4, G = 16, Pad = 0;

    /// <summary>Single semester: left inset G/2, right inset G/2 - P (the last tile's slot carries one P).</summary>
    [Fact]
    public void SlotInsets_SingleSemester()
    {
        Assert.Equal((8.0, 4.0), TileChrome.SlotInsets(0, 1, P, Pad, G));
    }

    /// <summary>Several semesters: first sub-column has the outer left, last has the outer right, middle has only pads.</summary>
    [Fact]
    public void SlotInsets_SeveralSemesters_FirstMiddleLast()
    {
        Assert.Equal((G / 2, Pad), TileChrome.SlotInsets(0, 3, P, Pad, G));
        Assert.Equal((Pad, Pad), TileChrome.SlotInsets(1, 3, P, Pad, G));
        Assert.Equal((Pad, G / 2 - P), TileChrome.SlotInsets(2, 3, P, Pad, G));
        Assert.Equal((7.0, 7.0), TileChrome.SlotInsets(1, 3, P, 7, G));
    }

    /// <summary>Content (bar / ghost) insets: half the day-edge gap on both outer edges; pads inside a day.</summary>
    [Fact]
    public void ContentInsets_OuterEdgesAreHalfTheDayEdgeGap()
    {
        Assert.Equal((8.0, 8.0), TileChrome.ContentInsets(0, 1, Pad, G));
        Assert.Equal((8.0, Pad), TileChrome.ContentInsets(0, 2, Pad, G));
        Assert.Equal((Pad, 8.0), TileChrome.ContentInsets(1, 2, Pad, G));
    }

    /// <summary>
    /// Lays two single-semester days of width 200 side by side (one tile slot each, filling the slot area) and
    /// checks the drawn gaps: the last tile of day 1 to the first of day 2 is exactly G; two tiles side by side in a
    /// day (slots of 90 and 90 inside a 200 column with insets 8 and 4) are exactly P apart.
    /// </summary>
    [Fact]
    public void Gaps_DayToDayIsDayEdgeGap_AndInDayIsTileGap()
    {
        const double col = 200;
        var (l, r) = TileChrome.SlotInsets(0, 1, P, Pad, G);
        // One tile filling the slot area: its drawn width is slot - P.
        double slot = col - l - r;
        double tile1Right = 0 + l + slot - P;          // day 1 at X = 0
        double tile2Left = col + l;                     // day 2 at X = col
        Assert.Equal(G, tile2Left - tile1Right, Eps);

        // Two tiles in one day: slots a and b adjacent, each drawn P narrower than its slot.
        double a = slot / 2, b = slot / 2;
        double aRight = l + a - P, bLeft = l + a;
        Assert.Equal(P, bLeft - aRight, Eps);
    }

    /// <summary>Walks up from the test binaries to find <c>src/TermPoint/AppLayout.axaml</c>, or null when absent.</summary>
    /// <returns>The file's full path, or null.</returns>
    private static string? FindAppLayout()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "TermPoint", "AppLayout.axaml");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

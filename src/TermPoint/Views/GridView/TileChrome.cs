using Avalonia;

namespace TermPoint.Views.GridView;

/// <summary>
/// The horizontal "chrome" of a Schedule-grid tile: every border, padding and icon that sits between the edge of a
/// tile and the text of one of its lines. <see cref="ScheduleGridView"/> reads each value from
/// <c>AppLayout.axaml</c> once per render and uses the SAME values both to draw the tiles and to work out how wide a
/// tile must be (<see cref="TileOverhead"/>, <see cref="EntryWidth"/>), so the drawn tile and its width can't drift
/// apart. The calculation itself is pure (no Avalonia controls), so it is unit-tested directly.
/// </summary>
/// <param name="Gap">Gap between side-by-side tiles (<c>TilePaddingHorizontal</c>). Each tile's slot includes it.</param>
/// <param name="BorderThin">Border thickness of a plain tile: one semester shown, no overlay (<c>TileBorderThickness</c>).</param>
/// <param name="BorderThick">Border thickness of a tile with a semester colour or any overlay tile (<c>TileThickBorderThickness</c>).</param>
/// <param name="OverlayOuterBorder">Outer border of an overlay tile that also has a semester colour (<c>TileOverlayOuterBorderThickness</c>).</param>
/// <param name="ContentPadding">Padding between a tile's (innermost) border and its lines (<c>TileContentPadding</c>); only Left and Right count here.</param>
/// <param name="EntryRowPadding">Padding inside one line's row (<c>TileEntryRowPadding</c>); only Left and Right count here.</param>
/// <param name="SelectionBorder">Border added to each side of a selected line (<c>TileSelectionBorderThickness</c>). Counted always, so selecting never needs a re-layout.</param>
/// <param name="FlagIconSize">Width of a flag icon (<c>TileFlagIconSize</c>).</param>
/// <param name="FlagIconMargin">Margin around a flag icon (<c>TileFlagIconMargin</c>); only Left and Right count here.</param>
/// <param name="MeasuringMargin">Small allowance for rounding in text measurement (<c>TileMeasuringMargin</c>).</param>
internal readonly record struct TileChrome(
    double Gap,
    double BorderThin,
    double BorderThick,
    double OverlayOuterBorder,
    Thickness ContentPadding,
    Thickness EntryRowPadding,
    double SelectionBorder,
    double FlagIconSize,
    Thickness FlagIconMargin,
    double MeasuringMargin)
{
    /// <summary>
    /// Horizontal space a tile takes on BOTH sides between its edges and its lines: the border (or borders) plus the
    /// content padding. The render code draws exactly this: a plain tile has one border; a tile with a semester
    /// colour or any overlay has a thick one; an overlay tile that also has a semester colour has an outer border, an
    /// inner (semester colour) border and the padding.
    /// </summary>
    /// <param name="hasOverlay">True when any line of the tile is an overlay line.</param>
    /// <param name="hasSemesterColour">True when the tile is drawn in a semester colour (several semesters shown).</param>
    /// <returns>Pixels across both sides.</returns>
    public double ChromeWidth(bool hasOverlay, bool hasSemesterColour)
    {
        double borderPerSide = hasOverlay && hasSemesterColour ? OverlayOuterBorder + BorderThick
                             : hasOverlay || hasSemesterColour ? BorderThick
                             : BorderThin;
        return 2 * borderPerSide + ContentPadding.Left + ContentPadding.Right;
    }

    /// <summary>
    /// Vertical space a tile takes top and bottom between its edges and its lines: the same border (or borders) as
    /// <see cref="ChromeWidth"/> plus the content padding's top and bottom. A tile's drawn height includes it, so
    /// the height needed for its lines must too.
    /// </summary>
    /// <param name="hasOverlay">True when any line of the tile is an overlay line.</param>
    /// <param name="hasSemesterColour">True when the tile is drawn in a semester colour.</param>
    /// <returns>Pixels across top and bottom.</returns>
    public double ChromeHeight(bool hasOverlay, bool hasSemesterColour)
    {
        double borderPerSide = hasOverlay && hasSemesterColour ? OverlayOuterBorder + BorderThick
                             : hasOverlay || hasSemesterColour ? BorderThick
                             : BorderThin;
        return 2 * borderPerSide + ContentPadding.Top + ContentPadding.Bottom;
    }

    /// <summary>Height of a flag icon on a line: its size plus its top and bottom margin.</summary>
    public double FlagHeight => FlagIconSize + FlagIconMargin.Top + FlagIconMargin.Bottom;

    /// <summary>
    /// The height one line occupies: the taller of its text line and (when flagged) its flag icon, plus the row's top
    /// and bottom padding. A selected line's border is added separately by the caller (it depends on selection).
    /// </summary>
    /// <param name="textLineHeight">Height of one text line in the drawn font.</param>
    /// <param name="flagged">True when the line shows a flag icon.</param>
    /// <returns>Pixels the line's row needs, before any selection border.</returns>
    public double EntryHeight(double textLineHeight, bool flagged) =>
        Math.Max(textLineHeight, flagged ? FlagHeight : 0) + EntryRowPadding.Top + EntryRowPadding.Bottom;

    /// <summary>
    /// Everything a tile adds to the width of its widest line: the gap to its neighbour, both sides' borders and
    /// padding, the line row's padding, room for a selected line's border, and the measuring margin. It does not
    /// include a subject marker or a flag icon; those belong to a line and are added by <see cref="EntryWidth"/>.
    /// </summary>
    /// <param name="hasOverlay">True when any line of the tile is an overlay line.</param>
    /// <param name="hasSemesterColour">True when the tile is drawn in a semester colour.</param>
    /// <returns>Pixels to add to the widest line's width to get the tile's slot width.</returns>
    public double TileOverhead(bool hasOverlay, bool hasSemesterColour) =>
        Gap
        + ChromeWidth(hasOverlay, hasSemesterColour)
        + EntryRowPadding.Left + EntryRowPadding.Right
        + 2 * SelectionBorder
        + MeasuringMargin;

    /// <summary>The width a flag icon takes on a line: its size plus its left and right margin.</summary>
    public double FlagWidth => FlagIconSize + FlagIconMargin.Left + FlagIconMargin.Right;

    /// <summary>
    /// The width one line needs: its measured (Bold) text, plus its subject marker if it has one, plus its flag icon
    /// if it is flagged. The icon and marker don't shrink, so they are counted in full (the text is what gets trimmed).
    /// </summary>
    /// <param name="textWidth">Measured width of the line's label in its heaviest weight.</param>
    /// <param name="markerWidth">Width of the subject marker and its gap, or 0 when the line has none.</param>
    /// <param name="flagged">True when the line shows a flag icon.</param>
    /// <returns>Pixels the line needs inside its row.</returns>
    public double EntryWidth(double textWidth, double markerWidth, bool flagged) =>
        textWidth + markerWidth + (flagged ? FlagWidth : 0);

    /// <summary>
    /// The horizontal insets of a day column's TILE SLOTS (where tiles are laid out), for one flat sub-column.
    /// A day group's outer left edge is inset by half the day-edge gap; its outer right edge by half the day-edge gap
    /// minus <paramref name="gap"/>, because the last tile's slot already carries one gap (a tile is drawn
    /// <c>gap</c> narrower than its slot). So the last tile of one day and the first tile of the next are exactly
    /// <paramref name="dayEdgeGap"/> apart, while tiles inside a day are <paramref name="gap"/> apart. Boundaries
    /// between semester sub-columns of the same day use <paramref name="multiSemPad"/> instead.
    /// </summary>
    /// <param name="subIdx">Index of the semester sub-column within its day group (0 in single-semester mode).</param>
    /// <param name="semCount">Number of semester sub-columns per day (1 in single-semester mode).</param>
    /// <param name="gap">Gap between side-by-side tiles (<c>TilePaddingHorizontal</c>).</param>
    /// <param name="multiSemPad">Inset on each side of an internal semester boundary (<c>MultiSemesterInternalTilePadding</c>).</param>
    /// <param name="dayEdgeGap">Total gap between the last tile of one day and the first of the next (<c>DayEdgeGap</c>).</param>
    /// <returns>The left and right inset, in pixels, to subtract from the column width to get the slot area.</returns>
    public static (double Left, double Right) SlotInsets(int subIdx, int semCount, double gap, double multiSemPad, double dayEdgeGap)
    {
        bool multi = semCount > 1;
        double left  = multi && subIdx > 0 ? multiSemPad : dayEdgeGap / 2;
        double right = multi && subIdx < semCount - 1 ? multiSemPad : dayEdgeGap / 2 - gap;
        return (left, right);
    }

    /// <summary>
    /// The horizontal insets of a day column's visible CONTENT edges (semester bars, ghost outlines): the same as
    /// <see cref="SlotInsets"/> but with the full half day-edge gap on the outer right, so they end exactly where the
    /// last tile's drawn border ends.
    /// </summary>
    /// <param name="subIdx">Index of the semester sub-column within its day group.</param>
    /// <param name="semCount">Number of semester sub-columns per day.</param>
    /// <param name="multiSemPad">Inset on each side of an internal semester boundary.</param>
    /// <param name="dayEdgeGap">Total gap between the last tile of one day and the first of the next.</param>
    /// <returns>The left and right inset, in pixels.</returns>
    public static (double Left, double Right) ContentInsets(int subIdx, int semCount, double multiSemPad, double dayEdgeGap)
    {
        bool multi = semCount > 1;
        return (multi && subIdx > 0 ? multiSemPad : dayEdgeGap / 2,
                multi && subIdx < semCount - 1 ? multiSemPad : dayEdgeGap / 2);
    }
}

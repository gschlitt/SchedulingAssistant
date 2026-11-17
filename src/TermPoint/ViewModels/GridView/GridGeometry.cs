namespace TermPoint.ViewModels.GridView;

/// <summary>
/// The pure pixel/time arithmetic of the Schedule View grid, kept free of Avalonia so it is unit-tested: minutes to
/// Y (with the expanded gridlines), the inverse (pointer Y to minutes), and pointer X to the day under it. The view
/// draws with these same functions, so a dragged ghost lines up with the tiles.
/// </summary>
public static class GridGeometry
{
    /// <summary>
    /// Returns the cumulative gridline expansion offset for <paramref name="minutes"/>. The dictionary is keyed at
    /// 30-minute intervals; if <paramref name="minutes"/> does not land on one (a section with a non-standard
    /// duration), the value is interpolated linearly between the two surrounding gridlines so drawing degrades
    /// gracefully instead of failing.
    /// </summary>
    /// <param name="offsets">The gridline offsets built when the grid was drawn, keyed by minutes from midnight.</param>
    /// <param name="minutes">The time, in minutes from midnight.</param>
    /// <returns>The cumulative Y expansion in pixels (0 when the dictionary has no entries around the time).</returns>
    public static double GridlineOffset(IReadOnlyDictionary<int, double> offsets, int minutes)
    {
        if (offsets.TryGetValue(minutes, out var exact)) return exact;

        var lower = (minutes / 30) * 30;
        var upper = lower + 30;
        var lo    = offsets.TryGetValue(lower, out var loVal) ? loVal : 0;
        var hi    = offsets.TryGetValue(upper, out var hiVal) ? hiVal : lo;
        return lo + (hi - lo) * ((minutes - lower) / 30.0);
    }

    /// <summary>
    /// The Y of a time, relative to the top of the time area (just below the day header).
    /// </summary>
    /// <param name="minutes">The time, in minutes from midnight.</param>
    /// <param name="firstRowMinutes">The time at the top of the time area.</param>
    /// <param name="offsets">The gridline offsets (see <see cref="GridlineOffset"/>).</param>
    /// <param name="halfHourHeight">Pixels per half hour before any gridline expansion.</param>
    /// <returns>The Y in pixels.</returns>
    public static double YOfMinutes(int minutes, int firstRowMinutes, IReadOnlyDictionary<int, double> offsets, double halfHourHeight) =>
        (minutes - firstRowMinutes) / 30.0 * halfHourHeight + GridlineOffset(offsets, minutes);

    /// <summary>
    /// The inverse of <see cref="YOfMinutes"/>: the whole minute whose Y is nearest <paramref name="y"/>, clamped to
    /// the shown range. <see cref="YOfMinutes"/> never decreases with the time, so a binary search finds it.
    /// </summary>
    /// <param name="y">The Y, relative to the top of the time area.</param>
    /// <param name="firstRowMinutes">The first shown time.</param>
    /// <param name="lastRowMinutes">The last shown time.</param>
    /// <param name="offsets">The gridline offsets (see <see cref="GridlineOffset"/>).</param>
    /// <param name="halfHourHeight">Pixels per half hour before any gridline expansion.</param>
    /// <returns>Minutes from midnight in [<paramref name="firstRowMinutes"/>, <paramref name="lastRowMinutes"/>]; the earlier minute on a tie.</returns>
    public static int MinutesAtY(double y, int firstRowMinutes, int lastRowMinutes,
        IReadOnlyDictionary<int, double> offsets, double halfHourHeight)
    {
        if (lastRowMinutes <= firstRowMinutes) return firstRowMinutes;
        double YOf(int m) => YOfMinutes(m, firstRowMinutes, offsets, halfHourHeight);

        if (y <= YOf(firstRowMinutes)) return firstRowMinutes;
        if (y >= YOf(lastRowMinutes)) return lastRowMinutes;

        // Smallest minute whose Y is at or past y.
        int lo = firstRowMinutes, hi = lastRowMinutes;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (YOf(mid) >= y) hi = mid; else lo = mid + 1;
        }
        return y - YOf(lo - 1) <= YOf(lo) - y ? lo - 1 : lo;
    }

    /// <summary>
    /// The day under a pointer X: the day whose columns contain it, otherwise the nearest day that is not folded.
    /// Folded days are never a result. A day's columns are the <see cref="GridData.SemesterCount"/> contiguous flat
    /// columns of that day; its span runs from the first column's left edge to the last column's right edge.
    /// </summary>
    /// <param name="x">The pointer X in canvas coordinates.</param>
    /// <param name="data">The grid being shown.</param>
    /// <param name="dayXOffsets">Left edge of each flat column.</param>
    /// <param name="dayColWidths">Width of each flat column.</param>
    /// <returns>The day number (1 = Monday ... 7 = Sunday), or null when every shown day is folded or none is shown.</returns>
    public static int? NearestUnfoldedDay(double x, GridData data, IReadOnlyList<double> dayXOffsets, IReadOnlyList<double> dayColWidths)
    {
        int semCount = Math.Max(1, data.SemesterCount);
        int? best = null;
        double bestDistance = double.MaxValue;

        for (int first = 0; first < data.DayColumns.Count; first += semCount)
        {
            if (data.DayColumns[first].IsFolded) continue;
            int last = Math.Min(first + semCount, data.DayColumns.Count) - 1;
            if (last >= dayXOffsets.Count || last >= dayColWidths.Count) break;

            double left = dayXOffsets[first];
            double right = dayXOffsets[last] + dayColWidths[last];
            double distance = x < left ? left - x : x > right ? x - right : 0;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = data.DayColumns[first].DayNumber;
            }
        }
        return best;
    }

    /// <summary>
    /// The auto-scroll speed along one axis while a meeting is dragged near (or past) an edge of the grid's viewport.
    /// Zero in the middle; inside the band along an edge the speed grows linearly from 0 at the band's inner side to
    /// <paramref name="maxSpeed"/> at the edge, and stays at <paramref name="maxSpeed"/> past it (the canvas holds
    /// pointer capture, so the pointer can leave the viewport).
    /// </summary>
    /// <param name="position">The pointer along this axis, relative to the viewport's start edge (may be negative or beyond <paramref name="viewportLength"/>).</param>
    /// <param name="viewportLength">The viewport's length along this axis.</param>
    /// <param name="band">Width of the band along each edge that scrolls; 0 or less disables auto-scroll.</param>
    /// <param name="maxSpeed">Speed at and past the edge, in viewport pixels per second.</param>
    /// <returns>Negative to scroll toward the start (up/left), positive toward the end (down/right), 0 for none. When the viewport is narrower than two bands, the nearer edge wins.</returns>
    public static double AutoScrollSpeed(double position, double viewportLength, double band, double maxSpeed)
    {
        if (band <= 0 || viewportLength <= 0) return 0;
        double effectiveBand = Math.Min(band, viewportLength / 2);
        double fromStart = position;
        double fromEnd = viewportLength - position;
        if (fromStart < effectiveBand && fromStart <= fromEnd)
            return -maxSpeed * Math.Clamp((effectiveBand - fromStart) / effectiveBand, 0, 1);
        if (fromEnd < effectiveBand)
            return maxSpeed * Math.Clamp((effectiveBand - fromEnd) / effectiveBand, 0, 1);
        return 0;
    }
}

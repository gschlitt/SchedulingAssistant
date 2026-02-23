namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// One section entry within a tile.
/// Label = "HIST101 A" (course code + section code, or just section code)
/// Initials = instructor initials (may be empty)
/// </summary>
public record TileEntry(string Label, string Initials);

/// <summary>
/// A single tile drawn on the grid, potentially containing multiple co-scheduled sections
/// (same start time and duration).
/// </summary>
public record GridTile(
    IReadOnlyList<TileEntry> Entries,
    int StartMinutes,
    int EndMinutes,
    /// <summary>0-based column index within an overlap cluster.</summary>
    int OverlapIndex,
    /// <summary>Total number of columns in the overlap cluster.</summary>
    int OverlapCount);

/// <summary>One day column's worth of positioned tiles.</summary>
public record GridDayColumn(string Header, IReadOnlyList<GridTile> Tiles);

/// <summary>All data needed by the view to render the schedule grid.</summary>
public record GridData(
    /// <summary>Start of the visible time range, snapped to the half-hour at or before the earliest start.</summary>
    int FirstRowMinutes,
    /// <summary>End of the visible time range, snapped to the half-hour at or after the latest end.</summary>
    int LastRowMinutes,
    IReadOnlyList<GridDayColumn> DayColumns)
{
    public static readonly GridData Empty = new(480, 22 * 60, []);
    public bool HasData => DayColumns.Any(d => d.Tiles.Count > 0);
}

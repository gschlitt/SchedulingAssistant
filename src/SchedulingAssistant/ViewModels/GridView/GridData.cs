namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// A single section meeting to be drawn as a tile.
/// Line1 = course calendar code (or section code if no course)
/// Line2 = section code
/// Line3 = instructor initials (may be empty)
/// </summary>
public record GridTile(
    string Line1,
    string Line2,
    string Line3,
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
    public static readonly GridData Empty = new(480, 1260, []);
    public bool HasData => DayColumns.Any(d => d.Tiles.Count > 0);
}

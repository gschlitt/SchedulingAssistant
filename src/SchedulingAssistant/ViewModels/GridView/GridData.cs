namespace SchedulingAssistant.ViewModels.GridView;

/// <summary>
/// Abstract base for any time-positioned block that can be placed on the schedule grid.
/// Both section meetings and instructor commitments derive from this type.
/// The layout engine (ComputeTiles) operates entirely on GridBlocks — no subtype
/// branching occurs during overlap detection or column assignment.
/// </summary>
public abstract record GridBlock(int Day, int StartMinutes, int EndMinutes, bool IsOverlay);

/// <summary>A scheduled meeting for a section, with optional overlay highlighting.</summary>
public record SectionMeetingBlock(
    int Day, int StartMinutes, int EndMinutes, bool IsOverlay,
    string Label, string Initials, string SectionId
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay);

/// <summary>
/// An instructor commitment (non-teaching obligation).
/// Always rendered as an overlay tile (red border, red text, Name as label).
/// Has no associated section; participates in the same overlap layout as section meetings.
/// </summary>
public record CommitmentBlock(
    int Day, int StartMinutes, int EndMinutes,
    string Name, string CommitmentId
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay: true);

/// <summary>
/// One entry within a tile.
/// Label    = display text (e.g. "HIST101 A" for sections, commitment name for commitments)
/// Initials = instructor initials — empty for commitments
/// SectionId = the section's database ID (used for selection/highlighting) — empty for commitments
/// IsOverlay = true if rendered with red border/text
/// IsCommitment = true for commitment entries (display-only; not selectable)
/// </summary>
public record TileEntry(
    string Label,
    string Initials,
    string SectionId,
    bool IsOverlay = false,
    bool IsCommitment = false);

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

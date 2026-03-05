namespace SchedulingAssistant.ViewModels.GridView;

// ── Grid block hierarchy ──────────────────────────────────────────────────────
//
// The schedule grid can display two kinds of time blocks:
//   • SectionMeetingBlock  — a scheduled meeting for a section
//   • CommitmentBlock      — an instructor's non-teaching obligation (committee,
//                            office hours, etc.), entered via the Instructor flyout
//
// Both are subtypes of GridBlock. The layout engine (ComputeTiles) works entirely
// on the base type — it only cares about Day/StartMinutes/EndMinutes to figure out
// which blocks overlap and which column to assign them to. No subtype branching
// happens during layout math.
//
// The renderer (ScheduleGridView.axaml.cs) converts each GridBlock to a TileEntry
// via ScheduleGridViewModel.ToEntry(). TileEntry carries the display text, overlay
// flag, and the IsCommitment flag that suppresses click interactions.
//
// If you ever add a third kind of block (e.g., room reservations), derive it from
// GridBlock, add a case to ToEntry(), and add a case to the BlockId() helper in
// ReloadCore(). Everything else (layout, rendering, dedup) works automatically.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Abstract base for any time-positioned block that can be placed on the schedule grid.
/// Day uses 1=Monday … 6=Saturday. Times are minutes from midnight (e.g. 510 = 8:30 AM).
/// </summary>
public abstract record GridBlock(int Day, int StartMinutes, int EndMinutes, bool IsOverlay);

/// <summary>
/// A single scheduled meeting for a section (one slot in section.Schedule).
/// Label    = display text shown on the tile, e.g. "HIST101 A"
/// Initials = space-joined instructor initials, e.g. "JRS MKL" (may be empty)
/// SectionId = database ID of the section — used to highlight/select the tile
/// IsOverlay = true when the section matches the active overlay (renders red)
/// </summary>
public record SectionMeetingBlock(
    int Day, int StartMinutes, int EndMinutes, bool IsOverlay,
    string Label, string Initials, string SectionId
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay);

/// <summary>
/// An instructor commitment (non-teaching obligation stored in InstructorCommitments table).
/// Commitments are independent of sections — they share the same grid but have no
/// course code or section code. They are always rendered as overlay cards (red border,
/// red text) and only appear when the relevant instructor's overlay is active.
/// CommitmentId = database ID from the InstructorCommitments table
/// Name = the commitment title shown on the card (e.g. "Department Meeting")
/// IsOverlay is hardcoded true — commitments are always overlay-styled.
/// </summary>
public record CommitmentBlock(
    int Day, int StartMinutes, int EndMinutes,
    string Name, string CommitmentId
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay: true);

/// <summary>
/// One row within a rendered tile. A tile can have multiple entries when two or
/// more blocks share the exact same start and end time (co-scheduled); they are
/// stacked vertically with a thin separator rule between them.
///
/// Label      = text shown on the row (course+section code for sections; commitment name for commitments)
/// Initials   = instructor initials appended after the label (empty for commitments)
/// SectionId  = database section ID used for selection/highlighting (empty string for commitments)
/// IsOverlay  = true → red text and red tile border (applies to both overlay sections and commitments)
/// IsCommitment = true → this entry came from a CommitmentBlock. The renderer uses this
///               to suppress click interactions (no selection, no context menu, no hand cursor).
///               It does NOT affect visual styling — IsOverlay handles that.
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

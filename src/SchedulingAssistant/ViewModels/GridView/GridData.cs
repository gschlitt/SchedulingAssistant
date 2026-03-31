using Avalonia.Media;

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
/// SemesterId identifies which semester this block belongs to; used in multi-semester
/// mode to route each block to the correct semester sub-column. Empty string is safe
/// for single-semester mode.
/// SemesterName is the display name of the semester (e.g. "Fall 2025") used by the renderer
/// to look up the appropriate semester color. Empty string if not in multi-semester mode.
/// SemesterColor is the hex color string (e.g. "#C65D1E") assigned to the semester.
/// Empty string falls back to name-based color lookup in ScheduleGridViewModel.
/// </summary>
public abstract record GridBlock(int Day, int StartMinutes, int EndMinutes, bool IsOverlay, string SemesterId = "", string SemesterName = "", string SemesterColor = "");

/// <summary>
/// A single scheduled meeting for a section (one slot in section.Schedule).
/// Label               = display text shown on the tile, e.g. "HIST101 A"
/// Initials            = space-joined instructor initials, e.g. "JRS MKL" (may be empty)
/// SectionId           = database ID of the section — used to highlight/select the tile
/// IsOverlay           = true when the section matches the active overlay (renders red)
/// SemesterId          = database ID of the semester this section belongs to
/// SemesterName        = display name of the semester (e.g. "Fall 2025") used for color lookup
/// FrequencyAnnotation = parenthesised frequency annotation for this specific meeting slot,
///                       e.g. "(odd)", "(1,6,7)". Empty string for weekly meetings.
/// IsDeemphasized      = true when "Emphasize Unstaffed" is active and this section is staffed.
///                       The renderer applies a strikethrough to visually de-emphasise staffed tiles.
/// </summary>
public record SectionMeetingBlock(
    int Day, int StartMinutes, int EndMinutes, bool IsOverlay,
    string Label, string Initials, string SectionId,
    string SemesterId = "", string SemesterName = "", string SemesterColor = "",
    string FrequencyAnnotation = "",
    bool IsDeemphasized = false
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay, SemesterId, SemesterName, SemesterColor);

/// <summary>
/// An instructor commitment (non-teaching obligation stored in InstructorCommitments table).
/// Commitments are independent of sections — they share the same grid but have no
/// course code or section code. They are always rendered as overlay cards (red border,
/// red text) and only appear when the relevant instructor's overlay is active.
/// CommitmentId = database ID from the InstructorCommitments table
/// Name = the commitment title shown on the card (e.g. "Department Meeting")
/// IsOverlay is hardcoded true — commitments are always overlay-styled.
/// SemesterId = database ID of the semester this commitment belongs to
/// SemesterName = display name of the semester (e.g. "Fall 2025") used for color lookup
/// </summary>
public record CommitmentBlock(
    int Day, int StartMinutes, int EndMinutes,
    string Name, string CommitmentId, string SemesterId = "", string SemesterName = "", string SemesterColor = ""
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay: true, SemesterId, SemesterName, SemesterColor);

/// <summary>
/// One row within a rendered tile. A tile can have multiple entries when two or
/// more blocks share the exact same start and end time (co-scheduled); they are
/// stacked vertically with a thin separator rule between them.
///
/// Label               = text shown on the row (course+section code for sections; commitment name for commitments)
/// Initials            = instructor initials appended after the label (empty for commitments)
/// SectionId           = database section ID used for selection/highlighting (empty string for commitments)
/// IsOverlay           = true → red text and red tile border (applies to both overlay sections and commitments)
/// IsCommitment        = true → this entry came from a CommitmentBlock. The renderer uses this
///                       to suppress click interactions (no selection, no context menu, no hand cursor).
///                       It does NOT affect visual styling — IsOverlay handles that.
/// FrequencyAnnotation = parenthesised frequency annotation, e.g. "(odd)", "(1,6,7)".
///                       Empty string for weekly meetings and all commitments.
/// IsDeemphasized      = true when "Emphasize Unstaffed" is active and this section is staffed.
///                       The renderer applies a strikethrough to visually de-emphasise staffed tiles.
/// </summary>
public record TileEntry(
    string Label,
    string Initials,
    string SectionId,
    bool IsOverlay = false,
    bool IsCommitment = false,
    string FrequencyAnnotation = "",
    bool IsDeemphasized = false);

/// <summary>
/// A single tile drawn on the grid, potentially containing multiple co-scheduled sections
/// (same start time and duration).
/// SemesterName is the display name of the semester this tile belongs to (all entries
/// in a tile come from the same semester since they were tiled from semester-filtered blocks).
/// Empty string if single-semester mode.
/// SemesterColor is the hex color string assigned to the semester; empty string falls back
/// to name-based color lookup in the renderer.
/// </summary>
public record GridTile(
    IReadOnlyList<TileEntry> Entries,
    int StartMinutes,
    int EndMinutes,
    /// <summary>0-based column index within an overlap cluster.</summary>
    int OverlapIndex,
    /// <summary>Total number of columns in the overlap cluster.</summary>
    int OverlapCount,
    string SemesterName = "",
    string SemesterColor = "");

/// <summary>
/// One day-semester column's worth of positioned tiles.
/// In single-semester mode, Header is the day name and SemesterName is empty.
/// In multi-semester mode, SemesterName identifies which semester this sub-column belongs to
/// (e.g. "Fall 2025"). The renderer uses it to draw the colored semester indicator bar even
/// when the column has no tiles.
/// SemesterColor is the hex color string assigned to the semester; empty string falls back
/// to name-based color lookup in the renderer.
/// </summary>
public record GridDayColumn(
    string Header,
    IReadOnlyList<GridTile> Tiles,
    string SemesterName = "",
    string SemesterColor = "");

/// <summary>
/// Data for a tooltip shown when the user hovers over a tile on the schedule grid.
/// <para>
/// <see cref="Lines"/> holds one string per display line (e.g. time range, semester name).
/// Future fields can be appended here as the tooltip grows; the renderer iterates Lines so
/// new entries appear automatically without touching the view.
/// </para>
/// </summary>
public record TileTooltip(IReadOnlyList<string> Lines);

/// <summary>All data needed by the view to render the schedule grid.</summary>
public record GridData(
    /// <summary>Start of the visible time range, snapped to the half-hour at or before the earliest start.</summary>
    int FirstRowMinutes,
    /// <summary>End of the visible time range, snapped to the half-hour at or after the latest end.</summary>
    int LastRowMinutes,
    IReadOnlyList<GridDayColumn> DayColumns,
    /// <summary>
    /// Number of semester sub-columns per day. 1 in single-semester mode; N in multi-semester mode.
    /// Total column count = number of visible days × SemesterCount.
    /// Columns are ordered: all semester sub-columns for Monday, then all for Tuesday, etc.
    /// Within each day group, sub-columns appear in the same order as the selected semesters list.
    /// </summary>
    int SemesterCount = 1)
{
    /// <summary>
    /// A grid with no content. Uses the default 08:00–22:00 range; the real range is
    /// computed dynamically in <see cref="ScheduleGridViewModel"/> once data is loaded.
    /// </summary>
    public static GridData Empty =>
        new(8 * 60, 22 * 60, []);

    /// <summary>True when more than one semester's data is displayed side-by-side.</summary>
    public bool IsMultiSemester => SemesterCount > 1;

    public bool HasData => DayColumns.Any(d => d.Tiles.Count > 0);
}

using Avalonia.Media;
using TermPoint.Models;

namespace TermPoint.ViewModels.GridView;

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
// If you ever add a fourth kind of block, derive it from GridBlock, add a case to
// ToEntry(), and add a case to the EntityId() helper in DeduplicateBlocks().
// Everything else (layout, rendering, dedup) works automatically.
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
/// Initials            = the first instructor's initials plus " +N" for the rest, e.g. "JRS +1" (may be empty)
/// SectionId           = database ID of the section — used to highlight/select the tile
/// IsOverlay           = true when the section matches the active overlay (renders red)
/// SemesterId          = database ID of the semester this section belongs to
/// SemesterName        = display name of the semester (e.g. "Fall 2025") used for color lookup
/// FrequencyAnnotation = parenthesised frequency annotation for this specific meeting slot,
///                       e.g. "(odd)", "(1,6,7)". Empty string for weekly meetings.
/// IsDeemphasized      = true when "Emphasize Unstaffed" is active and this section is staffed.
///                       The renderer applies a strikethrough to visually de-emphasise staffed tiles.
/// IsEmphasized        = true when "Emphasize Unstaffed" is active and this section is unstaffed.
///                       The renderer applies a distinct background to make these tiles stand out.
/// Flag                = the section's advisory attention flag.
/// SubjectColor        = the section's subject marker colour (<c>#RRGGBB</c>) from
///                       <see cref="TermPoint.Services.SubjectColorPresenter.ColorFor"/>; empty string when colours are
///                       off or the subject has none (the line is then drawn exactly as without the feature).
/// InstructorNames     = every instructor's full name, comma-separated, for the line's hover tooltip. Set only when
///                       Initials was shortened to "+N" (spec 1.2.4 item 2.5); empty otherwise, meaning no tooltip.
/// </summary>
public record SectionMeetingBlock(
    int Day, int StartMinutes, int EndMinutes, bool IsOverlay,
    string Label, string Initials, string SectionId,
    string SemesterId = "", string SemesterName = "", string SemesterColor = "",
    string FrequencyAnnotation = "",
    bool IsDeemphasized = false,
    bool IsEmphasized = false,
    SectionFlag Flag = SectionFlag.None,
    string SubjectColor = "",
    string InstructorNames = ""
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay, SemesterId, SemesterName, SemesterColor);

/// <summary>
/// A faculty/committee meeting from the Meetings table. Unlike section blocks, meeting
/// blocks show a plain title rather than a course code. They render with a distinct
/// visual treatment and do not support click-selection in this version.
/// Title     = display text (e.g. "Faculty Meeting")
/// Attendees           = space-joined initials of assigned attendees (may be empty)
/// MeetingId           = database ID — kept for future edit-on-click support
/// FrequencyAnnotation = parenthesised frequency annotation, e.g. "(odd)", "(1,6,7)".
///                       Empty string when the event meets every week.
/// </summary>
public record MeetingBlock(
    int Day, int StartMinutes, int EndMinutes, bool IsOverlay,
    string Title, string Attendees, string MeetingId,
    string SemesterId = "", string SemesterName = "", string SemesterColor = "",
    string FrequencyAnnotation = "",
    /// <summary>Full comma-separated attendee names for the hover tooltip. Empty when no attendees.</summary>
    string AttendeeList = ""
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay, SemesterId, SemesterName, SemesterColor);

/// <summary>
/// A ghost tile representing a candidate room+time slot from the Room Availability Browser.
/// Rendered semi-transparent with a distinct visual style (dashed border, muted fill).
/// Ghost blocks suppress click interactions and do not participate in selection.
/// </summary>
/// <param name="RoomLabel">Room display label, e.g. "A 101".</param>
/// <param name="TimeLabel">Time range display, e.g. "8:00–9:15".</param>
/// <param name="Kind">
/// What this ghost means when its day is folded: the kind of attention mark the strip draws for it
/// (<see cref="FoldedDayMarks"/>). Defaults to <see cref="MarkKind.Proposal"/> (the Room Browser's candidate slot), so
/// the Room Browser needs no change. This is the hook for the Slot Browser: it passes its own kind (moving from,
/// moving to, a break...) and the strip code stays the same.
/// </param>
public record GhostBlock(
    int Day, int StartMinutes, int EndMinutes,
    string RoomLabel, string TimeLabel,
    string SemesterId = "", string SemesterName = "", string SemesterColor = "",
    MarkKind Kind = MarkKind.Proposal
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay: false, SemesterId, SemesterName, SemesterColor);

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
/// A meeting from an imported shared schedule CSV (cross-department visibility).
/// Rendered with purple outlined styling — distinct from both the red overlay and normal tiles.
/// Does not participate in selection or context menu interactions.
/// </summary>
/// <param name="Label">Pre-formatted display text, e.g. "CHEM101 A".</param>
/// <param name="FrequencyAnnotation">Parenthesised frequency, e.g. "(odd)". Empty for weekly.</param>
/// <param name="SourceLabel">Name of the importing source (e.g. "Chemistry Department").</param>
/// <param name="Notes">Optional freeform notes from the sender.</param>
public record SharedScheduleBlock(
    int Day, int StartMinutes, int EndMinutes,
    string Label, string FrequencyAnnotation = "",
    string SourceLabel = "", string Notes = "",
    string SemesterId = "", string SemesterName = "", string SemesterColor = ""
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay: false, SemesterId, SemesterName, SemesterColor);

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
/// IsEmphasized        = true when "Emphasize Unstaffed" is active and this section is unstaffed.
///                       The renderer applies a distinct background to make these tiles stand out.
/// IsMeeting           = true when this entry came from a <see cref="MeetingBlock"/>.
///                       The renderer uses this to apply a distinct visual treatment
///                       (e.g. a different tile background tint) without affecting overlay or
///                       click-suppression logic. Click interactions are suppressed via
///                       IsCommitment = true on meeting entries.
/// </summary>
public record TileEntry(
    string Label,
    string Initials,
    string SectionId,
    bool IsOverlay = false,
    bool IsCommitment = false,
    string FrequencyAnnotation = "",
    bool IsDeemphasized = false,
    bool IsMeeting = false,
    bool IsEmphasized = false,
    /// <summary>Full comma-separated attendee names shown in the tile hover tooltip. Empty for sections and commitments.</summary>
    string AttendeeList = "",
    /// <summary>True when this entry came from a <see cref="SharedScheduleBlock"/>. The renderer applies purple outlined styling.</summary>
    bool IsSharedSchedule = false,
    /// <summary>The section's advisory attention flag, drawn as a small colored icon after the label. None = no icon.</summary>
    SectionFlag Flag = SectionFlag.None,
    /// <summary>
    /// The subject marker colour (<c>#RRGGBB</c>): a small rounded square drawn before the section text, carried from
    /// <see cref="SectionMeetingBlock.SubjectColor"/>. Empty string = no marker (always empty for non-section entries).
    /// </summary>
    string SubjectColor = "",
    /// <summary>
    /// Full instructor names for this line's own hover tooltip, carried from
    /// <see cref="SectionMeetingBlock.InstructorNames"/>. Non-empty only when the line's initials were shortened
    /// to "+N" (spec 1.2.4 item 2.5); empty = no tooltip on the line.
    /// </summary>
    string InstructorNames = "");

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
/// What a folded day's strip needs to say about the day it hides (collapse days, <c>designs/collapse-days.md</c>
/// P2-1). Built by <see cref="ScheduleGridViewModel"/> from the day's blocks across ALL shown semesters (Q6), and
/// carried only by the FIRST sub-column of a folded day.
/// </summary>
/// <param name="SectionCount">
/// Distinct sections with at least one non-overlay section meeting on the day, each counted once (so two meetings
/// of one section on the day count 1, and an overlay turning on never changes the number).
/// </param>
/// <param name="HasOtherThings">
/// True when the day also holds anything else the grid draws: faculty/committee meetings, instructor commitments,
/// shared-schedule tiles or overlay section tiles. Keeps "T · 0" from suggesting the day is empty.
/// </param>
/// <param name="SectionMeetings">
/// Every non-overlay section meeting on the day (one entry per meeting, all semesters). The strip draws the union of
/// their time spans as remnant bands (Revision 1, R1-2).
/// </param>
/// <param name="OtherSpans">
/// The time span of every other thing the grid draws on the day, with its kind (R1-3): faculty/committee meetings,
/// instructor commitments, shared-schedule tiles and overlay section tiles. Drawn as bars in each kind's own colour.
/// </param>
public record FoldedDaySummary(
    int SectionCount,
    bool HasOtherThings,
    IReadOnlyList<FoldedSectionMeeting> SectionMeetings,
    IReadOnlyList<FoldedOtherSpan> OtherSpans);

/// <summary>One section meeting on a folded day: which section, and when it starts and ends (minutes from midnight).</summary>
/// <param name="SectionId">Database ID of the section.</param>
/// <param name="StartMinutes">Start time in minutes from midnight.</param>
/// <param name="EndMinutes">End time in minutes from midnight (exclusive).</param>
/// <param name="Label">The meeting's tile label, e.g. "MECH361 001" (used in the strip's tooltip when it is marked). Empty when unknown.</param>
public record FoldedSectionMeeting(string SectionId, int StartMinutes, int EndMinutes, string Label = "");

/// <summary>The kinds of "other thing" a folded strip shows as bars; each uses its own tile colour on the grid.</summary>
public enum FoldedOtherKind
{
    /// <summary>A faculty/committee meeting (the Meetings table).</summary>
    Meeting,
    /// <summary>An instructor commitment (always drawn in the overlay style).</summary>
    Commitment,
    /// <summary>A tile from an imported shared schedule.</summary>
    SharedSchedule,
    /// <summary>A section tile shown because an overlay (instructor/room/etc.) matches it.</summary>
    OverlaySection,
}

/// <summary>One other thing on a folded day: what kind, and when it starts and ends (minutes from midnight).</summary>
/// <param name="Kind">Which kind of thing it is (decides the bar colour).</param>
/// <param name="StartMinutes">Start time in minutes from midnight.</param>
/// <param name="EndMinutes">End time in minutes from midnight (exclusive).</param>
public record FoldedOtherSpan(FoldedOtherKind Kind, int StartMinutes, int EndMinutes);

/// <summary>
/// One day-semester column's worth of positioned tiles.
/// In single-semester mode, Header is the day name and SemesterName is empty.
/// In multi-semester mode, SemesterName identifies which semester this sub-column belongs to
/// (e.g. "Fall 2025"). The renderer uses it to draw the colored semester indicator bar even
/// when the column has no tiles.
/// SemesterColor is the hex color string assigned to the semester; empty string falls back
/// to name-based color lookup in the renderer.
/// DayNumber is the real day of the week this column shows (1 = Monday ... 7 = Sunday). Never work out a
/// column's day from its position: the shown days can have gaps (e.g. Sunday on, Saturday off).
/// IsFolded is true when the user has folded this column's day: the column then has NO tiles (so nothing is
/// measured for it) and the renderer draws a strip instead. Every sub-column of a folded day has it set.
/// Folded is the strip's summary; set only on the FIRST sub-column of a folded day, null everywhere else.
/// </summary>
public record GridDayColumn(
    string Header,
    int DayNumber,
    IReadOnlyList<GridTile> Tiles,
    string SemesterName = "",
    string SemesterColor = "",
    bool IsFolded = false,
    FoldedDaySummary? Folded = null);

/// <summary>
/// Data for a tooltip shown when the user hovers over a tile on the schedule grid.
/// <para>
/// <see cref="Lines"/> holds one string per display line (e.g. time range, semester name).
/// Future fields can be appended here as the tooltip grows; the renderer iterates Lines so
/// new entries appear automatically without touching the view.
/// </para>
/// </summary>
/// <summary>
/// AttendeeList, when non-null, is rendered as a separate wrapped TextBlock (MaxWidth 300)
/// below the main lines — used for meeting tiles with many attendees.
/// </summary>
public record TileTooltip(IReadOnlyList<string> Lines, string? AttendeeList = null);

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

    /// <summary>
    /// True when the grid holds anything to draw. A folded day has no tiles, so its summary counts too: folding
    /// must not make a busy week look empty.
    /// </summary>
    public bool HasData => DayColumns.Any(d => d.Tiles.Count > 0
                                            || d.Folded is { SectionCount: > 0 } or { HasOtherThings: true });

    /// <summary>
    /// The real day of the week (1 = Monday ... 7 = Sunday) shown by a flat column. Reads
    /// <see cref="GridDayColumn.DayNumber"/>, never the column's position, so it stays right when the
    /// shown days have gaps.
    /// </summary>
    /// <param name="flatCol">Flat column index, 0 .. DayColumns.Count - 1.</param>
    /// <returns>The day number of that column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside <see cref="DayColumns"/>.</exception>
    public int DayOfColumn(int flatCol) => DayColumns[flatCol].DayNumber;

    /// <summary>
    /// Finds the first flat column of a day (its first semester sub-column).
    /// </summary>
    /// <param name="day">Day number, 1 = Monday ... 7 = Sunday.</param>
    /// <returns>The first column index of that day, or null when the day isn't shown.</returns>
    public int? FirstColumnOfDay(int day)
    {
        for (int i = 0; i < DayColumns.Count; i++)
            if (DayColumns[i].DayNumber == day) return i;
        return null;
    }

    /// <summary>
    /// Program conflicts detected among the filtered sections by enabled watches.
    /// Set by the grid pipeline after construction; consumed by the renderer to draw
    /// colored overlays. Empty when no watches are active or no conflicts exist.
    /// </summary>
    public IReadOnlyList<Services.ProgramConflict> ProgramConflicts { get; init; } = [];
}

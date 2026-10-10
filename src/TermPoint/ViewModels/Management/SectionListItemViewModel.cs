using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;
using TermPoint.Services;
using System;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Display wrapper for a section row in the sections list panel.
/// Holds formatted strings so the view needs no converter logic.
/// </summary>
public partial class SectionListItemViewModel : ObservableObject, ISectionListEntry
{
    public Section Section { get; }
    public string Heading { get; }
    public IReadOnlyList<string> ScheduleLines { get; }

    // New: meeting details with meeting type for expanded display
    public IReadOnlyList<MeetingDisplayInfo> MeetingDetails { get; }

    // ── Meeting table column strings ─────────────────────────────────────────
    // The card's meeting table is one Grid with one TextBlock per column. Each TextBlock holds its
    // header Run followed directly by one of the strings below, so every meeting lands on its own
    // line under the header. All six are computed once in the constructor from MeetingDetails by
    // BuildColumnLines, which guarantees that all six have the same number of lines — that equal
    // line count (plus equal line height) is the only thing keeping the columns aligned row by row.

    /// <summary>
    /// The "Day" column body: one line per meeting (in <see cref="MeetingDetails"/> order), each
    /// preceded by a line break, e.g. <c>"\nMon\nWed"</c>. Empty (<c>""</c>) when the section has
    /// no meetings, so the view shows only the header line. See <see cref="BuildColumnLines"/> for
    /// the empty-value and embedded-newline rules shared by all six column strings.
    /// </summary>
    public string MeetingDayLines { get; }

    /// <summary>The "Start" column body; same shape and rules as <see cref="MeetingDayLines"/>.</summary>
    public string MeetingStartLines { get; }

    /// <summary>The "End" column body; same shape and rules as <see cref="MeetingDayLines"/>.</summary>
    public string MeetingEndLines { get; }

    /// <summary>
    /// The "Freq" column body; same shape and rules as <see cref="MeetingDayLines"/>. Weekly
    /// meetings have an empty frequency, so their lines are a single non-breaking space.
    /// </summary>
    public string MeetingFrequencyLines { get; }

    /// <summary>
    /// The "Room" column body; same shape and rules as <see cref="MeetingDayLines"/>. A meeting
    /// with no room has an empty value, so its line is a single non-breaking space.
    /// </summary>
    public string MeetingRoomLines { get; }

    /// <summary>
    /// The "Type" column body; same shape and rules as <see cref="MeetingDayLines"/>. A meeting
    /// with no meeting type has an empty value, so its line is a single non-breaking space.
    /// </summary>
    public string MeetingTypeLines { get; }

    // Right-side summary properties (displayed in order top-to-bottom)
    public string? InstructorLine { get; }
    public string? InstructorHeaderLine { get; }
    public string? SectionTypeName { get; }
    public string? TagLine { get; }
    public string? ReserveLine { get; }
    public string? ResourceLine { get; }
    public string? NoteLine { get; }

    /// <summary>Capacity number for the summary row, or null when unspecified (hidden in the UI).</summary>
    public string? CapacityLabel { get; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>
    /// True when the Schedule Grid has an active filter and this section's ID is in the
    /// passing set. Drives card background tint via <c>FilterHighlightBackgroundConverter</c>.
    /// Set externally by <see cref="SectionListViewModel.ApplyFilterHighlights"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isFilterHighlighted;

    /// <summary>
    /// True when this section is the currently selected section (from any view — Section List,
    /// Schedule Grid, or Workload panel). Drives the outer accent border (<c>UserSelectedSectionBorderColor</c>),
    /// which wraps the filter border when both are active.
    /// Set externally by <see cref="SectionListViewModel.ApplySelectionHighlight"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// True when at least one meeting in this section has a non-default (non-weekly) frequency.
    /// Drives visibility of the Freq column header in the expanded section card; the column itself
    /// collapses automatically via SharedSizeGroup when no content is visible.
    /// </summary>
    public bool HasNonDefaultFrequency =>
        MeetingDetails.Any(m => !string.IsNullOrEmpty(m.Frequency));

    /// <summary>
    /// Advisory warning text describing room-scheduling conflicts with other sections
    /// in the same semester. Null when no conflicts exist.
    /// Set externally by <see cref="SectionListViewModel.ApplyRoomConflicts"/>.
    /// </summary>
    /// <remarks>
    /// Feeds <see cref="PropertyLines"/>: because the warnings are applied after construction,
    /// changing this value re-raises <see cref="PropertyLines"/> and <see cref="HasPropertyLines"/>
    /// so the card rebuilds its property-line inlines.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PropertyLines))]
    [NotifyPropertyChangedFor(nameof(HasPropertyLines))]
    private string? _roomConflictWarning;

    /// <summary>
    /// Advisory warning text describing instructor-scheduling conflicts with other sections
    /// in the same semester. Null when no conflicts exist.
    /// Set externally by <see cref="SectionListViewModel.ApplyInstructorConflicts"/>.
    /// </summary>
    /// <remarks>
    /// Feeds <see cref="PropertyLines"/>; see <see cref="RoomConflictWarning"/> for why this
    /// notifies.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PropertyLines))]
    [NotifyPropertyChangedFor(nameof(HasPropertyLines))]
    private string? _instructorConflictWarning;

    /// <summary>
    /// The present "property lines" of the card, in display order: room conflict, instructor
    /// conflict, tags, reserves, resources. A source whose text is null, empty or whitespace
    /// contributes no entry, so a card with nothing to show returns an empty list.
    ///
    /// The view model states only <em>what</em> each line is (<see cref="CardPropertyKind"/>)
    /// and its text; icons, weights and colours are applied by the view
    /// (<c>PropertyLinesBehavior</c>, using values set in AXAML). Text is passed through untouched and is
    /// never parsed as markup, so user-entered names display exactly as typed.
    ///
    /// Computed on each read rather than cached: it is read only when a binding refreshes
    /// (card creation, or a conflict warning changing), and the warnings can change after
    /// construction, so a cache would need invalidating anyway.
    /// </summary>
    public IReadOnlyList<CardPropertyLine> PropertyLines
    {
        get
        {
            var lines = new List<CardPropertyLine>(5);
            AddPropertyLine(lines, CardPropertyKind.RoomConflict,       RoomConflictWarning);
            AddPropertyLine(lines, CardPropertyKind.InstructorConflict, InstructorConflictWarning);
            AddPropertyLine(lines, CardPropertyKind.Tags,               TagLine);
            AddPropertyLine(lines, CardPropertyKind.Reserves,           ReserveLine);
            AddPropertyLine(lines, CardPropertyKind.Resources,          ResourceLine);
            return lines;
        }
    }

    /// <summary>
    /// True when <see cref="PropertyLines"/> is non-empty. Drives the visibility of the card's
    /// property-line <c>TextBlock</c>, so a card with no property lines shows (and builds) none.
    /// </summary>
    public bool HasPropertyLines => PropertyLines.Count > 0;

    /// <summary>
    /// Appends a line of the given kind to <paramref name="lines"/> unless
    /// <paramref name="text"/> is null, empty or whitespace.
    /// </summary>
    /// <param name="lines">The list being built; mutated in place.</param>
    /// <param name="kind">The kind of line to add.</param>
    /// <param name="text">The line's text; a blank value means "nothing to show" and adds nothing.</param>
    private static void AddPropertyLine(List<CardPropertyLine> lines, CardPropertyKind kind, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            lines.Add(new CardPropertyLine(kind, text));
    }

    /// <summary>True when this is a temporary placeholder being added/copied (not yet saved).</summary>
    [ObservableProperty] private bool _isBeingCreated;

    // ── Attention flag ───────────────────────────────────────────────────────
    // Read-only on the card. The flag is chosen in the section editor's Flag dropdown (Apply
    // saves it, Cancel discards it); after a save the card is rebuilt from the saved section,
    // so this value always mirrors the section. The card shows a colored flag icon on its
    // top line (collapsed and expanded) only when a flag is set.

    /// <summary>This section's advisory attention flag (drives the top-line flag icon).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlag))]
    [NotifyPropertyChangedFor(nameof(FlagBrush))]
    [NotifyPropertyChangedFor(nameof(FlagTooltip))]
    private SectionFlag _flag;

    /// <summary>True when a flag is set (controls flag-icon visibility on the card).</summary>
    public bool HasFlag => Flag != SectionFlag.None;

    /// <summary>
    /// Tooltip for the flag icon, which is only shown while a flag is set. Names the flag
    /// (e.g. "Red flag (set in the section editor)") and says where it is changed.
    /// Empty when no flag is set.
    /// </summary>
    public string FlagTooltip => HasFlag
        ? $"{Flag} flag (set in the section editor)"
        : string.Empty;

    /// <summary>Brush for the top-line flag icon, or null when no flag is set.</summary>
    public IBrush? FlagBrush => FlagVisuals.ResolveBrush(Flag);

    public string SortKeyInstructor { get; }
    public string SortKeySectionType { get; }

    /// <summary>Semester name (e.g. "Fall 2025") for multi-semester border color resolution.</summary>
    public string SemesterName { get; }

    /// <summary>Hex color override for the semester border (e.g. "#C65D1E"), or empty.</summary>
    public string SemesterColor { get; }

    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public SectionListItemViewModel(
        Section section,
        Dictionary<string, Course> courseLookup,
        Dictionary<string, Instructor> instructorLookup,
        Dictionary<string, Room> roomLookup,
        Dictionary<string, SchedulingEnvironmentValue> sectionTypeLookup,
        Dictionary<string, Campus> campusLookup,
        Dictionary<string, SchedulingEnvironmentValue> tagLookup,
        Dictionary<string, SchedulingEnvironmentValue> resourceLookup,
        Dictionary<string, SchedulingEnvironmentValue> reserveLookup,
        Dictionary<string, SchedulingEnvironmentValue> meetingTypeLookup,
        string semesterName = "",
        string semesterColor = "")
    {
        Section = section;

        SemesterName = semesterName;
        SemesterColor = semesterColor;

        _flag = section.Flag;

        // Compute sort keys for instructor and section type
        var instructorNames = section.InstructorAssignments
            .Where(a => instructorLookup.TryGetValue(a.InstructorId, out _))
            .Select(a => instructorLookup[a.InstructorId])
            .OrderBy(i => i.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(i => $"{i.FirstName} {i.LastName}")
            .ToList();
        SortKeyInstructor = instructorNames.Count > 0
            ? string.Join(" ", instructorNames).ToLowerInvariant()
            : "\uffff";

        SortKeySectionType = section.SectionTypeId is not null && sectionTypeLookup.TryGetValue(section.SectionTypeId, out var st)
            ? st.Name.ToLowerInvariant()
            : "\uffff";

        var calendarCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
            ? course.CalendarCode
            : null;

        Heading = calendarCode is not null
            ? $"{calendarCode} {section.SectionCode}".Trim()
            : section.SectionCode;

        ScheduleLines = section.Schedule
            .OrderBy(s => s.Day).ThenBy(s => s.StartMinutes)
            .Select(s =>
            {
                var day   = s.Day >= 1 && s.Day <= 6 ? DayNames[s.Day] : $"Day {s.Day}";
                var start = FormatMinutes(s.StartMinutes);
                var end   = FormatMinutes(s.EndMinutes);
                var freq  = SectionDaySchedule.FormatFrequency(s.Frequency);
                var freqPart = freq.Length > 0 ? $" {freq}" : string.Empty;
                var room  = s.RoomId is not null && roomLookup.TryGetValue(s.RoomId, out var r)
                    ? $"  {r.Building} {r.RoomNumber}".TrimEnd()
                    : string.Empty;
                return $"{day}  {start}–{end}{freqPart}{room}";
            })
            .ToList();

        // Build meeting details with meeting type and frequency
        MeetingDetails = section.Schedule
            .OrderBy(s => s.Day).ThenBy(s => s.StartMinutes)
            .Select(s =>
            {
                var day = s.Day >= 1 && s.Day <= 6 ? DayNames[s.Day] : $"Day {s.Day}";
                var start = FormatMinutes(s.StartMinutes);
                var end = FormatMinutes(s.EndMinutes);
                var freq = SectionDaySchedule.FormatFrequency(s.Frequency);
                var room = s.RoomId is not null && roomLookup.TryGetValue(s.RoomId, out var r)
                    ? $"{r.Building} {r.RoomNumber}"
                    : string.Empty;
                var meetingType = s.MeetingTypeId is not null && meetingTypeLookup.TryGetValue(s.MeetingTypeId, out var mt)
                    ? mt.Name
                    : string.Empty;
                return new MeetingDisplayInfo
                {
                    Day         = day,
                    StartTime   = start,
                    EndTime     = end,
                    Frequency   = freq,
                    Room        = room,
                    MeetingType = meetingType
                };
            })
            .ToList();

        // Build the six column strings of the meeting table from the finished MeetingDetails list.
        MeetingDayLines       = BuildColumnLines(MeetingDetails.Select(m => m.Day));
        MeetingStartLines     = BuildColumnLines(MeetingDetails.Select(m => m.StartTime));
        MeetingEndLines       = BuildColumnLines(MeetingDetails.Select(m => m.EndTime));
        MeetingFrequencyLines = BuildColumnLines(MeetingDetails.Select(m => m.Frequency));
        MeetingRoomLines      = BuildColumnLines(MeetingDetails.Select(m => m.Room));
        MeetingTypeLines      = BuildColumnLines(MeetingDetails.Select(m => m.MeetingType));

        // Build individual summary properties for the right-side stack
        var instructorParts = section.InstructorAssignments
            .Select(a =>
            {
                if (!instructorLookup.TryGetValue(a.InstructorId, out var instr)) return null;
                var name = $"{instr.FirstName} {instr.LastName}";
                return a.Workload.HasValue ? $"{name} [{a.Workload.Value:0.##}]" : name;
            })
            .Where(n => n is not null)
            .ToList();
        InstructorLine = instructorParts.Count > 0 ? string.Join("; ", instructorParts) : null;

        // Header line format: "Name (workload)" without brackets, stacked vertically
        var instructorHeaderParts = section.InstructorAssignments
            .OrderBy(a => instructorLookup.TryGetValue(a.InstructorId, out var i) ? $"{i.FirstName} {i.LastName}" : "")
            .Select(a =>
            {
                if (!instructorLookup.TryGetValue(a.InstructorId, out var instr)) return null;
                var name = $"{instr.FirstName} {instr.LastName}";
                return a.Workload.HasValue ? $"{name} ({a.Workload.Value:0.##})" : name;
            })
            .Where(n => n is not null)
            .ToList();
        InstructorHeaderLine = instructorHeaderParts.Count > 0 ? string.Join(", ", instructorHeaderParts) : null;

        // Section type name
        SectionTypeName = section.SectionTypeId is not null && sectionTypeLookup.TryGetValue(section.SectionTypeId, out var sectionType)
            ? sectionType.Name
            : null;

        var tagNames = section.TagIds
            .Select(id => tagLookup.TryGetValue(id, out var t) ? t.Name : null)
            .Where(n => n is not null)
            .ToList();
        TagLine = tagNames.Count > 0 ? string.Join(", ", tagNames) : null;

        var reserveParts = section.Reserves
            .Select(r => reserveLookup.TryGetValue(r.ReserveId, out var rv)
                ? $"{rv.Name}:{r.Code}" : null)
            .Where(n => n is not null)
            .ToList();
        ReserveLine = reserveParts.Count > 0 ? string.Join(", ", reserveParts) : null;

        var resourceNames = section.ResourceIds
            .Select(id => resourceLookup.TryGetValue(id, out var r) ? r.Name : null)
            .Where(n => n is not null)
            .ToList();
        ResourceLine = resourceNames.Count > 0 ? string.Join(", ", resourceNames) : null;

        NoteLine = !string.IsNullOrWhiteSpace(section.Notes) ? section.Notes : null;

        CapacityLabel = section.Capacity?.ToString();
    }

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";

    /// <summary>
    /// A non-breaking space (U+00A0). Stands in for an empty value in a meeting-table column so
    /// the line still holds a glyph and keeps the same height as its neighbours. Written as a
    /// numeric cast, not a literal, because the character is invisible in source.
    /// </summary>
    private const char NonBreakingSpace = (char)0x00A0;

    /// <summary>
    /// Builds the body of one column of the meeting table: for each value, in order, a line break
    /// (<c>"\n"</c>) followed by the value. The view places the result directly after the column's
    /// header Run in a single <c>TextBlock</c>, so the header is line 1 and each meeting is the
    /// next line down. With no values the result is <c>""</c>, so a card with no meetings shows
    /// only the header line (no blank line).
    ///
    /// The six columns must stay aligned row by row, which holds only if every column has exactly
    /// one line per meeting and every line is the same height. Two rules guarantee that:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Empty values.</b> A null, empty or whitespace-only value becomes a single
    ///     non-breaking space (U+00A0), never an empty segment. A line with no glyph can
    ///     collapse to a different height than a line with text; the non-breaking space gives it a
    ///     glyph in the same font, so it is as tall as its neighbours.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Embedded newlines.</b> Any <c>\r\n</c>, <c>\r</c> or <c>\n</c> inside a value
    ///     (e.g. a room name typed with a line break) is replaced by a space, so a value can never
    ///     add a line to its own column and push the other columns out of step.
    ///   </description></item>
    /// </list>
    /// </summary>
    /// <param name="values">
    /// The column's value for each meeting, in meeting order. Null entries are treated as empty.
    /// </param>
    /// <returns>
    /// The concatenated <c>"\n" + value</c> segments, or <c>""</c> when <paramref name="values"/>
    /// is empty.
    /// </returns>
    private static string BuildColumnLines(IEnumerable<string?> values)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var value in values)
        {
            sb.Append('\n');

            if (string.IsNullOrWhiteSpace(value))
            {
                // Keep the line the same height as its neighbours (see the remarks above).
                sb.Append(NonBreakingSpace);
                continue;
            }

            // Flatten any line breaks inside the value; "\r\n" first so it becomes one space.
            sb.Append(value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '));
        }
        return sb.ToString();
    }
}

/// <summary>Display info for a single meeting within a section.</summary>
public class MeetingDisplayInfo
{
    public string Day { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    /// <summary>
    /// Formatted frequency annotation, e.g. "(odd)", "(1,6,7)". Empty string when weekly.
    /// </summary>
    public string Frequency { get; set; } = "";
    public string Room { get; set; } = "";
    public string MeetingType { get; set; } = "";
}

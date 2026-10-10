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
    /// (<c>PropertyLinesBehavior</c> plus AXAML styles). Text is passed through untouched and is
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

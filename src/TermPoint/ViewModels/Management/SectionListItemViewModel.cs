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
    [ObservableProperty]
    private string? _roomConflictWarning;

    /// <summary>
    /// Advisory warning text describing instructor-scheduling conflicts with other sections
    /// in the same semester. Null when no conflicts exist.
    /// Set externally by <see cref="SectionListViewModel.ApplyInstructorConflicts"/>.
    /// </summary>
    [ObservableProperty]
    private string? _instructorConflictWarning;

    /// <summary>True when this is a temporary placeholder being added/copied (not yet saved).</summary>
    [ObservableProperty] private bool _isBeingCreated;

    // ── Attention flag ───────────────────────────────────────────────────────
    // Set via right-click on the card. The card shows a colored flag icon on its
    // top line (collapsed and expanded); the value is persisted to the section's
    // JSON through the parent VM's save callback.

    /// <summary>This section's advisory attention flag (drives the top-line flag icon).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlag))]
    [NotifyPropertyChangedFor(nameof(FlagBrush))]
    [NotifyPropertyChangedFor(nameof(FlagDisplayBrush))]
    [NotifyPropertyChangedFor(nameof(FlagOpacity))]
    [NotifyPropertyChangedFor(nameof(FlagTooltip))]
    private SectionFlag _flag;

    /// <summary>True when a flag is set (controls flag-icon visibility on the card).</summary>
    public bool HasFlag => Flag != SectionFlag.None;

    /// <summary>Brush used by the flag icon — colored when set, muted grey when unset.</summary>
    public IBrush FlagDisplayBrush => FlagVisuals.ResolveBrush(Flag)
        ?? (Avalonia.Application.Current?.Resources.TryGetResource("FlagMuted", null, out var v) == true && v is IBrush b
            ? b : Brushes.LightGray);

    /// <summary>Opacity for the flag icon — full when set, faint hint when unset.</summary>
    public double FlagOpacity => HasFlag ? 1.0 : 0.5;

    /// <summary>Tooltip for the flag icon — instructional when unset, descriptive when set.</summary>
    public string FlagTooltip => HasFlag
        ? "Section flag (right-click the card to change)"
        : "Right-click the card to set an attention flag";

    /// <summary>Brush for the top-line flag icon, or null when no flag is set.</summary>
    public IBrush? FlagBrush => FlagVisuals.ResolveBrush(Flag);

    /// <summary>True while the right-click flag picker popup is open over this card.</summary>
    [ObservableProperty] private bool _isFlagMenuOpen;

    /// <summary>Invoked after the flag changes so the parent can persist it and refresh the grid.</summary>
    private readonly Action<Section>? _onFlagChanged;

    /// <summary>Opens the right-click flag picker popup (wired to RightClickCommandBehavior).</summary>
    [RelayCommand]
    private void ShowFlagMenu() => IsFlagMenuOpen = true;

    /// <summary>
    /// Applies <paramref name="flag"/> to this section, updates the card immediately, persists
    /// it through the parent callback, and closes the picker. Passing None clears the flag.
    /// </summary>
    [RelayCommand]
    private void SetFlag(SectionFlag flag)
    {
        IsFlagMenuOpen = false;
        if (Flag == flag) return;
        Flag = flag;
        Section.Flag = flag;
        _onFlagChanged?.Invoke(Section);
    }

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
        string semesterColor = "",
        Action<Section>? onFlagChanged = null)
    {
        Section = section;

        SemesterName = semesterName;
        SemesterColor = semesterColor;

        _flag = section.Flag;
        _onFlagChanged = onFlagChanged;

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

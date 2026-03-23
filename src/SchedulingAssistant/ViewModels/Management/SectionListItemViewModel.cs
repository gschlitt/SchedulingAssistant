using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

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

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>
    /// True when the Schedule Grid has an active filter and this section's ID is in the
    /// passing set. Drives a 3 pt <c>FilterColor</c> border around the card in the section list.
    /// Set externally by <see cref="SectionListViewModel.ApplyFilterHighlights"/>.
    /// </summary>
    [ObservableProperty] private bool _isFilterHighlighted;

    /// <summary>
    /// True when at least one meeting in this section has a non-default (non-weekly) frequency.
    /// Drives visibility of the Freq column header in the expanded section card; the column itself
    /// collapses automatically via SharedSizeGroup when no content is visible.
    /// </summary>
    public bool HasNonDefaultFrequency =>
        MeetingDetails.Any(m => !string.IsNullOrEmpty(m.Frequency));

    /// <summary>True when this is a temporary placeholder being added/copied (not yet saved).</summary>
    [ObservableProperty] private bool _isBeingCreated;

    public string SortKeyInstructor { get; }
    public string SortKeySectionType { get; }

    /// <summary>
    /// Left-border brush for this section's semester, resolved from AppColors.
    /// Used in multi-semester view to visually indicate semester membership.
    /// </summary>
    public IBrush? SemesterLeftBorderBrush { get; }

    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public SectionListItemViewModel(
        Section section,
        Dictionary<string, Course> courseLookup,
        Dictionary<string, Instructor> instructorLookup,
        Dictionary<string, Room> roomLookup,
        Dictionary<string, SectionPropertyValue> sectionTypeLookup,
        Dictionary<string, Campus> campusLookup,
        Dictionary<string, SectionPropertyValue> tagLookup,
        Dictionary<string, SectionPropertyValue> resourceLookup,
        Dictionary<string, SectionPropertyValue> reserveLookup,
        Dictionary<string, SectionPropertyValue> meetingTypeLookup,
        string semesterName = "")
    {
        Section = section;

        // Resolve semester color from AppColors by semester name
        string borderKey = GetSemesterBorderKey(semesterName);
        object? bd = null;
        Application.Current?.Resources.TryGetResource(borderKey, null, out bd);
        SemesterLeftBorderBrush = bd as IBrush;

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
    }

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>
    /// Maps a semester name to its corresponding AppColors border brush key.
    /// </summary>
    private static string GetSemesterBorderKey(string semesterName)
    {
        if (string.IsNullOrEmpty(semesterName))
            return "FallBorder";

        var firstWord = semesterName.Split(' ')[0];
        return firstWord switch
        {
            "Fall" => "FallBorder",
            "Winter" => "WinterBorder",
            "Early" => "EarlySummerBorder",
            "Summer" => "SummerBorder",
            "Late" => "LateSummerBorder",
            _ => "FallBorder"
        };
    }

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

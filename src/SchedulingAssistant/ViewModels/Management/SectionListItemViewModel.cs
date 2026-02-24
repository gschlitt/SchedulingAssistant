using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Display wrapper for a section row in the sections list panel.
/// Holds formatted strings so the view needs no converter logic.
/// </summary>
public partial class SectionListItemViewModel : ObservableObject
{
    public Section Section { get; }
    public string Heading { get; }
    public IReadOnlyList<string> ScheduleLines { get; }

    // Right-side summary properties (displayed in order top-to-bottom)
    public string? InstructorLine { get; }
    public string? TagLine { get; }
    public string? ReserveLine { get; }
    public string? ResourceLine { get; }
    public string? NoteLine { get; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isCollapsed;

    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public SectionListItemViewModel(
        Section section,
        Dictionary<string, Course> courseLookup,
        Dictionary<string, Instructor> instructorLookup,
        Dictionary<string, Room> roomLookup,
        Dictionary<string, SectionPropertyValue> sectionTypeLookup,
        Dictionary<string, SectionPropertyValue> campusLookup,
        Dictionary<string, SectionPropertyValue> tagLookup,
        Dictionary<string, SectionPropertyValue> resourceLookup,
        Dictionary<string, SectionPropertyValue> reserveLookup)
    {
        Section = section;

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
                var room  = s.RoomId is not null && roomLookup.TryGetValue(s.RoomId, out var r)
                    ? $"  {r.Building} {r.RoomNumber}".TrimEnd()
                    : string.Empty;
                return $"{day}  {start}–{end}{room}";
            })
            .ToList();

        // Build individual summary properties for the right-side stack
        var instructorParts = section.InstructorAssignments
            .Select(a =>
            {
                if (!instructorLookup.TryGetValue(a.InstructorId, out var instr)) return null;
                var name = $"{instr.LastName}, {instr.FirstName}";
                return a.Workload.HasValue ? $"{name} [{a.Workload.Value:0.#}]" : name;
            })
            .Where(n => n is not null)
            .ToList();
        InstructorLine = instructorParts.Count > 0 ? string.Join("; ", instructorParts) : null;

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

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}

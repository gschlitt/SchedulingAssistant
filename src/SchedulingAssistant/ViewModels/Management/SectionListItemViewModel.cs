using CommunityToolkit.Mvvm.ComponentModel;
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
    public IReadOnlyList<string> PropertyLines { get; }

    [ObservableProperty] private bool _isExpanded;

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

        // Build property summary lines for collapsed card
        var lines = new List<string>();

        // Instructors (with workload in brackets if present)
        var instructorParts = section.InstructorAssignments
            .Select(a =>
            {
                if (!instructorLookup.TryGetValue(a.InstructorId, out var instr)) return null;
                var name = $"{instr.LastName}, {instr.FirstName}";
                return a.Workload.HasValue ? $"{name} [{a.Workload.Value:0.#}]" : name;
            })
            .Where(n => n is not null)
            .ToList();
        if (instructorParts.Count > 0)
            lines.Add($"Instructor: {string.Join("; ", instructorParts)}");

        if (section.SectionTypeId is not null &&
            sectionTypeLookup.TryGetValue(section.SectionTypeId, out var sType))
            lines.Add($"Type: {sType.Name}");

        if (section.CampusId is not null &&
            campusLookup.TryGetValue(section.CampusId, out var campus))
            lines.Add($"Campus: {campus.Name}");

        var tagNames = section.TagIds
            .Select(id => tagLookup.TryGetValue(id, out var t) ? t.Name : null)
            .Where(n => n is not null)
            .ToList();
        if (tagNames.Count > 0)
            lines.Add($"Tags: {string.Join(", ", tagNames)}");

        var resourceNames = section.ResourceIds
            .Select(id => resourceLookup.TryGetValue(id, out var r) ? r.Name : null)
            .Where(n => n is not null)
            .ToList();
        if (resourceNames.Count > 0)
            lines.Add($"Resources: {string.Join(", ", resourceNames)}");

        var reserveParts = section.Reserves
            .Select(r => reserveLookup.TryGetValue(r.ReserveId, out var rv)
                ? $"{rv.Name}:{r.Code}" : null)
            .Where(n => n is not null)
            .ToList();
        if (reserveParts.Count > 0)
            lines.Add($"Reserves: [{string.Join(", ", reserveParts)}]");

        PropertyLines = lines;
    }

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}

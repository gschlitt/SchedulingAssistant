using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Display wrapper for a section row in the sections list panel.
/// Holds formatted strings so the view needs no converter logic.
/// </summary>
public class SectionListItemViewModel
{
    public Section Section { get; }
    public string Heading { get; }
    public IReadOnlyList<string> ScheduleLines { get; }

    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri"];

    public SectionListItemViewModel(Section section, Dictionary<string, Course> courseLookup)
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
                var day = s.Day >= 1 && s.Day <= 5 ? DayNames[s.Day] : $"Day {s.Day}";
                var start = FormatMinutes(s.StartMinutes);
                var end   = FormatMinutes(s.EndMinutes);
                return $"{day}  {start}–{end}";
            })
            .ToList();
    }

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}

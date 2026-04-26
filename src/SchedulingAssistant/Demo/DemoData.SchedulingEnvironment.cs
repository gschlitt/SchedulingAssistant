using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<SchedulingEnvironmentValue> SectionTypes =
    [
        new() { Id = "demo-st-lecture",  Name = "Lecture",    SortOrder = 1 },
        new() { Id = "demo-st-lab",      Name = "Lab",       SortOrder = 2 },
        new() { Id = "demo-st-seminar",  Name = "Seminar",   SortOrder = 3 }
    ];

    public static readonly List<SchedulingEnvironmentValue> MeetingTypes =
    [
        new() { Id = "demo-mt-regular", Name = "Regular",  SortOrder = 1 },
        new() { Id = "demo-mt-online",  Name = "Online",   SortOrder = 2 }
    ];

    public static readonly List<SchedulingEnvironmentValue> StaffTypes =
    [
        new() { Id = "demo-staff-ft", Name = "Full-Time", SortOrder = 1 },
        new() { Id = "demo-staff-pt", Name = "Part-Time", SortOrder = 2 }
    ];

    public static readonly List<SchedulingEnvironmentValue> Tags = [];
    public static readonly List<SchedulingEnvironmentValue> Resources = [];
    public static readonly List<SchedulingEnvironmentValue> Reserves = [];
}

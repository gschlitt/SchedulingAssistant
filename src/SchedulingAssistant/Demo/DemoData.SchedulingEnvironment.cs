using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<SchedulingEnvironmentValue> SectionTypes =
    [
        new() { Id = "demo-st-1", Name = "F2F (TRD)", SortOrder = 0 },
        new() { Id = "demo-st-2", Name = "Blend (HYB)", SortOrder = 1 },
        new() { Id = "demo-st-3", Name = "Online Asynch (OLO)", SortOrder = 2 },
        new() { Id = "demo-st-4", Name = "Online Synch (OLM)", SortOrder = 3 }
    ];

    public static readonly List<SchedulingEnvironmentValue> MeetingTypes =
    [
        new() { Id = "demo-mt-1", Name = "F2F Lesson", SortOrder = 0 },
        new() { Id = "demo-mt-2", Name = "Remote Lesson", SortOrder = 1 },
        new() { Id = "demo-mt-3", Name = "Lab", SortOrder = 2 }
    ];

    public static readonly List<SchedulingEnvironmentValue> StaffTypes =
    [
        new() { Id = "demo-staff-1", Name = "Type B", SortOrder = 0 },
        new() { Id = "demo-staff-2", Name = "LTA", SortOrder = 1 },
        new() { Id = "demo-staff-3", Name = "Sess.", SortOrder = 2 },
        new() { Id = "demo-staff-4", Name = "Faculty (Permanent)", SortOrder = 3 },
        new() { Id = "demo-staff-5", Name = "Staff (Permanent)", SortOrder = 4 },
        new() { Id = "demo-staff-6", Name = "Lab Instructor", SortOrder = 5 },
        new() { Id = "demo-staff-7", Name = "Lab Assistant", SortOrder = 6 }
    ];

    public static readonly List<SchedulingEnvironmentValue> Tags =
    [
        new() { Id = "demo-tag-1", Name = "e.g. Cohort 1", SortOrder = 0 },
        new() { Id = "demo-tag-2", Name = "e.g. EnvStudCert", SortOrder = 1 }
    ];
    public static readonly List<SchedulingEnvironmentValue> Resources =
    [
        new() { Id = "demo-res-1", Name = "e.g. Laptop cart", SortOrder = 0 }
    ];
    public static readonly List<SchedulingEnvironmentValue> Reserves = [];
}

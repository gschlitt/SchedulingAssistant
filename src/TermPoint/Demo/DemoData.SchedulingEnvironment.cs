using TermPoint.Models;

namespace TermPoint.Demo;

public static partial class DemoData
{
    public static readonly List<SchedulingEnvironmentValue> SectionTypes =
    [
        new() { Id = "demo-st-1", Name = "F2F", SortOrder = 0 },
        new() { Id = "demo-st-2", Name = "Remote", SortOrder = 1 }
    ];

    public static readonly List<SchedulingEnvironmentValue> MeetingTypes =
    [
        new() { Id = "demo-mt-1", Name = "F2F Lecture", SortOrder = 0 },
        new() { Id = "demo-mt-2", Name = "Remote SyncLecture", SortOrder = 1 },
        new() { Id = "demo-mt-3", Name = "F2F Lab", SortOrder = 2 }
    ];

    public static readonly List<SchedulingEnvironmentValue> StaffTypes =
    [
        new() { Id = "demo-staff-1", Name = "Sess.", SortOrder = 2 },
        new() { Id = "demo-staff-2", Name = "Faculty (Permanent)", SortOrder = 3 },
        new() { Id = "demo-staff-3", Name = "Staff (Permanent)", SortOrder = 4 },
        new() { Id = "demo-staff-4", Name = "Lab Instructor", SortOrder = 5 },
        new() { Id = "demo-staff-5", Name = "Lab Assistant", SortOrder = 6 }
    ];

    public static readonly List<SchedulingEnvironmentValue> Tags =
    [
        new() { Id = "demo-tag-1", Name = "Cohort 1", SortOrder = 0 },
        new() { Id = "demo-tag-2", Name = "Cohort 2", SortOrder = 1 },
        new() { Id = "demo-tag-3", Name = "EnvStudCert", SortOrder = 2 },
        new() { Id = "demo-tag-4", Name = "BSc", SortOrder = 3 }
    ];
    public static readonly List<SchedulingEnvironmentValue> Resources =
    [
        new() { Id = "demo-res-1", Name = "Cart", SortOrder = 0 }
    ];
    public static readonly List<SchedulingEnvironmentValue> Reserves =
    [
        new() { Id = "demo-reserve-1", Name = "BSc", SortOrder = 0 },
        new() { Id = "demo-reserve-2", Name = "BA", SortOrder = 1 }
    ];
    public static readonly List<SchedulingEnvironmentValue> RoomTypes =
    [
        new() { Id = "demo-rt-1", Name = "Small Lecture", SortOrder = 0 },
        new() { Id = "demo-rt-2", Name = "Theatre", SortOrder = 1 },
        new() { Id = "demo-rt-3", Name = "Lab24", SortOrder = 2 },
        new() { Id = "demo-rt-4", Name = "Lab36", SortOrder = 3 }
    ];
}

using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Section> Sections =
    [
        new()
        {
            Id            = "demo-sec-comp101-a",
            SemesterId    = "demo-sem-fall",
            CourseId      = "demo-course-comp101",
            SectionCode   = "A",
            SectionTypeId = "demo-st-lecture",
            CampusId      = "demo-campus-main",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 540, DurationMinutes = 60, RoomId = "demo-room-1" },
                new() { Day = 3, StartMinutes = 540, DurationMinutes = 60, RoomId = "demo-room-1" },
                new() { Day = 5, StartMinutes = 540, DurationMinutes = 60, RoomId = "demo-room-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-1" } ]
        },
        new()
        {
            Id            = "demo-sec-comp201-a",
            SemesterId    = "demo-sem-fall",
            CourseId      = "demo-course-comp201",
            SectionCode   = "A",
            SectionTypeId = "demo-st-lecture",
            CampusId      = "demo-campus-main",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 570, DurationMinutes = 90, RoomId = "demo-room-2" },
                new() { Day = 4, StartMinutes = 570, DurationMinutes = 90, RoomId = "demo-room-2" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-1" } ]
        },
        new()
        {
            Id            = "demo-sec-math101-a",
            SemesterId    = "demo-sem-fall",
            CourseId      = "demo-course-math101",
            SectionCode   = "A",
            SectionTypeId = "demo-st-lecture",
            CampusId      = "demo-campus-main",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 660, DurationMinutes = 60, RoomId = "demo-room-1" },
                new() { Day = 3, StartMinutes = 660, DurationMinutes = 60, RoomId = "demo-room-1" },
                new() { Day = 5, StartMinutes = 660, DurationMinutes = 60, RoomId = "demo-room-1" }
            ],
            InstructorAssignments = [ new() { InstructorId = "demo-inst-2" } ]
        }
    ];
}

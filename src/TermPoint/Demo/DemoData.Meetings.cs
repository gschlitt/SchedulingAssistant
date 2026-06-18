using TermPoint.Models;

namespace TermPoint.Demo;

public static partial class DemoData
{
    public static readonly List<Meeting> Meetings =
    [
        new()
        {
            Id          = "demo-meeting-1",
            SemesterId  = "demo-sem-1",
            Title       = "DeptMtng",
            Schedule =
            [
                new() { Day = 3, StartMinutes = 870, DurationMinutes = 180, Frequency = "4" }
            ],
            InstructorAssignments =
            [
                new() { InstructorId = "demo-inst-1" },
                new() { InstructorId = "demo-inst-5" },
                new() { InstructorId = "demo-inst-9" }
            ]
        }
    ];
}

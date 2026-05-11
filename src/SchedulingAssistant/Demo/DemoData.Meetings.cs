using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Meeting> Meetings =
    [
        new()
        {
            Id          = "demo-meeting-1",
            SemesterId  = "demo-sem-1",
            Title       = "DEPMTNG",
            Schedule =
            [
                new() { Day = 1, StartMinutes = 960, DurationMinutes = 180, Frequency = "4" }
            ],
            InstructorAssignments =
            [
                new() { InstructorId = "demo-inst-3" },
                new() { InstructorId = "demo-inst-5" },
                new() { InstructorId = "demo-inst-6" },
                new() { InstructorId = "demo-inst-7" },
                new() { InstructorId = "demo-inst-8" },
                new() { InstructorId = "demo-inst-10" },
                new() { InstructorId = "demo-inst-15" },
                new() { InstructorId = "demo-inst-17" },
                new() { InstructorId = "demo-inst-20" },
                new() { InstructorId = "demo-inst-23" },
                new() { InstructorId = "demo-inst-24" },
                new() { InstructorId = "demo-inst-31" },
                new() { InstructorId = "demo-inst-32" },
                new() { InstructorId = "demo-inst-38" },
                new() { InstructorId = "demo-inst-39" }
            ]
        }
    ];
}

using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<InstructorCommitment> InstructorCommitments =
    [
        new()
        {
            Id           = "demo-commit-1",
            InstructorId = "demo-inst-39",
            SemesterId   = "demo-sem-1",
            Name         = "HdMtng",
            Day          = 5,
            StartMinutes = 600,
            EndMinutes   = 720
        },
        new()
        {
            Id           = "demo-commit-2",
            InstructorId = "demo-inst-39",
            SemesterId   = "demo-sem-2",
            Name         = "HdsMtng",
            Day          = 5,
            StartMinutes = 660,
            EndMinutes   = 750
        }
    ];
}

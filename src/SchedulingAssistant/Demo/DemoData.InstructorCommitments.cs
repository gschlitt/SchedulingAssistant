using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<InstructorCommitment> InstructorCommitments =
    [
        new()
        {
            Id           = "demo-commit-1",
            InstructorId = "demo-inst-2",
            SemesterId   = "demo-sem-1",
            Name         = "Lib Comm",
            Day          = 3,
            StartMinutes = 840,
            EndMinutes   = 1050
        }
    ];
}

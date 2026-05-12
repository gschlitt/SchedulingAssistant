using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Release> Releases =
    [
        new()
        {
            Id            = "demo-release-1",
            SemesterId    = "demo-sem-2",
            InstructorId  = "demo-inst-39",
            Title         = "DepHead",
            WorkloadValue = 1m
        },
        new()
        {
            Id            = "demo-release-2",
            SemesterId    = "demo-sem-1",
            InstructorId  = "demo-inst-39",
            Title         = "DepHead",
            WorkloadValue = 2m
        }
    ];
}

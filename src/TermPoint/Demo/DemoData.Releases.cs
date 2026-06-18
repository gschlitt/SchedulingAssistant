using TermPoint.Models;

namespace TermPoint.Demo;

public static partial class DemoData
{
    public static readonly List<Release> Releases =
    [
        new()
        {
            Id            = "demo-release-1",
            SemesterId    = "demo-sem-1",
            InstructorId  = "demo-inst-2",
            Title         = "Research",
            WorkloadValue = 1m
        }
    ];
}

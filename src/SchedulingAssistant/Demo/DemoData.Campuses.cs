using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Campus> Campuses =
    [
        new()
        {
            Id           = "demo-campus-1",
            Name         = "ABB"
        },
        new()
        {
            Id           = "demo-campus-2",
            Name         = "CHWK"
        }
    ];
}

using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Campus> Campuses =
    [
        new()
        {
            Id           = "demo-campus-main",
            Name         = "Main Campus",
            Abbreviation = "MC",
            SortOrder    = 1
        }
    ];
}

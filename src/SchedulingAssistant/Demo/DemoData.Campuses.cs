using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Campus> Campuses =
    [
        new()
        {
            Id           = "demo-campus-1",
            Name         = "Westbrook",
            Abbreviation = "WB"
        },
        new()
        {
            Id           = "demo-campus-2",
            Name         = "Lakeview",
            Abbreviation = "LV",
            SortOrder    = 1
        }
    ];
}

using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Campus> Campuses =
    [
        new()
        {
            Id           = "demo-campus-1",
            Name         = "Lakeview",
            Abbreviation = "LV"
        },
        new()
        {
            Id           = "demo-campus-2",
            Name         = "Westbrook",
            Abbreviation = "WB"
        }
    ];
}

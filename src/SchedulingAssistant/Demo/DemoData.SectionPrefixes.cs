using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<SectionPrefix> SectionPrefixes =
    [
        new()
        {
            Id       = "demo-prefix-a",
            Prefix   = "A",
            CampusId = "demo-campus-main"
        }
    ];
}

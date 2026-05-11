using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<SectionCodePattern> SectionCodePatterns =
    [
        new()
        {
            Id            = "demo-scp-1",
            Name          = "WSLec",
            Prefix        = "WS",
            CampusId      = "demo-campus-2",
            Examples      = "WS1, WS2"
        },
        new()
        {
            Id            = "demo-scp-2",
            Name          = "WSLab",
            Prefix        = "W#",
            UseLetters    = true,
            CampusId      = "demo-campus-2",
            SortOrder     = 1,
            Examples      = "W#A, W#B"
        },
        new()
        {
            Id            = "demo-scp-3",
            Name          = "LKLec",
            Prefix        = "LK",
            CampusId      = "demo-campus-1",
            SortOrder     = 2,
            Examples      = "LK1, LK2"
        },
        new()
        {
            Id            = "demo-scp-4",
            Name          = "LKLab",
            Prefix        = "L#",
            UseLetters    = true,
            CampusId      = "demo-campus-1",
            SortOrder     = 3,
            Examples      = "L#A, L#B"
        }
    ];
}

using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<BlockPattern> BlockPatterns =
    [
        new()
        {
            Id   = "demo-bp-mwf",
            Name = "MWF",
            Days = [1, 3, 5]
        },
        new()
        {
            Id   = "demo-bp-tth",
            Name = "TTh",
            Days = [2, 4]
        }
    ];
}

using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<BlockPattern> BlockPatterns =
    [
        new()
        {
            Id   = "demo-bp-1",
            Name = "MWF",
            Days = [1, 3, 5]
        },
        new()
        {
            Id   = "demo-bp-2",
            Name = "TR",
            Days = [2, 4]
        }
    ];
}

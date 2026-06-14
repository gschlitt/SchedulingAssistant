using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<BlockPattern> BlockPatterns =
    [
        new()
        {
            Id   = "demo-bp-1",
            Name = "MW",
            Days = [1, 3]
        },
        new()
        {
            Id   = "demo-bp-2",
            Name = "MWF",
            Days = [1, 3, 5]
        },
        new()
        {
            Id   = "demo-bp-3",
            Name = "TR",
            Days = [2, 4]
        },
        new()
        {
            Id   = "demo-bp-4",
            Name = "TRF",
            Days = [2, 4]
        }
    ];
}

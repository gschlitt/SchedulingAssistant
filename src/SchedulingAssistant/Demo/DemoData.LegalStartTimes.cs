using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<LegalStartTime> LegalStartTimes =
    [
        new()
        {
            BlockLength = 1.0,
            StartTimes  = [480, 540, 600, 660, 720, 780, 840, 900, 960]
        },
        new()
        {
            BlockLength = 1.5,
            StartTimes  = [480, 570, 660, 750, 840, 930]
        }
    ];
}

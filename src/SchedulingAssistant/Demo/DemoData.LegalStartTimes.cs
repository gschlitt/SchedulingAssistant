using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<LegalStartTime> LegalStartTimes =
    [
        new()
        {
            BlockLength = 1.5,
            StartTimes  = [510, 600, 690, 780, 870, 960, 1050, 1080]
        },
        new()
        {
            BlockLength = 3.0,
            StartTimes  = [510, 690, 870, 960, 1050, 1080, 1110, 1140]
        }
    ];
}

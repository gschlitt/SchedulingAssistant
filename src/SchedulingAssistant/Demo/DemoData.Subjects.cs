using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Subject> Subjects =
    [
        new()
        {
            Id                   = "demo-subj-1",
            Name                 = "Biology",
            CalendarAbbreviation = "BIO"
        }
    ];
}

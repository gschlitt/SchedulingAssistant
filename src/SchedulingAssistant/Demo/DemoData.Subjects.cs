using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Subject> Subjects =
    [
        new()
        {
            Id                    = "demo-subj-cs",
            Name                  = "Computer Science",
            CalendarAbbreviation  = "COMP"
        },
        new()
        {
            Id                    = "demo-subj-math",
            Name                  = "Mathematics",
            CalendarAbbreviation  = "MATH"
        }
    ];
}

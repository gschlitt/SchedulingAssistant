using TermPoint.Models;

namespace TermPoint.Demo;

public static partial class DemoData
{
    public static readonly List<Subject> Subjects =
    [
        new()
        {
            Id                   = "demo-subj-1",
            Name                 = "Geography",
            CalendarAbbreviation = "GEOG"
        }
    ];
}

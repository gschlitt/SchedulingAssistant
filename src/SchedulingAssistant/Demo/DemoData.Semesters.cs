using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Semester> Semesters =
    [
        new()
        {
            Id              = "demo-sem-fall",
            AcademicYearId  = "demo-ay-2025",
            Name            = "Fall",
            SortOrder       = 1
        },
        new()
        {
            Id              = "demo-sem-spring",
            AcademicYearId  = "demo-ay-2025",
            Name            = "Spring",
            SortOrder       = 2
        }
    ];
}

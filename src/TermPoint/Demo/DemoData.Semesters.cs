using TermPoint.Models;

namespace TermPoint.Demo;

public static partial class DemoData
{
    public static readonly List<Semester> Semesters =
    [
        new()
        {
            Id             = "demo-sem-1",
            AcademicYearId = "demo-ay-1",
            Name           = "Fall",
            SortOrder      = 0
        },
        new()
        {
            Id             = "demo-sem-2",
            AcademicYearId = "demo-ay-1",
            Name           = "Winter",
            SortOrder      = 1
        },
        new()
        {
            Id             = "demo-sem-3",
            AcademicYearId = "demo-ay-1",
            Name           = "Early Summer",
            SortOrder      = 2
        },
        new()
        {
            Id             = "demo-sem-4",
            AcademicYearId = "demo-ay-1",
            Name           = "Summer",
            SortOrder      = 3
        },
        new()
        {
            Id             = "demo-sem-5",
            AcademicYearId = "demo-ay-1",
            Name           = "Late Summer",
            SortOrder      = 4
        }
    ];
}

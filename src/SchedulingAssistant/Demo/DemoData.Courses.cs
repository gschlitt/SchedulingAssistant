using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Course> Courses =
    [
        new()
        {
            Id            = "demo-course-comp101",
            SubjectId     = "demo-subj-cs",
            CalendarCode  = "COMP 101",
            Title         = "Introduction to Computing",
            IsActive      = true
        },
        new()
        {
            Id            = "demo-course-comp201",
            SubjectId     = "demo-subj-cs",
            CalendarCode  = "COMP 201",
            Title         = "Data Structures",
            IsActive      = true
        },
        new()
        {
            Id            = "demo-course-math101",
            SubjectId     = "demo-subj-math",
            CalendarCode  = "MATH 101",
            Title         = "Calculus I",
            IsActive      = true
        }
    ];
}

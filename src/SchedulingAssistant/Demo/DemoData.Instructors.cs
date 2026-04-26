using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Instructor> Instructors =
    [
        new()
        {
            Id        = "demo-inst-1",
            FirstName = "Jane",
            LastName  = "Smith",
            Initials  = "JS",
            IsActive  = true
        },
        new()
        {
            Id        = "demo-inst-2",
            FirstName = "John",
            LastName  = "Doe",
            Initials  = "JD",
            IsActive  = true
        }
    ];
}

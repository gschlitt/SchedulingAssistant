namespace SchedulingAssistant.Models;

/// <summary>
/// Controls the column by which the instructor list (and all instructor loads) are sorted.
/// Persisted in <see cref="Services.AppSettings"/> so the user's preference is remembered
/// across sessions and propagated to every place that loads instructors from the database.
/// </summary>
public enum InstructorSortMode
{
    LastName  = 0,
    FirstName = 1,
    Initials  = 2,
    StaffType = 3,
}

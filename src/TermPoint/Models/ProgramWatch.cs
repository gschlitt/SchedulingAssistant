namespace TermPoint.Models;

/// <summary>
/// A monitored group of courses whose sections are checked for time overlaps,
/// helping administrators assess student access to the schedule.
/// </summary>
public class ProgramWatch
{
    /// <summary>Stable surrogate key.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name — auto-generated from tags or courses, user-editable.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the watch uses tag-based or course-based section matching.</summary>
    public ProgramWatchMode Mode { get; set; } = ProgramWatchMode.Tag;

    /// <summary>When false, the watch is retained but produces no conflict indications.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Tag IDs for tag-based mode (AND logic — a section must carry all listed tags).
    /// References <c>SchedulingEnvironmentValues</c> rows where type = 'tag'.
    /// </summary>
    public List<string> TagIds { get; set; } = [];

    /// <summary>
    /// Course IDs for course-based mode.
    /// References <c>Courses</c> rows by ID.
    /// </summary>
    public List<string> CourseIds { get; set; } = [];

    /// <summary>
    /// Level values for tag/level-based mode (OR logic — a section matches if its level
    /// equals any listed value). When empty, level filtering is not applied.
    /// Values are the same strings used by <see cref="Services.CourseLevelParser"/>
    /// (e.g. "100", "200", "300").
    /// </summary>
    public List<string> LevelIds { get; set; } = [];
}

namespace SchedulingAssistant.Models;

/// <summary>
/// A faculty, committee, or other recurring meeting that appears on the weekly schedule
/// grid alongside sections. Unlike a <see cref="Section"/>, a meeting has no course
/// association — it is identified by a plain title string.
///
/// Inherits all common scheduling fields from <see cref="SchedulableBase"/>:
/// day/time slots, rooms, campus, tags, resources, and attendees (drawn from the
/// instructor pool via <see cref="SchedulableBase.InstructorAssignments"/>).
/// </summary>
public class Meeting : SchedulableBase
{
    /// <summary>
    /// Human-readable title for this meeting (e.g. "Faculty Meeting", "Curriculum Committee").
    /// Also stored in the dedicated <c>title</c> column for easy DB browsing.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-text notes about this meeting (visible only in the meeting editor).</summary>
    public string Notes { get; set; } = string.Empty;
}

namespace SchedulingAssistant.Models;

/// <summary>
/// Abstract base for any entity that can be placed on the weekly schedule grid.
/// Holds the fields that <see cref="Section"/> and <see cref="Meeting"/> share:
/// day/time slots (via <see cref="Schedule"/>), rooms (stored per-slot in each
/// <see cref="SectionDaySchedule"/>), campus, tags, resources, and attendee/instructor
/// assignments.
/// </summary>
public abstract class SchedulableBase
{
    /// <summary>Unique database identifier. Must be set before any repository call.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The semester this item belongs to. Set from the dedicated DB column, not from JSON.</summary>
    public string SemesterId { get; set; } = string.Empty;

    /// <summary>
    /// One entry per scheduled time slot (day + start + duration).
    /// A single schedulable item may meet on multiple days (e.g. MWF), each represented
    /// by its own <see cref="SectionDaySchedule"/>. Stored in the JSON data column.
    /// </summary>
    public List<SectionDaySchedule> Schedule { get; set; } = new();

    /// <summary>
    /// People associated with this item drawn from the instructor pool, optionally
    /// weighted by workload fraction. For sections these are the teaching instructors;
    /// for meetings these are the attendees.
    /// Stored in the JSON data column.
    /// </summary>
    public List<InstructorAssignment> InstructorAssignments { get; set; } = new();

    /// <summary>
    /// Convenience accessor — returns just the instructor IDs from
    /// <see cref="InstructorAssignments"/>. Not persisted; derived on every access.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> InstructorIds => InstructorAssignments.Select(a => a.InstructorId);

    /// <summary>Optional campus ID (FK into the Campuses table). Stored in JSON.</summary>
    public string? CampusId { get; set; }

    /// <summary>
    /// Tag IDs assigned to this item (FK into SchedulingEnvironmentValues of type "tag").
    /// Stored in JSON.
    /// </summary>
    public List<string> TagIds { get; set; } = new();

    /// <summary>
    /// Resource IDs assigned to this item (FK into SchedulingEnvironmentValues of type "resource").
    /// Stored in JSON.
    /// </summary>
    public List<string> ResourceIds { get; set; } = new();
}

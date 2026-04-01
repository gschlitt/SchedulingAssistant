namespace SchedulingAssistant.Models;

/// <summary>
/// The core scheduling entity — a course section offered in a specific semester.
/// Inherits all common scheduling fields (time slots, rooms, campus, tags, resources,
/// instructor assignments) from <see cref="SchedulableBase"/>.
/// </summary>
public class Section : SchedulableBase
{
    /// <summary>FK into the Courses table. Set from the dedicated DB column, not from JSON.</summary>
    public string? CourseId { get; set; }

    // ── Section-specific fields (stored in the JSON data column) ─────────────

    /// <summary>Uniquely identifies this section within its course and semester (e.g. "A", "AB1").</summary>
    public string SectionCode { get; set; } = string.Empty;

    /// <summary>Free-text notes about this section (visible only in the section editor).</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Section-type property-value ID (FK into SchedulingEnvironmentValues of type "sectionType").</summary>
    public string? SectionTypeId { get; set; }

    /// <summary>Reserved-seat blocks for this section. Stored in JSON.</summary>
    public List<SectionReserve> Reserves { get; set; } = new();

    /// <summary>
    /// Course level band, copied from the course at save time (e.g. "100", "300").
    /// Stored here so level filtering on the grid does not require a course lookup.
    /// Null or empty when the section's course has no level assigned.
    /// </summary>
    public string? Level { get; set; }
}

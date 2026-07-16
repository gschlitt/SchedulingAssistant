using System.Text.Json.Serialization;

namespace TermPoint.Models;

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

    /// <summary>
    /// Optional seating capacity for this section. Null means "not specified".
    /// Seeded at creation from the course's capacity, else the app default; freely editable.
    /// </summary>
    public int? Capacity { get; set; }

    /// <summary>
    /// Advisory attention flag for this section. <see cref="SectionFlag.None"/> means no flag.
    /// Stored in the JSON data column; pre-existing sections without the field deserialize to None.
    /// </summary>
    public SectionFlag Flag { get; set; } = SectionFlag.None;

    // ── Transient shared-schedule properties (never persisted) ─────────────────
    // Populated only for imported shared sections. All default to false/null,
    // so local sections incur zero cost.

    /// <summary>True for imported shared sections. Prevents edit/save operations.</summary>
    [JsonIgnore] public bool IsShared { get; init; }

    /// <summary>Display label identifying the source department (e.g. "Chemistry Department").</summary>
    [JsonIgnore] public string? SourceLabel { get; init; }

    /// <summary>Exported course code string for grid tile rendering. Local sections use CourseId lookup instead.</summary>
    [JsonIgnore] public string? DisplayCourseCode { get; init; }

    /// <summary>Exported instructor names and initials for display. Both resolved and unresolved instructors.</summary>
    [JsonIgnore] public List<(string Name, string Initials)>? DisplayInstructors { get; init; }

    /// <summary>Room conflict annotation, populated after conflict detection runs.</summary>
    [JsonIgnore] public string? RoomConflictNote { get; set; }

    /// <summary>Instructor conflict annotation, populated after conflict detection runs.</summary>
    [JsonIgnore] public string? InstructorConflictNote { get; set; }

    /// <summary>Raw imported campus name from CSV, consumed by ImportResolver then discarded.</summary>
    [JsonIgnore] public string? ImportedCampusName { get; init; }

    /// <summary>Raw imported section type name from CSV, consumed by ImportResolver then discarded.</summary>
    [JsonIgnore] public string? ImportedSectionTypeName { get; init; }

    /// <summary>Raw imported tag names from CSV, consumed by ImportResolver then discarded.</summary>
    [JsonIgnore] public List<string>? ImportedTagNames { get; init; }
}

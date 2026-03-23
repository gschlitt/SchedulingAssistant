namespace SchedulingAssistant.Models;

/// <summary>
/// A physical or operational campus associated with rooms, section prefixes, and sections.
/// First-class entity stored in the <c>Campuses</c> table.
/// </summary>
public class Campus
{
    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Full display name of the campus (e.g. "Main Campus").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short abbreviation used in section code generation (e.g. "MC").
    /// Null or empty when no abbreviation is configured.
    /// </summary>
    public string? Abbreviation { get; set; }

    /// <summary>
    /// Display order within the campus list. Lower values appear first.
    /// Densely re-packed after each move operation.
    /// </summary>
    public int SortOrder { get; set; } = 0;
}

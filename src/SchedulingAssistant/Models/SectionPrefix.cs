namespace SchedulingAssistant.Models;

/// <summary>
/// A section code prefix — the initial symbols used in a typical section code,
/// such as "AB" or "A#". An optional campus association indicates which campus
/// sections with this prefix are typically offered at.
/// </summary>
public class SectionPrefix
{
    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The prefix string itself (e.g. "AB", "A#", "CH"). Case-sensitive as stored;
    /// uniqueness is enforced case-insensitively.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Optional foreign key into <c>SectionPropertyValues</c> (type = "campus").
    /// Null when the prefix is not associated with a specific campus.
    /// </summary>
    public string? CampusId { get; set; }
}

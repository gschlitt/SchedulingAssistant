namespace SchedulingAssistant.Models;

/// <summary>
/// Specifies whether the section designator that follows a prefix is a number or a letter.
/// </summary>
public enum DesignatorType
{
    /// <summary>The designator is an integer (e.g. "AB1", "AB17").</summary>
    Number,

    /// <summary>The designator is a single letter (e.g. "TUTA", "A#C").</summary>
    Letter
}

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

    /// <summary>
    /// Whether section designators that follow this prefix are numbers (e.g. 1, 17)
    /// or letters (e.g. A, C). Defaults to <see cref="DesignatorType.Number"/>.
    /// </summary>
    public DesignatorType DesignatorType { get; set; } = DesignatorType.Number;
}

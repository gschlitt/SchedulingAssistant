namespace SchedulingAssistant.Models;

public class SectionPropertyValue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional short code used as a section code prefix/suffix for this campus.
    /// Only meaningful for the Campus property type; null for all others.
    /// </summary>
    public string? SectionCodeAbbreviation { get; set; }
}

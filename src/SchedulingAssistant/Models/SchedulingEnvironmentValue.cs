namespace SchedulingAssistant.Models;

public class SchedulingEnvironmentValue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional short code used as a section code prefix/suffix for this campus.
    /// Only meaningful for the Campus property type; null for all others.
    /// </summary>
    public string? SectionCodeAbbreviation { get; set; }

    /// <summary>
    /// Display order within this property type. Lower values appear first.
    /// Defaults to 0 for all existing records; densely re-packed after each move operation.
    /// </summary>
    public int SortOrder { get; set; } = 0;
}

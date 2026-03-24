namespace SchedulingAssistant.Models;

public class AcademicUnit
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>Short abbreviation used in the suggested database filename (e.g. "CS").</summary>
    public string Abbreviation { get; set; } = string.Empty;
}

namespace SchedulingAssistant.Models;

public class Semester
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AcademicYearId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>
    /// The fixed set of semester names auto-created for each Academic Year.
    /// </summary>
    public static readonly string[] DefaultNames =
        ["Fall", "Winter", "Summer", "Early Summer", "Late Summer"];
}

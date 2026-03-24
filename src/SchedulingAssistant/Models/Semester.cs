namespace SchedulingAssistant.Models;

public class Semester
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AcademicYearId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>
    /// Hex color string (e.g. "#C65D1E") assigned to this semester for grid display.
    /// Empty string falls back to the name-based color lookup in ScheduleGridViewModel.
    /// </summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// The fixed set of semester names auto-created for each Academic Year.
    /// </summary>
    public static readonly string[] DefaultNames =
        ["Fall", "Winter", "Early Summer", "Summer", "Late Summer"];
}

namespace SchedulingAssistant.Models;

public class AcademicUnit
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
}

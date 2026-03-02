namespace SchedulingAssistant.Models;

public class Subject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string CalendarAbbreviation { get; set; } = string.Empty;
}

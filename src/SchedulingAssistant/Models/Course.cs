namespace SchedulingAssistant.Models;

public class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string CalendarCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

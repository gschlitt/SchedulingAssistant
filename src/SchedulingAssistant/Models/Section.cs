namespace SchedulingAssistant.Models;

public class Section
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SemesterId { get; set; } = string.Empty;
    public string? CourseId { get; set; }
    public string? InstructorId { get; set; }
    public string? RoomId { get; set; }

    // Fields stored in the data JSON column
    public string SectionCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<SectionDaySchedule> Schedule { get; set; } = new();
}

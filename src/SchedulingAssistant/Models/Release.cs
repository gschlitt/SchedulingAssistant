namespace SchedulingAssistant.Models;

public class Release
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SemesterId { get; set; } = string.Empty;
    public string InstructorId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal WorkloadValue { get; set; }
}

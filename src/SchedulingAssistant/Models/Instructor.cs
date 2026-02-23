namespace SchedulingAssistant.Models;

public class Instructor
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? StaffTypeId { get; set; }
}

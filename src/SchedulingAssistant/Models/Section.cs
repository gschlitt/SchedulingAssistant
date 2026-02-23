namespace SchedulingAssistant.Models;

public class Section
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SemesterId { get; set; } = string.Empty;
    public string? CourseId { get; set; }

    // Fields stored in the data JSON column
    public string SectionCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<SectionDaySchedule> Schedule { get; set; } = new();

    /// <summary>Multi-instructor support with workload. Stored in JSON data column.</summary>
    public List<InstructorAssignment> InstructorAssignments { get; set; } = new();

    /// <summary>Backward-compat accessor — returns just the IDs from InstructorAssignments.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> InstructorIds => InstructorAssignments.Select(a => a.InstructorId);

    // Section property assignments (stored as IDs into SectionPropertyValues)
    public string? SectionTypeId { get; set; }
    public string? CampusId { get; set; }
    public List<string> TagIds { get; set; } = new();
    public List<string> ResourceIds { get; set; } = new();
    public List<SectionReserve> Reserves { get; set; } = new();
}

namespace SchedulingAssistant.Models;

/// <summary>Links an instructor to a section, with an optional workload share.</summary>
public class InstructorAssignment
{
    public string InstructorId { get; set; } = string.Empty;
    /// <summary>Workload contribution for this instructor on this section (e.g. 1.0 = full, 0.5 = half).</summary>
    public decimal? Workload { get; set; }
}

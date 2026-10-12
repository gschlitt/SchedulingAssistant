namespace TermPoint.Models;

/// <summary>
/// A workload release (course-load reduction) granted to an instructor for a semester.
/// Derives from <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// </summary>
public class Release : PersistedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SemesterId { get; set; } = string.Empty;
    public string InstructorId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal WorkloadValue { get; set; }
}

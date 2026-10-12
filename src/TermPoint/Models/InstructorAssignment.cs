namespace TermPoint.Models;

/// <summary>
/// Links an instructor to a section, with an optional workload share.
/// A record nested inside <see cref="SchedulableBase.InstructorAssignments"/>; it derives from
/// <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// Editors that rebuild the assignment list must carry the original's unknown fields across
/// (see <see cref="PersistedRecord"/>).
/// </summary>
public class InstructorAssignment : PersistedRecord
{
    public string InstructorId { get; set; } = string.Empty;
    /// <summary>Workload contribution for this instructor on this section (e.g. 1.0 = full, 0.5 = half).</summary>
    public decimal? Workload { get; set; }
}

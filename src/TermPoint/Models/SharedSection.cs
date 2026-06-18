namespace TermPoint.Models;

/// <summary>
/// One logical section from an imported shared schedule CSV.
/// In-memory only — never persisted to the local database.
/// </summary>
public class SharedSection
{
    /// <summary>Human-readable course identifier (e.g. "CHEM101").</summary>
    public string CourseCode { get; set; } = string.Empty;

    /// <summary>Human-readable section identifier (e.g. "A").</summary>
    public string SectionCode { get; set; } = string.Empty;

    /// <summary>Optional freeform notes from the sender.</summary>
    public string? Notes { get; set; }

    /// <summary>One entry per meeting occurrence. Empty for unscheduled sections.</summary>
    public List<SharedMeeting> Meetings { get; set; } = new();
}

namespace SchedulingAssistant.Models;

/// <summary>
/// Container for one CSV import operation. Holds all sections from a single
/// shared schedule file. In-memory only — never persisted to the local database.
/// </summary>
public class SharedScheduleSet
{
    /// <summary>
    /// Display label identifying the source (e.g. "Chemistry Department").
    /// From the CSV header comment, or the filename if the header is absent.
    /// </summary>
    public string SourceLabel { get; set; } = string.Empty;

    /// <summary>Date the CSV was exported. Null if the header comment was absent or unparseable.</summary>
    public DateTime? ExportedAt { get; set; }

    /// <summary>All sections in this shared schedule.</summary>
    public List<SharedSection> Sections { get; set; } = new();
}

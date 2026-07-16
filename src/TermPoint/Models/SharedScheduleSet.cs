namespace TermPoint.Models;

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

    /// <summary>All sections in this shared schedule (resolved <see cref="Section"/> objects with IsShared = true).</summary>
    public List<Section> Sections { get; set; } = new();

    /// <summary>
    /// Resolution outcome from the import step. Null when the import predates
    /// the enriched format or resolution has not yet run.
    /// </summary>
    public ImportResolutionSummary? ResolutionSummary { get; set; }
}

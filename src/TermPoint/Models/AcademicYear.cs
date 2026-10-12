namespace TermPoint.Models;

/// <summary>
/// An academic year (e.g. "2025-2026") that groups semesters.
/// Derives from <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// </summary>
public class AcademicYear : PersistedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Extracts the start year from a name formatted as "YYYY-YYYY".
    /// Returns int.MaxValue if the format is unrecognised (sorts to end).
    /// </summary>
    public int StartYear =>
        Name.Length >= 4 && int.TryParse(Name[..4], out var y) ? y : int.MaxValue;
}

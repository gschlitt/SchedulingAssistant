namespace TermPoint.Models;

/// <summary>
/// A subject area (e.g. "History", abbreviation "HIST") under which courses are catalogued.
/// Derives from <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// </summary>
public class Subject : PersistedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string CalendarAbbreviation { get; set; } = string.Empty;
}

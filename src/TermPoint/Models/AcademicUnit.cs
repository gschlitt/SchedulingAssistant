namespace TermPoint.Models;

/// <summary>
/// The single academic unit (department) a database belongs to, plus its institution identity.
/// Derives from <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// </summary>
public class AcademicUnit : PersistedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>Short abbreviation used in the suggested database filename (e.g. "CS").</summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>Name of the parent institution (e.g. "Greendale Community College").</summary>
    public string InstitutionName { get; set; } = string.Empty;

    /// <summary>Short abbreviation for the institution (e.g. "GCC").</summary>
    public string InstitutionAbbrev { get; set; } = string.Empty;
}

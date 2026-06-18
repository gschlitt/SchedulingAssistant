namespace TermPoint.Models;

public class AcademicUnit
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

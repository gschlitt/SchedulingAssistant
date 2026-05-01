namespace SchedulingAssistant.Models;

/// <summary>
/// Portable configuration file (.tpconfig) written to the database folder after first-run setup.
/// Contains institution-level defaults that can be shared between colleagues or reused when
/// creating a new database at the same institution.
/// Does NOT contain institution name, abbreviation, or academic unit — those are per-DB.
/// </summary>
public class TpConfigData
{
    /// <summary>Block length definitions with their legal start times.</summary>
    public List<TpConfigBlockLength> BlockLengths { get; set; } = [];

    /// <summary>Campus name definitions.</summary>
    public List<string> Campuses { get; set; } = [];

    /// <summary>Semester template definitions, in display order.</summary>
    public List<TpConfigSemesterDef> SemesterDefs { get; set; } = [];

    /// <summary>Day-pattern definitions (e.g. MWF, TR).</summary>
    public List<TpConfigBlockPattern> BlockPatterns { get; set; } = [];

    /// <summary>Whether Saturday is available as a scheduling day.</summary>
    public bool IncludeSaturday { get; set; } = false;

    /// <summary>Whether Sunday is available as a scheduling day.</summary>
    public bool IncludeSunday { get; set; } = false;
}

/// <summary>A block length entry with its legal start times.</summary>
public class TpConfigBlockLength
{
    /// <summary>Duration in fractional hours (e.g. 1.5, 2.0, 3.0).</summary>
    public double Hours { get; set; }

    /// <summary>Valid start times as minutes-from-midnight (e.g. 510 = 08:30).</summary>
    public List<int> StartTimes { get; set; } = [];
}

/// <summary>A named day-pattern entry (e.g. "MWF" with days [1,3,5]).</summary>
public class TpConfigBlockPattern
{
    /// <summary>User-facing label (e.g. "MWF").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Day numbers (1=Mon … 7=Sun).</summary>
    public List<int> Days { get; set; } = [];
}

/// <summary>A semester template entry with a name and default display color.</summary>
public class TpConfigSemesterDef
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color string (e.g. "#C65D1E"). Empty = use position-based default.</summary>
    public string Color { get; set; } = string.Empty;
}

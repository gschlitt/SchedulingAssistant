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

    /// <summary>Section code pattern definitions.</summary>
    public List<TpConfigSectionCodePattern> SectionCodePatterns { get; set; } = [];

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

/// <summary>A section code pattern entry for .tpconfig portability.</summary>
public class TpConfigSectionCodePattern
{
    /// <summary>User-facing label (e.g. "Lecture", "Lab").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Fixed text prepended to the incrementing part.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Fixed text appended after the incrementing part.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>When true, the incrementing part uses letters (A, B, C…); otherwise integers.</summary>
    public bool UseLetters { get; set; }

    /// <summary>First numeric value in the sequence (when UseLetters is false).</summary>
    public int FirstNumber { get; set; } = 1;

    /// <summary>Minimum display width for the numeric part, left-padded with zeros.</summary>
    public int PadWidth { get; set; }

    /// <summary>Step between successive numeric codes.</summary>
    public int Increment { get; set; } = 1;

    /// <summary>First letter in the sequence (when UseLetters is true).</summary>
    public char FirstLetter { get; set; } = 'A';

    /// <summary>Campus name for pre-fill resolution (portable — not an ID).</summary>
    public string? CampusName { get; set; }

    /// <summary>Section type name for pre-fill resolution (portable — not an ID).</summary>
    public string? SectionTypeName { get; set; }

    /// <summary>Display order within the pattern chooser list.</summary>
    public int SortOrder { get; set; }
}

/// <summary>A semester template entry with a name and default display color.</summary>
public class TpConfigSemesterDef
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color string (e.g. "#C65D1E"). Empty = use position-based default.</summary>
    public string Color { get; set; } = string.Empty;
}

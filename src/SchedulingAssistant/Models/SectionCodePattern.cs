namespace SchedulingAssistant.Models;

/// <summary>
/// A named template that governs how section codes are generated for a particular
/// type of section (e.g. "Lecture", "Lab", "Evening Tutorial").
///
/// The generated code is: <c>Prefix + IncrementingPart + Suffix</c>.
/// The incrementing part is either a formatted integer sequence or an alphabetic sequence.
/// </summary>
public class SectionCodePattern
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User-facing label shown in the pattern chooser, e.g. "Lecture" or "Lab".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fixed text prepended to the incrementing part, e.g. "D", "AB", "L", "A#".
    /// May include spaces or punctuation (e.g. "AB "). May be empty.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Fixed text appended after the incrementing part. May include spaces or punctuation.
    /// May be empty.
    /// </summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, the incrementing part uses letters (A, B, C…).
    /// When <c>false</c>, it uses integers.
    /// </summary>
    public bool UseLetters { get; set; } = false;

    // ── Numeric sequence fields (used when UseLetters == false) ───────────────

    /// <summary>The numeric value of the first code in the sequence (e.g. 1, 100).</summary>
    public int FirstNumber { get; set; } = 1;

    /// <summary>
    /// Minimum display width for the numeric part, left-padded with zeros.
    /// 0 means no padding (output as-is). 3 means "001", "002", etc.
    /// </summary>
    public int PadWidth { get; set; } = 0;

    /// <summary>The step between successive numeric codes (e.g. 1, 10, 100).</summary>
    public int Increment { get; set; } = 1;

    // ── Letter sequence fields (used when UseLetters == true) ─────────────────

    /// <summary>The letter of the first code in the sequence (e.g. 'A').</summary>
    public char FirstLetter { get; set; } = 'A';

    // ── Pre-fill fields ───────────────────────────────────────────────────────

    /// <summary>
    /// Optional campus to pre-fill in the section editor when this pattern is chosen.
    /// Null means no pre-fill.
    /// </summary>
    public string? CampusId { get; set; }

    /// <summary>
    /// Optional section-type property value ID to pre-fill when this pattern is chosen.
    /// Null means no pre-fill.
    /// </summary>
    public string? SectionTypeId { get; set; }

    /// <summary>Display order within the pattern chooser list.</summary>
    public int SortOrder { get; set; } = 0;
}

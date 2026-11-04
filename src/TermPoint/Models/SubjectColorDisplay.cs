namespace TermPoint.Models;

/// <summary>
/// When subject colours are shown on section cards and schedule tiles. Stored in <c>settings.json</c> as a number
/// (like the other enum settings), so the explicit values below must never change or be reused: a stored 1 must
/// always mean <see cref="Always"/>. Add new modes with new numbers only.
/// </summary>
public enum SubjectColorDisplay
{
    /// <summary>Colours show only while the sections in view span two or more subjects. The default.</summary>
    OnlyWhenSeveral = 0,

    /// <summary>Colours always show (when at least one subject has a colour).</summary>
    Always = 1,

    /// <summary>Colours never show.</summary>
    Never = 2
}

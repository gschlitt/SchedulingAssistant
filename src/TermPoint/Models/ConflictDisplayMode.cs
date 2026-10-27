namespace TermPoint.Models;

/// <summary>
/// How a section card shows the section's room and instructor conflicts (spec item 25). A per-user display choice,
/// stored in <see cref="Services.AppSettings.SectionConflictDisplay"/>; conflicts are detected the same way in both.
/// </summary>
public enum ConflictDisplayMode
{
    /// <summary>One line per conflict, in full. The original look, and the default.</summary>
    Full,

    /// <summary>
    /// One compact flag per kind of conflict (room, instructor), all on one line, with that kind's full list shown when
    /// the pointer rests on the flag. Saves vertical space when co-taught courses produce long lists.
    /// </summary>
    Flag,
}

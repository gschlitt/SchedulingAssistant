namespace TermPoint.Models;

/// <summary>
/// Determines how a <see cref="ProgramWatch"/> resolves its covered sections.
/// </summary>
public enum ProgramWatchMode
{
    /// <summary>Sections are included if they carry all specified tags (AND logic).</summary>
    Tag,

    /// <summary>Sections are included if their course is in the watch's course list.</summary>
    Course
}

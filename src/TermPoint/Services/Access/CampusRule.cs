using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The campus rule (Access Analyzer rule 15, revised by 6e): when a watch's Multi-campus box is off, a slate keeps
/// its in-person sections at one campus; online sections and sections with no campus fit any campus. Pure and
/// static.
/// </summary>
public static class CampusRule
{
    /// <summary>
    /// Whether a section is in person for the campus rule: it has at least one non-remote meeting and a campus.
    /// </summary>
    /// <param name="section">The section to test.</param>
    /// <returns><c>true</c> when the section is in person.</returns>
    public static bool IsInPerson(Section section) =>
        section.CampusId is not null && section.Schedule.Any(m => !m.IsRemote);

    /// <summary>
    /// Whether two sections cannot share a one-campus slate: both are in person and at different campuses.
    /// </summary>
    /// <param name="a">The first section.</param>
    /// <param name="b">The second section.</param>
    /// <returns><c>true</c> when the two are in person at different campuses.</returns>
    public static bool Splits(Section a, Section b) =>
        IsInPerson(a) && IsInPerson(b) && a.CampusId != b.CampusId;
}

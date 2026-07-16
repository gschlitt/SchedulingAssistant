namespace TermPoint.Models;

/// <summary>
/// Captures the outcome of resolving exported display names against local entities
/// during shared schedule import. Used for status messages and the shared schedule strip.
/// Counts reflect distinct names, not occurrences.
/// </summary>
public record ImportResolutionSummary(
    int ResolvedInstructorCount,
    int UnresolvedInstructorCount,
    int ResolvedRoomCount,
    int UnresolvedRoomCount,
    int ResolvedTagCount,
    int UnresolvedTagCount,
    bool ResolvedCampus,
    bool ResolvedSectionType,
    int ResolvedMeetingTypeCount,
    int UnresolvedMeetingTypeCount,
    List<string> Warnings)
{
    /// <summary>An empty summary with no resolved or unresolved items and no warnings.</summary>
    public static ImportResolutionSummary Empty => new(0, 0, 0, 0, 0, 0, false, false, 0, 0, new());
}

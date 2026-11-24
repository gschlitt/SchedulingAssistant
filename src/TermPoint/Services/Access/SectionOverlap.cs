using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// One pair of meetings, one from each of two sections, that overlap (same day, overlapping time, at least one
/// shared week). Used to draw conflict marks on the grid.
/// </summary>
/// <param name="A">The meeting of the first section.</param>
/// <param name="B">The meeting of the second section.</param>
public readonly record struct MeetingPair(SectionDaySchedule A, SectionDaySchedule B);

/// <summary>
/// The one conflict test (Access Analyzer rule 13), used by the program-conflict detector and the access
/// analyzer: two sections conflict if some meeting of one and some meeting of the other fall on the same day,
/// overlap in time (half-open: one ending at 10:00 and one starting at 10:00 do not conflict) and share at least
/// one week. A section with no meetings conflicts with nothing. Pure and static; no state.
/// </summary>
public static class SectionOverlap
{
    /// <summary>
    /// Whether two single meetings overlap: same day, half-open time overlap, and a shared week
    /// (<see cref="SectionDaySchedule.FrequenciesOverlap"/>).
    /// </summary>
    /// <param name="a">The first meeting.</param>
    /// <param name="b">The second meeting.</param>
    /// <returns><c>true</c> when the meetings overlap.</returns>
    public static bool MeetingsOverlap(SectionDaySchedule a, SectionDaySchedule b) =>
        a.Day == b.Day
        && a.StartMinutes < b.EndMinutes
        && b.StartMinutes < a.EndMinutes
        && SectionDaySchedule.FrequenciesOverlap(a.Frequency, b.Frequency);

    /// <summary>
    /// Whether two sections conflict (rule 13): some meeting of one overlaps some meeting of the other.
    /// </summary>
    /// <param name="a">The first section.</param>
    /// <param name="b">The second section.</param>
    /// <returns><c>true</c> when the sections conflict; always <c>false</c> if either has no meetings.</returns>
    public static bool Conflicts(Section a, Section b)
    {
        foreach (var ma in a.Schedule)
            foreach (var mb in b.Schedule)
                if (MeetingsOverlap(ma, mb)) return true;
        return false;
    }

    /// <summary>
    /// Every overlapping meeting pair between two sections, in the order meetings of <paramref name="a"/> then
    /// meetings of <paramref name="b"/> appear.
    /// </summary>
    /// <param name="a">The first section.</param>
    /// <param name="b">The second section.</param>
    /// <returns>The overlapping pairs; empty when the sections do not conflict.</returns>
    public static IReadOnlyList<MeetingPair> OverlappingMeetings(Section a, Section b)
    {
        var pairs = new List<MeetingPair>();
        foreach (var ma in a.Schedule)
            foreach (var mb in b.Schedule)
                if (MeetingsOverlap(ma, mb)) pairs.Add(new MeetingPair(ma, mb));
        return pairs;
    }
}

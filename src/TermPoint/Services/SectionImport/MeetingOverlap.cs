namespace TermPoint.Services.SectionImport;

/// <summary>
/// Decides whether two meetings of one section overlap in a way worth a warning (design D11): they
/// are on the same day, their times overlap, <b>and</b> they can meet in the same week. Odd-week and
/// even-week meetings at the same time are legitimate and never overlap.
/// </summary>
internal static class MeetingOverlap
{
    /// <summary>
    /// True when two stored frequencies share at least one week. Weekly (null or empty) shares with
    /// everything; odd and even share none with each other; odd or even and a week list share when the
    /// list has a week of that parity; two week lists share when they intersect.
    /// </summary>
    /// <param name="a">The first stored frequency: null, <c>odd</c>, <c>even</c>, or a week list like <c>1,6,7</c>.</param>
    /// <param name="b">The second stored frequency.</param>
    /// <returns>True when some week has both meetings.</returns>
    public static bool ShareAWeek(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return true;

        var parityA = ParityOf(a);
        var parityB = ParityOf(b);

        if (parityA is not null && parityB is not null)
            return parityA == parityB;
        if (parityA is not null)
            return Weeks(b).Any(w => w % 2 == parityA);
        if (parityB is not null)
            return Weeks(a).Any(w => w % 2 == parityB);

        return Weeks(a).Intersect(Weeks(b)).Any();
    }

    /// <summary>
    /// True when two meetings overlap in time on the same day and can meet in the same week.
    /// Two meetings that merely touch (one ends when the other begins) do not overlap.
    /// </summary>
    /// <param name="a">The first meeting.</param>
    /// <param name="b">The second meeting.</param>
    /// <returns>True when the meetings conflict.</returns>
    public static bool Conflict(MeetingDraft a, MeetingDraft b) =>
        a.Day == b.Day
        && a.StartMinutes < b.EndMinutes
        && b.StartMinutes < a.EndMinutes
        && ShareAWeek(a.Frequency, b.Frequency);

    /// <summary>
    /// Finds every pair of conflicting meetings in a section.
    /// </summary>
    /// <param name="meetings">The section's meetings.</param>
    /// <returns>Each conflicting pair once.</returns>
    public static IReadOnlyList<(MeetingDraft First, MeetingDraft Second)> ConflictingPairs(
        IReadOnlyList<MeetingDraft> meetings)
    {
        var pairs = new List<(MeetingDraft, MeetingDraft)>();
        for (var i = 0; i < meetings.Count; i++)
            for (var j = i + 1; j < meetings.Count; j++)
                if (Conflict(meetings[i], meetings[j]))
                    pairs.Add((meetings[i], meetings[j]));
        return pairs;
    }

    /// <summary>The parity a frequency stands for: 1 for odd weeks, 0 for even weeks, null for a week list.</summary>
    /// <param name="frequency">A non-empty stored frequency.</param>
    private static int? ParityOf(string frequency) => frequency switch
    {
        "odd" => 1,
        "even" => 0,
        _ => null,
    };

    /// <summary>The week numbers of a stored week list such as <c>1,6,7</c>.</summary>
    /// <param name="weekList">A stored week list.</param>
    private static IEnumerable<int> Weeks(string weekList) =>
        weekList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => int.TryParse(w, out var n) ? n : 0)
            .Where(n => n > 0);
}

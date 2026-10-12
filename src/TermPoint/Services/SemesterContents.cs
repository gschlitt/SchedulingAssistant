namespace TermPoint.Services;

/// <summary>
/// Per-kind counts of everything a semester has attached to it (spec item 8). The six kinds are
/// exactly the six tables tied to a semester by <c>semester_id</c>: sections, meetings, releases,
/// instructor commitments, scheduling notes and program watches. A semester is "empty" only when
/// all six counts are zero. Produced by <see cref="SemesterContentsService.CountContents"/>.
/// </summary>
/// <param name="Sections">Number of sections in the semester.</param>
/// <param name="Meetings">Number of meetings (non-section calendar entries) in the semester.</param>
/// <param name="Releases">Number of workload releases in the semester.</param>
/// <param name="Commitments">Number of instructor scheduled commitments in the semester.</param>
/// <param name="SchedulingNotes">Number of instructor scheduling notes in the semester.</param>
/// <param name="ProgramWatches">Number of access (program) watches in the semester.</param>
public sealed record SemesterContents(
    int Sections,
    int Meetings,
    int Releases,
    int Commitments,
    int SchedulingNotes,
    int ProgramWatches)
{
    /// <summary>Total number of rows across all six kinds.</summary>
    public int Total => Sections + Meetings + Releases + Commitments + SchedulingNotes + ProgramWatches;

    /// <summary>True when the semester has nothing attached to it, so it may be deleted.</summary>
    public bool IsEmpty => Total == 0;

    /// <summary>
    /// Describes the contents as a human-readable list for dialogs and refusal messages, naming only
    /// the kinds that are present, with singular or plural wording as appropriate. The words are the
    /// ones users see in the app: sections, meetings, releases, scheduled commitments, scheduling
    /// notes and program watches. Example: <c>"12 sections, 3 meetings, 1 release"</c>.
    /// </summary>
    /// <returns>The list, or <c>"nothing"</c> when <see cref="IsEmpty"/>.</returns>
    public string Describe()
    {
        var parts = new List<string>();
        AddPart(parts, Sections, "section", "sections");
        AddPart(parts, Meetings, "meeting", "meetings");
        AddPart(parts, Releases, "release", "releases");
        AddPart(parts, Commitments, "scheduled commitment", "scheduled commitments");
        AddPart(parts, SchedulingNotes, "scheduling note", "scheduling notes");
        AddPart(parts, ProgramWatches, "program watch", "program watches");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>Appends "<c>{count} {noun}</c>" to <paramref name="parts"/> when <paramref name="count"/> is non-zero.</summary>
    /// <param name="parts">The list being built.</param>
    /// <param name="count">How many rows of this kind there are.</param>
    /// <param name="singular">The noun used when <paramref name="count"/> is 1.</param>
    /// <param name="plural">The noun used for any other non-zero count.</param>
    private static void AddPart(List<string> parts, int count, string singular, string plural)
    {
        if (count > 0)
            parts.Add($"{count} {(count == 1 ? singular : plural)}");
    }
}

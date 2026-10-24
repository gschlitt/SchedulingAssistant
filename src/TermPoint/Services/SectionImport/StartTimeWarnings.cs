namespace TermPoint.Services.SectionImport;

/// <summary>
/// The legal-start-time warnings (design P1): a meeting whose Begin is not a legal start time for its
/// length. The rule mirrors the meeting editor's start-time list: when the academic year has start
/// times for the meeting's length, Begin is checked against those; when it has none for that length,
/// against every start time it has. Meetings are grouped by (Begin, length), so a file with 30 meetings
/// at one illegal time gets one warning. When the academic year has no start times at all, nothing is
/// checked per meeting and one file-wide warning says so.
/// </summary>
internal sealed class StartTimeWarnings
{
    /// <summary>How far a block length (hours) may be from a meeting's length and still count as the same length.</summary>
    private const double BlockLengthTolerance = 0.001;

    private readonly ImportSetupSnapshot _snapshot;

    /// <summary>The start times of every block length together.</summary>
    private readonly HashSet<int> _allStartTimes;

    /// <summary>Creates the check over a snapshot's legal start times.</summary>
    /// <param name="snapshot">Supplies the academic year and its legal start times.</param>
    public StartTimeWarnings(ImportSetupSnapshot snapshot)
    {
        _snapshot = snapshot;
        _allStartTimes = snapshot.LegalStartTimes.SelectMany(l => l.StartTimes).ToHashSet();
    }

    /// <summary>
    /// Produces the legal-start-time warnings for the meetings of the sections to be added.
    /// </summary>
    /// <param name="sections">The sections the import will add.</param>
    /// <returns>
    /// Nothing when there are no meetings; a single file-wide warning when the year has no start times;
    /// otherwise one warning per distinct (Begin, length) that is not legal.
    /// </returns>
    public IEnumerable<ImportWarning> Collect(IEnumerable<SectionDraft> sections)
    {
        var meetings = sections.SelectMany(s => s.Meetings).ToList();
        if (meetings.Count == 0)
            return Array.Empty<ImportWarning>();

        if (_allStartTimes.Count == 0)
            return new[]
            {
                new ImportWarning(ImportWarningKinds.NoLegalStartTimes, Array.Empty<int>(),
                    $"{_snapshot.AcademicYear.Name} has no legal start times set up, so start times weren't checked. " +
                    $"You can add them in {ImportText.SchedulingSettingsPlace}."),
            };

        var illegal = new RowGrouping<(int Start, int Duration)>();
        foreach (var meeting in meetings.Where(m => !IsLegal(m)))
            illegal.Add((meeting.StartMinutes, meeting.DurationMinutes), meeting.RowNumber);

        return illegal.Entries.Select(Describe).ToList();
    }

    /// <summary>
    /// True when the meeting's Begin is in the list for its length, or, when that length has no list of
    /// its own, in the union of all lists.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    /// <returns>True when Begin is a legal start time.</returns>
    private bool IsLegal(MeetingDraft meeting)
    {
        var own = OwnStartTimes(meeting.DurationMinutes);
        return own is not null ? own.Contains(meeting.StartMinutes) : _allStartTimes.Contains(meeting.StartMinutes);
    }

    /// <summary>
    /// The start times configured for a meeting length, or null when that length has none (no entry, or
    /// an entry with an empty list).
    /// </summary>
    /// <param name="durationMinutes">The meeting's length in minutes.</param>
    /// <returns>The start times, or null.</returns>
    private List<int>? OwnStartTimes(int durationMinutes)
    {
        var hours = durationMinutes / 60.0;
        return _snapshot.LegalStartTimes
            .FirstOrDefault(l => Math.Abs(l.BlockLength - hours) < BlockLengthTolerance && l.StartTimes.Count > 0)
            ?.StartTimes;
    }

    /// <summary>Writes the warning for one group of meetings that share an illegal Begin and length.</summary>
    /// <param name="group">The group: its key, its meeting count and its rows.</param>
    /// <returns>The warning.</returns>
    private ImportWarning Describe(RowGrouping<(int Start, int Duration)>.Entry group)
    {
        var (start, duration) = group.Key;
        var forLength = OwnStartTimes(duration) is not null ? $" for {ImportText.LengthAdjective(duration)} meetings" : string.Empty;
        var message =
            $"{ImportText.Time(start)} isn't a legal start time{forLength} " +
            $"({ImportText.Counted(group.Count, "meeting", "meetings")}: {ImportRowList.Format(group.Rows)}). " +
            $"To make it one, add it in {ImportText.SchedulingSettingsPlace}, and check again.";
        return new ImportWarning(ImportWarningKinds.LegalStartTime, group.Rows, message);
    }
}

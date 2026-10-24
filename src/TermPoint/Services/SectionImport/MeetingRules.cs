namespace TermPoint.Services.SectionImport;

/// <summary>
/// Applies the meeting-time rules of the design (Phase 4, "Meeting-time rules") to one file row and
/// turns a valid row into one <see cref="MeetingDraft"/> per day. Every fault found on the row is
/// reported, not just the first. A row whose meeting cells are all blank is a section with no meeting
/// time (online or to be announced): fine, and it makes no meeting.
/// <para>
/// This class checks what makes a meeting impossible to save (errors). What only deserves a second
/// look (legal start times, overlaps) is a warning, found later by <see cref="ImportWarningCollector"/>.
/// </para>
/// </summary>
internal sealed class MeetingRules
{
    /// <summary>The day numbers of Saturday and Sunday.</summary>
    private const int Saturday = 6, Sunday = 7;

    private readonly ImportSetupSnapshot _snapshot;
    private readonly List<ImportProblem> _problems;

    /// <summary>Creates the rules over a snapshot, reporting into a problem list.</summary>
    /// <param name="snapshot">Supplies whether Saturday and Sunday are turned on.</param>
    /// <param name="problems">Where problems are added.</param>
    public MeetingRules(ImportSetupSnapshot snapshot, List<ImportProblem> problems)
    {
        _snapshot = snapshot;
        _problems = problems;
    }

    /// <summary>
    /// Checks one row's Meeting Days, Begin, End and Frequency and expands it into meetings.
    /// </summary>
    /// <param name="row">The row, with its raw meeting text and resolved meeting type and room.</param>
    /// <returns>
    /// One meeting per day, in the order the days are listed; empty when the row has no meeting
    /// time or has any problem (the problems are then in the problem list).
    /// </returns>
    public IReadOnlyList<MeetingDraft> Expand(MeetingRowDraft row)
    {
        var hasDays = row.RawDays.Length > 0;
        var hasBegin = row.RawBegin.Length > 0;
        var hasEnd = row.RawEnd.Length > 0;
        if (!hasDays && !hasBegin && !hasEnd)
            return Array.Empty<MeetingDraft>();

        var problemsBefore = _problems.Count;
        CheckAllThreeFilled(row, hasDays, hasBegin, hasEnd);

        var days = hasDays ? ParseDays(row) : null;
        var begin = hasBegin ? ParseTime(row, SectionImportColumns.Begin, row.RawBegin) : null;
        var end = hasEnd ? ParseTime(row, SectionImportColumns.End, row.RawEnd) : null;

        if (begin is { } b && b < MeetingTimeLimits.MinStartMinutes)
            Add(row, SectionImportColumns.Begin,
                $"Begin {ImportText.Time(b)} is before {ImportText.Time(MeetingTimeLimits.MinStartMinutes)}, the earliest time TermPoint shows.");
        if (end is { } e && e > MeetingTimeLimits.MaxEndMinutes)
            Add(row, SectionImportColumns.End,
                $"End {ImportText.Time(e)} is after {ImportText.Time(MeetingTimeLimits.MaxEndMinutes)}, the latest time TermPoint shows.");
        if (begin is { } start && end is { } finish && finish <= start)
            Add(row, SectionImportColumns.End, "End must be after Begin.");

        var frequency = ScheduleText.ParseFrequency(row.RawFrequency);
        if (!frequency.IsValid)
            Add(row, SectionImportColumns.Frequency, frequency.Error!);

        if (_problems.Count > problemsBefore || days is null || begin is null || end is null)
            return Array.Empty<MeetingDraft>();

        return days
            .Select(day => new MeetingDraft(row.RowNumber, day, begin.Value, end.Value - begin.Value,
                frequency.Value, row.MeetingType, row.Room))
            .ToList();
    }

    /// <summary>
    /// Reports a row whose Meeting Days, Begin and End are not all filled or all blank, naming what is
    /// missing. The problem is placed on the first missing column.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="hasDays">Whether Meeting Days is filled.</param>
    /// <param name="hasBegin">Whether Begin is filled.</param>
    /// <param name="hasEnd">Whether End is filled.</param>
    private void CheckAllThreeFilled(MeetingRowDraft row, bool hasDays, bool hasBegin, bool hasEnd)
    {
        var missing = new List<string>();
        if (!hasDays) missing.Add(SectionImportColumns.MeetingDays);
        if (!hasBegin) missing.Add(SectionImportColumns.Begin);
        if (!hasEnd) missing.Add(SectionImportColumns.End);
        if (missing.Count == 0)
            return;

        Add(row, missing[0],
            $"{ImportText.JoinWithAnd(missing)} {(missing.Count == 1 ? "is" : "are")} empty; fill in all three of " +
            $"{SectionImportColumns.MeetingDays}, {SectionImportColumns.Begin} and {SectionImportColumns.End}, or leave all three blank.");
    }

    /// <summary>
    /// Parses the Meeting Days cell and checks that Saturday and Sunday are turned on when named.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The day numbers, or null after reporting a problem (a day that is turned off is a problem too).</returns>
    private IReadOnlyList<int>? ParseDays(MeetingRowDraft row)
    {
        var parsed = ScheduleText.ParseDays(row.RawDays);
        if (!parsed.IsValid)
        {
            Add(row, SectionImportColumns.MeetingDays, parsed.Error!);
            return null;
        }

        var allowed = true;
        foreach (var day in parsed.Value)
        {
            var turnedOff = (day == Saturday && !_snapshot.IncludeSaturday) || (day == Sunday && !_snapshot.IncludeSunday);
            if (!turnedOff)
                continue;
            Add(row, SectionImportColumns.MeetingDays,
                $"{ImportText.DayName(day)} isn't turned on in TermPoint. " +
                $"Turn it on in {ImportText.SchedulingSettingsPlace}, then check again.");
            allowed = false;
        }
        return allowed ? parsed.Value : null;
    }

    /// <summary>Parses a time cell, reporting a problem in <paramref name="column"/> when it is not a time.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">Begin or End.</param>
    /// <param name="text">The cell text (not blank).</param>
    /// <returns>Minutes from midnight, or null after reporting a problem.</returns>
    private int? ParseTime(MeetingRowDraft row, string column, string text)
    {
        var parsed = ScheduleText.ParseTime(text);
        if (parsed.IsValid)
            return parsed.Value;
        Add(row, column, parsed.Error!);
        return null;
    }

    /// <summary>Records a problem on the row.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The template column.</param>
    /// <param name="message">The plain-English message.</param>
    private void Add(MeetingRowDraft row, string column, string message) =>
        _problems.Add(new ImportProblem(row.RowNumber, column, message));
}

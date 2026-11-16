using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>Why <see cref="MeetingMover.Move"/> declined to produce a new schedule.</summary>
public enum MeetingMoveRefusal
{
    /// <summary>The move was accepted.</summary>
    None,
    /// <summary>Nothing would differ from the current schedule.</summary>
    NoChange,
    /// <summary>A moved meeting would start before midnight or end after the end of the day.</summary>
    OutOfDay,
    /// <summary>A moved meeting would overlap another meeting of the same section that is not moving.</summary>
    OverlapsOwnMeeting,
}

/// <summary>
/// The outcome of <see cref="MeetingMover.Move"/>.
/// </summary>
/// <param name="Schedule">
/// The new schedule (every meeting a fresh clone, in the input order), or null when the move was refused.
/// </param>
/// <param name="Refusal">Why the move was refused; <see cref="MeetingMoveRefusal.None"/> when it was accepted.</param>
/// <param name="ConflictDay">With <see cref="MeetingMoveRefusal.OverlapsOwnMeeting"/>: the day of the first meeting in the way.</param>
/// <param name="ConflictStartMinutes">With <see cref="MeetingMoveRefusal.OverlapsOwnMeeting"/>: that meeting's start, in minutes from midnight.</param>
public sealed record MeetingMoveResult(
    List<SectionDaySchedule>? Schedule,
    MeetingMoveRefusal Refusal,
    int ConflictDay = 0,
    int ConflictStartMinutes = 0)
{
    /// <summary>True when the move was accepted and <see cref="Schedule"/> is set.</summary>
    public bool IsOk => Schedule is not null;
}

/// <summary>
/// The pure rules for dragging a meeting in Schedule View: which meetings move together, how a dragged time
/// snaps, what the new schedule is, and when the move is refused. No UI and no repositories, so every rule is
/// unit-tested; the drag behaviour only feeds it numbers and shows its labels.
/// </summary>
public static class MeetingMover
{
    /// <summary>Step, in minutes, of a free (Shift) drag, and the fallback when no legal start times exist.</summary>
    public const int FreeStepMinutes = 5;

    /// <summary>Minutes in a day; a moved meeting must end by then.</summary>
    private const int MinutesPerDay = 24 * 60;

    /// <summary>Tolerance, in minutes, when matching a duration to a legal block length (hours as a double).</summary>
    private const double DurationToleranceMinutes = 0.5;

    /// <summary>Three-letter English day names; index 0 is unused (1 = Mon ... 7 = Sun).</summary>
    private static readonly string[] DayAbbreviations = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    /// <summary>
    /// Returns the indices of the meetings that move with the dragged one.
    /// </summary>
    /// <param name="schedule">The section's meetings.</param>
    /// <param name="meetingIndex">Index of the dragged meeting.</param>
    /// <param name="moveAlone">True to move only the dragged meeting.</param>
    /// <returns>
    /// Just <paramref name="meetingIndex"/> when moving alone; otherwise every index whose start equals the
    /// dragged meeting's start (the "group", the dragged one included), in schedule order.
    /// </returns>
    public static IReadOnlyList<int> MovingIndices(IReadOnlyList<SectionDaySchedule> schedule, int meetingIndex, bool moveAlone)
    {
        if (moveAlone) return [meetingIndex];
        int start = schedule[meetingIndex].StartMinutes;
        var result = new List<int>();
        for (int i = 0; i < schedule.Count; i++)
            if (schedule[i].StartMinutes == start) result.Add(i);
        return result;
    }

    /// <summary>
    /// Whether a drag of these meetings may change their day.
    /// <para>
    /// Only a single meeting may: moving one of two grouped meetings onto the other meeting's day could silently
    /// merge them (same day, same start), so a group drag changes the time only.
    /// </para>
    /// </summary>
    /// <param name="moving">The indices from <see cref="MovingIndices"/>.</param>
    /// <returns>True when exactly one meeting moves.</returns>
    public static bool CanChangeDay(IReadOnlyList<int> moving) => moving.Count == 1;

    /// <summary>
    /// Snaps a raw dragged start time.
    /// </summary>
    /// <param name="rawStartMinutes">The unsnapped start, in minutes from midnight.</param>
    /// <param name="durationMinutes">The meeting length in minutes.</param>
    /// <param name="legalStartTimes">The department legal start times, by block length (hours).</param>
    /// <param name="free">True (Shift held) to round to <see cref="FreeStepMinutes"/> instead of legal starts.</param>
    /// <returns>
    /// When free: the nearest multiple of <see cref="FreeStepMinutes"/>. Otherwise the nearest legal start for
    /// the block length matching <paramref name="durationMinutes"/>; if that length has none, the nearest of all
    /// legal starts of all lengths; if there are none at all, the 5-minute rounding. Ties go to the earlier time.
    /// </returns>
    public static int Snap(int rawStartMinutes, int durationMinutes, IReadOnlyList<LegalStartTime> legalStartTimes, bool free)
    {
        if (free) return RoundToStep(rawStartMinutes);

        var forLength = legalStartTimes
            .Where(l => Math.Abs(l.BlockLength * 60 - durationMinutes) <= DurationToleranceMinutes)
            .SelectMany(l => l.StartTimes)
            .ToList();
        var candidates = forLength.Count > 0 ? forLength : legalStartTimes.SelectMany(l => l.StartTimes).ToList();
        if (candidates.Count == 0) return RoundToStep(rawStartMinutes);

        return candidates
            .OrderBy(c => Math.Abs(c - rawStartMinutes))
            .ThenBy(c => c)
            .First();
    }

    /// <summary>Rounds to the nearest multiple of <see cref="FreeStepMinutes"/>; halves round up.</summary>
    /// <param name="minutes">Minutes from midnight.</param>
    /// <returns>The rounded minutes.</returns>
    private static int RoundToStep(int minutes) =>
        (int)Math.Floor(minutes / (double)FreeStepMinutes + 0.5) * FreeStepMinutes;

    /// <summary>
    /// Computes the schedule that results from dropping a meeting (and its group) at a new day and start.
    /// Never mutates the input.
    /// </summary>
    /// <param name="schedule">The section current meetings; not modified.</param>
    /// <param name="meetingIndex">Index of the dragged meeting.</param>
    /// <param name="newDay">The dropped-on day (treated as the original day when a group moves).</param>
    /// <param name="newStartMinutes">The snapped start, in minutes from midnight.</param>
    /// <param name="moveAlone">True to move only the dragged meeting.</param>
    /// <returns>
    /// A result holding a new list in which every meeting is a <see cref="SectionDaySchedule.Clone"/> (the input
    /// order, nothing dropped), or a refusal: NoChange, OutOfDay, or OverlapsOwnMeeting (with the first
    /// conflicting meeting day and start). Touching end-to-start is not an overlap; meetings whose frequencies
    /// do not overlap (odd vs even) do not conflict.
    /// </returns>
    public static MeetingMoveResult Move(IReadOnlyList<SectionDaySchedule> schedule, int meetingIndex, int newDay, int newStartMinutes, bool moveAlone)
    {
        var moving = MovingIndices(schedule, meetingIndex, moveAlone);
        bool single = CanChangeDay(moving);
        if (!single) newDay = schedule[meetingIndex].Day;

        var result = schedule.Select(m => m.Clone()).ToList();
        foreach (int i in moving)
        {
            result[i].StartMinutes = newStartMinutes;
            if (single) result[i].Day = newDay;
        }

        bool changed = moving.Any(i => result[i].Day != schedule[i].Day || result[i].StartMinutes != schedule[i].StartMinutes);
        if (!changed) return new MeetingMoveResult(null, MeetingMoveRefusal.NoChange);

        if (moving.Any(i => result[i].StartMinutes < 0 || result[i].EndMinutes > MinutesPerDay))
            return new MeetingMoveResult(null, MeetingMoveRefusal.OutOfDay);

        var movingSet = moving.ToHashSet();
        foreach (int i in moving)
        {
            for (int j = 0; j < result.Count; j++)
            {
                if (movingSet.Contains(j)) continue;
                var a = result[i];
                var b = result[j];
                if (a.Day == b.Day
                    && a.StartMinutes < b.EndMinutes && b.StartMinutes < a.EndMinutes
                    && SectionDaySchedule.FrequenciesOverlap(a.Frequency, b.Frequency))
                    return new MeetingMoveResult(null, MeetingMoveRefusal.OverlapsOwnMeeting, b.Day, b.StartMinutes);
            }
        }

        return new MeetingMoveResult(result, MeetingMoveRefusal.None);
    }

    /// <summary>
    /// The drag label for the time being dropped on. Approved wording (Greg, 2026-11-16).
    /// </summary>
    /// <param name="days">The days the moved meetings will be on.</param>
    /// <param name="startMinutes">Start, in minutes from midnight.</param>
    /// <param name="durationMinutes">Length in minutes.</param>
    /// <returns>E.g. "Tue, Thu 11:30-13:00" (distinct days ascending, 24-hour H:mm, plain hyphen).</returns>
    public static string TimeLabel(IEnumerable<int> days, int startMinutes, int durationMinutes) =>
        $"{string.Join(", ", days.Distinct().OrderBy(d => d).Select(DayName))} {FormatTime(startMinutes)}-{FormatTime(startMinutes + durationMinutes)}";

    /// <summary>
    /// The drag label when the drop would overlap another meeting of the same section. Approved wording
    /// (Greg, 2026-11-16).
    /// </summary>
    /// <param name="day">Day of the meeting in the way.</param>
    /// <param name="startMinutes">Its start, in minutes from midnight.</param>
    /// <returns>E.g. "Overlaps its Wed 10:00 meeting".</returns>
    public static string OverlapLabel(int day, int startMinutes) =>
        $"Overlaps its {DayName(day)} {FormatTime(startMinutes)} meeting";

    /// <summary>Three-letter English name of a day number (1 = Mon ... 7 = Sun).</summary>
    /// <param name="day">The day number.</param>
    /// <returns>The name, or empty when out of range.</returns>
    private static string DayName(int day) => day >= 1 && day < DayAbbreviations.Length ? DayAbbreviations[day] : string.Empty;

    /// <summary>Formats minutes from midnight as H:mm (24-hour, no leading zero on the hour).</summary>
    /// <param name="minutes">Minutes from midnight.</param>
    /// <returns>The formatted time.</returns>
    private static string FormatTime(int minutes) => $"{minutes / 60}:{minutes % 60:D2}";
}

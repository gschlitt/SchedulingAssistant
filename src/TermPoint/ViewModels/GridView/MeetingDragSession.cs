using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.GridView;

/// <summary>One ghost shown while a meeting is dragged: where the meeting would sit if dropped now.</summary>
/// <param name="Day">Day number, 1 = Monday ... 7 = Sunday.</param>
/// <param name="StartMinutes">Start, in minutes from midnight.</param>
/// <param name="EndMinutes">End, in minutes from midnight.</param>
public sealed record DragGhost(int Day, int StartMinutes, int EndMinutes);

/// <summary>
/// What the drag shows and what a drop would do at the current pointer position.
/// </summary>
/// <param name="Ghosts">One ghost per meeting that would move.</param>
/// <param name="Label">The time label beside the pointer (approved wording, see <see cref="MeetingMover"/>).</param>
/// <param name="IsRefused">True when the drop is refused for a reason the user should see (overlap, out of the day): ghost and label use the refused colour.</param>
/// <param name="Result">The outcome of <see cref="MeetingMover.Move"/> for this position.</param>
public sealed record MeetingDragPreview(IReadOnlyList<DragGhost> Ghosts, string Label, bool IsRefused, MeetingMoveResult Result)
{
    /// <summary>True when letting go here would save a changed schedule (false for a refusal and for "no change").</summary>
    public bool CanDrop => Result.IsOk;
}

/// <summary>
/// One drag of a meeting in Schedule View: remembers the section's meetings as they were when the meeting was
/// lifted, and turns a pointer position (day, raw start time, modifier keys) into a <see cref="MeetingDragPreview"/>.
/// Pure: no UI, no repositories; the view feeds it numbers and draws what it returns.
/// </summary>
public sealed class MeetingDragSession
{
    /// <summary>Minutes in a day, the upper limit of a moved meeting's end.</summary>
    private const int MinutesPerDay = 24 * 60;

    private readonly List<SectionDaySchedule> _snapshot;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;

    /// <summary>Starts a drag.</summary>
    /// <param name="sectionId">The section being dragged.</param>
    /// <param name="meetingIndex">Index of the dragged meeting in <paramref name="schedule"/>.</param>
    /// <param name="schedule">The section's meetings now; copied with <see cref="SectionDaySchedule.Clone"/>, so later edits of this list do not affect the session.</param>
    /// <param name="legalStartTimes">The academic year's legal start times (may be empty).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="meetingIndex"/> is not an index of <paramref name="schedule"/>.</exception>
    public MeetingDragSession(string sectionId, int meetingIndex, IReadOnlyList<SectionDaySchedule> schedule,
        IReadOnlyList<LegalStartTime> legalStartTimes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(meetingIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(meetingIndex, schedule.Count);
        SectionId = sectionId;
        MeetingIndex = meetingIndex;
        _snapshot = schedule.Select(m => m.Clone()).ToList();
        _legalStartTimes = legalStartTimes;
    }

    /// <summary>The section being dragged.</summary>
    public string SectionId { get; }

    /// <summary>Index of the dragged meeting in the section's schedule.</summary>
    public int MeetingIndex { get; }

    /// <summary>Day of the dragged meeting when it was lifted.</summary>
    public int OriginalDay => _snapshot[MeetingIndex].Day;

    /// <summary>Start of the dragged meeting when it was lifted, in minutes from midnight.</summary>
    public int OriginalStartMinutes => _snapshot[MeetingIndex].StartMinutes;

    /// <summary>Length of the dragged meeting in minutes.</summary>
    public int DurationMinutes => _snapshot[MeetingIndex].DurationMinutes;

    /// <summary>
    /// Works out what a drop at this position would do.
    /// </summary>
    /// <param name="day">The day under the pointer (ignored for a group drag, which keeps each meeting's own day).</param>
    /// <param name="rawStartMinutes">The unsnapped start of the ghost's top edge, in minutes from midnight.</param>
    /// <param name="free">True (Shift held) to move in 5-minute steps instead of snapping to legal start times.</param>
    /// <param name="moveAlone">True to move only the dragged meeting; false to move every meeting that starts at the same time.</param>
    /// <param name="earliestStartMinutes">The earliest start the grid shows.</param>
    /// <param name="latestEndMinutes">The latest end the grid shows.</param>
    /// <returns>The ghosts, label, refusal state and move result for this position.</returns>
    public MeetingDragPreview Preview(int day, int rawStartMinutes, bool free, bool moveAlone,
        int earliestStartMinutes, int latestEndMinutes)
    {
        int duration = DurationMinutes;
        int lower = Math.Max(0, earliestStartMinutes);
        int upper = Math.Min(MinutesPerDay, latestEndMinutes) - duration;
        int clamped = upper < lower ? lower : Math.Clamp(rawStartMinutes, lower, upper);

        int snapped = MeetingMover.Snap(clamped, duration, _legalStartTimes, free);
        // The meeting's own start is a snap point too: a meeting at a time that is not a legal start can be dragged
        // back home, and letting go there saves nothing (design story 5). Ties go to the legal start.
        if (!free && Math.Abs(clamped - OriginalStartMinutes) < Math.Abs(clamped - snapped))
            snapped = OriginalStartMinutes;
        var result = MeetingMover.Move(_snapshot, MeetingIndex, day, snapped, moveAlone);

        var moving = MeetingMover.MovingIndices(_snapshot, MeetingIndex, moveAlone);
        bool canChangeDay = MeetingMover.CanChangeDay(moving);
        var ghosts = moving
            .Select(i => new DragGhost(canChangeDay ? day : _snapshot[i].Day, snapped, snapped + _snapshot[i].DurationMinutes))
            .ToList();

        bool overlaps = result.Refusal == MeetingMoveRefusal.OverlapsOwnMeeting;
        string label = overlaps
            ? MeetingMover.OverlapLabel(result.ConflictDay, result.ConflictStartMinutes)
            : MeetingMover.TimeLabel(ghosts.Select(g => g.Day), snapped, duration);
        bool refused = overlaps || result.Refusal == MeetingMoveRefusal.OutOfDay;

        return new MeetingDragPreview(ghosts, label, refused, result);
    }

    /// <summary>
    /// Whether a section's stored meetings are still exactly the ones this drag started from. A drop is only saved
    /// when this holds, so a change made elsewhere while the meeting was lifted is never overwritten.
    /// </summary>
    /// <param name="current">The meetings as stored now.</param>
    /// <returns>True when the count and every meeting's day, start, length and frequency are unchanged.</returns>
    public bool MatchesSnapshot(IReadOnlyList<SectionDaySchedule> current)
    {
        if (current.Count != _snapshot.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            var a = _snapshot[i];
            var b = current[i];
            if (a.Day != b.Day || a.StartMinutes != b.StartMinutes
                || a.DurationMinutes != b.DurationMinutes || a.Frequency != b.Frequency)
                return false;
        }
        return true;
    }
}

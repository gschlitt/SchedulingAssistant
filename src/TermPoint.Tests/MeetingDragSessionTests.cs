using System.Collections.Generic;
using System.Linq;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="MeetingDragSession"/>: what a pointer position turns into (snapping, Shift steps, clamping, day change,
/// refusals, group drags) and the snapshot that guards the drop.
/// </summary>
public sealed class MeetingDragSessionTests
{
    private const int Earliest = 8 * 60;
    private const int LatestEnd = 18 * 60;

    /// <summary>Legal 1.5-hour starts: 8:30, 11:30, 13:00.</summary>
    private static readonly List<LegalStartTime> Legal = [new() { BlockLength = 1.5, StartTimes = [510, 690, 780] }];

    private static SectionDaySchedule Meeting(int day, int start, int duration = 90, string? frequency = null) =>
        new() { Day = day, StartMinutes = start, DurationMinutes = duration, Frequency = frequency };

    /// <summary>A Tuesday/Thursday 10:00 section.</summary>
    private static List<SectionDaySchedule> TueThu() => [Meeting(2, 600), Meeting(4, 600)];

    /// <summary>A raw time snaps to the nearest legal start for the block length.</summary>
    [Fact]
    public void Preview_SnapsToLegalStart()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        var p = s.Preview(2, 660, free: false, moveAlone: true, Earliest, LatestEnd);
        var ghost = Assert.Single(p.Ghosts);
        Assert.Equal(new DragGhost(2, 690, 780), ghost);
        Assert.True(p.CanDrop);
        Assert.False(p.IsRefused);
        Assert.Equal("Tue 11:30-13:00", p.Label);
    }

    /// <summary>
    /// The meeting's own start (10:00, not a legal start here) is a snap point too, so dragging it back near home
    /// is "no change"; a tie between home and a legal start goes to the legal start.
    /// </summary>
    [Fact]
    public void Preview_OwnStartIsASnapPoint()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        var home = s.Preview(2, 620, free: false, moveAlone: true, Earliest, LatestEnd);
        Assert.Equal(600, Assert.Single(home.Ghosts).StartMinutes);
        Assert.Equal(MeetingMoveRefusal.NoChange, home.Result.Refusal);

        var tie = s.Preview(2, 645, free: false, moveAlone: true, Earliest, LatestEnd);
        Assert.Equal(690, Assert.Single(tie.Ghosts).StartMinutes);
    }

    /// <summary>With Shift (free) the time moves in 5-minute steps.</summary>
    [Fact]
    public void Preview_Free_UsesFiveMinuteSteps()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        var p = s.Preview(2, 642, free: true, moveAlone: true, Earliest, LatestEnd);
        Assert.Equal(640, Assert.Single(p.Ghosts).StartMinutes);
    }

    /// <summary>The raw time is clamped so the meeting stays inside the shown range.</summary>
    [Fact]
    public void Preview_ClampsToShownRange()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        var low = s.Preview(2, 0, free: true, moveAlone: true, Earliest, LatestEnd);
        var high = s.Preview(2, 5000, free: true, moveAlone: true, Earliest, LatestEnd);
        Assert.Equal(Earliest, Assert.Single(low.Ghosts).StartMinutes);
        Assert.Equal(LatestEnd - 90, Assert.Single(high.Ghosts).StartMinutes);
        Assert.Equal(LatestEnd, Assert.Single(high.Ghosts).EndMinutes);
    }

    /// <summary>A range too small for the meeting falls back to its lower bound.</summary>
    [Fact]
    public void Preview_RangeTooSmall_UsesLowerBound()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        var p = s.Preview(2, 700, free: true, moveAlone: true, 600, 650);
        Assert.Equal(600, Assert.Single(p.Ghosts).StartMinutes);
    }

    /// <summary>A single meeting may change day.</summary>
    [Fact]
    public void Preview_SingleMeeting_ChangesDay()
    {
        var s = new MeetingDragSession("s", 1, TueThu(), Legal);
        var p = s.Preview(5, 780, free: false, moveAlone: true, Earliest, LatestEnd);
        Assert.Equal(new DragGhost(5, 780, 870), Assert.Single(p.Ghosts));
        Assert.True(p.CanDrop);
        Assert.Equal("Fri 13:00-14:30", p.Label);
        Assert.Equal(5, p.Result.Schedule![1].Day);
        Assert.Equal(2, p.Result.Schedule[0].Day);
    }

    /// <summary>Landing on another meeting of the same section is refused with the overlap label.</summary>
    [Fact]
    public void Preview_OverlapsOwnMeeting_IsRefused()
    {
        var schedule = new List<SectionDaySchedule> { Meeting(1, 600), Meeting(3, 600) };
        var s = new MeetingDragSession("s", 0, schedule, Legal);
        var p = s.Preview(3, 600, free: true, moveAlone: true, Earliest, LatestEnd);
        Assert.True(p.IsRefused);
        Assert.False(p.CanDrop);
        Assert.Equal(MeetingMoveRefusal.OverlapsOwnMeeting, p.Result.Refusal);
        Assert.Equal("Overlaps its Wed 10:00 meeting", p.Label);
        Assert.Single(p.Ghosts);
    }

    /// <summary>Dropping where it started is "no change": not refused (no warning colour), nothing to save.</summary>
    [Fact]
    public void Preview_AtTheStart_IsNoChange()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        var p = s.Preview(2, 600, free: true, moveAlone: true, Earliest, LatestEnd);
        Assert.Equal(MeetingMoveRefusal.NoChange, p.Result.Refusal);
        Assert.False(p.IsRefused);
        Assert.False(p.CanDrop);
    }

    /// <summary>A group drag shows one ghost per same-start meeting on its own day and ignores the pointer's day.</summary>
    [Fact]
    public void Preview_Group_OneGhostPerMeeting_IgnoresDay()
    {
        var schedule = new List<SectionDaySchedule> { Meeting(2, 600), Meeting(4, 600), Meeting(5, 510) };
        var s = new MeetingDragSession("s", 0, schedule, Legal);
        var p = s.Preview(5, 690, free: false, moveAlone: false, Earliest, LatestEnd);
        Assert.Equal([new DragGhost(2, 690, 780), new DragGhost(4, 690, 780)], p.Ghosts);
        Assert.Equal("Tue, Thu 11:30-13:00", p.Label);
        Assert.True(p.CanDrop);
    }

    /// <summary>The session exposes the dragged meeting's original place.</summary>
    [Fact]
    public void Properties_DescribeTheDraggedMeeting()
    {
        var s = new MeetingDragSession("sec", 1, TueThu(), Legal);
        Assert.Equal("sec", s.SectionId);
        Assert.Equal(1, s.MeetingIndex);
        Assert.Equal(4, s.OriginalDay);
        Assert.Equal(600, s.OriginalStartMinutes);
        Assert.Equal(90, s.DurationMinutes);
    }

    /// <summary>MatchesSnapshot compares count, day, start, length and frequency.</summary>
    [Fact]
    public void MatchesSnapshot_DetectsChanges()
    {
        var s = new MeetingDragSession("s", 0, TueThu(), Legal);
        Assert.True(s.MatchesSnapshot(TueThu()));

        var moved = TueThu(); moved[1].StartMinutes = 610;
        Assert.False(s.MatchesSnapshot(moved));
        var day = TueThu(); day[0].Day = 3;
        Assert.False(s.MatchesSnapshot(day));
        var longer = TueThu(); longer[0].DurationMinutes = 120;
        Assert.False(s.MatchesSnapshot(longer));
        var freq = TueThu(); freq[0].Frequency = "odd";
        Assert.False(s.MatchesSnapshot(freq));
        Assert.False(s.MatchesSnapshot(TueThu().Take(1).ToList()));
    }

    /// <summary>The session keeps its own copy: editing the list it was built from changes nothing.</summary>
    [Fact]
    public void Snapshot_IsNotAffectedByLaterEdits()
    {
        var live = TueThu();
        var s = new MeetingDragSession("s", 0, live, Legal);

        live[0].StartMinutes = 900;
        live.Add(Meeting(5, 510));

        Assert.Equal(600, s.OriginalStartMinutes);
        Assert.False(s.MatchesSnapshot(live));
        Assert.True(s.MatchesSnapshot(TueThu()));
    }
}

using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="SectionEditChanges"/> and <see cref="HistoryText.EditLabel"/>: a section card's Apply is labelled
/// by what it changed.
/// </summary>
public sealed class SectionEditChangesTests
{
    /// <summary>A section with one meeting, one instructor and one tag.</summary>
    private static Section Sample() => new()
    {
        Id = "s1", CourseId = "c1", SectionCode = "A", Notes = "", Capacity = 30,
        Schedule = { new SectionDaySchedule { Day = 2, StartMinutes = 600, DurationMinutes = 80, RoomId = "r1" } },
        InstructorAssignments = { new InstructorAssignment { InstructorId = "i1", Workload = 3 } },
        TagIds = { "t1" },
    };

    /// <summary>A copy of the sample, changed by <paramref name="change"/>.</summary>
    private static Section Changed(Action<Section> change)
    {
        var s = JsonHelpersRoundTrip(Sample());
        change(s);
        return s;
    }

    /// <summary>A deep copy through the stored JSON, as the repository would give it.</summary>
    private static Section JsonHelpersRoundTrip(Section s)
        => TermPoint.Data.JsonHelpers.Deserialize<Section>(TermPoint.Data.JsonHelpers.Serialize(s));

    /// <summary>An Apply that changed nothing lists no areas and keeps the plain label.</summary>
    [Fact]
    public void NothingChanged_PlainEditLabel()
    {
        var areas = SectionEditChanges.Describe(Sample(), Changed(_ => { }));
        Assert.Empty(areas);
        Assert.Equal("Edit HIST 101 A", HistoryText.EditLabel("HIST 101 A", areas));
    }

    /// <summary>One area gives the right-click menu's label form.</summary>
    [Fact]
    public void OneArea_ChangeLabel()
    {
        var areas = SectionEditChanges.Describe(Sample(), Changed(s => s.InstructorAssignments[0].InstructorId = "i2"));
        Assert.Equal("Change instructors of HIST 101 A", HistoryText.EditLabel("HIST 101 A", areas));
    }

    /// <summary>Several areas are listed in the card's order.</summary>
    [Fact]
    public void SeveralAreas_ListedInCardOrder()
    {
        var areas = SectionEditChanges.Describe(Sample(), Changed(s =>
        {
            s.Notes = "x";
            s.Schedule[0].StartMinutes = 690;
            s.InstructorAssignments[0].Workload = 1.5m;
        }));
        Assert.Equal("Edit HIST 101 A: instructors, meeting times, notes", HistoryText.EditLabel("HIST 101 A", areas));
    }

    /// <summary>Each meeting field maps to its area; adding a meeting is "meetings".</summary>
    [Fact]
    public void Meetings_MapToTimesRoomOrMeetings()
    {
        Assert.Equal(new[] { HistoryText.AreaRoom },
            SectionEditChanges.Describe(Sample(), Changed(s => s.Schedule[0].RoomId = "r2")));
        Assert.Equal(new[] { HistoryText.AreaMeetingTimes },
            SectionEditChanges.Describe(Sample(), Changed(s => s.Schedule[0].DurationMinutes = 50)));
        Assert.Equal(new[] { HistoryText.AreaMeetings },
            SectionEditChanges.Describe(Sample(), Changed(s => s.Schedule.Add(new SectionDaySchedule { Day = 4, StartMinutes = 600, DurationMinutes = 80 }))));
    }

    /// <summary>The single-value fields each give their own area.</summary>
    [Fact]
    public void SingleFields_EachTheirOwnArea()
    {
        Assert.Equal(new[] { HistoryText.AreaCapacity }, SectionEditChanges.Describe(Sample(), Changed(s => s.Capacity = null)));
        Assert.Equal(new[] { HistoryText.AreaTags }, SectionEditChanges.Describe(Sample(), Changed(s => s.TagIds.Clear())));
        Assert.Equal(new[] { HistoryText.AreaFlag }, SectionEditChanges.Describe(Sample(), Changed(s => s.Flag = SectionFlag.Red)));
        Assert.Equal(new[] { HistoryText.AreaSectionCode }, SectionEditChanges.Describe(Sample(), Changed(s => s.SectionCode = "B")));
        Assert.Equal(new[] { HistoryText.AreaCampus }, SectionEditChanges.Describe(Sample(), Changed(s => s.CampusId = "x")));
    }
}

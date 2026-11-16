using System.Reflection;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The pure rules for dragging a meeting (<see cref="MeetingMover"/>), <see cref="SectionDaySchedule.Clone"/>,
/// and the Schedule View context menu picking the exact meeting by its index.
/// </summary>
public sealed class MeetingMoverTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Releases the temp database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>Builds a meeting with the given day, start, duration and optional frequency and room.</summary>
    private static SectionDaySchedule M(int day, int start, int duration = 90, string? freq = null, string? room = null) =>
        new() { Day = day, StartMinutes = start, DurationMinutes = duration, Frequency = freq, RoomId = room,
                MeetingTypeId = "mt-" + day, RoomTypeId = "rt-" + day, ExtensionData = FutureFields.Make("m" + day + "-" + start) };

    /// <summary>Tue/Thu 10:00 group.</summary>
    private static List<SectionDaySchedule> TueThu() => [M(2, 600), M(4, 600)];

    // -- Grouping ---------------------------------------------------------------

    /// <summary>A group of Tue/Thu same-start meetings moves together, time only, even when a new day is given.</summary>
    [Fact]
    public void Group_MovesBoth_TimeOnly_EvenIfNewDayDiffers()
    {
        var input = TueThu();
        var r = MeetingMover.Move(input, 0, newDay: 5, newStartMinutes: 690, moveAlone: false);

        Assert.True(r.IsOk);
        Assert.Equal([2, 4], r.Schedule!.Select(m => m.Day));
        Assert.All(r.Schedule!, m => Assert.Equal(690, m.StartMinutes));
        Assert.False(MeetingMover.CanChangeDay(MeetingMover.MovingIndices(input, 0, false)));
    }

    /// <summary>moveAlone moves just the dragged meeting and may change its day.</summary>
    [Fact]
    public void MoveAlone_MovesOne_AndCanChangeDay()
    {
        var r = MeetingMover.Move(TueThu(), 1, newDay: 5, newStartMinutes: 690, moveAlone: true);

        Assert.True(r.IsOk);
        Assert.Equal((2, 600), (r.Schedule![0].Day, r.Schedule![0].StartMinutes));
        Assert.Equal((5, 690), (r.Schedule![1].Day, r.Schedule![1].StartMinutes));
    }

    /// <summary>A section whose meetings all start differently moves one, and may change day.</summary>
    [Fact]
    public void AllDifferentStarts_MovesOne_CanChangeDay()
    {
        var input = new List<SectionDaySchedule> { M(1, 480), M(3, 540), M(5, 600) };
        var moving = MeetingMover.MovingIndices(input, 1, false);
        Assert.Equal([1], moving);
        Assert.True(MeetingMover.CanChangeDay(moving));

        var r = MeetingMover.Move(input, 1, 4, 780, false);
        Assert.True(r.IsOk);
        Assert.Equal((4, 780), (r.Schedule![1].Day, r.Schedule![1].StartMinutes));
        Assert.Equal((1, 480), (r.Schedule![0].Day, r.Schedule![0].StartMinutes));
    }

    /// <summary>Odd/even twins on the same day and start move together and do not conflict with each other.</summary>
    [Fact]
    public void OddEvenTwins_MoveTogether_NotOverlapping()
    {
        var input = new List<SectionDaySchedule> { M(2, 600, freq: "odd"), M(2, 600, freq: "even") };
        var r = MeetingMover.Move(input, 1, 2, 660, false);

        Assert.True(r.IsOk);
        Assert.All(r.Schedule!, m => Assert.Equal(660, m.StartMinutes));
        Assert.Equal(["odd", "even"], r.Schedule!.Select(m => m.Frequency));
    }

    // -- Refusals ---------------------------------------------------------------

    /// <summary>Landing on a non-moving meeting of the same section is refused, naming it.</summary>
    [Fact]
    public void OverlapsOwnMeeting_ReportsConflict()
    {
        var input = new List<SectionDaySchedule> { M(1, 480), M(3, 600) };
        var r = MeetingMover.Move(input, 0, 3, 630, moveAlone: true);

        Assert.False(r.IsOk);
        Assert.Null(r.Schedule);
        Assert.Equal(MeetingMoveRefusal.OverlapsOwnMeeting, r.Refusal);
        Assert.Equal((3, 600), (r.ConflictDay, r.ConflictStartMinutes));
    }

    /// <summary>Frequencies that do not overlap (odd vs even) may share time.</summary>
    [Fact]
    public void DisjointFrequencies_Allowed()
    {
        var input = new List<SectionDaySchedule> { M(1, 480, freq: "odd"), M(3, 600, freq: "even") };
        Assert.True(MeetingMover.Move(input, 0, 3, 630, true).IsOk);
    }

    /// <summary>Touching end-to-start is not an overlap.</summary>
    [Fact]
    public void TouchingEndToStart_Allowed()
    {
        var input = new List<SectionDaySchedule> { M(1, 480), M(3, 600) };
        Assert.True(MeetingMover.Move(input, 0, 3, 510, true).IsOk);   // ends 600, other starts 600
        Assert.True(MeetingMover.Move(input, 0, 3, 690, true).IsOk);   // other ends 690, this starts 690
    }

    /// <summary>A drop that changes nothing is NoChange.</summary>
    [Fact]
    public void NoChange_Refused()
    {
        var r = MeetingMover.Move(TueThu(), 0, 2, 600, false);
        Assert.Equal(MeetingMoveRefusal.NoChange, r.Refusal);
        Assert.Null(r.Schedule);
        // A group drag asked to change only the day is also no change.
        Assert.Equal(MeetingMoveRefusal.NoChange, MeetingMover.Move(TueThu(), 0, 5, 600, false).Refusal);
    }

    /// <summary>Starting before midnight or ending after it is OutOfDay.</summary>
    [Fact]
    public void OutOfDay_Refused()
    {
        Assert.Equal(MeetingMoveRefusal.OutOfDay, MeetingMover.Move(TueThu(), 0, 2, -5, false).Refusal);
        Assert.Equal(MeetingMoveRefusal.OutOfDay, MeetingMover.Move(TueThu(), 0, 2, 24 * 60 - 30, false).Refusal);
        Assert.True(MeetingMover.Move(TueThu(), 0, 2, 24 * 60 - 90, false).IsOk);
    }

    // -- Purity and preservation ------------------------------------------------

    /// <summary>The input list and its objects are untouched, and every result meeting is a new object.</summary>
    [Fact]
    public void Move_DoesNotMutateInput()
    {
        var input = TueThu();
        var originals = input.ToList();
        var before = input.Select(m => (m.Day, m.StartMinutes, m.DurationMinutes)).ToList();

        var r = MeetingMover.Move(input, 0, 2, 690, false);

        Assert.True(r.IsOk);
        Assert.Equal(originals, input);
        Assert.Equal(before, input.Select(m => (m.Day, m.StartMinutes, m.DurationMinutes)).ToList());
        Assert.All(r.Schedule!, m => Assert.DoesNotContain(m, input));
    }

    /// <summary>Untouched and moved meetings keep every field and their unknown JSON fields.</summary>
    [Fact]
    public void Move_PreservesEveryFieldAndExtensionData()
    {
        var input = new List<SectionDaySchedule> { M(1, 480, 75, "odd", "r1"), M(3, 600, 120, null, "r2"), M(5, 600, 60, "even", "r3") };
        var r = MeetingMover.Move(input, 0, 2, 900, moveAlone: true);

        Assert.True(r.IsOk);
        for (int i = 0; i < input.Count; i++)
        {
            var a = input[i]; var b = r.Schedule![i];
            Assert.Equal(a.DurationMinutes, b.DurationMinutes);
            Assert.Equal(a.MeetingTypeId, b.MeetingTypeId);
            Assert.Equal(a.RoomId, b.RoomId);
            Assert.Equal(a.RoomTypeId, b.RoomTypeId);
            Assert.Equal(a.Frequency, b.Frequency);
            Assert.NotNull(b.ExtensionData);
            Assert.Equal(a.ExtensionData!.Keys.OrderBy(k => k), b.ExtensionData!.Keys.OrderBy(k => k));
        }
        Assert.Equal((2, 900), (r.Schedule![0].Day, r.Schedule![0].StartMinutes));
        Assert.Equal((3, 600), (r.Schedule![1].Day, r.Schedule![1].StartMinutes));
    }

    // -- Clone ------------------------------------------------------------------

    /// <summary>Clone copies every public settable property and the extension data.</summary>
    [Fact]
    public void Clone_CopiesEveryPublicSettableProperty()
    {
        var source = new SectionDaySchedule();
        var props = typeof(SectionDaySchedule).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToList();
        Assert.NotEmpty(props);
        int n = 0;
        foreach (var p in props)
        {
            n++;
            if (p.Name == nameof(PersistedRecord.ExtensionData)) { p.SetValue(source, FutureFields.Make("clone")); continue; }
            if (p.PropertyType == typeof(int)) p.SetValue(source, 100 + n);
            else if (p.PropertyType == typeof(string)) p.SetValue(source, "v" + n);
            else if (p.PropertyType == typeof(bool)) p.SetValue(source, true);
            else Assert.Fail($"Unhandled property type {p.PropertyType} for {p.Name}; extend this test.");
        }

        var copy = source.Clone();

        Assert.NotSame(source, copy);
        foreach (var p in props)
        {
            if (p.Name == nameof(PersistedRecord.ExtensionData))
            {
                Assert.NotNull(copy.ExtensionData);
                Assert.Equal(source.ExtensionData!.Keys.OrderBy(k => k), copy.ExtensionData!.Keys.OrderBy(k => k));
            }
            else Assert.Equal(p.GetValue(source), p.GetValue(copy));
        }
    }

    // -- Snap -------------------------------------------------------------------

    /// <summary>Legal starts for 1.5 h and 3 h blocks.</summary>
    private static List<LegalStartTime> Legal() =>
    [
        new() { BlockLength = 1.5, StartTimes = [480, 570, 660] },
        new() { BlockLength = 3.0, StartTimes = [600, 780] },
    ];

    /// <summary>The block length matching the duration supplies the candidates.</summary>
    [Fact]
    public void Snap_ExactLengthMatch()
    {
        Assert.Equal(570, MeetingMover.Snap(590, 90, Legal(), free: false));
        Assert.Equal(600, MeetingMover.Snap(590, 180, Legal(), free: false));
    }

    /// <summary>Equidistant candidates: the earlier wins.</summary>
    [Fact]
    public void Snap_TieGoesToEarlier()
    {
        Assert.Equal(480, MeetingMover.Snap(525, 90, Legal(), free: false));   // 480 and 570 both 45 away
    }

    /// <summary>A length with no legal starts snaps to the nearest start of any length.</summary>
    [Fact]
    public void Snap_CustomLength_UsesAllStarts()
    {
        Assert.Equal(780, MeetingMover.Snap(760, 100, Legal(), free: false));
    }

    /// <summary>With no legal starts at all, round to 5 minutes.</summary>
    [Fact]
    public void Snap_NoLegalStarts_FiveMinutes()
    {
        Assert.Equal(595, MeetingMover.Snap(596, 90, new List<LegalStartTime>(), free: false));
        Assert.Equal(595, MeetingMover.Snap(596, 90, [new() { BlockLength = 1.5 }], free: false));
    }

    /// <summary>Free (Shift) ignores legal starts and rounds to 5 minutes.</summary>
    [Fact]
    public void Snap_Free_FiveMinutes()
    {
        Assert.Equal(595, MeetingMover.Snap(593, 90, Legal(), free: true));
        Assert.Equal(600, MeetingMover.Snap(598, 90, Legal(), free: true));
    }

    // -- Labels -----------------------------------------------------------------

    /// <summary>The approved label formats.</summary>
    [Fact]
    public void Labels_AreTheApprovedFormat()
    {
        Assert.Equal("Tue, Thu 11:30-13:00", MeetingMover.TimeLabel([4, 2, 4], 690, 90));
        Assert.Equal("Mon 8:30-10:00", MeetingMover.TimeLabel([1], 510, 90));
        Assert.Equal("Overlaps its Wed 10:00 meeting", MeetingMover.OverlapLabel(3, 600));
    }

    // -- Context menu picks the exact meeting -----------------------------------

    /// <summary>Stores a section with two same-day/start (odd/even) meetings and returns it as the grid loads it.</summary>
    private (SectionRepository Repo, Section Section) StoreTwins()
    {
        var repo = new SectionRepository(_fx.Db);
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [M(2, 600, freq: "odd", room: "room-a"), M(2, 600, freq: "even", room: "room-a")],
        };
        repo.Insert(section);
        return (repo, repo.GetById(section.Id)!);
    }

    /// <summary>Loads a menu, picks room B in the Room panel and confirms.</summary>
    private static void PickRoomB(SectionContextMenuViewModel menu, Section section, int meetingDay, int meetingStart, int meetingIndex)
    {
        var rooms = new List<Room> { new() { Id = "room-a", RoomNumber = "A" }, new() { Id = "room-b", RoomNumber = "B" } };
        menu.Load(section, meetingDay, meetingStart, new List<Instructor>(), rooms,
            new List<SchedulingEnvironmentValue>(), meetingIndex: meetingIndex);
        menu.ShowRoomCommand.Execute(null);
        menu.SelectedRoom = menu.Rooms.First(r => r.Id == "room-b");
        menu.ConfirmCommand.Execute(null);
    }

    /// <summary>The meeting index picks the second twin.</summary>
    [Fact]
    public void ContextMenu_UsesMeetingIndex_ForTwins()
    {
        var (repo, section) = StoreTwins();
        var menu = new SectionContextMenuViewModel(repo, () => { }, _fx.Lock, new FakeSectionCardState(), new SectionMarks());

        PickRoomB(menu, section, 2, 600, meetingIndex: 1);

        var saved = repo.GetById(section.Id)!;
        Assert.Equal("room-a", saved.Schedule[0].RoomId);
        Assert.Equal("room-b", saved.Schedule[1].RoomId);
    }

    /// <summary>A stale index (its meeting no longer at that day/start) falls back to the first match.</summary>
    [Fact]
    public void ContextMenu_StaleIndex_FallsBackToFirstMatch()
    {
        var (repo, section) = StoreTwins();
        section.Schedule.Add(M(4, 780, room: "room-a"));   // index 2 is Thu 13:00, not Tue 10:00
        repo.Update(section);
        section = repo.GetById(section.Id)!;
        var menu = new SectionContextMenuViewModel(repo, () => { }, _fx.Lock, new FakeSectionCardState(), new SectionMarks());

        PickRoomB(menu, section, 2, 600, meetingIndex: 2);

        var saved = repo.GetById(section.Id)!;
        Assert.Equal("room-b", saved.Schedule[0].RoomId);
        Assert.Equal("room-a", saved.Schedule[1].RoomId);
        Assert.Equal("room-a", saved.Schedule[2].RoomId);
    }
}

using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="SectionReferences"/>, the one helper shared by the deletes' cleanup (a deleted room or tag is
/// removed from every section) and by undo (references to things deleted since a step are left out).
/// </summary>
public sealed class SectionReferencesTests
{
    private const string Values = SectionReferences.EnvironmentValuesTable;

    /// <summary>A section that references every kind of thing, with two meetings.</summary>
    private static Section Full() => new()
    {
        Id = "s1",
        SemesterId = "sem1",
        CourseId = "c1",
        SectionCode = "A",
        CampusId = "campus1",
        SectionTypeId = "st1",
        DeliveryTypeId = "dt1",
        TagIds = { "tag1", "tag2" },
        ResourceIds = { "res1", "res2" },
        Reserves = { new SectionReserve { ReserveId = "rv1", Code = 1 }, new SectionReserve { ReserveId = "rv2", Code = 2 } },
        InstructorAssignments =
        {
            new InstructorAssignment { InstructorId = "i1", Workload = 1m },
            new InstructorAssignment { InstructorId = "i2" },
        },
        Schedule =
        {
            new SectionDaySchedule { Day = 1, StartMinutes = 600, DurationMinutes = 60, RoomId = "r1", MeetingTypeId = "mt1", RoomTypeId = "rt1" },
            new SectionDaySchedule { Day = 3, StartMinutes = 600, DurationMinutes = 60, RoomId = "r1", MeetingTypeId = "mt2" },
        },
    };

    /// <summary>The section as stored, for before-and-after comparison.</summary>
    private static string Json(Section s) => JsonHelpers.Serialize(s);

    // ── The two cleanup helpers ──────────────────────────────────────────────

    /// <summary>RemoveRoom clears the room from every meeting that uses it, and says so only when something changed.</summary>
    [Fact]
    public void RemoveRoom_ClearsEveryMeetingUsingIt()
    {
        var s = Full();
        s.Schedule.Add(new SectionDaySchedule { Day = 5, RoomId = "r2" });

        Assert.True(SectionReferences.RemoveRoom(s, "r1"));

        Assert.Equal(new string?[] { null, null, "r2" }, s.Schedule.Select(m => m.RoomId).ToArray());
        Assert.False(SectionReferences.RemoveRoom(s, "r1"));
        Assert.False(SectionReferences.RemoveRoom(s, "nobody"));
    }

    /// <summary>RemoveEnvironmentValue does for each type exactly what the delete cleanup did before it was shared.</summary>
    [Theory]
    [InlineData(SchedulingEnvironmentTypes.Tag, "tag1")]
    [InlineData(SchedulingEnvironmentTypes.Resource, "res1")]
    [InlineData(SchedulingEnvironmentTypes.SectionType, "st1")]
    [InlineData(SchedulingEnvironmentTypes.DeliveryType, "dt1")]
    [InlineData(SchedulingEnvironmentTypes.Reserve, "rv1")]
    [InlineData(SchedulingEnvironmentTypes.MeetingType, "mt1")]
    public void RemoveEnvironmentValue_RemovesTheReference(string type, string id)
    {
        var s = Full();
        var before = Json(s);

        Assert.True(SectionReferences.RemoveEnvironmentValue(s, type, id));

        Assert.NotEqual(before, Json(s));
        Assert.DoesNotContain($"\"{id}\"", Json(s));
        Assert.False(SectionReferences.RemoveEnvironmentValue(s, type, id));              // already gone
        Assert.False(SectionReferences.RemoveEnvironmentValue(Full(), type, "other"));    // not referenced
    }

    /// <summary>Other values of the same type stay; a meeting type is cleared from every meeting that has it.</summary>
    [Fact]
    public void RemoveEnvironmentValue_KeepsOtherValues_AndClearsEveryMeeting()
    {
        var s = Full();
        s.Schedule.Add(new SectionDaySchedule { Day = 5, MeetingTypeId = "mt1" });

        SectionReferences.RemoveEnvironmentValue(s, SchedulingEnvironmentTypes.MeetingType, "mt1");
        SectionReferences.RemoveEnvironmentValue(s, SchedulingEnvironmentTypes.Tag, "tag1");
        SectionReferences.RemoveEnvironmentValue(s, SchedulingEnvironmentTypes.Reserve, "rv1");

        Assert.Equal(new string?[] { null, "mt2", null }, s.Schedule.Select(m => m.MeetingTypeId).ToArray());
        Assert.Equal(new[] { "tag2" }, s.TagIds);
        Assert.Equal(new[] { "rv2" }, s.Reserves.Select(r => r.ReserveId).ToArray());
    }

    /// <summary>Room type and staff type do nothing to sections (as before), and neither does an unknown type.</summary>
    [Theory]
    [InlineData(SchedulingEnvironmentTypes.RoomType)]
    [InlineData(SchedulingEnvironmentTypes.StaffType)]
    [InlineData("somethingNew")]
    public void RemoveEnvironmentValue_RoomTypeAndStaffType_DoNothing(string type)
    {
        var s = Full();
        var before = Json(s);

        Assert.False(SectionReferences.RemoveEnvironmentValue(s, type, "rt1"));
        Assert.False(SectionReferences.RemoveEnvironmentValue(s, type, "st1"));

        Assert.Equal(before, Json(s));
    }

    // ── RemoveDeleted ────────────────────────────────────────────────────────

    /// <summary>
    /// Every kind of reference whose target was deleted is removed and reported (kind word and table); campus and room
    /// type are untouched even when deleted; references to things not deleted stay.
    /// </summary>
    [Fact]
    public void RemoveDeleted_RemovesEveryKind_ButNotCampusOrRoomType()
    {
        var s = Full();
        var deleted = new HashSet<(string, string)>
        {
            (SectionReferences.RoomsTable, "r1"), (Values, "mt1"), (Values, "st1"), (Values, "dt1"), (Values, "tag1"),
            (Values, "res1"), (Values, "rv1"), (SectionReferences.InstructorsTable, "i1"),
            ("Campuses", "campus1"), (Values, "rt1"),
        };

        var removed = SectionReferences.RemoveDeleted(s, (table, id) => deleted.Contains((table, id)));

        Assert.Equal(new[]
        {
            new RemovedReference(SectionReferences.RoomsTable, "r1", "room"),
            new RemovedReference(Values, "mt1", "meeting type"),
            new RemovedReference(SectionReferences.RoomsTable, "r1", "room"),   // the second meeting
            new RemovedReference(Values, "st1", "section type"),
            new RemovedReference(Values, "dt1", "delivery type"),
            new RemovedReference(Values, "tag1", "tag"),
            new RemovedReference(Values, "res1", "resource"),
            new RemovedReference(Values, "rv1", "reserve"),
            new RemovedReference(SectionReferences.InstructorsTable, "i1", "instructor"),
        }, removed.ToArray());

        Assert.All(s.Schedule, m => Assert.Null(m.RoomId));
        Assert.Equal(new string?[] { null, "mt2" }, s.Schedule.Select(m => m.MeetingTypeId).ToArray());
        Assert.Null(s.SectionTypeId);
        Assert.Null(s.DeliveryTypeId);
        Assert.Equal(new[] { "tag2" }, s.TagIds);
        Assert.Equal(new[] { "res2" }, s.ResourceIds);
        Assert.Equal(new[] { "rv2" }, s.Reserves.Select(r => r.ReserveId).ToArray());
        Assert.Equal(new[] { "i2" }, s.InstructorAssignments.Select(a => a.InstructorId).ToArray());
        Assert.Equal("campus1", s.CampusId);                       // campus: untouched
        Assert.Equal("rt1", s.Schedule[0].RoomTypeId);             // room type: untouched
    }

    /// <summary>Nothing deleted: nothing removed, the section is unchanged.</summary>
    [Fact]
    public void RemoveDeleted_WithNothingDeleted_ChangesNothing()
    {
        var s = Full();
        var before = Json(s);

        var removed = SectionReferences.RemoveDeleted(s, (_, _) => false);

        Assert.Empty(removed);
        Assert.Equal(before, Json(s));
    }

    /// <summary>Unknown JSON fields (written by a newer version) survive the removal.</summary>
    [Fact]
    public void RemoveDeleted_KeepsUnknownFields()
    {
        var s = JsonHelpers.Deserialize<Section>(
            "{\"id\":\"s1\",\"sectionCode\":\"A\",\"tagIds\":[\"t1\"],\"fromTheFuture\":{\"k\":[1,2]}}");

        var removed = SectionReferences.RemoveDeleted(s, (t, id) => t == Values && id == "t1");

        Assert.Equal("tag", Assert.Single(removed).Kind);
        Assert.Contains("fromTheFuture", Json(s));
        Assert.Empty(s.TagIds);
    }
}

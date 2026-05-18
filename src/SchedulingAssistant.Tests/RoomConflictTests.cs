using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

public class RoomConflictTests
{
    private static readonly Dictionary<string, string> Rooms = new()
    {
        ["r1"] = "A 101",
        ["r2"] = "B 202"
    };

    private static readonly Dictionary<string, string> Courses = new()
    {
        ["c1"] = "CHEM101",
        ["c2"] = "BIOL200"
    };

    private static Section MakeSection(
        string id,
        string? courseId,
        string sectionCode,
        string? roomId,
        int day,
        int start,
        int duration,
        string? frequency = null,
        string semesterId = "sem1")
    {
        return new Section
        {
            Id = id,
            CourseId = courseId,
            SectionCode = sectionCode,
            SemesterId = semesterId,
            Schedule =
            [
                new SectionDaySchedule
                {
                    Day = day,
                    StartMinutes = start,
                    DurationMinutes = duration,
                    RoomId = roomId,
                    Frequency = frequency
                }
            ]
        };
    }

    [Fact]
    public void NoConflict_DifferentRooms()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r2", day: 1, start: 480, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void NoConflict_DifferentDays()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r1", day: 2, start: 480, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void NoConflict_AdjacentTimes()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 540, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void NoConflict_NullRoom()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", null, day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", null, day: 1, start: 480, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void Conflict_ExactOverlap()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);

        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
        Assert.Single(result["s1"]);
        Assert.Single(result["s2"]);
        Assert.Contains("BIOL200 A", result["s1"][0]);
        Assert.Contains("CHEM101 A", result["s2"][0]);
    }

    [Fact]
    public void Conflict_PartialOverlap()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 510, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
    }

    [Fact]
    public void NoConflict_OddVsEvenFrequency()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60, frequency: "odd"),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60, frequency: "even")
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void Conflict_OddVsNull_WeeklyOverlaps()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60, frequency: "odd"),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60, frequency: null)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
    }

    [Fact]
    public void NoConflict_DisjointWeekLists()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60, frequency: "1,2,3"),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60, frequency: "4,5,6")
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void Conflict_OverlappingWeekLists()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60, frequency: "1,3,5"),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60, frequency: "3,7,9")
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
    }

    [Fact]
    public void MultipleConflicts_OneSection()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s3", "c1", "B", "r1", day: 1, start: 500, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.Equal(2, result["s1"].Count);
    }

    [Fact]
    public void ThreeWayConflict()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", "r1", day: 1, start: 480, duration: 60),
            MakeSection("s3", "c1", "B", "r1", day: 1, start: 480, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
        Assert.True(result.ContainsKey("s3"));
        Assert.Equal(2, result["s1"].Count);
        Assert.Equal(2, result["s2"].Count);
        Assert.Equal(2, result["s3"].Count);
    }

    [Fact]
    public void ConflictDescription_ContainsRoomAndDay()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", "r1", day: 3, start: 480, duration: 60),
            MakeSection("s2", "c2", "B", "r1", day: 3, start: 480, duration: 60)
        };

        var result = RoomConflictService.DetectConflicts(sections, Rooms, Courses);
        var desc = result["s1"][0];
        Assert.Contains("A 101", desc);
        Assert.Contains("Wed", desc);
        Assert.Contains("0800", desc);
    }
}

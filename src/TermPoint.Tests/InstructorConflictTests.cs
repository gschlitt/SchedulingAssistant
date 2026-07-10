using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class InstructorConflictTests
{
    private static readonly Dictionary<string, string> Instructors = new()
    {
        ["i1"] = "J. Smith",
        ["i2"] = "A. Jones"
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
        string[] instructorIds,
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
            InstructorAssignments = instructorIds
                .Select(iid => new InstructorAssignment { InstructorId = iid })
                .ToList(),
            Schedule =
            [
                new SectionDaySchedule
                {
                    Day = day,
                    StartMinutes = start,
                    DurationMinutes = duration,
                    Frequency = frequency
                }
            ]
        };
    }

    [Fact]
    public void NoConflict_DifferentInstructors()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i2"], day: 1, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void NoConflict_DifferentDays()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 2, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void NoConflict_AdjacentTimes()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 540, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void NoConflict_NoInstructor()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", [], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", [], day: 1, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void Conflict_ExactOverlap()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);

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
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 510, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
    }

    [Fact]
    public void NoConflict_OddVsEvenFrequency()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "odd"),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "even")
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void Conflict_OddVsNull_WeeklyOverlaps()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "odd"),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: null)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
    }

    [Fact]
    public void NoConflict_DisjointWeekLists()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "1,2,3"),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "4,5,6")
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void Conflict_OverlappingWeekLists()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "1,3,5"),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60, frequency: "3,7,9")
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
    }

    [Fact]
    public void MultipleConflicts_OneSection()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s3", "c1", "B", ["i1"], day: 1, start: 500, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.Equal(2, result["s1"].Count);
    }

    [Fact]
    public void ThreeWayConflict()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s3", "c1", "B", ["i1"], day: 1, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));
        Assert.True(result.ContainsKey("s3"));
        Assert.Equal(2, result["s1"].Count);
        Assert.Equal(2, result["s2"].Count);
        Assert.Equal(2, result["s3"].Count);
    }

    [Fact]
    public void ConflictDescription_ContainsInstructorNameAndDay()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 3, start: 480, duration: 60),
            MakeSection("s2", "c2", "B", ["i1"], day: 3, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);
        var desc = result["s1"][0];
        Assert.Contains("J. Smith", desc);
        Assert.Contains("Wed", desc);
        Assert.Contains("0800", desc);
    }

    [Fact]
    public void TwoInstructors_OnlyConflictingOneAppears()
    {
        // s1 has instructors i1 and i2; s2 has only i1.
        // Only i1 conflicts — i2 should not appear in s2's warnings.
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1", "i2"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflicts(sections, Instructors, Courses);

        Assert.True(result.ContainsKey("s1"));
        Assert.True(result.ContainsKey("s2"));

        // Both sections should mention J. Smith (i1), not A. Jones (i2)
        Assert.All(result["s1"], desc => Assert.Contains("J. Smith", desc));
        Assert.All(result["s2"], desc => Assert.Contains("J. Smith", desc));
        Assert.DoesNotContain(result["s1"], desc => desc.Contains("A. Jones"));
        Assert.DoesNotContain(result["s2"], desc => desc.Contains("A. Jones"));
    }

    // ── DetectConflictsByInstructor tests ────────────────────────────────────

    [Fact]
    public void ByInstructor_NoConflict_ReturnsEmpty()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 540, duration: 60)
        };

        var result = InstructorConflictService.DetectConflictsByInstructor(sections, Courses);
        Assert.Empty(result);
    }

    [Fact]
    public void ByInstructor_Conflict_KeyedByInstructorId()
    {
        var sections = new List<Section>
        {
            MakeSection("s1", "c1", "A", ["i1"], day: 1, start: 480, duration: 60),
            MakeSection("s2", "c2", "A", ["i1"], day: 1, start: 480, duration: 60)
        };

        var result = InstructorConflictService.DetectConflictsByInstructor(sections, Courses);
        Assert.True(result.ContainsKey("i1"));
        Assert.Single(result["i1"]);
        Assert.Contains("CHEM101 A", result["i1"][0]);
        Assert.Contains("BIOL200 A", result["i1"][0]);
    }
}

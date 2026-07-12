using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ProgramConflictService.DetectConflicts"/>. Each test builds
/// synthetic sections and watches to verify conflict detection logic.
/// </summary>
public sealed class ProgramConflictServiceTests
{
    // ── Tag-based watch: basic overlap ────────────────────────────────────────

    [Fact]
    public void TagBased_OverlappingSectionsFromDifferentCourses_ProducesConflict()
    {
        var watch = MakeTagWatch("w1", "CS Core", ["tag-core"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-core"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 510, duration: 60, ["tag-core"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Single(result);
        var c = result[0];
        Assert.Equal("w1", c.WatchId);
        Assert.Equal("s1", c.MeetingA.SectionId);
        Assert.Equal("s2", c.MeetingB.SectionId);
    }

    // ── Same course: no conflict ─────────────────────────────────────────────

    [Fact]
    public void SameCourse_OverlappingSections_NoConflict()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    // ── No time overlap: no conflict ─────────────────────────────────────────

    [Fact]
    public void DifferentCourses_NoTimeOverlap_NoConflict()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 600, duration: 60, ["tag-1"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    // ── Adjacent times (touching but not overlapping): no conflict ────────────

    [Fact]
    public void AdjacentTimes_NoConflict()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        // secA ends at 540, secB starts at 540 — half-open intervals don't overlap
        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 540, duration: 60, ["tag-1"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    // ── Different days: no conflict ──────────────────────────────────────────

    [Fact]
    public void SameTime_DifferentDays_NoConflict()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-2", day: 2, start: 480, duration: 60, ["tag-1"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    // ── Tag AND logic: section must carry ALL watch tags ─────────────────────

    [Fact]
    public void TagAndLogic_SectionMissingOneTag_NotCovered()
    {
        var watch = MakeTagWatch("w1", "Multi-tag", ["tag-A", "tag-B"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-A", "tag-B"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, ["tag-A"]); // missing tag-B

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    [Fact]
    public void TagAndLogic_BothSectionsHaveAllTags_ProducesConflict()
    {
        var watch = MakeTagWatch("w1", "Multi-tag", ["tag-A", "tag-B"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-A", "tag-B"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, ["tag-A", "tag-B", "tag-C"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Single(result);
    }

    // ── Course-based watch ───────────────────────────────────────────────────

    [Fact]
    public void CourseBased_OverlappingSections_ProducesConflict()
    {
        var watch = MakeCourseWatch("w1", "Year 1", ["crs-1", "crs-2"]);

        var secA = MakeSection("s1", "crs-1", day: 3, start: 600, duration: 50, []);
        var secB = MakeSection("s2", "crs-2", day: 3, start: 620, duration: 50, []);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Single(result);
    }

    [Fact]
    public void CourseBased_SectionNotInCourseList_NotCovered()
    {
        var watch = MakeCourseWatch("w1", "Year 1", ["crs-1", "crs-2"]);

        var secA = MakeSection("s1", "crs-1", day: 3, start: 600, duration: 50, []);
        var secB = MakeSection("s2", "crs-3", day: 3, start: 600, duration: 50, []); // crs-3 not in watch

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    // ── Multiple watches: same pair can conflict independently ────────────────

    [Fact]
    public void MultipleWatches_SamePairConflictsUnderBoth()
    {
        var w1 = MakeTagWatch("w1", "Watch A", ["tag-1"]);
        var w2 = MakeTagWatch("w2", "Watch B", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, ["tag-1"]);

        var result = Detect([w1, w2], [secA, secB], TagMap(secA, secB));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.WatchId == "w1");
        Assert.Contains(result, c => c.WatchId == "w2");
    }

    // ── Multiple meetings on different days ──────────────────────────────────

    [Fact]
    public void MultipleMeetings_ConflictsOnlyOnOverlappingDays()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        // secA meets Mon+Wed, secB meets Wed+Fri — only Wed overlaps
        var secA = MakeSection("s1", "crs-1", tags: ["tag-1"],
            (1, 480, 60), (3, 480, 60));
        var secB = MakeSection("s2", "crs-2", tags: ["tag-1"],
            (3, 480, 60), (5, 480, 60));

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Single(result);
        Assert.Equal(3, result[0].MeetingA.Day); // Wednesday
    }

    // ── Co-scheduled: exact same time slot ───────────────────────────────────

    [Fact]
    public void CoScheduled_ExactSameSlot_ProducesConflict()
    {
        var watch = MakeCourseWatch("w1", "Block", ["crs-1", "crs-2"]);

        var secA = MakeSection("s1", "crs-1", day: 2, start: 510, duration: 75, []);
        var secB = MakeSection("s2", "crs-2", day: 2, start: 510, duration: 75, []);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Single(result);
    }

    // ── Frequency: odd vs even — no conflict ─────────────────────────────────

    [Fact]
    public void Frequency_OddVsEven_NoConflict()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"], frequency: "odd");
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, ["tag-1"], frequency: "even");

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    [Fact]
    public void Frequency_BothOdd_ProducesConflict()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"], frequency: "odd");
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, ["tag-1"], frequency: "odd");

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Single(result);
    }

    // ── Empty watch: no tags or courses → no conflicts ───────────────────────

    [Fact]
    public void EmptyTagWatch_NoConflicts()
    {
        var watch = new ProgramWatch { Id = "w1", Mode = ProgramWatchMode.Tag, TagIds = [] };

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, ["tag-1"]);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    [Fact]
    public void EmptyCourseWatch_NoConflicts()
    {
        var watch = new ProgramWatch { Id = "w1", Mode = ProgramWatchMode.Course, CourseIds = [] };

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, []);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, []);

        var result = Detect([watch], [secA, secB], TagMap(secA, secB));

        Assert.Empty(result);
    }

    // ── Section with no tags in map is ignored by tag-based watches ──────────

    [Fact]
    public void SectionNotInTagMap_NotCovered()
    {
        var watch = MakeTagWatch("w1", "Watch", ["tag-1"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, ["tag-1"]);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, []);

        // secB has no entry in tag map
        var tagMap = new Dictionary<string, IReadOnlyList<string>>
        {
            ["s1"] = new List<string> { "tag-1" },
        };

        var result = ProgramConflictService.DetectConflicts([watch], [secA, secB], tagMap);

        Assert.Empty(result);
    }

    // ── Three courses, three sections — multiple conflicts ───────────────────

    [Fact]
    public void ThreeCourses_AllOverlap_ThreeConflictPairs()
    {
        var watch = MakeCourseWatch("w1", "Trio", ["crs-1", "crs-2", "crs-3"]);

        var secA = MakeSection("s1", "crs-1", day: 1, start: 480, duration: 60, []);
        var secB = MakeSection("s2", "crs-2", day: 1, start: 480, duration: 60, []);
        var secC = MakeSection("s3", "crs-3", day: 1, start: 480, duration: 60, []);

        var result = Detect([watch], [secA, secB, secC], TagMap(secA, secB, secC));

        // 3 courses → C(3,2) = 3 pairs
        Assert.Equal(3, result.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProgramWatch MakeTagWatch(string id, string name, List<string> tagIds) =>
        new() { Id = id, Name = name, Mode = ProgramWatchMode.Tag, IsEnabled = true, TagIds = tagIds };

    private static ProgramWatch MakeCourseWatch(string id, string name, List<string> courseIds) =>
        new() { Id = id, Name = name, Mode = ProgramWatchMode.Course, IsEnabled = true, CourseIds = courseIds };

    /// <summary>Creates a section with a single schedule slot.</summary>
    private static Section MakeSection(
        string id, string courseId, int day, int start, int duration,
        List<string> tags, string? frequency = null) =>
        new()
        {
            Id = id,
            CourseId = courseId,
            TagIds = tags,
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = start, DurationMinutes = duration, Frequency = frequency }],
        };

    /// <summary>Creates a section with multiple schedule slots.</summary>
    private static Section MakeSection(
        string id, string courseId, List<string> tags,
        params (int Day, int Start, int Duration)[] slots)
    {
        var sec = new Section { Id = id, CourseId = courseId, TagIds = tags };
        foreach (var (day, start, duration) in slots)
            sec.Schedule.Add(new SectionDaySchedule { Day = day, StartMinutes = start, DurationMinutes = duration });
        return sec;
    }

    /// <summary>
    /// Builds the tag-ID-by-section-ID map from a set of sections, using each
    /// section's <see cref="SchedulableBase.TagIds"/>.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> TagMap(params Section[] sections)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var s in sections)
            map[s.Id] = s.TagIds;
        return map;
    }

    /// <summary>Shorthand that calls <see cref="ProgramConflictService.DetectConflicts"/>.</summary>
    private static List<ProgramConflict> Detect(
        List<ProgramWatch> watches,
        List<Section> sections,
        Dictionary<string, IReadOnlyList<string>> tagMap) =>
        ProgramConflictService.DetectConflicts(watches, sections, tagMap);
}

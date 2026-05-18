using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for the Room Availability Browser domain layer:
/// <see cref="OccupancyIndex"/>, <see cref="RoomSolution.Classify"/>,
/// <see cref="RoomAvailabilityService.GenerateTemplates"/>, and
/// <see cref="RoomAvailabilityService.GenerateSolutions"/>.
/// </summary>
public class RoomAvailabilityTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // OccupancyIndex.IsAvailable
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsAvailable_NoBookings_ReturnsTrue()
    {
        var index = new OccupancyIndex();
        index.Build([], [], null);

        Assert.True(index.IsAvailable("room1", 1, 480, 60));
    }

    [Fact]
    public void IsAvailable_DifferentRoom_ReturnsTrue()
    {
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.True(index.IsAvailable("room2", 1, 480, 60));
    }

    [Fact]
    public void IsAvailable_DifferentDay_ReturnsTrue()
    {
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.True(index.IsAvailable("room1", 2, 480, 60));
    }

    [Fact]
    public void IsAvailable_ExactOverlap_ReturnsFalse()
    {
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.False(index.IsAvailable("room1", 1, 480, 60));
    }

    [Fact]
    public void IsAvailable_PartialOverlap_ReturnsFalse()
    {
        // Booking 8:00-9:00 (480-540); candidate 8:30-9:30 (510-570).
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.False(index.IsAvailable("room1", 1, 510, 60));
    }

    [Fact]
    public void IsAvailable_CandidateContainsBooking_ReturnsFalse()
    {
        // Booking 9:00-10:00; candidate 8:00-11:00 — booking is fully inside candidate.
        var section = MakeSection("s1", "room1", day: 1, start: 540, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.False(index.IsAvailable("room1", 1, 480, 180));
    }

    [Fact]
    public void IsAvailable_BookingContainsCandidate_ReturnsFalse()
    {
        // Booking 8:00-11:00; candidate 9:00-10:00.
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 180);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.False(index.IsAvailable("room1", 1, 540, 60));
    }

    [Fact]
    public void IsAvailable_Adjacent_ReturnsTrue()
    {
        // Booking ends at 540 (9:00); candidate starts at 540. No overlap.
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.True(index.IsAvailable("room1", 1, 540, 60));
    }

    [Fact]
    public void IsAvailable_AdjacentBefore_ReturnsTrue()
    {
        // Candidate 7:00-8:00 (420-480); booking starts at 480.
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.True(index.IsAvailable("room1", 1, 420, 60));
    }

    [Fact]
    public void IsAvailable_ExcludedSection_ReturnsTrue()
    {
        // The section being edited should be excluded from occupancy.
        var section = MakeSection("s1", "room1", day: 1, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([section], [], excludeSectionId: "s1");

        Assert.True(index.IsAvailable("room1", 1, 480, 60));
    }

    [Fact]
    public void IsAvailable_MeetingsIncluded()
    {
        // Meetings (non-section events) should block the room.
        var meeting = new Meeting
        {
            Id = "m1",
            Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 60, RoomId = "room1" }]
        };
        var index = new OccupancyIndex();
        index.Build([], [meeting], null);

        Assert.False(index.IsAvailable("room1", 1, 480, 60));
    }

    [Fact]
    public void IsAvailable_NullRoomId_Ignored()
    {
        // Schedule entries with no room assigned should not block any room.
        var section = new Section
        {
            Id = "s1",
            Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 60, RoomId = null }]
        };
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        Assert.True(index.IsAvailable("room1", 1, 480, 60));
    }

    [Fact]
    public void IsAvailable_MultipleBookings_ChecksAll()
    {
        // Two bookings in same room on same day: 8:00-9:00 and 10:00-11:00.
        var section = new Section
        {
            Id = "s1",
            Schedule =
            [
                new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 60, RoomId = "room1" },
                new SectionDaySchedule { Day = 1, StartMinutes = 600, DurationMinutes = 60, RoomId = "room1" }
            ]
        };
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        // Gap between bookings should be free.
        Assert.True(index.IsAvailable("room1", 1, 540, 60));
        // Each booking should block.
        Assert.False(index.IsAvailable("room1", 1, 480, 60));
        Assert.False(index.IsAvailable("room1", 1, 600, 60));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FrequenciesOverlap
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "odd", true)]
    [InlineData("even", null, true)]
    [InlineData("", "odd", true)]
    [InlineData("odd", "even", false)]
    [InlineData("even", "odd", false)]
    [InlineData("odd", "odd", true)]
    [InlineData("even", "even", true)]
    [InlineData("odd", "1,3,5", true)]
    [InlineData("even", "1,3,5", false)]
    [InlineData("odd", "2,4,6", false)]
    [InlineData("even", "2,4,6", true)]
    [InlineData("1,3", "2,4", false)]
    [InlineData("1,3", "3,5", true)]
    [InlineData("1,6,7", "2,4,8", false)]
    [InlineData("1,6,7", "7,9,11", true)]
    public void FrequenciesOverlap_Cases(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, SectionDaySchedule.FrequenciesOverlap(a, b));
        // Symmetric: order shouldn't matter.
        Assert.Equal(expected, SectionDaySchedule.FrequenciesOverlap(b, a));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OccupancyIndex.IsAvailable with frequency
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsAvailable_OddVsEven_NoConflict()
    {
        var section = new Section
        {
            Id = "s1",
            Schedule = [new SectionDaySchedule { Day = 5, StartMinutes = 780, DurationMinutes = 80, RoomId = "room1", Frequency = "odd" }]
        };
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        // Even-week query at the same time should be available.
        Assert.True(index.IsAvailable("room1", 5, 780, 80, "even"));
        // Odd-week query at the same time should still conflict.
        Assert.False(index.IsAvailable("room1", 5, 780, 80, "odd"));
        // Every-week (null) query overlaps with odd — conflict.
        Assert.False(index.IsAvailable("room1", 5, 780, 80));
    }

    [Fact]
    public void IsAvailable_CustomWeekLists_NoOverlap()
    {
        var section = new Section
        {
            Id = "s1",
            Schedule = [new SectionDaySchedule { Day = 3, StartMinutes = 600, DurationMinutes = 50, RoomId = "room1", Frequency = "1,3,5" }]
        };
        var index = new OccupancyIndex();
        index.Build([section], [], null);

        // Disjoint week list — no conflict.
        Assert.True(index.IsAvailable("room1", 3, 600, 50, "2,4,6"));
        // Overlapping week list — conflict.
        Assert.False(index.IsAvailable("room1", 3, 600, 50, "3,7,9"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RoomSolution.Classify
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Classify_SingleSlot_IsTier1()
    {
        var slots = new[] { new SolutionSlot(1, 480, 60, "r1", "A 101") };
        Assert.Equal(SolutionTier.SameRoomSameTime, RoomSolution.Classify(slots));
    }

    [Fact]
    public void Classify_SameRoomSameTime_IsTier1()
    {
        var slots = new[]
        {
            new SolutionSlot(1, 480, 60, "r1", "A 101"),
            new SolutionSlot(3, 480, 60, "r1", "A 101"),
            new SolutionSlot(5, 480, 60, "r1", "A 101")
        };
        Assert.Equal(SolutionTier.SameRoomSameTime, RoomSolution.Classify(slots));
    }

    [Fact]
    public void Classify_SameRoomSameTime_DifferentDurations_IsTier1()
    {
        // Same start time counts as "same time" even if durations differ (MW 90, F 50).
        var slots = new[]
        {
            new SolutionSlot(1, 480, 90, "r1", "A 101"),
            new SolutionSlot(3, 480, 90, "r1", "A 101"),
            new SolutionSlot(5, 480, 50, "r1", "A 101")
        };
        Assert.Equal(SolutionTier.SameRoomSameTime, RoomSolution.Classify(slots));
    }

    [Fact]
    public void Classify_SameRoomDiffTimes_IsTier2a()
    {
        var slots = new[]
        {
            new SolutionSlot(1, 480, 60, "r1", "A 101"),
            new SolutionSlot(3, 540, 60, "r1", "A 101")
        };
        Assert.Equal(SolutionTier.SameRoomDiffTimes, RoomSolution.Classify(slots));
    }

    [Fact]
    public void Classify_SameTimeDiffRooms_IsTier2b()
    {
        var slots = new[]
        {
            new SolutionSlot(1, 480, 60, "r1", "A 101"),
            new SolutionSlot(3, 480, 60, "r2", "B 202")
        };
        Assert.Equal(SolutionTier.SameTimeDiffRooms, RoomSolution.Classify(slots));
    }

    [Fact]
    public void Classify_DiffRoomsDiffTimes_IsMixed()
    {
        var slots = new[]
        {
            new SolutionSlot(1, 480, 60, "r1", "A 101"),
            new SolutionSlot(3, 540, 60, "r2", "B 202")
        };
        Assert.Equal(SolutionTier.Mixed, RoomSolution.Classify(slots));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MeetingTemplate.DisplayLabel
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DisplayLabel_UniformDuration_ShowsSimpleFormat()
    {
        var template = new MeetingTemplate("p1", "MWF", new[]
        {
            new TemplateDaySpec(1, 50.0/60, 50, [480, 540]),
            new TemplateDaySpec(3, 50.0/60, 50, [480, 540]),
            new TemplateDaySpec(5, 50.0/60, 50, [480, 540])
        });
        Assert.Equal("MWF 50min", template.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_MixedDurations_ShowsGroupedFormat()
    {
        // MW 90min, F 50min.
        var template = new MeetingTemplate("p1", "MWF", new[]
        {
            new TemplateDaySpec(1, 1.5, 90, [480]),
            new TemplateDaySpec(3, 1.5, 90, [480]),
            new TemplateDaySpec(5, 50.0/60, 50, [480])
        });

        var label = template.DisplayLabel;
        Assert.StartsWith("MWF (", label);
        Assert.Contains("MW90min", label);
        Assert.Contains("F50min", label);
    }

    [Fact]
    public void DisplayLabel_EmptyDaySpecs_ReturnsPatternName()
    {
        var template = new MeetingTemplate("p1", "TR", Array.Empty<TemplateDaySpec>());
        Assert.Equal("TR", template.DisplayLabel);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateTemplates
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateTemplates_CrossProduct()
    {
        var patterns = new[]
        {
            new BlockPattern { Id = "mwf", Name = "MWF", Days = [1, 3, 5] },
            new BlockPattern { Id = "tr", Name = "TR", Days = [2, 4] }
        };
        var legalStarts = new[]
        {
            new LegalStartTime { BlockLength = 1.0, StartTimes = [480, 540, 600] },
            new LegalStartTime { BlockLength = 1.5, StartTimes = [480, 570] }
        };

        var service = new RoomAvailabilityService();
        var templates = service.GenerateTemplates(patterns, legalStarts);

        // 2 patterns × 2 block lengths = 4 templates.
        Assert.Equal(4, templates.Count);

        // Each template should have the right number of day specs.
        var mwfTemplates = templates.Where(t => t.PatternName == "MWF").ToList();
        Assert.Equal(2, mwfTemplates.Count);
        Assert.All(mwfTemplates, t => Assert.Equal(3, t.DaySpecs.Count));

        var trTemplates = templates.Where(t => t.PatternName == "TR").ToList();
        Assert.Equal(2, trTemplates.Count);
        Assert.All(trTemplates, t => Assert.Equal(2, t.DaySpecs.Count));
    }

    [Fact]
    public void GenerateTemplates_SkipsEmptyStartTimes()
    {
        var patterns = new[] { new BlockPattern { Id = "mwf", Name = "MWF", Days = [1, 3, 5] } };
        var legalStarts = new[]
        {
            new LegalStartTime { BlockLength = 1.0, StartTimes = [] },
            new LegalStartTime { BlockLength = 1.5, StartTimes = [480] }
        };

        var service = new RoomAvailabilityService();
        var templates = service.GenerateTemplates(patterns, legalStarts);

        // Only the 1.5hr block length has start times, so 1 template.
        Assert.Single(templates);
        Assert.Equal(90, templates[0].DaySpecs[0].DurationMinutes);
    }

    [Fact]
    public void GenerateTemplates_Sorted_ByPatternThenDuration()
    {
        var patterns = new[]
        {
            new BlockPattern { Id = "tr", Name = "TR", Days = [2, 4] },
            new BlockPattern { Id = "mwf", Name = "MWF", Days = [1, 3, 5] }
        };
        var legalStarts = new[]
        {
            new LegalStartTime { BlockLength = 1.5, StartTimes = [480] },
            new LegalStartTime { BlockLength = 1.0, StartTimes = [480] }
        };

        var service = new RoomAvailabilityService();
        var templates = service.GenerateTemplates(patterns, legalStarts);

        // Sorted by pattern name first (MWF < TR), then duration (60 < 90).
        Assert.Equal("MWF", templates[0].PatternName);
        Assert.Equal(60, templates[0].DaySpecs[0].DurationMinutes);
        Assert.Equal("MWF", templates[1].PatternName);
        Assert.Equal(90, templates[1].DaySpecs[0].DurationMinutes);
        Assert.Equal("TR", templates[2].PatternName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateSolutions — Tier 1 (same room, same time)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateSolutions_Tier1_SameRoomSameTime()
    {
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = MakeMWFTemplate(60, [480, 540]);
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, []);

        // Should have Tier 1 solutions: r1 at 480 and r1 at 540.
        var tier1 = solutions.Where(s => s.Tier == SolutionTier.SameRoomSameTime).ToList();
        Assert.True(tier1.Count >= 2, $"Expected at least 2 Tier 1 solutions, got {tier1.Count}");

        var at480 = tier1.First(s => s.Slots.All(sl => sl.StartMinutes == 480));
        Assert.All(at480.Slots, sl => Assert.Equal("r1", sl.RoomId));
    }

    [Fact]
    public void GenerateSolutions_Tier1_BlockedOnOneDay_NoTier1()
    {
        // Room blocked on Wednesday at all legal times → no Tier 1 for that room.
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = MakeMWFTemplate(60, [480]);

        var blocker = MakeSection("s1", "r1", day: 3, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([blocker], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, []);

        var tier1 = solutions.Where(s => s.Tier == SolutionTier.SameRoomSameTime).ToList();
        Assert.Empty(tier1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateSolutions — Augment mode (existing meetings)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateSolutions_AugmentMode_FillsGapDaysOnly()
    {
        // Section already has Monday. Template is MWF. Should only solve W and F.
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = MakeMWFTemplate(60, [480]);

        var existingMeetings = new[]
        {
            new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 60, RoomId = "r1" }
        };

        var index = new OccupancyIndex();
        index.Build([], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, existingMeetings);

        Assert.NotEmpty(solutions);

        // Each solution should include the existing Monday slot plus W and F.
        foreach (var sol in solutions)
        {
            var days = sol.Slots.Select(s => s.Day).OrderBy(d => d).ToList();
            Assert.Equal([1, 3, 5], days);
        }
    }

    [Fact]
    public void GenerateSolutions_AllDaysFilled_ReturnsEmpty()
    {
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = MakeMWFTemplate(60, [480]);

        // All three days already have meetings.
        var existingMeetings = new[]
        {
            new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 60, RoomId = "r1" },
            new SectionDaySchedule { Day = 3, StartMinutes = 480, DurationMinutes = 60, RoomId = "r1" },
            new SectionDaySchedule { Day = 5, StartMinutes = 480, DurationMinutes = 60, RoomId = "r1" }
        };

        var index = new OccupancyIndex();
        index.Build([], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, existingMeetings);

        Assert.Empty(solutions);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateSolutions — Tier classification with augment
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateSolutions_AugmentMode_TierAccountsForExisting()
    {
        // Existing: Monday at 480 in r1.
        // Gap days: W and F. If they also get r1 at 480, whole schedule is Tier 1.
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = MakeMWFTemplate(60, [480]);

        var existingMeetings = new[]
        {
            new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 60, RoomId = "r1" }
        };

        var index = new OccupancyIndex();
        index.Build([], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, existingMeetings);

        var tier1 = solutions.FirstOrDefault(s => s.Tier == SolutionTier.SameRoomSameTime);
        Assert.NotNull(tier1);
        Assert.All(tier1.Slots, sl => Assert.Equal("r1", sl.RoomId));
        Assert.All(tier1.Slots, sl => Assert.Equal(480, sl.StartMinutes));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateSolutions — Per-day durations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateSolutions_PerDayDurations_RespectsEachDayLength()
    {
        // MW = 90 min, F = 50 min. All days at 480.
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = new MeetingTemplate("p1", "MWF", new[]
        {
            new TemplateDaySpec(1, 1.5, 90, [480]),
            new TemplateDaySpec(3, 1.5, 90, [480]),
            new TemplateDaySpec(5, 50.0/60, 50, [480])
        });

        var index = new OccupancyIndex();
        index.Build([], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, []);

        Assert.NotEmpty(solutions);
        var sol = solutions.First();

        var mon = sol.Slots.First(s => s.Day == 1);
        var fri = sol.Slots.First(s => s.Day == 5);
        Assert.Equal(90, mon.DurationMinutes);
        Assert.Equal(50, fri.DurationMinutes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateSolutions — Deduplication
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateSolutions_NoDuplicates()
    {
        // With a single room and single legal start time, all tiers would
        // find the same (room, time) combination. Should appear only once.
        var rooms = new[] { MakeRoom("r1", "SCI", "101") };
        var template = MakeMWFTemplate(60, [480]);

        var index = new OccupancyIndex();
        index.Build([], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, []);

        var keys = solutions.Select(s =>
            string.Join("|", s.Slots.OrderBy(sl => sl.Day).Select(sl => $"{sl.Day}:{sl.RoomId}:{sl.StartMinutes}")))
            .ToList();
        Assert.Equal(keys.Distinct().Count(), keys.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateSolutions — Sort order
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateSolutions_SortedByTierThenRoomThenTime()
    {
        var rooms = new[]
        {
            MakeRoom("r1", "A", "101"),
            MakeRoom("r2", "B", "201")
        };

        // Two legal start times so both Tier 1 and Tier 2 solutions are possible.
        var template = MakeMWFTemplate(60, [480, 540]);

        // Block r1 on Wednesday at 480 → forces some solutions to use different times/rooms.
        var blocker = MakeSection("s1", "r1", day: 3, start: 480, duration: 60);
        var index = new OccupancyIndex();
        index.Build([blocker], [], null);

        var service = new RoomAvailabilityService();
        var solutions = service.GenerateSolutions(template, rooms, index, []);

        // Verify tier ordering: each solution's tier >= previous.
        for (int i = 1; i < solutions.Count; i++)
        {
            Assert.True(solutions[i].Tier >= solutions[i - 1].Tier,
                $"Solution {i} (Tier {solutions[i].Tier}) should not precede Tier {solutions[i - 1].Tier}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ApplyFilter
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFilter_ByCampus()
    {
        var rooms = new[]
        {
            new Room { Id = "r1", CampusId = "c1", Building = "A", RoomNumber = "101" },
            new Room { Id = "r2", CampusId = "c2", Building = "B", RoomNumber = "201" }
        };
        var filtered = RoomAvailabilityService.ApplyFilter(rooms, campusId: "c1", null, null, null);
        Assert.Single(filtered);
        Assert.Equal("r1", filtered[0].Id);
    }

    [Fact]
    public void ApplyFilter_ByBuilding_CaseInsensitive()
    {
        var rooms = new[]
        {
            new Room { Id = "r1", Building = "Science", RoomNumber = "101" },
            new Room { Id = "r2", Building = "Arts", RoomNumber = "201" }
        };
        var filtered = RoomAvailabilityService.ApplyFilter(rooms, null, building: "science", null, null);
        Assert.Single(filtered);
        Assert.Equal("r1", filtered[0].Id);
    }

    [Fact]
    public void ApplyFilter_ByMinCapacity()
    {
        var rooms = new[]
        {
            new Room { Id = "r1", Capacity = 20, Building = "A", RoomNumber = "101" },
            new Room { Id = "r2", Capacity = 50, Building = "A", RoomNumber = "102" },
            new Room { Id = "r3", Capacity = 80, Building = "A", RoomNumber = "103" }
        };
        var filtered = RoomAvailabilityService.ApplyFilter(rooms, null, null, null, minCapacity: 50);
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, r => Assert.True(r.Capacity >= 50));
    }

    [Fact]
    public void ApplyFilter_NullFilters_ReturnsAll()
    {
        var rooms = new[]
        {
            new Room { Id = "r1", Building = "A", RoomNumber = "101" },
            new Room { Id = "r2", Building = "B", RoomNumber = "201" }
        };
        var filtered = RoomAvailabilityService.ApplyFilter(rooms, null, null, null, null);
        Assert.Equal(2, filtered.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RoomLabel helper
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoomLabel_WithBuilding_ConcatenatesBuildingAndNumber()
    {
        var room = new Room { Building = "SCI", RoomNumber = "101" };
        Assert.Equal("SCI 101", RoomAvailabilityService.RoomLabel(room));
    }

    [Fact]
    public void RoomLabel_NoBuilding_ReturnsRoomNumberOnly()
    {
        var room = new Room { RoomNumber = "101" };
        Assert.Equal("101", RoomAvailabilityService.RoomLabel(room));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TierLabel
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TierLabel_ReturnsHumanReadableStrings()
    {
        Assert.Equal("Same room, same time",
            new RoomSolution([], SolutionTier.SameRoomSameTime).TierLabel);
        Assert.Equal("Same room, different times",
            new RoomSolution([], SolutionTier.SameRoomDiffTimes).TierLabel);
        Assert.Equal("Same time, different rooms",
            new RoomSolution([], SolutionTier.SameTimeDiffRooms).TierLabel);
        Assert.Equal("Mixed",
            new RoomSolution([], SolutionTier.Mixed).TierLabel);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static Section MakeSection(string id, string roomId, int day, int start, int duration)
    {
        return new Section
        {
            Id = id,
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = start, DurationMinutes = duration, RoomId = roomId }]
        };
    }

    private static Room MakeRoom(string id, string building, string number, int capacity = 30)
    {
        return new Room { Id = id, Building = building, RoomNumber = number, Capacity = capacity };
    }

    /// <summary>
    /// Creates a uniform MWF template where all days share the same duration and legal start times.
    /// </summary>
    private static MeetingTemplate MakeMWFTemplate(int durationMinutes, int[] legalStarts)
    {
        double blockLengthHours = durationMinutes / 60.0;
        var starts = legalStarts.ToList().AsReadOnly();
        return new MeetingTemplate("mwf", "MWF", new[]
        {
            new TemplateDaySpec(1, blockLengthHours, durationMinutes, starts),
            new TemplateDaySpec(3, blockLengthHours, durationMinutes, starts),
            new TemplateDaySpec(5, blockLengthHours, durationMinutes, starts)
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Spec-based solver (GenerateSolutionsFromSpecs)
    // ═══════════════════════════════════════════════════════════════════════

    private static BlockPattern MakePattern(string id, string name, int[] days) =>
        new BlockPattern { Id = id, Name = name, Days = days.ToList() };

    private static LegalStartTime MakeLst(double blockLength, int[] starts) =>
        new LegalStartTime { BlockLength = blockLength, StartTimes = starts.ToList() };

    [Fact]
    public void SpecSolver_AllFixed_FindsRooms()
    {
        // Scenario 3: M 180min at 0800. All fields set except room.
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var lst = new[] { MakeLst(3.0, [480]) };
        var patterns = new[] { MakePattern("mwf", "MWF", [1, 3, 5]) };
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var specs = new[] { new MeetingSpec(0, Day: 1, DurationMinutes: 180, StartMinutes: 480, RoomTypeId: null) };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        Assert.NotEmpty(solutions);
        Assert.All(solutions, sol => Assert.Contains(sol.Slots, s => s.RoomId == "r1" && s.Day == 1));
    }

    [Fact]
    public void SpecSolver_OpenStart_EnumeratesLegalStarts()
    {
        // Scenario 1-like: MWF 90min, days fixed, start not set.
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var lst = new[] { MakeLst(1.5, [480, 570]) }; // 0800, 0930
        var patterns = new[] { MakePattern("mwf", "MWF", [1, 3, 5]) };
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var specs = new MeetingSpec[]
        {
            new(0, Day: 1, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
            new(1, Day: 3, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
            new(2, Day: 5, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
        };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        Assert.NotEmpty(solutions);
        // Tier 1 should have same room, same start across all 3 days.
        var tier1 = solutions.Where(s => s.Tier == SolutionTier.SameRoomSameTime).ToList();
        Assert.NotEmpty(tier1);
    }

    [Fact]
    public void SpecSolver_OpenDays_PrefersPatterns()
    {
        // Scenario 2: 2 meetings, 90min each, no days specified.
        // Patterns: TR (days 2,4). Available days: 1,2,3,4,5.
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var lst = new[] { MakeLst(1.5, [480]) };
        var patterns = new[] { MakePattern("tr", "TR", [2, 4]) };
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var specs = new MeetingSpec[]
        {
            new(0, Day: null, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
            new(1, Day: null, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null),
        };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        Assert.NotEmpty(solutions);
        // First solution should be pattern-matched (TR).
        var first = solutions[0];
        Assert.True(first.IsPatternMatch);
        var days = first.Slots.Select(s => s.Day).OrderBy(d => d).ToList();
        Assert.Equal([2, 4], days);
    }

    [Fact]
    public void SpecSolver_MixedFixedAndOpen_CompletesPattern()
    {
        // Scenario 4-like: T fixed, R fixed, 1 open day.
        // Pattern MWF doesn't fit. Pattern TWR (2,4,?) doesn't exist.
        // But we add a pattern "TWRF" with days [2,4,5] → should complete with F=5.
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var lst = new[] { MakeLst(1.5, [480]) };
        var patterns = new[]
        {
            MakePattern("mwf", "MWF", [1, 3, 5]),
            MakePattern("twrf", "TWThF", [2, 3, 4, 5])  // nope, this is 4 days but we only have 3 specs total
        };
        var index = new OccupancyIndex();
        index.Build([], [], null);

        // T(2) at 80min, R(4) at 80min, 1 open at 180min.
        var specs = new MeetingSpec[]
        {
            new(0, Day: 2, DurationMinutes: 80, StartMinutes: null, RoomTypeId: null),
            new(1, Day: 4, DurationMinutes: 80, StartMinutes: null, RoomTypeId: null),
            new(2, Day: null, DurationMinutes: 180, StartMinutes: null, RoomTypeId: null),
        };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        // Should find solutions with the open spec assigned to some day (M, W, or F).
        Assert.NotEmpty(solutions);
        var openDay = solutions[0].Slots.FirstOrDefault(s => s.DurationMinutes == 180);
        Assert.NotNull(openDay);
        Assert.DoesNotContain(openDay.Day, new[] { 2, 4 }); // not T or R
    }

    [Fact]
    public void SpecSolver_RoomBusy_ExcludesConflicts()
    {
        // Room r1 is busy Mon 0800-0930. Spec wants Mon 90min at 0800.
        var rooms = new[] { MakeRoom("r1", "A", "101"), MakeRoom("r2", "B", "201") };
        var lst = new[] { MakeLst(1.5, [480, 570]) };
        var patterns = Array.Empty<BlockPattern>();
        var blocking = MakeSection("s1", "r1", day: 1, start: 480, duration: 90);
        var index = new OccupancyIndex();
        index.Build([blocking], [], null);

        var specs = new[] { new MeetingSpec(0, Day: 1, DurationMinutes: 90, StartMinutes: 480, RoomTypeId: null) };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        // r1 at 0800 is busy; only r2 should appear at 0800.
        Assert.All(solutions.Where(s => s.Slots.Any(sl => sl.StartMinutes == 480)),
            sol => Assert.All(sol.Slots, s => Assert.Equal("r2", s.RoomId)));
    }

    [Fact]
    public void SpecSolver_CustomDuration_FallsBackToAllStarts()
    {
        // 80min does not match any configured block length (only 1.5hr = 90min exists).
        // Solver should fall back to all start times where 80min fits.
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var lst = new[] { MakeLst(1.5, [480, 570]) };
        var patterns = Array.Empty<BlockPattern>();
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var specs = new[] { new MeetingSpec(0, Day: 1, DurationMinutes: 80, StartMinutes: null, RoomTypeId: null) };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        Assert.NotEmpty(solutions);
    }

    [Fact]
    public void SpecSolver_FullySpecified_AlsoSuggestsAlternatives()
    {
        // All fields set (day=1, start=480, dur=90). Should find room for that time
        // AND suggest alternative times.
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var lst = new[] { MakeLst(1.5, [480, 570]) };
        var patterns = Array.Empty<BlockPattern>();
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var specs = new[] { new MeetingSpec(0, Day: 1, DurationMinutes: 90, StartMinutes: 480, RoomTypeId: null) };

        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        // Should have the exact match (0800) AND the alternative (0930).
        var starts = solutions.SelectMany(s => s.Slots).Select(s => s.StartMinutes).Distinct().ToList();
        Assert.Contains(480, starts);
        Assert.Contains(570, starts);
    }

    [Fact]
    public void SpecSolver_NoRooms_ReturnsEmpty()
    {
        var index = new OccupancyIndex();
        index.Build([], [], null);

        var specs = new[] { new MeetingSpec(0, Day: 1, DurationMinutes: 90, StartMinutes: null, RoomTypeId: null) };
        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, Array.Empty<Room>(), index,
            new[] { MakeLst(1.5, [480]) }, Array.Empty<BlockPattern>());

        Assert.Empty(solutions);
    }

    [Fact]
    public void SpecSolver_EmptySpecs_ReturnsEmpty()
    {
        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(Array.Empty<MeetingSpec>(),
            new[] { MakeRoom("r1", "A", "101") },
            new OccupancyIndex(),
            new[] { MakeLst(1.5, [480]) },
            Array.Empty<BlockPattern>());

        Assert.Empty(solutions);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Spec-based solver: frequency-aware room sharing
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpecSolver_OddVsEven_SameRoomTimeAvailable()
    {
        // Room A 101 is booked Friday 1300–1420 on odd weeks.
        var occupant = new Section
        {
            Id = "s1",
            Schedule = [new SectionDaySchedule { Day = 5, StartMinutes = 780, DurationMinutes = 80, RoomId = "r1", Frequency = "odd" }]
        };
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var index = new OccupancyIndex();
        index.Build([occupant], [], null);

        var lst = new[] { MakeLst(80.0 / 60.0, [780]) };
        var patterns = Array.Empty<BlockPattern>();

        // Query: Friday 1300–1420 on even weeks — should find Room A 101.
        var specs = new[] { new MeetingSpec(0, Day: 5, DurationMinutes: 80, StartMinutes: 780, RoomTypeId: null, Frequency: "even") };
        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        Assert.NotEmpty(solutions);
        Assert.Contains(solutions, sol => sol.Slots.Any(s => s.RoomId == "r1" && s.Day == 5 && s.StartMinutes == 780));
    }

    [Fact]
    public void SpecSolver_SameFrequency_RoomBlocked()
    {
        // Room A 101 is booked Friday 1300–1420 on odd weeks.
        var occupant = new Section
        {
            Id = "s1",
            Schedule = [new SectionDaySchedule { Day = 5, StartMinutes = 780, DurationMinutes = 80, RoomId = "r1", Frequency = "odd" }]
        };
        var rooms = new[] { MakeRoom("r1", "A", "101") };
        var index = new OccupancyIndex();
        index.Build([occupant], [], null);

        var lst = new[] { MakeLst(80.0 / 60.0, [780]) };
        var patterns = Array.Empty<BlockPattern>();

        // Query: Friday 1300–1420 on odd weeks — room should be blocked.
        var specs = new[] { new MeetingSpec(0, Day: 5, DurationMinutes: 80, StartMinutes: 780, RoomTypeId: null, Frequency: "odd") };
        var svc = new RoomAvailabilityService();
        var solutions = svc.GenerateSolutionsFromSpecs(specs, rooms, index, lst, patterns);

        Assert.Empty(solutions);
    }
}

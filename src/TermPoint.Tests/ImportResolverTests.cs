using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

public class ImportResolverTests
{
    private readonly ImportResolver _resolver = new();

    // ── Full Section Resolution ────────────────────────────────────────────────

    [Fact]
    public void Resolve_FullyResolvableSection_PopulatesAllIds()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var room = MakeRoom("Science Building", "204");
        var campus = new Campus { Id = "c1", Name = "Main Campus" };
        var sectionType = MakeSev("st1", "Lecture");
        var tag = MakeSev("t1", "Upper Level");
        var meetingType = MakeSev("mt1", "In Person");

        var index = ImportResolutionIndex.Build(
            [instructor], [room], [campus], [sectionType], [tag], [meetingType]);

        var section = MakeSharedSection(
            instructors: [("Smith, John", "JRS")],
            campusName: "Main Campus",
            sectionTypeName: "Lecture",
            tagNames: ["Upper Level"],
            meetings: [MakeMeeting("Science Building", "204", "In Person")]);

        var summary = _resolver.Resolve([section], index);

        Assert.Equal(instructor.Id, Assert.Single(section.InstructorAssignments).InstructorId);
        Assert.Equal("c1", section.CampusId);
        Assert.Equal("st1", section.SectionTypeId);
        Assert.Equal("t1", Assert.Single(section.TagIds));
        Assert.Equal(room.Id, section.Schedule[0].RoomId);
        Assert.Equal("mt1", section.Schedule[0].MeetingTypeId);

        Assert.Equal(1, summary.ResolvedInstructorCount);
        Assert.Equal(0, summary.UnresolvedInstructorCount);
        Assert.Equal(1, summary.ResolvedRoomCount);
        Assert.Equal(0, summary.UnresolvedRoomCount);
        Assert.Equal(1, summary.ResolvedTagCount);
        Assert.Equal(0, summary.UnresolvedTagCount);
        Assert.True(summary.ResolvedCampus);
        Assert.True(summary.ResolvedSectionType);
        Assert.Equal(1, summary.ResolvedMeetingTypeCount);
        Assert.Equal(0, summary.UnresolvedMeetingTypeCount);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void Resolve_MixedResolvedUnresolved_CorrectSummaryCounts()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var tag1 = MakeSev("t1", "Upper Level");
        // tag2 "Pre-Med Required" is NOT in local data

        var index = ImportResolutionIndex.Build(
            [instructor], [], [], [], [tag1], []);

        var section = MakeSharedSection(
            instructors: [("Smith, John", "JRS"), ("Doe, Jane", "JD")],
            tagNames: ["Upper Level", "Pre-Med Required"]);

        var summary = _resolver.Resolve([section], index);

        Assert.Single(section.InstructorAssignments);
        Assert.Equal(instructor.Id, section.InstructorAssignments[0].InstructorId);

        Assert.Equal(1, summary.ResolvedInstructorCount);
        Assert.Equal(1, summary.UnresolvedInstructorCount);
        Assert.Equal(1, summary.ResolvedTagCount);
        Assert.Equal(1, summary.UnresolvedTagCount);
    }

    [Fact]
    public void Resolve_DistinctNameCounting_SameInstructorOnTwoSections_CountedOnce()
    {
        var instructor = MakeInstructor("Smith", "John", "JRS");
        var index = ImportResolutionIndex.Build([instructor], [], [], [], [], []);

        var section1 = MakeSharedSection(
            instructors: [("Smith, John", "JRS")]);
        var section2 = MakeSharedSection(
            instructors: [("Smith, John", "JRS")]);

        var summary = _resolver.Resolve([section1, section2], index);

        // Same instructor appears on two sections, but distinct name count is 1
        Assert.Equal(1, summary.ResolvedInstructorCount);
        Assert.Equal(0, summary.UnresolvedInstructorCount);
    }

    // ── Instructor Resolution Details ──────────────────────────────────────────

    [Fact]
    public void Resolve_MultipleInstructorsPerSection_AllResolved()
    {
        var inst1 = MakeInstructor("Smith", "John", "JRS");
        var inst2 = MakeInstructor("Doe", "Jane", "JD");
        var index = ImportResolutionIndex.Build([inst1, inst2], [], [], [], [], []);

        var section = MakeSharedSection(
            instructors: [("Smith, John", "JRS"), ("Doe, Jane", "JD")]);

        var summary = _resolver.Resolve([section], index);

        Assert.Equal(2, section.InstructorAssignments.Count);
        Assert.Contains(section.InstructorAssignments, a => a.InstructorId == inst1.Id);
        Assert.Contains(section.InstructorAssignments, a => a.InstructorId == inst2.Id);
        Assert.Equal(2, summary.ResolvedInstructorCount);
    }

    [Fact]
    public void Resolve_AmbiguousInstructor_ProducesWarning()
    {
        var inst1 = MakeInstructor("Smith", "John", "JS");
        var inst2 = MakeInstructor("Smith", "John", "JS");
        var index = ImportResolutionIndex.Build([inst1, inst2], [], [], [], [], []);

        var section = MakeSharedSection(
            instructors: [("Smith, John", "JS")]);

        var summary = _resolver.Resolve([section], index);

        Assert.Empty(section.InstructorAssignments);
        Assert.Equal(0, summary.ResolvedInstructorCount);
        Assert.Equal(1, summary.UnresolvedInstructorCount);
        Assert.Single(summary.Warnings);
        Assert.Contains("Ambiguous", summary.Warnings[0]);
    }

    [Fact]
    public void Resolve_NoInstructors_LeavesAssignmentsEmpty()
    {
        var index = ImportResolutionIndex.Build([], [], [], [], [], []);

        var section = MakeSharedSection();

        var summary = _resolver.Resolve([section], index);

        Assert.Empty(section.InstructorAssignments);
        Assert.Equal(0, summary.ResolvedInstructorCount);
        Assert.Equal(0, summary.UnresolvedInstructorCount);
    }

    // ── Room Resolution Per-Meeting ────────────────────────────────────────────

    [Fact]
    public void Resolve_DifferentRoomsPerMeeting_ResolvedIndependently()
    {
        var room1 = MakeRoom("Science Building", "204");
        var room2 = MakeRoom("Science Building", "110");
        var index = ImportResolutionIndex.Build([], [room1, room2], [], [], [], []);

        var section = MakeSharedSection(meetings: [
            MakeMeeting("Science Building", "204", null),
            MakeMeeting("Science Building", "110", null)
        ]);

        var summary = _resolver.Resolve([section], index);

        Assert.Equal(room1.Id, section.Schedule[0].RoomId);
        Assert.Equal(room2.Id, section.Schedule[1].RoomId);
        Assert.Equal(2, summary.ResolvedRoomCount);
        Assert.Equal(0, summary.UnresolvedRoomCount);
    }

    [Fact]
    public void Resolve_RoomUnresolved_RoomIdStaysNull()
    {
        var index = ImportResolutionIndex.Build([], [], [], [], [], []);

        var section = MakeSharedSection(meetings: [
            MakeMeeting("Unknown Building", "999", null)
        ]);

        var summary = _resolver.Resolve([section], index);

        Assert.Null(section.Schedule[0].RoomId);
        Assert.Equal(0, summary.ResolvedRoomCount);
        Assert.Equal(1, summary.UnresolvedRoomCount);
    }

    [Fact]
    public void Resolve_SameRoomOnMultipleMeetings_CountedOnceAsDistinct()
    {
        var room = MakeRoom("Science Building", "204");
        var index = ImportResolutionIndex.Build([], [room], [], [], [], []);

        var section = MakeSharedSection(meetings: [
            MakeMeeting("Science Building", "204", null),
            MakeMeeting("Science Building", "204", null)
        ]);

        var summary = _resolver.Resolve([section], index);

        Assert.Equal(room.Id, section.Schedule[0].RoomId);
        Assert.Equal(room.Id, section.Schedule[1].RoomId);
        Assert.Equal(1, summary.ResolvedRoomCount);
    }

    // ── Tag Resolution ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_MultipleTags_PartialMatch()
    {
        var tag1 = MakeSev("t1", "Upper Level");
        // "Pre-Med Required" not in local data
        var index = ImportResolutionIndex.Build([], [], [], [], [tag1], []);

        var section = MakeSharedSection(tagNames: ["Upper Level", "Pre-Med Required"]);

        var summary = _resolver.Resolve([section], index);

        Assert.Single(section.TagIds);
        Assert.Equal("t1", section.TagIds[0]);
        Assert.Equal(1, summary.ResolvedTagCount);
        Assert.Equal(1, summary.UnresolvedTagCount);
    }

    [Fact]
    public void Resolve_AllTagsMatch_AllAdded()
    {
        var tag1 = MakeSev("t1", "Upper Level");
        var tag2 = MakeSev("t2", "Pre-Med Required");
        var index = ImportResolutionIndex.Build([], [], [], [], [tag1, tag2], []);

        var section = MakeSharedSection(tagNames: ["Upper Level", "Pre-Med Required"]);

        var summary = _resolver.Resolve([section], index);

        Assert.Equal(2, section.TagIds.Count);
        Assert.Contains("t1", section.TagIds);
        Assert.Contains("t2", section.TagIds);
        Assert.Equal(2, summary.ResolvedTagCount);
        Assert.Equal(0, summary.UnresolvedTagCount);
    }

    [Fact]
    public void Resolve_NoTags_LeavesTagIdsEmpty()
    {
        var index = ImportResolutionIndex.Build([], [], [], [], [], []);

        var section = MakeSharedSection();

        _resolver.Resolve([section], index);

        Assert.Empty(section.TagIds);
    }

    // ── Campus and SectionType ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_CampusResolved_SetsCampusId()
    {
        var campus = new Campus { Id = "c1", Name = "Main Campus" };
        var index = ImportResolutionIndex.Build([], [], [campus], [], [], []);

        var section = MakeSharedSection(campusName: "Main Campus");

        var summary = _resolver.Resolve([section], index);

        Assert.Equal("c1", section.CampusId);
        Assert.True(summary.ResolvedCampus);
    }

    [Fact]
    public void Resolve_CampusUnresolved_CampusIdStaysNull()
    {
        var index = ImportResolutionIndex.Build([], [], [], [], [], []);

        var section = MakeSharedSection(campusName: "Unknown Campus");

        var summary = _resolver.Resolve([section], index);

        Assert.Null(section.CampusId);
        Assert.False(summary.ResolvedCampus);
    }

    [Fact]
    public void Resolve_SectionTypeResolved_SetsSectionTypeId()
    {
        var sev = MakeSev("st1", "Lecture");
        var index = ImportResolutionIndex.Build([], [], [], [sev], [], []);

        var section = MakeSharedSection(sectionTypeName: "Lecture");

        var summary = _resolver.Resolve([section], index);

        Assert.Equal("st1", section.SectionTypeId);
        Assert.True(summary.ResolvedSectionType);
    }

    // ── Meeting Type ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_MeetingTypeResolved_SetsMeetingTypeId()
    {
        var sev = MakeSev("mt1", "In Person");
        var index = ImportResolutionIndex.Build([], [], [], [], [], [sev]);

        var section = MakeSharedSection(meetings: [
            MakeMeeting(null, null, "In Person")
        ]);

        var summary = _resolver.Resolve([section], index);

        Assert.Equal("mt1", section.Schedule[0].MeetingTypeId);
        Assert.Equal(1, summary.ResolvedMeetingTypeCount);
        Assert.Equal(0, summary.UnresolvedMeetingTypeCount);
    }

    [Fact]
    public void Resolve_MeetingTypeUnresolved_MeetingTypeIdStaysNull()
    {
        var index = ImportResolutionIndex.Build([], [], [], [], [], []);

        var section = MakeSharedSection(meetings: [
            MakeMeeting(null, null, "Online")
        ]);

        var summary = _resolver.Resolve([section], index);

        Assert.Null(section.Schedule[0].MeetingTypeId);
        Assert.Equal(0, summary.ResolvedMeetingTypeCount);
        Assert.Equal(1, summary.UnresolvedMeetingTypeCount);
    }

    // ── Empty Input ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_EmptySectionList_ReturnsEmptySummary()
    {
        var index = ImportResolutionIndex.Build([], [], [], [], [], []);

        var summary = _resolver.Resolve([], index);

        Assert.Equal(0, summary.ResolvedInstructorCount);
        Assert.Equal(0, summary.UnresolvedInstructorCount);
        Assert.Equal(0, summary.ResolvedRoomCount);
        Assert.Equal(0, summary.UnresolvedRoomCount);
        Assert.False(summary.ResolvedCampus);
        Assert.False(summary.ResolvedSectionType);
        Assert.Empty(summary.Warnings);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Instructor MakeInstructor(string last, string first, string initials) =>
        new() { Id = Guid.NewGuid().ToString(), LastName = last, FirstName = first, Initials = initials };

    private static Room MakeRoom(string building, string roomNumber) =>
        new() { Id = Guid.NewGuid().ToString(), Building = building, RoomNumber = roomNumber };

    private static SchedulingEnvironmentValue MakeSev(string id, string name) =>
        new() { Id = id, Name = name };

    private static SectionDaySchedule MakeMeeting(
        string? building, string? roomNumber, string? meetingTypeName) =>
        new()
        {
            Day = 1,
            StartMinutes = 480,
            DurationMinutes = 50,
            ImportedBuilding = building,
            ImportedRoomNumber = roomNumber,
            ImportedMeetingTypeName = meetingTypeName
        };

    /// <summary>
    /// Creates a shared section with the specified imported properties.
    /// All parameters are optional and default to empty/null.
    /// </summary>
    private static Section MakeSharedSection(
        List<(string Name, string Initials)>? instructors = null,
        string? campusName = null,
        string? sectionTypeName = null,
        List<string>? tagNames = null,
        List<SectionDaySchedule>? meetings = null)
    {
        return new Section
        {
            Id = Guid.NewGuid().ToString(),
            IsShared = true,
            SourceLabel = "Test Department",
            DisplayCourseCode = "TEST101",
            SectionCode = "A",
            DisplayInstructors = instructors,
            ImportedCampusName = campusName,
            ImportedSectionTypeName = sectionTypeName,
            ImportedTagNames = tagNames,
            Schedule = meetings ?? []
        };
    }
}

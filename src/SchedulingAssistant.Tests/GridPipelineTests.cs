using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.GridView;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for the internal static pipeline methods extracted from
/// <see cref="ScheduleGridViewModel.ReloadCore"/>.
///
/// Covered methods (all <c>internal static</c>, hence independently testable):
/// <list type="bullet">
///   <item><see cref="ScheduleGridViewModel.BuildSectionLabel"/></item>
///   <item><see cref="ScheduleGridViewModel.ComputeOverlayMatchedSectionIds"/></item>
///   <item><see cref="ScheduleGridViewModel.BuildFilteredBlocks"/></item>
///   <item><see cref="ScheduleGridViewModel.BuildOverlayBlocks"/></item>
///   <item><see cref="ScheduleGridViewModel.DeduplicateBlocks"/></item>
/// </list>
///
/// Private instance methods (<c>BuildLookups</c>, <c>BuildCommitmentBlocks</c>,
/// <c>UpdateDisplayProperties</c>, etc.) require a live database and are tested
/// indirectly through integration/UI tests.
/// </summary>
public class GridPipelineTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="Section"/> with sensible defaults.</summary>
    private static Section Sec(
        string id             = "s1",
        string? courseId      = "c1",
        string sectionCode    = "A",
        string semesterId     = "sem1",
        string? campusId      = null,
        string? sectionTypeId = null,
        List<string>? tagIds          = null,
        List<string>? instructorIds   = null,
        List<SectionDaySchedule>? schedule = null) =>
        new()
        {
            Id            = id,
            CourseId      = courseId,
            SectionCode   = sectionCode,
            SemesterId    = semesterId,
            CampusId      = campusId,
            SectionTypeId = sectionTypeId,
            TagIds        = tagIds ?? [],
            InstructorAssignments = (instructorIds ?? [])
                .Select(iid => new InstructorAssignment { InstructorId = iid })
                .ToList(),
            Schedule = schedule ?? []
        };

    /// <summary>Creates a <see cref="SectionDaySchedule"/> with sensible defaults.</summary>
    private static SectionDaySchedule Slot(
        int day, int startMinutes, int durationMinutes = 90,
        string? roomId = null, string? meetingTypeId = null) =>
        new()
        {
            Day             = day,
            StartMinutes    = startMinutes,
            DurationMinutes = durationMinutes,
            RoomId          = roomId,
            MeetingTypeId   = meetingTypeId
        };

    /// <summary>
    /// Creates a <see cref="FilterSnapshot"/> with all filters inactive by default.
    /// Pass named arguments to activate specific filter dimensions.
    /// </summary>
    private static FilterSnapshot Snap(
        IEnumerable<string>? instructors  = null,
        IEnumerable<string>? rooms        = null,
        IEnumerable<string>? subjects     = null,
        IEnumerable<string>? campuses     = null,
        IEnumerable<string>? sectionTypes = null,
        IEnumerable<string>? tags         = null,
        IEnumerable<string>? meetingTypes = null,
        IEnumerable<string>? levels       = null,
        bool notStaffed   = false,
        bool unroomed     = false,
        bool hasOverlay   = false,
        string? overlayType = null,
        string? overlayId   = null) =>
        new(
            new HashSet<string>(instructors  ?? []),
            new HashSet<string>(rooms        ?? []),
            new HashSet<string>(subjects     ?? []),
            new HashSet<string>(campuses     ?? []),
            new HashSet<string>(sectionTypes ?? []),
            new HashSet<string>(tags         ?? []),
            new HashSet<string>(meetingTypes ?? []),
            new HashSet<string>(levels       ?? []),
            notStaffed, unroomed, hasOverlay, overlayType, overlayId);

    /// <summary>
    /// Creates a <see cref="GridLookups"/> with empty supporting dictionaries by default.
    /// Pass named arguments to populate specific lookups for a test.
    /// </summary>
    private static GridLookups Lookups(
        IReadOnlyList<Section>?          sections         = null,
        Dictionary<string, Course>?      courses          = null,
        Dictionary<string, Instructor>?  instructors      = null,
        Dictionary<string, string>?      semesterIdToName = null) =>
        new(
            Sections:         sections ?? [],
            Courses:          courses  ?? new(),
            Instructors:      instructors ?? new(),
            Rooms:            new Dictionary<string, Room>(),
            Subjects:         new Dictionary<string, Subject>(),
            Campuses:         new Dictionary<string, SectionPropertyValue>(),
            SectionTypes:     new Dictionary<string, SectionPropertyValue>(),
            Tags:             new Dictionary<string, SectionPropertyValue>(),
            MeetingTypes:     new Dictionary<string, SectionPropertyValue>(),
            Levels:           new Dictionary<string, string>(),
            SemesterIdToName: semesterIdToName ?? new());

    // ═════════════════════════════════════════════════════════════════════════
    // BuildSectionLabel
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildSectionLabel_HasCourseAndInstructor_FormatsLabelAndInitials()
    {
        var section = Sec(sectionCode: "A", courseId: "c1", instructorIds: ["i1"]);
        var courses     = new Dictionary<string, Course>     { ["c1"] = new() { Id = "c1", CalendarCode = "HIST101" } };
        var instructors = new Dictionary<string, Instructor> { ["i1"] = new() { Id = "i1", Initials = "JRS" } };

        var (label, initials) = ScheduleGridViewModel.BuildSectionLabel(section, courses, instructors);

        Assert.Equal("HIST101 A", label);
        Assert.Equal("JRS", initials);
    }

    [Fact]
    public void BuildSectionLabel_NoCourseId_ReturnsJustSectionCode()
    {
        var section = Sec(courseId: null, sectionCode: "B");

        var (label, initials) = ScheduleGridViewModel.BuildSectionLabel(
            section, new Dictionary<string, Course>(), new Dictionary<string, Instructor>());

        Assert.Equal("B", label);
        Assert.Equal(string.Empty, initials);
    }

    [Fact]
    public void BuildSectionLabel_CourseIdNotInLookup_ReturnsJustSectionCode()
    {
        // CourseId is set but does not resolve in the lookup.
        var section = Sec(courseId: "missing", sectionCode: "C");

        var (label, _) = ScheduleGridViewModel.BuildSectionLabel(
            section, new Dictionary<string, Course>(), new Dictionary<string, Instructor>());

        Assert.Equal("C", label);
    }

    [Fact]
    public void BuildSectionLabel_NoInstructors_ReturnsEmptyInitials()
    {
        var section = Sec(courseId: null, instructorIds: []);

        var (_, initials) = ScheduleGridViewModel.BuildSectionLabel(
            section, new Dictionary<string, Course>(), new Dictionary<string, Instructor>());

        Assert.Equal(string.Empty, initials);
    }

    [Fact]
    public void BuildSectionLabel_MultipleInstructors_JoinsInitialsWithSpace()
    {
        var section = Sec(courseId: null, instructorIds: ["i1", "i2"]);
        var instructors = new Dictionary<string, Instructor>
        {
            ["i1"] = new() { Initials = "JRS" },
            ["i2"] = new() { Initials = "MKL" }
        };

        var (_, initials) = ScheduleGridViewModel.BuildSectionLabel(
            section, new Dictionary<string, Course>(), instructors);

        Assert.Equal("JRS MKL", initials);
    }

    [Fact]
    public void BuildSectionLabel_InstructorIdNotInLookup_SkipsIt()
    {
        // Three instructor IDs; the middle one has no entry in the lookup.
        var section = Sec(courseId: null, instructorIds: ["i1", "missing", "i3"]);
        var instructors = new Dictionary<string, Instructor>
        {
            ["i1"] = new() { Initials = "JRS" },
            ["i3"] = new() { Initials = "ABC" }
        };

        var (_, initials) = ScheduleGridViewModel.BuildSectionLabel(
            section, new Dictionary<string, Course>(), instructors);

        // "missing" is silently skipped; remaining initials are joined normally.
        Assert.Equal("JRS ABC", initials);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ComputeOverlayMatchedSectionIds
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeOverlayMatchedIds_NoOverlay_ReturnsEmpty()
    {
        var sections = new List<Section> { Sec(instructorIds: ["i1"]) };

        var result = ScheduleGridViewModel.ComputeOverlayMatchedSectionIds(sections, Snap());

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeOverlayMatchedIds_RoomOverlay_ReturnsEmpty()
    {
        // Room overlays are resolved per-meeting inside BuildFilteredBlocks, not here.
        var sections = new List<Section> { Sec() };
        var snap = Snap(hasOverlay: true, overlayType: "Room", overlayId: "r1");

        var result = ScheduleGridViewModel.ComputeOverlayMatchedSectionIds(sections, snap);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeOverlayMatchedIds_InstructorOverlay_ReturnsMatchingSections()
    {
        var match    = Sec("s1", instructorIds: ["i1"]);
        var noMatch  = Sec("s2", instructorIds: ["i2"]);
        var snap = Snap(hasOverlay: true, overlayType: "Instructor", overlayId: "i1");

        var result = ScheduleGridViewModel.ComputeOverlayMatchedSectionIds([match, noMatch], snap);

        Assert.Single(result);
        Assert.Contains("s1", result);
    }

    [Fact]
    public void ComputeOverlayMatchedIds_TagOverlay_ReturnsMatchingSections()
    {
        var match   = Sec("s1", tagIds: ["tag1", "tag2"]);
        var noMatch = Sec("s2", tagIds: ["tag3"]);
        var snap = Snap(hasOverlay: true, overlayType: "Tag", overlayId: "tag1");

        var result = ScheduleGridViewModel.ComputeOverlayMatchedSectionIds([match, noMatch], snap);

        Assert.Single(result);
        Assert.Contains("s1", result);
    }

    [Fact]
    public void ComputeOverlayMatchedIds_InstructorOverlay_NoSectionMatches_ReturnsEmpty()
    {
        var section = Sec("s1", instructorIds: ["i2"]);
        var snap = Snap(hasOverlay: true, overlayType: "Instructor", overlayId: "i1");

        var result = ScheduleGridViewModel.ComputeOverlayMatchedSectionIds([section], snap);

        Assert.Empty(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BuildFilteredBlocks
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildFilteredBlocks_NoFilter_ReturnsAllMeetings()
    {
        var section = Sec(schedule: [Slot(1, 540), Slot(3, 600)]);
        var lookups = Lookups(semesterIdToName: new() { ["sem1"] = "Fall 2025" });

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], Snap(), lookups, []);

        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.False(b.IsOverlay));
    }

    [Fact]
    public void BuildFilteredBlocks_InstructorFilter_ExcludesNonMatchingSection()
    {
        var matching    = Sec("s1", instructorIds: ["i1"], schedule: [Slot(1, 540)]);
        var nonMatching = Sec("s2", instructorIds: ["i2"], schedule: [Slot(1, 600)]);
        var snap = Snap(instructors: ["i1"]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([matching, nonMatching], snap, Lookups(), []);

        Assert.Single(result);
        Assert.Equal("s1", ((SectionMeetingBlock)result[0]).SectionId);
    }

    [Fact]
    public void BuildFilteredBlocks_NotStaffedFilter_IncludesOnlyUnstaffedSections()
    {
        var staffed   = Sec("s1", instructorIds: ["i1"], schedule: [Slot(1, 540)]);
        var unstaffed = Sec("s2", instructorIds: [],     schedule: [Slot(1, 600)]);
        var snap = Snap(notStaffed: true);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([staffed, unstaffed], snap, Lookups(), []);

        Assert.Single(result);
        Assert.Equal("s2", ((SectionMeetingBlock)result[0]).SectionId);
    }

    [Fact]
    public void BuildFilteredBlocks_CampusFilter_ExcludesWrongCampus()
    {
        var match   = Sec("s1", campusId: "campus1", schedule: [Slot(1, 540)]);
        var noMatch = Sec("s2", campusId: "campus2", schedule: [Slot(1, 540)]);
        var snap = Snap(campuses: ["campus1"]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([match, noMatch], snap, Lookups(), []);

        Assert.Single(result);
        Assert.Equal("s1", ((SectionMeetingBlock)result[0]).SectionId);
    }

    [Fact]
    public void BuildFilteredBlocks_RoomFilter_ExcludesMeetingsInOtherRooms()
    {
        // One section with two meetings: only the one in the filtered room passes.
        var section = Sec(schedule: [
            Slot(1, 540, roomId: "r1"),  // passes room filter
            Slot(3, 540, roomId: "r2")   // excluded
        ]);
        var snap = Snap(rooms: ["r1"]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], snap, Lookups(), []);

        Assert.Single(result);
        Assert.Equal(1, ((SectionMeetingBlock)result[0]).Day);
    }

    [Fact]
    public void BuildFilteredBlocks_UnroomedFilter_IncludesOnlyMeetingsWithNoRoom()
    {
        var section = Sec(schedule: [
            Slot(1, 540, roomId: null),  // no room → passes
            Slot(3, 540, roomId: "r1")   // has room → excluded
        ]);
        var snap = Snap(unroomed: true);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], snap, Lookups(), []);

        Assert.Single(result);
        Assert.Equal(1, ((SectionMeetingBlock)result[0]).Day);
    }

    [Fact]
    public void BuildFilteredBlocks_TagFilter_RequiresAllSelectedTags_AndLogic()
    {
        // Tag filter is AND: a section must carry ALL selected tags to pass.
        var hasAll     = Sec("s1", tagIds: ["t1", "t2", "t3"], schedule: [Slot(1, 540)]);
        var missingOne = Sec("s2", tagIds: ["t1"],             schedule: [Slot(1, 540)]);
        var snap = Snap(tags: ["t1", "t2"]); // section must have both t1 AND t2

        var result = ScheduleGridViewModel.BuildFilteredBlocks([hasAll, missingOne], snap, Lookups(), []);

        Assert.Single(result);
        Assert.Equal("s1", ((SectionMeetingBlock)result[0]).SectionId);
    }

    [Fact]
    public void BuildFilteredBlocks_OverlayMatchedSection_SetsIsOverlayTrue()
    {
        var section    = Sec("s1", schedule: [Slot(1, 540)]);
        var overlayIds = new HashSet<string> { "s1" };

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], Snap(), Lookups(), overlayIds);

        Assert.Single(result);
        Assert.True(result[0].IsOverlay);
    }

    [Fact]
    public void BuildFilteredBlocks_NonOverlaySection_SetsIsOverlayFalse()
    {
        var section = Sec("s1", schedule: [Slot(1, 540)]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], Snap(), Lookups(), []);

        Assert.Single(result);
        Assert.False(result[0].IsOverlay);
    }

    [Fact]
    public void BuildFilteredBlocks_RoomOverlay_MeetingInOverlayRoom_SetsIsOverlayTrue()
    {
        // Room overlay is applied per-meeting: the meeting in the overlay room gets
        // IsOverlay=true even though the section itself is not in the overlay set.
        var section = Sec(schedule: [
            Slot(1, 540, roomId: "r1"),  // overlay room → IsOverlay=true
            Slot(3, 540, roomId: "r2")   // different room → IsOverlay=false
        ]);
        var snap = Snap(hasOverlay: true, overlayType: "Room", overlayId: "r1");

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], snap, Lookups(), []);

        Assert.Equal(2, result.Count);
        var day1 = result.OfType<SectionMeetingBlock>().First(b => b.Day == 1);
        var day3 = result.OfType<SectionMeetingBlock>().First(b => b.Day == 3);
        Assert.True(day1.IsOverlay);
        Assert.False(day3.IsOverlay);
    }

    [Fact]
    public void BuildFilteredBlocks_SubjectFilter_ExcludesWrongSubject()
    {
        // Course has SubjectId "subj2", but the filter is set to "subj1".
        var course  = new Course { Id = "c1", SubjectId = "subj2", CalendarCode = "MATH101" };
        var section = Sec(courseId: "c1", schedule: [Slot(1, 540)]);
        var lookups = Lookups(courses: new() { ["c1"] = course });
        var snap = Snap(subjects: ["subj1"]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], snap, lookups, []);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildFilteredBlocks_LevelFilter_ExcludesWrongLevel()
    {
        // "HIST201" → Level "2XX". Filter is set to "1XX" only.
        var course  = new Course { Id = "c1", SubjectId = "subj1", CalendarCode = "HIST201" };
        var section = Sec(courseId: "c1", schedule: [Slot(1, 540)]);
        var lookups = Lookups(courses: new() { ["c1"] = course });
        var snap = Snap(levels: ["1XX"]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], snap, lookups, []);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildFilteredBlocks_LevelFilter_IncludesMatchingLevel()
    {
        // "HIST101" → Level "1XX". Filter includes "1XX".
        var course  = new Course { Id = "c1", SubjectId = "subj1", CalendarCode = "HIST101" };
        var section = Sec(courseId: "c1", schedule: [Slot(1, 540)]);
        var lookups = Lookups(courses: new() { ["c1"] = course });
        var snap = Snap(levels: ["1XX"]);

        var result = ScheduleGridViewModel.BuildFilteredBlocks([section], snap, lookups, []);

        Assert.Single(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BuildOverlayBlocks
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildOverlayBlocks_NoOverlay_ReturnsEmpty()
    {
        var section = Sec(schedule: [Slot(1, 540)]);

        var result = ScheduleGridViewModel.BuildOverlayBlocks([section], Snap(), Lookups(), [], []);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildOverlayBlocks_InstructorOverlay_SectionAlreadyFiltered_NotDuplicated()
    {
        // s1 is in overlayMatchedIds but was already added by Pass 1.
        var section  = Sec("s1", instructorIds: ["i1"], schedule: [Slot(1, 540)]);
        var snap     = Snap(hasOverlay: true, overlayType: "Instructor", overlayId: "i1");
        var overlayIds = new HashSet<string> { "s1" };

        // Simulate a Pass 1 block with SectionId = "s1"
        var filteredBlock = new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem1", "");

        var result = ScheduleGridViewModel.BuildOverlayBlocks([section], snap, Lookups(), [filteredBlock], overlayIds);

        // s1 was already present → Pass 2 should add nothing.
        Assert.Empty(result);
    }

    [Fact]
    public void BuildOverlayBlocks_InstructorOverlay_SectionNotFiltered_AddsAllMeetings()
    {
        // s1 is in overlayMatchedIds but was NOT included by Pass 1 (filtered out).
        var section  = Sec("s1", instructorIds: ["i1"], schedule: [Slot(1, 540), Slot(3, 540)]);
        var snap     = Snap(hasOverlay: true, overlayType: "Instructor", overlayId: "i1");
        var overlayIds = new HashSet<string> { "s1" };
        var lookups  = Lookups(semesterIdToName: new() { ["sem1"] = "Fall 2025" });

        var result = ScheduleGridViewModel.BuildOverlayBlocks([section], snap, lookups, [], overlayIds);

        // Both meetings should be added with IsOverlay=true.
        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.True(b.IsOverlay));
    }

    [Fact]
    public void BuildOverlayBlocks_RoomOverlay_AddsMeetingsInOverlayRoom()
    {
        var section = Sec(schedule: [
            Slot(1, 540, roomId: "r1"),  // in overlay room → added
            Slot(3, 540, roomId: "r2")   // not in overlay room → skipped
        ]);
        var snap    = Snap(hasOverlay: true, overlayType: "Room", overlayId: "r1");
        var lookups = Lookups(semesterIdToName: new() { ["sem1"] = "Fall 2025" });

        var result = ScheduleGridViewModel.BuildOverlayBlocks([section], snap, lookups, [], []);

        Assert.Single(result);
        Assert.Equal(1, ((SectionMeetingBlock)result[0]).Day);
        Assert.True(result[0].IsOverlay);
    }

    [Fact]
    public void BuildOverlayBlocks_RoomOverlay_AlreadyAddedMeeting_Skipped()
    {
        // The meeting in "r1" was already added by Pass 1 — Pass 2 must not duplicate it.
        var section = Sec("s1", schedule: [Slot(1, 540, roomId: "r1")]);
        var snap    = Snap(hasOverlay: true, overlayType: "Room", overlayId: "r1");

        // Existing Pass 1 block covering the same (sectionId, day, start, end).
        var existingBlock = new SectionMeetingBlock(1, 540, 630, true, "A", "", "s1", "sem1", "");

        var result = ScheduleGridViewModel.BuildOverlayBlocks([section], snap, Lookups(), [existingBlock], []);

        Assert.Empty(result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DeduplicateBlocks
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeduplicateBlocks_NoDuplicates_ReturnsAll()
    {
        var blocks = new List<GridBlock>
        {
            new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem1", ""),
            new SectionMeetingBlock(3, 540, 630, false, "B", "", "s2", "sem1", "")
        };

        var result = ScheduleGridViewModel.DeduplicateBlocks(blocks);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DeduplicateBlocks_DuplicateSectionBlock_KeepsFirstOccurrence()
    {
        // Same (sectionId, semesterId, day, start, end) → duplicate.
        var first  = new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem1", "");
        var second = new SectionMeetingBlock(1, 540, 630, true,  "A", "", "s1", "sem1", "");

        var result = ScheduleGridViewModel.DeduplicateBlocks([first, second]);

        Assert.Single(result);
        Assert.False(result[0].IsOverlay); // first occurrence (IsOverlay=false) is kept
    }

    [Fact]
    public void DeduplicateBlocks_SameSectionIdDifferentDay_BothKept()
    {
        // Different day → different key → both blocks are unique.
        var blocks = new List<GridBlock>
        {
            new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem1", ""),
            new SectionMeetingBlock(3, 540, 630, false, "A", "", "s1", "sem1", "")
        };

        var result = ScheduleGridViewModel.DeduplicateBlocks(blocks);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DeduplicateBlocks_SameSectionIdDifferentSemester_BothKept()
    {
        // SemesterId is part of the dedup key — the same section ID in two semesters
        // must not be collapsed into one.
        var blocks = new List<GridBlock>
        {
            new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem1", ""),
            new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem2", "")
        };

        var result = ScheduleGridViewModel.DeduplicateBlocks(blocks);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DeduplicateBlocks_DuplicateCommitmentBlock_KeepsFirst()
    {
        var first  = new CommitmentBlock(1, 540, 630, "Meeting", "c1", "sem1", "Fall 2025");
        var second = new CommitmentBlock(1, 540, 630, "Meeting", "c1", "sem1", "Fall 2025");

        var result = ScheduleGridViewModel.DeduplicateBlocks([first, second]);

        Assert.Single(result);
    }

    [Fact]
    public void DeduplicateBlocks_FirstIsOverlayFalse_SecondIsOverlayTrue_PreservesFirst()
    {
        // Pass 1 emits the block with the correct IsOverlay value;
        // Pass 2 would re-add it as IsOverlay=true. Dedup must keep Pass 1's flag.
        var pass1 = new SectionMeetingBlock(1, 540, 630, false, "HIST101 A", "JRS", "s1", "sem1", "");
        var pass2 = new SectionMeetingBlock(1, 540, 630, true,  "HIST101 A", "JRS", "s1", "sem1", "");

        var result = ScheduleGridViewModel.DeduplicateBlocks([pass1, pass2]);

        Assert.Single(result);
        Assert.False(result[0].IsOverlay);
    }

    [Fact]
    public void DeduplicateBlocks_MixedBlockTypes_EachDedupedIndependently()
    {
        // SectionMeetingBlock and CommitmentBlock each have their own entity-ID keying,
        // so a section block and a commitment block can share start/end without collision.
        var section    = new SectionMeetingBlock(1, 540, 630, false, "A", "", "s1", "sem1", "");
        var commitment = new CommitmentBlock(    1, 540, 630, "Meeting", "c1", "sem1", "");

        var result = ScheduleGridViewModel.DeduplicateBlocks([section, section, commitment, commitment]);

        // One section block + one commitment block; duplicates of each removed.
        Assert.Equal(2, result.Count);
        Assert.Single(result.OfType<SectionMeetingBlock>());
        Assert.Single(result.OfType<CommitmentBlock>());
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of the schedule grid's block pipeline under a subject focus (spec item 20, step 20.4):
/// <see cref="ScheduleGridViewModel.BuildSectionBlocks"/> is the exact composition the grid runs (overlay pre-pass,
/// Pass 1 with the focus test, Pass 2), fed here with a real <see cref="FocusService"/> over a temp database so the
/// focus decision is the production one. Covers: All shows everything, a Subjects focus hides out-of-focus blocks,
/// None shows no section blocks, an Instructor / Room / Tag overlay still shows out-of-focus matches (flagged as
/// overlay), the ordinary filter works within the focus, and meetings are unaffected.
/// </summary>
public sealed class GridFocusTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Protects settings.json: constructing the semester context may touch it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    private readonly FocusService _focus;

    private readonly Course _biolCourse = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly Course _chemCourse = new() { Id = "c-chem", SubjectId = "subj-chem", CalendarCode = "CHEM101", Title = "Chem" };

    private readonly Section _bio;
    private readonly Section _chem;

    /// <summary>
    /// Seeds two courses (Biology, Chemistry) and builds one section of each: both taught by instructor "i1",
    /// both tagged "t1", both meeting in room "r1" (the Chemistry one also in "r2").
    /// </summary>
    public GridFocusTests()
    {
        var courseRepo = new CourseRepository(_fx.Db);
        courseRepo.Insert(_biolCourse);
        courseRepo.Insert(_chemCourse);

        // Only Matches is used, which needs just the course repository; the null key keeps settings untouched.
        _focus = new FocusService(null!, courseRepo, null!, null!, null!, new SemesterContext(), new SectionStore(), null);

        _bio = Section("s-bio", _biolCourse.Id, "B1", [Slot(0, 540, "r1")]);
        _chem = Section("s-chem", _chemCourse.Id, "C1", [Slot(1, 600, "r1"), Slot(2, 600, "r2")]);
    }

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>A section of the course with the standard instructor "i1" and tag "t1".</summary>
    private static Section Section(string id, string courseId, string code, List<SectionDaySchedule> slots) => new()
    {
        Id = id,
        CourseId = courseId,
        SectionCode = code,
        SemesterId = "sem1",
        TagIds = ["t1"],
        InstructorAssignments = [new InstructorAssignment { InstructorId = "i1" }],
        Schedule = slots,
    };

    /// <summary>A 90-minute meeting on the day, in the room.</summary>
    private static SectionDaySchedule Slot(int day, int start, string roomId) =>
        new() { Day = day, StartMinutes = start, DurationMinutes = 90, RoomId = roomId };

    /// <summary>A snapshot with no ordinary filter, optionally with an overlay or a subject filter.</summary>
    private static FilterSnapshot Snap(
        IEnumerable<string>? subjects = null, bool hasOverlay = false, string? overlayType = null, string? overlayId = null) =>
        new(
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(subjects ?? []), new HashSet<string>(),
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
            false, false, false, hasOverlay, overlayType, overlayId);

    /// <summary>Lookups holding the two courses (the focus test reads the course repository, not these).</summary>
    private GridLookups Lookups(IReadOnlyList<Section> sections)
    {
        var courses = new Dictionary<string, Course> { [_biolCourse.Id] = _biolCourse, [_chemCourse.Id] = _chemCourse };
        return new GridLookups(
            Sections: sections, Courses: courses, ActiveCourses: courses,
            Instructors: new Dictionary<string, Instructor>(), ActiveInstructors: new Dictionary<string, Instructor>(),
            Rooms: new Dictionary<string, Room>(), Subjects: new Dictionary<string, Subject>(),
            Campuses: new Dictionary<string, Campus>(),
            SectionTypes: new Dictionary<string, SchedulingEnvironmentValue>(),
            Tags: new Dictionary<string, SchedulingEnvironmentValue>(),
            MeetingTypes: new Dictionary<string, SchedulingEnvironmentValue>(),
            Levels: new Dictionary<string, string>(),
            SemesterIdToName: new Dictionary<string, string>(), SemesterIdToColor: new Dictionary<string, string>());
    }

    /// <summary>
    /// Runs the grid's section pipeline under the current focus and returns the section blocks as the grid would
    /// display them (Pass 1 then Pass 2, deduplicated).
    /// </summary>
    private List<SectionMeetingBlock> Run(FilterSnapshot snap)
    {
        var sections = new List<Section> { _bio, _chem };
        var lookups = Lookups(sections);
        Func<Section, bool>? inFocus = _focus.Current.Kind == FocusKind.All ? null : _focus.Matches;

        var (filtered, overlayOnly, _) = ScheduleGridViewModel.BuildSectionBlocks(sections, snap, lookups, inFocus);
        return ScheduleGridViewModel.DeduplicateBlocks(filtered.Concat(overlayOnly)).OfType<SectionMeetingBlock>().ToList();
    }

    /// <summary>The distinct section ids among the blocks, sorted.</summary>
    private static string[] SectionIds(IEnumerable<SectionMeetingBlock> blocks) =>
        blocks.Select(b => b.SectionId).Distinct().OrderBy(x => x).ToArray();

    // ── Focus on the ordinary blocks ──────────────────────────────────────────

    /// <summary>Control: under All every section's meetings are blocks, none flagged as overlay.</summary>
    [Fact]
    public void UnderAll_ShowsEverything()
    {
        var blocks = Run(Snap());

        Assert.Equal(["s-bio", "s-chem"], SectionIds(blocks));
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.False(b.IsOverlay));
    }

    /// <summary>A Subjects focus drops the blocks of out-of-focus sections.</summary>
    [Fact]
    public void UnderSubjects_HidesOutOfFocusBlocks()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects(["subj-biol"]));

        var blocks = Run(Snap());

        Assert.Equal(["s-bio"], SectionIds(blocks));
    }

    /// <summary>A focus on both subjects shows both.</summary>
    [Fact]
    public void UnderTwoSubjects_ShowsBoth()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects(["subj-biol", "subj-chem"]));

        Assert.Equal(["s-bio", "s-chem"], SectionIds(Run(Snap())));
    }

    /// <summary>None shows no section blocks at all.</summary>
    [Fact]
    public void UnderNone_ShowsNoSectionBlocks()
    {
        _focus.SetFocus(SubjectFocus.None);

        Assert.Empty(Run(Snap()));
    }

    /// <summary>The ordinary filter narrows within the focus: a Chemistry subject filter under a Biology focus shows nothing.</summary>
    [Fact]
    public void OrdinaryFilter_WorksWithinTheFocus()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects(["subj-biol"]));

        Assert.Empty(Run(Snap(subjects: ["subj-chem"])));
        Assert.Equal(["s-bio"], SectionIds(Run(Snap(subjects: ["subj-biol"]))));
    }

    // ── Overlays ignore the focus ─────────────────────────────────────────────

    /// <summary>An Instructor overlay still shows the instructor's out-of-focus section, flagged as overlay.</summary>
    [Fact]
    public void InstructorOverlay_ShowsOutOfFocusSections_AsOverlay()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects(["subj-biol"]));

        var blocks = Run(Snap(hasOverlay: true, overlayType: "Instructor", overlayId: "i1"));

        Assert.Equal(["s-bio", "s-chem"], SectionIds(blocks));
        Assert.All(blocks, b => Assert.True(b.IsOverlay));
        Assert.Equal(2, blocks.Count(b => b.SectionId == "s-chem")); // every meeting of the section
    }

    /// <summary>A Tag overlay still shows the out-of-focus section carrying the tag.</summary>
    [Fact]
    public void TagOverlay_ShowsOutOfFocusSections_AsOverlay()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects(["subj-biol"]));

        var blocks = Run(Snap(hasOverlay: true, overlayType: "Tag", overlayId: "t1"));

        Assert.Equal(["s-bio", "s-chem"], SectionIds(blocks));
        Assert.All(blocks, b => Assert.True(b.IsOverlay));
    }

    /// <summary>A Room overlay still shows the out-of-focus meetings in that room, and only those.</summary>
    [Fact]
    public void RoomOverlay_ShowsOutOfFocusMeetingsInThatRoom()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects(["subj-biol"]));

        var blocks = Run(Snap(hasOverlay: true, overlayType: "Room", overlayId: "r2"));

        // Biology's r1 meeting is an ordinary in-focus block; Chemistry's r2 meeting appears as an overlay.
        var chem = Assert.Single(blocks, b => b.SectionId == "s-chem");
        Assert.True(chem.IsOverlay);
        Assert.Equal(2, chem.Day);
        Assert.Single(blocks, b => b.SectionId == "s-bio");
    }

    /// <summary>Under None an Instructor overlay still draws the instructor's sections (the overlay is the whole picture).</summary>
    [Fact]
    public void UnderNone_InstructorOverlayStillShows()
    {
        _focus.SetFocus(SubjectFocus.None);

        var blocks = Run(Snap(hasOverlay: true, overlayType: "Instructor", overlayId: "i1"));

        Assert.Equal(["s-bio", "s-chem"], SectionIds(blocks));
        Assert.All(blocks, b => Assert.True(b.IsOverlay));
    }

    // ── Pass 1 predicate ──────────────────────────────────────────────────────

    /// <summary>
    /// Pass 1 alone, with a focus test, drops the section; with a null test (the default every older caller uses)
    /// it behaves exactly as before the focus existed.
    /// </summary>
    [Fact]
    public void BuildFilteredBlocks_NullFocusTest_ChangesNothing()
    {
        var sections = new List<Section> { _bio, _chem };
        var lookups = Lookups(sections);

        var all = ScheduleGridViewModel.BuildFilteredBlocks(sections, Snap(), lookups, []);
        var focused = ScheduleGridViewModel.BuildFilteredBlocks(sections, Snap(), lookups, [], s => s.Id == "s-bio");

        Assert.Equal(3, all.Count);
        Assert.Single(focused);
    }
}

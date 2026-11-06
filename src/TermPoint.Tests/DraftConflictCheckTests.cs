using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the section editor's live conflict notice (designs/conflict-muting.md, Part A):
/// <see cref="DraftConflictCheck"/> itself, and how <see cref="SectionEditViewModel"/> runs it
/// (when it re-checks, what the draft holds, that the notice never changes the section and never blocks Save).
/// </summary>
public class DraftConflictCheckTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Instructors = new() { ["i1"] = "J. Smith", ["i2"] = "A. Jones" };
    private static readonly Dictionary<string, string> Rooms = new() { ["r1"] = "A 121" };
    private static readonly Dictionary<string, string> Courses = new() { ["c1"] = "CHEM101", ["c2"] = "BIOL200" };

    /// <summary>Builds a one-meeting section (Monday by default) for the detector tests.</summary>
    private static Section MakeSection(
        string id, string courseId, string code, string[] instructorIds,
        int start, int duration, string? roomId = null, int day = 1) => new()
    {
        Id = id,
        CourseId = courseId,
        SectionCode = code,
        SemesterId = "sem1",
        InstructorAssignments = instructorIds.Select(i => new InstructorAssignment { InstructorId = i }).ToList(),
        Schedule = [new SectionDaySchedule { Day = day, StartMinutes = start, DurationMinutes = duration, RoomId = roomId }],
    };

    private static IReadOnlyList<string> Check(Section draft, params Section[] saved) =>
        DraftConflictCheck.Check(draft, saved, Rooms, Instructors, Courses);

    /// <summary>
    /// Builds an editor for an existing section with the given instructors listed (none ticked unless the
    /// section already has them). All other dependencies are empty stubs (mirrors SectionCapacityTests).
    /// </summary>
    private static SectionEditViewModel MakeVm(Section section, IEnumerable<Instructor> instructors,
        Func<Section, Task>? onSave = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section,
            isNew:             false,
            isCopy:            false,
            courses:           new List<Course> { new() { Id = "c1", CalendarCode = "CHEM101" } },
            subjects:          new List<Subject>(),
            instructors:       instructors.ToList(),
            rooms:             new List<Room>(),
            legalStartTimes:   new List<LegalStartTime>(),
            includeSaturday:   false,
            includeSunday:     false,
            sectionTypes:      new List<SchedulingEnvironmentValue>(),
            meetingTypes:      new List<SchedulingEnvironmentValue>(),
            campuses:          new List<Campus>(),
            allTags:           new List<SchedulingEnvironmentValue>(),
            allResources:      new List<SchedulingEnvironmentValue>(),
            allReserves:       new List<SchedulingEnvironmentValue>(),
            codePatterns:      new List<SectionCodePattern>(),
            isSectionCodeDuplicate: (_, _) => false,
            onSave:            onSave ?? (_ => Task.CompletedTask),
            blockPatternRepository: blockPatternRepo.Object,
            roomTypes:         new List<SchedulingEnvironmentValue>());
    }

    private static readonly Instructor Smith = new() { Id = "i1", FirstName = "Jane", LastName = "Smith" };

    // ══════════════════════════════════════════════════════════════════════════
    // DraftConflictCheck
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Check_DraftOverlapsAnotherSectionsInstructor_ReportsIt()
    {
        var other = MakeSection("s2", "c2", "B", ["i1"], start: 480, duration: 50);
        var draft = MakeSection("s1", "c1", "A", ["i1"], start: 480, duration: 50);

        var lines = Check(draft, other);

        Assert.Equal(["Instructor conflict: J. Smith Mon 0800–0850 with BIOL200 B"], lines);
    }

    [Fact]
    public void Check_EditMovesAwayFromConflict_SavedCopyIsIgnored()
    {
        // The saved copy of s1 still clashes with s2; the draft has moved to 10:00. Only the draft counts.
        var savedSelf = MakeSection("s1", "c1", "A", ["i1"], start: 480, duration: 50);
        var other     = MakeSection("s2", "c2", "B", ["i1"], start: 480, duration: 50);
        var draft     = MakeSection("s1", "c1", "A", ["i1"], start: 600, duration: 50);

        Assert.Empty(Check(draft, savedSelf, other));
    }

    [Fact]
    public void Check_DraftSameAsItsSavedCopy_NoSelfConflict()
    {
        var savedSelf = MakeSection("s1", "c1", "A", ["i1"], start: 480, duration: 50, roomId: "r1");
        var draft     = MakeSection("s1", "c1", "A", ["i1"], start: 480, duration: 50, roomId: "r1");

        Assert.Empty(Check(draft, savedSelf));
    }

    [Fact]
    public void Check_RoomAndInstructorConflicts_RoomListedFirst()
    {
        var other = MakeSection("s2", "c2", "B", ["i1"], start: 480, duration: 50, roomId: "r1");
        var draft = MakeSection("s1", "c1", "A", ["i1"], start: 480, duration: 50, roomId: "r1");

        var lines = Check(draft, other);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("Room conflict", lines[0]);
        Assert.StartsWith("Instructor conflict", lines[1]);
    }

    [Fact]
    public void Check_ConflictBetweenOtherSections_NotReported()
    {
        // s2 and s3 clash with each other; the draft is clear. The editor reports only the draft's own conflicts.
        var s2    = MakeSection("s2", "c2", "B", ["i2"], start: 480, duration: 50);
        var s3    = MakeSection("s3", "c2", "C", ["i2"], start: 480, duration: 50);
        var draft = MakeSection("s1", "c1", "A", ["i1"], start: 480, duration: 50);

        Assert.Empty(Check(draft, s2, s3));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SectionEditViewModel wiring
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>A stand-in check that reports one conflict whenever the draft has an instructor.</summary>
    private static IReadOnlyList<string> ConflictWhenStaffed(Section draft) =>
        draft.InstructorAssignments.Count > 0 ? ["Instructor conflict: J. Smith"] : [];

    [Fact]
    public void Editor_WithoutChecker_ShowsNoNotice()
    {
        var vm = MakeVm(MakeSection("s1", "c1", "A", ["i1"], 480, 50), [Smith]);

        Assert.Null(vm.ConflictWarning);
    }

    [Fact]
    public void Editor_SettingChecker_ChecksAtOnce()
    {
        // An existing section that already has a conflict shows it as soon as the editor opens.
        var vm = MakeVm(MakeSection("s1", "c1", "A", ["i1"], 480, 50), [Smith]);

        vm.CheckConflicts = ConflictWhenStaffed;

        Assert.Equal("Instructor conflict: J. Smith", vm.ConflictWarning);
    }

    [Fact]
    public void Editor_TickingInstructor_RaisesNotice_UntickingClearsIt()
    {
        var vm = MakeVm(MakeSection("s1", "c1", "A", [], 480, 50), [Smith]);
        vm.CheckConflicts = ConflictWhenStaffed;
        Assert.Null(vm.ConflictWarning);

        vm.InstructorSelections[0].IsSelected = true;
        Assert.Equal("Instructor conflict: J. Smith", vm.ConflictWarning);

        vm.InstructorSelections[0].IsSelected = false;
        Assert.Null(vm.ConflictWarning);
    }

    [Fact]
    public void Editor_Draft_CarriesIdSemesterScheduleAndTickedInstructors()
    {
        var section = MakeSection("s1", "c1", "A", [], 480, 50);
        var vm = MakeVm(section, [Smith]);
        Section? seen = null;
        vm.CheckConflicts = d => { seen = d; return []; };

        vm.InstructorSelections[0].IsSelected = true;

        Assert.NotNull(seen);
        Assert.NotSame(section, seen);
        Assert.Equal("s1", seen!.Id);
        Assert.Equal("sem1", seen.SemesterId);
        Assert.Equal(["i1"], seen.InstructorAssignments.Select(a => a.InstructorId));
        Assert.Equal(section.Schedule.Count, seen.Schedule.Count);
    }

    [Fact]
    public void Editor_Check_NeverChangesTheSection()
    {
        // Cancel must still discard everything, so the check may not write into the section being edited.
        var section = MakeSection("s1", "c1", "A", [], 480, 50);
        var scheduleBefore = section.Schedule;
        var vm = MakeVm(section, [Smith]);
        vm.CheckConflicts = ConflictWhenStaffed;

        vm.InstructorSelections[0].IsSelected = true;

        Assert.Empty(section.InstructorAssignments);
        Assert.Same(scheduleBefore, section.Schedule);
    }

    [Fact]
    public void Editor_SeveralConflicts_OnePerLine()
    {
        var vm = MakeVm(MakeSection("s1", "c1", "A", ["i1"], 480, 50), [Smith]);

        vm.CheckConflicts = _ => ["Room conflict: A 121", "Instructor conflict: J. Smith"];

        Assert.Equal("Room conflict: A 121\nInstructor conflict: J. Smith", vm.ConflictWarning);
    }

    [Fact]
    public async Task Editor_Save_NotBlockedByConflict()
    {
        var saved = false;
        var vm = MakeVm(MakeSection("s1", "c1", "A", ["i1"], 480, 50), [Smith],
            onSave: _ => { saved = true; return Task.CompletedTask; });
        vm.CheckConflicts = ConflictWhenStaffed;
        Assert.NotNull(vm.ConflictWarning);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(saved);
    }
}

using System.Reflection;
using System.Reflection.Emit;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.TestSupport.ImportScenario;

namespace TermPoint.Tests;

/// <summary>
/// Every code path that writes a section is either an undo step (<c>SectionHistory.Record</c>: a user's section edit) or a
/// pause (<c>SectionHistory.PauseRecording</c>: anything else), and nothing else. A path that is neither would hit the
/// change log's barrier at run time and empty the user's undo history (a safe failure, but a bad one), so this class
/// pins the list of paths twice: <see cref="EverySectionWriteIsOnTheKnownList"/> scans the compiled code for every call of
/// <c>ISectionRepository.Insert/Update/Delete/DeleteBySemesterId</c> and compares the callers with the list below, and one
/// behavioural test per caller drives the real code over a real writer <see cref="DatabaseContext"/> with its change log and
/// a real <see cref="SectionHistory"/>.
///
/// <para>Each behavioural test first makes one earlier step ("Prior step"), so a history cut would be visible. A step test
/// expects exactly one new step with the approved label. A pause test expects no new step, the earlier step still there,
/// a history that still reaches the last save, and the change log's "other changes" flag set (the change is unsaved).
/// <see cref="ASectionWriteWithNeitherStepNorPause_CutsTheHistory"/> proves the fail-safe the tripwire protects.</para>
///
/// <para>The scan sees repository calls only. Raw SQL against <c>Sections</c> (none exists in the app today) is not
/// found by it; the change log's barrier is the net for that.</para>
/// </summary>
public sealed class SectionWritePathTests : IDisposable
{
    /// <summary>The label of the step every test makes first.</summary>
    private const string PriorLabel = "Prior step";

    /// <summary>Protects the developer's real settings.json (reloading the semester context saves it).</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database (a writer, so its change log is installed) plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly SectionRepository _sections;
    private readonly CourseRepository _courses;
    private readonly SubjectRepository _subjects;
    private readonly InstructorRepository _instructors;
    private readonly RoomRepository _rooms;
    private readonly SchedulingEnvironmentRepository _env;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly FocusService _focus;
    private readonly SectionHistory _history;
    private readonly string _aId;
    private readonly string _prior;
    private readonly string _rId;
    private readonly string _wId;

    /// <summary>
    /// Seeds one academic year with Fall (shown), Winter (not shown, holds section W) and Spring (empty), the course
    /// HIST101, a room and a tag, and sections A (the one the tests edit), P (the prior step's) and R (with the room and
    /// tag); then simulates a save and makes the prior step.
    /// </summary>
    public SectionWritePathTests()
    {
        var db = _fx.Db;
        _sections = new SectionRepository(db);
        _courses = new CourseRepository(db);
        _subjects = new SubjectRepository(db);
        _instructors = new InstructorRepository(db);
        _rooms = new RoomRepository(db);
        _env = new SchedulingEnvironmentRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);

        _ayRepo.Insert(new AcademicYear { Id = "ay1", Name = "2025-2026" });
        _semesterRepo.Insert(new Semester { Id = "fall", AcademicYearId = "ay1", Name = "Fall 2025", SortOrder = 0 });
        _semesterRepo.Insert(new Semester { Id = "winter", AcademicYearId = "ay1", Name = "Winter 2026", SortOrder = 1 });
        _semesterRepo.Insert(new Semester { Id = "spring", AcademicYearId = "ay1", Name = "Spring 2026", SortOrder = 2 });
        new LegalStartTimeRepository(db).Insert(new LegalStartTime { BlockLength = 1.5, StartTimes = [510, 690, 780] }, "ay1");
        _subjects.Insert(new Subject { Id = "s1", Name = "History", CalendarAbbreviation = "HIST" });
        _courses.Insert(new Course { Id = "c1", SubjectId = "s1", CalendarCode = "HIST101", Title = "Intro" });
        _rooms.Insert(new Room { Id = "r1", Building = "B", RoomNumber = "204" });
        _env.Insert(SchedulingEnvironmentTypes.Tag, new SchedulingEnvironmentValue { Id = "t1", Name = "Lab" });

        _aId = Seed(new Section
        {
            SemesterId = "fall", CourseId = "c1", SectionCode = "A",
            Schedule =
            [
                new() { Day = 2, StartMinutes = 600, DurationMinutes = 90 },
                new() { Day = 4, StartMinutes = 600, DurationMinutes = 90 },
            ],
        });
        _prior = Seed(new Section { SemesterId = "fall", CourseId = "c1", SectionCode = "P" });
        _rId = Seed(new Section
        {
            SemesterId = "fall", CourseId = "c1", SectionCode = "R", TagIds = ["t1"],
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90, RoomId = "r1" }],
        });
        _wId = Seed(new Section { SemesterId = "winter", CourseId = "c1", SectionCode = "W" });

        _focus = new FocusService(
            new SubjectRepository(db), _courses, _sections, _semesterRepo, _ayRepo,
            _semesterContext, _sectionStore, null);
        _semesterContext.Reload(_ayRepo, _semesterRepo, "ay1", new HashSet<string> { "fall" });
        _sectionStore.Reload(_sections, ["fall"]);

        _history = NewHistory(db, _sections, _sectionStore, _semesterContext);
        MakePriorStep(_history, _sections, _prior, db);
    }

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _history.Dispose();
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── The tripwire ─────────────────────────────────────────────────────────

    /// <summary>
    /// The callers of <c>ISectionRepository.Insert/Update/Delete/DeleteBySemesterId</c> in the TermPoint assembly, as
    /// "Type.Method" (lambdas and async methods are mapped back to the method that contains them), each with whether it is
    /// a step or a pause and the behavioural test that covers it. Adding a section write means adding a line here.
    /// <c>SectionImportWriter.InsertAll</c> is the import's helper under <c>Write</c>'s pause.
    /// </summary>
    private static readonly (string Caller, string Kind, string Test)[] KnownCallers =
    [
        ("ScheduleGridViewModel.CommitMeetingDrag", "step", nameof(Drag_IsOneMoveStep)),
        ("SectionContextMenuViewModel.Confirm", "step", nameof(ContextMenu_IsOneStep_WithTheApprovedLabel)),
        ("SectionListViewModel.CreateEditVm", "step", nameof(SectionList_Edit_IsOneEditStep) + ", " + nameof(SectionList_Add_IsOneAddStep) + ", " + nameof(SectionList_Copy_IsOneCopyStep)),
        ("SectionListViewModel.Delete", "step", nameof(SectionList_Delete_IsOneDeleteStep)),
        ("CourseListViewModel.ApplyTagsToAllSections", "step", nameof(ApplyTags_IsOneStep_ForAllSections)),
        ("RoomListViewModel.Delete", "pause", nameof(RoomDelete_IsPaused)),
        ("SchedulingEnvironmentListViewModel.Delete", "pause", nameof(EnvironmentValueDelete_IsPaused)),
        ("CopySemesterViewModel.ExecuteCopy", "pause", nameof(CopySemester_IsPaused)),
        ("SectionImportWriter.InsertAll", "pause", nameof(SectionImport_IsPaused)),
        ("SemesterContentsService.EmptySemester", "pause", nameof(EmptySemester_IsPaused)),
    ];

    /// <summary>
    /// Scans every method body of the TermPoint assembly for calls to the section repository's writes and compares the
    /// callers with <see cref="KnownCallers"/>. Fails on a new caller (an unrecorded section write that would empty the
    /// undo history) and on a listed caller that no longer writes sections.
    /// </summary>
    [Fact]
    public void EverySectionWriteIsOnTheKnownList()
    {
        var found = SectionWriteScanner.FindCallers(typeof(SectionRepository).Assembly);
        var expected = KnownCallers.Select(k => k.Caller).OrderBy(c => c, StringComparer.Ordinal).ToList();

        var unknown = found.Except(expected).ToList();
        var gone = expected.Except(found).ToList();

        Assert.True(unknown.Count == 0 && gone.Count == 0,
            "The code that writes sections changed.\n" +
            (unknown.Count == 0 ? "" : $"  New callers of ISectionRepository Insert/Update/Delete/DeleteBySemesterId: {string.Join(", ", unknown)}.\n" +
                "  Wrap the new write in SectionHistory.Record (when it is a user's section edit: it becomes an undo step) or in\n" +
                "  SectionHistory.PauseRecording (anything else: never undone, but unsaved), never inside an open transaction;\n" +
                "  add a behavioural test to SectionWritePathTests that drives it; then add it to KnownCallers.\n") +
            (gone.Count == 0 ? "" : $"  Listed but no longer found: {string.Join(", ", gone)}. Remove or rename them in KnownCallers.\n"));
    }

    /// <summary>The scanner finds the callers it should, under compiler-generated names too (a guard for the tripwire).</summary>
    [Fact]
    public void Scanner_MapsLambdasAndAsyncMethodsBackToTheirMethod()
    {
        var found = SectionWriteScanner.FindCallers(typeof(SectionRepository).Assembly);

        Assert.Contains("SectionListViewModel.Delete", found);          // async state machine plus a lambda
        Assert.Contains("SectionListViewModel.CreateEditVm", found);    // a lambda inside an async lambda
        Assert.Contains("SemesterContentsService.EmptySemester", found); // a lambda that is a method of the class itself
        Assert.DoesNotContain(found, c => c.Contains('<') || c.StartsWith("SectionRepository.", StringComparison.Ordinal)
                                          || c.StartsWith("DemoSectionRepository.", StringComparison.Ordinal));
    }

    // ── The five steps ───────────────────────────────────────────────────────

    /// <summary>Step 1: a drag's drop is one "Move ..." step.</summary>
    [Fact]
    public void Drag_IsOneMoveStep()
    {
        var grid = NewGrid();
        var session = grid.TryBeginMeetingDrag(_aId, 1)!;
        var preview = session.Preview(5, 780, free: false, moveAlone: true, 480, 1080);

        Assert.True(grid.CommitMeetingDrag(session, preview));

        AssertOneNewStep(HistoryText.Move("HIST101 A", [new MeetingMove(4, 600, 5, 780)]));
    }

    /// <summary>Step 2: each Confirm of the right-click menu is one step, labelled by the sub-panel.</summary>
    [Theory]
    [InlineData(TileSubPanel.Instructors, "Change instructors of HIST101 A")]
    [InlineData(TileSubPanel.Room, "Change room of HIST101 A")]
    [InlineData(TileSubPanel.Tags, "Change tags of HIST101 A")]
    [InlineData(TileSubPanel.Flag, "Change flag of HIST101 A")]
    public void ContextMenu_IsOneStep_WithTheApprovedLabel(TileSubPanel panel, string label)
    {
        var section = _sections.GetById(_aId)!;
        var menu = new SectionContextMenuViewModel(_sections, () => { }, _fx.Lock, new FakeSectionCardState(), new SectionMarks(),
            history: _history);
        menu.Load(section, 2, 600,
            [new Instructor { Id = "i1", FirstName = "Ann", LastName = "Lee", IsActive = true }],
            [new Room { Id = "r1", Building = "B", RoomNumber = "204" }],
            [new SchedulingEnvironmentValue { Id = "t1", Name = "Lab" }],
            detailsHeading: "HIST101 A");

        switch (panel)
        {
            case TileSubPanel.Instructors:
                menu.ShowInstructorsCommand.Execute(null);
                menu.Instructors[0].IsChecked = true;
                break;
            case TileSubPanel.Room:
                menu.ShowRoomCommand.Execute(null);
                menu.SelectedRoom = menu.Rooms.First(r => r.Id == "r1");
                break;
            case TileSubPanel.Tags:
                menu.ShowTagsCommand.Execute(null);
                menu.Tags[0].IsChecked = true;
                break;
            default:
                menu.ShowFlagCommand.Execute(null);
                menu.SelectedFlag = menu.Flags.First(f => f.Value != section.Flag);
                break;
        }
        menu.ConfirmCommand.Execute(null);

        AssertOneNewStep(label);
    }

    /// <summary>Step 3: applying the section editor on an existing section is one step naming what changed ("Change notes of ...").</summary>
    [Fact]
    public async Task SectionList_Edit_IsOneEditStep()
    {
        var list = NewSectionList();
        list.SelectedItem = ItemOf(list, "A");

        await list.EditCommand.ExecuteAsync(null);
        list.EditVm!.Notes = "changed";
        await list.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Null(list.LastErrorMessage);
        AssertOneNewStep("Change notes of HIST101 A");
    }

    /// <summary>Step 3: applying the section editor on a new section is one "Add ..." step.</summary>
    [Fact]
    public async Task SectionList_Add_IsOneAddStep()
    {
        var list = NewSectionList();

        await list.AddCommand.ExecuteAsync(null);
        list.EditVm!.SelectedCourseId = "c1";
        list.EditVm.SectionCode = "C";
        await list.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Null(list.LastErrorMessage);
        AssertOneNewStep("Add HIST101 C");
        Assert.NotNull(_sections.GetAll("fall").SingleOrDefault(s => s.SectionCode == "C"));
    }

    /// <summary>Step 3: applying a copy is one "Copy X as Y" step.</summary>
    [Fact]
    public async Task SectionList_Copy_IsOneCopyStep()
    {
        var list = NewSectionList();
        list.SelectedItem = ItemOf(list, "A");

        await list.CopyCommand.ExecuteAsync(null);
        list.EditVm!.SectionCode = "B";
        await list.EditVm.SaveCommand.ExecuteAsync(null);

        Assert.Null(list.LastErrorMessage);
        AssertOneNewStep("Copy HIST101 A as HIST101 B");
    }

    /// <summary>Step 4: deleting a section is one "Delete ..." step.</summary>
    [Fact]
    public async Task SectionList_Delete_IsOneDeleteStep()
    {
        var list = NewSectionList();
        list.SelectedItem = ItemOf(list, "A");

        await list.DeleteCommand.ExecuteAsync(null);

        Assert.Null(list.LastErrorMessage);
        Assert.Null(_sections.GetById(_aId));
        AssertOneNewStep("Delete HIST101 A");
    }

    /// <summary>Step 5: "Apply tags to all sections" is ONE step however many sections it changes.</summary>
    [Fact]
    public async Task ApplyTags_IsOneStep_ForAllSections()
    {
        var count = _sections.GetByCourseId("c1").Count;
        Assert.True(count > 1);
        var list = new CourseListViewModel(_courses, _subjects, _env, new YesDialog(), _sections,
            _semesterRepo, _ayRepo, _instructors, _fx.Lock, _sectionStore,
            new SubjectColorStore(new[] { "#B7E79D" }, () => new AppSettings()),
            FocusTestSupport.NoColours(_courses, _subjects, _sectionStore),
            new FakeSectionCardState(), history: _history);
        list.SelectedCourse = list.Courses.Single();
        list.EditCommand.Execute(null);
        list.EditVm!.TagSelections.First().IsSelected = true;

        await list.EditVm.ApplyTagsToAllSectionsCommand.ExecuteAsync(null);

        Assert.Equal($"Tags applied to {count} sections.", list.EditVm.ApplyTagsResult);
        AssertOneNewStep(HistoryText.ApplyTags(count, "HIST101"));
    }

    // ── The five pauses ──────────────────────────────────────────────────────

    /// <summary>Pause 6: deleting a room removes it from meetings (section writes) without a step.</summary>
    [Fact]
    public async Task RoomDelete_IsPaused()
    {
        var list = new RoomListViewModel(_rooms, new CampusRepository(_fx.Db), _env, _sections, NewSectionList(),
            _fx.Db, new YesDialog(), _fx.Lock, _history);
        list.SelectedRow = list.Rooms.Single();

        await list.DeleteCommand.ExecuteAsync(null);

        Assert.Null(list.LastErrorMessage);
        Assert.Null(_sections.GetById(_rId)!.Schedule[0].RoomId);   // the section really was written
        AssertPaused();
    }

    /// <summary>Pause 7: deleting a tag removes it from sections (section writes) without a step.</summary>
    [Fact]
    public async Task EnvironmentValueDelete_IsPaused()
    {
        var list = new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Tag, "Tags",
            _env, _sections, _courses, _instructors, _fx.Db, NewSectionList(), new YesDialog(), _fx.Lock, history: _history);
        list.SelectedItem = list.Items.Single();

        await list.DeleteCommand.ExecuteAsync(null);

        Assert.Null(list.LastErrorMessage);
        Assert.Empty(_sections.GetById(_rId)!.TagIds);
        AssertPaused();
    }

    /// <summary>Pause 8: Copy Semester inserts sections without a step.</summary>
    [Fact]
    public void CopySemester_IsPaused()
    {
        var vm = new CopySemesterViewModel(_ayRepo, _semesterRepo, _sections, _fx.Db, _fx.Lock, history: _history)
        {
            FromAcademicYear = _ayRepo.GetById("ay1")!, FromSemester = _semesterRepo.GetById("fall")!,
            ToAcademicYear = _ayRepo.GetById("ay1")!, ToSemester = _semesterRepo.GetById("spring")!,
        };

        vm.CopyCommand.Execute(null);

        Assert.True(vm.IsComplete, vm.StatusMessage);
        Assert.NotEmpty(_sections.GetAll("spring"));
        AssertPaused();
    }

    /// <summary>
    /// Pause 9: an import's section inserts, without a step. Driven through <see cref="SectionImportWriter.Write"/> (the
    /// writer is the caller; the dialogs above it add nothing to the question) on the import tests' own rig.
    /// </summary>
    [Fact]
    public void SectionImport_IsPaused()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var prior = new Section { SemesterId = rig.Fall.Id, CourseId = "c-x", SectionCode = "Q" };
        rig.Sections.Insert(prior);
        var store = new SectionStore();
        var semesters = new SemesterContext();
        using var history = NewHistory(rig.Db, rig.Sections, store, semesters);
        MakePriorStep(history, rig.Sections, prior.Id, rig.Db);
        var report = rig.Plan(Sheet());
        Assert.False(report.HasProblems);
        var writer = new SectionImportWriter(rig.Db, rig.Subjects, rig.Campuses, rig.Environment, rig.Rooms, rig.Courses,
            rig.Instructors, rig.Sections, history);

        var counts = writer.Write(report.Plan, rig.Fall.Id);

        Assert.True(counts.Sections > 0);
        AssertPaused(history, rig.Db);
    }

    /// <summary>Pause 10: Empty Semester deletes sections without a step.</summary>
    [Fact]
    public void EmptySemester_IsPaused()
    {
        var service = new SemesterContentsService(_fx.Db, _sections, new MeetingRepository(_fx.Db),
            new ReleaseRepository(_fx.Db), new InstructorCommitmentRepository(_fx.Db), new SchedulingNoteRepository(_fx.Db),
            _semesterRepo, _ayRepo, new LegalStartTimeRepository(_fx.Db), _semesterContext, _history);

        service.EmptySemester("winter");

        Assert.Null(_sections.GetById(_wId));
        Assert.True(_fx.Db.ChangeLog!.WasDeleted("Sections", _wId));
        AssertPaused();
    }

    // ── The fail-safe ────────────────────────────────────────────────────────

    /// <summary>
    /// A section write that is neither a step nor a pause (a path nobody wrapped) cuts the history: the earlier step is
    /// gone and the history no longer reaches the last save. This is what the tripwire keeps from happening.
    /// </summary>
    [Fact]
    public void ASectionWriteWithNeitherStepNorPause_CutsTheHistory()
    {
        Assert.Single(_history.UndoableSteps);
        Assert.True(_history.HistoryReachesLastSave);

        var section = _sections.GetById(_aId)!;
        section.Notes = "unwrapped";
        _sections.Update(section);
        using (_history.PauseRecording()) { }   // any evaluation re-reads the log

        Assert.Empty(_history.UndoableSteps);
        Assert.False(_history.HistoryReachesLastSave);
    }

    // ── MeetingMove.Between ──────────────────────────────────────────────────

    /// <summary>One meeting moved to another day and time.</summary>
    [Fact]
    public void MeetingMoveBetween_SingleMove()
    {
        var moves = MeetingMove.Between([Meeting(2, 600), Meeting(4, 600)], [Meeting(2, 600), Meeting(5, 780)]);

        Assert.Equal([new MeetingMove(4, 600, 5, 780)], moves);
    }

    /// <summary>Only the time changes.</summary>
    [Fact]
    public void MeetingMoveBetween_TimeOnly()
    {
        var moves = MeetingMove.Between([Meeting(4, 600)], [Meeting(4, 690)]);

        Assert.Equal([new MeetingMove(4, 600, 4, 690)], moves);
        Assert.Equal("Move HIST101 A Thu 10:00 → 11:30", HistoryText.Move("HIST101 A", moves));
    }

    /// <summary>A group move of two days: both listed, ordered by their old day and start.</summary>
    [Fact]
    public void MeetingMoveBetween_GroupMove_IsOrderedByOldDayAndStart()
    {
        var moves = MeetingMove.Between([Meeting(4, 600), Meeting(2, 600)], [Meeting(5, 780), Meeting(3, 780)]);

        Assert.Equal([new MeetingMove(2, 600, 3, 780), new MeetingMove(4, 600, 5, 780)], moves);
        Assert.Equal("Move HIST101 A Tue, Thu 10:00 → Wed, Fri 13:00", HistoryText.Move("HIST101 A", moves));
    }

    /// <summary>Nothing moved: no moves.</summary>
    [Fact]
    public void MeetingMoveBetween_UnchangedList_IsEmpty()
    {
        Assert.Empty(MeetingMove.Between([Meeting(2, 600), Meeting(4, 600)], [Meeting(2, 600), Meeting(4, 600)]));
        Assert.Empty(MeetingMove.Between([], []));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A meeting of 90 minutes.</summary>
    /// <param name="day">The day (1 = Monday).</param>
    /// <param name="start">The start, in minutes after midnight.</param>
    private static SectionDaySchedule Meeting(int day, int start) => new() { Day = day, StartMinutes = start, DurationMinutes = 90 };

    /// <summary>Inserts a section as setup.</summary>
    /// <param name="section">The section.</param>
    /// <returns>Its id.</returns>
    private string Seed(Section section)
    {
        _sections.Insert(section);
        return section.Id;
    }

    /// <summary>A history over a database context, with no save signals (nothing here saves).</summary>
    private static SectionHistory NewHistory(DatabaseContext db, SectionRepository sections, SectionStore store, SemesterContext semesters) =>
        new(db, store, sections, semesters, new FakeSectionCardState(), null, new SilentAppLogger());

    /// <summary>
    /// Simulates a save of the setup writes (the log's flags and history start clean) and makes the one earlier step the
    /// tests look for.
    /// </summary>
    /// <param name="history">The history.</param>
    /// <param name="sections">The section repository.</param>
    /// <param name="sectionId">The section the prior step edits.</param>
    /// <param name="db">The database context.</param>
    private static void MakePriorStep(SectionHistory history, SectionRepository sections, string sectionId, DatabaseContext db)
    {
        db.ChangeLog!.TrimAfterSave(long.MaxValue, resetFlags: true);
        var section = sections.GetById(sectionId)!;
        history.Record(PriorLabel, () => { section.Notes = "prior"; sections.Update(section); });
        Assert.Equal(PriorLabel, Assert.Single(history.UndoableSteps).Label);
        Assert.True(history.HistoryReachesLastSave);
        Assert.False(db.ChangeLog.OtherChanges);
    }

    /// <summary>Expects the prior step plus exactly one new step with <paramref name="label"/>, and an uncut history.</summary>
    /// <param name="label">The new step's approved label.</param>
    private void AssertOneNewStep(string label)
    {
        var steps = _history.UndoableSteps;
        Assert.Equal([label, PriorLabel], steps.Select(s => s.Label).ToArray());
        Assert.True(_history.HistoryReachesLastSave);
    }

    /// <summary>Expects only the prior step, an uncut history, and the "other changes" flag set (unsaved, not undoable).</summary>
    private void AssertPaused() => AssertPaused(_history, _fx.Db);

    /// <summary>Expects only the prior step, an uncut history, and the "other changes" flag set (unsaved, not undoable).</summary>
    /// <param name="history">The history.</param>
    /// <param name="db">Its database context.</param>
    private static void AssertPaused(SectionHistory history, DatabaseContext db)
    {
        Assert.Equal(PriorLabel, Assert.Single(history.UndoableSteps).Label);
        Assert.True(history.HistoryReachesLastSave);
        Assert.True(db.ChangeLog!.OtherChanges);
    }

    /// <summary>A real section list view model with the history.</summary>
    private SectionListViewModel NewSectionList() =>
        new(_sections, _courses, _subjects, _instructors, _rooms, new LegalStartTimeRepository(_fx.Db), _semesterRepo,
            new BlockPatternRepository(_fx.Db), new SectionCodePatternRepository(_fx.Db), _semesterContext, _sectionStore,
            _env, new CampusRepository(_fx.Db), new MeetingRepository(_fx.Db), new YesDialog(), _fx.Lock,
            FocusTestSupport.AllFocus(_semesterContext, _sectionStore),
            FocusTestSupport.NoColours(_courses, _subjects, _sectionStore), new SectionMarks(), _history);

    /// <summary>The list item of the section with the given code.</summary>
    /// <param name="list">The section list.</param>
    /// <param name="code">The section code.</param>
    private static SectionListItemViewModel ItemOf(SectionListViewModel list, string code) =>
        list.SectionItems.OfType<SectionListItemViewModel>().Single(i => i.Section.SectionCode == code);

    /// <summary>The real grid view model with the history.</summary>
    private ScheduleGridViewModel NewGrid()
    {
        var db = _fx.Db;
        var notifier = new GridChangeNotifier();
        return new ScheduleGridViewModel(
            _sections, _courses, _instructors, _rooms, _subjects, _env,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), notifier,
            new InstructorCommitmentRepository(db), _fx.Lock, new SharedScheduleService(),
            _focus, FocusTestSupport.NoColours(_courses, _subjects, _sectionStore), new BlockPatternRepository(db),
            new FakeSectionCardState(), new LegalStartTimeRepository(db), new SectionMarks(),
            AccessStateTestSupport.For(db, _sectionStore, notifier), history: _history);
    }

    /// <summary>A dialog service that answers yes to every question.</summary>
    private sealed class YesDialog : IDialogService
    {
        /// <inheritdoc/>
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(true);

        /// <inheritdoc/>
        public Task ShowError(string message) => Task.CompletedTask;
    }
}

/// <summary>
/// Finds, in a compiled assembly, every method that calls a write of the section repository, by reading the IL (so it is
/// exact about types, unlike a text search, and sees calls made through the interface, a repository class or a
/// decorator).
/// </summary>
internal static class SectionWriteScanner
{
    /// <summary>The section repository methods that write.</summary>
    private static readonly HashSet<string> WriteNames = ["Insert", "Update", "Delete", "DeleteBySemesterId"];

    /// <summary>IL opcodes by their value, built from <see cref="OpCodes"/> (for the operand sizes).</summary>
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    /// <summary>
    /// Every "Type.Method" that calls <c>Insert</c>, <c>Update</c>, <c>Delete</c> or <c>DeleteBySemesterId</c> declared on
    /// <see cref="ISectionRepository"/> or on a class that implements it. Callers inside the repositories themselves
    /// (<c>TermPoint.Data.Repositories</c>, and any implementation of the interface) are left out. A lambda, display class
    /// or async state machine is reported as the user method that contains it.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The distinct callers, ordinal-sorted.</returns>
    public static List<string> FindCallers(Assembly assembly)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var callers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in LoadableTypes(assembly))
        {
            var owner = UserType(type);
            if (IsRepositoryImplementation(owner)) continue;

            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch { continue; }
                if (il is null) continue;

                if (CallsASectionWrite(method, il))
                    callers.Add($"{owner.Name}.{UserMethodName(method)}");
            }
        }
        return callers.OrderBy(c => c, StringComparer.Ordinal).ToList();
    }

    /// <summary>The types of an assembly that can be loaded.</summary>
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    /// <summary>The nearest enclosing type that is not compiler generated (display classes and state machines nest in it).</summary>
    private static Type UserType(Type type)
    {
        while (type.DeclaringType is not null && IsGenerated(type)) type = type.DeclaringType;
        return type;
    }

    /// <summary>True for a compiler-generated type (its name has angle brackets, e.g. "&lt;&gt;c__DisplayClass3_0").</summary>
    private static bool IsGenerated(Type type) => type.Name.Contains('<') || type.Name.Contains('>');

    /// <summary>True for the section repository implementations (and anything in the repositories namespace).</summary>
    private static bool IsRepositoryImplementation(Type type) =>
        typeof(ISectionRepository).IsAssignableFrom(type) || type.Namespace == "TermPoint.Data.Repositories";

    /// <summary>
    /// The method a piece of generated code belongs to: for a lambda ("&lt;CreateEditVm&gt;b__4_0") or a state machine's
    /// MoveNext (its type "&lt;Delete&gt;d__12") the name between the first angle brackets; otherwise the method's own name.
    /// </summary>
    private static string UserMethodName(MethodBase method)
    {
        var name = method.Name;
        if (method.DeclaringType is { } t && IsGenerated(t) && !name.StartsWith('<'))
            name = t.Name;   // MoveNext, or a lambda body inside a display class: the type's name carries the origin
        var close = name.IndexOf('>');
        if (!name.StartsWith('<') || close < 0) return name;
        // "<<CreateEditVm>b__0>d" nests: the innermost brackets hold the user method.
        var open = name.LastIndexOf('<', close);
        return name[(open + 1)..close];
    }

    /// <summary>True when the IL contains a call or callvirt of a section repository write.</summary>
    /// <param name="method">The method the IL belongs to (its module resolves the tokens).</param>
    /// <param name="il">The method body.</param>
    private static bool CallsASectionWrite(MethodBase method, byte[] il)
    {
        var typeArgs = method.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var i = 0; i < il.Length;)
        {
            var value = (short)il[i++];
            if (value == 0xFE && i < il.Length) value = (short)(0xFE00 | il[i++]);
            if (!Codes.TryGetValue(value, out var code)) return false;   // unknown byte: stop reading this body

            if ((code == OpCodes.Call || code == OpCodes.Callvirt) && i + 4 <= il.Length)
            {
                var token = BitConverter.ToInt32(il, i);
                try
                {
                    var target = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                    if (WriteNames.Contains(target.Name) && target.DeclaringType is { } declaring
                        && typeof(ISectionRepository).IsAssignableFrom(declaring))
                        return true;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or BadImageFormatException)
                {
                    // A token that does not resolve is not one of ours.
                }
            }

            i += OperandSize(code, il, i);
        }
        return false;
    }

    /// <summary>The number of operand bytes that follow an opcode.</summary>
    /// <param name="code">The opcode.</param>
    /// <param name="il">The method body.</param>
    /// <param name="at">Index of the first operand byte (a switch's case count is read from there).</param>
    private static int OperandSize(OpCode code, byte[] il, int at) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => 4,
    };
}

using System.Text.Json.Nodes;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 3, save paths: every place that UPDATES an existing record with an object other than
/// the one loaded from the database (an editor that works on a clone, a Save that rebuilds a child
/// list from view-model rows, ...) must carry the original's unknown JSON fields across, or an older
/// app version would delete the fields a newer version stored.
/// <para>
/// Each test stores the standard future fields on a record (and on its nested records where it has
/// any), drives the real view model through its real Edit and Save commands, and then inspects what
/// reached the database or the repository. Paths that merely mutate the loaded object need no
/// special handling and are covered by the repository tests; the few that are cheap to drive are
/// included here as controls. (The section editor has its own tests in
/// <see cref="SectionCloneTests"/>.)
/// </para>
/// </summary>
public sealed class ExtensionDataSavePathTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    // Saving a semester reloads SemesterContext, which writes the user's settings.json; keep the real one safe.
    private readonly SettingsFileGuard _settingsGuard = new();

    // Repositories over the fixture database.
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly LegalStartTimeRepository _legalRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly BlockPatternRepository _blockPatternRepo;
    private readonly SectionCodePatternRepository _codePatternRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly CampusRepository _campusRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SchedulingNoteRepository _noteRepo;
    private readonly MeetingRepository _meetingRepo;

    // Services the list view models need.
    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly GridChangeNotifier _changeNotifier = new();
    private readonly IDialogService _dialog = new NullDialog();

    /// <summary>Builds the repositories over the temp database.</summary>
    public ExtensionDataSavePathTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _roomRepo = new RoomRepository(db);
        _legalRepo = new LegalStartTimeRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _blockPatternRepo = new BlockPatternRepository(db);
        _codePatternRepo = new SectionCodePatternRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        _campusRepo = new CampusRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _releaseRepo = new ReleaseRepository(db);
        _commitmentRepo = new InstructorCommitmentRepository(db);
        _noteRepo = new SchedulingNoteRepository(db);
        _meetingRepo = new MeetingRepository(db);
    }

    /// <summary>Releases the temp database and lock, and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>A dialog service that never shows anything and always answers "no".</summary>
    private sealed class NullDialog : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(false);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>A real section list view model (needed by the room and environment lists).</summary>
    private SectionListViewModel NewSectionList() =>
        new(_sectionRepo, _courseRepo, _subjectRepo, _instructorRepo, _roomRepo,
            _legalRepo, _semesterRepo, _blockPatternRepo, _codePatternRepo,
            _semesterContext, _sectionStore, _envRepo, _campusRepo, _meetingRepo,
            _dialog, _fx.Lock,
            FocusTestSupport.AllFocus(_semesterContext, _sectionStore));

    // ══════════════════════════════════════════════════════════════════════════
    // List view models whose Edit works on a hand-built copy of the selected record
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CourseEdit_KeepsUnknownFields_AndTheFieldsTheCopyUsedToOmit()
    {
        _subjectRepo.Insert(new Subject { Id = "subj-1", Name = "History", CalendarAbbreviation = "HIST" });
        // The tag must exist: the course editor only keeps tags it can offer as checkboxes.
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, new SchedulingEnvironmentValue { Id = "tag-1", Name = "Core" });
        // Level "200" is deliberately NOT what the course number would suggest ("100"), and
        // Capacity is set: the edit-copy used to drop both, so every Apply reset them.
        var course = new Course { Id = "c-1", SubjectId = "subj-1", CalendarCode = "HIST101", Title = "Intro",
                                  Level = "200", Capacity = 30, TagIds = ["tag-1"] };
        _courseRepo.Insert(course);
        _fx.Stamp("Courses", course.Id);

        var vm = new CourseListViewModel(_courseRepo, _subjectRepo, _envRepo, _dialog, _sectionRepo,
            _semesterRepo, _ayRepo, _instructorRepo, _fx.Lock, _sectionStore);
        vm.SelectedCourse = vm.Courses.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.CourseTitle = "Intro to History";
        await vm.EditVm.SaveCommand.ExecuteAsync(null);

        var raw = _fx.ReadData("Courses", course.Id);
        Assert.Equal("Intro to History", raw["title"]!.GetValue<string>());
        Assert.Equal(30, raw["capacity"]!.GetValue<int>());
        Assert.Equal("200", raw["level"]!.GetValue<string>());
        Assert.Equal("tag-1", raw["tagIds"]![0]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Courses", "root");
    }

    [Fact]
    public async Task SubjectEdit_FromTheCourseList_KeepsUnknownFields()
    {
        var subject = new Subject { Id = "subj-1", Name = "History", CalendarAbbreviation = "HIST" };
        _subjectRepo.Insert(subject);
        _fx.Stamp("Subjects", subject.Id);

        var vm = new CourseListViewModel(_courseRepo, _subjectRepo, _envRepo, _dialog, _sectionRepo,
            _semesterRepo, _ayRepo, _instructorRepo, _fx.Lock, _sectionStore);
        vm.SelectedSubject = vm.Subjects.Single();
        vm.EditSubjectCommand.Execute(null);
        vm.SubjectEditVm!.Name = "World History";
        await vm.SubjectEditVm.SaveCommand.ExecuteAsync(null);

        var raw = _fx.ReadData("Subjects", subject.Id);
        Assert.Equal("World History", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Subjects", "root");
    }

    [Fact]
    public void SubjectEdit_FromTheSubjectList_KeepsUnknownFields()
    {
        var subject = new Subject { Id = "subj-1", Name = "History", CalendarAbbreviation = "HIST" };
        _subjectRepo.Insert(subject);
        _fx.Stamp("Subjects", subject.Id);

        var vm = new SubjectListViewModel(_subjectRepo, _dialog, _fx.Lock, FocusTestSupport.AllFocus());
        vm.SelectedSubject = vm.Subjects.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Name = "World History";
        vm.EditVm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("Subjects", subject.Id);
        Assert.Equal("World History", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Subjects", "root");
    }

    [Fact]
    public void InstructorEdit_KeepsUnknownFields()
    {
        var instructor = new Instructor { Id = "inst-1", LastName = "Smith", FirstName = "Jo", Initials = "JS" };
        _instructorRepo.Insert(instructor);
        _fx.Stamp("Instructors", instructor.Id);

        var vm = new InstructorListViewModel(_instructorRepo, _envRepo, _sectionRepo, _courseRepo, _releaseRepo,
            _commitmentRepo, _noteRepo, _semesterRepo, _ayRepo, _semesterContext, _changeNotifier,
            _dialog, _fx.Lock,
            new WorkloadMailerViewModel(_ayRepo, _semesterRepo, _instructorRepo, _sectionRepo, _releaseRepo, _courseRepo, FocusTestSupport.AllFocus()),
            FocusTestSupport.AllFocus());
        vm.SelectedInstructor = vm.Instructors.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Email = "jo@example.edu";
        vm.EditVm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("Instructors", instructor.Id);
        Assert.Equal("jo@example.edu", raw["email"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Instructors", "root");
    }

    [Fact]
    public void RoomEdit_KeepsUnknownFields()
    {
        var room = new Room { Id = "room-1", Building = "B", RoomNumber = "101", Capacity = 40 };
        _roomRepo.Insert(room);
        _fx.Stamp("Rooms", room.Id);

        var vm = new RoomListViewModel(_roomRepo, _campusRepo, _envRepo, _sectionRepo, NewSectionList(),
            _fx.Db, _dialog, _fx.Lock);
        vm.SelectedRow = vm.Rooms.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Notes = "Projector";
        vm.EditVm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("Rooms", room.Id);
        Assert.Equal("Projector", raw["notes"]!.GetValue<string>());
        Assert.Equal(40, raw["capacity"]!.GetValue<int>());
        FutureFields.AssertPresent(raw, "Rooms", "root");
    }

    [Fact]
    public void CampusEdit_KeepsUnknownFields()
    {
        var campus = new Campus { Id = "campus-1", Name = "Main", Abbreviation = "MC" };
        _campusRepo.Insert(campus);
        _fx.Stamp("Campuses", campus.Id);

        var vm = new CampusListViewModel(_campusRepo, _dialog, _fx.Lock);
        vm.SelectedItem = vm.Items.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Name = "Main Campus";
        vm.EditVm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("Campuses", campus.Id);
        Assert.Equal("Main Campus", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Campuses", "root");
    }

    [Fact]
    public void SchedulingEnvironmentValueEdit_KeepsUnknownFields()
    {
        var value = new SchedulingEnvironmentValue { Id = "st-1", Name = "Lecture" };
        _envRepo.Insert(SchedulingEnvironmentTypes.SectionType, value);
        _fx.Stamp("SchedulingEnvironmentValues", value.Id);

        var vm = new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.SectionType, "Section Type",
            _envRepo, _sectionRepo, _courseRepo, _instructorRepo, _fx.Db, NewSectionList(), _dialog, _fx.Lock);
        vm.SelectedItem = vm.Items.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Name = "Lecture (large)";
        vm.EditVm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("SchedulingEnvironmentValues", value.Id);
        Assert.Equal("Lecture (large)", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "SchedulingEnvironmentValues", "root");
    }

    [Fact]
    public void SectionCodePatternEdit_KeepsUnknownFields()
    {
        var pattern = new SectionCodePattern { Id = "pat-1", Name = "Lecture", Prefix = "L", Examples = "L1, L2" };
        _codePatternRepo.Insert(pattern);
        _fx.Stamp("SectionCodePatterns", pattern.Id);

        var vm = new SectionCodePatternListViewModel(_codePatternRepo, _campusRepo, _envRepo, _dialog, _fx.Lock);
        vm.SelectedItem = vm.Items.Single();
        vm.EditCommand.Execute(null);
        vm.EditVm!.Name = "Large lecture";
        vm.EditVm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("SectionCodePatterns", pattern.Id);
        Assert.Equal("Large lecture", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "SectionCodePatterns", "root");
    }

    /// <summary>
    /// Renaming a semester updates its data column from a copy. The column also holds the Color
    /// (PascalCase, historical format), which must be kept alongside the unknown fields.
    /// </summary>
    [Fact]
    public void SemesterRename_KeepsUnknownFields_AndColor()
    {
        var ay = new AcademicYear { Id = "ay-1", Name = "2025-2026" };
        _ayRepo.Insert(ay);
        var sem = new Semester { Id = "sem-1", AcademicYearId = ay.Id, Name = "Fall", SortOrder = 1, Color = "#C65D1E" };
        _semesterRepo.Insert(sem);
        _fx.Stamp("Semesters", sem.Id);

        var contentsService = new SemesterContentsService(
            _fx.Db, _sectionRepo, _meetingRepo, _releaseRepo, _commitmentRepo, _noteRepo,
            new ProgramWatchRepository(_fx.Db), _semesterRepo, _ayRepo, _legalRepo, _semesterContext);
        var vm = new SemesterManagerViewModel(ay.Id, _ayRepo, _semesterRepo, contentsService, _semesterContext, _dialog, _fx.Lock);
        vm.SelectedSemester = vm.Semesters.Single();
        vm.RenameCommand.Execute(null);
        vm.EditVm!.Name = "Autumn";
        vm.EditVm.SaveCommand.Execute(null);

        Assert.Equal("Autumn", _semesterRepo.GetById(sem.Id)!.Name);
        var raw = _fx.ReadData("Semesters", sem.Id);
        Assert.Equal("#C65D1E", raw["Color"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Semesters", "root");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Edit view models that build a brand-new object on Save
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CommitmentEditor_Save_KeepsUnknownFields()
    {
        var original = new InstructorCommitment
        {
            InstructorId = "inst-1", SemesterId = "sem-1", Name = "Office hours", Day = 2,
            StartMinutes = 600, EndMinutes = 660,
        };
        _commitmentRepo.Insert(original);
        _fx.Stamp("InstructorCommitments", original.Id);

        // Open the way CommitmentsManagementViewModel.Edit does: re-fetch, edit, Update what is handed back.
        var vm = new CommitmentEditViewModel(_commitmentRepo.GetById(original.Id)!,
            onCancel: () => { }, onSave: c => { _commitmentRepo.Update(c); return Task.CompletedTask; });
        vm.Name = "Drop-in hours";
        await vm.SaveCommand.ExecuteAsync(null);

        var raw = _fx.ReadData("InstructorCommitments", original.Id);
        Assert.Equal("Drop-in hours", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "InstructorCommitments", "root");
    }

    [Fact]
    public async Task ReleaseEditor_Save_KeepsUnknownFields()
    {
        var original = new Release { SemesterId = "sem-1", InstructorId = "inst-1", Title = "Admin", WorkloadValue = 0.5m };
        _releaseRepo.Insert(original);
        _fx.Stamp("Releases", original.Id);

        var vm = new ReleaseEditViewModel(_releaseRepo.GetById(original.Id)!,
            onCancel: () => { }, onSave: r => { _releaseRepo.Update(r); return Task.CompletedTask; });
        vm.Title = "Department chair";
        await vm.SaveCommand.ExecuteAsync(null);

        var raw = _fx.ReadData("Releases", original.Id);
        Assert.Equal("Department chair", raw["title"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "Releases", "root");
    }

    [Fact]
    public void BlockPatternEditor_Save_KeepsUnknownFields_AndANewPatternStartsWithNone()
    {
        var existing = new BlockPattern { Name = "MWF", Days = [1, 3, 5] };
        _blockPatternRepo.Insert(existing);
        _fx.Stamp("BlockPatterns", existing.Id);

        // Editing: the slot view model copies the id onto what the editor hands back, then Updates.
        var stored = _blockPatternRepo.GetById(existing.Id)!;
        BlockPattern? handedBack = null;
        var editor = new BlockPatternEditViewModel(1, stored, includeSaturday: false, includeSunday: false,
            onSave: p => handedBack = p, onCancel: () => { });
        editor.Name = "MWF mornings";
        editor.SaveCommand.Execute(null);
        Assert.NotNull(handedBack);
        handedBack!.Id = existing.Id;
        _blockPatternRepo.Update(handedBack);

        var raw = _fx.ReadData("BlockPatterns", existing.Id);
        Assert.Equal("MWF mornings", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "BlockPatterns", "root");

        // Creating: an empty slot has no original, so the new pattern carries nothing.
        BlockPattern? created = null;
        var creator = new BlockPatternEditViewModel(2, existing: null, includeSaturday: false, includeSunday: false,
            onSave: p => created = p, onCancel: () => { });
        creator.Name = "TR";
        creator.Days.First(d => d.Day == 2).IsChecked = true;
        creator.SaveCommand.Execute(null);
        Assert.NotNull(created);
        Assert.Null(created!.ExtensionData);
    }

    /// <summary>
    /// The scheduling note is saved by building a new note and upserting it over the stored row;
    /// the view model must hand the stored note's unknown fields to that new object.
    /// </summary>
    [Fact]
    public void SchedulingNotes_Save_KeepsUnknownFields()
    {
        var note = new SchedulingNote { SemesterId = "sem-1", InstructorId = "inst-1", Text = "first" };
        _noteRepo.Save(note);
        _fx.Stamp("SchedulingNotes", note.Id);

        var vm = new SchedulingNotesViewModel(_noteRepo, _fx.Lock);
        vm.SetContext("inst-1", "sem-1");
        Assert.Equal("first", vm.Text);
        vm.Text = "second";
        vm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("SchedulingNotes", note.Id);
        Assert.Equal("second", raw["text"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "SchedulingNotes", "root");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Meetings (events): editor rebuilds the schedule and attendee lists
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MeetingEditor_Save_KeepsUnknownFields_OnTheMeetingItsSlotsAndItsAttendees()
    {
        // A meeting loaded from the database (as the list hands it to the editor), with unknown
        // fields on the meeting, its slot and each of two attendees.
        var meeting = new Meeting
        {
            SemesterId = "sem-1", Title = "Curriculum committee",
            Schedule = [new() { Day = 2, StartMinutes = 600, DurationMinutes = 60 }],
            InstructorAssignments = [new() { InstructorId = "inst-1" }, new() { InstructorId = "inst-2" }],
        };
        _meetingRepo.Insert(meeting);
        _fx.Stamp("Meetings", meeting.Id, "$", "meeting");
        _fx.Stamp("Meetings", meeting.Id, "$.schedule[0]", "slot");
        _fx.Stamp("Meetings", meeting.Id, "$.instructorAssignments[0]", "attendee-inst-1");
        _fx.Stamp("Meetings", meeting.Id, "$.instructorAssignments[1]", "attendee-inst-2");

        var loaded = _meetingRepo.GetById(meeting.Id)!;
        var instructors = new List<Instructor>
        {
            new() { Id = "inst-1", LastName = "One", FirstName = "A" },
            new() { Id = "inst-2", LastName = "Two", FirstName = "B" },
            new() { Id = "inst-3", LastName = "Three", FirstName = "C" },
        };
        var vm = new MeetingEditViewModel(loaded, isNew: false, _meetingRepo, new MeetingStore(),
            legalStartTimes: new List<LegalStartTime>(), allInstructors: instructors,
            meetingTypes: new List<SchedulingEnvironmentValue>(), allRooms: new List<Room>(),
            roomTypes: new List<SchedulingEnvironmentValue>(), campuses: new List<Campus>(),
            allTags: new List<SchedulingEnvironmentValue>(), allResources: new List<SchedulingEnvironmentValue>(),
            allStaffTypes: new List<SchedulingEnvironmentValue>());

        vm.Title = "Curriculum and standards committee";
        vm.AttendeeSelections.First(a => a.Value.Id == "inst-3").IsSelected = true;   // a new attendee
        vm.SaveCommand.Execute(null);

        var raw = _fx.ReadData("Meetings", meeting.Id);
        Assert.Equal("Curriculum and standards committee", raw["title"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "meeting", "meeting");
        FutureFields.AssertPresent(raw["schedule"]![0], "slot", "slot");

        var attendees = raw["instructorAssignments"]!.AsArray().ToDictionary(a => a!["instructorId"]!.GetValue<string>());
        Assert.Equal(new[] { "inst-1", "inst-2", "inst-3" }, attendees.Keys.OrderBy(k => k));
        FutureFields.AssertPresent(attendees["inst-1"], "attendee 1", "attendee-inst-1");
        FutureFields.AssertPresent(attendees["inst-2"], "attendee 2", "attendee-inst-2");
        FutureFields.AssertAbsent(attendees["inst-3"], "attendee added during the edit");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Grid quick-edit context menu: rebuilds a section's instructor assignments in place
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GridQuickEdit_Instructors_KeepsUnknownFields_AndWorkloads()
    {
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
            InstructorAssignments = [new() { InstructorId = "inst-1", Workload = 0.5m }, new() { InstructorId = "inst-2", Workload = 0.25m }],
        };
        _sectionRepo.Insert(section);
        _fx.Stamp("Sections", section.Id, "$", "section");
        _fx.Stamp("Sections", section.Id, "$.schedule[0]", "meeting");
        _fx.Stamp("Sections", section.Id, "$.instructorAssignments[0]", "assignment-inst-1");
        _fx.Stamp("Sections", section.Id, "$.instructorAssignments[1]", "assignment-inst-2");

        // The grid hands the context menu the section object loaded from the store.
        var loaded = _sectionRepo.GetById(section.Id)!;
        var instructors = new List<Instructor>
        {
            new() { Id = "inst-1", LastName = "One", FirstName = "A" },
            new() { Id = "inst-2", LastName = "Two", FirstName = "B" },
            new() { Id = "inst-3", LastName = "Three", FirstName = "C" },
        };
        var menu = new SectionContextMenuViewModel(_sectionRepo, () => { }, _fx.Lock);
        menu.Load(loaded, meetingDay: 1, meetingStart: 510, instructors, new List<Room>(), new List<SchedulingEnvironmentValue>());

        menu.ShowInstructorsCommand.Execute(null);
        menu.Instructors.First(i => i.Id == "inst-1").IsChecked = false;   // removed
        menu.Instructors.First(i => i.Id == "inst-3").IsChecked = true;    // added
        menu.ConfirmCommand.Execute(null);

        var raw = _fx.ReadData("Sections", section.Id);
        var assignments = raw["instructorAssignments"]!.AsArray().ToDictionary(a => a!["instructorId"]!.GetValue<string>());
        Assert.Equal(new[] { "inst-2", "inst-3" }, assignments.Keys.OrderBy(k => k));
        FutureFields.AssertPresent(assignments["inst-2"], "kept assignment", "assignment-inst-2");
        Assert.Equal(0.25m, assignments["inst-2"]!["workload"]!.GetValue<decimal>());     // workload kept too
        FutureFields.AssertAbsent(assignments["inst-3"], "assignment added from the grid");
        Assert.Equal(1m, assignments["inst-3"]!["workload"]!.GetValue<decimal>());

        // Everything else on the section and its meeting was untouched.
        FutureFields.AssertPresent(raw, "section", "section");
        FutureFields.AssertPresent(raw["schedule"]![0], "meeting", "meeting");
    }

    /// <summary>
    /// Controls for the quick-edit actions that mutate the loaded object in place (room, tags,
    /// flag): they need no carry-over, and must not disturb anything they do not touch.
    /// </summary>
    [Fact]
    public void GridQuickEdit_RoomTagsAndFlag_MutateInPlace_AndKeepUnknownFields()
    {
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
            InstructorAssignments = [new() { InstructorId = "inst-1", Workload = 1m }],
        };
        _sectionRepo.Insert(section);
        _fx.Stamp("Sections", section.Id, "$", "section");
        _fx.Stamp("Sections", section.Id, "$.schedule[0]", "meeting");
        _fx.Stamp("Sections", section.Id, "$.instructorAssignments[0]", "assignment");

        var loaded = _sectionRepo.GetById(section.Id)!;
        var rooms = new List<Room> { new() { Id = "room-1", Building = "B", RoomNumber = "1" } };
        var tags = new List<SchedulingEnvironmentValue> { new() { Id = "tag-1", Name = "T" } };
        var menu = new SectionContextMenuViewModel(_sectionRepo, () => { }, _fx.Lock);
        menu.Load(loaded, 1, 510, new List<Instructor>(), rooms, tags);

        menu.ShowRoomCommand.Execute(null);
        menu.SelectedRoom = menu.Rooms.First(r => r.Id == "room-1");
        menu.ConfirmCommand.Execute(null);

        menu.ShowTagsCommand.Execute(null);
        menu.Tags.First().IsChecked = true;
        menu.ConfirmCommand.Execute(null);

        var raw = _fx.ReadData("Sections", section.Id);
        Assert.Equal("room-1", raw["schedule"]![0]!["roomId"]!.GetValue<string>());
        Assert.Equal("tag-1", raw["tagIds"]![0]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "section", "section");
        FutureFields.AssertPresent(raw["schedule"]![0], "meeting", "meeting");
        FutureFields.AssertPresent(raw["instructorAssignments"]![0], "assignment", "assignment");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Other writers that mutate what they loaded (controls)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AcademicUnitService_UpdateUnit_KeepsUnknownFields()
    {
        var repo = new AcademicUnitRepository(_fx.Db);
        var unit = repo.GetAll().First();                    // the default unit seeded at database creation
        _fx.Stamp("AcademicUnits", unit.Id);

        new AcademicUnitService(repo).UpdateUnit("Biology", "UFV", "UFV");

        var raw = _fx.ReadData("AcademicUnits", unit.Id);
        Assert.Equal("Biology", raw["name"]!.GetValue<string>());
        FutureFields.AssertPresent(raw, "AcademicUnits", "root");
    }

    [Fact]
    public void ProgramWatch_ToggleThroughItsViewModel_KeepsUnknownFields()
    {
        var repo = new ProgramWatchRepository(_fx.Db);
        var watch = new ProgramWatch { Name = "CS core", Mode = ProgramWatchMode.Course, CourseIds = ["c-1"] };
        repo.Save("sem-1", watch);
        _fx.Stamp("ProgramWatches", watch.Id);

        // The panel loads watches from the repository, mutates the loaded object (name / enabled)
        // and saves that same object.
        var loaded = repo.GetAll("sem-1").Single();
        loaded.IsEnabled = false;
        loaded.Name = "CS core (paused)";
        repo.Save("sem-1", loaded);

        var raw = _fx.ReadData("ProgramWatches", watch.Id);
        Assert.False(raw["isEnabled"]!.GetValue<bool>());
        FutureFields.AssertPresent(raw, "ProgramWatches", "root");
    }
}

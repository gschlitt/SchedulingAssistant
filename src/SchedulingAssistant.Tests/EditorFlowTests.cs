using Moq;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Integration-style unit tests for the inline editor workflows.
/// Each test constructs the ViewModel under test directly, using Moq stubs for
/// repositories and real model objects. No database or UI layer is involved.
///
/// <para>Section Add flow:</para>
/// <list type="bullet">
///   <item>Verifies the two-step gate (course → section code → AreOtherFieldsEnabled).</item>
///   <item>Verifies that executing SaveCommand invokes the onSave callback.</item>
/// </list>
///
/// <para>Meeting Add flow:</para>
/// <list type="bullet">
///   <item>Verifies that SaveCommand cannot execute while Title is blank.</item>
///   <item>Verifies that SaveCommand becomes executable once a Title is set.</item>
///   <item>Verifies that executing SaveCommand calls <see cref="IMeetingRepository.Insert"/>
///         and then reloads the <see cref="MeetingStore"/>.</item>
/// </list>
///
/// <para>MeetingListViewModel Add flow:</para>
/// <list type="bullet">
///   <item>Verifies that Add with no semester selected leaves EditVm null.</item>
///   <item>Verifies that Add with a semester selected opens the inline editor.</item>
/// </list>
/// </summary>
public class EditorFlowTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a minimal <see cref="SectionEditViewModel"/> with all lists empty and
    /// injectable save/validation callbacks. Repositories are stubbed to return empty lists.
    /// </summary>
    /// <param name="section">The section model to edit. Pass a new <see cref="Section"/> for Add flows.</param>
    /// <param name="isNew">True for Add/Copy flows.</param>
    /// <param name="isDuplicate">
    ///   The uniqueness predicate. Defaults to always returning false (no duplicate).
    /// </param>
    /// <param name="onSave">
    ///   Called when SaveCommand succeeds. Defaults to a no-op; replace with a capturing
    ///   delegate to assert invocation.
    /// </param>
    private static SectionEditViewModel MakeSectionEditVm(
        Section section,
        bool isNew = true,
        Func<string, string, bool>? isDuplicate = null,
        Func<Section, Task>? onSave = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

        return new SectionEditViewModel(
            section,
            isNew,
            isCopy:            false,
            courses:           new List<Course>(),
            subjects:          new List<Subject>(),
            instructors:       new List<Instructor>(),
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
            isSectionCodeDuplicate: isDuplicate ?? ((_, _) => false),
            onSave:            onSave ?? (_ => Task.CompletedTask),
            blockPatternRepository: blockPatternRepo.Object,
            roomTypes:         new List<SchedulingEnvironmentValue>(),
            defaultBlockLength: null);
    }

    /// <summary>
    /// Builds a minimal <see cref="MeetingEditViewModel"/> backed by a mock repository
    /// and a real <see cref="MeetingStore"/>.
    /// </summary>
    /// <param name="meeting">The meeting model to edit.</param>
    /// <param name="isNew">True for Add flows.</param>
    /// <param name="meetingRepo">Optional pre-configured mock. A default stub is used when null.</param>
    /// <param name="meetingStore">Optional store. A fresh empty store is used when null.</param>
    private static (MeetingEditViewModel Vm, Mock<IMeetingRepository> Repo, MeetingStore Store)
        MakeMeetingEditVm(
            Meeting? meeting = null,
            bool isNew = true,
            Mock<IMeetingRepository>? meetingRepo = null,
            MeetingStore? meetingStore = null)
    {
        meeting ??= new Meeting { SemesterId = "sem-1" };
        var repo  = meetingRepo ?? new Mock<IMeetingRepository>();
        var store = meetingStore ?? new MeetingStore();

        // Stub GetAll so MeetingStore.Reload does not throw.
        repo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Meeting>());

        var vm = new MeetingEditViewModel(
            meeting, isNew,
            repo.Object, store,
            legalStartTimes: new List<LegalStartTime>(),
            allInstructors:  new List<Instructor>(),
            meetingTypes:    new List<SchedulingEnvironmentValue>(),
            allRooms:        new List<Room>(),
            roomTypes:       new List<SchedulingEnvironmentValue>(),
            campuses:        new List<Campus>(),
            allTags:         new List<SchedulingEnvironmentValue>(),
            allResources:    new List<SchedulingEnvironmentValue>(),
            allStaffTypes:   new List<SchedulingEnvironmentValue>());

        return (vm, repo, store);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SectionEditViewModel — Step-Gate Behaviour
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SectionEdit_NewSection_AreOtherFieldsEnabled_IsFalse_Initially()
    {
        var vm = MakeSectionEditVm(new Section());

        // On construction for a brand-new section, neither course nor code is set.
        Assert.False(vm.AreOtherFieldsEnabled);
    }

    [Fact]
    public void SectionEdit_AfterCourseSelected_SectionCode_IsEnabled_OtherFields_StillLocked()
    {
        var course = new Course { Id = "c-1", CalendarCode = "HIST101" };
        var vm = MakeSectionEditVm(new Section());

        // Manually simulate the bindings that OnSelectedSubjectChanged ultimately drives.
        // For the step-gate test we only need SelectedCourseId.
        vm.SelectedCourseId = course.Id;

        Assert.True(vm.IsSectionCodeEnabled,      "Section code field should be enabled once a course is chosen.");
        Assert.False(vm.AreOtherFieldsEnabled,    "Other fields must stay locked until section code is committed.");
    }

    [Fact]
    public void SectionEdit_AfterCourseAndCodeCommitted_AreOtherFieldsEnabled_IsTrue()
    {
        var course = new Course { Id = "c-1", CalendarCode = "HIST101" };
        var vm     = MakeSectionEditVm(new Section());

        vm.SelectedCourseId = course.Id;
        vm.SectionCode      = "A";
        vm.CommitSectionCode();                   // simulates LostFocus on the TextBox

        Assert.True(vm.AreOtherFieldsEnabled,
            "All fields should unlock after course + unique section code are committed.");
    }

    [Fact]
    public void SectionEdit_DuplicateSectionCode_AreOtherFieldsEnabled_RemainsLocked()
    {
        var course = new Course { Id = "c-1", CalendarCode = "HIST101" };
        var vm     = MakeSectionEditVm(
            new Section(),
            isDuplicate: (_, _) => true);          // every code is a "duplicate"

        vm.SelectedCourseId = course.Id;
        vm.SectionCode      = "A";
        vm.CommitSectionCode();

        Assert.NotNull(vm.SectionCodeError);
        Assert.False(vm.AreOtherFieldsEnabled,
            "Other fields must stay locked when the section code fails uniqueness check.");
    }

    [Fact]
    public void SectionEdit_ChangingCourseAfterCommit_RelockFields()
    {
        var course1 = new Course { Id = "c-1", CalendarCode = "HIST101" };
        var course2 = new Course { Id = "c-2", CalendarCode = "ENGL201" };
        var vm      = MakeSectionEditVm(new Section());

        // Fully unlock with course1 + code.
        vm.SelectedCourseId = course1.Id;
        vm.SectionCode      = "A";
        vm.CommitSectionCode();
        Assert.True(vm.AreOtherFieldsEnabled);

        // Changing the course should immediately re-lock other fields.
        vm.SelectedCourseId = course2.Id;
        Assert.False(vm.AreOtherFieldsEnabled,
            "Other fields must re-lock when the selected course changes.");
    }

    [Fact]
    public async Task SectionEdit_SaveCommand_InvokesOnSaveCallback()
    {
        var course = new Course { Id = "c-1", CalendarCode = "HIST101" };

        Section? savedSection = null;
        var vm = MakeSectionEditVm(
            new Section(),
            onSave: s => { savedSection = s; return Task.CompletedTask; });

        // Unlock the editor through the step-gate sequence.
        vm.SelectedCourseId = course.Id;
        vm.SectionCode      = "A";
        vm.CommitSectionCode();
        Assert.True(vm.AreOtherFieldsEnabled);
        Assert.True(vm.SaveCommand.CanExecute(null),
            "SaveCommand should be executable once all fields are unlocked.");

        vm.SaveCommand.Execute(null);

        // Allow any async continuations to complete.
        await Task.Yield();

        Assert.NotNull(savedSection);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MeetingEditViewModel — Title Gate & Save Behaviour
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MeetingEdit_NewMeeting_SaveCommand_CannotExecute_WhenTitleIsBlank()
    {
        var (vm, _, _) = MakeMeetingEditVm();

        // Title defaults to empty string for a new meeting.
        Assert.False(vm.SaveCommand.CanExecute(null),
            "SaveCommand must be disabled when Title is blank.");
    }

    [Fact]
    public void MeetingEdit_WithTitle_SaveCommand_CanExecute()
    {
        var (vm, _, _) = MakeMeetingEditVm();

        vm.Title = "Faculty Senate";

        Assert.True(vm.SaveCommand.CanExecute(null),
            "SaveCommand must become enabled once a non-blank Title is set.");
    }

    [Fact]
    public void MeetingEdit_WhitespaceTitle_SaveCommand_CannotExecute()
    {
        var (vm, _, _) = MakeMeetingEditVm();

        vm.Title = "   ";

        Assert.False(vm.SaveCommand.CanExecute(null),
            "SaveCommand must stay disabled when Title contains only whitespace.");
    }

    [Fact]
    public void MeetingEdit_Save_CallsInsert_ForNewMeeting()
    {
        var repo = new Mock<IMeetingRepository>();
        repo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Meeting>());

        var (vm, _, _) = MakeMeetingEditVm(isNew: true, meetingRepo: repo);

        vm.Title = "Budget Review";
        vm.SaveCommand.Execute(null);

        repo.Verify(r => r.Insert(It.IsAny<Meeting>()), Times.Once,
            "Insert must be called exactly once when saving a new meeting.");
        repo.Verify(r => r.Update(It.IsAny<Meeting>()), Times.Never,
            "Update must NOT be called for a new meeting.");
    }

    [Fact]
    public void MeetingEdit_Save_CallsUpdate_ForExistingMeeting()
    {
        var existing = new Meeting { Id = "m-1", SemesterId = "sem-1", Title = "Old Title" };
        var repo     = new Mock<IMeetingRepository>();
        repo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Meeting>());

        var (vm, _, _) = MakeMeetingEditVm(meeting: existing, isNew: false, meetingRepo: repo);

        vm.Title = "Revised Title";
        vm.SaveCommand.Execute(null);

        repo.Verify(r => r.Update(It.IsAny<Meeting>()), Times.Once,
            "Update must be called exactly once when saving an existing meeting.");
        repo.Verify(r => r.Insert(It.IsAny<Meeting>()), Times.Never,
            "Insert must NOT be called for an existing meeting.");
    }

    [Fact]
    public void MeetingEdit_Save_FiresEditCompletedEvent()
    {
        var (vm, _, _) = MakeMeetingEditVm();

        bool completed = false;
        vm.EditCompleted += () => completed = true;

        vm.Title = "Department Meeting";
        vm.SaveCommand.Execute(null);

        Assert.True(completed, "EditCompleted must fire after a successful save.");
    }

    [Fact]
    public void MeetingEdit_Save_ReloadsStore_SoMeetingsChangedFires()
    {
        var repo  = new Mock<IMeetingRepository>();
        repo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Meeting>());

        var store = new MeetingStore();
        bool storeReloaded = false;
        store.MeetingsChanged += () => storeReloaded = true;

        var (vm, _, _) = MakeMeetingEditVm(isNew: true, meetingRepo: repo, meetingStore: store);

        vm.Title = "Staff Meeting";
        vm.SaveCommand.Execute(null);

        Assert.True(storeReloaded,
            "MeetingStore.MeetingsChanged must fire after save so grid and list refresh.");
    }

    [Fact]
    public void MeetingEdit_Cancel_FiresEditCompleted_WithoutSaving()
    {
        var repo = new Mock<IMeetingRepository>();

        var (vm, _, _) = MakeMeetingEditVm(meetingRepo: repo);

        bool completed = false;
        vm.EditCompleted += () => completed = true;

        vm.Title = "Should Not Be Saved";
        vm.CancelCommand.Execute(null);

        Assert.True(completed, "EditCompleted must fire on cancel.");
        repo.Verify(r => r.Insert(It.IsAny<Meeting>()), Times.Never,
            "Cancel must not persist anything.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MeetingListViewModel — Add command opens the inline editor
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a <see cref="MeetingListViewModel"/> with all repository calls stubbed to
    /// return empty lists. When <paramref name="semester"/> and <paramref name="academicYear"/>
    /// are provided the <see cref="SemesterContext"/> is loaded via its normal
    /// <c>Reload</c> path so that <c>SelectedSemesters</c> is populated.
    /// </summary>
    private static (MeetingListViewModel Vm, SemesterContext SemCtx, WriteLockService LockSvc) MakeMeetingListVm(
        Semester? semester = null,
        AcademicYear? academicYear = null)
    {
        var meetingRepo    = new Mock<IMeetingRepository>();
        var instructorRepo = new Mock<IInstructorRepository>();
        var roomRepo       = new Mock<IRoomRepository>();
        var propertyRepo   = new Mock<ISchedulingEnvironmentRepository>();
        var campusRepo     = new Mock<ICampusRepository>();
        var legalRepo      = new Mock<ILegalStartTimeRepository>();

        // Return empty lists from all repository calls to avoid NullReferenceExceptions.
        meetingRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Meeting>());
        instructorRepo.Setup(r => r.GetAll()).Returns(new List<Instructor>());
        roomRepo.Setup(r => r.GetAll()).Returns(new List<Room>());
        propertyRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<SchedulingEnvironmentValue>());
        campusRepo.Setup(r => r.GetAll()).Returns(new List<Campus>());
        legalRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<LegalStartTime>());

        var semesterContext = new SemesterContext();

        if (semester is not null && academicYear is not null)
        {
            // Populate SemesterContext via its normal Reload path so SelectedSemesters is set.
            // Pass restoreSemesterIds so the test semester is marked selected after reload;
            // otherwise Reload restores the previous selection (empty) and SelectedSemesters
            // stays empty, causing AddCommand to return early without opening the editor.
            var ayRepo  = new Mock<IAcademicYearRepository>();
            var semRepo = new Mock<ISemesterRepository>();
            ayRepo.Setup(r => r.GetAll()).Returns(new List<AcademicYear> { academicYear });
            semRepo.Setup(r => r.GetAll()).Returns(new List<Semester> { semester });
            semesterContext.Reload(ayRepo.Object, semRepo.Object,
                restoreSemesterIds: new HashSet<string> { semester.Id });
        }

        var store       = new MeetingStore();
        var lockService = new WriteLockService();   // IsWriter = false by default; caller acquires if needed

        var vm = new MeetingListViewModel(
            meetingRepo.Object,
            instructorRepo.Object,
            roomRepo.Object,
            propertyRepo.Object,
            campusRepo.Object,
            legalRepo.Object,
            semesterContext,
            store,
            lockService);

        return (vm, semesterContext, lockService);
    }

    [Fact]
    public void MeetingList_Add_WithNoSemesterSelected_LeavesEditVmNull()
    {
        var (vm, _, _) = MakeMeetingListVm(semester: null, academicYear: null);

        // Even in reader mode Execute is a no-op (CanExecute = false), and even if it
        // ran, there is no semester to add to — either way EditVm stays null.
        vm.AddCommand.Execute(null);

        Assert.Null(vm.EditVm);
    }

    [Fact]
    public void MeetingList_Add_WithSemesterSelected_OpensEditor()
    {
        var ay       = new AcademicYear { Id = "ay-1", Name = "2025-2026" };
        var semester = new Semester     { Id = "sem-1", Name = "Fall", AcademicYearId = ay.Id };

        var (vm, _, lockSvc) = MakeMeetingListVm(semester: semester, academicYear: ay);

        // Add requires the write lock; acquire it for the duration of this test.
        var tempDb = Path.GetTempFileName();
        try
        {
            lockSvc.TryAcquire(tempDb);

            vm.AddCommand.Execute(null);

            Assert.NotNull(vm.EditVm);
            Assert.True(vm.EditVm!.IsNew);
        }
        finally
        {
            lockSvc.Dispose();
            try { File.Delete(tempDb); } catch { /* best-effort */ }
            try { File.Delete(tempDb + ".lock"); } catch { /* best-effort */ }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MeetingListViewModel — Writer-mode gate: Edit/Delete/LockStateChanged
    // Complements the reader-mode tests in WriteLockReadOnlyTests. Together they
    // prove the full guard: blocked when reader, open when writer + selection.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With a card selected and the write lock held, EditCommand must be executable.
    /// Before the fix, CanEdit only checked selection — this test would have passed
    /// even in reader mode, masking the missing write guard.
    /// </summary>
    [Fact]
    public void MeetingList_EditCommand_WriterMode_WithSelection_CanExecute()
    {
        var (vm, _, lockSvc) = MakeMeetingListVm();
        var tempDb = Path.GetTempFileName();
        try
        {
            lockSvc.TryAcquire(tempDb);

            vm.SelectedItem = new MeetingListItemViewModel(
                new Meeting { Id = "m-1", SemesterId = "s-1" },
                new Dictionary<string, Instructor>(),
                new Dictionary<string, Room>(),
                new Dictionary<string, SchedulingEnvironmentValue>(),
                new Dictionary<string, SchedulingEnvironmentValue>());

            Assert.True(vm.EditCommand.CanExecute(null),
                "EditCommand must be enabled when writer holds the lock and a card is selected.");
        }
        finally
        {
            lockSvc.Dispose();
            try { File.Delete(tempDb); } catch { /* best-effort */ }
            try { File.Delete(tempDb + ".lock"); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// With a card selected and the write lock held, DeleteCommand must be executable.
    /// </summary>
    [Fact]
    public void MeetingList_DeleteCommand_WriterMode_WithSelection_CanExecute()
    {
        var (vm, _, lockSvc) = MakeMeetingListVm();
        var tempDb = Path.GetTempFileName();
        try
        {
            lockSvc.TryAcquire(tempDb);

            vm.SelectedItem = new MeetingListItemViewModel(
                new Meeting { Id = "m-1", SemesterId = "s-1" },
                new Dictionary<string, Instructor>(),
                new Dictionary<string, Room>(),
                new Dictionary<string, SchedulingEnvironmentValue>(),
                new Dictionary<string, SchedulingEnvironmentValue>());

            Assert.True(vm.DeleteCommand.CanExecute(null),
                "DeleteCommand must be enabled when writer holds the lock and a card is selected.");
        }
        finally
        {
            lockSvc.Dispose();
            try { File.Delete(tempDb); } catch { /* best-effort */ }
            try { File.Delete(tempDb + ".lock"); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Verifies that IsWriteEnabled and command CanExecute reflect the live lock state:
    /// false before TryAcquire, true after. The CanExecute predicates read IsWriter directly,
    /// so they update synchronously — no dispatcher pump needed.
    /// (The CanExecuteChanged events are dispatched via Dispatcher.UIThread.Post and cannot
    /// be tested reliably without a running Avalonia UI loop.)
    /// </summary>
    [Fact]
    public void MeetingList_LockStateTransition_IsWriteEnabled_AndCanExecute_Reflect_LiveLockState()
    {
        var (vm, _, lockSvc) = MakeMeetingListVm();
        var tempDb = Path.GetTempFileName();
        try
        {
            // Reader mode: commands blocked.
            Assert.False(vm.IsWriteEnabled);
            Assert.False(vm.AddCommand.CanExecute(null));
            Assert.False(vm.EditCommand.CanExecute(null));
            Assert.False(vm.DeleteCommand.CanExecute(null));

            lockSvc.TryAcquire(tempDb);

            // Writer mode: IsWriteEnabled and Add CanExecute update immediately — both read
            // IsWriter directly, so no event dispatch is needed. Edit/Delete also require a
            // selection to return true; those are covered by the dedicated writer+selection tests.
            Assert.True(vm.IsWriteEnabled,
                "IsWriteEnabled must return true once the lock is acquired.");
            Assert.True(vm.AddCommand.CanExecute(null),
                "AddCommand.CanExecute must be true in writer mode.");
        }
        finally
        {
            lockSvc.Dispose();
            try { File.Delete(tempDb); } catch { /* best-effort */ }
            try { File.Delete(tempDb + ".lock"); } catch { /* best-effort */ }
        }
    }
}

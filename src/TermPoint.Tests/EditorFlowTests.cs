using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TermPoint.Tests;

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
        Func<Section, Task>? onSave = null,
        IReadOnlyList<BlockPattern>? blockPatterns = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll())
            .Returns((blockPatterns ?? new List<BlockPattern>()).ToList());

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
    // MeetingEditViewModel — Room Availability Browser wiring
    //
    // The solver/domain layer (occupancy, classification, solution generation,
    // slot→spec mapping) is covered source-agnostically in RoomAvailabilityTests
    // and RoomAvailabilityIntegrationTests, and those tests already include Events
    // as room occupants. What was previously untested is the Event editor's own
    // wiring around the browser: the open gate (CanOpenRoomBrowser), the factory
    // invocation with the correct specs, and the transfer-back of an accepted
    // solution into the slot rows (AcceptBrowserSolution). These tests close that
    // gap by driving a real MeetingEditViewModel through those commands with a
    // stubbed CreateRoomBrowser factory.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a time slot to the meeting and gives it a committed block length so the
    /// slot satisfies <c>CanOpenRoomBrowser</c>. Returns the created slot for further setup.
    /// </summary>
    private static SectionMeetingViewModel AddSlotWithDuration(
        MeetingEditViewModel vm, double blockLengthHours = 1.0)
    {
        vm.AddSlotCommand.Execute(null);
        var slot = vm.Slots[^1];
        slot.SelectedBlockLength = blockLengthHours;   // commits a duration directly
        return slot;
    }

    /// <summary>
    /// Builds a <see cref="RoomAvailabilityBrowserViewModel"/> suitable as a
    /// <c>CreateRoomBrowser</c> factory return value. All data lists are empty so it
    /// computes no solutions; the accept/cancel callbacks are forwarded from the editor
    /// so a test can invoke them to simulate the user accepting or cancelling.
    /// </summary>
    private static RoomAvailabilityBrowserViewModel MakeStubBrowser(
        IReadOnlyList<MeetingSpec> specs,
        Action<IReadOnlyList<SpecSolution>> accept,
        Action cancel)
    {
        return new RoomAvailabilityBrowserViewModel(
            specs,
            allRooms:         new List<Room>(),
            legalStartTimes:  new List<LegalStartTime>(),
            blockPatterns:    new List<BlockPattern>(),
            semesterSections: new List<Section>(),
            semesterMeetings: new List<Meeting>(),
            excludeSectionId: null,
            excludeMeetingId: null,
            semesterId:       "sem-1",
            semesterName:     "Fall",
            semesterColor:    "#FFFFFF",
            setGhostBlocks:   _ => { },
            onAccept:         accept,
            onCancel:         cancel);
    }

    [Fact]
    public void MeetingEdit_OpenRoomBrowser_NoSlots_CannotExecute()
    {
        var (vm, _, _) = MakeMeetingEditVm();

        Assert.Empty(vm.Slots);
        Assert.False(vm.OpenRoomBrowserCommand.CanExecute(null),
            "Room browser must be unavailable when the meeting has no time slots.");
    }

    [Fact]
    public void MeetingEdit_OpenRoomBrowser_SlotWithoutDuration_CannotExecute()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        vm.AddSlotCommand.Execute(null);   // slot added but no block length committed

        Assert.False(vm.OpenRoomBrowserCommand.CanExecute(null),
            "Room browser must be unavailable while any slot is missing a duration.");
    }

    [Fact]
    public void MeetingEdit_OpenRoomBrowser_SlotWithDuration_CanExecute()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        AddSlotWithDuration(vm);

        Assert.True(vm.OpenRoomBrowserCommand.CanExecute(null),
            "Room browser must be available once every slot has a duration.");
    }

    [Fact]
    public void MeetingEdit_OpenRoomBrowser_InvokesFactory_WithSpecs_AndOpensPanel()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        var slot = AddSlotWithDuration(vm, blockLengthHours: 1.5);
        slot.SelectedDay = 1;   // Monday

        IReadOnlyList<MeetingSpec>? capturedSpecs = null;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            capturedSpecs = specs;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);

        Assert.True(vm.IsRoomBrowserOpen, "Accepting the factory result must mark the panel open.");
        Assert.NotNull(vm.RoomBrowserVm);

        // The slot's day/duration must reach the solver as a spec.
        Assert.NotNull(capturedSpecs);
        Assert.Single(capturedSpecs!);
        Assert.Equal(1, capturedSpecs![0].Day);
        Assert.Equal(90, capturedSpecs[0].DurationMinutes);
        Assert.Null(vm.LastErrorMessage);
    }

    [Fact]
    public void MeetingEdit_OpenRoomBrowser_AllRemoteSlots_SetsError_DoesNotOpen()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        var slot = AddSlotWithDuration(vm);
        // Mark the only slot Remote — remote slots are excluded from the solver.
        slot.SelectedRoomType =
            slot.RoomTypeOptions.First(o => o.Id == SectionDaySchedule.RemoteRoomTypeId);

        bool factoryCalled = false;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            factoryCalled = true;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);

        Assert.False(factoryCalled, "Factory must not run when every slot is remote.");
        Assert.False(vm.IsRoomBrowserOpen);
        Assert.NotNull(vm.LastErrorMessage);
    }

    [Fact]
    public void MeetingEdit_AcceptBrowserSolution_FillsBlankSlotFields_AndClosesPanel()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        var slot = AddSlotWithDuration(vm, blockLengthHours: 1.0);
        // Day left at 0 ("any"), start time and room left blank.

        Action<IReadOnlyList<SpecSolution>>? capturedAccept = null;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            capturedAccept = accept;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);
        Assert.NotNull(capturedAccept);

        // Simulate the user accepting a solution for spec index 0.
        capturedAccept!(new List<SpecSolution>
        {
            new(SpecIndex: 0, Day: 3, StartMinutes: 540, DurationMinutes: 60,
                RoomId: "room-1", RoomLabel: "A 101")
        });

        Assert.Equal(3, slot.SelectedDay);
        Assert.Equal(540, slot.SelectedStartTime);
        Assert.Equal("room-1", slot.SelectedRoomId);
        Assert.False(vm.IsRoomBrowserOpen, "Accepting a solution must close the browser panel.");
    }

    [Fact]
    public void MeetingEdit_AcceptBrowserSolution_DoesNotOverwriteAlreadyChosenDay()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        var slot = AddSlotWithDuration(vm, blockLengthHours: 1.0);
        slot.SelectedDay = 1;   // Monday already chosen by the user

        Action<IReadOnlyList<SpecSolution>>? capturedAccept = null;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            capturedAccept = accept;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);

        capturedAccept!(new List<SpecSolution>
        {
            new(SpecIndex: 0, Day: 5, StartMinutes: 600, DurationMinutes: 60,
                RoomId: "room-9", RoomLabel: "B 9")
        });

        // The user-chosen day must be preserved; blank fields still get filled.
        Assert.Equal(1, slot.SelectedDay);
        Assert.Equal(600, slot.SelectedStartTime);
        Assert.Equal("room-9", slot.SelectedRoomId);
    }

    [Fact]
    public void MeetingEdit_CloseRoomBrowser_ClearsPanel()
    {
        var (vm, _, _) = MakeMeetingEditVm();
        AddSlotWithDuration(vm);
        vm.CreateRoomBrowser = MakeStubBrowser;

        vm.OpenRoomBrowserCommand.Execute(null);
        Assert.True(vm.IsRoomBrowserOpen);

        vm.CloseRoomBrowserCommand.Execute(null);

        Assert.False(vm.IsRoomBrowserOpen);
        Assert.Null(vm.RoomBrowserVm);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SectionEditViewModel — Room Availability Browser wiring
    //
    // The Section editor has its OWN copy of the room-browser methods (separate from
    // the Event editor's), so the Event-editor tests above do not exercise it. Items
    // 1–3 below mirror the Event coverage for symmetry. The marquee test is the last
    // one: it guards behavior that exists ONLY in the Section editor — pattern
    // coupling. When meetings are coupled (apply a pattern, then set one field on the
    // lead row and it propagates to the others), AcceptBrowserSolution must first call
    // TearDownPatternCoupling(); otherwise writing the lead row's per-day start time
    // would propagate and silently overwrite the other rows, collapsing a multi-time
    // browser solution back to a single time.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a meeting row to the section and gives it a committed block length so the
    /// row satisfies <c>CanOpenRoomBrowser</c>. Returns the created row for further setup.
    /// </summary>
    private static SectionMeetingViewModel AddMeetingWithDuration(
        SectionEditViewModel vm, double blockLengthHours = 1.0)
    {
        vm.AddMeetingCommand.Execute(null);
        var meeting = vm.Meetings[^1];
        meeting.SelectedBlockLength = blockLengthHours;
        return meeting;
    }

    [Fact]
    public void SectionEdit_OpenRoomBrowser_NoMeetings_CannotExecute()
    {
        var vm = MakeSectionEditVm(new Section());

        Assert.Empty(vm.Meetings);
        Assert.False(vm.OpenRoomBrowserCommand.CanExecute(null),
            "Room browser must be unavailable when the section has no meetings.");
    }

    [Fact]
    public void SectionEdit_OpenRoomBrowser_MeetingWithoutDuration_CannotExecute()
    {
        var vm = MakeSectionEditVm(new Section());
        vm.AddMeetingCommand.Execute(null);   // meeting added but no block length committed

        Assert.False(vm.OpenRoomBrowserCommand.CanExecute(null),
            "Room browser must be unavailable while any meeting is missing a duration.");
    }

    [Fact]
    public void SectionEdit_OpenRoomBrowser_MeetingWithDuration_CanExecute()
    {
        var vm = MakeSectionEditVm(new Section());
        AddMeetingWithDuration(vm);

        Assert.True(vm.OpenRoomBrowserCommand.CanExecute(null),
            "Room browser must be available once every meeting has a duration.");
    }

    [Fact]
    public void SectionEdit_OpenRoomBrowser_InvokesFactory_WithSpecs_AndOpensPanel()
    {
        var vm = MakeSectionEditVm(new Section());
        var meeting = AddMeetingWithDuration(vm, blockLengthHours: 1.5);
        meeting.SelectedDay = 1;   // Monday

        IReadOnlyList<MeetingSpec>? capturedSpecs = null;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            capturedSpecs = specs;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);

        Assert.True(vm.IsRoomBrowserOpen);
        Assert.NotNull(vm.RoomBrowserVm);
        Assert.NotNull(capturedSpecs);
        Assert.Single(capturedSpecs!);
        Assert.Equal(1, capturedSpecs![0].Day);
        Assert.Equal(90, capturedSpecs[0].DurationMinutes);
        Assert.Null(vm.LastErrorMessage);
    }

    [Fact]
    public void SectionEdit_OpenRoomBrowser_AllRemoteMeetings_SetsError_DoesNotOpen()
    {
        var vm = MakeSectionEditVm(new Section());
        var meeting = AddMeetingWithDuration(vm);
        meeting.SelectedRoomType =
            meeting.RoomTypeOptions.First(o => o.Id == SectionDaySchedule.RemoteRoomTypeId);

        bool factoryCalled = false;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            factoryCalled = true;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);

        Assert.False(factoryCalled, "Factory must not run when every meeting is remote.");
        Assert.False(vm.IsRoomBrowserOpen);
        Assert.NotNull(vm.LastErrorMessage);
    }

    [Fact]
    public void SectionEdit_AcceptBrowserSolution_FillsBlankMeetingFields_AndClosesPanel()
    {
        var vm = MakeSectionEditVm(new Section());
        var meeting = AddMeetingWithDuration(vm, blockLengthHours: 1.0);
        // Day left at 0 ("any"), start time and room left blank.

        Action<IReadOnlyList<SpecSolution>>? capturedAccept = null;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            capturedAccept = accept;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);
        Assert.NotNull(capturedAccept);

        capturedAccept!(new List<SpecSolution>
        {
            new(SpecIndex: 0, Day: 3, StartMinutes: 540, DurationMinutes: 60,
                RoomId: "room-1", RoomLabel: "A 101")
        });

        Assert.Equal(3, meeting.SelectedDay);
        Assert.Equal(540, meeting.SelectedStartTime);
        Assert.Equal("room-1", meeting.SelectedRoomId);
        Assert.False(vm.IsRoomBrowserOpen, "Accepting a solution must close the browser panel.");
    }

    /// <summary>
    /// The marquee Section-only test. With a pattern applied, the meeting rows are
    /// coupled: block length set on the lead row propagates to the others, but start
    /// time has NOT yet propagated, so start-time coupling is still live. Accepting a
    /// solution with a different start time per day must NOT collapse the rows to the
    /// lead's time — proving AcceptBrowserSolution tore down the coupling first.
    /// </summary>
    [Fact]
    public void SectionEdit_AcceptBrowserSolution_CoupledMeetings_KeepsPerDayStartTimes()
    {
        // One MWF pattern (days Mon/Wed/Fri) loaded into pattern slot 1.
        var mwf = new BlockPattern { Id = "mwf", Name = "MWF", Days = new List<int> { 1, 3, 5 } };
        var vm  = MakeSectionEditVm(new Section(), blockPatterns: new[] { mwf });

        // Apply the pattern: creates 3 coupled rows (Mon, Wed, Fri), no durations yet.
        vm.ApplyPattern1Command.Execute(null);
        Assert.Equal(3, vm.Meetings.Count);

        // Set the lead row's block length; coupling propagates it to the followers and
        // decouples ONLY that field. Start-time coupling remains live — the risky state.
        vm.Meetings[0].SelectedBlockLength = 1.0;
        Assert.All(vm.Meetings, m => Assert.Equal(1.0, m.SelectedBlockLength));

        Action<IReadOnlyList<SpecSolution>>? capturedAccept = null;
        vm.CreateRoomBrowser = (specs, accept, cancel) =>
        {
            capturedAccept = accept;
            return MakeStubBrowser(specs, accept, cancel);
        };

        vm.OpenRoomBrowserCommand.Execute(null);
        Assert.NotNull(capturedAccept);

        // Solution assigns a DIFFERENT start time to each day.
        capturedAccept!(new List<SpecSolution>
        {
            new(SpecIndex: 0, Day: 1, StartMinutes: 480, DurationMinutes: 60, RoomId: "r1", RoomLabel: "A 1"),
            new(SpecIndex: 1, Day: 3, StartMinutes: 600, DurationMinutes: 60, RoomId: "r1", RoomLabel: "A 1"),
            new(SpecIndex: 2, Day: 5, StartMinutes: 720, DurationMinutes: 60, RoomId: "r1", RoomLabel: "A 1"),
        });

        // Each row must keep its own start time. If coupling had survived, the lead's
        // 0800 would have propagated to Wed and Fri before their slots were filled.
        Assert.Equal(480, vm.Meetings[0].SelectedStartTime);
        Assert.Equal(600, vm.Meetings[1].SelectedStartTime);
        Assert.Equal(720, vm.Meetings[2].SelectedStartTime);
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
        var meetingRepo      = new Mock<IMeetingRepository>();
        var instructorRepo   = new Mock<IInstructorRepository>();
        var roomRepo         = new Mock<IRoomRepository>();
        var propertyRepo     = new Mock<ISchedulingEnvironmentRepository>();
        var campusRepo       = new Mock<ICampusRepository>();
        var legalRepo        = new Mock<ILegalStartTimeRepository>();
        var sectionRepo      = new Mock<ISectionRepository>();
        var blockPatternRepo = new Mock<IBlockPatternRepository>();

        // Return empty lists from all repository calls to avoid NullReferenceExceptions.
        meetingRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Meeting>());
        instructorRepo.Setup(r => r.GetAll()).Returns(new List<Instructor>());
        roomRepo.Setup(r => r.GetAll()).Returns(new List<Room>());
        propertyRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<SchedulingEnvironmentValue>());
        campusRepo.Setup(r => r.GetAll()).Returns(new List<Campus>());
        legalRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<LegalStartTime>());
        sectionRepo.Setup(r => r.GetAll(It.IsAny<string>())).Returns(new List<Section>());
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());

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
            sectionRepo.Object,
            blockPatternRepo.Object,
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

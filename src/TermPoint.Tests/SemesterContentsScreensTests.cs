using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for step 8b of spec item 8: the three screens that empty or delete semesters and academic
/// years (<see cref="EmptySemesterViewModel"/>, <see cref="SemesterManagerViewModel"/> and
/// <see cref="AcademicYearListViewModel"/>) go through <see cref="SemesterContentsService"/>, so they
/// follow the same rules (everything attached to a semester counts, a loaded semester is protected
/// wherever it sits in a multi-semester selection, and nothing is orphaned).
/// <para>
/// Real temp-file database, real repositories, a real <see cref="SemesterContext"/>, a write lock held
/// as writer, and a fake <see cref="IDialogService"/> that records what the user was asked. The
/// service's own rules are covered by <see cref="SemesterContentsServiceTests"/>; these tests check
/// that each screen uses them and presents the results.
/// </para>
/// </summary>
public sealed class SemesterContentsScreensTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>The six kinds of row tied to a semester; used to seed exactly one kind.</summary>
    public enum Kind { Sections, Meetings, Releases, Commitments, SchedulingNotes, ProgramWatches }

    // Distinct counts per kind, so a mix-up between two kinds cannot go unnoticed.
    private static readonly SemesterContents FullContents = new(3, 2, 4, 5, 6, 7);
    private static readonly SemesterContents SingleOfEach = new(1, 1, 1, 1, 1, 1);
    private static readonly SemesterContents PairOfEach   = new(2, 2, 2, 2, 2, 2);

    /// <summary>Protects the developer's real settings.json: SemesterContext.Reload saves AppSettings.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer (so write commands are enabled).</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;
    private readonly MeetingRepository _meetingRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SchedulingNoteRepository _noteRepo;
    private readonly ProgramWatchRepository _watchRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly LegalStartTimeRepository _legalRepo;
    private readonly SemesterContext _context = new();
    private readonly SemesterContentsService _service;

    // Ids of the seeded structure: two academic years of two semesters each.
    private readonly string _ay1 = Guid.NewGuid().ToString();     // "2024-2025"
    private readonly string _ay2 = Guid.NewGuid().ToString();     // "2025-2026"
    private readonly string _fall   = Guid.NewGuid().ToString();  // AY1, FullContents
    private readonly string _winter = Guid.NewGuid().ToString();  // AY1, SingleOfEach
    private readonly string _spring = Guid.NewGuid().ToString();  // AY2, PairOfEach
    private readonly string _summer = Guid.NewGuid().ToString();  // AY2, empty

    /// <summary>
    /// Builds the repositories and the service, then seeds two academic years (2024-2025: Fall,
    /// Winter; 2025-2026: Spring, Summer) with legal start times for both, and rows of all six kinds
    /// in Fall, Winter and Spring. Summer is left empty.
    /// </summary>
    public SemesterContentsScreensTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _meetingRepo = new MeetingRepository(db);
        _releaseRepo = new ReleaseRepository(db);
        _commitmentRepo = new InstructorCommitmentRepository(db);
        _noteRepo = new SchedulingNoteRepository(db);
        _watchRepo = new ProgramWatchRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _legalRepo = new LegalStartTimeRepository(db);

        _service = new SemesterContentsService(
            db, _sectionRepo, _meetingRepo, _releaseRepo, _commitmentRepo, _noteRepo,
            _watchRepo, _semesterRepo, _ayRepo, _legalRepo, _context);

        _ayRepo.Insert(new AcademicYear { Id = _ay1, Name = "2024-2025" });
        _ayRepo.Insert(new AcademicYear { Id = _ay2, Name = "2025-2026" });
        _semesterRepo.Insert(new Semester { Id = _fall,   AcademicYearId = _ay1, Name = "Fall",   SortOrder = 0 });
        _semesterRepo.Insert(new Semester { Id = _winter, AcademicYearId = _ay1, Name = "Winter", SortOrder = 1 });
        _semesterRepo.Insert(new Semester { Id = _spring, AcademicYearId = _ay2, Name = "Spring", SortOrder = 0 });
        _semesterRepo.Insert(new Semester { Id = _summer, AcademicYearId = _ay2, Name = "Summer", SortOrder = 1 });
        foreach (var ay in new[] { _ay1, _ay2 })
        {
            _legalRepo.Insert(new LegalStartTime { BlockLength = 1.5, StartTimes = [510, 600] }, ay);
            _legalRepo.Insert(new LegalStartTime { BlockLength = 3.0, StartTimes = [510, 690] }, ay);
        }

        Seed(_fall, FullContents);
        Seed(_winter, SingleOfEach);
        Seed(_spring, PairOfEach);
    }

    /// <summary>Releases the temp database and lock, and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A dialog service that records every confirmation text and error it is shown and answers
    /// confirmations with a fixed, changeable answer.
    /// </summary>
    private sealed class FakeDialog(bool answer) : IDialogService
    {
        /// <summary>What <see cref="Confirm"/> returns.</summary>
        public bool Answer { get; set; } = answer;

        /// <summary>Every confirmation message the user was asked, in order.</summary>
        public List<string> Confirmations { get; } = new();

        /// <inheritdoc/>
        public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        {
            Confirmations.Add(message);
            return Task.FromResult(Answer);
        }

        /// <inheritdoc/>
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>
    /// Inserts the number of rows of each kind given by <paramref name="counts"/> into a semester,
    /// using each kind's own repository.
    /// </summary>
    /// <param name="semesterId">The semester to fill.</param>
    /// <param name="counts">How many rows of each kind to insert.</param>
    private void Seed(string semesterId, SemesterContents counts)
    {
        for (var i = 0; i < counts.Sections; i++)
            _sectionRepo.Insert(new Section { SemesterId = semesterId, SectionCode = $"S{i}" });

        for (var i = 0; i < counts.Meetings; i++)
            _meetingRepo.Insert(new Meeting { SemesterId = semesterId, Title = $"Meeting {i}" });

        for (var i = 0; i < counts.Releases; i++)
            _releaseRepo.Insert(new Release { SemesterId = semesterId, InstructorId = "inst-1", Title = $"Release {i}" });

        for (var i = 0; i < counts.Commitments; i++)
            _commitmentRepo.Insert(new InstructorCommitment
            {
                SemesterId = semesterId, InstructorId = "inst-1", Name = $"Commitment {i}",
                Day = 1, StartMinutes = 480, EndMinutes = 540
            });

        // One scheduling note per (instructor, semester), so vary the instructor.
        for (var i = 0; i < counts.SchedulingNotes; i++)
            _noteRepo.Save(new SchedulingNote { SemesterId = semesterId, InstructorId = $"inst-{i}", Text = "note" });

        for (var i = 0; i < counts.ProgramWatches; i++)
            _watchRepo.Save(semesterId, new ProgramWatch { Name = $"Watch {i}" });
    }

    /// <summary>Returns a <see cref="SemesterContents"/> with 1 in the given kind and 0 elsewhere.</summary>
    /// <param name="kind">The one kind that has a row.</param>
    private static SemesterContents OnlyOne(Kind kind) => new(
        kind == Kind.Sections ? 1 : 0,
        kind == Kind.Meetings ? 1 : 0,
        kind == Kind.Releases ? 1 : 0,
        kind == Kind.Commitments ? 1 : 0,
        kind == Kind.SchedulingNotes ? 1 : 0,
        kind == Kind.ProgramWatches ? 1 : 0);

    /// <summary>Loads the given semesters of an academic year into the context (the "loaded" set).</summary>
    /// <param name="yearId">The academic year the semesters belong to.</param>
    /// <param name="semesterIds">The semesters to load, in any order.</param>
    private void Load(string yearId, params string[] semesterIds) =>
        _context.Reload(_ayRepo, _semesterRepo, yearId, semesterIds.ToHashSet());

    /// <summary>Makes a year the selected year with NO semester ticked, as the main view can be.</summary>
    /// <param name="yearId">The year to select.</param>
    private void SelectYearWithNothingTicked(string yearId) =>
        _context.Reload(_ayRepo, _semesterRepo, yearId, new HashSet<string>());

    /// <summary>Builds an Empty Semester view model with the given semester picked in its two pickers.</summary>
    /// <param name="yearId">The academic year to pick.</param>
    /// <param name="semesterId">The semester to pick.</param>
    private EmptySemesterViewModel NewEmptyVm(string yearId, string semesterId)
    {
        var vm = new EmptySemesterViewModel(_ayRepo, _semesterRepo, _service, _context, _fx.Lock);
        vm.SelectedAcademicYear = vm.AcademicYears.Single(a => a.Id == yearId);
        vm.SelectedSemester = vm.Semesters.Single(s => s.Id == semesterId);
        return vm;
    }

    /// <summary>Builds the Semester Manager for one year with one semester row selected.</summary>
    /// <param name="yearId">The academic year to manage.</param>
    /// <param name="semesterId">The semester row to select.</param>
    /// <param name="dialog">The dialog service answering the confirmation.</param>
    private SemesterManagerViewModel NewManagerVm(string yearId, string semesterId, IDialogService dialog)
    {
        var vm = new SemesterManagerViewModel(yearId, _ayRepo, _semesterRepo, _service, _context, dialog, _fx.Lock);
        vm.SelectedSemester = vm.Semesters.Single(s => s.Semester.Id == semesterId);
        return vm;
    }

    /// <summary>Builds the Academic Years list with one year selected.</summary>
    /// <param name="yearId">The academic year to select.</param>
    /// <param name="dialog">The dialog service answering the confirmation.</param>
    private AcademicYearListViewModel NewYearListVm(string yearId, IDialogService dialog)
    {
        var vm = new AcademicYearListViewModel(_ayRepo, _semesterRepo, _service, _context, _legalRepo, dialog, _fx.Lock);
        vm.SelectedAcademicYear = vm.AcademicYears.Single(a => a.Id == yearId);
        return vm;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Empty Semester
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A semester with content shows the full per-kind list, not just sections.</summary>
    [Fact]
    public void EmptySemester_ContentsSummary_ListsEveryKindPresent()
    {
        var vm = NewEmptyVm(_ay1, _fall);

        Assert.Equal(FullContents, vm.Contents);
        Assert.Equal(
            "This semester contains 3 sections, 2 meetings, 4 releases, 5 scheduled commitments, " +
            "6 scheduling notes, 7 program watches.",
            vm.ContentsSummary);
    }

    /// <summary>A semester holding only non-section rows is described by them (it is not "0 sections").</summary>
    [Fact]
    public void EmptySemester_ContentsSummary_CountsRowsOtherThanSections()
    {
        Seed(_summer, OnlyOne(Kind.Releases));

        var vm = NewEmptyVm(_ay2, _summer);

        Assert.Equal("This semester contains 1 release.", vm.ContentsSummary);
    }

    /// <summary>A semester with nothing in it says so.</summary>
    [Fact]
    public void EmptySemester_ContentsSummary_EmptySemester()
    {
        var vm = NewEmptyVm(_ay2, _summer);

        Assert.Equal("This semester is empty.", vm.ContentsSummary);
    }

    /// <summary>With no semester selected there is no summary and nothing can be emptied.</summary>
    [Fact]
    public void EmptySemester_NoSemesterSelected_HasNoSummaryAndCannotEmpty()
    {
        var vm = NewEmptyVm(_ay1, _fall);

        vm.SelectedSemester = null;

        Assert.Null(vm.Contents);
        Assert.Null(vm.ContentsSummary);
        Assert.False(vm.CanEmpty);
        Assert.False(vm.EmptyCommand.CanExecute(null));
    }

    /// <summary>
    /// Confirming removes all six kinds for the chosen semester only. The confirmation was shown the
    /// semester's name and a description naming every kind present; afterwards the screen reports the
    /// semester empty and the button is disabled again.
    /// </summary>
    [Fact]
    public async Task EmptySemester_Confirmed_RemovesAllSixKinds_AfterListingThemInTheConfirmation()
    {
        var vm = NewEmptyVm(_ay1, _fall);
        var asked = new List<(string Name, string Description)>();
        vm.ConfirmEmpty = (name, description) =>
        {
            asked.Add((name, description));
            return Task.FromResult(true);
        };

        Assert.True(vm.CanEmpty);
        await vm.EmptyCommand.ExecuteAsync(null);

        // The user was asked once, with every kind present spelled out.
        var (askedName, askedDescription) = Assert.Single(asked);
        Assert.Equal("2024-2025 Fall", askedName);   // academic year + semester, shown in bold in the dialog
        Assert.Equal(FullContents.Describe(), askedDescription);
        foreach (var noun in new[] { "sections", "meetings", "releases", "scheduled commitments", "scheduling notes", "program watches" })
            Assert.Contains(noun, askedDescription);

        // All six kinds are gone from Fall; other semesters are untouched.
        Assert.True(_service.IsEmpty(_fall));
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));
        Assert.Equal(PairOfEach, _service.CountContents(_spring));

        // The screen shows the new state and the follow-on advice.
        Assert.True(vm.Contents!.IsEmpty);
        Assert.Equal("This semester is empty.", vm.ContentsSummary);
        Assert.Equal("Done. Everything has been removed from Fall.",
            vm.StatusMessage);
        Assert.False(vm.CanEmpty);
        Assert.False(vm.EmptyCommand.CanExecute(null));
    }

    /// <summary>Declining the confirmation deletes nothing and leaves the screen as it was.</summary>
    [Fact]
    public async Task EmptySemester_Declined_DeletesNothing()
    {
        var vm = NewEmptyVm(_ay1, _fall);
        var asked = 0;
        vm.ConfirmEmpty = (_, _) => { asked++; return Task.FromResult(false); };

        await vm.EmptyCommand.ExecuteAsync(null);

        Assert.Equal(1, asked);
        Assert.Equal(FullContents, _service.CountContents(_fall));
        Assert.Equal(FullContents, vm.Contents);
        Assert.Null(vm.StatusMessage);
        Assert.True(vm.CanEmpty);
    }

    /// <summary>A semester with nothing in it has nothing to empty.</summary>
    [Fact]
    public void EmptySemester_CanEmpty_IsFalseWhenTheSemesterIsEmpty()
    {
        var vm = NewEmptyVm(_ay2, _summer);

        Assert.False(vm.CanEmpty);
        Assert.False(vm.EmptyCommand.CanExecute(null));
    }

    /// <summary>The currently loaded semester cannot be emptied, and the screen says why.</summary>
    [Fact]
    public void EmptySemester_CanEmpty_IsFalseWhenTheSemesterIsLoaded()
    {
        Load(_ay1, _fall);

        var vm = NewEmptyVm(_ay1, _fall);

        Assert.True(vm.IsCurrentlyLoaded);
        Assert.False(vm.CanEmpty);
        Assert.False(vm.EmptyCommand.CanExecute(null));
        Assert.Equal(
            "This semester is currently loaded. To empty it, first switch to a different semester in the main view.",
            vm.StatusMessage);
    }

    /// <summary>
    /// Regression for the multi-semester bug: only the FIRST selected semester used to be guarded.
    /// Winter is the second of two loaded semesters and must be protected too, while a semester that
    /// is not loaded at all can still be emptied.
    /// </summary>
    [Fact]
    public void EmptySemester_CanEmpty_IsFalseWhenTheSemesterIsLoadedAsTheSecondOfTwo()
    {
        Load(_ay1, _fall, _winter);
        Assert.Equal(_fall, _context.SelectedSemesterDisplay!.Semester.Id);   // Winter is not the first

        var winter = NewEmptyVm(_ay1, _winter);
        Assert.True(winter.IsCurrentlyLoaded);
        Assert.False(winter.CanEmpty);
        Assert.False(winter.EmptyCommand.CanExecute(null));

        var spring = NewEmptyVm(_ay2, _spring);   // a different year, not loaded
        Assert.False(spring.IsCurrentlyLoaded);
        Assert.True(spring.CanEmpty);
    }

    /// <summary>
    /// The guard follows the main view while the screen stays open: loading the selected semester
    /// disables Empty (and announces it), and loading a different one enables it again.
    /// </summary>
    [Fact]
    public void EmptySemester_CanEmpty_FollowsLoadedStateChangesWhileOpen()
    {
        var vm = NewEmptyVm(_ay2, _spring);
        var propertyNames = new List<string?>();
        var canExecuteChanged = 0;
        vm.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName);
        vm.EmptyCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
        Assert.True(vm.CanEmpty);

        Load(_ay2, _spring);                                    // the user loads this very semester

        Assert.True(vm.IsCurrentlyLoaded);
        Assert.False(vm.CanEmpty);
        Assert.False(vm.EmptyCommand.CanExecute(null));
        Assert.Contains(nameof(EmptySemesterViewModel.CanEmpty), propertyNames);
        Assert.True(canExecuteChanged > 0);
        Assert.NotNull(vm.StatusMessage);

        Load(_ay1, _fall);                                      // and then switches away

        Assert.False(vm.IsCurrentlyLoaded);
        Assert.True(vm.CanEmpty);
        Assert.True(vm.EmptyCommand.CanExecute(null));
        Assert.Null(vm.StatusMessage);
    }

    /// <summary>Changing the selected semester re-evaluates the button, not just the text.</summary>
    [Fact]
    public void EmptySemester_ChangingTheSelection_NotifiesCanEmptyAndTheSummary()
    {
        var vm = NewEmptyVm(_ay2, _spring);
        var propertyNames = new List<string?>();
        var canExecuteChanged = 0;
        vm.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName);
        vm.EmptyCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
        Assert.True(vm.CanEmpty);

        vm.SelectedSemester = vm.Semesters.Single(s => s.Id == _summer);   // Summer is empty

        Assert.False(vm.CanEmpty);
        Assert.Contains(nameof(EmptySemesterViewModel.CanEmpty), propertyNames);
        Assert.Contains(nameof(EmptySemesterViewModel.ContentsSummary), propertyNames);
        Assert.True(canExecuteChanged > 0);
        Assert.Equal("This semester is empty.", vm.ContentsSummary);
    }

    /// <summary>
    /// If the semester becomes loaded after the user has started (here, while the confirmation is
    /// open), the service refuses, nothing is deleted, and the service's own message is shown.
    /// </summary>
    [Fact]
    public async Task EmptySemester_ServiceRefusal_IsShownAndNothingIsDeleted()
    {
        var vm = NewEmptyVm(_ay2, _spring);
        var errors = new List<string>();
        vm.ShowError = message => { errors.Add(message); return Task.CompletedTask; };
        vm.ConfirmEmpty = (_, _) =>
        {
            Load(_ay2, _spring);            // loaded while the dialog is open
            return Task.FromResult(true);
        };

        await vm.EmptyCommand.ExecuteAsync(null);

        var shown = Assert.Single(errors);
        Assert.Equal(
            "\"Spring\" is currently loaded. Switch to a different semester in the main view before emptying it.",
            shown);
        Assert.Equal(PairOfEach, _service.CountContents(_spring));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Semester Manager: delete semester
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The 1.2.2 "emptied" case: the sections are gone but meetings, releases, commitments, notes and
    /// watches remain. Delete is refused, the message lists what is left and points to Empty Semester,
    /// the user is not even asked to confirm, and nothing is deleted.
    /// </summary>
    [Fact]
    public async Task DeleteSemester_Refused_WhenOnlyTheSectionsWereRemoved()
    {
        _sectionRepo.DeleteBySemesterId(_winter);   // what Empty Semester did in 1.2.2
        var dialog = new FakeDialog(answer: true);
        var vm = NewManagerVm(_ay1, _winter, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            "\"Winter\" still contains 1 meeting, 1 release, 1 scheduled commitment, 1 scheduling note, 1 program watch. " +
            "Use Empty Semester to remove everything before deleting it.",
            vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);
        Assert.NotNull(_semesterRepo.GetById(_winter));
        Assert.Equal(2, vm.Semesters.Count);
    }

    /// <summary>A semester whose only remaining row is a release is still refused.</summary>
    [Fact]
    public async Task DeleteSemester_Refused_WhenOnlyAReleaseRemains()
    {
        Seed(_summer, OnlyOne(Kind.Releases));
        var dialog = new FakeDialog(answer: true);
        var vm = NewManagerVm(_ay2, _summer, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            "\"Summer\" still contains 1 release. Use Empty Semester to remove everything before deleting it.",
            vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);
        Assert.NotNull(_semesterRepo.GetById(_summer));
    }

    /// <summary>A loaded semester cannot be deleted, even when it is empty.</summary>
    [Fact]
    public async Task DeleteSemester_Refused_WhenLoaded()
    {
        Load(_ay2, _summer);
        var dialog = new FakeDialog(answer: true);
        var vm = NewManagerVm(_ay2, _summer, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            "\"Summer\" is currently loaded. Switch to a different semester in the main view before deleting it.",
            vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);
        Assert.NotNull(_semesterRepo.GetById(_summer));
    }

    /// <summary>The loaded guard covers every selected semester, not just the first.</summary>
    [Fact]
    public async Task DeleteSemester_Refused_WhenLoadedAsTheSecondOfTwo()
    {
        Load(_ay2, _spring, _summer);
        Assert.Equal(_spring, _context.SelectedSemesterDisplay!.Semester.Id);   // Summer is not the first
        var dialog = new FakeDialog(answer: true);
        var vm = NewManagerVm(_ay2, _summer, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("currently loaded", vm.LastErrorMessage);
        Assert.NotNull(_semesterRepo.GetById(_summer));
    }

    /// <summary>
    /// An empty, unloaded semester is deleted after the user confirms; the list refreshes, the other
    /// semesters stay, and an earlier refusal message is cleared.
    /// </summary>
    [Fact]
    public async Task DeleteSemester_Succeeds_WhenEmptyAndNotLoaded()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = NewManagerVm(_ay2, _spring, dialog);
        await vm.DeleteCommand.ExecuteAsync(null);                  // Spring is full: refused
        Assert.NotNull(vm.LastErrorMessage);
        vm.SelectedSemester = vm.Semesters.Single(s => s.Semester.Id == _summer);

        await vm.DeleteCommand.ExecuteAsync(null);                  // Summer is empty: allowed

        Assert.Null(vm.LastErrorMessage);
        Assert.Equal(new[] { "Delete semester \"Summer\"?" }, dialog.Confirmations);
        Assert.Null(_semesterRepo.GetById(_summer));
        Assert.NotNull(_semesterRepo.GetById(_spring));
        Assert.Equal(new[] { _spring }, vm.Semesters.Select(s => s.Semester.Id).ToArray());
    }

    /// <summary>Declining the confirmation deletes nothing.</summary>
    [Fact]
    public async Task DeleteSemester_Declined_DeletesNothing()
    {
        var dialog = new FakeDialog(answer: false);
        var vm = NewManagerVm(_ay2, _summer, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Single(dialog.Confirmations);
        Assert.NotNull(_semesterRepo.GetById(_summer));
        Assert.Null(vm.LastErrorMessage);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Academic Years: delete academic year
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// While any semester of the year still holds something, the year cannot be deleted. The message
    /// names each non-empty semester; the user is not asked to confirm; nothing at all is deleted.
    /// </summary>
    [Fact]
    public async Task DeleteAcademicYear_Refused_WhileASemesterIsNotEmpty()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(_ay1, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.NotNull(vm.LastErrorMessage);
        Assert.Contains("\"2024-2025\" can't be deleted", vm.LastErrorMessage);
        Assert.Contains($"Fall ({FullContents.Describe()})", vm.LastErrorMessage);
        Assert.Contains($"Winter ({SingleOfEach.Describe()})", vm.LastErrorMessage);
        Assert.Contains("Empty Semester", vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);

        // Nothing was deleted.
        Assert.NotNull(_ayRepo.GetById(_ay1));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay1).Count);
        Assert.Equal(2, _legalRepo.GetAll(_ay1).Count);
        Assert.Equal(FullContents, _service.CountContents(_fall));
    }

    /// <summary>A year with a single stray row in a single semester is still refused, naming that semester.</summary>
    [Fact]
    public async Task DeleteAcademicYear_Refused_WhenOneSemesterHasOneRowOfAnyKind()
    {
        _service.EmptySemester(_spring);
        Seed(_summer, OnlyOne(Kind.ProgramWatches));
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(_ay2, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            "\"2025-2026\" can't be deleted because it still contains: Summer (1 program watch). " +
            "Use Empty Semester to remove everything from each of these semesters before deleting the year.",
            vm.LastErrorMessage);
        Assert.NotNull(_ayRepo.GetById(_ay2));
    }

    /// <summary>A year containing a loaded semester is refused, and the message names that semester.</summary>
    [Fact]
    public async Task DeleteAcademicYear_Refused_WhileOneOfItsSemestersIsLoaded()
    {
        _service.EmptySemester(_spring);            // the whole year is empty...
        Load(_ay2, _summer);                        // ...but Summer is loaded
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(_ay2, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            "\"2025-2026\" is currently loaded in the main view. " +
            "Switch to a different academic year there before deleting it.",
            vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);
        Assert.NotNull(_ayRepo.GetById(_ay2));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay2).Count);
    }

    /// <summary>
    /// Once every semester is empty and none is loaded, the year goes after one confirmation that
    /// does not mention sections: its semesters, legal start times and the year itself are all gone,
    /// the other year is untouched, and the refusal from the first attempt is cleared.
    /// </summary>
    [Fact]
    public async Task DeleteAcademicYear_Succeeds_WhenEverySemesterIsEmpty()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(_ay2, dialog);
        await vm.DeleteCommand.ExecuteAsync(null);                  // Spring still has rows: refused
        Assert.NotNull(vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);

        _service.EmptySemester(_spring);                            // now Spring and Summer are both empty
        vm.SelectedAcademicYear = vm.AcademicYears.Single(a => a.Id == _ay2);
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
        var confirmation = Assert.Single(dialog.Confirmations);
        Assert.Equal("Delete academic year \"2025-2026\" and its 2 empty semesters?", confirmation);
        Assert.DoesNotContain("section", confirmation, StringComparison.OrdinalIgnoreCase);

        // The year, its semesters and its legal start times are gone.
        Assert.Null(_ayRepo.GetById(_ay2));
        Assert.Empty(_semesterRepo.GetByAcademicYear(_ay2));
        Assert.Empty(_legalRepo.GetAll(_ay2));
        Assert.DoesNotContain(vm.AcademicYears, a => a.Id == _ay2);

        // The other year is intact.
        Assert.NotNull(_ayRepo.GetById(_ay1));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay1).Count);
        Assert.Equal(2, _legalRepo.GetAll(_ay1).Count);
        Assert.Equal(FullContents, _service.CountContents(_fall));
    }

    /// <summary>
    /// The confirmation counts the year's empty semesters and reads naturally for none, one or
    /// several, and never talks about sections.
    /// </summary>
    [Theory]
    [InlineData(0, "Delete academic year \"2031-2032\"?")]
    [InlineData(1, "Delete academic year \"2031-2032\" and its empty semester?")]
    [InlineData(3, "Delete academic year \"2031-2032\" and its 3 empty semesters?")]
    public async Task DeleteAcademicYear_ConfirmationText_CountsTheEmptySemesters(int semesters, string expected)
    {
        var ayId = Guid.NewGuid().ToString();
        _ayRepo.Insert(new AcademicYear { Id = ayId, Name = "2031-2032" });
        for (var i = 0; i < semesters; i++)
            _semesterRepo.Insert(new Semester { AcademicYearId = ayId, Name = $"Term {i}", SortOrder = i });
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(ayId, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(new[] { expected }, dialog.Confirmations);
        Assert.DoesNotContain("section", dialog.Confirmations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Null(_ayRepo.GetById(ayId));
    }

    /// <summary>Declining the confirmation deletes nothing.</summary>
    [Fact]
    public async Task DeleteAcademicYear_Declined_DeletesNothing()
    {
        _service.EmptySemester(_spring);
        var dialog = new FakeDialog(answer: false);
        var vm = NewYearListVm(_ay2, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Single(dialog.Confirmations);
        Assert.Null(vm.LastErrorMessage);
        Assert.NotNull(_ayRepo.GetById(_ay2));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay2).Count);
        Assert.Equal(2, _legalRepo.GetAll(_ay2).Count);
    }

    /// <summary>
    /// The deleted year is the one the main view has selected, but no semester of it is ticked (so
    /// nothing is "loaded" and the delete is allowed). After the delete the main view's
    /// <see cref="SemesterContext"/> must not still point at the year that no longer exists: it falls
    /// back to the first remaining year with nothing ticked, and offers none of the deleted year's
    /// semesters.
    /// </summary>
    [Fact]
    public async Task DeleteAcademicYear_TheSelectedYearWithNothingTicked_LeavesTheContextOnAnExistingYear()
    {
        _service.EmptySemester(_spring);
        SelectYearWithNothingTicked(_ay2);
        Assert.Equal(_ay2, _context.SelectedAcademicYear!.Id);
        Assert.Empty(_context.SelectedSemesters);
        Assert.NotEmpty(_context.FilteredCheckItems);               // Spring and Summer are on offer, unticked
        Assert.Null(_service.WhyCannotDeleteAcademicYear(_ay2));    // so the delete is allowed
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(_ay2, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
        Assert.Null(_ayRepo.GetById(_ay2));
        Assert.Equal(_ay1, _context.SelectedAcademicYear!.Id);
        Assert.DoesNotContain(_context.AcademicYears, a => a.Id == _ay2);
        Assert.DoesNotContain(_context.SemesterDisplays, d => d.Semester.AcademicYearId == _ay2);
        Assert.DoesNotContain(_context.FilteredCheckItems, c => c.SemDisplay.Semester.AcademicYearId == _ay2);
        Assert.Empty(_context.SelectedSemesters);
        Assert.False(_context.HasSemesterSelected);
    }

    /// <summary>
    /// Same situation, but the deleted year was the only one left: the context ends with no
    /// selected year, no semester choices and nothing loaded, rather than a dangling year.
    /// </summary>
    [Fact]
    public async Task DeleteAcademicYear_TheOnlyYearLeft_LeavesTheContextWithNoYear()
    {
        _service.EmptySemester(_spring);
        _service.DeleteAcademicYear(_ay2);
        _service.EmptySemester(_fall);
        _service.EmptySemester(_winter);
        SelectYearWithNothingTicked(_ay1);
        Assert.Equal(_ay1, _context.SelectedAcademicYear!.Id);
        var dialog = new FakeDialog(answer: true);
        var vm = NewYearListVm(_ay1, dialog);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
        Assert.Empty(_ayRepo.GetAll());
        Assert.Null(_context.SelectedAcademicYear);
        Assert.Empty(_context.AcademicYears);
        Assert.Empty(_context.FilteredCheckItems);
        Assert.Empty(_context.SelectedSemesters);
    }
}

using Moq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// A scripted stand-in for an open editor (spec item 3.1): reports whether it has unapplied changes, answers
/// <see cref="TryApplyAsync"/> from a setting, and counts how often it was applied and discarded.
/// </summary>
internal sealed class FakeUnappliedEditor : IUnappliedChanges
{
    /// <summary>What <see cref="HasUnappliedChanges"/> reports.</summary>
    public bool Has { get; set; } = true;

    /// <summary>What <see cref="TryApplyAsync"/> returns.</summary>
    public bool ApplySucceeds { get; set; } = true;

    /// <summary>Runs inside <see cref="TryApplyAsync"/> when it succeeds (models the list closing the editor); null for none.</summary>
    public Action? OnApplied { get; set; }

    /// <summary>How many times Apply was attempted.</summary>
    public int ApplyCalls { get; private set; }

    /// <summary>How many times the changes were discarded.</summary>
    public int DiscardCalls { get; private set; }

    /// <inheritdoc/>
    public bool HasUnappliedChanges => Has;

    /// <inheritdoc/>
    public Task<bool> TryApplyAsync()
    {
        ApplyCalls++;
        if (ApplySucceeds) { Has = false; OnApplied?.Invoke(); }
        return Task.FromResult(ApplySucceeds);
    }

    /// <inheritdoc/>
    public void DiscardUnapplied() { DiscardCalls++; Has = false; }
}

/// <summary>
/// Tests of the editors' unapplied-changes support: the finder and the snapshot comparison in the editors. The close
/// question itself is tested in <c>UnsavedChangesGateTests</c>.
/// </summary>
public sealed class UnappliedChangesTests
{
    // ── The finder ────────────────────────────────────────────────────────────

    /// <summary>The finder skips nulls and clean editors, looks inside hosts, and returns the first dirty editor.</summary>
    [Fact]
    public void Finder_ReturnsTheFirstEditorWithChanges_LookingInsideHosts()
    {
        var clean = new FakeUnappliedEditor { Has = false };
        var dirty = new FakeUnappliedEditor();
        var host = new Mock<IUnappliedChangesHost>();
        host.Setup(h => h.FindUnappliedChanges()).Returns(dirty);

        Assert.Same(dirty, UnappliedChangesFinder.First(null, clean, host.Object));
        Assert.Null(UnappliedChangesFinder.First(null, clean));
    }

    // ── HasUnappliedChanges: the section editor ──────────────────────────────

    /// <summary>Builds a section editor over an existing section "A" of course c-1 (the edit flow).</summary>
    private static SectionEditViewModel MakeSectionEditor(Func<Section, Task>? onSave = null)
    {
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());
        var section = new Section { CourseId = "c-1", SectionCode = "A" };

        return new SectionEditViewModel(
            section, isNew: false, isCopy: false,
            courses: new List<Course> { new() { Id = "c-1", CalendarCode = "HIST101" } },
            subjects: new List<Subject>(),
            instructors: new List<Instructor>(),
            rooms: new List<Room>(),
            legalStartTimes: new List<LegalStartTime>(),
            includeSaturday: false, includeSunday: false,
            sectionTypes: new List<SchedulingEnvironmentValue>(),
            meetingTypes: new List<SchedulingEnvironmentValue>(),
            campuses: new List<Campus>(),
            allTags: new List<SchedulingEnvironmentValue>(),
            allResources: new List<SchedulingEnvironmentValue>(),
            allReserves: new List<SchedulingEnvironmentValue>(),
            codePatterns: new List<SectionCodePattern>(),
            isSectionCodeDuplicate: (_, _) => false,
            onSave: onSave ?? (_ => Task.CompletedTask),
            blockPatternRepository: blockPatternRepo.Object,
            roomTypes: new List<SchedulingEnvironmentValue>(),
            defaultBlockLength: null);
    }

    /// <summary>Opened unchanged: no unapplied changes.</summary>
    [Fact]
    public void SectionEditor_OpenedUnchanged_HasNoUnappliedChanges()
        => Assert.False(MakeSectionEditor().HasUnappliedChanges);

    /// <summary>A typed note is a change; typing it back out is not (compared with the opening values, not a flag).</summary>
    [Fact]
    public void SectionEditor_ChangedThenChangedBack_ReportsAccordingly()
    {
        var vm = MakeSectionEditor();

        vm.Notes = "bring the projector";
        Assert.True(vm.HasUnappliedChanges);

        vm.Notes = "";
        Assert.False(vm.HasUnappliedChanges);
    }

    /// <summary>Adding a meeting row is a change; removing it again is not.</summary>
    [Fact]
    public void SectionEditor_MeetingRowAddedThenRemoved_ReportsAccordingly()
    {
        var vm = MakeSectionEditor();

        vm.AddMeetingCommand.Execute(null);
        Assert.True(vm.HasUnappliedChanges);

        vm.RemoveMeetingCommand.Execute(vm.Meetings[0]);
        Assert.False(vm.HasUnappliedChanges);
    }

    /// <summary>Setting a property to the value it already has (as Avalonia does after a list loads) is not a change.</summary>
    [Fact]
    public void SectionEditor_SettingAValueToItself_IsNotAChange()
    {
        var vm = MakeSectionEditor();

        vm.SectionCode = vm.SectionCode;
        vm.SelectedCourseId = vm.SelectedCourseId;

        Assert.False(vm.HasUnappliedChanges);
    }

    /// <summary>
    /// TryApplyAsync runs the Save path: a duplicate code is refused (false, the editor's own error shows and the
    /// save callback never runs); a valid change is saved (true, the callback runs).
    /// </summary>
    [Fact]
    public async Task SectionEditor_TryApply_RefusedWhenInvalid_AppliedWhenValid()
    {
        var saved = 0;
        var blockPatternRepo = new Mock<IBlockPatternRepository>();
        blockPatternRepo.Setup(r => r.GetAll()).Returns(new List<BlockPattern>());
        var duplicate = true;
        var vm = new SectionEditViewModel(
            new Section { CourseId = "c-1", SectionCode = "A" }, isNew: false, isCopy: false,
            new List<Course> { new() { Id = "c-1", CalendarCode = "HIST101" } }, new List<Subject>(),
            new List<Instructor>(), new List<Room>(), new List<LegalStartTime>(), false, false,
            new List<SchedulingEnvironmentValue>(), new List<SchedulingEnvironmentValue>(), new List<Campus>(),
            new List<SchedulingEnvironmentValue>(), new List<SchedulingEnvironmentValue>(), new List<SchedulingEnvironmentValue>(),
            new List<SectionCodePattern>(),
            isSectionCodeDuplicate: (_, _) => duplicate,
            onSave: _ => { saved++; return Task.CompletedTask; },
            blockPatternRepository: blockPatternRepo.Object,
            roomTypes: new List<SchedulingEnvironmentValue>());
        vm.Notes = "changed";

        Assert.False(await vm.TryApplyAsync());
        Assert.NotNull(vm.SectionCodeError);
        Assert.Equal(0, saved);

        duplicate = false;
        Assert.True(await vm.TryApplyAsync());
        Assert.Equal(1, saved);
    }

    // ── HasUnappliedChanges: two flyout editors ──────────────────────────────

    /// <summary>Subject editor: unchanged false, changed true, changed back false; Apply saves; an empty name is refused.</summary>
    [Fact]
    public async Task SubjectEditor_SnapshotComparison_AndApply()
    {
        var saved = new List<Subject>();
        var vm = new SubjectEditViewModel(
            new Subject { Name = "History", CalendarAbbreviation = "HIST" }, isNew: false,
            onSave: s => { saved.Add(s); return Task.CompletedTask; }, onCancel: () => { },
            nameExists: _ => false, abbreviationExists: _ => false);

        Assert.False(vm.HasUnappliedChanges);
        vm.Name = "Histories";
        Assert.True(vm.HasUnappliedChanges);
        vm.Name = "History";
        Assert.False(vm.HasUnappliedChanges);

        vm.Name = "";                                   // invalid: Save is disabled
        Assert.True(vm.HasUnappliedChanges);
        Assert.False(await vm.TryApplyAsync());
        Assert.Empty(saved);

        vm.Name = "Ancient History";
        Assert.True(await vm.TryApplyAsync());
        Assert.Equal("Ancient History", Assert.Single(saved).Name);
    }

    /// <summary>Room editor: a new editor with only its defaults has no changes; typing makes one; erasing it clears it.</summary>
    [Fact]
    public void RoomEditor_NewWithDefaults_ThenTyped_ThenErased()
    {
        var none = new CampusOption(null, "(none)");
        var noType = new RoomTypeOption(null, "(none)");
        var vm = new RoomEditViewModel(
            new Room(), isNew: true,
            new List<CampusOption> { none }, new List<RoomTypeOption> { noType },
            onSave: _ => { }, onCancel: () => { });

        Assert.False(vm.HasUnappliedChanges);       // an untouched Add editor is not "changes"
        vm.Building = "Science";
        Assert.True(vm.HasUnappliedChanges);
        vm.Building = "";
        Assert.False(vm.HasUnappliedChanges);
    }

    /// <summary>Instructor editor: Discard runs the Cancel callback.</summary>
    [Fact]
    public void InstructorEditor_Discard_RunsCancel()
    {
        var cancelled = 0;
        var vm = new InstructorEditViewModel(
            new Instructor { FirstName = "Jo", LastName = "Smith", Initials = "JS" }, isNew: false,
            new List<SchedulingEnvironmentValue>(), onSave: _ => { }, onCancel: () => cancelled++, initialsExist: _ => false);
        vm.Email = "jo@example.edu";
        Assert.True(vm.HasUnappliedChanges);

        vm.DiscardUnapplied();

        Assert.Equal(1, cancelled);
    }
}

/// <summary>
/// Lifecycle test for the unapplied-editor sentence of the lost-lock banner.
/// </summary>
public sealed class SessionLifecycleUnappliedChangesTests : SessionLifecycleTestBase
{
    /// <summary>
    /// A lost lock with an editor holding unapplied changes: the single banner gains the approved sentence (and only
    /// then); the editor is not asked about.
    /// </summary>
    [Fact]
    public async Task LostLock_WithUnappliedEditorChanges_BannerGainsTheSentence()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        _host.UnappliedEditor = new FakeUnappliedEditor();
        var reasons = new List<WriteLockLostReason>();
        _checkout.WriteLockLost += reasons.Add;
        WriteExternalFreshLock(db);
        Assert.Equal(SaveOutcome.LockLost, await _checkout.SaveAsync());

        await _lifecycle.HandleWriteLockLostAsync(Assert.Single(reasons));

        var banner = Assert.Single(_host.LockLossSurface.SaveErrors);
        Assert.Contains("Changes you hadn't applied in the open editor couldn't be saved.", banner);
        Assert.Empty(_prompts.Calls);
    }
}

using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The Access tab (<see cref="AccessTabViewModel"/>), the eye on a tag (<see cref="AccessWatchToggler"/>) and the
/// eye's state in the filter's Tags popup (<see cref="GridFilterViewModel"/>). Real repositories over a temp
/// database; the write lock is a switch so the read-only behaviour is exercised without a lock file.
/// <para>
/// Seed: two semesters (Fall, Winter), both loaded. Tag "Zeta" holds courses A and B, whose single sections conflict
/// in Winter only; tag "Alpha" holds course C, which is fine. So Zeta needs a look in Winter and not in Fall.
/// </para>
/// </summary>
public sealed class AccessTabTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SectionStore _store = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly SectionRepository _sectionRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly AccessState _state;
    private readonly SemesterContext _semesters = new();
    private readonly SchedulingEnvironmentValue _zeta = new() { Name = "Zeta", IsAccessWatch = true };
    private readonly SchedulingEnvironmentValue _alpha = new() { Name = "Alpha", IsAccessWatch = true };
    private readonly string _fall;
    private readonly string _winter;
    private bool _canWrite = true;

    public AccessTabTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);

        foreach (var code in new[] { "A", "B", "C" })
            courseRepo.Insert(new Course { Id = "c-" + code, CalendarCode = code, Title = code });
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        var winter = new Semester { AcademicYearId = year.Id, Name = "Winter", SortOrder = 1 };
        semesterRepo.Insert(fall);
        semesterRepo.Insert(winter);
        _fall = fall.Id;
        _winter = winter.Id;
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, _zeta);
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, _alpha);

        AddSection(_fall, "A", 1, _zeta); AddSection(_fall, "B", 2, _zeta); AddSection(_fall, "C", 1, _alpha);
        AddSection(_winter, "A", 1, _zeta); AddSection(_winter, "B", 1, _zeta); AddSection(_winter, "C", 1, _alpha);

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        _semesters.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { _fall });
        _store.Reload(_sectionRepo, [_fall]);
    }

    private void AddSection(string semesterId, string course, int day, SchedulingEnvironmentValue tag) =>
        _sectionRepo.Insert(new Section
        {
            CourseId = "c-" + course, SemesterId = semesterId, SectionCode = "1", TagIds = [tag.Id],
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }],
        });

    private AccessWatchToggler NewToggler() => new(_envRepo, _state, _notifier, () => _canWrite);

    private AccessTabViewModel NewTab() =>
        new(_state, _semesters, new CampusRepository(_fx.Db), NewToggler());

    private void LoadBoth()
    {
        _semesters.Reload(new AcademicYearRepository(_fx.Db), new SemesterRepository(_fx.Db), null,
            new HashSet<string> { _fall, _winter });
        _store.Reload(_sectionRepo, [_fall, _winter]);
    }

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── The tab ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rows_AreSortedAttentionFirstThenByName()
    {
        _canWrite = true;
        var tab = NewTab();
        LoadBoth();
        // Zeta needs a look (Winter), so it leads although Alpha sorts first by name.
        Assert.Equal(["Zeta", "Alpha"], tab.Rows.Select(r => r.Name).ToArray());
        Assert.True(tab.Rows[0].IsAttention);
        Assert.Equal("▲", tab.Rows[0].Mark);
        Assert.True(tab.Rows[1].IsOk);
        Assert.Equal("○", tab.Rows[1].Mark);
        Assert.True(tab.HasRows);
        Assert.False(tab.IsEmpty);
    }

    [Fact]
    public void NeedsALook_FollowsTheWorstRow()
    {
        var tab = NewTab();
        Assert.False(tab.NeedsALook); // Fall alone: nothing conflicts
        LoadBoth();
        Assert.True(tab.NeedsALook);
    }

    [Fact]
    public void SeveralSemesters_OneRowPerWatchFromItsWorstSemester_WithThePrefix()
    {
        var tab = NewTab();
        LoadBoth();
        var zeta = tab.Rows.Single(r => r.Name == "Zeta");
        Assert.Equal("Winter: A 1 conflicts with B 1", zeta.HealthLine);
        Assert.EndsWith(": 1 section", tab.Rows.Single(r => r.Name == "Alpha").HealthLine);
        Assert.Equal(2, tab.Rows.Count);
    }

    [Fact]
    public void OneSemester_HasNoPrefix_AndTheTitleNamesIt()
    {
        var tab = NewTab();
        Assert.Equal("Both sections fit together", tab.Rows.Single(r => r.Name == "Zeta").HealthLine);
        Assert.StartsWith("Access · ", tab.Title);
        Assert.Contains("Fall", tab.Title);
    }

    [Fact]
    public void NoWatches_ShowsTheApprovedEmptyText()
    {
        _envRepo.Update(new SchedulingEnvironmentValue { Id = _zeta.Id, Name = "Zeta", IsAccessWatch = false });
        _envRepo.Update(new SchedulingEnvironmentValue { Id = _alpha.Id, Name = "Alpha", IsAccessWatch = false });
        var tab = NewTab();
        Assert.True(tab.IsEmpty);
        Assert.False(tab.HasRows);
        Assert.Equal("No access watches yet. An access watch is a tag you watch: TermPoint then shows whether students can still take all its courses together.",
            tab.EmptyText);
    }

    [Fact]
    public void AddWatch_IsOnlyForAWriter_AndRaisesTheRequest()
    {
        var tab = NewTab();
        int requests = 0;
        tab.AddWatchRequested += () => requests++;

        Assert.True(tab.CanAddWatch);
        Assert.Equal("Add a watch", tab.AddWatchTip);
        tab.AddWatchCommand.Execute(null);
        Assert.Equal(1, requests);

        _canWrite = false;
        Assert.False(tab.CanAddWatch);
        Assert.Equal("Read-only: you can look at watches but not change them.", tab.AddWatchTip);
        Assert.False(tab.AddWatchCommand.CanExecute(null));
    }

    [Fact]
    public void MarkAllConflicts_IsTwoWay_AndWorksReadOnly()
    {
        _canWrite = false;
        var tab = NewTab();
        Assert.False(tab.MarkAllConflicts);
        tab.MarkAllConflicts = true;
        Assert.True(_state.MarkAllConflicts);
        Assert.True(tab.MarkAllConflicts);
    }

    [Fact]
    public void ARowFollowsAnEyeClick()
    {
        var tab = NewTab();
        NewToggler().SetWatched(_alpha.Id, false);
        Assert.Equal(["Zeta"], tab.Rows.Select(r => r.Name).ToArray());
    }

    // ── The toggler ───────────────────────────────────────────────────────────

    [Fact]
    public void Toggler_WritesTheFlag_InvalidatesTheState_AndRedrawsTheGrid()
    {
        var toggler = NewToggler();
        int changed = 0, redraws = 0;
        _state.Changed += () => changed++;
        _notifier.GridContentChanged += () => redraws++;
        var before = _state.Current;

        toggler.SetWatched(_alpha.Id, false);

        Assert.False(_envRepo.GetById(_alpha.Id)!.IsAccessWatch);
        Assert.True(changed >= 1);
        Assert.Equal(1, redraws);
        Assert.NotSame(before, _state.Current);

        toggler.SetWatched(_alpha.Id, true);
        Assert.True(_envRepo.GetById(_alpha.Id)!.IsAccessWatch);
    }

    [Fact]
    public void Toggler_WhenNothingChanges_SavesNothing()
    {
        int redraws = 0;
        _notifier.GridContentChanged += () => redraws++;
        NewToggler().SetWatched(_alpha.Id, true); // already watched
        NewToggler().SetWatched("no-such-tag", true);
        Assert.Equal(0, redraws);
    }

    [Fact]
    public void Toggler_ReadOnly_RefusesAndSavesNothing()
    {
        _canWrite = false;
        var toggler = NewToggler();
        Assert.False(toggler.CanToggle);
        Assert.Throws<InvalidOperationException>(() => toggler.SetWatched(_alpha.Id, false));
        Assert.True(_envRepo.GetById(_alpha.Id)!.IsAccessWatch);
    }

    // ── The Tags popup ────────────────────────────────────────────────────────

    private GridFilterViewModel NewFilter()
    {
        var vm = new GridFilterViewModel { WatchToggler = NewToggler() };
        vm.PopulateOptions(
            new Dictionary<string, Instructor>(), new Dictionary<string, Room>(), new Dictionary<string, Subject>(),
            new Dictionary<string, Campus>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            _envRepo.GetAll(SchedulingEnvironmentTypes.Tag).ToDictionary(t => t.Id),
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, string>(),
            new Dictionary<string, Course>());
        return vm;
    }

    [Fact]
    public void Filter_TagItemsKnowWhichTagsAreWatched()
    {
        _envRepo.Update(new SchedulingEnvironmentValue { Id = _alpha.Id, Name = "Alpha", IsAccessWatch = false });
        var vm = NewFilter();
        Assert.False(vm.Tags.Single(t => t.Name == "Alpha").IsWatched);
        Assert.True(vm.Tags.Single(t => t.Name == "Zeta").IsWatched);
        Assert.Equal("Watch this tag", vm.Tags.Single(t => t.Name == "Alpha").WatchTip);
        Assert.Equal("Watched: click to stop watching", vm.Tags.Single(t => t.Name == "Zeta").WatchTip);
    }

    [Fact]
    public void Filter_ToggleTagWatch_SavesAndFlipsTheItem_AndAGridReloadKeepsTheTicks()
    {
        var vm = NewFilter();
        vm.Tags.Single(t => t.Name == "Zeta").IsSelected = true;

        vm.ToggleTagWatchCommand.Execute(vm.Tags.Single(t => t.Name == "Alpha"));

        Assert.False(_envRepo.GetById(_alpha.Id)!.IsAccessWatch);
        Assert.False(vm.Tags.Single(t => t.Name == "Alpha").IsWatched);

        // A reload rebuilds the items from the saved tags and keeps the tick.
        vm.PopulateOptions(
            new Dictionary<string, Instructor>(), new Dictionary<string, Room>(), new Dictionary<string, Subject>(),
            new Dictionary<string, Campus>(), new Dictionary<string, SchedulingEnvironmentValue>(),
            _envRepo.GetAll(SchedulingEnvironmentTypes.Tag).ToDictionary(t => t.Id),
            new Dictionary<string, SchedulingEnvironmentValue>(), new Dictionary<string, string>(),
            new Dictionary<string, Course>());
        Assert.True(vm.Tags.Single(t => t.Name == "Zeta").IsSelected);
        Assert.False(vm.Tags.Single(t => t.Name == "Alpha").IsWatched);
    }

    [Fact]
    public void Filter_ReadOnly_EyesAreDisabled_AndAClickSavesNothing()
    {
        _canWrite = false;
        var vm = NewFilter();
        vm.IsTagsPopupOpen = true;
        Assert.False(vm.CanEditWatches);
        vm.ToggleTagWatchCommand.Execute(vm.Tags.Single(t => t.Name == "Alpha"));
        Assert.True(_envRepo.GetById(_alpha.Id)!.IsAccessWatch);
    }

    [Fact]
    public void Filter_OpenTagsForWatch_OpensThePopupWithTheNote_AndClosingHidesIt()
    {
        var vm = NewFilter();
        Assert.False(vm.IsTagsPopupOpen);
        vm.OpenTagsForWatch();
        Assert.True(vm.IsTagsPopupOpen);
        Assert.True(vm.ShowWatchNote);
        Assert.True(vm.CanEditWatches);
        Assert.StartsWith("An access watch is a tag watched for student access,", vm.WatchNoteText);
        Assert.EndsWith("you can enable the watch by clicking the eye.", vm.WatchNoteText);

        vm.IsTagsPopupOpen = false;
        Assert.False(vm.ShowWatchNote);
    }

    [Fact]
    public void SchedulingEnvironmentList_ShowsTheWatchColumnOnlyForTags()
    {
        var rig = new SchedulingEnvironmentListViewModel(
            SchedulingEnvironmentTypes.Tag, "Tags", _envRepo, _sectionRepo, new CourseRepository(_fx.Db),
            new InstructorRepository(_fx.Db), _fx.Db, null!, null!, _fx.Lock,
            watchToggler: NewToggler(), changeNotifier: _notifier);
        Assert.True(rig.ShowWatchColumn);

        var alpha = rig.Items.Single(i => i.Name == "Alpha");
        rig.ToggleWatchCommand.Execute(alpha);
        Assert.False(_envRepo.GetById(_alpha.Id)!.IsAccessWatch);
        Assert.False(rig.Items.Single(i => i.Name == "Alpha").IsAccessWatch);

        var other = new SchedulingEnvironmentListViewModel(
            SchedulingEnvironmentTypes.SectionType, "Section Type", _envRepo, _sectionRepo, new CourseRepository(_fx.Db),
            new InstructorRepository(_fx.Db), _fx.Db, null!, null!, _fx.Lock);
        Assert.False(other.ShowWatchColumn);
    }
}

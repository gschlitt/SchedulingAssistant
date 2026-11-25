using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Management;
using TermPoint.ViewModels.Access;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The Access tab's notices: <see cref="AccessNotices.Compare"/> through <see cref="AccessState"/> (after a save, after
/// a reader-refresh-style reload with many changes, never after a semester switch), the cause named, replacement,
/// dismissal, automatic removal, and the cause of a components save. Real repositories over a temp database.
/// <para>
/// Seed: courses A, B, C and a watched tag "Y2". Fall: A 1 Monday, B 1 Tuesday, C 1 Wednesday, all tagged, so the
/// watch's three single sections fit together. Winter: A 1 and B 1 both Monday (they conflict), C 1 Wednesday.
/// </para>
/// </summary>
public sealed class AccessNoticesTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SectionStore _store = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly SectionRepository _sectionRepo;
    private readonly AccessState _state;
    private readonly SchedulingEnvironmentValue _tag = new() { Name = "Y2", IsAccessWatch = true };
    private readonly string _fall;
    private readonly string _winter;

    public AccessNoticesTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);

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
        envRepo.Insert(SchedulingEnvironmentTypes.Tag, _tag);

        foreach (var (course, fallDay, winterDay) in new[] { ("A", 1, 1), ("B", 2, 1), ("C", 3, 3) })
        {
            AddSection(_fall, course, fallDay);
            AddSection(_winter, course, winterDay);
        }

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        _store.Reload(_sectionRepo, [_fall]);
    }

    private void AddSection(string semesterId, string course, int day) =>
        _sectionRepo.Insert(new Section
        {
            CourseId = "c-" + course, SemesterId = semesterId, SectionCode = "1", TagIds = [_tag.Id],
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }],
        });

    private Section Get(string semesterId, string course) =>
        _sectionRepo.GetAll(semesterId).Single(s => s.CourseId == "c-" + course);

    /// <summary>Saves a section's day without reloading the store.</summary>
    private void Edit(string semesterId, string course, int day)
    {
        var s = Get(semesterId, course);
        s.Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(s);
    }

    /// <summary>Saves a section's day and reloads the store, as a save does.</summary>
    private void Move(string semesterId, string course, int day)
    {
        Edit(semesterId, course, day);
        _store.Reload(_sectionRepo, [semesterId]);
    }

    private Func<string?, string> CampusName => id => id ?? "none";

    private string TextOf(AccessNotice notice, string semesterId) =>
        AccessText.NoticeText(_state.Current.Semesters[semesterId].Inputs, "Y2", notice, CampusName);

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    [Fact]
    public void ASave_ThatBreaksTheWatch_MakesALostNotice_NamingTheNewConflict()
    {
        _ = _state.Current;
        Assert.Empty(_state.Notices);

        int raised = 0;
        _state.NoticesChanged += () => raised++;
        Move(_fall, "B", 1); // B 1 now meets with A 1

        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Lost, notice.Change.Kind);
        Assert.True(notice.Change.WasSingleSection);
        Assert.True(notice.IsBad);
        Assert.Equal(_tag.Id, notice.WatchId);
        Assert.Equal(_fall, notice.SemesterId);
        Assert.Equal(NoticeCauseKind.NewConflicts, notice.Cause.Kind);
        Assert.Equal("A", notice.Cause.Partners.Single().CourseId![2..]);
        Assert.Equal(1, raised);
        Assert.Equal(
            "The conflict between B 1 and A 1 means Y2's sections no longer fit together. Open Y2 for more detail.",
            TextOf(notice, _fall));
    }

    [Fact]
    public void AReloadWithManyChanges_NamesTheLatestChanges()
    {
        _ = _state.Current;
        // a reader's refresh: two sections changed in the file before one reload
        Edit(_fall, "B", 1);
        Edit(_fall, "C", 1);
        _store.Reload(_sectionRepo, [_fall]);

        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Lost, notice.Change.Kind);
        Assert.Equal(NoticeCauseKind.LatestChanges, notice.Cause.Kind);
        Assert.StartsWith("The latest changes mean Y2's sections no longer fit together.", TextOf(notice, _fall));
    }

    [Fact]
    public void ASemesterSwitch_NeverMakesANotice()
    {
        _ = _state.Current;
        // Winter's watch has no full slate, Fall's has: switching must not read as a loss
        _store.Reload(_sectionRepo, [_winter]);
        Assert.Empty(_state.Notices);
        _store.Reload(_sectionRepo, [_fall, _winter]);
        Assert.Empty(_state.Notices);
        _store.Reload(_sectionRepo, [_fall]);
        Assert.Empty(_state.Notices);
    }

    [Fact]
    public void ASemesterAlreadyLoaded_IsComparedWhileAnotherIsAdded()
    {
        _store.Reload(_sectionRepo, [_fall, _winter]);
        _ = _state.Current;
        Edit(_winter, "B", 2); // Winter's conflict is fixed
        _store.Reload(_sectionRepo, [_fall, _winter]);

        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Restored, notice.Change.Kind);
        Assert.Equal(_winter, notice.SemesterId);
        Assert.False(notice.IsBad);
    }

    [Fact]
    public void PlainInvalidate_AndGridContentChanges_NeverMakeNotices()
    {
        _ = _state.Current;
        Edit(_fall, "B", 1); // saved, but the store is not reloaded
        _state.Invalidate();
        _notifier.NotifyGridContentChanged();
        Assert.Empty(_state.Notices);
    }

    [Fact]
    public void ANewNotice_ReplacesTheWatchsOlderOne()
    {
        _ = _state.Current;
        // C 1 loses the tag: C leaves the watch (this notice stays true)
        var c1 = Get(_fall, "C");
        c1.TagIds = [];
        _sectionRepo.Update(c1);
        _store.Reload(_sectionRepo, [_fall]);
        var left = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Left, left.Change.Kind);
        Assert.Equal(["c-C"], left.Change.CourseIds);

        Move(_fall, "B", 1); // now A and B conflict: a Lost notice replaces the Left one
        var lost = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Lost, lost.Change.Kind);
        Assert.True(lost.Id > left.Id);
    }

    [Fact]
    public void Dismiss_RemovesTheNotice_AndRaisesTheEvent()
    {
        _ = _state.Current;
        Move(_fall, "B", 1);
        var notice = Assert.Single(_state.Notices);

        int raised = 0;
        _state.NoticesChanged += () => raised++;
        _state.DismissNotice(notice.Id);
        Assert.Empty(_state.Notices);
        Assert.Equal(1, raised);
        _state.DismissNotice(notice.Id); // already gone: nothing raised
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ANoticeThatStopsBeingTrue_GoesByItself()
    {
        _ = _state.Current;
        Move(_fall, "B", 1);
        Assert.Equal(AccessChangeKind.Lost, Assert.Single(_state.Notices).Change.Kind);

        Move(_fall, "B", 2); // fixed: the Lost notice goes, the good news comes
        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Restored, notice.Change.Kind);

        Move(_fall, "B", 1); // broken again: Restored is no longer true
        Assert.Equal(AccessChangeKind.Lost, Assert.Single(_state.Notices).Change.Kind);
    }

    [Fact]
    public void ALeftCourse_ComingBack_DropsTheNotice()
    {
        _ = _state.Current;
        var c1 = Get(_fall, "C");
        c1.TagIds = [];
        _sectionRepo.Update(c1);
        _store.Reload(_sectionRepo, [_fall]);
        Assert.Equal(AccessChangeKind.Left, Assert.Single(_state.Notices).Change.Kind);
        Assert.StartsWith("Changing C 1's tags means C is no longer in Y2.", TextOf(_state.Notices[0], _fall));

        c1.TagIds = [_tag.Id];
        _sectionRepo.Update(c1);
        _store.Reload(_sectionRepo, [_fall]);
        Assert.Empty(_state.Notices);
    }

    [Fact]
    public void AWatchThatIsUnwatched_LosesItsNotices()
    {
        _ = _state.Current;
        Move(_fall, "B", 1);
        Assert.Single(_state.Notices);

        _tag.IsAccessWatch = false;
        new SchedulingEnvironmentRepository(_fx.Db).Update(_tag);
        _state.Invalidate();
        Assert.Empty(_state.Notices);
    }

    [Fact]
    public void ACampusMove_NamesTheCampus_AndATagChange_IsDraftWorded()
    {
        _ = _state.Current;
        var b1 = Get(_fall, "B");
        b1.Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 540, DurationMinutes = 60 }];
        b1.CampusId = "chilliwack";
        _sectionRepo.Update(b1);
        _store.Reload(_sectionRepo, [_fall]);
        var notice = Assert.Single(_state.Notices);
        // moved and conflicting: the conflict is named first
        Assert.Equal(NoticeCauseKind.NewConflicts, notice.Cause.Kind);

        var inputs = _state.Current.Semesters[_fall].Inputs;
        var cause = new NoticeCause(NoticeCauseKind.MovedToCampus, b1, [], "chilliwack", []);
        Assert.Equal(("Moving B 1 to chilliwack", false), AccessText.NoticeCauseText(inputs, cause, CampusName));
        Assert.Equal(("Adding B 1", false), AccessText.NoticeCauseText(inputs, NoticeCause.Of(NoticeCauseKind.Added, b1), CampusName));
        Assert.Equal(("Deleting B 1", false), AccessText.NoticeCauseText(inputs, NoticeCause.Of(NoticeCauseKind.Deleted, b1), CampusName));
        Assert.Equal(("Changing B 1's tags", false), AccessText.NoticeCauseText(inputs, NoticeCause.Of(NoticeCauseKind.Tags, b1), CampusName));
    }

    [Fact]
    public void OneSectionAdded_OrDeleted_NamesIt()
    {
        _ = _state.Current;
        // a second B section on Monday does not break a watch that has one section per component... but deleting C's
        // only section removes the course: a Left notice caused by a deletion
        _sectionRepo.Delete(Get(_fall, "C").Id);
        _store.Reload(_sectionRepo, [_fall]);
        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Left, notice.Change.Kind);
        Assert.Equal(NoticeCauseKind.Deleted, notice.Cause.Kind);
        Assert.StartsWith("Deleting C 1 means C is no longer in Y2.", TextOf(notice, _fall));
    }
}

/// <summary>
/// The cause of a notice that follows a components save: <see cref="AccessSetupService"/> tells
/// <see cref="AccessState"/> which courses it wrote.
/// <para>
/// Seed: course L with a lecture and a lab that meet at the same time, tagged for the watch "Z". With both as
/// components the watch has no full slate; once L's only component is the lecture, the lab is an alternative.
/// </para>
/// </summary>
public sealed class AccessNoticesComponentsTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SectionStore _store = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly SectionRepository _sectionRepo;
    private readonly AccessState _state;
    private readonly AccessSetupService _setup;
    private readonly SchedulingEnvironmentValue _lecture = new() { Name = "Lecture", SortOrder = 0 };
    private readonly SchedulingEnvironmentValue _lab = new() { Name = "Laboratory", SortOrder = 1 };
    private readonly string _fall;

    public AccessNoticesComponentsTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);

        courseRepo.Insert(new Course { Id = "c-L", CalendarCode = "L", Title = "L" });
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        semesterRepo.Insert(fall);
        _fall = fall.Id;
        var tag = new SchedulingEnvironmentValue { Name = "Z", IsAccessWatch = true };
        envRepo.Insert(SchedulingEnvironmentTypes.Tag, tag);
        envRepo.Insert(SchedulingEnvironmentTypes.SectionType, _lecture);
        envRepo.Insert(SchedulingEnvironmentTypes.SectionType, _lab);
        foreach (var (code, type) in new[] { ("L1", _lecture.Id), ("L2", _lab.Id) })
            _sectionRepo.Insert(new Section
            {
                CourseId = "c-L", SemesterId = _fall, SectionCode = code, SectionTypeId = type, TagIds = [tag.Id],
                Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 540, DurationMinutes = 60 }],
            });

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        _setup = new AccessSetupService(db, envRepo, courseRepo, _sectionRepo, new AccessChecksSetupRepository(db),
            _state, _notifier, () => true);
        _store.Reload(_sectionRepo, [_fall]);
    }

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    [Fact]
    public void ACourseComponentsSave_NamesTheCourse()
    {
        // Setup with both types as components and L's list the same: nothing changes for the watch
        _setup.SaveSetup(new HashSet<string> { _lecture.Id, _lab.Id },
            new Dictionary<string, IReadOnlyList<string>> { ["c-L"] = [_lecture.Id, _lab.Id] });
        Assert.False(_state.Current.Semesters[_fall].Watches.Single().Analysis.HasSlate);
        Assert.Empty(_state.Notices);

        // L's only component is now the lecture: the lab is an alternative, and the watch fits together
        _setup.SaveCourseComponents(new Dictionary<string, IReadOnlyList<string>> { ["c-L"] = [_lecture.Id] });

        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Restored, notice.Change.Kind);
        Assert.Equal(NoticeCauseKind.Components, notice.Cause.Kind);
        Assert.Equal(["c-L"], notice.Cause.CourseIds);
        var inputs = _state.Current.Semesters[_fall].Inputs;
        Assert.Equal("Changing L's components means Z's sections fit together again.",
            AccessText.NoticeText(inputs, "Z", notice, _ => "x"));
    }

    [Fact]
    public void SeveralCourses_AreNamedGenerally()
    {
        var inputs = _state.Current.Semesters[_fall].Inputs;
        var cause = new NoticeCause(NoticeCauseKind.Components, null, [], null, ["c-L", "c-M"]);
        Assert.Equal(("Changing courses' components", false), AccessText.NoticeCauseText(inputs, cause, _ => "x"));
        Assert.Equal(("The latest changes", true), AccessText.NoticeCauseText(inputs, NoticeCause.Latest, _ => "x"));
    }

    [Fact]
    public void ATypeLevelSave_WithNoCourseWritten_NamesTheLatestChanges()
    {
        _ = _state.Current;
        _setup.SaveNoComponents(); // no type is a component any more: the lab becomes an alternative
        var notice = Assert.Single(_state.Notices);
        Assert.Equal(AccessChangeKind.Restored, notice.Change.Kind);
        Assert.Equal(NoticeCauseKind.LatestChanges, notice.Cause.Kind);
    }
}

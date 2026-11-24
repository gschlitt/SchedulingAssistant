using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="AccessState"/>: lazy recompute (stale after a store reload, a grid-content notification and
/// <c>Invalidate</c>), each semester analysed separately (Q43), and Mark all conflicts (harmless marks, persisted
/// per computer). Real repositories over a temp database.
/// <para>
/// Seed: two courses A and B and a watched tag "Y2". In each semester, A and B each have two sections, and A 1 / B 1
/// meet at the same time on Monday while A 2 meets on Tuesday and B 2 on Wednesday, so the conflict between A 1 and B 1 costs nothing
/// (a harmless mark) and every section is usable.
/// </para>
/// </summary>
public sealed class AccessStateTests : IDisposable
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

    public AccessStateTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);

        foreach (var code in new[] { "A", "B" })
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

        foreach (var sem in new[] { _fall, _winter })
            foreach (var code in new[] { "A", "B" })
            {
                AddSection(sem, code, "1", 1);
                AddSection(sem, code, "2", code == "A" ? 2 : 3);
            }

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        _store.Reload(_sectionRepo, [_fall]);
    }

    private Section AddSection(string semesterId, string course, string code, int day)
    {
        var s = new Section
        {
            CourseId = "c-" + course, SemesterId = semesterId, SectionCode = code, TagIds = [_tag.Id],
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }],
        };
        _sectionRepo.Insert(s);
        return s;
    }

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    [Fact]
    public void NothingLoaded_IsAnEmptySnapshot()
    {
        var state = AccessStateTestSupport.For(_fx.Db, new SectionStore(), new GridChangeNotifier());
        Assert.Empty(state.Current.Semesters);
    }

    [Fact]
    public void Current_IsComputedLazily_AndKeptUntilStale()
    {
        var first = _state.Current;
        Assert.Same(first, _state.Current);
        Assert.Equal(_fall, Assert.Single(first.Semesters).Key);
    }

    [Fact]
    public void StoreReload_GridContentChange_AndInvalidate_EachMakeItStale_AndRaiseChanged()
    {
        int changed = 0;
        _state.Changed += () => changed++;

        var snap = _state.Current;
        _store.Reload(_sectionRepo, [_fall]);
        Assert.Equal(1, changed);
        var afterReload = _state.Current;
        Assert.NotSame(snap, afterReload);

        _notifier.NotifyGridContentChanged();
        Assert.Equal(2, changed);
        var afterNotify = _state.Current;
        Assert.NotSame(afterReload, afterNotify);

        _state.Invalidate();
        Assert.Equal(3, changed);
        Assert.NotSame(afterNotify, _state.Current);
    }

    [Fact]
    public void ABurstOfChanges_CostsOneRecompute_WhenCurrentIsAsked()
    {
        var snap = _state.Current;
        _state.Invalidate();
        _state.Invalidate();
        _notifier.NotifyGridContentChanged();
        var next = _state.Current;
        Assert.NotSame(snap, next);
        Assert.Same(next, _state.Current);
    }

    [Fact]
    public void ASaveThatChangesATime_ShowsInTheNextSnapshot()
    {
        _state.MarkAllConflicts = true;
        Assert.Single(_state.Current.Semesters[_fall].Watches.Single().Marks);

        var a1 = _sectionRepo.GetAll(_fall).Single(s => s.CourseId == "c-A" && s.SectionCode == "1");
        a1.Schedule = [new SectionDaySchedule { Day = 5, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(a1);
        _store.Reload(_sectionRepo, [_fall]);

        Assert.Empty(_state.Current.Semesters[_fall].Watches.Single().Marks);
    }

    [Fact]
    public void TwoSemesters_AreAnalysedSeparately()
    {
        _state.MarkAllConflicts = true;
        // Winter's B 1 moves to Thursday: no conflict in Winter. A conflict across semesters must never be marked.
        var b1 = _sectionRepo.GetAll(_winter).Single(s => s.CourseId == "c-B" && s.SectionCode == "1");
        b1.Schedule = [new SectionDaySchedule { Day = 4, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(b1);
        _store.Reload(_sectionRepo, [_fall, _winter]);

        var snap = _state.Current;
        Assert.Equal(2, snap.Semesters.Count);
        Assert.Single(snap.Semesters[_fall].Watches.Single().Marks);
        Assert.Empty(snap.Semesters[_winter].Watches.Single().Marks);
        Assert.All(snap.Semesters[_fall].Watches.Single().Marks, m =>
        {
            Assert.Equal(_fall, m.A.SemesterId);
            Assert.Equal(_fall, m.B.SemesterId);
        });
        // the analysis is available too, per watch
        Assert.True(snap.Semesters[_fall].Watches.Single().Analysis.HasSlate);
    }

    [Fact]
    public void MarkAll_AddsHarmlessMarks_PersistsAndRedrawsTheGrid()
    {
        Assert.False(_state.MarkAllConflicts);
        Assert.Empty(_state.Current.Semesters[_fall].Watches.Single().Marks);

        int redraws = 0;
        _notifier.GridContentChanged += () => redraws++;
        _state.MarkAllConflicts = true;

        Assert.True(redraws >= 1);
        var mark = Assert.Single(_state.Current.Semesters[_fall].Watches.Single().Marks);
        Assert.Equal(ConflictMarkKind.Harmless, mark.Kind);
        Assert.True(AppSettings.Load().MarkAllAccessConflicts);

        _state.MarkAllConflicts = false;
        Assert.Empty(_state.Current.Semesters[_fall].Watches.Single().Marks);
        Assert.False(AppSettings.Load().MarkAllAccessConflicts);
    }

    [Fact]
    public void OnlyWatchedTagsAreAnalysed()
    {
        var envRepo = new SchedulingEnvironmentRepository(_fx.Db);
        envRepo.Insert(SchedulingEnvironmentTypes.Tag, new SchedulingEnvironmentValue { Name = "Not watched" });
        _state.Invalidate();
        Assert.Equal("Y2", _state.Current.Semesters[_fall].Watches.Single().Tag.Name);

        _tag.IsAccessWatch = false;
        envRepo.Update(_tag);
        _state.Invalidate();
        Assert.Empty(_state.Current.Semesters[_fall].Watches);
    }
}

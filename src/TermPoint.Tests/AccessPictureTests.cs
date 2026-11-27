using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// <see cref="AccessPicture"/>: the open watch (or a course alone), ticks, pins, stepping, Show other courses, what
/// a save keeps and resets, and closing itself when the watch goes. Real repositories over a temp database.
/// <para>
/// Seed: courses A and B and the watched tag "W". Fall: A has sections 1 (Monday) and 2 (Tuesday), B has 1 (Monday)
/// and 2 (Wednesday), so A 1 and B 1 conflict and there are three slates. Winter: A 1 and B 1 only, both Monday, so
/// the watch has no full slate there.
/// </para>
/// </summary>
public sealed class AccessPictureTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SectionStore _store = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly SectionRepository _sectionRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly AccessState _state;
    private readonly AccessPicture _picture;
    private readonly SchedulingEnvironmentValue _tag = new() { Name = "W", IsAccessWatch = true };
    private readonly string _fall;
    private readonly string _winter;

    public AccessPictureTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);

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
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, _tag);

        AddSection(_fall, "A", "1", 1); AddSection(_fall, "A", "2", 2);
        AddSection(_fall, "B", "1", 1); AddSection(_fall, "B", "2", 3);
        AddSection(_winter, "A", "1", 1); AddSection(_winter, "B", "1", 1);

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        _picture = new AccessPicture(_state);
        _store.Reload(_sectionRepo, [_fall]);
    }

    private void AddSection(string semesterId, string course, string code, int day) =>
        _sectionRepo.Insert(new Section
        {
            CourseId = "c-" + course, SemesterId = semesterId, SectionCode = code, TagIds = [_tag.Id],
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }],
        });

    private Section Get(string semesterId, string course, string code) =>
        _sectionRepo.GetAll(semesterId).Single(s => s.CourseId == "c-" + course && s.SectionCode == code);

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    [Fact]
    public void NothingIsOpenAtFirst()
    {
        Assert.False(_picture.IsOpen);
        Assert.Null(_picture.Analysis);
        Assert.Empty(_picture.CoveredSections);
        Assert.False(_picture.Covers(Get(_fall, "A", "1").Id));
        Assert.Equal(string.Empty, _picture.Name);
    }

    [Fact]
    public void OpenWatch_OpensItAndAnalysesIt()
    {
        int raised = 0;
        _picture.Changed += () => raised++;
        Assert.True(_picture.OpenWatch(_tag.Id));

        Assert.True(_picture.IsOpen);
        Assert.False(_picture.IsCourseOnly);
        Assert.Equal("W", _picture.Name);
        Assert.Equal(_fall, _picture.SemesterId);
        Assert.Equal(3, _picture.Analysis!.SlateCount);
        Assert.Equal(4, _picture.CoveredSections.Count);
        Assert.True(_picture.Covers(Get(_fall, "B", "2").Id));
        Assert.Equal(1, raised);

        _picture.Close();
        Assert.False(_picture.IsOpen);
        Assert.Equal(2, raised);
        _picture.Close();
        Assert.Equal(2, raised);
    }

    [Fact]
    public void OpenWatch_OfATagThatIsNotAWatch_DoesNothing()
    {
        Assert.False(_picture.OpenWatch("nope"));
        Assert.False(_picture.IsOpen);
    }

    [Fact]
    public void OpenWatch_PicksTheWorstSemester_AndTheSwitchChangesIt()
    {
        _store.Reload(_sectionRepo, [_fall, _winter]);
        Assert.True(_picture.OpenWatch(_tag.Id));
        Assert.Equal(_winter, _picture.SemesterId); // no full slate in Winter
        Assert.Equal([_fall, _winter], _picture.SemesterIds.OrderBy(x => x == _winter).ToArray());

        _picture.SetSemester(_fall);
        Assert.Equal(_fall, _picture.SemesterId);
        Assert.Equal(3, _picture.Analysis!.SlateCount);

        Assert.True(_picture.OpenWatch(_tag.Id, _fall));
        Assert.Equal(_fall, _picture.SemesterId);
    }

    [Fact]
    public void Ticks_NarrowTheAnalysis_AtLeastOneStays_AndTickAllRestores()
    {
        _picture.OpenWatch(_tag.Id);
        Assert.Null(_picture.TickedCourseIds);

        Assert.True(_picture.SetCourseTicked("c-B", false));
        Assert.Equal(["c-A"], _picture.Analysis!.ChosenCourseIds);
        Assert.True(_picture.Analysis.IsSubset);
        Assert.Equal(2, _picture.Analysis.SlateCount);

        Assert.False(_picture.SetCourseTicked("c-A", false)); // the last tick stays
        Assert.Equal(["c-A"], _picture.Analysis!.ChosenCourseIds);

        Assert.True(_picture.SetCourseTicked("c-B", true)); // all ticked again: back to "all"
        Assert.Null(_picture.TickedCourseIds);
        Assert.False(_picture.Analysis!.IsSubset);

        _picture.SetCourseTicked("c-B", false);
        _picture.TickAll();
        Assert.Null(_picture.TickedCourseIds);
        Assert.Equal(3, _picture.Analysis!.SlateCount);
    }

    [Fact]
    public void Pins_NarrowTheSlates_OnlyCoveredSectionsCanBePinned_AndUnpinAllClears()
    {
        _picture.OpenWatch(_tag.Id);
        var a1 = Get(_fall, "A", "1");
        Assert.True(_picture.TogglePin(a1.Id));
        Assert.True(_picture.IsPinned(a1.Id));
        Assert.Equal(1, _picture.Analysis!.SlateCount); // only (A 1, B 2)
        Assert.Contains(a1.Id, _picture.Analysis.PinnedSectionIds);

        Assert.False(_picture.TogglePin("not a section"));
        Assert.True(_picture.TogglePin(a1.Id));
        Assert.Empty(_picture.PinnedSectionIds);
        Assert.Equal(3, _picture.Analysis!.SlateCount);

        _picture.TogglePin(a1.Id);
        _picture.ClearPins();
        Assert.Empty(_picture.PinnedSectionIds);
        Assert.Equal(3, _picture.Analysis!.SlateCount);
    }

    [Fact]
    public void Stepping_ListsTheSlatesInOrder_AndStaysWithinThem()
    {
        _picture.OpenWatch(_tag.Id);
        Assert.True(_picture.CanStep);
        Assert.True(_picture.IsStepping);   // more than one slate: stepping starts by itself
        Assert.Equal(3, _picture.Slates.Count);
        Assert.Equal(0, _picture.SlateIndex);
        // the first slate is the lexicographically first: A 1 with B 2
        Assert.Equal(["1", "2"], _picture.CurrentSlate!.Select(s => s.SectionCode).ToArray());
        Assert.Equal("c-A", _picture.CurrentSlate![0].CourseId);

        _picture.Step(-1);
        Assert.Equal(0, _picture.SlateIndex);
        _picture.Step(1); _picture.Step(1); _picture.Step(1);
        Assert.Equal(2, _picture.SlateIndex);

    }

    [Fact]
    public void ASave_KeepsTicksAndPins_AndStartsSteppingAtTheFirstSlate()
    {
        _picture.OpenWatch(_tag.Id);
        _picture.SetCourseTicked("c-B", false);
        _picture.TogglePin(Get(_fall, "A", "2").Id);
        _picture.TickAll();
        // with A 2 pinned there are two slates; step to the second
        _picture.Step(1);
        Assert.Equal(1, _picture.SlateIndex);

        var b2 = Get(_fall, "B", "2");
        b2.Schedule = [new SectionDaySchedule { Day = 4, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(b2);
        _store.Reload(_sectionRepo, [_fall]);

        Assert.True(_picture.IsOpen);
        Assert.True(_picture.IsPinned(Get(_fall, "A", "2").Id));
        Assert.True(_picture.IsStepping);
        Assert.Equal(0, _picture.SlateIndex);
    }

    [Fact]
    public void ASave_KeepsTheTicks()
    {
        _picture.OpenWatch(_tag.Id);
        _picture.SetCourseTicked("c-B", false);
        _store.Reload(_sectionRepo, [_fall]);
        Assert.Equal(["c-A"], _picture.TickedCourseIds!.ToArray());
        Assert.Equal(["c-A"], _picture.Analysis!.ChosenCourseIds);
    }

    [Fact]
    public void APinForASectionThatIsGone_IsDropped()
    {
        _picture.OpenWatch(_tag.Id);
        var a2 = Get(_fall, "A", "2");
        _picture.TogglePin(a2.Id);
        _sectionRepo.Delete(a2.Id);
        _store.Reload(_sectionRepo, [_fall]);
        Assert.Equal(1, _picture.Analysis!.SlateCount); // only A 1 with B 2 is left
        Assert.Empty(_picture.PinnedSectionIds);
    }

    [Fact]
    public void WhenTheWatchGoes_ThePictureClosesItself()
    {
        _picture.OpenWatch(_tag.Id);
        int raised = 0;
        _picture.Changed += () => raised++;

        _tag.IsAccessWatch = false;
        _envRepo.Update(_tag);
        _state.Invalidate();

        Assert.False(_picture.IsOpen);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void WhenTheSemesterIsSwitchedAway_ThePictureFollowsTheWatchOrCloses()
    {
        _store.Reload(_sectionRepo, [_fall, _winter]);
        _picture.OpenWatch(_tag.Id, _fall);
        _store.Reload(_sectionRepo, [_winter]);
        Assert.True(_picture.IsOpen);
        Assert.Equal(_winter, _picture.SemesterId);

        _tag.IsAccessWatch = false;
        _envRepo.Update(_tag);
        _state.Invalidate();
        Assert.False(_picture.IsOpen);
    }

    [Fact]
    public void OpenCourse_AnalysesTheCourseAlone()
    {
        Assert.True(_picture.OpenCourse("c-A"));
        Assert.True(_picture.IsCourseOnly);
        Assert.Null(_picture.TagId);
        Assert.Equal("A", _picture.Name);
        Assert.Equal(["c-A"], _picture.Analysis!.ChosenCourseIds);
        Assert.Equal(2, _picture.CoveredSections.Count);
        Assert.False(_picture.Covers(Get(_fall, "B", "1").Id));
        Assert.False(_picture.OpenCourse("c-nobody"));
    }
}

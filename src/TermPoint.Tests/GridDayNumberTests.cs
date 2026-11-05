using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Collapse days, task 2 (<c>designs/collapse-days.md</c>): a Schedule grid column knows its real day
/// (<see cref="GridDayColumn.DayNumber"/>), and <see cref="GridData.DayOfColumn"/> /
/// <see cref="GridData.FirstColumnOfDay"/> read it instead of working the day out from the column's position.
/// The position-based version put a Sunday tile on "Saturday" when Sunday was on and Saturday off.
/// </summary>
public sealed class GridDayNumberTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock, built only by the view-model test.</summary>
    private ExtensionDataDbFixture? _fx;

    /// <summary>Saturday/Sunday settings as found, restored on dispose.</summary>
    private readonly (bool Sat, bool Sun) _savedDays =
        (AppSettings.Current.IncludeSaturday, AppSettings.Current.IncludeSunday);

    /// <summary>Restores the day settings, releases the database and the settings guard.</summary>
    public void Dispose()
    {
        AppSettings.Current.IncludeSaturday = _savedDays.Sat;
        AppSettings.Current.IncludeSunday   = _savedDays.Sun;
        _fx?.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>
    /// Builds a <see cref="GridData"/> with one column per (day, semester), in the grid's order
    /// (all of a day's semester sub-columns together).
    /// </summary>
    /// <param name="semesters">Semester sub-columns per day.</param>
    /// <param name="days">The day numbers shown, in order.</param>
    private static GridData Grid(int semesters, params int[] days)
    {
        var cols = new List<GridDayColumn>();
        foreach (var d in days)
            for (int s = 0; s < semesters; s++)
                cols.Add(new GridDayColumn($"Day{d}", d, [], $"Sem{s}"));
        return new GridData(480, 1320, cols, semesters);
    }

    /// <summary>The ordinary Monday to Friday week: column N is day N + 1.</summary>
    [Fact]
    public void MondayToFriday_ColumnsMapToTheirDays()
    {
        var g = Grid(1, 1, 2, 3, 4, 5);
        for (int c = 0; c < 5; c++) Assert.Equal(c + 1, g.DayOfColumn(c));
        for (int d = 1; d <= 5; d++) Assert.Equal(d - 1, g.FirstColumnOfDay(d));
        Assert.Null(g.FirstColumnOfDay(6));
        Assert.Null(g.FirstColumnOfDay(7));
    }

    /// <summary>A gap in the week (Tuesday missing): later columns keep their real day.</summary>
    [Fact]
    public void GapInTheWeek_TuesdayMissing()
    {
        var g = Grid(1, 1, 3, 4, 5);
        Assert.Equal(new[] { 1, 3, 4, 5 }, Enumerable.Range(0, 4).Select(g.DayOfColumn));
        Assert.Equal(0, g.FirstColumnOfDay(1));
        Assert.Null(g.FirstColumnOfDay(2));
        Assert.Equal(1, g.FirstColumnOfDay(3));
        Assert.Equal(3, g.FirstColumnOfDay(5));
    }

    /// <summary>Saturday on: column 5 is Saturday.</summary>
    [Fact]
    public void SaturdayOn()
    {
        var g = Grid(1, 1, 2, 3, 4, 5, 6);
        Assert.Equal(6, g.DayOfColumn(5));
        Assert.Equal(5, g.FirstColumnOfDay(6));
        Assert.Null(g.FirstColumnOfDay(7));
    }

    /// <summary>The live bug: Sunday on, Saturday off. Column 5 is Sunday, not Saturday.</summary>
    [Fact]
    public void SundayOnSaturdayOff_SundayIsSundayNotSaturday()
    {
        var g = Grid(1, 1, 2, 3, 4, 5, 7);
        Assert.Equal(7, g.DayOfColumn(5));
        Assert.Equal(5, g.FirstColumnOfDay(7));
        Assert.Null(g.FirstColumnOfDay(6));
    }

    /// <summary>Two semesters: each day owns two columns, and a day's first column is its first sub-column.</summary>
    [Fact]
    public void SeveralSemesters_TwoSubColumnsPerDay()
    {
        var g = Grid(2, 1, 3, 7);
        Assert.Equal(new[] { 1, 1, 3, 3, 7, 7 }, Enumerable.Range(0, 6).Select(g.DayOfColumn));
        Assert.Equal(0, g.FirstColumnOfDay(1));
        Assert.Equal(2, g.FirstColumnOfDay(3));
        Assert.Equal(4, g.FirstColumnOfDay(7));
        Assert.Null(g.FirstColumnOfDay(2));
    }

    /// <summary>
    /// The view model's own grid carries the day numbers: Monday to Friday by default, and with Sunday on and
    /// Saturday off the last column is day 7 (so a Sunday tile is not read as Saturday).
    /// </summary>
    [Fact]
    public void ViewModelGrid_SetsDayNumbers()
    {
        _fx = new ExtensionDataDbFixture();
        var db = _fx.Db;
        var sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        var semesterRepo = new SemesterRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var instructorRepo = new InstructorRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);
        var roomRepo = new RoomRepository(db);

        var subject = new Subject { Id = "subj", Name = "Biology", CalendarAbbreviation = "BIOL" };
        subjectRepo.Insert(subject);
        var course = new Course { Id = "c", SubjectId = subject.Id, CalendarCode = "BIOL101", Title = "Bio" };
        courseRepo.Insert(course);
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        semesterRepo.Insert(fall);
        sectionRepo.Insert(new Section
        {
            CourseId = course.Id, SemesterId = fall.Id, SectionCode = "A",
            Schedule = [new SectionDaySchedule { Day = 7, StartMinutes = 510, DurationMinutes = 90 }],
        });

        AppSettings.Current.IncludeSaturday = false;
        AppSettings.Current.IncludeSunday   = true;

        var semesterContext = new SemesterContext();
        var sectionStore = new SectionStore();
        using var focus = new FocusService(
            subjectRepo, courseRepo, sectionRepo, semesterRepo, ayRepo, semesterContext, sectionStore, null);
        using var presenter = new SubjectColorPresenter(
            new SubjectColorStore(["#B7E79D"], () => new AppSettings()), focus, sectionStore, courseRepo, subjectRepo);
        var notifier = new GridChangeNotifier();
        var grid = new ScheduleGridViewModel(
            sectionRepo, courseRepo, instructorRepo, roomRepo, subjectRepo, envRepo,
            new CampusRepository(db), semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            sectionStore, new MeetingStore(), new MeetingRepository(db), notifier, new InstructorCommitmentRepository(db),
            _fx.Lock, new SharedScheduleService(),
            new AccessPanelViewModel(new ProgramWatchRepository(db), semesterContext, notifier, envRepo, courseRepo,
                () => true),
            focus, presenter);

        semesterContext.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { fall.Id });
        sectionStore.Reload(sectionRepo, [fall.Id]);

        var cols = grid.GridData.DayColumns;
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 7 }, cols.Select(c => c.DayNumber));
        Assert.Equal("Sunday", cols[^1].Header);
        Assert.Equal(7, grid.GridData.DayOfColumn(5));
        Assert.Single(cols[^1].Tiles);
    }
}

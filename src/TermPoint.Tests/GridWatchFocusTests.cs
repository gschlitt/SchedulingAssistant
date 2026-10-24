using System;
using System.Collections.Generic;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Program access watches scan only the sections in the chosen subjects (spec item 20, decided 2026-10-23): a clash
/// with a section in an unchosen subject is not reported, because the user can't see that section and each such
/// report needed an investigation. Real <see cref="ScheduleGridViewModel"/> and <see cref="AccessPanelViewModel"/>
/// over a temp database.
/// <para>
/// Seed: one Fall semester, a Biology and a Chemistry section meeting at the same time (Monday 8:30), and one
/// enabled course-mode watch covering both courses, so under All the watch reports exactly one conflict.
/// </para>
/// </summary>
public sealed class GridWatchFocusTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json (SemesterContext saves the selection).</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly FocusService _focus;
    private readonly ScheduleGridViewModel _grid;

    private readonly Subject _biol = new() { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Subject _chem = new() { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" };

    public GridWatchFocusTests()
    {
        var db = _fx.Db;
        var sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        var semesterRepo = new SemesterRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);
        var watchRepo = new ProgramWatchRepository(db);

        subjectRepo.Insert(_biol);
        subjectRepo.Insert(_chem);
        var biolCourse = new Course { Id = "c-biol", SubjectId = _biol.Id, CalendarCode = "BIOL101", Title = "Bio" };
        var chemCourse = new Course { Id = "c-chem", SubjectId = _chem.Id, CalendarCode = "CHEM101", Title = "Chem" };
        courseRepo.Insert(biolCourse);
        courseRepo.Insert(chemCourse);

        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        semesterRepo.Insert(fall);

        // Same day and time, so a watch over both courses sees one clash.
        foreach (var course in new[] { biolCourse, chemCourse })
            sectionRepo.Insert(new Section
            {
                CourseId = course.Id, SemesterId = fall.Id, SectionCode = "A",
                Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
            });

        watchRepo.Save(fall.Id, new ProgramWatch
        {
            Name = "Pre-med", Mode = ProgramWatchMode.Course, IsEnabled = true,
            CourseIds = [biolCourse.Id, chemCourse.Id],
        });

        _focus = new FocusService(
            subjectRepo, courseRepo, sectionRepo, semesterRepo, ayRepo, _semesterContext, _sectionStore, null);

        var notifier = new GridChangeNotifier();
        _grid = new ScheduleGridViewModel(
            sectionRepo, courseRepo, new InstructorRepository(db), new RoomRepository(db), subjectRepo, envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), notifier, new InstructorCommitmentRepository(db),
            _fx.Lock, new SharedScheduleService(),
            new AccessPanelViewModel(watchRepo, _semesterContext, notifier, envRepo, courseRepo, () => true),
            _focus);

        // Open the Fall semester and load its sections, as the section list does in the app.
        _semesterContext.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { fall.Id });
        _sectionStore.Reload(sectionRepo, [fall.Id]);
    }

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>Under All, the watch sees both sections and reports the clash.</summary>
    [Fact]
    public void UnderAll_WatchReportsTheClash()
    {
        Assert.Equal(1, _grid.Access.TotalConflictCount);
    }

    /// <summary>With only Biology chosen, the Chemistry section is not scanned, so no clash is reported.</summary>
    [Fact]
    public void ClashWithAnUnchosenSubject_IsNotReported()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        Assert.Equal(0, _grid.Access.TotalConflictCount);
    }

    /// <summary>With both subjects chosen, the clash is reported again.</summary>
    [Fact]
    public void ClashWithinTheChosenSubjects_IsReported()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]));

        Assert.Equal(1, _grid.Access.TotalConflictCount);
    }
}

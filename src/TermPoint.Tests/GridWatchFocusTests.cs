using System;
using System.Collections.Generic;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// The grid's Access conflict marks come from <see cref="AccessState"/> over the WHOLE semester (Access Analyzer
/// rule 29, Q41), not just the sections the subject focus shows. This replaces the old rule (spec item 20, 2026-10-23)
/// that watches scanned only the visible sections: a clash with a section in an unchosen subject now still glows the
/// visible section and its hover says the partner's subject isn't one of the chosen Subjects. Real <see cref="ScheduleGridViewModel"/>
/// over a temp database.
/// <para>
/// Seed: one Fall semester, a Biology and a Chemistry section meeting at the same time (Monday 8:30), both carrying
/// a watched tag "Pre-med", so under All the watch marks exactly one conflict (one overlapping meeting pair).
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

    private readonly ISectionRepository _sectionRepo;
    private readonly string _fallId;
    private readonly Section _biolSection;
    private readonly Section _chemSection;

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

        // A watched tag on both sections, same day and time, so the watch sees one clash.
        var tag = new SchedulingEnvironmentValue { Name = "Pre-med", IsAccessWatch = true };
        envRepo.Insert(SchedulingEnvironmentTypes.Tag, tag);
        Section Seed(Course course)
        {
            var section = new Section
            {
                CourseId = course.Id, SemesterId = fall.Id, SectionCode = "A", TagIds = [tag.Id],
                Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
            };
            sectionRepo.Insert(section);
            return section;
        }
        _biolSection = Seed(biolCourse);
        _chemSection = Seed(chemCourse);
        _sectionRepo = sectionRepo;
        _fallId = fall.Id;

        _focus = new FocusService(
            subjectRepo, courseRepo, sectionRepo, semesterRepo, ayRepo, _semesterContext, _sectionStore, null);

        var notifier = new GridChangeNotifier();
        _grid = new ScheduleGridViewModel(
            sectionRepo, courseRepo, new InstructorRepository(db), new RoomRepository(db), subjectRepo, envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), notifier, new InstructorCommitmentRepository(db),
            _fx.Lock, new SharedScheduleService(),
            _focus, FocusTestSupport.NoColours(courseRepo, subjectRepo, _sectionStore), new BlockPatternRepository(db), new FakeSectionCardState(), new LegalStartTimeRepository(db), new SectionMarks(),
            AccessStateTestSupport.For(db, _sectionStore, notifier));

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

    /// <summary>Under All, the watch marks the clash: one drawing record, and the hover says what it costs.</summary>
    [Fact]
    public void UnderAll_WatchMarksTheClash()
    {
        Assert.Single(_grid.GridData.ProgramConflicts);
        var lines = _grid.GridData.ConflictHoverLines[(_biolSection.Id, 1)];
        Assert.Contains("BIOL101 A conflicts with CHEM101 A, so **Pre-med** has no full slate. Moving BIOL101 A or CHEM101 A brings one back.", lines);
        Assert.DoesNotContain(lines, l => l.Contains("hidden by the filters"));
    }

    /// <summary>
    /// With only Biology chosen, the clash is still marked (whole semester, rule 29): the Biology section glows, and
    /// its hover says the Chemistry partner's subject isn't one of the chosen Subjects (not "hidden by the filters").
    /// </summary>
    [Fact]
    public void ClashWithAnUnchosenSubject_StillMarked_AndSaysHidden()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));

        Assert.Single(_grid.GridData.ProgramConflicts);
        var lines = _grid.GridData.ConflictHoverLines[(_biolSection.Id, 1)];
        Assert.Contains("CHEM101 A isn't shown: its subject isn't one of your chosen Subjects.", lines);
        Assert.DoesNotContain(lines, l => l.Contains("hidden by the filters"));
    }

    /// <summary>With both subjects chosen, the partner is shown, so there is no hidden-partner line.</summary>
    [Fact]
    public void ClashWithinTheChosenSubjects_HasNoHiddenLine()
    {
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id]));
        _focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id, _chem.Id]));

        Assert.Single(_grid.GridData.ProgramConflicts);
        Assert.DoesNotContain(_grid.GridData.ConflictHoverLines[(_biolSection.Id, 1)], l => l.Contains("hidden by the filters"));
    }

    /// <summary>
    /// A save ends in a store reload: moving the Chemistry section clears the mark, moving it back brings it back.
    /// A reader's Refresh ends in the same reload, so this covers it too.
    /// </summary>
    [Fact]
    public void MarksFollowAReload_AfterTheSectionsChange()
    {
        _chemSection.Schedule = [new SectionDaySchedule { Day = 2, StartMinutes = 510, DurationMinutes = 90 }];
        _sectionRepo.Update(_chemSection);
        _sectionStore.Reload(_sectionRepo, [_fallId]);
        Assert.Empty(_grid.GridData.ProgramConflicts);
        Assert.Empty(_grid.GridData.ConflictHoverLines);

        _chemSection.Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(_chemSection);
        _sectionStore.Reload(_sectionRepo, [_fallId]);
        Assert.Single(_grid.GridData.ProgramConflicts);
    }

    /// <summary>A tag that stops being watched marks nothing once the state is invalidated and the grid redraws.</summary>
    [Fact]
    public void TagNoLongerWatched_MarksNothing()
    {
        var envRepo = new SchedulingEnvironmentRepository(_fx.Db);
        var tag = envRepo.GetAll(SchedulingEnvironmentTypes.Tag).Single();
        tag.IsAccessWatch = false;
        envRepo.Update(tag);

        _sectionStore.Reload(_sectionRepo, [_fallId]);

        Assert.Empty(_grid.GridData.ProgramConflicts);
    }

    /// <summary>
    /// A tile the conflict marks speak about is hovered with their lines alone, as markup (no time range: Greg,
    /// 2026-11-25); a tile nothing speaks about keeps the time range.
    /// </summary>
    [Fact]
    public void TileTooltip_IsTheConflictLinesAlone_WithoutTheTime()
    {
        var tile = _grid.GridData.DayColumns.SelectMany(c => c.Tiles)
            .First(t => t.Entries.Any(e => e.SectionId == _biolSection.Id));

        var tip = ScheduleGridViewModel.BuildTileTooltip(tile, _grid.GridData.ConflictHoverLines, 1);

        Assert.DoesNotContain("0830-1000", tip.Lines);
        Assert.NotEmpty(tip.Lines);
        Assert.True(tip.Markup);

        var plain = ScheduleGridViewModel.BuildTileTooltip(tile, null, 1);
        Assert.Equal(["0830-1000"], plain.Lines);
        Assert.False(plain.Markup);
    }
}

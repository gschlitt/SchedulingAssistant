using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Collapse days, task 3 (<c>designs/collapse-days.md</c>): the fold state and commands on
/// <see cref="ScheduleGridViewModel"/>, the folded columns and their <see cref="FoldedDaySummary"/> in
/// <see cref="GridData"/>, the resets, and the export caption. View-model level only, no rendering.
/// <para>
/// World (BIOL 101, two semesters of one academic year): Fall section A meets Mon 8:30, Wed 8:30 and TWICE on Tuesday
/// (10:00 and 13:20); Fall section B meets Tue 8:30; Fall section C meets Fri 8:30; Winter section A meets Tue 8:30.
/// Thursday is empty. Real view model, focus service and presenter over a temp database.
/// </para>
/// </summary>
public sealed class GridFoldDaysTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json (SemesterContext saves the selection).</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Settings this test class touches, restored on dispose (AppSettings.Current is process-wide).</summary>
    private readonly (bool Sat, bool Sun, bool Meetings) _savedSettings =
        (AppSettings.Current.IncludeSaturday, AppSettings.Current.IncludeSunday, AppSettings.Current.ShowMeetingsOnGrid);

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly MeetingRepository _meetingRepo;
    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly MeetingStore _meetingStore = new();
    private readonly FocusService _focus;
    private readonly SubjectColorPresenter _presenter;
    private readonly ScheduleGridViewModel _grid;
    private readonly BlockPatternRepository _patternRepo;
    private readonly Subject _biol = new() { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Course _course = new() { Id = "c-biol", SubjectId = "subj-biol", CalendarCode = "BIOL101", Title = "Bio" };
    private readonly AcademicYear _year = new() { Name = "2025-2026" };
    private readonly Semester _fall;
    private readonly Semester _winter;
    private string _fallA = "", _fallB = "";

    /// <summary>Seeds the world and builds the grid (no semester selected yet; call <see cref="Open"/>).</summary>
    public GridFoldDaysTests()
    {
        AppSettings.Current.IncludeSaturday = false;
        AppSettings.Current.IncludeSunday = false;
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _meetingRepo = new MeetingRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);

        _subjectRepo.Insert(_biol);
        _courseRepo.Insert(_course);
        _ayRepo.Insert(_year);
        _fall = new Semester { AcademicYearId = _year.Id, Name = "Fall", SortOrder = 0 };
        _winter = new Semester { AcademicYearId = _year.Id, Name = "Winter", SortOrder = 1 };
        _semesterRepo.Insert(_fall);
        _semesterRepo.Insert(_winter);

        _fallA = AddSection(_fall, "A", (1, 510), (3, 510), (2, 600), (2, 800));
        _fallB = AddSection(_fall, "B", (2, 510));
        AddSection(_fall, "C", (5, 510));
        AddSection(_winter, "A", (2, 510));

        _focus = new FocusService(
            _subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo, _semesterContext, _sectionStore, null);
        _presenter = new SubjectColorPresenter(
            new SubjectColorStore(["#B7E79D"], () => new AppSettings()), _focus, _sectionStore, _courseRepo, _subjectRepo);
        var notifier = new GridChangeNotifier();
        _grid = new ScheduleGridViewModel(
            _sectionRepo, _courseRepo, new InstructorRepository(db), new RoomRepository(db), _subjectRepo, envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, _meetingStore, _meetingRepo, notifier, new InstructorCommitmentRepository(db),
            _fx.Lock, new SharedScheduleService(),
            new AccessPanelViewModel(new ProgramWatchRepository(db), _semesterContext, notifier, envRepo, _courseRepo,
                () => true),
            _focus, _presenter, _patternRepo = new BlockPatternRepository(db));
    }

    /// <summary>Restores settings, releases the presenter, the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        AppSettings.Current.IncludeSaturday = _savedSettings.Sat;
        AppSettings.Current.IncludeSunday = _savedSettings.Sun;
        AppSettings.Current.ShowMeetingsOnGrid = _savedSettings.Meetings;
        _presenter.Dispose();
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Inserts a BIOL 101 section with the given (day, start) meetings, each 90 minutes.</summary>
    /// <returns>The new section's id.</returns>
    private string AddSection(Semester sem, string code, params (int Day, int Start)[] meetings)
    {
        var s = new Section
        {
            CourseId = _course.Id, SemesterId = sem.Id, SectionCode = code,
            Schedule = meetings.Select(m => new SectionDaySchedule { Day = m.Day, StartMinutes = m.Start, DurationMinutes = 90 }).ToList(),
        };
        _sectionRepo.Insert(s);
        return s.Id;
    }

    /// <summary>Shows these semesters (as ticking them in the semester picker does), which reloads the grid.</summary>
    private void Open(params Semester[] semesters)
    {
        var ids = semesters.Select(s => s.Id).ToList();
        _semesterContext.Reload(_ayRepo, _semesterRepo, _year.Id, new HashSet<string>(ids));
        _sectionStore.Reload(_sectionRepo, ids);
    }

    /// <summary>The columns of one day.</summary>
    private List<GridDayColumn> Cols(int day) => _grid.GridData.DayColumns.Where(c => c.DayNumber == day).ToList();

    /// <summary>The folded days, sorted, for easy comparison.</summary>
    private int[] Folded() => _grid.FoldedDays.OrderBy(d => d).ToArray();

    // ── Columns and summaries ──────────────────────────────────────────────────

    /// <summary>With nothing folded the grid is as before: no folded flag, no summary, tiles present.</summary>
    [Fact]
    public void NothingFolded_ColumnsAreUnchanged()
    {
        Open(_fall);
        Assert.Empty(_grid.FoldedDays);
        Assert.Equal("", _grid.FoldedDaysCaption);
        Assert.All(_grid.GridData.DayColumns, c => { Assert.False(c.IsFolded); Assert.Null(c.Folded); });
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, _grid.GridData.DayColumns.Select(c => c.DayNumber));
        Assert.Equal(3, Cols(2).Single().Tiles.Count);
    }

    /// <summary>A folded day (Tuesday) has no tiles and a summary: sections counted once, with each meeting listed.</summary>
    [Fact]
    public void FoldedDay_HasNoTilesAndOneSummary_SectionCountedOnce()
    {
        Open(_fall);
        _grid.ToggleDay(2);
        var tue = Assert.Single(Cols(2));
        Assert.True(tue.IsFolded);
        Assert.Empty(tue.Tiles);
        // Section A has two Tuesday meetings, B has one: 2 sections, 3 meetings.
        Assert.Equal(2, tue.Folded!.SectionCount);
        Assert.Equal(3, tue.Folded.SectionMeetings.Count);
        Assert.False(tue.Folded.HasOtherThings);
        Assert.Equal(new[] { 510, 600, 800 }, tue.Folded.SectionMeetings.Select(m => m.StartMinutes).OrderBy(x => x));
        Assert.Equal(new[] { 1, 3, 4, 5 }, _grid.GridData.DayColumns.Where(c => !c.IsFolded).Select(c => c.DayNumber));
    }

    /// <summary>An empty folded day (Thursday) still has a summary, reading zero.</summary>
    [Fact]
    public void FoldedEmptyDay_SummaryReadsZero()
    {
        Open(_fall);
        _grid.ToggleDay(4);
        var thu = Assert.Single(Cols(4));
        Assert.Equal(0, thu.Folded!.SectionCount);
        Assert.False(thu.Folded.HasOtherThings);
        Assert.Empty(thu.Folded.SectionMeetings);
    }

    /// <summary>Two semesters: both Tuesday sub-columns are folded, the summary is on the first only and combines both.</summary>
    [Fact]
    public void TwoSemesters_OneSummaryOnFirstSubColumn_CombinedCount()
    {
        Open(_fall, _winter);
        _grid.ToggleDay(2);
        var tue = Cols(2);
        Assert.Equal(2, tue.Count);
        Assert.All(tue, c => { Assert.True(c.IsFolded); Assert.Empty(c.Tiles); });
        Assert.NotNull(tue[0].Folded);
        Assert.Null(tue[1].Folded);
        // Fall A + Fall B + Winter A (a different section with the same code) = 3 distinct sections.
        Assert.Equal(3, tue[0].Folded!.SectionCount);
        Assert.Equal(4, tue[0].Folded!.SectionMeetings.Count);
        // Other days keep both sub-columns, unfolded, with no summary.
        Assert.All(Cols(1), c => { Assert.False(c.IsFolded); Assert.Null(c.Folded); });
        Assert.Equal(2, Cols(1).Count);
    }

    /// <summary>A day with only a faculty meeting reads zero sections but "has other things".</summary>
    [Fact]
    public void FoldedDayWithAMeeting_HasOtherThings()
    {
        AppSettings.Current.ShowMeetingsOnGrid = true;
        _meetingRepo.Insert(new Meeting
        {
            Title = "Faculty Meeting", SemesterId = _fall.Id,
            Schedule = [new SectionDaySchedule { Day = 4, StartMinutes = 600, DurationMinutes = 60 }],
        });
        Open(_fall);
        _grid.ToggleDay(4);
        var thu = Cols(4).Single().Folded!;
        Assert.Equal(0, thu.SectionCount);
        Assert.True(thu.HasOtherThings);
    }

    /// <summary>
    /// <see cref="ScheduleGridViewModel.SummarizeDay"/> on every block kind: commitments, meetings, shared-schedule
    /// and overlay section blocks make "other things"; overlay sections are not counted.
    /// </summary>
    [Fact]
    public void SummarizeDay_OtherThingsAndCounts()
    {
        var plain = new SectionMeetingBlock(2, 510, 600, false, "BIOL101 A", "", "s1");
        var dup   = new SectionMeetingBlock(2, 700, 790, false, "BIOL101 A", "", "s1");
        var overlay = new SectionMeetingBlock(2, 510, 600, true, "BIOL101 B", "", "s2");
        var otherDay = new CommitmentBlock(3, 510, 600, "Committee", "k1");

        var s = ScheduleGridViewModel.SummarizeDay([plain, dup, otherDay], 2);
        Assert.Equal(1, s.SectionCount);
        Assert.False(s.HasOtherThings);
        Assert.Equal(2, s.SectionMeetings.Count);
        Assert.Equal(new[] { (510, 600), (700, 790) }, s.SectionMeetings.Select(m => (m.StartMinutes, m.EndMinutes)).ToArray());
        Assert.Empty(s.OtherSpans);

        Assert.True(ScheduleGridViewModel.SummarizeDay([otherDay], 3).HasOtherThings);
        // Every kind of other thing carries its kind and its span.
        var kinds = ScheduleGridViewModel.SummarizeDay(
            [new MeetingBlock(2, 600, 660, false, "T", "", "m"), new CommitmentBlock(2, 700, 760, "C", "k"),
             new SharedScheduleBlock(2, 800, 860, "CHEM101 A"), overlay], 2).OtherSpans;
        Assert.Equal(
            new[] { (FoldedOtherKind.Meeting, 600, 660), (FoldedOtherKind.Commitment, 700, 760),
                    (FoldedOtherKind.SharedSchedule, 800, 860), (FoldedOtherKind.OverlaySection, 510, 600) },
            kinds.Select(k => (k.Kind, k.StartMinutes, k.EndMinutes)).ToArray());
        Assert.True(ScheduleGridViewModel.SummarizeDay([new MeetingBlock(2, 1, 2, false, "T", "", "m")], 2).HasOtherThings);
        Assert.True(ScheduleGridViewModel.SummarizeDay([new SharedScheduleBlock(2, 1, 2, "CHEM101 A")], 2).HasOtherThings);
        var o = ScheduleGridViewModel.SummarizeDay([overlay], 2);
        Assert.Equal(0, o.SectionCount);
        Assert.True(o.HasOtherThings);
        Assert.Empty(o.SectionMeetings);
        Assert.Equal(FoldedOtherKind.OverlaySection, Assert.Single(o.OtherSpans).Kind);
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    /// <summary>ToggleDay folds an open day and opens a folded one; the change is announced.</summary>
    [Fact]
    public void ToggleDay_FoldsAndOpens_RaisesChange()
    {
        Open(_fall);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)_grid).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _grid.ToggleDay(2);
        Assert.Equal(new[] { 2 }, Folded());
        Assert.Contains(nameof(ScheduleGridViewModel.FoldedDays), raised);
        Assert.Contains(nameof(ScheduleGridViewModel.FoldedDaysCaption), raised);
        _grid.ToggleDay(2);
        Assert.Empty(_grid.FoldedDays);
        Assert.All(_grid.GridData.DayColumns, c => Assert.False(c.IsFolded));
    }

    /// <summary>The last open day can't be folded by ToggleDay, and CanFold says so.</summary>
    [Fact]
    public void ToggleDay_RefusesLastOpenDay()
    {
        Open(_fall);
        foreach (var d in new[] { 1, 2, 3, 4 }) _grid.ToggleDay(d);
        Assert.Equal(new[] { 1, 2, 3, 4 }, Folded());
        Assert.False(_grid.CanFold(5));
        _grid.ToggleDay(5);
        Assert.Equal(new[] { 1, 2, 3, 4 }, Folded());
        Assert.True(_grid.CanFold(5) == false);
        _grid.ToggleDay(4); // open one again
        Assert.True(_grid.CanFold(5));
    }

    /// <summary>CanFold is false for a day that isn't shown and for one already folded, true for an open day.</summary>
    [Fact]
    public void CanFold_ShownOpenDaysOnly()
    {
        Open(_fall);
        Assert.True(_grid.CanFold(1));
        Assert.False(_grid.CanFold(6));
        Assert.False(_grid.CanFold(7));
        _grid.ToggleDay(1);
        Assert.False(_grid.CanFold(1));
    }

    /// <summary>ToggleDay on a day that isn't shown does nothing.</summary>
    [Fact]
    public void ToggleDay_HiddenDay_NoOp()
    {
        Open(_fall);
        _grid.ToggleDay(6);
        Assert.Empty(_grid.FoldedDays);
    }

    /// <summary>ShowOnlyDay opens one day and folds the rest; a hidden day is a no-op that keeps what was folded.</summary>
    [Fact]
    public void ShowOnlyDay_FoldsTheRest_HiddenDayNoOp()
    {
        Open(_fall);
        _grid.ShowOnlyDay(4);
        Assert.Equal(new[] { 1, 2, 3, 5 }, Folded());
        _grid.ShowOnlyDay(7);
        Assert.Equal(new[] { 1, 2, 3, 5 }, Folded());
        _grid.ShowOnlyDay(4); // already so
        Assert.Equal(new[] { 1, 2, 3, 5 }, Folded());
        Assert.Equal(1, _grid.GridData.DayColumns.Count(c => !c.IsFolded));
    }

    /// <summary>ShowPattern opens exactly the pattern's shown days, whatever was folded before.</summary>
    [Fact]
    public void ShowPattern_ExactlyThePatternOpen()
    {
        Open(_fall);
        _grid.ToggleDay(1); // Monday folded before; MWF must open it again
        _grid.ShowPattern([1, 3, 5]);
        Assert.Equal(new[] { 2, 4 }, Folded());
        Assert.Equal("Mon, Wed, Fri only", _grid.FoldedDaysCaption);
    }

    /// <summary>A pattern day that isn't shown (Saturday off) is ignored; a pattern with no shown days does nothing.</summary>
    [Fact]
    public void ShowPattern_HiddenDaysIgnored_NoShownDaysNoOp()
    {
        Open(_fall);
        _grid.ShowPattern([1, 6]);
        Assert.Equal(new[] { 2, 3, 4, 5 }, Folded());
        _grid.ShowPattern([6, 7]);
        Assert.Equal(new[] { 2, 3, 4, 5 }, Folded());
    }

    /// <summary>ShowAllDays clears everything.</summary>
    [Fact]
    public void ShowAllDays_Clears()
    {
        Open(_fall);
        _grid.ShowPattern([2]);
        _grid.ShowAllDays();
        Assert.Empty(_grid.FoldedDays);
        Assert.Equal("", _grid.FoldedDaysCaption);
        Assert.All(_grid.GridData.DayColumns, c => Assert.False(c.IsFolded));
    }

    /// <summary>Commands before any semester is open (a blank grid) are harmless no-ops.</summary>
    [Fact]
    public void BlankGrid_CommandsAreNoOps()
    {
        _grid.ToggleDay(1);
        _grid.ShowOnlyDay(1);
        _grid.ShowPattern([1, 3]);
        _grid.ShowAllDays();
        Assert.Empty(_grid.FoldedDays);
        Assert.False(_grid.CanFold(1));
    }

    /// <summary>The caption lists the open days in week order and is empty when nothing is folded.</summary>
    [Fact]
    public void Caption_OpenDaysInWeekOrder()
    {
        Open(_fall);
        Assert.Equal("", _grid.FoldedDaysCaption);
        _grid.ShowPattern([2, 4]);
        Assert.Equal("Tue, Thu only", _grid.FoldedDaysCaption);
        _grid.ShowOnlyDay(5);
        Assert.Equal("Fri only", _grid.FoldedDaysCaption);
    }

    // ── A fold does not reload ─────────────────────────────────────────────────

    /// <summary>
    /// A fold redraws from the cached blocks: a course renamed straight in the database (no event) does not show on a
    /// fold, but does on the next ordinary reload, and that reload keeps the fold.
    /// </summary>
    [Fact]
    public void Fold_UsesCache_NotTheDatabase_AndReloadKeepsTheFold()
    {
        Open(_fall);
        string Monday() => Cols(1).Single().Tiles.Single().Entries.Single().Label;
        Assert.Equal("BIOL101 A", Monday());

        var renamed = _courseRepo.GetById(_course.Id)!;
        renamed.CalendarCode = "BIOL999";
        _courseRepo.Update(renamed);

        _grid.ToggleDay(2);
        Assert.Equal("BIOL101 A", Monday());

        _grid.Reload();
        Assert.Equal("BIOL999 A", Monday());
        Assert.Equal(new[] { 2 }, Folded());
        Assert.True(Cols(2).Single().IsFolded);
    }

    // ── Resets and non-resets ──────────────────────────────────────────────────

    /// <summary>Switching semester (or which semesters are shown) opens the full week.</summary>
    [Fact]
    public void SemesterSwitch_Resets()
    {
        Open(_fall);
        _grid.ShowOnlyDay(2);
        Open(_winter);
        Assert.Empty(_grid.FoldedDays);
        _grid.ShowOnlyDay(2);
        Open(_fall, _winter);
        Assert.Empty(_grid.FoldedDays);
        Assert.All(_grid.GridData.DayColumns, c => Assert.False(c.IsFolded));
    }

    /// <summary>A subject-focus change opens the full week.</summary>
    [Fact]
    public void FocusChange_Resets()
    {
        Open(_fall);
        _grid.ShowOnlyDay(2);
        Assert.True(_focus.SetFocus(SubjectFocus.ForSubjects([_biol.Id])));
        Assert.Empty(_grid.FoldedDays);
        Assert.All(_grid.GridData.DayColumns, c => Assert.False(c.IsFolded));
    }

    /// <summary>Saturday turned off while folded: it drops out, the other folded days stay.</summary>
    [Fact]
    public void SaturdayOff_DropsOutOfFoldedDays()
    {
        AppSettings.Current.IncludeSaturday = true;
        Open(_fall);
        _grid.ToggleDay(6);
        _grid.ToggleDay(2);
        Assert.Equal(new[] { 2, 6 }, Folded());

        AppSettings.Current.IncludeSaturday = false;
        _grid.Reload();
        Assert.Equal(new[] { 2 }, Folded());
        Assert.DoesNotContain(_grid.GridData.DayColumns, c => c.DayNumber == 6);

        // Turned back on, it comes back unfolded.
        AppSettings.Current.IncludeSaturday = true;
        _grid.Reload();
        Assert.Equal(new[] { 2 }, Folded());
        Assert.False(Cols(6).Single().IsFolded);
    }

    /// <summary>If dropping the hidden day would leave every shown day folded, the week opens up instead.</summary>
    [Fact]
    public void SaturdayOff_WhenOnlySaturdayWasOpen_ClearsEverything()
    {
        AppSettings.Current.IncludeSaturday = true;
        Open(_fall);
        _grid.ShowOnlyDay(6);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Folded());

        AppSettings.Current.IncludeSaturday = false;
        _grid.Reload();
        Assert.Empty(_grid.FoldedDays);
        Assert.All(_grid.GridData.DayColumns, c => Assert.False(c.IsFolded));
    }

    /// <summary>A filter change, a plain reload and a font-size change leave the folded days alone.</summary>
    [Fact]
    public void FilterReloadAndFontSize_KeepTheFold()
    {
        Open(_fall);
        _grid.ShowPattern([1, 3]);
        var before = Folded();

        _grid.Filter.Subjects.Single(i => i.Id == _biol.Id).IsSelected = true;
        Assert.Equal(before, Folded());
        _grid.Reload();
        Assert.Equal(before, Folded());
        _grid.TileFontSize = 9;
        Assert.Equal(before, Folded());
        Assert.True(Cols(2).Single().IsFolded);
        Assert.False(Cols(1).Single().IsFolded);
    }

    /// <summary>Folding doesn't change the selection.</summary>
    [Fact]
    public void Fold_DoesNotChangeSelection()
    {
        Open(_fall);
        _sectionStore.SetSelection(_fallB);
        _grid.ShowOnlyDay(1);
        Assert.Contains(_fallB, _grid.SelectedSectionIds);
    }

    // ── Day-header menu (collapse days, task 4) ────────────────────────────────

    /// <summary>Saves a block pattern in the test database.</summary>
    /// <param name="name">Pattern name, e.g. "MWF".</param>
    /// <param name="days">Its day numbers.</param>
    private void AddPattern(string name, params int[] days) =>
        _patternRepo.Insert(new BlockPattern { Name = name, Days = days.ToList() });

    /// <summary>The menu's pattern lines as text, for easy comparison.</summary>
    private string[] PatternTexts() => _grid.DayHeaderMenu.Patterns.Select(p => p.Text).ToArray();

    /// <summary>With nothing folded the menu offers "Show only", the patterns, and no "Show all days".</summary>
    [Fact]
    public void Menu_NothingFolded_ShowOnlyAndPatterns_NoShowAll()
    {
        Open(_fall);
        AddPattern("MWF", 1, 3, 5);
        Assert.True(_grid.DayHeaderMenu.Prepare(2, "Tuesday"));
        Assert.Equal("Show only Tuesday", _grid.DayHeaderMenu.ShowOnlyText);
        Assert.True(_grid.DayHeaderMenu.ShowOnlyVisible);
        Assert.False(_grid.DayHeaderMenu.ShowAllVisible);
        Assert.Equal(new[] { "Show MWF" }, PatternTexts());
    }

    /// <summary>"Show only" is absent when the day is the only open day; "Show all days" appears once something is folded.</summary>
    [Fact]
    public void Menu_OnlyOpenDay_HidesShowOnly_ShowAllAppears()
    {
        Open(_fall);
        _grid.ShowOnlyDay(2);
        _grid.DayHeaderMenu.Prepare(2, "Tuesday");
        Assert.False(_grid.DayHeaderMenu.ShowOnlyVisible);
        Assert.True(_grid.DayHeaderMenu.ShowAllVisible);
        // A folded day can still be made the only one.
        _grid.DayHeaderMenu.Prepare(4, "Thursday");
        Assert.True(_grid.DayHeaderMenu.ShowOnlyVisible);
    }

    /// <summary>Patterns none of whose days are shown (Saturday off) are skipped; a partly shown one stays.</summary>
    [Fact]
    public void Menu_PatternsFilteredToShownDays()
    {
        Open(_fall);   // Saturday and Sunday are off in this fixture
        AddPattern("Sat", 6);
        AddPattern("TR", 2, 4);
        AddPattern("WeekendMon", 1, 6, 7);
        _grid.DayHeaderMenu.Prepare(1, "Monday");
        Assert.Equal(new[] { "Show TR", "Show WeekendMon" }, PatternTexts().OrderBy(t => t).ToArray());
        Assert.DoesNotContain("Show Sat", PatternTexts());
    }

    /// <summary>Patterns are read when the menu opens, so one added or renamed afterwards shows next time.</summary>
    [Fact]
    public void Menu_ReadsPatternsEachTimeItOpens()
    {
        Open(_fall);
        _grid.DayHeaderMenu.Prepare(1, "Monday");
        Assert.Empty(PatternTexts());
        AddPattern("MW", 1, 3);
        _grid.DayHeaderMenu.Prepare(1, "Monday");
        Assert.Equal(new[] { "Show MW" }, PatternTexts());
    }

    /// <summary>The lines call the fold commands and close the menu.</summary>
    [Fact]
    public void Menu_Commands_CallThroughAndClose()
    {
        Open(_fall);
        AddPattern("TR", 2, 4);

        _grid.DayHeaderMenu.Prepare(3, "Wednesday");
        _grid.DayHeaderMenu.IsOpen = true;
        _grid.DayHeaderMenu.ShowOnlyCommand.Execute(null);
        Assert.Equal(new[] { 1, 2, 4, 5 }, Folded());
        Assert.False(_grid.DayHeaderMenu.IsOpen);

        _grid.DayHeaderMenu.Prepare(1, "Monday");
        _grid.DayHeaderMenu.IsOpen = true;
        _grid.DayHeaderMenu.Patterns.Single().ChooseCommand.Execute(null);
        Assert.Equal(new[] { 1, 3, 5 }, Folded());
        Assert.False(_grid.DayHeaderMenu.IsOpen);

        _grid.DayHeaderMenu.Prepare(1, "Monday");
        _grid.DayHeaderMenu.IsOpen = true;
        _grid.DayHeaderMenu.ShowAllCommand.Execute(null);
        Assert.Empty(_grid.FoldedDays);
        Assert.False(_grid.DayHeaderMenu.IsOpen);
    }

    /// <summary>A pattern with a blank name is not listed (the line would read "Show ").</summary>
    [Fact]
    public void Menu_BlankPatternNameSkipped()
    {
        Open(_fall);
        AddPattern("  ", 1, 3);
        _grid.DayHeaderMenu.Prepare(1, "Monday");
        Assert.Empty(PatternTexts());
    }
}

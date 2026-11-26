using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Access;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Check all courses (<see cref="AccessCheckViewModel"/>) and the Access tab's pages and notices
/// (<see cref="AccessTabViewModel"/>): before the setup, results, problem and quiet lines, out of date, the setup
/// answered No; the list, detail and check pages and how they hand over to each other; the notices collection.
/// Real repositories over a temp database; the write lock is a switch.
/// <para>
/// Seed (Fall, one semester): the section types Lecture and Laboratory. Course A: a lecture and a lab on Monday 9:00
/// (the lab can't be taken with the only lecture). Course B: lecture B1 on Monday, labs BL1 on Tuesday and BL2 on
/// Monday (BL2 is stranded). Course D: a lecture only (its lab is missing this term). Course S: a lecture only,
/// listing no other component. Tag "W" (watched) is on all of B's sections and on S1.
/// </para>
/// </summary>
public sealed class AccessCheckAndTabPagesTests : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SectionStore _store = new();
    private readonly GridChangeNotifier _notifier = new();
    private readonly SectionRepository _sectionRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly SemesterContext _semesters = new();
    private readonly AccessState _state;
    private readonly AccessSetupService _setup;
    private readonly AccessTabViewModel _tab;
    private readonly AccessPicture _picture;
    private readonly SchedulingEnvironmentValue _lecture = new() { Name = "Lecture", SortOrder = 0 };
    private readonly SchedulingEnvironmentValue _lab = new() { Name = "Laboratory", SortOrder = 1 };
    private readonly SchedulingEnvironmentValue _tag = new() { Name = "W", IsAccessWatch = true };
    private readonly string _fall;
    private readonly string _yearId;
    private bool _canWrite = true;

    public AccessCheckAndTabPagesTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        var courseRepo = new CourseRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var semesterRepo = new SemesterRepository(db);

        foreach (var code in new[] { "A", "B", "D", "S" })
            courseRepo.Insert(new Course { Id = "c-" + code, CalendarCode = code, Title = code });
        var year = new AcademicYear { Name = "2025-2026" };
        ayRepo.Insert(year);
        _yearId = year.Id;
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        semesterRepo.Insert(fall);
        _fall = fall.Id;
        _envRepo.Insert(SchedulingEnvironmentTypes.SectionType, _lecture);
        _envRepo.Insert(SchedulingEnvironmentTypes.SectionType, _lab);
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, _tag);

        Add("A", "A1", _lecture, 1); Add("A", "AL1", _lab, 1);
        Add("B", "B1", _lecture, 1, tagged: true); Add("B", "BL1", _lab, 2, tagged: true); Add("B", "BL2", _lab, 1, tagged: true);
        Add("D", "D1", _lecture, 3);
        Add("S", "S1", _lecture, 4, tagged: true);

        _state = AccessStateTestSupport.For(db, _store, _notifier);
        var toggler = new AccessWatchToggler(_envRepo, _state, _notifier, () => _canWrite);
        _setup = new AccessSetupService(db, _envRepo, courseRepo, _sectionRepo, new AccessChecksSetupRepository(db),
            _state, _notifier, () => _canWrite);
        _tab = AccessStateTestSupport.TabFor(db, _state, _semesters, _store, toggler, _setup, out _picture);
        _semesters.Reload(ayRepo, semesterRepo, year.Id, new HashSet<string> { _fall });
        _store.Reload(_sectionRepo, [_fall]);
    }

    private Section Add(string course, string code, SchedulingEnvironmentValue? type, int day, bool tagged = false)
    {
        var s = new Section
        {
            CourseId = "c-" + course, SemesterId = _fall, SectionCode = code, SectionTypeId = type?.Id,
            TagIds = tagged ? [_tag.Id] : [],
            Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }],
        };
        _sectionRepo.Insert(s);
        return s;
    }

    private Section Get(string code) => _sectionRepo.GetAll(_fall).Single(s => s.SectionCode == code);

    private void DoSetup() =>
        _setup.SaveSetup(new HashSet<string> { _lecture.Id, _lab.Id },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["c-A"] = [_lecture.Id, _lab.Id], ["c-B"] = [_lecture.Id, _lab.Id],
                ["c-D"] = [_lecture.Id, _lab.Id], ["c-S"] = [_lecture.Id],
            });

    /// <summary>Releases the database and the settings guard.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Check all courses ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BeforeTheSetup_Editing_TheButtonOpensTheWizard_ThenRunsWhenItHasSaved()
    {
        Action? waiting = null;
        int requests = 0;
        _setup.SetupWizardRequested += then => { requests++; waiting = then; };
        int shown = 0;
        _tab.Check.ShowRequested += () => shown++;

        _tab.CheckAllCoursesCommand.Execute(null);

        Assert.Equal(1, requests);
        Assert.NotNull(waiting);
        Assert.Equal(0, shown); // nothing shown until the wizard has saved
        Assert.False(_tab.IsCheckPage);

        DoSetup();
        waiting!();
        Assert.True(_tab.IsCheckPage);
        Assert.Single(_tab.Check.Semesters);
        Assert.Equal(1, shown);
    }

    [Fact]
    public void BeforeTheSetup_ReadOnly_ThereIsAQuietComment_AndNoCheck()
    {
        _canWrite = false;
        _tab.CheckAllCoursesCommand.Execute(null);

        Assert.True(_tab.IsCheckPage);
        Assert.Equal(
            "Access watches aren't set up for this database yet. Someone who can edit it needs to set them up before courses can be checked.",
            _tab.Check.Notice);
        Assert.True(_tab.Check.NoticeIsQuiet);
        Assert.Empty(_tab.Check.Semesters);
        Assert.False(_tab.Check.CanCheckAgain);
    }

    [Fact]
    public void AfterTheSetup_ProblemsAndQuietFindingsAreListed()
    {
        DoSetup();
        _tab.CheckAllCoursesCommand.Execute(null);

        var check = _tab.Check;
        Assert.Null(check.Notice);
        Assert.True(check.CanCheckAgain);
        var block = Assert.Single(check.Semesters);
        Assert.False(block.ShowName);
        Assert.Equal("2 courses need a look:", block.Headline);
        Assert.True(block.HeadlineIsAttention);
        Assert.Equal(2, block.Problems.Count);
        Assert.StartsWith("A can't be taken: ", block.Problems[0].Text);
        Assert.Equal("Show A on the grid", block.Problems[0].Tip);
        Assert.Equal("B BL2 is stranded: it conflicts with B B1, which every slate uses. No student taking B can use it.",
            block.Problems[1].Text);
        Assert.Equal("D has no Laboratory sections this term, but Laboratory is one of its components.",
            Assert.Single(block.Quiet).Text);
        Assert.Equal("Open in Courses", block.Quiet[0].ActionText);
        Assert.Equal("Checked each of the 3 courses with more than one component on its own (4 courses offered this term).",
            block.Footer);
        Assert.False(check.IsOutOfDate);
    }

    [Fact]
    public void ProblemLine_OpensTheCourseAlone_AndBackReturnsToTheCheck()
    {
        DoSetup();
        _tab.CheckAllCoursesCommand.Execute(null);
        _tab.Check.Semesters[0].Problems[1].OpenCommand.Execute(null); // B

        Assert.True(_picture.IsCourseOnly);
        Assert.Equal("c-B", _picture.CourseId);
        Assert.True(_tab.IsDetailPage);
        Assert.True(_tab.Detail.IsCheckMode);

        _tab.Detail.BackCommand.Execute(null);
        Assert.False(_picture.IsOpen);
        Assert.True(_tab.IsCheckPage);

        _tab.Check.BackCommand.Execute(null);
        Assert.True(_tab.IsListPage);
    }

    [Fact]
    public void Escape_BacksOutOnePage_AndIsLeftToOthersOnTheList()
    {
        DoSetup();
        Assert.False(_tab.EscapeCommand.CanExecute(null));   // the list: Escape goes on to an open flyout

        _tab.CheckAllCoursesCommand.Execute(null);
        _tab.Check.Semesters[0].Problems[1].OpenCommand.Execute(null); // B, alone
        Assert.True(_tab.IsDetailPage);

        Assert.True(_tab.EscapeCommand.CanExecute(null));
        _tab.EscapeCommand.Execute(null);
        Assert.False(_picture.IsOpen);
        Assert.True(_tab.IsCheckPage);

        _tab.EscapeCommand.Execute(null);
        Assert.True(_tab.IsListPage);
        Assert.False(_tab.GoBack());
    }

    [Fact]
    public void QuietLines_OpenInCourses_OrShowTheSection()
    {
        Add("B", "BX", null, 3); // a lab-less, typeless section of B
        _store.Reload(_sectionRepo, [_fall]);
        DoSetup();
        string? opened = null;
        _tab.OpenCoursesRequested += id => opened = id;
        _tab.CheckAllCoursesCommand.Execute(null);

        var quiet = _tab.Check.Semesters[0].Quiet;
        var courseLine = quiet.Single(q => q.ActionText == "Open in Courses");
        courseLine.ActionCommand.Execute(null);
        Assert.Equal("c-D", opened);

        var sectionLine = quiet.Single(q => q.ActionText == "Show");
        Assert.Equal("B BX has no section type, so it isn't counted.", sectionLine.Text);
        sectionLine.ActionCommand.Execute(null);
        Assert.Contains(Get("BX").Id, _store.SelectedSectionIds);
    }

    [Fact]
    public void AnyChange_MarksTheResultsOutOfDate_AndCheckAgainFreshensThem()
    {
        DoSetup();
        _tab.CheckAllCoursesCommand.Execute(null);
        Assert.False(_tab.Check.IsOutOfDate);

        _state.Invalidate();
        Assert.True(_tab.Check.IsOutOfDate);
        Assert.Equal("These results are from before the last change. Click Check again to update them.", _tab.Check.OutOfDateText);
        Assert.Equal(2, _tab.Check.Semesters[0].Problems.Count); // the old results stay

        _tab.Check.RunCommand.Execute(null);
        Assert.False(_tab.Check.IsOutOfDate);
    }

    [Fact]
    public void WhenTheSetupSaidNoCourseHasComponents_ThereIsNothingToCheck()
    {
        _setup.SaveNoComponents();
        _tab.CheckAllCoursesCommand.Execute(null);

        Assert.Equal("Nothing to check: when access watches were set up, each course was said to take just one section.", _tab.Check.Notice);
        Assert.Equal("To change that, mark a section type as a component under Scheduling Environment → Section Type.", _tab.Check.FooterLead);
        Assert.Empty(_tab.Check.Semesters);
        Assert.True(_tab.Check.CanCheckAgain);
    }

    [Fact]
    public void SeveralSemesters_AreCheckedSeparately_EachUnderItsName()
    {
        var winter = new Semester { AcademicYearId = _yearId, Name = "Winter", SortOrder = 1 };
        new SemesterRepository(_fx.Db).Insert(winter);
        // Winter: course A's lecture and lab are apart, so A is fine there
        foreach (var (code, type, day) in new[] { ("A1", _lecture, 1), ("AL1", _lab, 2) })
            _sectionRepo.Insert(new Section
            {
                CourseId = "c-A", SemesterId = winter.Id, SectionCode = code, SectionTypeId = type.Id,
                Schedule = [new SectionDaySchedule { Day = day, StartMinutes = 540, DurationMinutes = 60 }],
            });
        _semesters.Reload(new AcademicYearRepository(_fx.Db), new SemesterRepository(_fx.Db), _yearId,
            new HashSet<string> { _fall, winter.Id });
        _store.Reload(_sectionRepo, [_fall, winter.Id]);
        DoSetup();

        _tab.CheckAllCoursesCommand.Execute(null);

        Assert.Equal(2, _tab.Check.Semesters.Count);
        Assert.All(_tab.Check.Semesters, s => Assert.True(s.ShowName));
        var fall = _tab.Check.Semesters.Single(s => s.SemesterName == "Fall");
        var win = _tab.Check.Semesters.Single(s => s.SemesterName == "Winter");
        Assert.Equal("2 courses need a look:", fall.Headline);
        Assert.Equal("No problems in 1 course with more than one component.", win.Headline);
        Assert.False(win.HeadlineIsAttention);
    }

    // ── The tab's pages and notices ──────────────────────────────────────────────────────────────

    [Fact]
    public void OpeningARow_OpensTheDetail_AndBackReturnsToTheList()
    {
        Assert.True(_tab.IsListPage);
        var row = Assert.Single(_tab.Rows);
        Assert.Equal(_tag.Id, row.TagId);
        Assert.Equal(_fall, row.SemesterId);

        _tab.OpenWatchCommand.Execute(row);
        Assert.True(_tab.IsDetailPage);
        Assert.False(_tab.IsListPage);
        Assert.True(_picture.IsOpen);
        Assert.Equal("W", _tab.Detail.Name);

        _tab.Detail.BackCommand.Execute(null);
        Assert.True(_tab.IsListPage);
        Assert.False(_picture.IsOpen);
    }

    [Fact]
    public void TheLegendBarsClose_AlsoReturnsToTheList()
    {
        _tab.OpenWatchCommand.Execute(_tab.Rows[0]);
        _picture.Close();
        Assert.True(_tab.IsListPage);
    }

    [Fact]
    public void WhenTheOpenWatchGoes_TheTabReturnsToTheList()
    {
        _tab.OpenWatchCommand.Execute(_tab.Rows[0]);
        _tag.IsAccessWatch = false;
        _envRepo.Update(_tag);
        _state.Invalidate();
        Assert.True(_tab.IsListPage);
    }

    [Fact]
    public void ASave_MakesANotice_WithOpenAndDismiss_AndLightsTheDot()
    {
        DoSetup();
        _ = _state.Current;
        Assert.Empty(_tab.Notices);
        Assert.False(_tab.NeedsALook); // a stranded section is fragile, not attention

        // S 1 is W's lone section of S; B's sections are W's too. Moving BL1 onto Monday leaves no lab that fits B 1.
        var bl1 = Get("BL1");
        bl1.Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 540, DurationMinutes = 60 }];
        _sectionRepo.Update(bl1);
        _store.Reload(_sectionRepo, [_fall]);

        var notice = Assert.Single(_tab.Notices);
        Assert.True(notice.IsBad);
        Assert.StartsWith("The conflict between B BL1 and B B1 means W has no full slate. Open W for more detail.", notice.Text);
        Assert.True(_tab.HasNotices);
        Assert.True(_tab.NeedsALook);

        notice.OpenCommand.Execute(null);
        Assert.True(_tab.IsDetailPage);
        Assert.Equal("W", _tab.Detail.Name);
        _tab.Detail.BackCommand.Execute(null);

        notice.DismissCommand.Execute(null);
        Assert.Empty(_tab.Notices);
        Assert.False(_tab.HasNotices);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="SubjectColorPresenter"/> (colour by subject, <c>designs/subject-colours.md</c> task 3) over a
/// real temp database with real repositories: "colours on?" for each display mode against focus All / None / one
/// subject / two subjects and across several semesters; <see cref="SubjectColorPresenter.ColorFor"/>; when
/// <see cref="SubjectColorPresenter.Changed"/> fires (and when it stays quiet); and the Section list's cards
/// refreshing their chips in place.
/// <para>
/// World: subjects BIOL and CHEM, one course each, one academic year with three semesters: Fall holds a BIOL and a
/// CHEM section, Winter only BIOL, Summer only CHEM. The focus service runs in demo mode (null key, starts at All,
/// never saves); the colour store runs over its own <see cref="AppSettings"/> instance.
/// </para>
/// </summary>
public sealed class SubjectColorPresenterTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Protects settings.json: the colour store and the semester context save it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database with the full schema.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SubjectRepository _subjectRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    /// <summary>The settings the colour store reads and writes (never <see cref="AppSettings.Current"/>).</summary>
    private readonly AppSettings _settings = new();

    private readonly SubjectColorStore _colors;
    private readonly FocusService _focus;
    private readonly SubjectColorPresenter _presenter;

    private readonly Subject _biol = new() { Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Subject _chem = new() { Name = "Chemistry", CalendarAbbreviation = "CHEM" };
    private readonly Course _biolCourse;
    private readonly Course _chemCourse;

    private readonly string _yearId;
    private readonly string _fall;
    private readonly string _winter;
    private readonly string _summer;

    private readonly Section _fallBiol;
    private readonly Section _fallChem;

    private const string BiolColor = "#B7E79D";
    private const string ChemColor = "#EBB4E6";

    /// <summary>Lists created by <see cref="MakeList"/>, disposed with the fixture.</summary>
    private readonly List<SectionListViewModel> _lists = new();

    /// <summary>Seeds the world and builds the store, focus service and presenter, viewing Fall.</summary>
    public SubjectColorPresenterTests()
    {
        var db = _fx.Db;
        _subjectRepo = new SubjectRepository(db);
        _courseRepo = new CourseRepository(db);
        _sectionRepo = new SectionRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);

        _subjectRepo.Insert(_biol);
        _subjectRepo.Insert(_chem);
        _biolCourse = new Course { SubjectId = _biol.Id, CalendarCode = "BIOL105", Title = "Bio" };
        _chemCourse = new Course { SubjectId = _chem.Id, CalendarCode = "CHEM110", Title = "Chem" };
        _courseRepo.Insert(_biolCourse);
        _courseRepo.Insert(_chemCourse);

        var year = new AcademicYear { Name = "2025-2026" };
        _ayRepo.Insert(year);
        _yearId = year.Id;
        _fall = AddSemester("Fall", 0);
        _winter = AddSemester("Winter", 1);
        _summer = AddSemester("Summer", 2);

        _fallBiol = AddSection(_biolCourse, _fall, "AB1");
        _fallChem = AddSection(_chemCourse, _fall, "A");
        AddSection(_biolCourse, _winter, "AB1");
        AddSection(_chemCourse, _summer, "A");

        _colors = new SubjectColorStore([BiolColor, ChemColor], () => _settings);
        _focus = new FocusService(
            _subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo, _semesterContext, _sectionStore, null);
        _presenter = new SubjectColorPresenter(_colors, _focus, _sectionStore, _courseRepo, _subjectRepo);

        View(_fall);
    }

    /// <summary>Disposes the lists, presenter, focus service and database, and restores the settings file.</summary>
    public void Dispose()
    {
        foreach (var list in _lists) list.Dispose();
        _presenter.Dispose();
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Inserts a semester of the year and returns its id.</summary>
    private string AddSemester(string name, int sortOrder)
    {
        var sem = new Semester { AcademicYearId = _yearId, Name = name, SortOrder = sortOrder };
        _semesterRepo.Insert(sem);
        return sem.Id;
    }

    /// <summary>Inserts a section of the course in the semester and returns it.</summary>
    private Section AddSection(Course course, string semesterId, string code)
    {
        var s = new Section
        {
            CourseId = course.Id,
            SemesterId = semesterId,
            SectionCode = code,
            Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 90 }],
        };
        _sectionRepo.Insert(s);
        return s;
    }

    /// <summary>Reloads the section store with the given semesters, as a semester change does.</summary>
    private void View(params string[] semesterIds) => _sectionStore.Reload(_sectionRepo, semesterIds);

    /// <summary>Gives BIOL its colour (CHEM stays without one) and sets the display mode.</summary>
    private void ColourBiol(SubjectColorDisplay display)
    {
        _colors.Display = display;
        _colors.SetColor("BIOL", BiolColor);
    }

    /// <summary>The focus named by a test case: "All", "None", "One" (BIOL) or "Two" (BIOL and CHEM).</summary>
    private SubjectFocus FocusFor(string name) => name switch
    {
        "All" => SubjectFocus.All,
        "None" => SubjectFocus.None,
        "One" => SubjectFocus.ForSubjects([_biol.Id]),
        "Two" => SubjectFocus.ForSubjects([_biol.Id, _chem.Id]),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>Counts <see cref="SubjectColorPresenter.Changed"/> from now on.</summary>
    private Func<int> CountChanges()
    {
        var count = 0;
        _presenter.Changed += () => count++;
        return () => count;
    }

    /// <summary>
    /// Builds a Section list wired as dependency injection wires it (same store, focus and presenter), viewing Fall.
    /// </summary>
    private SectionListViewModel MakeList()
    {
        var db = _fx.Db;
        var list = new SectionListViewModel(
            _sectionRepo, _courseRepo, _subjectRepo, new InstructorRepository(db), new RoomRepository(db),
            new LegalStartTimeRepository(db), _semesterRepo, new BlockPatternRepository(db),
            new SectionCodePatternRepository(db), _semesterContext, _sectionStore,
            new SchedulingEnvironmentRepository(db), new CampusRepository(db), new MeetingRepository(db),
            new NullDialog(), _fx.Lock, _focus, _presenter);
        _lists.Add(list);
        _semesterContext.Reload(_ayRepo, _semesterRepo, _yearId, new HashSet<string> { _fall });
        return list;
    }

    /// <summary>The list's card for a section.</summary>
    private static SectionListItemViewModel Card(SectionListViewModel list, Section section) =>
        list.SectionItems.OfType<SectionListItemViewModel>().Single(c => c.Section.Id == section.Id);

    /// <summary>A dialog service that declines everything (no dialog is expected).</summary>
    private sealed class NullDialog : IDialogService
    {
        public System.Threading.Tasks.Task<bool> Confirm(string message, string confirmLabel = "Delete") =>
            System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task ShowError(string message) => System.Threading.Tasks.Task.CompletedTask;
    }

    // ── Colours on? ───────────────────────────────────────────────────────────

    /// <summary>Rule 1: with no colours set, colours are off in every display mode and every focus.</summary>
    [Theory]
    [InlineData(SubjectColorDisplay.OnlyWhenSeveral, "All")]
    [InlineData(SubjectColorDisplay.OnlyWhenSeveral, "Two")]
    [InlineData(SubjectColorDisplay.Always, "All")]
    [InlineData(SubjectColorDisplay.Always, "One")]
    [InlineData(SubjectColorDisplay.Never, "All")]
    public void NoColours_AlwaysOff(SubjectColorDisplay display, string focus)
    {
        _colors.Display = display;
        _focus.SetFocus(FocusFor(focus));

        Assert.False(_presenter.IsOn);
        Assert.Equal("", _presenter.ColorFor(_fallBiol));
    }

    /// <summary>Each display mode against each focus, viewing Fall (one BIOL and one CHEM section).</summary>
    [Theory]
    [InlineData(SubjectColorDisplay.Always, "All", true)]
    [InlineData(SubjectColorDisplay.Always, "None", true)]
    [InlineData(SubjectColorDisplay.Always, "One", true)]
    [InlineData(SubjectColorDisplay.Always, "Two", true)]
    [InlineData(SubjectColorDisplay.Never, "All", false)]
    [InlineData(SubjectColorDisplay.Never, "None", false)]
    [InlineData(SubjectColorDisplay.Never, "One", false)]
    [InlineData(SubjectColorDisplay.Never, "Two", false)]
    [InlineData(SubjectColorDisplay.OnlyWhenSeveral, "All", true)]
    [InlineData(SubjectColorDisplay.OnlyWhenSeveral, "None", false)]
    [InlineData(SubjectColorDisplay.OnlyWhenSeveral, "One", false)]
    [InlineData(SubjectColorDisplay.OnlyWhenSeveral, "Two", true)]
    public void DisplayMode_ByFocus(SubjectColorDisplay display, string focus, bool expectedOn)
    {
        ColourBiol(display);
        _focus.SetFocus(FocusFor(focus));

        Assert.Equal(expectedOn, _presenter.IsOn);
    }

    /// <summary>
    /// OnlyWhenSeveral counts subjects across every open semester: Winter (BIOL) plus Summer (CHEM) is two subjects,
    /// though each semester alone has one.
    /// </summary>
    [Fact]
    public void OnlyWhenSeveral_CountsAcrossOpenSemesters()
    {
        ColourBiol(SubjectColorDisplay.OnlyWhenSeveral);

        View(_winter);
        Assert.False(_presenter.IsOn);

        View(_winter, _summer);
        Assert.True(_presenter.IsOn);

        View(_summer);
        Assert.False(_presenter.IsOn);
    }

    /// <summary>A colour set only for an abbreviation this database doesn't have counts as "no colours".</summary>
    [Fact]
    public void ColourForAnotherDatabasesSubject_IsOff()
    {
        _colors.Display = SubjectColorDisplay.Always;
        _colors.SetColor("NURS", BiolColor);

        Assert.False(_presenter.IsOn);
    }

    // ── ColorFor ──────────────────────────────────────────────────────────────

    /// <summary>When on, a section gets its subject's colour; a subject without one gives empty.</summary>
    [Fact]
    public void ColorFor_On_SubjectColourOrEmpty()
    {
        ColourBiol(SubjectColorDisplay.Always);

        Assert.Equal(BiolColor, _presenter.ColorFor(_fallBiol));
        Assert.Equal("", _presenter.ColorFor(_fallChem));
    }

    /// <summary>When off (Never, or OnlyWhenSeveral with one subject in focus), every section's colour is empty.</summary>
    [Fact]
    public void ColorFor_Off_IsEmpty()
    {
        ColourBiol(SubjectColorDisplay.Never);
        Assert.Equal("", _presenter.ColorFor(_fallBiol));

        _colors.Display = SubjectColorDisplay.OnlyWhenSeveral;
        _focus.SetFocus(FocusFor("One"));
        Assert.Equal("", _presenter.ColorFor(_fallBiol));
    }

    /// <summary>A section with no course, or an unknown course, has no colour.</summary>
    [Fact]
    public void ColorFor_NoOrUnknownCourse_IsEmpty()
    {
        ColourBiol(SubjectColorDisplay.Always);

        Assert.Equal("", _presenter.ColorFor(new Section { CourseId = null, SectionCode = "X" }));
        Assert.Equal("", _presenter.ColorFor(new Section { CourseId = "no-such-course", SectionCode = "X" }));
    }

    // ── Changed ───────────────────────────────────────────────────────────────

    /// <summary>A colour change while colours are on raises Changed once, and the new colour is read at once.</summary>
    [Fact]
    public void Changed_OnStoreChange_WhileOn()
    {
        ColourBiol(SubjectColorDisplay.Always);
        Assert.True(_presenter.IsOn);
        var changes = CountChanges();

        _colors.SetColor("CHEM", ChemColor);

        Assert.Equal(1, changes());
        Assert.Equal(ChemColor, _presenter.ColorFor(_fallChem));
    }

    /// <summary>Setting the first colour turns colours on and raises Changed.</summary>
    [Fact]
    public void Changed_OnFirstColour()
    {
        _colors.Display = SubjectColorDisplay.Always;
        Assert.False(_presenter.IsOn);
        var changes = CountChanges();

        _colors.SetColor("BIOL", BiolColor);

        Assert.Equal(1, changes());
        Assert.True(_presenter.IsOn);
    }

    /// <summary>A store change while colours stay off (Never) raises nothing: nothing on screen can differ.</summary>
    [Fact]
    public void NoChanged_OnStoreChange_WhileOffStaysOff()
    {
        ColourBiol(SubjectColorDisplay.Never);
        Assert.False(_presenter.IsOn);
        var changes = CountChanges();

        _colors.SetColor("CHEM", ChemColor);

        Assert.Equal(0, changes());
    }

    /// <summary>A focus change that turns colours off (two subjects → one, OnlyWhenSeveral) raises Changed.</summary>
    [Fact]
    public void Changed_OnFocusChange_ThatFlips()
    {
        ColourBiol(SubjectColorDisplay.OnlyWhenSeveral);
        Assert.True(_presenter.IsOn);
        var changes = CountChanges();

        _focus.SetFocus(FocusFor("One"));

        Assert.Equal(1, changes());
        Assert.False(_presenter.IsOn);
    }

    /// <summary>A focus change under Always leaves colours on and every colour the same, so nothing is raised.</summary>
    [Fact]
    public void NoChanged_OnFocusChange_ThatDoesNotFlip()
    {
        ColourBiol(SubjectColorDisplay.Always);
        Assert.True(_presenter.IsOn);
        var changes = CountChanges();

        _focus.SetFocus(FocusFor("One"));

        Assert.Equal(0, changes());
    }

    /// <summary>A section reload that turns colours off (Fall → Winter only, OnlyWhenSeveral) raises Changed.</summary>
    [Fact]
    public void Changed_OnSectionReload_ThatFlips()
    {
        ColourBiol(SubjectColorDisplay.OnlyWhenSeveral);
        Assert.True(_presenter.IsOn);
        var changes = CountChanges();

        View(_winter);

        Assert.Equal(1, changes());
        Assert.False(_presenter.IsOn);
    }

    /// <summary>A reload with the same courses and subjects (an ordinary section save) raises nothing.</summary>
    [Fact]
    public void NoChanged_OnSectionReload_NothingRelevant()
    {
        ColourBiol(SubjectColorDisplay.Always);
        Assert.Equal(BiolColor, _presenter.ColorFor(_fallBiol));
        var changes = CountChanges();

        _fallBiol.SectionCode = "AB2";
        _sectionRepo.Update(_fallBiol);
        View(_fall);

        Assert.Equal(0, changes());
    }

    /// <summary>
    /// A reload after a course moved to another subject (colours on) raises Changed, and the course's sections now
    /// take the new subject's colour.
    /// </summary>
    [Fact]
    public void Changed_OnSectionReload_AfterCourseChangedSubject()
    {
        ColourBiol(SubjectColorDisplay.Always);
        Assert.Equal("", _presenter.ColorFor(_fallChem));
        var changes = CountChanges();

        _chemCourse.SubjectId = _biol.Id;
        _courseRepo.Update(_chemCourse);
        View(_fall);

        Assert.Equal(1, changes());
        Assert.Equal(BiolColor, _presenter.ColorFor(_fallChem));
    }

    /// <summary>After Dispose the presenter no longer listens, so it raises nothing.</summary>
    [Fact]
    public void Dispose_StopsListening()
    {
        ColourBiol(SubjectColorDisplay.Always);
        Assert.True(_presenter.IsOn);
        var changes = CountChanges();

        _presenter.Dispose();
        _colors.SetColor("CHEM", ChemColor);

        Assert.Equal(0, changes());
    }

    // ── Section list cards ────────────────────────────────────────────────────

    /// <summary>With no colours set, every card's chip colour is empty (headings look as in 1.2.3).</summary>
    [Fact]
    public void Cards_NoColours_ChipEmpty()
    {
        var list = MakeList();

        Assert.All(list.SectionItems.OfType<SectionListItemViewModel>(),
            c => Assert.Equal("", c.SubjectChipColor));
    }

    /// <summary>Cards built while colours are on carry their subject's colour from construction.</summary>
    [Fact]
    public void Cards_BuiltWhileOn_CarryColour()
    {
        ColourBiol(SubjectColorDisplay.Always);

        var list = MakeList();

        Assert.Equal(BiolColor, Card(list, _fallBiol).SubjectChipColor);
        Assert.Equal("", Card(list, _fallChem).SubjectChipColor);
    }

    /// <summary>
    /// A colour change refreshes the existing cards in place: the same card objects, with new chip colours.
    /// </summary>
    [Fact]
    public void Cards_RefreshInPlace_OnChanged()
    {
        ColourBiol(SubjectColorDisplay.Always);
        var list = MakeList();
        var biolCard = Card(list, _fallBiol);
        var chemCard = Card(list, _fallChem);

        _colors.SetColor("CHEM", ChemColor);
        Assert.Same(chemCard, Card(list, _fallChem));
        Assert.Equal(ChemColor, chemCard.SubjectChipColor);

        _colors.Display = SubjectColorDisplay.Never;
        Assert.Same(biolCard, Card(list, _fallBiol));
        Assert.Equal("", biolCard.SubjectChipColor);
        Assert.Equal("", chemCard.SubjectChipColor);
    }

    /// <summary>A disposed list no longer refreshes its cards.</summary>
    [Fact]
    public void Cards_DisposedList_StopsRefreshing()
    {
        ColourBiol(SubjectColorDisplay.Always);
        var list = MakeList();
        var chemCard = Card(list, _fallChem);

        list.Dispose();
        _colors.SetColor("CHEM", ChemColor);

        Assert.Equal("", chemCard.SubjectChipColor);
    }
}

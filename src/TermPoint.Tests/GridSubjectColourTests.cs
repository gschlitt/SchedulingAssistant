using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Colour by subject, task 4 (<c>designs/subject-colours.md</c>): the Schedule view's section entries carry their
/// subject marker colour (<see cref="TileEntry.SubjectColor"/>, from <see cref="SectionMeetingBlock.SubjectColor"/>)
/// when colours are on, and none when they are off, from each block builder (Pass 1, and both branches of the
/// overlay pass); the grid reloads when the colours change; and renaming a subject's abbreviation in the Courses
/// flyout re-colours the grid straight away. View-model level only, no rendering.
/// <para>
/// World: one Fall semester; BIOL 101 A on Monday in room Main 101, taught by Baker; CHEM 101 A on Tuesday with no
/// room or instructor. Real <see cref="ScheduleGridViewModel"/>, <see cref="FocusService"/> and
/// <see cref="SubjectColorPresenter"/> over a temp database, built in dependency-injection order (focus, presenter,
/// grid). The colour store runs over its own <see cref="AppSettings"/>, never the real settings.
/// </para>
/// </summary>
public sealed class GridSubjectColourTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json (SemesterContext saves the selection).</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    /// <summary>The settings the colour store reads and writes.</summary>
    private readonly AppSettings _settings = new();

    private readonly SubjectColorStore _colors;
    private readonly FocusService _focus;
    private readonly SubjectColorPresenter _presenter;
    private readonly ScheduleGridViewModel _grid;

    private readonly Subject _biol = new() { Id = "subj-biol", Name = "Biology", CalendarAbbreviation = "BIOL" };
    private readonly Subject _chem = new() { Id = "subj-chem", Name = "Chemistry", CalendarAbbreviation = "CHEM" };

    private readonly string _biolSectionId;
    private readonly string _chemSectionId;
    private readonly string _bakerId;
    private readonly string _roomId;

    private const string BiolColor = "#B7E79D";
    private const string ChemColor = "#EBB4E6";

    /// <summary>Seeds the world and builds the focus service, presenter and grid, viewing Fall.</summary>
    public GridSubjectColourTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        var roomRepo = new RoomRepository(db);

        _subjectRepo.Insert(_biol);
        _subjectRepo.Insert(_chem);
        var biolCourse = new Course { Id = "c-biol", SubjectId = _biol.Id, CalendarCode = "BIOL101", Title = "Bio" };
        var chemCourse = new Course { Id = "c-chem", SubjectId = _chem.Id, CalendarCode = "CHEM101", Title = "Chem" };
        _courseRepo.Insert(biolCourse);
        _courseRepo.Insert(chemCourse);

        var room = new Room { Building = "Main", RoomNumber = "101" };
        roomRepo.Insert(room);
        _roomId = room.Id;
        var baker = new Instructor { LastName = "Baker", FirstName = "Pat", Initials = "PB" };
        _instructorRepo.Insert(baker);
        _bakerId = baker.Id;

        var year = new AcademicYear { Name = "2025-2026" };
        _ayRepo.Insert(year);
        var fall = new Semester { AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        _semesterRepo.Insert(fall);

        var biolSection = new Section
        {
            CourseId = biolCourse.Id, SemesterId = fall.Id, SectionCode = "A",
            InstructorAssignments = [new InstructorAssignment { InstructorId = baker.Id }],
            Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 510, DurationMinutes = 90, RoomId = room.Id }],
        };
        var chemSection = new Section
        {
            CourseId = chemCourse.Id, SemesterId = fall.Id, SectionCode = "A",
            Schedule = [new SectionDaySchedule { Day = 2, StartMinutes = 510, DurationMinutes = 90 }],
        };
        _sectionRepo.Insert(biolSection);
        _sectionRepo.Insert(chemSection);
        _biolSectionId = biolSection.Id;
        _chemSectionId = chemSection.Id;

        _colors = new SubjectColorStore([BiolColor, ChemColor], () => _settings);

        // Dependency-injection order: the focus and the presenter subscribe to SectionsChanged before the grid.
        _focus = new FocusService(
            _subjectRepo, _courseRepo, _sectionRepo, _semesterRepo, _ayRepo, _semesterContext, _sectionStore, null);
        _presenter = new SubjectColorPresenter(_colors, _focus, _sectionStore, _courseRepo, _subjectRepo);

        var notifier = new GridChangeNotifier();
        _grid = new ScheduleGridViewModel(
            _sectionRepo, _courseRepo, _instructorRepo, roomRepo, _subjectRepo, _envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), notifier, new InstructorCommitmentRepository(db),
            _fx.Lock, new SharedScheduleService(),
            new AccessPanelViewModel(new ProgramWatchRepository(db), _semesterContext, notifier, _envRepo, _courseRepo,
                () => true),
            _focus, _presenter, new BlockPatternRepository(db));

        // Open the Fall semester and load its sections, as the section list does in the app.
        _semesterContext.Reload(_ayRepo, _semesterRepo, year.Id, new HashSet<string> { fall.Id });
        _sectionStore.Reload(_sectionRepo, [fall.Id]);
    }

    /// <summary>Releases the presenter, the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _presenter.Dispose();
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Every section entry on the grid (commitments, meetings and shared schedules left out).</summary>
    private List<TileEntry> SectionEntries() =>
        _grid.GridData.DayColumns.SelectMany(c => c.Tiles).SelectMany(t => t.Entries)
            .Where(e => !e.IsCommitment && !string.IsNullOrEmpty(e.SectionId)).ToList();

    /// <summary>The single grid entry of a section.</summary>
    private TileEntry EntryFor(string sectionId) => Assert.Single(SectionEntries(), e => e.SectionId == sectionId);

    /// <summary>Ticks the Chemistry subject filter, so Biology leaves Pass 1 (it can come back only as an overlay).</summary>
    private void FilterToChemistry() =>
        _grid.Filter.Subjects.Single(i => i.Id == _chem.Id).IsSelected = true;

    /// <summary>A dialog that is never expected to be shown.</summary>
    private sealed class NullDialog : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(false);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    // ── Colours off ────────────────────────────────────────────────────────────

    /// <summary>With no colours set, no entry carries a colour (the grid draws exactly as 1.2.3).</summary>
    [Fact]
    public void NoColoursSet_NoEntryHasAColour()
    {
        _colors.Display = SubjectColorDisplay.Always;

        var entries = SectionEntries();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(string.Empty, e.SubjectColor));
    }

    /// <summary>Display "Never" turns colours off even though a subject has one.</summary>
    [Fact]
    public void DisplayNever_NoEntryHasAColour()
    {
        _colors.SetColor("BIOL", BiolColor);
        _colors.Display = SubjectColorDisplay.Never;

        Assert.All(SectionEntries(), e => Assert.Equal(string.Empty, e.SubjectColor));
    }

    /// <summary>
    /// "Only when more than one subject is shown" with the focus on one subject: colours are off, and the overlay
    /// pass (which ignores the focus) adds no colour either.
    /// </summary>
    [Fact]
    public void OnlyWhenSeveral_OneSubjectInFocus_NoColours_EvenOnOverlay()
    {
        _colors.SetColor("BIOL", BiolColor);
        _colors.Display = SubjectColorDisplay.OnlyWhenSeveral;
        _focus.SetFocus(SubjectFocus.ForSubjects([_chem.Id]));
        // Brings Biology back through the overlay pass. A room overlay, because the focus narrows the instructor list
        // (Baker teaches only Biology) but not the room list.
        _grid.Filter.SetRoomOverlay(_roomId);

        Assert.True(EntryFor(_biolSectionId).IsOverlay);
        Assert.All(SectionEntries(), e => Assert.Equal(string.Empty, e.SubjectColor));
    }

    // ── Colours on: each block builder ─────────────────────────────────────────

    /// <summary>Pass 1 (the filtered pass): a coloured subject's entries carry it; an uncoloured subject's don't.</summary>
    [Fact]
    public void FilteredPass_CarriesTheSubjectColour()
    {
        _colors.SetColor("BIOL", BiolColor);
        _colors.Display = SubjectColorDisplay.Always;

        Assert.False(EntryFor(_biolSectionId).IsOverlay);
        Assert.Equal(BiolColor, EntryFor(_biolSectionId).SubjectColor, ignoreCase: true);
        Assert.Equal(string.Empty, EntryFor(_chemSectionId).SubjectColor);
    }

    /// <summary>The overlay pass, section-level branch (instructor overlay): the overlay-only entry carries its colour.</summary>
    [Fact]
    public void InstructorOverlayPass_CarriesTheSubjectColour()
    {
        _colors.SetColor("BIOL", BiolColor);
        _colors.SetColor("CHEM", ChemColor);
        _colors.Display = SubjectColorDisplay.Always;
        FilterToChemistry();
        Assert.DoesNotContain(SectionEntries(), e => e.SectionId == _biolSectionId);

        _grid.Filter.SetInstructorOverlay(_bakerId);

        var biol = EntryFor(_biolSectionId);
        Assert.True(biol.IsOverlay);
        Assert.Equal(BiolColor, biol.SubjectColor, ignoreCase: true);
        Assert.Equal(ChemColor, EntryFor(_chemSectionId).SubjectColor, ignoreCase: true);
    }

    /// <summary>The overlay pass, room branch: the overlay-only entry carries its colour.</summary>
    [Fact]
    public void RoomOverlayPass_CarriesTheSubjectColour()
    {
        _colors.SetColor("BIOL", BiolColor);
        _colors.Display = SubjectColorDisplay.Always;
        FilterToChemistry();
        Assert.DoesNotContain(SectionEntries(), e => e.SectionId == _biolSectionId);

        _grid.Filter.SetRoomOverlay(_roomId);

        var biol = EntryFor(_biolSectionId);
        Assert.True(biol.IsOverlay);
        Assert.Equal(BiolColor, biol.SubjectColor, ignoreCase: true);
    }

    /// <summary>The static builders stamp nothing when no colour function is passed (the existing pipeline tests' path).</summary>
    [Fact]
    public void Builders_WithoutAColourFunction_StampNothing()
    {
        _colors.SetColor("BIOL", BiolColor);
        _colors.Display = SubjectColorDisplay.Always;

        Assert.Equal(BiolColor, EntryFor(_biolSectionId).SubjectColor, ignoreCase: true);   // the grid's own path

        var block = new SectionMeetingBlock(1, 510, 600, false, "BIOL101 A", "PB", _biolSectionId);
        Assert.Equal(string.Empty, block.SubjectColor);
    }

    // ── Reloads ────────────────────────────────────────────────────────────────

    /// <summary>Setting, changing and clearing a colour reloads the grid each time (P5-5).</summary>
    [Fact]
    public void ColourChanges_ReloadTheGrid()
    {
        _colors.Display = SubjectColorDisplay.Always;
        Assert.Equal(string.Empty, EntryFor(_chemSectionId).SubjectColor);

        _colors.SetColor("CHEM", ChemColor);
        Assert.Equal(ChemColor, EntryFor(_chemSectionId).SubjectColor, ignoreCase: true);

        _colors.SetColor("CHEM", BiolColor);
        Assert.Equal(BiolColor, EntryFor(_chemSectionId).SubjectColor, ignoreCase: true);

        _colors.ClearColors(["CHEM"]);
        Assert.Equal(string.Empty, EntryFor(_chemSectionId).SubjectColor);
    }

    /// <summary>
    /// Renaming CHEM to CHMX in the Courses flyout, where CHMX already has a colour and CHEM has none (so no colour is
    /// copied and the store raises nothing), re-colours the grid at once, with no section reload.
    /// </summary>
    [Fact]
    public async Task SubjectRename_RecoloursTheGridAtOnce()
    {
        _colors.Display = SubjectColorDisplay.Always;
        _colors.SetColor("CHMX", ChemColor);   // not a subject yet, so colours are still off
        Assert.Equal(string.Empty, EntryFor(_chemSectionId).SubjectColor);

        var courses = new CourseListViewModel(_courseRepo, _subjectRepo, _envRepo, new NullDialog(), _sectionRepo,
            _semesterRepo, _ayRepo, _instructorRepo, _fx.Lock, _sectionStore, _colors, _presenter);
        courses.SelectedSubject = courses.Subjects.Single(s => s.Id == _chem.Id);
        courses.EditSubjectCommand.Execute(null);
        courses.SubjectEditVm!.CalendarAbbreviation = "CHMX";
        await courses.SubjectEditVm.SaveCommand.ExecuteAsync(null);

        Assert.Null(courses.SubjectEditVm);   // saved
        Assert.Equal(ChemColor, EntryFor(_chemSectionId).SubjectColor, ignoreCase: true);
    }
}

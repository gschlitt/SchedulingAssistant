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
/// Marked sections in Schedule View: the <see cref="SectionMarks"/> set, the right-click menu's mark commands (and
/// that they work in read-only mode and while a section card is open, since marking saves nothing), and that marks
/// survive grid reloads.
/// </summary>
public sealed class SectionMarksTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json (reloading the semester context saves it).</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer.</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;
    private readonly FocusService _focus;

    /// <summary>Opens the temp database and the services the grid view model needs.</summary>
    public SectionMarksTests()
    {
        _sectionRepo = new SectionRepository(_fx.Db);
        _focus = new FocusService(
            new SubjectRepository(_fx.Db), new CourseRepository(_fx.Db), _sectionRepo, new SemesterRepository(_fx.Db),
            new AcademicYearRepository(_fx.Db), _semesterContext, _sectionStore, null);
    }

    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();

    /// <summary>Releases the focus service, the database and the settings guard.</summary>
    public void Dispose()
    {
        _focus.Dispose();
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // -- SectionMarks -----------------------------------------------------------

    /// <summary>Toggle marks an unmarked id and unmarks a marked one; several can be marked at once.</summary>
    [Fact]
    public void Toggle_MarksAndUnmarks()
    {
        var marks = new SectionMarks();
        Assert.False(marks.HasAny);

        marks.Toggle("a");
        marks.Toggle("b");
        Assert.True(marks.IsMarked("a"));
        Assert.True(marks.IsMarked("b"));
        Assert.True(marks.HasAny);
        Assert.Equal(new[] { "a", "b" }, marks.Ids.OrderBy(i => i));

        marks.Toggle("a");
        Assert.False(marks.IsMarked("a"));
        Assert.True(marks.IsMarked("b"));
    }

    /// <summary>Clear unmarks everything.</summary>
    [Fact]
    public void Clear_RemovesAll()
    {
        var marks = new SectionMarks();
        marks.Toggle("a");
        marks.Toggle("b");

        marks.Clear();

        Assert.False(marks.HasAny);
        Assert.Empty(marks.Ids);
    }

    /// <summary>Changed fires for every real change and never for Clear on an empty set.</summary>
    [Fact]
    public void Changed_RaisedOnlyOnRealChange()
    {
        var marks = new SectionMarks();
        var raised = 0;
        marks.Changed += () => raised++;

        marks.Clear();
        Assert.Equal(0, raised);

        marks.Toggle("a");
        Assert.Equal(1, raised);
        marks.Toggle("a");
        Assert.Equal(2, raised);

        marks.Toggle("a");
        marks.Clear();
        Assert.Equal(4, raised);
        marks.Clear();
        Assert.Equal(4, raised);
    }

    // -- Context menu -----------------------------------------------------------

    /// <summary>Stores a one-meeting section and returns it as the grid would load it.</summary>
    private Section StoreSection(string code = "A")
    {
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = code,
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
        };
        _sectionRepo.Insert(section);
        return _sectionRepo.GetById(section.Id)!;
    }

    /// <summary>Builds a menu over <paramref name="marks"/> and loads it for <paramref name="section"/>.</summary>
    private SectionContextMenuViewModel LoadMenu(
        Section section, SectionMarks marks, WriteLockService? lockService = null, FakeSectionCardState? state = null)
    {
        var menu = new SectionContextMenuViewModel(
            _sectionRepo, () => { }, lockService ?? _fx.Lock, state ?? new FakeSectionCardState(), marks);
        menu.Load(section, 1, 510, new List<Instructor>(), new List<Room>(), new List<SchedulingEnvironmentValue>());
        return menu;
    }

    /// <summary>Load names the button for the section's own state and reports whether any mark exists.</summary>
    [Fact]
    public void Menu_Load_SetsTextAndHasAnyMarks()
    {
        var marks = new SectionMarks();
        var a = StoreSection("A");
        var b = StoreSection("B");

        var menu = LoadMenu(a, marks);
        Assert.Equal("Mark Section", menu.MarkItemText);
        Assert.False(menu.HasAnyMarks);

        marks.Toggle(a.Id);
        menu.Load(a, 1, 510, [], [], []);
        Assert.Equal("Unmark Section", menu.MarkItemText);
        Assert.True(menu.HasAnyMarks);

        menu.Load(b, 1, 510, [], [], []);   // another section is marked, this one is not
        Assert.Equal("Mark Section", menu.MarkItemText);
        Assert.True(menu.HasAnyMarks);
    }

    /// <summary>ToggleMark marks, then unmarks, and closes the menu each time.</summary>
    [Fact]
    public void Menu_ToggleMark_TogglesAndCloses()
    {
        var marks = new SectionMarks();
        var section = StoreSection();
        var menu = LoadMenu(section, marks);

        menu.IsOpen = true;
        menu.ToggleMarkCommand.Execute(null);
        Assert.True(marks.IsMarked(section.Id));
        Assert.False(menu.IsOpen);
        Assert.Equal("Unmark Section", menu.MarkItemText);
        Assert.True(menu.HasAnyMarks);

        menu.IsOpen = true;
        menu.ToggleMarkCommand.Execute(null);
        Assert.False(marks.IsMarked(section.Id));
        Assert.False(menu.IsOpen);
        Assert.Equal("Mark Section", menu.MarkItemText);
        Assert.False(menu.HasAnyMarks);
    }

    /// <summary>ClearMarks clears every mark (not only this section's) and closes the menu.</summary>
    [Fact]
    public void Menu_ClearMarks_ClearsAllAndCloses()
    {
        var marks = new SectionMarks();
        var a = StoreSection("A");
        var b = StoreSection("B");
        marks.Toggle(a.Id);
        marks.Toggle(b.Id);
        var menu = LoadMenu(a, marks);

        menu.IsOpen = true;
        menu.ClearMarksCommand.Execute(null);

        Assert.False(marks.HasAny);
        Assert.False(menu.IsOpen);
        Assert.False(menu.HasAnyMarks);
    }

    /// <summary>Marking is not an edit: it works while a section card is open (which refuses edits).</summary>
    [Fact]
    public void Menu_ToggleMark_WorksWhileACardIsOpen()
    {
        var marks = new SectionMarks();
        var section = StoreSection();
        var menu = LoadMenu(section, marks, state: new FakeSectionCardState { Heading = "HIST101 A" });
        Assert.NotNull(menu.RefusalMessage);

        Assert.True(menu.ToggleMarkCommand.CanExecute(null));
        menu.ToggleMarkCommand.Execute(null);
        Assert.True(marks.IsMarked(section.Id));

        Assert.True(menu.ClearMarksCommand.CanExecute(null));
        menu.ClearMarksCommand.Execute(null);
        Assert.False(marks.HasAny);
    }

    /// <summary>Marking is not an edit: it works in read-only mode (no write lock), where Confirm and the edit items are refused.</summary>
    [Fact]
    public void Menu_ToggleMark_WorksInReadOnlyMode()
    {
        var marks = new SectionMarks();
        var section = StoreSection();
        var reader = new WriteLockService();   // TryAcquire never called: IsWriter is false
        var menu = LoadMenu(section, marks, reader);
        Assert.False(menu.ConfirmCommand.CanExecute(null));
        Assert.False(menu.ShowInstructorsCommand.CanExecute(null));
        Assert.False(menu.ShowRoomCommand.CanExecute(null));
        Assert.False(menu.ShowTagsCommand.CanExecute(null));
        Assert.False(menu.ShowFlagCommand.CanExecute(null));

        Assert.True(menu.ToggleMarkCommand.CanExecute(null));
        menu.ToggleMarkCommand.Execute(null);
        Assert.True(marks.IsMarked(section.Id));
    }

    // -- Grid view model --------------------------------------------------------

    /// <summary>Builds the real grid view model over the temp database, as the dependency container does.</summary>
    private ScheduleGridViewModel CreateGrid()
    {
        var db = _fx.Db;
        var envRepo = new SchedulingEnvironmentRepository(db);
        var notifier = new GridChangeNotifier();
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        return new ScheduleGridViewModel(
            _sectionRepo, courseRepo, new InstructorRepository(db), new RoomRepository(db), subjectRepo, envRepo,
            new CampusRepository(db), _semesterContext, new AcademicUnitService(new AcademicUnitRepository(db)),
            _sectionStore, new MeetingStore(), new MeetingRepository(db), notifier,
            new InstructorCommitmentRepository(db), _fx.Lock, new SharedScheduleService(),
            new AccessPanelViewModel(new ProgramWatchRepository(db), _semesterContext, notifier, envRepo, courseRepo, () => true),
            _focus, FocusTestSupport.NoColours(courseRepo, subjectRepo, _sectionStore), new BlockPatternRepository(db),
            new FakeSectionCardState());
    }

    /// <summary>Marks follow the section id, so a grid reload leaves them in place; the menu shares the same set.</summary>
    [Fact]
    public void Grid_Reload_KeepsMarks()
    {
        var grid = CreateGrid();
        var section = StoreSection();
        grid.Marks.Toggle(section.Id);
        var changed = 0;
        grid.Marks.Changed += () => changed++;

        grid.Reload();
        grid.Reload();

        Assert.True(grid.Marks.IsMarked(section.Id));
        Assert.Equal(0, changed);

        grid.ContextMenu.Load(section, 1, 510, [], [], []);
        Assert.Equal("Unmark Section", grid.ContextMenu.MarkItemText);
        grid.ContextMenu.ToggleMarkCommand.Execute(null);
        Assert.False(grid.Marks.IsMarked(section.Id));
    }
}

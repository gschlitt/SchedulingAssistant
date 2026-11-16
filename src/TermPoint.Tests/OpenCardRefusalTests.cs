using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Saves that would reload the section store are refused while a section card is open in the Section List
/// (the reload collapses the card and discards what the user typed): the wording
/// (<see cref="OpenCardMessages"/>), the Schedule View right-click menu, the Course editor's "Apply tags to all
/// sections", and the Section List's own <see cref="ISectionCardState"/>.
/// </summary>
public sealed class OpenCardRefusalTests : IDisposable
{
    /// <summary>Protects the developer's real settings.json.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer (Confirm needs it).</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;

    /// <summary>Opens the temp database and its section repository.</summary>
    public OpenCardRefusalTests()
    {
        _sectionRepo = new SectionRepository(_fx.Db);
    }

    /// <summary>Releases the temp database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // -- The approved wording ---------------------------------------------------

    /// <summary>The approved strings, pinned exactly.</summary>
    [Fact]
    public void Messages_AreTheApprovedWording()
    {
        Assert.Equal("Finish editing HIST101 A in the Section List first.", OpenCardMessages.FinishFirst("HIST101 A"));
        Assert.Equal(
            "Finish editing a new section in the Section List first. Apply or Cancel it there, then drag again.",
            OpenCardMessages.FinishFirstToDrag("a new section"));
    }

    /// <summary>WhyRefused is the FinishFirst text when a card is open, otherwise null.</summary>
    [Fact]
    public void WhyRefused_FollowsTheCardState()
    {
        var state = new FakeSectionCardState();
        Assert.Null(OpenCardMessages.WhyRefused(state));
        state.Heading = "BIOL110 B";
        Assert.Equal(OpenCardMessages.FinishFirst("BIOL110 B"), OpenCardMessages.WhyRefused(state));
    }

    // -- Right-click menu -------------------------------------------------------

    /// <summary>Stores a one-meeting section and returns it as the grid would load it.</summary>
    private Section StoreSection()
    {
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }],
        };
        _sectionRepo.Insert(section);
        return _sectionRepo.GetById(section.Id)!;
    }

    /// <summary>Builds a menu over <paramref name="state"/>, counting saves, and loads it for <paramref name="section"/>.</summary>
    private SectionContextMenuViewModel LoadMenu(Section section, FakeSectionCardState state, Action onSaved)
    {
        var menu = new SectionContextMenuViewModel(_sectionRepo, onSaved, _fx.Lock, state);
        menu.Load(section, 1, 510, new List<Instructor>(), new List<Room>(), new List<SchedulingEnvironmentValue>());
        return menu;
    }

    /// <summary>A card open at Load: the message is set and the four Show buttons are disabled.</summary>
    [Fact]
    public void Menu_CardOpenAtLoad_DisablesTheShowCommands()
    {
        var menu = LoadMenu(StoreSection(), new FakeSectionCardState { Heading = "HIST101 A" }, () => { });

        Assert.Equal(OpenCardMessages.FinishFirst("HIST101 A"), menu.RefusalMessage);
        Assert.False(menu.ShowInstructorsCommand.CanExecute(null));
        Assert.False(menu.ShowRoomCommand.CanExecute(null));
        Assert.False(menu.ShowTagsCommand.CanExecute(null));
        Assert.False(menu.ShowFlagCommand.CanExecute(null));
    }

    /// <summary>No card open: no message, the Show buttons work.</summary>
    [Fact]
    public void Menu_NoCard_ShowCommandsEnabled()
    {
        var menu = LoadMenu(StoreSection(), new FakeSectionCardState(), () => { });

        Assert.Null(menu.RefusalMessage);
        Assert.True(menu.ShowInstructorsCommand.CanExecute(null));
        Assert.True(menu.ShowRoomCommand.CanExecute(null));
        Assert.True(menu.ShowTagsCommand.CanExecute(null));
        Assert.True(menu.ShowFlagCommand.CanExecute(null));
    }

    /// <summary>A card opened after Load: Confirm saves nothing, reloads nothing, and shows the message.</summary>
    [Fact]
    public void Menu_CardOpenedAfterLoad_ConfirmRefuses()
    {
        var state = new FakeSectionCardState();
        var saved = 0;
        var section = StoreSection();
        var menu = LoadMenu(section, state, () => saved++);
        menu.ShowFlagCommand.Execute(null);
        menu.SelectedFlag = menu.Flags.First(f => f.Value != section.Flag);

        state.Heading = "HIST101 A";
        menu.ConfirmCommand.Execute(null);

        Assert.Equal(0, saved);
        Assert.Equal(section.Flag, _sectionRepo.GetById(section.Id)!.Flag);
        Assert.Equal(OpenCardMessages.FinishFirst("HIST101 A"), menu.RefusalMessage);
        Assert.Equal(TileSubPanel.None, menu.ActiveSubPanel);
    }

    /// <summary>No card open: Confirm saves and reloads as before.</summary>
    [Fact]
    public void Menu_NoCard_ConfirmSaves()
    {
        var saved = 0;
        var section = StoreSection();
        var menu = LoadMenu(section, new FakeSectionCardState(), () => saved++);
        menu.ShowFlagCommand.Execute(null);
        var chosen = menu.Flags.First(f => f.Value != section.Flag);
        menu.SelectedFlag = chosen;

        menu.ConfirmCommand.Execute(null);

        Assert.Equal(1, saved);
        Assert.Equal(chosen.Value, _sectionRepo.GetById(section.Id)!.Flag);
        Assert.Null(menu.RefusalMessage);
    }

    // -- Course editor ----------------------------------------------------------

    /// <summary>Builds an editor for an existing course with an apply-tags callback that counts its calls.</summary>
    private static CourseEditViewModel NewCourseEditor(Func<string?>? refused, Action onApply) =>
        new(new Course { Id = "c-1", CalendarCode = "HIST101" }, isNew: false,
            allTags: new List<SchedulingEnvironmentValue>(),
            onSave: _ => Task.CompletedTask, onCancel: () => { }, codeExists: _ => false,
            subjects: new System.Collections.ObjectModel.ObservableCollection<Subject>(),
            applyTagsToAll: _ => { onApply(); return Task.FromResult(3); },
            whyApplyTagsRefused: refused);

    /// <summary>A refusal: the callback is not called and the refusal is the result text.</summary>
    [Fact]
    public async Task CourseEditor_Refused_DoesNotApply()
    {
        var calls = 0;
        var vm = NewCourseEditor(() => OpenCardMessages.FinishFirst("HIST101 A"), () => calls++);

        await vm.ApplyTagsToAllSectionsCommand.ExecuteAsync(null);

        Assert.Equal(0, calls);
        Assert.Equal(OpenCardMessages.FinishFirst("HIST101 A"), vm.ApplyTagsResult);
    }

    /// <summary>No refusal: the callback runs as before.</summary>
    [Fact]
    public async Task CourseEditor_NotRefused_Applies()
    {
        var calls = 0;
        var vm = NewCourseEditor(() => null, () => calls++);

        await vm.ApplyTagsToAllSectionsCommand.ExecuteAsync(null);

        Assert.Equal(1, calls);
        Assert.Equal("Tags applied to 3 sections.", vm.ApplyTagsResult);
    }

    // -- Section List -----------------------------------------------------------

    /// <summary>A dialog service that never shows anything.</summary>
    private sealed class NullDialog : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(false);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>With no editor open, the Section List reports no open card.</summary>
    [Fact]
    public void SectionList_NoEditorOpen_OpenCardHeadingIsNull()
    {
        var db = _fx.Db;
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        var semesterContext = new SemesterContext();
        var sectionStore = new SectionStore();
        var list = new SectionListViewModel(_sectionRepo, courseRepo, subjectRepo, new InstructorRepository(db),
            new RoomRepository(db), new LegalStartTimeRepository(db), new SemesterRepository(db),
            new BlockPatternRepository(db), new SectionCodePatternRepository(db), semesterContext, sectionStore,
            new SchedulingEnvironmentRepository(db), new CampusRepository(db), new MeetingRepository(db),
            new NullDialog(), _fx.Lock,
            FocusTestSupport.AllFocus(semesterContext, sectionStore),
            FocusTestSupport.NoColours(courseRepo, subjectRepo, sectionStore));

        ISectionCardState state = list;
        Assert.Null(state.OpenCardHeading);
    }
}

using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Task 2 of colour-by-subject: the colour controls of the Courses flyout (<see cref="CourseListViewModel"/> and
/// <see cref="SubjectRowViewModel"/>). Colours are personal settings, so they must work for a read-only user.
/// Each test gives the view model its own <see cref="AppSettings"/> through the store, so the real settings are
/// never touched by colour changes.
/// </summary>
public sealed class CourseListSubjectColourTests : IDisposable
{
    private static readonly string[] Palette = { "#B7E79D", "#EBB4E6", "#F8C58C", "#F2A89A" };

    private readonly ExtensionDataDbFixture _fx = new();
    private readonly SettingsFileGuard _settingsGuard = new();
    private readonly AppSettings _settings = new();
    private readonly SubjectColorStore _store;
    private readonly RecordingDialog _dialog = new();
    private readonly SubjectRepository _subjectRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly InstructorRepository _instructorRepo;

    /// <summary>Builds the repositories and a store over a private settings object.</summary>
    public CourseListSubjectColourTests()
    {
        _store = new SubjectColorStore(Palette, () => _settings);
        _subjectRepo = new SubjectRepository(_fx.Db);
        _courseRepo = new CourseRepository(_fx.Db);
        _sectionRepo = new SectionRepository(_fx.Db);
        _envRepo = new SchedulingEnvironmentRepository(_fx.Db);
        _semesterRepo = new SemesterRepository(_fx.Db);
        _ayRepo = new AcademicYearRepository(_fx.Db);
        _instructorRepo = new InstructorRepository(_fx.Db);
    }

    /// <summary>Releases the temp database and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    /// <summary>A dialog that records the last Confirm question and answers with a preset value.</summary>
    private sealed class RecordingDialog : IDialogService
    {
        /// <summary>The answer Confirm returns.</summary>
        public bool Answer { get; set; }
        /// <summary>The last question asked, or null.</summary>
        public string? Message { get; private set; }
        /// <summary>The label of the confirm button last asked, or null.</summary>
        public string? Label { get; private set; }

        public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        {
            Message = message;
            Label = confirmLabel;
            return Task.FromResult(Answer);
        }

        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>Inserts a subject into the temp database.</summary>
    private void AddSubject(string id, string name, string abbreviation) =>
        _subjectRepo.Insert(new Subject { Id = id, Name = name, CalendarAbbreviation = abbreviation });

    /// <summary>Builds the view model; <paramref name="lockService"/> defaults to the fixture's writer lock.</summary>
    private CourseListViewModel NewVm(WriteLockService? lockService = null) =>
        new(_courseRepo, _subjectRepo, _envRepo, _dialog, _sectionRepo, _semesterRepo, _ayRepo,
            _instructorRepo, lockService ?? _fx.Lock, new SectionStore(), _store,
            FocusTestSupport.NoColours(_courseRepo, _subjectRepo));

    /// <summary>A lock service that never acquired the lock, so IsWriter is false (read-only user).</summary>
    private static WriteLockService ReadOnlyLock() => new();

    /// <summary>The preview chip text is the abbreviation plus a sample section code; no abbreviation gives just the sample.</summary>
    [Fact]
    public void PreviewText_UsesAbbreviation_AndFallsBackWithoutOne()
    {
        Assert.Equal("BIOL 105 AB1", new SubjectRowViewModel(new Subject { CalendarAbbreviation = "BIOL" }, _store).PreviewText);
        Assert.Equal("105 AB1", new SubjectRowViewModel(new Subject { CalendarAbbreviation = "" }, _store).PreviewText);
    }

    /// <summary>Every colour command and the display choice work for a read-only user.</summary>
    [Fact]
    public async Task ColourCommands_WorkWhileReadOnly()
    {
        AddSubject("1", "Physics", "PHYS");
        AddSubject("2", "Biology", "BIOL");
        var ro = ReadOnlyLock();
        Assert.False(ro.IsWriter);
        var vm = NewVm(ro);
        Assert.False(vm.AddSubjectCommand.CanExecute(null)); // sanity: database edits ARE gated

        Assert.True(vm.SuggestColoursCommand.CanExecute(null));
        vm.SuggestColoursCommand.Execute(null);
        Assert.NotNull(_store.GetColor("PHYS"));
        Assert.NotNull(_store.GetColor("BIOL"));

        var row = vm.SubjectRows.First(r => r.Subject.CalendarAbbreviation == "PHYS");
        row.ColorHex = "#112233";
        row.CommitCommand.Execute(null);
        Assert.Equal("#112233", _store.GetColor("PHYS"));

        row.ClearColorCommand.Execute(null);
        Assert.Null(_store.GetColor("PHYS"));

        vm.SelectedDisplay = SubjectColorDisplay.Never;
        Assert.Equal(SubjectColorDisplay.Never, _store.Display);

        _dialog.Answer = true;
        Assert.True(vm.ClearColoursCommand.CanExecute(null));
        await vm.ClearColoursCommand.ExecuteAsync(null);
        Assert.Null(_store.GetColor("BIOL"));
    }

    /// <summary>Suggest colours fills blanks only; a colour that exists is left alone.</summary>
    [Fact]
    public void Suggest_FillsBlanksOnly()
    {
        AddSubject("1", "Physics", "PHYS");
        AddSubject("2", "Biology", "BIOL");
        _store.SetColor("PHYS", "#010203");
        var vm = NewVm();

        vm.SuggestColoursCommand.Execute(null);

        Assert.Equal("#010203", _store.GetColor("PHYS"));
        Assert.NotNull(_store.GetColor("BIOL"));
        Assert.False(vm.CanSuggest);
        Assert.False(vm.SuggestColoursCommand.CanExecute(null));
    }

    /// <summary>Clear asks first; No removes nothing; Yes removes only this database's subjects.</summary>
    [Fact]
    public async Task Clear_AsksFirst_AndClearsOnlyThisDatabase()
    {
        AddSubject("1", "Physics", "PHYS");
        AddSubject("2", "Biology", "BIOL");
        _store.SetColor("PHYS", "#010203");
        _store.SetColor("BIOL", "#040506");
        _store.SetColor("CHEM", "#070809"); // not in this database
        var vm = NewVm();

        _dialog.Answer = false;
        await vm.ClearColoursCommand.ExecuteAsync(null);
        Assert.Equal("Remove the colours of all 2 subjects in this database? You can't undo this.", _dialog.Message);
        Assert.Equal("Remove colours", _dialog.Label);
        Assert.NotNull(_store.GetColor("PHYS"));
        Assert.NotNull(_store.GetColor("BIOL"));

        _dialog.Answer = true;
        await vm.ClearColoursCommand.ExecuteAsync(null);
        Assert.Null(_store.GetColor("PHYS"));
        Assert.Null(_store.GetColor("BIOL"));
        Assert.Equal("#070809", _store.GetColor("CHEM"));
        Assert.All(vm.SubjectRows, r => Assert.Equal("", r.ColorHex));
    }

    /// <summary>The question uses the singular for one subject.</summary>
    [Fact]
    public async Task Clear_SingularWording()
    {
        AddSubject("1", "Physics", "PHYS");
        _store.SetColor("PHYS", "#010203");
        var vm = NewVm();
        _dialog.Answer = false;
        await vm.ClearColoursCommand.ExecuteAsync(null);
        Assert.Equal("Remove the colour of 1 subject in this database? You can't undo this.", _dialog.Message);
    }

    /// <summary>Renaming PHYS to PHYX through the subject editor copies the colour to the new abbreviation.</summary>
    [Fact]
    public async Task Rename_CopiesColour()
    {
        AddSubject("1", "Physics", "PHYS");
        _store.SetColor("PHYS", "#010203");
        var vm = NewVm();

        vm.SelectedSubject = vm.Subjects.Single();
        vm.EditSubjectCommand.Execute(null);
        vm.SubjectEditVm!.CalendarAbbreviation = "PHYX";
        await vm.SubjectEditVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("#010203", _store.GetColor("PHYX"));
        Assert.Equal("#010203", _store.GetColor("PHYS"));
        Assert.Equal("#010203", vm.SubjectRows.Single().ColorHex);
    }

    /// <summary>The display choice appears only once a colour exists, and goes away after Clear.</summary>
    [Fact]
    public async Task DisplayChoice_AppearsWithFirstColour_GoesAfterClear()
    {
        AddSubject("1", "Physics", "PHYS");
        var vm = NewVm();
        Assert.False(vm.HasAnyColour);
        Assert.False(vm.ClearColoursCommand.CanExecute(null));

        var seen = new List<string?>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        vm.SubjectRows.Single().ColorHex = "#010203";
        vm.SubjectRows.Single().CommitCommand.Execute(null);
        Assert.True(vm.HasAnyColour);
        Assert.Contains(nameof(CourseListViewModel.HasAnyColour), seen);
        Assert.True(vm.ClearColoursCommand.CanExecute(null));

        _dialog.Answer = true;
        await vm.ClearColoursCommand.ExecuteAsync(null);
        Assert.False(vm.HasAnyColour);
    }

    /// <summary>SubjectRows mirrors Subjects, in order, after load and after add and delete.</summary>
    [Fact]
    public async Task SubjectRows_MirrorSubjects()
    {
        AddSubject("1", "Biology", "BIOL");
        AddSubject("2", "Physics", "PHYS");
        var vm = NewVm();
        AssertMirrored(vm);

        vm.AddSubjectCommand.Execute(null);
        vm.SubjectEditVm!.Name = "Chemistry";
        vm.SubjectEditVm.CalendarAbbreviation = "CHEM";
        await vm.SubjectEditVm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.SubjectRows.Count);
        AssertMirrored(vm);

        vm.SelectedSubject = vm.Subjects.First(s => s.CalendarAbbreviation == "PHYS");
        vm.DeleteSubjectCommand.Execute(null);
        Assert.Equal(2, vm.SubjectRows.Count);
        AssertMirrored(vm);
    }

    /// <summary>Asserts the rows hold the same subjects as Subjects, in the same order.</summary>
    private static void AssertMirrored(CourseListViewModel vm) =>
        Assert.Equal(vm.Subjects.ToList(), vm.SubjectRows.Select(r => r.Subject).ToList());

    /// <summary>Selecting a row selects its subject, and selecting a subject selects its row.</summary>
    [Fact]
    public void Selection_SyncsBothWays()
    {
        AddSubject("1", "Biology", "BIOL");
        AddSubject("2", "Physics", "PHYS");
        var vm = NewVm();

        vm.SelectedSubjectRow = vm.SubjectRows[1];
        Assert.Same(vm.Subjects[1], vm.SelectedSubject);

        vm.SelectedSubject = vm.Subjects[0];
        Assert.Same(vm.SubjectRows[0], vm.SelectedSubjectRow);
    }

    /// <summary>Two subjects with the same colour (any case) raise the shared-colour note.</summary>
    [Fact]
    public void HasSharedColours_WhenTwoSubjectsShareAColour()
    {
        AddSubject("1", "Biology", "BIOL");
        AddSubject("2", "Physics", "PHYS");
        var vm = NewVm();
        _store.SetColor("BIOL", "#aabbcc");
        Assert.False(vm.HasSharedColours);

        _store.SetColor("PHYS", "#AABBCC");
        Assert.True(vm.HasSharedColours);
    }

    /// <summary>A row has a colour once one is chosen, any colour (dark ones too); dragging does not save.</summary>
    [Fact]
    public void Row_HasColor_AndDraggingDoesNotSave()
    {
        AddSubject("1", "Biology", "BIOL");
        var row = NewVm().SubjectRows.Single();
        Assert.False(row.HasColor);

        row.ColorHex = "#000011";
        Assert.True(row.HasColor);
        Assert.Null(_store.GetColor("BIOL")); // not saved until the flyout closes (Commit)
    }
}

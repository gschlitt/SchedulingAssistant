using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="WatchCreationViewModel"/> — the inline creation form for program watches.
/// </summary>
public sealed class WatchCreationViewModelTests
{
    private readonly StubEnvRepo _envRepo = new();
    private readonly StubCourseRepo _courseRepo = new();
    private ProgramWatch? _savedWatch;
    private bool _cancelCalled;

    private WatchCreationViewModel CreateVm() => new(
        _envRepo,
        _courseRepo,
        w => _savedWatch = w,
        () => _cancelCalled = true);

    // ── Show / Hide ──────────────────────────────────────────────────────────

    [Fact]
    public void Show_MakesFormVisible_AndLoadsTags()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        var vm = CreateVm();

        vm.Show();

        Assert.True(vm.IsVisible);
        Assert.Single(vm.Tags);
        Assert.Equal("Core", vm.Tags[0].Name);
    }

    [Fact]
    public void Hide_ClearsState()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        var vm = CreateVm();
        vm.Show();

        vm.Hide();

        Assert.False(vm.IsVisible);
        Assert.Empty(vm.Tags);
        Assert.Empty(vm.Courses);
        Assert.Equal(string.Empty, vm.WatchName);
    }

    // ── Auto-name generation ─────────────────────────────────────────────────

    [Fact]
    public void TagSelection_AutoGeneratesName_WithPlusSeparator()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t2", Name = "Upper" });
        var vm = CreateVm();
        vm.Show();

        vm.Tags[0].IsSelected = true;
        vm.Tags[1].IsSelected = true;

        Assert.Equal("Core + Upper", vm.WatchName);
    }

    [Fact]
    public void CourseSelection_AutoGeneratesName_WithCommaSeparator()
    {
        _courseRepo.Courses.Add(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        _courseRepo.Courses.Add(new Course { Id = "c2", CalendarCode = "CHEM201", IsActive = true });
        var vm = CreateVm();
        vm.Show();
        vm.IsTagMode = false;

        vm.Courses[0].IsSelected = true;
        vm.Courses[1].IsSelected = true;

        Assert.Equal("BIOL101, CHEM201", vm.WatchName);
    }

    [Fact]
    public void ManualNameEdit_StopsAutoGeneration()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t2", Name = "Upper" });
        var vm = CreateVm();
        vm.Show();

        vm.Tags[0].IsSelected = true;
        Assert.Equal("Core", vm.WatchName);

        // Simulate user typing a custom name
        vm.WatchName = "My Custom Watch";

        // Further selections should not overwrite
        vm.Tags[1].IsSelected = true;
        Assert.Equal("My Custom Watch", vm.WatchName);
    }

    [Fact]
    public void SwitchingMode_ResetsManualEditFlag()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _courseRepo.Courses.Add(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        var vm = CreateVm();
        vm.Show();

        vm.Tags[0].IsSelected = true;
        vm.WatchName = "Custom";

        // Switch to course mode — should reset manual edit
        vm.IsTagMode = false;
        vm.Courses[0].IsSelected = true;

        Assert.Equal("BIOL101", vm.WatchName);
    }

    [Fact]
    public void IsCourseMode_MirrorsInverseOfIsTagMode()
    {
        var vm = CreateVm();

        // Default: tag mode on, course mode off.
        Assert.True(vm.IsTagMode);
        Assert.False(vm.IsCourseMode);

        // IsTagMode is the single source of truth; IsCourseMode follows it one-directionally.
        // (In the UI the RadioButton group drives IsTagMode from the Course radio; that
        // reverse leg was removed from the VM because it re-entered the group manager and
        // hung the Schedule-View reattach.)
        vm.IsTagMode = false;
        Assert.True(vm.IsCourseMode);

        vm.IsTagMode = true;
        Assert.False(vm.IsCourseMode);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public void Save_WithNoSelection_ShowsValidationError()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        var vm = CreateVm();
        vm.Show();

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.ValidationError);
        Assert.Null(_savedWatch);
    }

    [Fact]
    public void Save_WithNoSelection_CourseMode_ShowsCourseError()
    {
        _courseRepo.Courses.Add(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        var vm = CreateVm();
        vm.Show();
        vm.IsTagMode = false;

        vm.SaveCommand.Execute(null);

        Assert.Equal("Select at least one course.", vm.ValidationError);
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_TagBased_CreatesCorrectWatch()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t2", Name = "Upper" });
        var vm = CreateVm();
        vm.Show();

        vm.Tags[0].IsSelected = true;
        vm.Tags[1].IsSelected = true;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(_savedWatch);
        Assert.Equal("Core + Upper", _savedWatch!.Name);
        Assert.Equal(ProgramWatchMode.Tag, _savedWatch.Mode);
        Assert.True(_savedWatch.IsEnabled);
        Assert.Equal(["t1", "t2"], _savedWatch.TagIds);
        Assert.Empty(_savedWatch.CourseIds);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Save_CourseBased_CreatesCorrectWatch()
    {
        _courseRepo.Courses.Add(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        _courseRepo.Courses.Add(new Course { Id = "c2", CalendarCode = "CHEM201", IsActive = true });
        var vm = CreateVm();
        vm.Show();
        vm.IsTagMode = false;

        vm.Courses[0].IsSelected = true;
        vm.Courses[1].IsSelected = true;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(_savedWatch);
        Assert.Equal("BIOL101, CHEM201", _savedWatch!.Name);
        Assert.Equal(ProgramWatchMode.Course, _savedWatch.Mode);
        Assert.True(_savedWatch.IsEnabled);
        Assert.Empty(_savedWatch.TagIds);
        Assert.Equal(["c1", "c2"], _savedWatch.CourseIds);
    }

    [Fact]
    public void Save_WithCustomName_UsesCustomName()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        var vm = CreateVm();
        vm.Show();

        vm.Tags[0].IsSelected = true;
        vm.WatchName = "BSc Year 1";
        vm.SaveCommand.Execute(null);

        Assert.NotNull(_savedWatch);
        Assert.Equal("BSc Year 1", _savedWatch!.Name);
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_HidesFormAndInvokesCallback()
    {
        var vm = CreateVm();
        vm.Show();

        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsVisible);
        Assert.True(_cancelCalled);
    }

    // ── Edit ─────────────────────────────────────────────────────────────────

    /// <summary>Editing pre-fills mode, selections and name, and the button reads "Save".</summary>
    [Fact]
    public void ShowForEdit_PrefillsModeSelectionsAndName()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t2", Name = "Upper" });
        var vm = CreateVm();
        var watch = new ProgramWatch { Name = "My name", Mode = ProgramWatchMode.Tag, TagIds = ["t2"], LevelIds = ["300"] };

        vm.ShowForEdit(watch);

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsEditing);
        Assert.Equal("Save", vm.SaveButtonText);
        Assert.True(vm.IsTagMode);
        Assert.False(vm.Tags.Single(t => t.Id == "t1").IsSelected);
        Assert.True(vm.Tags.Single(t => t.Id == "t2").IsSelected);
        Assert.True(vm.Levels.Single(l => l.Id == "300").IsSelected);
        Assert.Equal("My name", vm.WatchName);
    }

    /// <summary>A name the user typed is kept when selections change during an edit.</summary>
    [Fact]
    public void ShowForEdit_CustomName_IsNotOverwrittenBySelectionChanges()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t2", Name = "Upper" });
        var vm = CreateVm();
        vm.ShowForEdit(new ProgramWatch { Name = "BSc Year 1", Mode = ProgramWatchMode.Tag, TagIds = ["t1"] });

        vm.Tags.Single(t => t.Id == "t2").IsSelected = true;

        Assert.Equal("BSc Year 1", vm.WatchName);
    }

    /// <summary>An auto-generated name keeps following the selections during an edit.</summary>
    [Fact]
    public void ShowForEdit_AutoName_FollowsSelectionChanges()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t2", Name = "Upper" });
        var vm = CreateVm();
        vm.ShowForEdit(new ProgramWatch { Name = "Core", Mode = ProgramWatchMode.Tag, TagIds = ["t1"] });

        vm.Tags.Single(t => t.Id == "t2").IsSelected = true;

        Assert.Equal("Core + Upper", vm.WatchName);
    }

    /// <summary>
    /// Saving an edit keeps the watch's Id, on/off state and unknown JSON fields (spec item 3), takes the new
    /// selections, and does not touch the original object.
    /// </summary>
    [Fact]
    public void ShowForEdit_Save_KeepsIdEnabledAndExtensionData()
    {
        _courseRepo.Courses.Add(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        _courseRepo.Courses.Add(new Course { Id = "c2", CalendarCode = "CHEM201", IsActive = true });
        var original = new ProgramWatch
        {
            Name = "BIOL101", Mode = ProgramWatchMode.Course, IsEnabled = false, CourseIds = ["c1"],
            ExtensionData = new() { ["future"] = System.Text.Json.JsonDocument.Parse("42").RootElement }
        };
        var vm = CreateVm();
        vm.ShowForEdit(original);

        vm.Courses.Single(c => c.Id == "c2").IsSelected = true;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(_savedWatch);
        Assert.NotSame(original, _savedWatch);
        Assert.Equal(original.Id, _savedWatch!.Id);
        Assert.False(_savedWatch.IsEnabled);
        Assert.Equal(["c1", "c2"], _savedWatch.CourseIds);
        Assert.Equal(42, _savedWatch.ExtensionData!["future"].GetInt32());
        Assert.Equal(["c1"], original.CourseIds);
        Assert.False(vm.IsVisible);
        Assert.False(vm.IsEditing);
    }

    /// <summary>An inactive course the watch still names is listed (ticked) and kept on save, not silently dropped.</summary>
    [Fact]
    public void ShowForEdit_InactiveCourse_IsListedAndKept()
    {
        _courseRepo.Courses.Add(new Course { Id = "c1", CalendarCode = "BIOL101", IsActive = true });
        _courseRepo.Courses.Add(new Course { Id = "old", CalendarCode = "BIOL099", IsActive = false });
        var vm = CreateVm();
        vm.ShowForEdit(new ProgramWatch { Name = "x", Mode = ProgramWatchMode.Course, CourseIds = ["c1", "old"] });

        Assert.True(vm.Courses.Single(c => c.Id == "old").IsSelected);
        vm.SaveCommand.Execute(null);

        Assert.Equal(["c1", "old"], _savedWatch!.CourseIds);
    }

    /// <summary>Opening the form for a new watch after an edit goes back to create mode (fresh Id, enabled).</summary>
    [Fact]
    public void Show_AfterEdit_ReturnsToCreateMode()
    {
        _envRepo.Tags.Add(new SchedulingEnvironmentValue { Id = "t1", Name = "Core" });
        var vm = CreateVm();
        var original = new ProgramWatch { Name = "Core", Mode = ProgramWatchMode.Tag, TagIds = ["t1"], IsEnabled = false };
        vm.ShowForEdit(original);
        vm.CancelCommand.Execute(null);

        vm.Show();
        Assert.Equal("Create", vm.SaveButtonText);
        vm.Tags[0].IsSelected = true;
        vm.SaveCommand.Execute(null);

        Assert.NotEqual(original.Id, _savedWatch!.Id);
        Assert.True(_savedWatch.IsEnabled);
    }

    // ── Stubs ────────────────────────────────────────────────────────────────

    private sealed class StubEnvRepo : ISchedulingEnvironmentRepository
    {
        public List<SchedulingEnvironmentValue> Tags { get; } = [];

        public List<SchedulingEnvironmentValue> GetAll(string type) =>
            type == SchedulingEnvironmentTypes.Tag ? Tags : [];

        public SchedulingEnvironmentValue? GetById(string id) => Tags.Find(t => t.Id == id);
        public void Insert(string type, SchedulingEnvironmentValue value) => Tags.Add(value);
        public void Update(SchedulingEnvironmentValue value) { }
        public void Delete(string id, System.Data.Common.DbTransaction? tx = null) { }
        public bool ExistsByName(string type, string name, string? excludeId = null) => false;
    }

    private sealed class StubCourseRepo : ICourseRepository
    {
        public List<Course> Courses { get; } = [];

        public List<Course> GetAll() => Courses;
        public List<Course> GetBySubject(string subjectId) => [];
        public List<Course> GetAllActive() => Courses.Where(c => c.IsActive).ToList();
        public Course? GetById(string id) => Courses.Find(c => c.Id == id);
        public bool HasSections(string courseId) => false;
        public bool ExistsByCalendarCode(string code, string? excludeId = null) => false;
        public void Insert(Course course) => Courses.Add(course);
        public void Update(Course course, System.Data.Common.DbTransaction? tx = null) { }
        public void Delete(string id) { }
    }
}

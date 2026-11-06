using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// Manages the inline form that creates a new <see cref="ProgramWatch"/> (<see cref="Show"/>) or edits an
/// existing one (<see cref="ShowForEdit"/>). Handles mode
/// selection, tag/course multi-select, auto-name generation, and validation.
/// </summary>
public partial class WatchCreationViewModel : ObservableObject
{
    private readonly ISchedulingEnvironmentRepository _envRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly Action<ProgramWatch> _onSave;
    private readonly Action _onCancel;

    /// <summary>Whether the creation form is visible.</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>Selected mode: true = Tag, false = Course.</summary>
    [ObservableProperty] private bool _isTagMode = true;

    /// <summary>Inverse of <see cref="IsTagMode"/> for RadioButton binding.</summary>
    [ObservableProperty] private bool _isCourseMode;

    /// <summary>Auto-generated or user-edited name for the watch.</summary>
    [ObservableProperty] private string _watchName = string.Empty;

    /// <summary>Whether the user has manually edited the name (stops auto-generation).</summary>
    private bool _nameManuallyEdited;

    /// <summary>The last value set by auto-generation, used to detect manual edits.</summary>
    private string _lastAutoName = string.Empty;

    /// <summary>Available tags for selection (tag/level-based mode).</summary>
    public ObservableCollection<SelectableItem> Tags { get; } = [];

    /// <summary>Available levels for selection (tag/level-based mode, OR logic).</summary>
    public ObservableCollection<SelectableItem> Levels { get; } = [];

    /// <summary>Available courses for selection (course-based mode).</summary>
    public ObservableCollection<SelectableItem> Courses { get; } = [];

    /// <summary>Validation error shown when trying to save without selections.</summary>
    [ObservableProperty] private string? _validationError;

    /// <summary>
    /// The watch being edited, or <c>null</c> when the form is creating a new watch. Set by
    /// <see cref="ShowForEdit"/>; <see cref="Save"/> rebuilds this watch (same Id, on/off state and
    /// unknown JSON fields) instead of making a new one.
    /// </summary>
    private ProgramWatch? _editingWatch;

    /// <summary>True while the form is editing an existing watch rather than creating one.</summary>
    public bool IsEditing => _editingWatch is not null;

    /// <summary>The confirm button's caption: "Create" for a new watch, "Save" when editing.</summary>
    public string SaveButtonText => IsEditing ? "Save" : "Create";

    /// <summary>The Id of the watch being edited, or <c>null</c> when creating.</summary>
    public string? EditingWatchId => _editingWatch?.Id;

    /// <param name="envRepo">Repository for loading tags.</param>
    /// <param name="courseRepo">Repository for loading courses.</param>
    /// <param name="onSave">Callback invoked with the new watch when the user saves.</param>
    /// <param name="onCancel">Callback invoked when the user cancels creation.</param>
    public WatchCreationViewModel(
        ISchedulingEnvironmentRepository envRepo,
        ICourseRepository courseRepo,
        Action<ProgramWatch> onSave,
        Action onCancel)
    {
        _envRepo = envRepo;
        _courseRepo = courseRepo;
        _onSave = onSave;
        _onCancel = onCancel;
    }

    /// <summary>Opens the creation form and loads available tags and courses.</summary>
    public void Show()
    {
        SetEditingWatch(null);
        _nameManuallyEdited = false;
        _lastAutoName = string.Empty;
        ValidationError = null;
        WatchName = string.Empty;
        IsTagMode = true;
        LoadTags();
        LoadLevels();
        LoadCourses();
        IsVisible = true;
    }

    /// <summary>
    /// Opens the form pre-filled with an existing watch's mode, selections and name, so the user can change it.
    /// Saving calls the save callback with a rebuilt watch that keeps <paramref name="watch"/>'s Id, on/off state
    /// and unknown JSON fields; the callback tells it apart from a new watch by that Id.
    ///
    /// <para>Inactive courses the watch still names are added to the course list (already ticked), so they stay
    /// visible and are not silently dropped on save. Tags that no longer exist cannot be shown and are dropped.</para>
    /// </summary>
    /// <param name="watch">The watch to edit. It is not modified; the rebuilt copy goes to the save callback.</param>
    public void ShowForEdit(ProgramWatch watch)
    {
        SetEditingWatch(watch);
        ValidationError = null;

        // Mode first: its change handler regenerates the name, which is overwritten below.
        IsTagMode = watch.Mode == ProgramWatchMode.Tag;
        LoadTags();
        LoadLevels();
        LoadCourses(watch.CourseIds);

        if (watch.Mode == ProgramWatchMode.Tag)
        {
            SelectIds(Tags, watch.TagIds);
            SelectIds(Levels, watch.LevelIds);
        }
        else
        {
            SelectIds(Courses, watch.CourseIds);
        }

        // Selecting regenerated the auto-name. Keep auto-naming on only if the saved name is that auto-name;
        // a name the user typed earlier stays as typed (OnWatchNameChanged marks it as manually edited).
        _nameManuallyEdited = false;
        RegenerateName();
        WatchName = watch.Name;

        IsVisible = true;
    }

    /// <summary>Resets and hides the creation form.</summary>
    public void Hide()
    {
        IsVisible = false;
        SetEditingWatch(null);
        _nameManuallyEdited = false;
        _lastAutoName = string.Empty;
        Tags.Clear();
        Levels.Clear();
        Courses.Clear();
        WatchName = string.Empty;
        ValidationError = null;
    }

    // IsTagMode is the single source of truth for the watch mode. IsCourseMode is a pure
    // one-directional mirror that exists only so the "Course-based" RadioButton has a
    // two-way bindable target; nothing in this VM reads it (Save/RegenerateName/etc. all
    // branch on IsTagMode). The mirror is deliberately one-directional:
    //
    //   • IsTagMode changes  → update IsCourseMode to its inverse (below).
    //   • IsCourseMode changes → do NOT write back to IsTagMode.
    //
    // A back-write is redundant — the RadioButton group already keeps the two radios
    // mutually exclusive, so when the user picks "Course-based" the group manager unchecks
    // the "Tag" radio, whose two-way binding sets IsTagMode = false for us. Worse, the
    // back-write RE-ENTERED the group manager mid-OnCheckedChanged (it fires while the
    // manager is writing IsCheckedProperty), which wedged the UI thread when the detached
    // Schedule View was re-attached and its AccessPanelView rebuilt these radios. Dropping
    // the reverse leg removes that re-entrancy for good.
    partial void OnIsTagModeChanged(bool value)
    {
        if (IsCourseMode == value)
            IsCourseMode = !value;

        _nameManuallyEdited = false;
        _lastAutoName = string.Empty;
        RegenerateName();
        ValidationError = null;
    }

    partial void OnWatchNameChanged(string value)
    {
        if (!_nameManuallyEdited && value != _lastAutoName)
            _nameManuallyEdited = true;
    }

    /// <summary>
    /// Called when a tag or course selection changes. Regenerates the auto-name
    /// if not manually edited.
    /// </summary>
    public void OnSelectionChanged()
    {
        if (!_nameManuallyEdited)
            RegenerateName();
        ValidationError = null;
    }

    /// <summary>
    /// Validates the form and hands the resulting watch to the save callback, then hides the form. When creating,
    /// the watch is new (fresh Id, enabled). When editing, it is a rebuilt copy of the edited watch: same Id and
    /// on/off state, its unknown JSON fields carried over (spec item 3), and only the current mode's selections
    /// kept (switching mode clears the other mode's lists, as for a new watch).
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        List<string> tagIds = [], levelIds = [], courseIds = [];

        if (IsTagMode)
        {
            tagIds = GetSelectedIdsFrom(Tags);
            levelIds = GetSelectedIdsFrom(Levels);

            if (tagIds.Count == 0 && levelIds.Count == 0)
            {
                ValidationError = "Select at least one tag or level.";
                return;
            }
        }
        else
        {
            courseIds = GetSelectedIdsFrom(Courses);

            if (courseIds.Count == 0)
            {
                ValidationError = "Select at least one course.";
                return;
            }
        }

        var original = _editingWatch;
        var watch = new ProgramWatch
        {
            Name = string.IsNullOrWhiteSpace(WatchName) ? GenerateDefaultName() : WatchName.Trim(),
            Mode = IsTagMode ? ProgramWatchMode.Tag : ProgramWatchMode.Course,
            IsEnabled = original?.IsEnabled ?? true,
            TagIds = tagIds,
            LevelIds = levelIds,
            CourseIds = courseIds
        }.WithExtensionDataFrom(original);
        if (original is not null)
            watch.Id = original.Id;

        _onSave(watch);
        Hide();
    }

    /// <summary>
    /// Records which watch the form is editing (or <c>null</c> for a new watch) and refreshes the properties
    /// that depend on it.
    /// </summary>
    /// <param name="watch">The watch being edited, or <c>null</c>.</param>
    private void SetEditingWatch(ProgramWatch? watch)
    {
        _editingWatch = watch;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(EditingWatchId));
    }

    /// <summary>Ticks every item in <paramref name="source"/> whose Id is in <paramref name="ids"/>.</summary>
    /// <param name="source">The selectable list to update.</param>
    /// <param name="ids">The Ids to tick.</param>
    private static void SelectIds(ObservableCollection<SelectableItem> source, IEnumerable<string> ids)
    {
        var wanted = ids.ToHashSet();
        foreach (var item in source)
            if (wanted.Contains(item.Id))
                item.IsSelected = true;
    }

    /// <summary>Cancels creation and hides the form.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Hide();
        _onCancel();
    }

    private static List<string> GetSelectedIdsFrom(ObservableCollection<SelectableItem> source) =>
        [.. source.Where(i => i.IsSelected).Select(i => i.Id)];

    private void RegenerateName()
    {
        if (IsTagMode)
        {
            var tagNames = Tags.Where(i => i.IsSelected).Select(i => i.Name).ToList();
            var levelNames = Levels.Where(i => i.IsSelected).Select(i => i.Name).ToList();
            var generated = FormatTagLevelName(tagNames, levelNames);
            _lastAutoName = generated;
            WatchName = generated;
        }
        else
        {
            var courseNames = Courses.Where(i => i.IsSelected).Select(i => i.Name).ToList();
            var generated = courseNames.Count > 0 ? string.Join(", ", courseNames) : string.Empty;
            _lastAutoName = generated;
            WatchName = generated;
        }
    }

    /// <summary>
    /// Builds a display name from selected tags and levels.
    /// Tags joined with " + ", levels joined with "/", combined with " @ " separator.
    /// </summary>
    private static string FormatTagLevelName(List<string> tagNames, List<string> levelNames)
    {
        var tagPart = tagNames.Count > 0 ? string.Join(" + ", tagNames) : null;
        var levelPart = levelNames.Count > 0 ? string.Join("/", levelNames) : null;

        return (tagPart, levelPart) switch
        {
            (not null, not null) => $"{tagPart} @ {levelPart}",
            (not null, null) => tagPart,
            (null, not null) => levelPart,
            _ => string.Empty
        };
    }

    private string GenerateDefaultName() => IsTagMode
        ? FormatTagLevelName(
            Tags.Where(i => i.IsSelected).Select(i => i.Name).ToList(),
            Levels.Where(i => i.IsSelected).Select(i => i.Name).ToList())
        : string.Join(", ", Courses.Where(i => i.IsSelected).Select(i => i.Name));

    private void LoadTags()
    {
        Tags.Clear();
        var tags = _envRepo.GetAll(SchedulingEnvironmentTypes.Tag);
        foreach (var tag in tags)
            Tags.Add(new SelectableItem(tag.Id, tag.Name, OnItemSelectionChanged));
    }

    private void LoadLevels()
    {
        Levels.Clear();
        foreach (var level in CourseLevelParser.AllLevels)
            Levels.Add(new SelectableItem(level, level, OnItemSelectionChanged));
    }

    /// <summary>
    /// Fills <see cref="Courses"/> with the active courses, plus any course in <paramref name="alsoInclude"/> that
    /// exists but is inactive (so an edited watch's inactive courses stay visible and are kept on save).
    /// </summary>
    /// <param name="alsoInclude">Course Ids to show even if inactive; <c>null</c> for none.</param>
    private void LoadCourses(IEnumerable<string>? alsoInclude = null)
    {
        Courses.Clear();
        var courses = _courseRepo.GetAllActive();
        foreach (var c in courses)
            Courses.Add(new SelectableItem(c.Id, c.CalendarCode, OnItemSelectionChanged));

        if (alsoInclude is null) return;
        var shown = courses.Select(c => c.Id).ToHashSet();
        foreach (var id in alsoInclude)
            if (shown.Add(id) && _courseRepo.GetById(id) is { } extra)
                Courses.Add(new SelectableItem(extra.Id, extra.CalendarCode, OnItemSelectionChanged));
    }

    private void OnItemSelectionChanged() => OnSelectionChanged();
}

/// <summary>
/// A selectable item (tag or course) for the watch creation multi-select lists.
/// </summary>
public partial class SelectableItem : ObservableObject
{
    private readonly Action? _onChanged;

    public string Id { get; }
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    public SelectableItem(string id, string name, Action? onChanged = null)
    {
        Id = id;
        Name = name;
        _onChanged = onChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _onChanged?.Invoke();
}

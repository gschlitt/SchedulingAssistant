using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// Manages the inline creation form for a new <see cref="ProgramWatch"/>. Handles mode
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

    /// <summary>Available tags for selection (tag-based mode).</summary>
    public ObservableCollection<SelectableItem> Tags { get; } = [];

    /// <summary>Available courses for selection (course-based mode).</summary>
    public ObservableCollection<SelectableItem> Courses { get; } = [];

    /// <summary>Validation error shown when trying to save without selections.</summary>
    [ObservableProperty] private string? _validationError;

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
        _nameManuallyEdited = false;
        _lastAutoName = string.Empty;
        ValidationError = null;
        WatchName = string.Empty;
        IsTagMode = true;
        LoadTags();
        LoadCourses();
        IsVisible = true;
    }

    /// <summary>Resets and hides the creation form.</summary>
    public void Hide()
    {
        IsVisible = false;
        _nameManuallyEdited = false;
        _lastAutoName = string.Empty;
        Tags.Clear();
        Courses.Clear();
        WatchName = string.Empty;
        ValidationError = null;
    }

    partial void OnIsTagModeChanged(bool value)
    {
        if (IsCourseMode == value)
            IsCourseMode = !value;
        _nameManuallyEdited = false;
        _lastAutoName = string.Empty;
        RegenerateName();
        ValidationError = null;
    }

    partial void OnIsCourseModeChanged(bool value)
    {
        if (IsTagMode == value)
            IsTagMode = !value;
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

    /// <summary>Validates and saves the new watch.</summary>
    [RelayCommand]
    private void Save()
    {
        var selectedIds = GetSelectedIds();
        if (selectedIds.Count == 0)
        {
            ValidationError = IsTagMode
                ? "Select at least one tag."
                : "Select at least one course.";
            return;
        }

        var watch = new ProgramWatch
        {
            Name = string.IsNullOrWhiteSpace(WatchName) ? GenerateDefaultName(selectedIds) : WatchName.Trim(),
            Mode = IsTagMode ? ProgramWatchMode.Tag : ProgramWatchMode.Course,
            IsEnabled = true,
            TagIds = IsTagMode ? selectedIds : [],
            CourseIds = IsTagMode ? [] : selectedIds
        };

        _onSave(watch);
        Hide();
    }

    /// <summary>Cancels creation and hides the form.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Hide();
        _onCancel();
    }

    private List<string> GetSelectedIds()
    {
        var source = IsTagMode ? Tags : Courses;
        return [.. source.Where(i => i.IsSelected).Select(i => i.Id)];
    }

    private void RegenerateName()
    {
        var source = IsTagMode ? Tags : Courses;
        var selected = source.Where(i => i.IsSelected).Select(i => i.Name).ToList();

        var generated = selected.Count switch
        {
            0 => string.Empty,
            _ when IsTagMode => string.Join(" + ", selected),
            _ => string.Join(", ", selected)
        };

        _lastAutoName = generated;
        WatchName = generated;
    }

    private string GenerateDefaultName(List<string> selectedIds)
    {
        var source = IsTagMode ? Tags : Courses;
        var names = source.Where(i => selectedIds.Contains(i.Id)).Select(i => i.Name).ToList();
        return IsTagMode ? string.Join(" + ", names) : string.Join(", ", names);
    }

    private void LoadTags()
    {
        Tags.Clear();
        var tags = _envRepo.GetAll(SchedulingEnvironmentTypes.Tag);
        foreach (var tag in tags)
            Tags.Add(new SelectableItem(tag.Id, tag.Name, OnItemSelectionChanged));
    }

    private void LoadCourses()
    {
        Courses.Clear();
        var courses = _courseRepo.GetAllActive();
        foreach (var c in courses)
            Courses.Add(new SelectableItem(c.Id, c.CalendarCode, OnItemSelectionChanged));
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

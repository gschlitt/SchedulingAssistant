using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Access;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.Management;

public partial class CourseEditViewModel : UnappliedChangesEditor
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(ComputedCalendarCode))]
    private Subject? _selectedSubject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(ComputedCalendarCode))]
    private string _courseNumber = string.Empty;

    /// <summary>
    /// The level band the user has selected (e.g. "100", "300"), or null when none
    /// is chosen.  Auto-suggested from the course number whenever it changes;
    /// the user may override the suggestion via the Level combo-box.
    /// </summary>
    [ObservableProperty] private string? _selectedLevel;

    /// <summary>Fixed ordered list of level options shown in the Level combo-box.</summary>
    public IReadOnlyList<string> LevelOptions => CourseLevelParser.AllLevels;

    [ObservableProperty] private string _courseTitle = string.Empty;

    /// <summary>
    /// Optional seating capacity as edited text; empty = null (unspecified).
    /// Validated in <see cref="ValidationError"/> and parsed in <see cref="ParsedCapacity"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _capacityText = string.Empty;

    /// <summary>Parsed capacity, or null when blank. Whitespace/invalid text is caught by validation.</summary>
    private int? ParsedCapacity =>
        string.IsNullOrWhiteSpace(CapacityText) ? null
        : int.TryParse(CapacityText.Trim(), out var n) ? n : null;

    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();

    /// <summary>Multi-select tag choices shown in the course editor.</summary>
    [ObservableProperty] private ObservableCollection<TagSelectionViewModel> _tagSelections = new();

    public string FormTitle => IsNew ? "Add Course" : "Edit Course";
    public bool IsNew { get; }

    /// <summary>
    /// True when this course already has sections scheduled in one or more semesters,
    /// which permanently locks the subject and course-number (i.e. the course's
    /// identity). The title, level, capacity, tags, and active flag remain editable.
    /// Always false for new courses. This mirrors the rule that forbids deleting a
    /// course once sections of it exist.
    /// </summary>
    public bool IsIdentityLocked { get; }

    /// <summary>
    /// Inverse of <see cref="IsIdentityLocked"/>. Bound to <c>IsEnabled</c> on the
    /// subject and course-number controls so they can't be changed once sections exist.
    /// </summary>
    public bool IsIdentityEditable => !IsIdentityLocked;

    public string ComputedCalendarCode => SelectedSubject is null
        ? string.Empty
        : $"{SelectedSubject.CalendarAbbreviation}{CourseNumber}";

    /// <summary>
    /// Comma-joined names of all selected tags, for display in the Expander header.
    /// Returns "(none)" when no tags are selected.
    /// </summary>
    public string TagSummary =>
        TagSelections.Where(t => t.IsSelected).Select(t => t.Value.Name) is var names && names.Any()
            ? string.Join(", ", names)
            : "(none)";

    private readonly Course _course;
    private readonly Func<Course, Task> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _codeExists;
    private readonly Func<List<string>, Task<int>>? _applyTagsToAll;
    private readonly Func<string?>? _whyApplyTagsRefused;
    private readonly CourseComponentsContext? _components;

    // ── Components group (Access Analyzer revision 8f) ──

    /// <summary>True when the Components group shows (the access checks are set up); false shows <see cref="ComponentsHint"/> instead.</summary>
    public bool HasComponentGroup => _components is not null;

    /// <summary>True when the access checks are not set up: the hint shows in place of the group.</summary>
    public bool ShowComponentsHint => _components is null;

    /// <summary>The hint shown before the access setup.</summary>
    public string ComponentsHint => AccessText.ComponentsHintNotSetUp;

    /// <summary>The text shown when no section type is marked Component (and the course lists none).</summary>
    public string NoComponentTypesText => AccessText.NoComponentTypes;

    /// <summary>The boxes: the component section types plus any type the course lists that is no longer one, in section type order.</summary>
    public ObservableCollection<ComponentOptionViewModel> ComponentOptions { get; } = new();

    /// <summary>True when there is no box to show.</summary>
    public bool HasNoComponentOptions => _components is not null && ComponentOptions.Count == 0;

    /// <summary>
    /// "Not counted with these components: X01 (no section type), ...": shown when two or more components are ticked
    /// and a section's type is neither ticked nor a component type (rule 8). Null when there is nothing to say.
    /// </summary>
    public string? NotCountedText { get; private set; }

    /// <summary>Rule 8a's sentences for the ticked list (a section of a component type the course does not list), each prefixed with the warning mark by the view.</summary>
    public IReadOnlyList<string> ComponentMismatchLines { get; private set; } = [];

    /// <summary>The ticked component type ids, in section type order.</summary>
    private List<string> TickedComponentIds() =>
        ComponentOptions.Where(o => o.IsSelected).Select(o => o.Type.Id).ToList();

    /// <summary>Re-reads the warnings after a box changed.</summary>
    private void RefreshComponentWarnings()
    {
        if (_components is null) return;
        var ticked = TickedComponentIds();
        NotCountedText = null;
        if (ticked.Count >= 2)
        {
            var typeById = _components.SectionTypes.ToDictionary(t => t.Id);
            var labels = _components.Sections
                .Where(s => s.SectionTypeId is null
                            || (!ticked.Contains(s.SectionTypeId)
                                && !(typeById.TryGetValue(s.SectionTypeId, out var t) && t.IsComponent)))
                .Select(s => $"{s.SectionCode} ({(s.SectionTypeId is not null && typeById.TryGetValue(s.SectionTypeId, out var ty) ? ty.Name : "no section type")})")
                .ToList();
            if (labels.Count > 0) NotCountedText = AccessText.NotCountedWarning(labels);
        }
        ComponentMismatchLines = _components.MismatchLines(ticked);
        OnPropertyChanged(nameof(NotCountedText));
        OnPropertyChanged(nameof(ComponentMismatchLines));
    }

    public string? ValidationError
    {
        get
        {
            if (SelectedSubject is null) return "Subject is required.";

            var numberTrimmed = CourseNumber.Trim();
            if (numberTrimmed.Length == 0) return "Course number is required.";
            if (numberTrimmed.Length > 8)
                return "Course number must be 8 characters or fewer.";

            if (!string.IsNullOrWhiteSpace(CapacityText)
                && !(int.TryParse(CapacityText.Trim(), out var cap) && cap >= 0))
                return "Capacity must be a whole number (or left blank).";

            var calendarCode = ComputedCalendarCode;
            if (_codeExists(calendarCode)) return $"\"{calendarCode}\" is already used by another course.";
            return null;
        }
    }

    /// <summary>
    /// Called when the CourseNumber property changes.  Auto-suggests a level by
    /// running <see cref="CourseLevelParser.ParseLevel"/> on the new value.  The
    /// suggestion replaces any previous auto-suggestion; a value the user set
    /// manually via the combo-box is NOT specially preserved here — if they want
    /// to keep a manual choice they should not change the course number.
    /// </summary>
    partial void OnCourseNumberChanged(string value)
    {
        SelectedLevel = CourseLevelParser.ParseLevel(value);
    }

    private bool CanSave() => SelectedSubject is not null && CourseNumber.Trim().Length > 0 && ValidationError is null;

    /// <summary>
    /// Constructs the course editor view-model.
    /// </summary>
    /// <param name="course">The course being edited, or a blank one for Add.</param>
    /// <param name="isNew">True when adding a new course; false when editing an existing one.</param>
    /// <param name="allTags">All available tag property values for the multi-select picker.</param>
    /// <param name="onSave">Async callback invoked when the user saves.</param>
    /// <param name="onCancel">Callback invoked when the user cancels.</param>
    /// <param name="codeExists">Delegate that returns true when the calendar code is already in use.</param>
    /// <param name="subjects">All subjects, used to populate the Subject dropdown.</param>
    /// <param name="applyTagsToAll">Optional callback that applies the given tag IDs to all sections of this course. Returns the number of sections updated.</param>
    /// <param name="identityLocked">When true (and not a new course), the subject and course-number fields are locked because the course already has sections scheduled.</param>
    /// <param name="whyApplyTagsRefused">Optional check run before "Apply tags to all sections": returns the message to show when it must be refused (an open section card would be discarded by the reload), or null to proceed.</param>
    /// <param name="components">The Components group's data, given only once the access checks are set up; null shows the hint and leaves the course's stored components alone.</param>
    public CourseEditViewModel(
        Course course,
        bool isNew,
        IReadOnlyList<SchedulingEnvironmentValue> allTags,
        Func<Course, Task> onSave,
        Action onCancel,
        Func<string, bool> codeExists,
        ObservableCollection<Subject> subjects,
        Func<List<string>, Task<int>>? applyTagsToAll = null,
        bool identityLocked = false,
        Func<string?>? whyApplyTagsRefused = null,
        CourseComponentsContext? components = null)
    {
        _components = components;
        _course = course;
        IsNew = isNew;
        IsIdentityLocked = identityLocked && !isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _codeExists = codeExists;
        _applyTagsToAll = applyTagsToAll;
        _whyApplyTagsRefused = whyApplyTagsRefused;
        Subjects = subjects;

        // Initialize from existing course if editing
        if (!isNew && !string.IsNullOrEmpty(course.CalendarCode) && subjects.Count > 0)
        {
            // Extract subject and course number from calendar code
            var calendarCode = course.CalendarCode;
            var subjectMatch = subjects.FirstOrDefault(s =>
                calendarCode.StartsWith(s.CalendarAbbreviation, StringComparison.OrdinalIgnoreCase));
            if (subjectMatch is not null)
            {
                SelectedSubject = subjectMatch;
                CourseNumber = calendarCode.Substring(subjectMatch.CalendarAbbreviation.Length);
            }
        }

        CourseTitle = course.Title;
        CapacityText = course.Capacity?.ToString() ?? string.Empty;
        IsActive = course.IsActive;

        // Restore persisted level (editing) or auto-suggest from the course number (new).
        SelectedLevel = !string.IsNullOrEmpty(course.Level)
            ? course.Level
            : CourseLevelParser.ParseLevel(CourseNumber);

        // Build tag multi-select, pre-checking any tags already on the course.
        TagSelections = new ObservableCollection<TagSelectionViewModel>(
            allTags.Select(t => new TagSelectionViewModel(t, course.TagIds.Contains(t.Id))));

        // Keep TagSummary fresh as checkboxes change.
        foreach (var tag in TagSelections)
            tag.PropertyChanged += (_, e) => { if (e.PropertyName == "IsSelected") OnPropertyChanged(nameof(TagSummary)); };

        if (components is not null)
        {
            var initial = components.Initial.ToHashSet();
            foreach (var type in components.SectionTypes.OrderBy(t => t.SortOrder).Where(t => t.IsComponent || initial.Contains(t.Id)))
            {
                int n = components.Sections.Count(s => s.SectionTypeId == type.Id);
                var option = new ComponentOptionViewModel(type, initial.Contains(type.Id), AccessText.ThisTermCount(n));
                option.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(ComponentOptionViewModel.IsSelected)) return;
                    RefreshComponentWarnings();
                };
                ComponentOptions.Add(option);
            }
            RefreshComponentWarnings();
        }

        TakeSnapshot();
    }

    /// <inheritdoc/>
    protected override string CaptureState() => UnappliedChangesKey.Of(
        SelectedSubject?.Id, CourseNumber.Trim(), SelectedLevel ?? "", CourseTitle.Trim(), CapacityText.Trim(),
        IsActive, TagSelections.Where(t => t.IsSelected).Select(t => t.Value.Id),
        ComponentOptions.Where(o => o.IsSelected).Select(o => o.Type.Id));

    /// <inheritdoc/>
    protected override System.Windows.Input.ICommand ApplyCommand => SaveCommand;

    /// <inheritdoc/>
    protected override System.Windows.Input.ICommand CancelCommandForDiscard => CancelCommand;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        _course.SubjectId = SelectedSubject!.Id;
        _course.CalendarCode = ComputedCalendarCode;
        _course.Title = CourseTitle.Trim();
        _course.Capacity = ParsedCapacity;
        _course.IsActive = IsActive;
        _course.Level = SelectedLevel ?? string.Empty;
        _course.TagIds = TagSelections
            .Where(t => t.IsSelected)
            .Select(t => t.Value.Id)
            .ToList();
        // Only once the access checks are set up: before that the stored list stays as it is (null = never set).
        if (_components is not null)
            _course.ComponentTypeIds = TickedComponentIds();
        await _onSave(_course);
        MarkApplied();
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    /// <summary>Whether the "Apply to all sections" button should be visible (editing existing course with callback available).</summary>
    public bool CanApplyTagsToAll => !IsNew && _applyTagsToAll is not null;

    /// <summary>Status message shown after applying tags to all sections.</summary>
    [ObservableProperty] private string? _applyTagsResult;

    /// <summary>
    /// Applies the currently selected tags to every section of this course across all semesters.
    /// </summary>
    [RelayCommand]
    private async Task ApplyTagsToAllSections()
    {
        if (_applyTagsToAll is null) return;
        if (_whyApplyTagsRefused?.Invoke() is { } refusal)
        {
            ApplyTagsResult = refusal;
            return;
        }
        var selectedTagIds = TagSelections
            .Where(t => t.IsSelected)
            .Select(t => t.Value.Id)
            .ToList();
        try
        {
            var count = await _applyTagsToAll(selectedTagIds);
            ApplyTagsResult = $"Tags applied to {count} section{(count == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "CourseEditViewModel.ApplyTagsToAllSections");
            ApplyTagsResult = WriteFailureText.For(ex, "Failed to apply tags. Please try again.");
        }
    }
}

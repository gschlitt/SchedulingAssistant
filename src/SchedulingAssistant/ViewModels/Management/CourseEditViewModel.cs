using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CourseEditViewModel : ViewModelBase
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
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();

    /// <summary>Multi-select tag choices shown in the course editor.</summary>
    [ObservableProperty] private ObservableCollection<TagSelectionViewModel> _tagSelections = new();

    public string FormTitle => IsNew ? "Add Course" : "Edit Course";
    public bool IsNew { get; }

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

    public string? ValidationError
    {
        get
        {
            if (SelectedSubject is null) return "Subject is required.";

            var numberTrimmed = CourseNumber.Trim();
            if (numberTrimmed.Length == 0) return "Course number is required.";
            if (numberTrimmed.Length > 8)
                return "Course number must be 8 characters or fewer.";

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
    public CourseEditViewModel(
        Course course,
        bool isNew,
        IReadOnlyList<SectionPropertyValue> allTags,
        Func<Course, Task> onSave,
        Action onCancel,
        Func<string, bool> codeExists,
        ObservableCollection<Subject> subjects)
    {
        _course = course;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _codeExists = codeExists;
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
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        _course.SubjectId = SelectedSubject!.Id;
        _course.CalendarCode = ComputedCalendarCode;
        _course.Title = CourseTitle.Trim();
        _course.IsActive = IsActive;
        _course.Level = SelectedLevel ?? string.Empty;
        _course.TagIds = TagSelections
            .Where(t => t.IsSelected)
            .Select(t => t.Value.Id)
            .ToList();
        await _onSave(_course);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

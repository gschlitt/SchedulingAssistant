using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
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

    [ObservableProperty] private string _courseTitle = string.Empty;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private ObservableCollection<TagSelectionViewModel> _tagSelections = new();

    public string FormTitle => IsNew ? "Add Course" : "Edit Course";
    public bool IsNew { get; }

    public string ComputedCalendarCode => SelectedSubject is null
        ? string.Empty
        : $"{SelectedSubject.CalendarAbbreviation}{CourseNumber}";

    /// <summary>
    /// Comma-separated names of all currently selected tags; shows "(none)" when empty.
    /// Recomputed automatically whenever any TagSelectionViewModel.IsSelected changes.
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
            if (!System.Text.RegularExpressions.Regex.IsMatch(numberTrimmed, @"^\d{3}$"))
                return "Course number must be exactly 3 digits (0-9).";

            var calendarCode = ComputedCalendarCode;
            if (_codeExists(calendarCode)) return $"\"{calendarCode}\" is already used by another course.";
            return null;
        }
    }

    private bool CanSave() => SelectedSubject is not null && CourseNumber.Trim().Length > 0 && ValidationError is null;

    /// <summary>
    /// Initialises the course editor.
    /// </summary>
    /// <param name="course">The course object to edit (or a new blank course for adds).</param>
    /// <param name="isNew">True when adding, false when editing.</param>
    /// <param name="onSave">Callback invoked with the mutated course on successful save.</param>
    /// <param name="onCancel">Callback invoked when the user clicks Cancel.</param>
    /// <param name="codeExists">Delegate returning true if the given calendar code is already taken.</param>
    /// <param name="subjects">All subjects to populate the subject dropdown.</param>
    /// <param name="allTags">All available tag property values for the tag multi-select.</param>
    public CourseEditViewModel(
        Course course,
        bool isNew,
        Func<Course, Task> onSave,
        Action onCancel,
        Func<string, bool> codeExists,
        ObservableCollection<Subject> subjects,
        IReadOnlyList<SectionPropertyValue> allTags)
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

        // Build tag multi-select: mark each tag selected if it's in course.TagIds.
        TagSelections = new ObservableCollection<TagSelectionViewModel>(
            allTags.Select(t => new TagSelectionViewModel(t, course.TagIds.Contains(t.Id))));

        // Re-raise TagSummary whenever any checkbox changes.
        WireTagSummary();
    }

    /// <summary>
    /// Subscribes to each TagSelectionViewModel's PropertyChanged so that TagSummary
    /// is re-raised whenever the user toggles a checkbox.
    /// </summary>
    private void WireTagSummary()
    {
        foreach (var tag in TagSelections)
            tag.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TagSummary));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        _course.SubjectId = SelectedSubject!.Id;
        _course.CalendarCode = ComputedCalendarCode;
        _course.Title = CourseTitle.Trim();
        _course.IsActive = IsActive;
        _course.TagIds = TagSelections
            .Where(t => t.IsSelected)
            .Select(t => t.Value.Id)
            .ToList();
        await _onSave(_course);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

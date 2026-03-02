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

    public string FormTitle => IsNew ? "Add Course" : "Edit Course";
    public bool IsNew { get; }

    public string ComputedCalendarCode => SelectedSubject is null
        ? string.Empty
        : $"{SelectedSubject.CalendarAbbreviation}{CourseNumber}";

    private readonly Course _course;
    private readonly Action<Course> _onSave;
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

    public CourseEditViewModel(
        Course course,
        bool isNew,
        Action<Course> onSave,
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
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _course.SubjectId = SelectedSubject!.Id;
        _course.CalendarCode = ComputedCalendarCode;
        _course.Title = CourseTitle.Trim();
        _course.IsActive = IsActive;
        _onSave(_course);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

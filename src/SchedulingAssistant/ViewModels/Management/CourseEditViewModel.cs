using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CourseEditViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _calendarCode = string.Empty;

    [ObservableProperty] private string _courseTitle = string.Empty;
    [ObservableProperty] private bool _isActive = true;

    public string FormTitle => IsNew ? "Add Course" : "Edit Course";
    public bool IsNew { get; }

    private readonly Course _course;
    private readonly Action<Course> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _codeExists;

    public string? ValidationError
    {
        get
        {
            var trimmed = CalendarCode.Trim();
            if (trimmed.Length == 0) return null;
            if (_codeExists(trimmed)) return $"\"{trimmed}\" is already used by another course.";
            return null;
        }
    }

    private bool CanSave() => CalendarCode.Trim().Length > 0 && ValidationError is null;

    public CourseEditViewModel(
        Course course,
        bool isNew,
        Action<Course> onSave,
        Action onCancel,
        Func<string, bool> codeExists)
    {
        _course = course;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _codeExists = codeExists;

        CalendarCode = course.CalendarCode;
        CourseTitle = course.Title;
        IsActive = course.IsActive;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _course.CalendarCode = CalendarCode.Trim();
        _course.Title = CourseTitle.Trim();
        _course.IsActive = IsActive;
        _onSave(_course);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

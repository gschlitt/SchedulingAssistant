using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SubjectEditViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty] private string _department = string.Empty;

    public string Title => IsNew ? "Add Subject" : "Edit Subject";
    public bool IsNew { get; }

    private readonly Subject _subject;
    private readonly Action<Subject> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _nameExists;

    public string? ValidationError
    {
        get
        {
            var trimmed = Name.Trim();
            if (trimmed.Length == 0) return null;
            if (_nameExists(trimmed)) return $"\"{trimmed}\" already exists.";
            return null;
        }
    }

    private bool CanSave() => Name.Trim().Length > 0 && ValidationError is null;

    public SubjectEditViewModel(
        Subject subject,
        bool isNew,
        Action<Subject> onSave,
        Action onCancel,
        Func<string, bool> nameExists)
    {
        _subject = subject;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _nameExists = nameExists;

        Name = subject.Name;
        Department = subject.Department;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _subject.Name = Name.Trim();
        _subject.Department = Department.Trim();
        _onSave(_subject);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

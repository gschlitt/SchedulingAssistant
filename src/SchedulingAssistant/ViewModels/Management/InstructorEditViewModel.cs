using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class InstructorEditViewModel : ViewModelBase
{
    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _initials = string.Empty;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _department = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    public string Title => IsNew ? "Add Instructor" : "Edit Instructor";
    public bool IsNew { get; }

    private readonly Instructor _instructor;
    private readonly Action<Instructor> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _initialsExist;

    public string? ValidationError
    {
        get
        {
            var trimmed = Initials.Trim();
            if (trimmed.Length == 0) return null;
            if (_initialsExist(trimmed)) return $"Initials \"{trimmed}\" are already used by another instructor.";
            return null;
        }
    }

    private bool CanSave() => LastName.Trim().Length > 0
                           && Initials.Trim().Length > 0
                           && ValidationError is null;

    public InstructorEditViewModel(
        Instructor instructor,
        bool isNew,
        Action<Instructor> onSave,
        Action onCancel,
        Func<string, bool> initialsExist)
    {
        _instructor = instructor;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _initialsExist = initialsExist;

        FirstName = instructor.FirstName;
        LastName = instructor.LastName;
        Initials = instructor.Initials;
        Email = instructor.Email;
        Department = instructor.Department;
        Notes = instructor.Notes;
    }

    partial void OnFirstNameChanged(string value) => AutoInitials();
    partial void OnLastNameChanged(string value)
    {
        AutoInitials();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void AutoInitials()
    {
        if (!string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName))
            Initials = $"{FirstName[0]}{LastName[0]}".ToUpper();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _instructor.FirstName = FirstName.Trim();
        _instructor.LastName = LastName.Trim();
        _instructor.Initials = Initials.Trim();
        _instructor.Email = Email.Trim();
        _instructor.Department = Department.Trim();
        _instructor.Notes = Notes.Trim();
        _onSave(_instructor);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

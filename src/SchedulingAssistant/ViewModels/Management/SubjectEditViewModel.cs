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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _calendarAbbreviation = string.Empty;

    public string Title => IsNew ? "Add Subject" : "Edit Subject";
    public bool IsNew { get; }

    private readonly Subject _subject;
    private readonly Action<Subject> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _nameExists;
    private readonly Func<string, bool> _abbreviationExists;

    public string? ValidationError
    {
        get
        {
            var nameTrimmed = Name.Trim();
            var abbrevTrimmed = CalendarAbbreviation.Trim();

            if (nameTrimmed.Length == 0) return "Subject name is required.";
            if (abbrevTrimmed.Length == 0) return "Calendar abbreviation is required.";
            if (_nameExists(nameTrimmed)) return $"Subject name \"{nameTrimmed}\" already exists.";
            if (_abbreviationExists(abbrevTrimmed)) return $"Calendar abbreviation \"{abbrevTrimmed}\" already exists.";
            return null;
        }
    }

    private bool CanSave() => Name.Trim().Length > 0 && CalendarAbbreviation.Trim().Length > 0 && ValidationError is null;

    public SubjectEditViewModel(
        Subject subject,
        bool isNew,
        Action<Subject> onSave,
        Action onCancel,
        Func<string, bool> nameExists,
        Func<string, bool> abbreviationExists)
    {
        _subject = subject;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _nameExists = nameExists;
        _abbreviationExists = abbreviationExists;

        Name = subject.Name;
        CalendarAbbreviation = subject.CalendarAbbreviation;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _subject.Name = Name.Trim();
        _subject.CalendarAbbreviation = CalendarAbbreviation.Trim();
        _onSave(_subject);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

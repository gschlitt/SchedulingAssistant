using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionPropertyEditViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    public string Title => IsNew ? "Add" : "Edit";
    public bool IsNew { get; }

    private readonly SectionPropertyValue _value;
    private readonly Action<SectionPropertyValue> _onSave;
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

    public SectionPropertyEditViewModel(
        SectionPropertyValue value,
        bool isNew,
        Action<SectionPropertyValue> onSave,
        Action onCancel,
        Func<string, bool> nameExists)
    {
        _value = value;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _nameExists = nameExists;
        Name = value.Name;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _value.Name = Name.Trim();
        _onSave(_value);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

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

    /// <summary>
    /// The section code abbreviation field value. Only relevant when ShowAbbreviation is true.
    /// </summary>
    [ObservableProperty] private string _abbreviation = string.Empty;

    /// <summary>
    /// When true, the "Section Code Abbreviation" field is shown in the edit form.
    /// Currently only true for the Campus property type.
    /// </summary>
    public bool ShowAbbreviation { get; }

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
        Func<string, bool> nameExists,
        bool showAbbreviation = false)
    {
        _value = value;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _nameExists = nameExists;
        ShowAbbreviation = showAbbreviation;
        Name = value.Name;
        Abbreviation = value.SectionCodeAbbreviation ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _value.Name = Name.Trim();
        _value.SectionCodeAbbreviation = ShowAbbreviation && Abbreviation.Trim().Length > 0
            ? Abbreviation.Trim()
            : null;
        _onSave(_value);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}

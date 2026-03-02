using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for editing the single Academic Unit in the system.
/// There is always exactly one unit; this dialog allows editing its name.
/// </summary>
public partial class AcademicUnitListViewModel : ViewModelBase
{
    private readonly AcademicUnitService _service;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty] private bool _isEditing;

    public string? ValidationError
    {
        get
        {
            var nameTrimmed = Name.Trim();
            if (nameTrimmed.Length == 0) return "Academic Unit name is required.";
            return null;
        }
    }

    private bool CanSave() => Name.Trim().Length > 0 && ValidationError is null;

    public AcademicUnitListViewModel(AcademicUnitService service)
    {
        _service = service;
        Load();
    }

    private void Load()
    {
        var unit = _service.GetUnit();
        Name = unit.Name;
        IsEditing = false;
    }

    [RelayCommand]
    private void Edit()
    {
        IsEditing = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _service.UpdateName(Name);
        IsEditing = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        Load();
        IsEditing = false;
    }
}

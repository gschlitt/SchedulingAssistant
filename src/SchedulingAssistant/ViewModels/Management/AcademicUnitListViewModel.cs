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
    /// <summary>Category label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Academic Units";

    private readonly AcademicUnitService _service;
    private readonly WriteLockService _lockService;

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

    private bool CanSave() => _lockService.IsWriter && Name.Trim().Length > 0 && ValidationError is null;

    /// <summary>True when the current user holds the write lock; controls whether the Edit button is enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    public AcademicUnitListViewModel(AcademicUnitService service, WriteLockService lockService)
    {
        _service = service;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load()
    {
        var unit = _service.GetUnit();
        Name = unit.Name;
        IsEditing = false;
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        SaveCommand.NotifyCanExecuteChanged();
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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Services;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// ViewModel for editing the single Academic Unit and its parent institution.
/// There is always exactly one unit; this dialog allows editing its name
/// along with the institution name and abbreviation.
/// </summary>
public partial class AcademicUnitListViewModel : ViewModelBase, IDisposable
{
    /// <summary>Category label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Academic Units";

    private readonly AcademicUnitService _service;
    private readonly WriteLockService _lockService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _institutionName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _institutionAbbrev = string.Empty;

    [ObservableProperty] private bool _isEditing;

    /// <summary>
    /// Summary line shown above the Edit button: "AcademicUnitName, InstitutionName".
    /// Omits institution name if it is blank.
    /// </summary>
    public string DisplaySummary
    {
        get
        {
            var unit = Name.Trim();
            var inst = InstitutionName.Trim();
            return inst.Length > 0 ? $"{unit}, {inst}" : unit;
        }
    }

    public string? ValidationError
    {
        get
        {
            if (Name.Trim().Length == 0) return "Academic Unit name is required.";
            if (InstitutionName.Trim().Length == 0) return "Institution name is required.";
            if (InstitutionAbbrev.Trim().Length == 0) return "Institution abbreviation is required.";
            return null;
        }
    }

    private bool CanSave() => _lockService.IsWriter && ValidationError is null;

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
        InstitutionName = unit.InstitutionName;
        InstitutionAbbrev = unit.InstitutionAbbrev;
        OnPropertyChanged(nameof(DisplaySummary));
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
        _service.UpdateUnit(Name, InstitutionName, InstitutionAbbrev);
        OnPropertyChanged(nameof(DisplaySummary));
        IsEditing = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        Load();
        IsEditing = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lockService.LockStateChanged -= OnLockStateChanged;
    }
}

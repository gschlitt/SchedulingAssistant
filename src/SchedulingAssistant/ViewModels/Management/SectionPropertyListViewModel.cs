using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionPropertyListViewModel : ViewModelBase
{
    private readonly SectionPropertyRepository _repo;
    private readonly string _type;

    public string DisplayName { get; }

    /// <summary>
    /// When true, the "Section Code Abbreviation" field is shown in the edit form and
    /// as a column in the list. Currently only true for the Campus property type.
    /// </summary>
    public bool ShowAbbreviation { get; }

    [ObservableProperty] private ObservableCollection<SectionPropertyValue> _items = new();
    [ObservableProperty] private SectionPropertyValue? _selectedItem;
    [ObservableProperty] private SectionPropertyEditViewModel? _editVm;

    /// <summary>Scaffolded for future use (e.g. warn before delete).</summary>
    public Func<string, Task>? ShowError { get; set; }

    public SectionPropertyListViewModel(
        string propertyType,
        string displayName,
        SectionPropertyRepository repo,
        bool showAbbreviation = false)
    {
        _type = propertyType;
        DisplayName = displayName;
        _repo = repo;
        ShowAbbreviation = showAbbreviation;
        Load();
    }

    public void Load() =>
        Items = new ObservableCollection<SectionPropertyValue>(_repo.GetAll(_type));

    [RelayCommand]
    private void Add()
    {
        var value = new SectionPropertyValue();
        EditVm = new SectionPropertyEditViewModel(value, isNew: true,
            onSave: v => { _repo.Insert(_type, v); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedItem is null) return;
        var copy = new SectionPropertyValue
        {
            Id = SelectedItem.Id,
            Name = SelectedItem.Name,
            SectionCodeAbbreviation = SelectedItem.SectionCodeAbbreviation,
        };
        EditVm = new SectionPropertyEditViewModel(copy, isNew: false,
            onSave: v => { _repo.Update(v); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name, excludeId: copy.Id),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedItem is null) return;
        _repo.Delete(SelectedItem.Id);
        Load();
    }
}

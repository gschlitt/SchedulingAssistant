using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class LegalStartTimeListViewModel : ViewModelBase
{
    private readonly LegalStartTimeRepository _repo;

    [ObservableProperty] private ObservableCollection<LegalStartTime> _entries = new();
    [ObservableProperty] private LegalStartTime? _selectedEntry;
    [ObservableProperty] private LegalStartTimeEditViewModel? _editVm;

    public LegalStartTimeListViewModel(LegalStartTimeRepository repo)
    {
        _repo = repo;
        Load();
    }

    private void Load() =>
        Entries = new ObservableCollection<LegalStartTime>(_repo.GetAll());

    [RelayCommand]
    private void Add()
    {
        var entry = new LegalStartTime();
        EditVm = new LegalStartTimeEditViewModel(entry, isNew: true,
            onSave: e => { _repo.Insert(e); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedEntry is null) return;
        var copy = new LegalStartTime { BlockLength = SelectedEntry.BlockLength, StartTimes = new List<int>(SelectedEntry.StartTimes) };
        EditVm = new LegalStartTimeEditViewModel(copy, isNew: false,
            onSave: e => { _repo.Update(e); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry is null) return;
        _repo.Delete(SelectedEntry.BlockLength);
        Load();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SemesterListViewModel : ViewModelBase
{
    private readonly SemesterRepository _repo;

    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();
    [ObservableProperty] private Semester? _selectedSemester;
    [ObservableProperty] private SemesterEditViewModel? _editVm;

    public SemesterListViewModel(SemesterRepository repo)
    {
        _repo = repo;
        Load();
    }

    private void Load() =>
        Semesters = new ObservableCollection<Semester>(_repo.GetAll());

    [RelayCommand]
    private void Add()
    {
        var semester = new Semester();
        EditVm = new SemesterEditViewModel(semester, isNew: true,
            onSave: s => { _repo.Insert(s); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedSemester is null) return;
        var copy = new Semester { Id = SelectedSemester.Id, Name = SelectedSemester.Name, SortOrder = SelectedSemester.SortOrder };
        EditVm = new SemesterEditViewModel(copy, isNew: false,
            onSave: s => { _repo.Update(s); Load(); EditVm = null; },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedSemester is null) return;
        _repo.Delete(SelectedSemester.Id);
        Load();
    }
}

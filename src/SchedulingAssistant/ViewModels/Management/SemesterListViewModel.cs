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

    /// <summary>Set by the view. Called with an error message when an action fails.</summary>
    public Func<string, Task>? ShowError { get; set; }

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
            onSave: s =>
            {
                try { _repo.Insert(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SemesterListViewModel.Add"); ShowError?.Invoke("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedSemester is null) return;
        var copy = new Semester { Id = SelectedSemester.Id, Name = SelectedSemester.Name, SortOrder = SelectedSemester.SortOrder };
        EditVm = new SemesterEditViewModel(copy, isNew: false,
            onSave: s =>
            {
                try { _repo.Update(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SemesterListViewModel.Edit"); ShowError?.Invoke("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedSemester is null) return;
        try
        {
            _repo.Delete(SelectedSemester.Id);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SemesterListViewModel.Delete");
            if (ShowError is not null)
                await ShowError("The delete could not be completed. Please try again.");
        }
    }
}

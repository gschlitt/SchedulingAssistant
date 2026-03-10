using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SemesterListViewModel : ViewModelBase
{
    private readonly SemesterRepository _repo;
    private readonly IDialogService _dialog;

    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();
    [ObservableProperty] private Semester? _selectedSemester;
    [ObservableProperty] private SemesterEditViewModel? _editVm;

    public SemesterListViewModel(SemesterRepository repo, IDialogService dialog)
    {
        _repo = repo;
        _dialog = dialog;
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
                catch (Exception ex) { App.Logger.LogError(ex, "SemesterListViewModel.Add"); _ = _dialog.ShowError("The save could not be completed. Please try again."); }
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
                catch (Exception ex) { App.Logger.LogError(ex, "SemesterListViewModel.Edit"); _ = _dialog.ShowError("The save could not be completed. Please try again."); }
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
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }
}

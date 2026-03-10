using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SubjectListViewModel : ViewModelBase
{
    private readonly SubjectRepository _repo;
    private readonly IDialogService _dialog;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private SubjectEditViewModel? _editVm;

    public SubjectListViewModel(SubjectRepository repo, IDialogService dialog)
    {
        _repo = repo;
        _dialog = dialog;
        Load();
    }

    private void Load()
    {
        Subjects = new ObservableCollection<Subject>(_repo.GetAll());
        SelectedSubject = null;
    }

    [RelayCommand]
    private void Add()
    {
        var subject = new Subject();
        EditVm = new SubjectEditViewModel(subject, isNew: true,
            onSave: async s =>
            {
                try { _repo.Insert(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SubjectListViewModel.Add"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(name),
            abbreviationExists: abbr => _repo.ExistsByAbbreviation(abbr));
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedSubject is null) return;
        var clone = new Subject
        {
            Id = SelectedSubject.Id,
            Name = SelectedSubject.Name,
            CalendarAbbreviation = SelectedSubject.CalendarAbbreviation
        };
        EditVm = new SubjectEditViewModel(clone, isNew: false,
            onSave: async s =>
            {
                try { _repo.Update(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SubjectListViewModel.Edit"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(name, excludeId: clone.Id),
            abbreviationExists: abbr => _repo.ExistsByAbbreviation(abbr, excludeId: clone.Id));
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedSubject is null) return;

        if (_repo.HasCourses(SelectedSubject.Id))
        {
            await _dialog.ShowError($"Cannot delete \"{SelectedSubject.Name}\" — it has courses. Remove all courses from this subject first.");
            return;
        }

        _repo.Delete(SelectedSubject.Id);
        Load();
    }
}

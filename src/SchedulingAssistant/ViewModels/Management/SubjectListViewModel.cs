using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SubjectListViewModel : ViewModelBase
{
    private readonly SubjectRepository _repo;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private SubjectEditViewModel? _editVm;

    /// <summary>Set by the view. Called with an error message when an action is blocked.</summary>
    public Func<string, Task>? ShowError { get; set; }

    public SubjectListViewModel(SubjectRepository repo)
    {
        _repo = repo;
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
            onSave: s => { _repo.Insert(s); Load(); EditVm = null; },
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
            onSave: s => { _repo.Update(s); Load(); EditVm = null; },
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
            if (ShowError is not null)
                await ShowError($"Cannot delete \"{SelectedSubject.Name}\" — it has courses. Remove all courses from this subject first.");
            return;
        }

        _repo.Delete(SelectedSubject.Id);
        Load();
    }
}

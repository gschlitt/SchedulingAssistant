using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class InstructorListViewModel : ViewModelBase
{
    private readonly InstructorRepository _repo;

    [ObservableProperty] private ObservableCollection<Instructor> _instructors = new();
    [ObservableProperty] private Instructor? _selectedInstructor;
    [ObservableProperty] private InstructorEditViewModel? _editVm;

    /// <summary>Set by the view. Called with an error message when an action is blocked.</summary>
    public Func<string, Task>? ShowError { get; set; }

    public InstructorListViewModel(InstructorRepository repo)
    {
        _repo = repo;
        Load();
    }

    private void Load() =>
        Instructors = new ObservableCollection<Instructor>(_repo.GetAll());

    [RelayCommand]
    private void Add()
    {
        var instructor = new Instructor();
        EditVm = new InstructorEditViewModel(instructor, isNew: true,
            onSave: i => { _repo.Insert(i); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            initialsExist: initials => _repo.ExistsByInitials(initials));
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedInstructor is null) return;
        var s = SelectedInstructor;
        var copy = new Instructor { Id = s.Id, FirstName = s.FirstName, LastName = s.LastName, Initials = s.Initials, Email = s.Email, Department = s.Department, Notes = s.Notes };
        EditVm = new InstructorEditViewModel(copy, isNew: false,
            onSave: i => { _repo.Update(i); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            initialsExist: initials => _repo.ExistsByInitials(initials, excludeId: copy.Id));
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedInstructor is null) return;

        if (_repo.HasSections(SelectedInstructor.Id))
        {
            if (ShowError is not null)
                await ShowError($"Cannot delete {SelectedInstructor.Initials} ({SelectedInstructor.FirstName} {SelectedInstructor.LastName}) — they are assigned to sections in one or more semesters.");
            return;
        }

        _repo.Delete(SelectedInstructor.Id);
        Load();
    }
}

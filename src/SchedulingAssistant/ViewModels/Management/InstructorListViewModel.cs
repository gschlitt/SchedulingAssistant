using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class InstructorListViewModel : ViewModelBase
{
    private readonly InstructorRepository _repo;
    private readonly SectionPropertyRepository _propertyRepo;

    [ObservableProperty] private ObservableCollection<Instructor> _instructors = new();
    [ObservableProperty] private Instructor? _selectedInstructor;
    [ObservableProperty] private InstructorEditViewModel? _editVm;
    [ObservableProperty] private bool _showOnlyActive = true;

    /// <summary>Set by the view. Called with an error message when an action is blocked.</summary>
    public Func<string, Task>? ShowError { get; set; }

    public InstructorListViewModel(InstructorRepository repo, SectionPropertyRepository propertyRepo)
    {
        _repo = repo;
        _propertyRepo = propertyRepo;
        ShowOnlyActive = AppSettings.Load().ShowOnlyActiveInstructors;
        Load();
    }

    private void Load()
    {
        var all = _repo.GetAll();
        var filtered = ShowOnlyActive ? all.Where(i => i.IsActive).ToList() : all;
        Instructors = new ObservableCollection<Instructor>(filtered);
    }

    partial void OnShowOnlyActiveChanged(bool value)
    {
        Load();
        var settings = AppSettings.Load();
        settings.ShowOnlyActiveInstructors = value;
        settings.Save();
    }

    private IReadOnlyList<SectionPropertyValue> GetStaffTypes() =>
        _propertyRepo.GetAll(SectionPropertyTypes.StaffType);

    [RelayCommand]
    private void Add()
    {
        var instructor = new Instructor();
        EditVm = new InstructorEditViewModel(instructor, isNew: true,
            staffTypes: GetStaffTypes(),
            onSave: i => { _repo.Insert(i); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            initialsExist: initials => _repo.ExistsByInitials(initials));
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedInstructor is null) return;
        var s = SelectedInstructor;
        var copy = new Instructor
        {
            Id = s.Id,
            FirstName = s.FirstName,
            LastName = s.LastName,
            Initials = s.Initials,
            Email = s.Email,
            Notes = s.Notes,
            IsActive = s.IsActive,
            StaffTypeId = s.StaffTypeId,
        };
        EditVm = new InstructorEditViewModel(copy, isNew: false,
            staffTypes: GetStaffTypes(),
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

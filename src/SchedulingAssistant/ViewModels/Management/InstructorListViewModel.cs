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
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly SemesterContext _semesterContext;

    [ObservableProperty] private ObservableCollection<Instructor> _instructors = new();
    [ObservableProperty] private Instructor? _selectedInstructor;
    [ObservableProperty] private InstructorEditViewModel? _editVm;
    [ObservableProperty] private bool _showOnlyActive = true;
    [ObservableProperty] private InstructorWorkloadViewModel _workloadVm = new();
    [ObservableProperty] private ReleaseManagementViewModel _releaseVm;

    /// <summary>Set by the view. Called with an error message when an action is blocked.</summary>
    public Func<string, Task>? ShowError { get; set; }

    public InstructorListViewModel(
        InstructorRepository repo,
        SectionPropertyRepository propertyRepo,
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        ReleaseRepository releaseRepo,
        SemesterContext semesterContext)
    {
        _repo = repo;
        _propertyRepo = propertyRepo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _releaseRepo = releaseRepo;
        _semesterContext = semesterContext;
        _releaseVm = new ReleaseManagementViewModel(releaseRepo);

        ShowOnlyActive = AppSettings.Load().ShowOnlyActiveInstructors;
        Load();

        // Subscribe to semester changes to reload workload
        _semesterContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
                RefreshWorkload();
        };

        // Subscribe to release changes to refresh workload display
        _releaseVm.ReleasesChanged += RefreshWorkload;
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

    partial void OnSelectedInstructorChanged(Instructor? value)
    {
        RefreshWorkload();
    }

    private void RefreshWorkload()
    {
        if (SelectedInstructor is null || _semesterContext.SelectedSemesterDisplay is null)
        {
            WorkloadVm.Clear();
            ReleaseVm.SetContext(string.Empty, string.Empty);
            return;
        }

        var semesterId = _semesterContext.SelectedSemesterDisplay.Semester.Id;
        var instructorId = SelectedInstructor.Id;

        // Load assigned sections
        var allSections = _sectionRepo.GetAll(semesterId);
        var assignedSections = new List<AssignedSectionWorkload>();

        foreach (var section in allSections)
        {
            var assignment = section.InstructorAssignments.FirstOrDefault(a => a.InstructorId == instructorId);
            if (assignment is not null)
            {
                var course = section.CourseId is not null ? _courseRepo.GetById(section.CourseId) : null;
                var courseCode = course?.CalendarCode ?? "(unknown)";
                assignedSections.Add(new AssignedSectionWorkload
                {
                    CourseCode = courseCode,
                    SectionCode = section.SectionCode,
                    WorkloadValue = assignment.Workload ?? 0m
                });
            }
        }

        // Load releases
        var dbReleases = _releaseRepo.GetByInstructor(semesterId, instructorId);
        var releaseWorkloads = dbReleases
            .Select(r => new ReleaseWorkload { Id = r.Id, Title = r.Title, WorkloadValue = r.WorkloadValue })
            .ToList();

        WorkloadVm.LoadWorkload(assignedSections, releaseWorkloads);
        ReleaseVm.SetContext(instructorId, semesterId);
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

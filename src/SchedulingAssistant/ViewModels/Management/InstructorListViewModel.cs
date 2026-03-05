using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class InstructorListViewModel : ViewModelBase, IDisposable
{
    private readonly InstructorRepository _repo;
    private readonly SectionPropertyRepository _propertyRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _academicYearRepo;
    private readonly SemesterContext _semesterContext;

    [ObservableProperty] private ObservableCollection<Instructor> _instructors = new();
    [ObservableProperty] private Instructor? _selectedInstructor;
    [ObservableProperty] private InstructorEditViewModel? _editVm;
    [ObservableProperty] private bool _showOnlyActive = true;
    [ObservableProperty] private InstructorWorkloadViewModel _workloadVm = new();
    [ObservableProperty] private ReleaseManagementViewModel _releaseVm;
    [ObservableProperty] private CommitmentsManagementViewModel _commitmentsVm;
    [ObservableProperty] private WorkloadHistoryViewModel _workloadHistoryVm;

    private void OnWorkloadVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstructorWorkloadViewModel.TotalWorkload))
            OnPropertyChanged(nameof(WorkloadUnitsDisplay));
    }

    /// <summary>Set by the view. Called with an error message when an action is blocked.</summary>
    public Func<string, Task>? ShowError { get; set; }

    /// <summary>Set by the view. Called to show a confirmation dialog. Returns true if user confirmed.</summary>
    public Func<string, Task<bool>>? ShowConfirmation { get; set; }

    public InstructorListViewModel(
        InstructorRepository repo,
        SectionPropertyRepository propertyRepo,
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        ReleaseRepository releaseRepo,
        InstructorCommitmentRepository commitmentRepo,
        SemesterRepository semesterRepo,
        AcademicYearRepository academicYearRepo,
        SemesterContext semesterContext,
        SectionChangeNotifier changeNotifier)
    {
        _repo = repo;
        _propertyRepo = propertyRepo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _releaseRepo = releaseRepo;
        _commitmentRepo = commitmentRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _semesterContext = semesterContext;
        _releaseVm = new ReleaseManagementViewModel(releaseRepo);
        _commitmentsVm = new CommitmentsManagementViewModel(commitmentRepo, changeNotifier);
        _workloadHistoryVm = new WorkloadHistoryViewModel(sectionRepo, courseRepo, semesterRepo, academicYearRepo, releaseRepo);

        ShowOnlyActive = AppSettings.Load().ShowOnlyActiveInstructors;
        Load();

        _semesterContext.PropertyChanged += OnSemesterContextPropertyChanged;
        _releaseVm.ReleasesChanged += RefreshWorkload;
        _workloadVm.PropertyChanged += OnWorkloadVmPropertyChanged;
    }

    private void OnSemesterContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
            RefreshWorkload();
    }

    public void Dispose()
    {
        _semesterContext.PropertyChanged -= OnSemesterContextPropertyChanged;
        ReleaseVm.ReleasesChanged -= RefreshWorkload;
        _workloadVm.PropertyChanged -= OnWorkloadVmPropertyChanged;
    }

    private void Load()
    {
        var staffTypes = _propertyRepo.GetAll(SectionPropertyTypes.StaffType)
            .ToDictionary(s => s.Id, s => s.Name);

        var all = _repo.GetAll();
        foreach (var instructor in all)
            instructor.StaffTypeName = instructor.StaffTypeId is not null
                ? staffTypes.GetValueOrDefault(instructor.StaffTypeId)
                : null;

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

    // Avalonia evaluates bindings even on hidden elements, so binding directly to
    // SelectedInstructor.FirstName logs a null-traversal error when no instructor is selected.
    // These properties return empty string instead of null, avoiding the spurious binding error.
    public string SelectedFirstName => SelectedInstructor?.FirstName ?? string.Empty;
    public string SelectedLastName => SelectedInstructor?.LastName ?? string.Empty;

    public string WorkloadUnitsDisplay => $"Workload units {WorkloadVm.TotalWorkload:0.##}";

    partial void OnSelectedInstructorChanged(Instructor? value)
    {
        OnPropertyChanged(nameof(SelectedFirstName));
        OnPropertyChanged(nameof(SelectedLastName));
        RefreshWorkload();
    }

    private void RefreshWorkload()
    {
        if (SelectedInstructor is null || _semesterContext.SelectedSemesterDisplay is null)
        {
            WorkloadVm.Clear();
            ReleaseVm.SetContext(string.Empty, string.Empty);
            CommitmentsVm.SetContext(string.Empty, string.Empty);
            WorkloadHistoryVm.Clear();
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
        CommitmentsVm.SetContext(instructorId, semesterId);
        WorkloadHistoryVm.LoadHistory(instructorId);
    }

    private IReadOnlyList<SectionPropertyValue> GetStaffTypes() =>
        _propertyRepo.GetAll(SectionPropertyTypes.StaffType);

    [RelayCommand]
    private void Add()
    {
        var instructor = new Instructor();
        EditVm = new InstructorEditViewModel(instructor, isNew: true,
            staffTypes: GetStaffTypes(),
            onSave: i =>
            {
                try { _repo.Insert(i); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "InstructorListViewModel.Add"); ShowError?.Invoke("The save could not be completed. Please try again."); }
            },
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
            onSave: i =>
            {
                try { _repo.Update(i); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "InstructorListViewModel.Edit"); ShowError?.Invoke("The save could not be completed. Please try again."); }
            },
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
                await ShowError("The selected instructor has assigned workload and cannot be deleted. Consider deactivating the instructor instead");
            return;
        }

        var confirmed = ShowConfirmation is not null
            ? await ShowConfirmation($"Delete {SelectedInstructor.FirstName} {SelectedInstructor.LastName}?")
            : true;

        if (confirmed)
        {
            try
            {
                _repo.Delete(SelectedInstructor.Id);
                Load();
            }
            catch (Exception ex)
            {
                App.Logger.LogError(ex, "InstructorListViewModel.Delete");
                if (ShowError is not null)
                    await ShowError("The delete could not be completed. Please try again.");
            }
        }
    }
}

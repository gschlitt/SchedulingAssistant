using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels;

public partial class WorkloadPanelViewModel : ViewModelBase
{
    private readonly InstructorRepository _instructorRepo;
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly SemesterContext _semesterContext;
    private readonly SectionListViewModel _sectionListVm;

    [ObservableProperty] private ObservableCollection<WorkloadRowViewModel> _rows = new();
    [ObservableProperty] private string? _lastErrorMessage;
    [ObservableProperty] private string? _selectedSectionId;

    /// <summary>Fired when the user clicks a work item chip.</summary>
    public event Action<WorkloadItemViewModel>? ItemClicked;

    public WorkloadPanelViewModel(
        InstructorRepository instructorRepo,
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        ReleaseRepository releaseRepo,
        SemesterRepository semesterRepo,
        SemesterContext semesterContext,
        SectionListViewModel sectionListVm)
    {
        _instructorRepo = instructorRepo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _releaseRepo = releaseRepo;
        _semesterRepo = semesterRepo;
        _semesterContext = semesterContext;
        _sectionListVm = sectionListVm;

        // Reload on semester change
        _semesterContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
                Load();
        };

        // Reload when sections change (saves, deletes, etc.)
        _sectionListVm.SectionsChanged += Load;

        Load();
    }

    /// <summary>Public method to reload workload data (e.g., after release changes).</summary>
    public void Reload() => Load();

    private void Load()
    {
        LastErrorMessage = null;
        try
        {
            if (_semesterContext.SelectedSemesterDisplay is null)
            {
                Rows = new ObservableCollection<WorkloadRowViewModel>();
                return;
            }

            var selectedSemester = _semesterContext.SelectedSemesterDisplay.Semester;
            var semesterId = selectedSemester.Id;
            var academicYearId = selectedSemester.AcademicYearId;

            // Active instructors (repo returns them sorted by lastName, firstName)
            var instructors = _instructorRepo.GetAll().Where(i => i.IsActive).ToList();

            // All sections and releases for the selected semester
            var sections = _sectionRepo.GetAll(semesterId);
            var releases = _releaseRepo.GetBySemester(semesterId);

            // Course code cache to avoid repeated DB hits
            var courseCache = new Dictionary<string, string>();
            string GetCourseCode(string? courseId)
            {
                if (courseId is null) return "?";
                if (!courseCache.TryGetValue(courseId, out var code))
                {
                    code = _courseRepo.GetById(courseId)?.CalendarCode ?? "?";
                    courseCache[courseId] = code;
                }
                return code;
            }

            // Build one row per active instructor
            var rows = new List<WorkloadRowViewModel>();
            foreach (var instructor in instructors)
            {
                var items = new List<WorkloadItemViewModel>();

                // Sections first
                foreach (var section in sections)
                {
                    var assignment = section.InstructorAssignments
                        .FirstOrDefault(a => a.InstructorId == instructor.Id);
                    if (assignment is null) continue;

                    var workload = assignment.Workload ?? 1m;
                    items.Add(new WorkloadItemViewModel
                    {
                        Kind = WorkloadItemKind.Section,
                        Id = section.Id,
                        Label = $"{GetCourseCode(section.CourseId)} {section.SectionCode} [{workload:0.##}]",
                        WorkloadValue = workload,
                    });
                }

                // Releases after sections
                foreach (var release in releases.Where(r => r.InstructorId == instructor.Id))
                {
                    items.Add(new WorkloadItemViewModel
                    {
                        Kind = WorkloadItemKind.Release,
                        Id = release.Id,
                        Label = $"{release.Title} [{release.WorkloadValue:0.##}]",
                        WorkloadValue = release.WorkloadValue,
                    });
                }

                var name = string.IsNullOrEmpty(instructor.FirstName)
                    ? instructor.LastName
                    : $"{instructor.LastName}, {instructor.FirstName}";

                rows.Add(new WorkloadRowViewModel
                {
                    InstructorId = instructor.Id,
                    FullName = name,
                    Initials = instructor.Initials,
                    Items = new ObservableCollection<WorkloadItemViewModel>(items),
                });
            }

            // Academic year totals: sum workload across all semesters in the AY
            var aySemesters = _semesterRepo.GetByAcademicYear(academicYearId);
            var ayTotals = new Dictionary<string, decimal>();
            foreach (var semester in aySemesters)
            {
                foreach (var section in _sectionRepo.GetAll(semester.Id))
                    foreach (var assignment in section.InstructorAssignments)
                    {
                        ayTotals.TryGetValue(assignment.InstructorId, out var cur);
                        ayTotals[assignment.InstructorId] = cur + (assignment.Workload ?? 1m);
                    }

                foreach (var release in _releaseRepo.GetBySemester(semester.Id))
                {
                    ayTotals.TryGetValue(release.InstructorId, out var cur);
                    ayTotals[release.InstructorId] = cur + release.WorkloadValue;
                }
            }

            foreach (var row in rows)
                row.AcademicYearTotal = ayTotals.GetValueOrDefault(row.InstructorId, 0m);

            Rows = new ObservableCollection<WorkloadRowViewModel>(rows);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "WorkloadPanelViewModel.Load");
            LastErrorMessage = "Failed to load workload data.";
        }
    }

    [RelayCommand]
    private void HandleItemClick(WorkloadItemViewModel item) => ItemClicked?.Invoke(item);
}

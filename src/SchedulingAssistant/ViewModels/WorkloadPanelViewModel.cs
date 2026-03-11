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

    partial void OnSelectedSectionIdChanged(string? value) => UpdateItemSelection();

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
            var selectedSemesters = _semesterContext.SelectedSemesters.ToList();
            if (selectedSemesters.Count == 0)
            {
                Rows = new ObservableCollection<WorkloadRowViewModel>();
                return;
            }

            var isMultiSemester = _semesterContext.IsMultiSemesterMode;
            var academicYearId = selectedSemesters[0].Semester.AcademicYearId;

            // Active instructors (repo returns them sorted by lastName, firstName)
            var instructors = _instructorRepo.GetAll().Where(i => i.IsActive).ToList();

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

            if (!isMultiSemester)
            {
                // Single-semester mode: existing behavior
                var semesterDisplay = selectedSemesters[0];
                var sections = _sectionRepo.GetAll(semesterDisplay.Semester.Id);
                var releases = _releaseRepo.GetBySemester(semesterDisplay.Semester.Id);

                foreach (var instructor in instructors)
                {
                    var items = BuildItemsForInstructor(instructor, sections, releases, GetCourseCode);
                    var name = FormatInstructorName(instructor);

                    rows.Add(new WorkloadRowViewModel
                    {
                        InstructorId = instructor.Id,
                        FullName = name,
                        Initials = instructor.Initials,
                        IsMultiSemesterMode = false,
                        Items = new ObservableCollection<WorkloadItemViewModel>(items),
                        SemesterGroups = Array.Empty<WorkloadSemesterGroupViewModel>(),
                    });
                }
            }
            else
            {
                // Multi-semester mode: build groups per semester per instructor
                foreach (var instructor in instructors)
                {
                    var groups = new List<WorkloadSemesterGroupViewModel>();

                    // One group per selected semester (always, even if empty)
                    foreach (var semesterDisplay in selectedSemesters)
                    {
                        var sections = _sectionRepo.GetAll(semesterDisplay.Semester.Id);
                        var releases = _releaseRepo.GetBySemester(semesterDisplay.Semester.Id);
                        var items = BuildItemsForInstructor(instructor, sections, releases, GetCourseCode);

                        groups.Add(new WorkloadSemesterGroupViewModel
                        {
                            SemesterId = semesterDisplay.Semester.Id,
                            SemesterName = semesterDisplay.Semester.Name,
                            Items = new ObservableCollection<WorkloadItemViewModel>(items),
                            SemesterColorBrush = WorkloadSemesterGroupViewModel.ResolveBrush(semesterDisplay.Semester.Name),
                        });
                    }

                    var name = FormatInstructorName(instructor);

                    rows.Add(new WorkloadRowViewModel
                    {
                        InstructorId = instructor.Id,
                        FullName = name,
                        Initials = instructor.Initials,
                        IsMultiSemesterMode = true,
                        Items = new ObservableCollection<WorkloadItemViewModel>(),  // Empty in multi-semester mode
                        SemesterGroups = groups,
                    });
                }
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

    /// <summary>
    /// Builds the workload items (sections and releases) for a single instructor in a given set of sections and releases.
    /// </summary>
    private static List<WorkloadItemViewModel> BuildItemsForInstructor(
        Models.Instructor instructor,
        IEnumerable<Models.Section> sections,
        IEnumerable<Models.Release> releases,
        Func<string?, string> getCourseCode)
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
                Label = $"{getCourseCode(section.CourseId)} {section.SectionCode} [{workload:0.##}]",
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

        return items;
    }

    /// <summary>Formats an instructor's first and last name, handling empty first names.</summary>
    private static string FormatInstructorName(Models.Instructor instructor)
    {
        return string.IsNullOrEmpty(instructor.FirstName)
            ? instructor.LastName
            : $"{instructor.FirstName} {instructor.LastName}";
    }

    [RelayCommand]
    private void HandleItemClick(WorkloadItemViewModel item) => ItemClicked?.Invoke(item);

    /// <summary>
    /// Updates the IsSelected flag on all workload items (both in single-semester Items and in multi-semester SemesterGroups).
    /// Called whenever SelectedSectionId changes, to highlight the selected section or release across the view.
    /// </summary>
    private void UpdateItemSelection()
    {
        foreach (var row in Rows)
        {
            // Single-semester mode: items in row.Items
            foreach (var item in row.Items)
                item.IsSelected = item.Id == SelectedSectionId;

            // Multi-semester mode: items in each group
            foreach (var group in row.SemesterGroups)
                foreach (var item in group.Items)
                    item.IsSelected = item.Id == SelectedSectionId;
        }
    }
}

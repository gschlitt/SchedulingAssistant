using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels;

public partial class WorkloadPanelViewModel : ViewModelBase
{
    private readonly IInstructorRepository _instructorRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IReleaseRepository _releaseRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly SemesterContext _semesterContext;
    private readonly SectionStore _sectionStore;

    [ObservableProperty] private ObservableCollection<WorkloadRowViewModel> _rows = new();
    [ObservableProperty] private string? _lastErrorMessage;
    [ObservableProperty] private string? _selectedSectionId;

    partial void OnSelectedSectionIdChanged(string? value) => UpdateItemSelection();

    /// <summary>Fired when the user clicks a work item chip.</summary>
    public event Action<WorkloadItemViewModel>? ItemClicked;

    public WorkloadPanelViewModel(
        IInstructorRepository instructorRepo,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IReleaseRepository releaseRepo,
        ISemesterRepository semesterRepo,
        SemesterContext semesterContext,
        SectionStore sectionStore)
    {
        _instructorRepo = instructorRepo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _releaseRepo = releaseRepo;
        _semesterRepo = semesterRepo;
        _semesterContext = semesterContext;
        _sectionStore = sectionStore;

        // Reload whenever sections change or the semester selection changes.
        // SectionStore.SectionsChanged fires for both cases: SectionListViewModel
        // calls sectionStore.Reload() after saves/deletes and on semester change.
        _sectionStore.SectionsChanged += Load;

        // Keep SelectedSectionId in sync with the store's single source of truth.
        _sectionStore.SelectionChanged += id => SelectedSectionId = id;

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
                // Single-semester mode
                var semesterDisplay = selectedSemesters[0];
                // Read from the shared cache — no DB query for sections here.
                var sections = _sectionStore.SectionsBySemester.TryGetValue(semesterDisplay.Semester.Id, out var s)
                    ? (IEnumerable<Section>)s : Array.Empty<Section>();
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
                        // Read from the shared cache — no DB query for sections here.
                        var sections = _sectionStore.SectionsBySemester.TryGetValue(semesterDisplay.Semester.Id, out var s)
                            ? (IEnumerable<Section>)s : Array.Empty<Section>();
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

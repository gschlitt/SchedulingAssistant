using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public class WorkloadHistoryItemViewModel : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public ObservableCollection<WorkloadHistoryItemViewModel> Children { get; set; } = new();
    public bool IsYear { get; set; }
    public bool IsSemester { get; set; }
    public bool IsSection { get; set; }
    public bool IsRelease { get; set; }
    public decimal WorkloadValue { get; set; }
}

public partial class WorkloadHistoryViewModel : ViewModelBase
{
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly IAcademicYearRepository _academicYearRepo;
    private readonly IReleaseRepository _releaseRepo;

    [ObservableProperty] private ObservableCollection<WorkloadHistoryItemViewModel> _items = new();

    public WorkloadHistoryViewModel(
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        IReleaseRepository releaseRepo)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _releaseRepo = releaseRepo;
    }

    public void LoadHistory(string instructorId)
    {
        Items.Clear();

        if (string.IsNullOrEmpty(instructorId))
            return;

        // Get all sections
        var allSections = _sectionRepo.GetAll();

        // Find all sections where this instructor is assigned
        var instructorSections = allSections
            .Where(s => s.InstructorAssignments.Any(a => a.InstructorId == instructorId))
            .ToList();

        // Get all semesters (for releases too)
        var allSemesters = _semesterRepo.GetAll();
        var semesterIds = new HashSet<string>(instructorSections.Select(s => s.SemesterId));
        var releasesBySemester = new Dictionary<string, List<Release>>();

        // Also include semesters with releases
        foreach (var semester in allSemesters)
        {
            var releasesInSemester = _releaseRepo.GetByInstructor(semester.Id, instructorId);
            if (releasesInSemester.Any())
            {
                releasesBySemester[semester.Id] = releasesInSemester;
                semesterIds.Add(semester.Id);
            }
        }

        var semesters = allSemesters
            .Where(s => semesterIds.Contains(s.Id))
            .OrderByDescending(s => s.Id)
            .ToList();

        if (!semesters.Any())
            return;

        // Group by academic year ID, then resolve the AcademicYear objects
        var groupedById = new Dictionary<string, List<Semester>>();
        var academicYearLookup = new Dictionary<string, AcademicYear>();

        foreach (var semester in semesters)
        {
            var academicYear = _academicYearRepo.GetById(semester.AcademicYearId);
            if (academicYear == null) continue;

            if (!groupedById.ContainsKey(semester.AcademicYearId))
                groupedById[semester.AcademicYearId] = new();
            groupedById[semester.AcademicYearId].Add(semester);
            academicYearLookup[semester.AcademicYearId] = academicYear;
        }

        // Build tree structure
        foreach (var ayId in groupedById.Keys.OrderByDescending(id => academicYearLookup[id].Name))
        {
            var academicYear = academicYearLookup[ayId];
            var semestersInYear = groupedById[ayId];
            decimal yearTotal = 0m;
            var yearItem = new WorkloadHistoryItemViewModel
            {
                Id = academicYear.Id,
                IsYear = true
            };

            foreach (var semester in semestersInYear.OrderByDescending(s => s.Name))
            {
                var semesterItem = new WorkloadHistoryItemViewModel
                {
                    Id = semester.Id,
                    Display = semester.Name,
                    IsSemester = true
                };

                // Get sections for this instructor in this semester
                var sectionsInSemester = instructorSections
                    .Where(s => s.SemesterId == semester.Id)
                    .OrderBy(s => s.SectionCode)
                    .ToList();

                foreach (var section in sectionsInSemester)
                {
                    var assignment = section.InstructorAssignments.FirstOrDefault(a => a.InstructorId == instructorId);
                    var workload = assignment?.Workload ?? 0m;

                    var course = section.CourseId != null ? _courseRepo.GetById(section.CourseId) : null;
                    var courseCode = course?.CalendarCode ?? "(unknown)";

                    var sectionItem = new WorkloadHistoryItemViewModel
                    {
                        Id = section.Id,
                        Display = $"{courseCode} {section.SectionCode} ({workload:0.##})",
                        IsSection = true,
                        WorkloadValue = workload
                    };
                    semesterItem.Children.Add(sectionItem);
                    yearTotal += workload;
                }

                // Get releases for this instructor in this semester
                var releasesInSemester = releasesBySemester.ContainsKey(semester.Id)
                    ? releasesBySemester[semester.Id].OrderBy(r => r.Title).ToList()
                    : new List<Release>();

                foreach (var release in releasesInSemester)
                {
                    var releaseItem = new WorkloadHistoryItemViewModel
                    {
                        Id = release.Id,
                        Display = $"{release.Title} ({release.WorkloadValue:0.##})",
                        IsRelease = true,
                        WorkloadValue = release.WorkloadValue
                    };
                    semesterItem.Children.Add(releaseItem);
                    yearTotal += release.WorkloadValue;
                }

                yearItem.Children.Add(semesterItem);
            }

            // Update year display with total
            yearItem.Display = $"{academicYear.Name} ({yearTotal:0.##} credits)";
            yearItem.WorkloadValue = yearTotal;
            Items.Add(yearItem);
        }
    }

    public void Clear()
    {
        Items.Clear();
    }
}

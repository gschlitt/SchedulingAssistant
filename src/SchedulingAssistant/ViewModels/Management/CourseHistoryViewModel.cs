using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Loads and displays the historical sections of a course, organized by academic year and semester.
/// Hierarchical structure: Academic Year → Semester → Sections (with instructor names).
/// </summary>
public partial class CourseHistoryViewModel : ViewModelBase
{
    private readonly SectionRepository _sectionRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _academicYearRepo;
    private readonly InstructorRepository _instructorRepo;

    [ObservableProperty] private ObservableCollection<CourseHistoryItemViewModel> _items = new();

    public CourseHistoryViewModel(
        SectionRepository sectionRepo,
        SemesterRepository semesterRepo,
        AcademicYearRepository academicYearRepo,
        InstructorRepository instructorRepo)
    {
        _sectionRepo = sectionRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _instructorRepo = instructorRepo;
    }

    /// <summary>
    /// Loads all sections for the given course and organizes them hierarchically
    /// by academic year and semester, with instructor assignments.
    /// </summary>
    public void LoadByCourse(string courseId)
    {
        Items.Clear();

        var sections = _sectionRepo.GetByCourseId(courseId);
        if (sections.Count == 0)
            return;

        // Fetch all necessary reference data
        var allSemesters = _semesterRepo.GetAll();
        var allAcademicYears = _academicYearRepo.GetAll();
        var allInstructors = _instructorRepo.GetAll();

        // Build instructor lookup
        var instructorLookup = allInstructors.ToDictionary(i => i.Id);

        // Group sections by semester
        var sectionsBySemester = sections
            .GroupBy(s => s.SemesterId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group semesters by academic year
        var semestersByAcademicYear = allSemesters
            .Where(s => sectionsBySemester.ContainsKey(s.Id))  // Only years that have sections
            .GroupBy(s => s.AcademicYearId)
            .OrderByDescending(g =>
            {
                // Sort academic years by start year (descending, so newest first)
                var year = allAcademicYears.FirstOrDefault(ay => ay.Id == g.Key);
                return year?.StartYear ?? int.MinValue;
            })
            .ToList();

        // Build the hierarchical tree
        foreach (var ayGroup in semestersByAcademicYear)
        {
            var academicYear = allAcademicYears.FirstOrDefault(ay => ay.Id == ayGroup.Key);
            if (academicYear is null)
                continue;

            var yearItem = new CourseHistoryItemViewModel
            {
                Id = academicYear.Id,
                Display = academicYear.Name,
                IsYear = true
            };

            // Add semesters within this academic year
            var semestersInYear = ayGroup
                .OrderBy(s => s.SortOrder)
                .ToList();

            var sectionCountForYear = 0;

            foreach (var semester in semestersInYear)
            {
                if (!sectionsBySemester.TryGetValue(semester.Id, out var sectionsInSemester))
                    continue;

                var semesterItem = new CourseHistoryItemViewModel
                {
                    Id = semester.Id,
                    Display = semester.Name,
                    IsSemester = true
                };

                // Add sections within this semester
                foreach (var section in sectionsInSemester)
                {
                    var instructorNames = section.InstructorAssignments.Count > 0
                        ? string.Join(", ", section.InstructorAssignments
                            .Select(a =>
                            {
                                if (instructorLookup.TryGetValue(a.InstructorId, out var instr))
                                    return $"{instr.FirstName} {instr.LastName}";
                                return "(Unknown)";
                            }))
                        : "(Unassigned)";

                    var sectionDisplay = $"{section.SectionCode} ({instructorNames})";

                    var sectionItem = new CourseHistoryItemViewModel
                    {
                        Id = section.Id,
                        Display = sectionDisplay,
                        IsSection = true
                    };

                    semesterItem.Children.Add(sectionItem);
                    sectionCountForYear++;
                }

                yearItem.Children.Add(semesterItem);
            }

            // Set the section count for this academic year
            yearItem.SectionCount = sectionCountForYear;

            Items.Add(yearItem);
        }
    }
}

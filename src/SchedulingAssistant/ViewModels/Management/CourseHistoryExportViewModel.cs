using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Exports the teaching history of a single course as a CSV file.
/// One row per instructor assignment per section, grouped by academic year
/// (descending) and semester (by sort order), with blank rows between years.
/// </summary>
public partial class CourseHistoryExportViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Export flyout sidebar.</summary>
    public string DisplayName => "Course History";

    private readonly MainWindowViewModel _mainVm;
    private readonly ICourseRepository _courseRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly IAcademicYearRepository _academicYearRepo;
    private readonly IInstructorRepository _instructorRepo;

    [ObservableProperty]
    private ObservableCollection<Course> _courses = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCourseHistoryCommand))]
    private Course? _selectedCourse;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCourseHistoryCommand))]
    private bool _isExporting;

    public CourseHistoryExportViewModel(
        MainWindowViewModel mainVm,
        ICourseRepository courseRepo,
        ISectionRepository sectionRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        IInstructorRepository instructorRepo)
    {
        _mainVm = mainVm;
        _courseRepo = courseRepo;
        _sectionRepo = sectionRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _instructorRepo = instructorRepo;

        LoadCourses();
    }

    /// <summary>
    /// Populates the course picker with all courses, sorted by calendar code.
    /// </summary>
    private void LoadCourses()
    {
        var all = _courseRepo.GetAll();
        Courses = new ObservableCollection<Course>(all);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCourseHistory()
    {
        var window = _mainVm.MainWindowReference;
        if (window is null || SelectedCourse is null) return;

        IsExporting = true;
        StatusMessage = null;

        try
        {
            var course = SelectedCourse;

            var sections = _sectionRepo.GetByCourseId(course.Id);
            if (sections.Count == 0)
            {
                StatusMessage = "No sections found for this course.";
                return;
            }

            var allSemesters = _semesterRepo.GetAll();
            var allAcademicYears = _academicYearRepo.GetAll();
            var allInstructors = _instructorRepo.GetAll();
            var instructorLookup = allInstructors.ToDictionary(i => i.Id);

            // Group sections by semester
            var sectionsBySemester = sections
                .GroupBy(s => s.SemesterId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Group semesters by academic year, newest first
            var semestersByAy = allSemesters
                .Where(s => sectionsBySemester.ContainsKey(s.Id))
                .GroupBy(s => s.AcademicYearId)
                .OrderByDescending(g =>
                {
                    var year = allAcademicYears.FirstOrDefault(ay => ay.Id == g.Key);
                    return year?.StartYear ?? int.MinValue;
                })
                .ToList();

            // Build CSV
            var csv = new StringBuilder();
            csv.AppendLine(CsvLine(new[] { "Academic Year", "Semester", "Section Code", "Instructor", "Workload" }));

            var isFirstYear = true;

            foreach (var ayGroup in semestersByAy)
            {
                var academicYear = allAcademicYears.FirstOrDefault(ay => ay.Id == ayGroup.Key);
                if (academicYear is null) continue;

                // Blank separator row between academic years
                if (!isFirstYear)
                    csv.AppendLine();

                isFirstYear = false;

                var semestersInYear = ayGroup.OrderBy(s => s.SortOrder).ToList();

                foreach (var semester in semestersInYear)
                {
                    if (!sectionsBySemester.TryGetValue(semester.Id, out var sectionsInSemester))
                        continue;

                    foreach (var section in sectionsInSemester.OrderBy(s => s.SectionCode))
                    {
                        if (section.InstructorAssignments.Count == 0)
                        {
                            csv.AppendLine(CsvLine(new[]
                            {
                                academicYear.Name,
                                semester.Name,
                                section.SectionCode,
                                "(Unassigned)",
                                ""
                            }));
                        }
                        else
                        {
                            foreach (var assignment in section.InstructorAssignments)
                            {
                                var instructorName = instructorLookup.TryGetValue(assignment.InstructorId, out var instr)
                                    ? $"{instr.FirstName} {instr.LastName}"
                                    : "(Unknown)";

                                var workload = assignment.Workload?.ToString("G") ?? "";

                                csv.AppendLine(CsvLine(new[]
                                {
                                    academicYear.Name,
                                    semester.Name,
                                    section.SectionCode,
                                    instructorName,
                                    workload
                                }));
                            }
                        }
                    }
                }
            }

            // File picker
            var settings = AppSettings.Current;
            IStorageFolder? suggestedFolder = null;
            if (settings.LastCourseHistoryExportPath is not null)
            {
                var dir = Path.GetDirectoryName(settings.LastCourseHistoryExportPath);
                if (dir is not null)
                    suggestedFolder = await window.StorageProvider.TryGetFolderFromPathAsync(dir);
            }

            var suggestedFileName = $"{SanitizeFilename(course.CalendarCode)}-History.csv";

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Course History",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "csv",
                SuggestedStartLocation = suggestedFolder,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV File") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file is null) return;

            var path = file.Path.LocalPath;

            // Write CSV with UTF-8 BOM for Excel compatibility
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));

            settings.LastCourseHistoryExportPath = path;
            settings.Save();

            StatusMessage = $"Saved: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private bool CanExport() => !IsExporting && SelectedCourse is not null;

    /// <summary>
    /// Removes or replaces characters that are invalid in Windows filenames.
    /// </summary>
    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(filename
            .Where(c => !invalid.Contains(c))
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Export" : sanitized;
    }

    /// <summary>
    /// Formats a list of strings as a CSV line.
    /// Each field is wrapped in quotes; internal quotes are escaped by doubling.
    /// </summary>
    private static string CsvLine(IEnumerable<string> fields)
    {
        var quoted = fields.Select(f => $"\"{(f ?? "").Replace("\"", "\"\"")}\"");
        return string.Join(",", quoted);
    }
}

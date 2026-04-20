using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.Management;

public partial class WorkloadReportViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Export flyout sidebar.</summary>
    public string DisplayName => "Workload Report";

    private readonly MainWindowViewModel _mainVm;
    private readonly SemesterContext _semesterContext;
    private readonly IInstructorRepository _instructorRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly IReleaseRepository _releaseRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly AcademicUnitService _academicUnitService;
    private readonly ICourseRepository _courseRepo;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportWorkloadReportCommand))]
    private bool _isExporting;

    public WorkloadReportViewModel(
        MainWindowViewModel mainVm,
        SemesterContext semesterContext,
        IInstructorRepository instructorRepo,
        ISectionRepository sectionRepo,
        IReleaseRepository releaseRepo,
        ISemesterRepository semesterRepo,
        AcademicUnitService academicUnitService,
        ICourseRepository courseRepo)
    {
        _mainVm = mainVm;
        _semesterContext = semesterContext;
        _instructorRepo = instructorRepo;
        _sectionRepo = sectionRepo;
        _releaseRepo = releaseRepo;
        _semesterRepo = semesterRepo;
        _academicUnitService = academicUnitService;
        _courseRepo = courseRepo;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportWorkloadReport()
    {
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        IsExporting = true;
        StatusMessage = null;

        try
        {
            // Get academic unit name
            var unit = _academicUnitService.GetUnit();
            var unitName = SanitizeFilename(unit.Name);

            // Get selected academic year
            var selectedAy = _semesterContext.SelectedAcademicYear;
            if (selectedAy is null)
            {
                StatusMessage = "No academic year selected.";
                return;
            }

            var ayDisplay = SanitizeFilename(selectedAy.Name); // e.g. "2025-26"

            // Get first 5 semesters for this AY
            var semesters = _semesterRepo.GetByAcademicYear(selectedAy.Id).Take(5).ToList();
            while (semesters.Count < 5)
                semesters.Add(null);

            // Get active instructors, sorted by last name then first name
            var instructors = _instructorRepo.GetAll()
                .Where(i => i.IsActive)
                .OrderBy(i => i.LastName)
                .ThenBy(i => i.FirstName)
                .ToList();

            // Build course code cache to avoid repeated DB lookups
            var courseCodeCache = new Dictionary<string, string>();
            string GetCourseCode(string? courseId)
            {
                if (courseId is null) return "?";
                if (!courseCodeCache.TryGetValue(courseId, out var code))
                {
                    code = _courseRepo.GetById(courseId)?.CalendarCode ?? "?";
                    courseCodeCache[courseId] = code;
                }
                return code;
            }

            // Build CSV content
            var csv = new StringBuilder();

            // Header row
            var headerParts = new List<string> { "Instructor" };
            foreach (var sem in semesters)
            {
                headerParts.Add(sem?.Name ?? "");
            }
            headerParts.Add("Total");
            csv.AppendLine(CsvLine(headerParts));

            // Data rows
            foreach (var instructor in instructors)
            {
                var rowParts = new List<string> { $"{instructor.FirstName} {instructor.LastName}" };
                decimal rowTotal = 0;

                // For each semester column
                foreach (var semester in semesters)
                {
                    if (semester is null)
                    {
                        rowParts.Add("");
                        continue;
                    }

                    // Build cell content: list of items with their workloads
                    var cellLines = new List<string>();
                    decimal cellTotal = 0;

                    // Add sections
                    var sections = _sectionRepo.GetAll(semester.Id);
                    foreach (var section in sections)
                    {
                        var assignment = section.InstructorAssignments
                            .FirstOrDefault(a => a.InstructorId == instructor.Id);
                        if (assignment is null) continue;

                        var credit = assignment.Workload ?? 1m;
                        cellTotal += credit;
                        rowTotal += credit;

                        var creditStr = credit.ToString("G");
                        cellLines.Add($"{GetCourseCode(section.CourseId)} {section.SectionCode} ({creditStr})");
                    }

                    // Add releases
                    var releases = _releaseRepo.GetBySemester(semester.Id)
                        .Where(r => r.InstructorId == instructor.Id);
                    foreach (var release in releases)
                    {
                        cellTotal += release.WorkloadValue;
                        rowTotal += release.WorkloadValue;
                        cellLines.Add($"{release.Title} ({release.WorkloadValue.ToString("G")})");
                    }

                    // Join lines with newline and add to row
                    rowParts.Add(string.Join("\n", cellLines));
                }

                // Add total
                rowParts.Add(rowTotal.ToString("G"));
                csv.AppendLine(CsvLine(rowParts));
            }

            // File picker
            var settings = AppSettings.Current;

            IStorageFolder? suggestedFolder = null;
            if (settings.LastWorkloadReportPath is not null)
            {
                var dir = Path.GetDirectoryName(settings.LastWorkloadReportPath);
                if (dir is not null)
                    suggestedFolder = await window.StorageProvider.TryGetFolderFromPathAsync(dir);
            }

            var suggestedFileName = $"{unitName}-Workload-{ayDisplay}.csv";

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Workload Report",
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

            // Write CSV file with UTF-8 BOM for Excel
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));

            settings.LastWorkloadReportPath = path;
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

    private bool CanExport() => !IsExporting;

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

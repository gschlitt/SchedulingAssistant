using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.Services;

/// <summary>
/// Pairs a Semester with a formatted display label including its Academic Year name.
/// Shared across all ViewModels that need to present or filter by semester.
/// </summary>
public class SemesterDisplay
{
    public required Semester Semester { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Singleton service that holds the globally-selected semester.
/// ViewModels subscribe to PropertyChanged to react when the selection changes.
/// </summary>
public partial class SemesterContext : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<SemesterDisplay> _semesterDisplays = new();

    [ObservableProperty]
    private SemesterDisplay? _selectedSemesterDisplay;

    /// <summary>
    /// Reloads the available semesters from the database.
    /// Call at startup and after any Academic Year add/delete.
    /// Preserves the current selection if the same semester still exists.
    /// </summary>
    public void Reload(AcademicYearRepository ayRepo, SemesterRepository semRepo)
    {
        var previousId = SelectedSemesterDisplay?.Semester.Id;

        var years = ayRepo.GetAll().ToDictionary(y => y.Id, y => y.Name);
        var semesters = semRepo.GetAll();

        var displays = semesters.Select(s => new SemesterDisplay
        {
            Semester = s,
            DisplayName = years.TryGetValue(s.AcademicYearId, out var yearName)
                ? $"{yearName} — {s.Name}"
                : s.Name
        }).ToList();

        SemesterDisplays = new ObservableCollection<SemesterDisplay>(displays);

        // Restore previous selection if still available, otherwise pick first
        SelectedSemesterDisplay = displays.FirstOrDefault(d => d.Semester.Id == previousId)
                                  ?? displays.FirstOrDefault();
    }
}

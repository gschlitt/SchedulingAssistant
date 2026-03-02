using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.Services;

/// <summary>
/// Pairs a Semester with a formatted display label (just the semester name, e.g., "Fall").
/// Academic year is shown separately in the AcademicYears dropdown.
/// </summary>
public class SemesterDisplay
{
    public required Semester Semester { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Singleton service that holds the globally-selected academic year and semester.
/// Provides two-level selection: first choose a year, then filter semesters within that year.
/// ViewModels subscribe to SelectedSemesterDisplay changes to react when semester selection changes.
/// </summary>
public partial class SemesterContext : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<AcademicYear> _academicYears = new();

    [ObservableProperty]
    private AcademicYear? _selectedAcademicYear;

    [ObservableProperty]
    private ObservableCollection<SemesterDisplay> _filteredSemesters = new();

    [ObservableProperty]
    private SemesterDisplay? _selectedSemesterDisplay;

    /// <summary>
    /// Kept for backward compatibility (legacy code may reference it).
    /// Returns a flat list of all semesters with full "Year — Semester" display names.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SemesterDisplay> _semesterDisplays = new();

    private Dictionary<string, AcademicYear> _yearLookup = new();
    private Dictionary<string, List<SemesterDisplay>> _semestersByYear = new();

    /// <summary>
    /// Reloads academic years and semesters from the database.
    /// Call at startup and after any Academic Year add/delete.
    /// Preserves the current selection if the same semester still exists.
    /// </summary>
    public void Reload(AcademicYearRepository ayRepo, SemesterRepository semRepo)
    {
        var previousSemesterId = SelectedSemesterDisplay?.Semester.Id;
        var previousYearId = SelectedAcademicYear?.Id;

        // Load all academic years and semesters
        var allYears = ayRepo.GetAll().OrderBy(y => y.StartYear).ToList();
        var allSemesters = semRepo.GetAll();

        // Build lookup tables
        _yearLookup = allYears.ToDictionary(y => y.Id);
        _semestersByYear = new Dictionary<string, List<SemesterDisplay>>();

        // Build all semester displays (flat list with full names) for backward compat
        var allDisplays = new List<SemesterDisplay>();
        foreach (var semester in allSemesters)
        {
            var display = new SemesterDisplay
            {
                Semester = semester,
                DisplayName = _yearLookup.TryGetValue(semester.AcademicYearId, out var year)
                    ? $"{year.Name} — {semester.Name}"
                    : semester.Name
            };
            allDisplays.Add(display);

            // Also group by year
            if (!_semestersByYear.ContainsKey(semester.AcademicYearId))
                _semestersByYear[semester.AcademicYearId] = new();
            _semestersByYear[semester.AcademicYearId].Add(display);
        }

        SemesterDisplays = new ObservableCollection<SemesterDisplay>(allDisplays);
        AcademicYears = new ObservableCollection<AcademicYear>(allYears);

        // Restore previous year selection, or pick first year
        var newSelectedYear = allYears.FirstOrDefault(y => y.Id == previousYearId)
                              ?? allYears.FirstOrDefault();

        if (newSelectedYear != null)
        {
            SelectedAcademicYear = newSelectedYear;
            UpdateFilteredSemesters();

            // Restore previous semester selection if it's in this year, otherwise pick first
            var semestersInYear = _semestersByYear.TryGetValue(newSelectedYear.Id, out var list) ? list : new();
            SelectedSemesterDisplay = semestersInYear.FirstOrDefault(d => d.Semester.Id == previousSemesterId)
                                      ?? semestersInYear.FirstOrDefault();
        }
        else
        {
            SelectedAcademicYear = null;
            SelectedSemesterDisplay = null;
            FilteredSemesters = new ObservableCollection<SemesterDisplay>();
        }
    }

    /// <summary>
    /// Called when SelectedAcademicYear changes (via partial void).
    /// Updates FilteredSemesters to show only semesters for the selected year.
    /// Auto-selects the first semester in the new year.
    /// </summary>
    partial void OnSelectedAcademicYearChanged(AcademicYear? oldValue, AcademicYear? newValue)
    {
        UpdateFilteredSemesters();
    }

    /// <summary>
    /// Updates FilteredSemesters based on SelectedAcademicYear.
    /// Auto-selects first semester in the year (or null if no semesters).
    /// </summary>
    private void UpdateFilteredSemesters()
    {
        if (SelectedAcademicYear == null)
        {
            FilteredSemesters = new ObservableCollection<SemesterDisplay>();
            SelectedSemesterDisplay = null;
            return;
        }

        var filtered = _semestersByYear.TryGetValue(SelectedAcademicYear.Id, out var list)
            ? list
            : new List<SemesterDisplay>();

        FilteredSemesters = new ObservableCollection<SemesterDisplay>(filtered);

        // Auto-select first semester in the new year
        SelectedSemesterDisplay = filtered.FirstOrDefault();
    }
}

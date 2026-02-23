using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Checkbox + workload wrapper for instructor multi-select in the section editor.</summary>
public partial class InstructorSelectionViewModel : ObservableObject
{
    public Instructor Value { get; }
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Workload as a user-editable string (e.g. "1", "0.5", "1.5").
    /// Validated on save to a positive decimal with at most 1 decimal place.
    /// Defaults to "1" when the instructor is first selected.
    /// </summary>
    [ObservableProperty] private string _workloadText = "1";

    public string DisplayName => $"{Value.LastName}, {Value.FirstName}";

    /// <summary>Parsed workload value, or null if the text is invalid/empty.</summary>
    public decimal? ParsedWorkload =>
        decimal.TryParse(WorkloadText, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? Math.Round(v, 1)
            : null;

    public InstructorSelectionViewModel(Instructor instructor, bool isSelected, decimal? workload = null)
    {
        Value = instructor;
        _isSelected = isSelected;
        _workloadText = workload.HasValue ? workload.Value.ToString("0.#") : "1";
    }
}

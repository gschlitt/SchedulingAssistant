using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>Checkbox + workload wrapper for instructor multi-select in the section editor.</summary>
public partial class InstructorSelectionViewModel : ObservableObject
{
    public Instructor Value { get; }
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Workload as a user-editable string (e.g. "1", "0.5", "0.33", "0.47", "0").
    /// Validated on save to a non-negative decimal with up to 2 decimal places.
    /// Defaults to "1" when the instructor is first selected.
    /// </summary>
    [ObservableProperty] private string _workloadText = "1";

    /// <summary>
    /// True when the instructor is outside the subject focus (spec item 20): the editor shows the name in a lighter
    /// colour, under the "Other instructors" heading. Set at construction; purely presentational.
    /// </summary>
    public bool IsOutOfFocus { get; }

    /// <summary>
    /// True on exactly one row, the first out-of-focus instructor that follows at least one in-focus one: the row
    /// that carries the "Other instructors" heading and rule above it. Set at construction.
    /// </summary>
    public bool ShowOthersHeading { get; }

    public string DisplayName => $"{Value.FirstName} {Value.LastName}";

    /// <summary>Parsed workload value, or null if the text is invalid/empty. Zero is a valid, explicit workload.</summary>
    public decimal? ParsedWorkload =>
        decimal.TryParse(WorkloadText, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
            ? Math.Round(v, 2)
            : null;

    /// <summary>Creates a row for the instructor chooser.</summary>
    /// <param name="instructor">The instructor.</param>
    /// <param name="isSelected">Whether the section currently has this instructor.</param>
    /// <param name="workload">The current workload, if any; null shows the default "1".</param>
    /// <param name="isOutOfFocus">Whether to dim the name (outside the subject focus).</param>
    /// <param name="showOthersHeading">Whether this row carries the "Other instructors" heading.</param>
    public InstructorSelectionViewModel(
        Instructor instructor, bool isSelected, decimal? workload = null,
        bool isOutOfFocus = false, bool showOthersHeading = false)
    {
        Value = instructor;
        IsOutOfFocus = isOutOfFocus;
        ShowOthersHeading = showOthersHeading;
        _isSelected = isSelected;
        _workloadText = workload.HasValue ? workload.Value.ToString("0.##") : "1";
    }
}

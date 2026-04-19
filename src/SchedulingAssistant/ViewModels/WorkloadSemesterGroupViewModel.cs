using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Represents one semester's workload items (sections and releases) within a
/// <see cref="WorkloadRowViewModel"/> when the view is in multi-semester mode.
/// Non-observable: rows are fully rebuilt on every load.
/// </summary>
public class WorkloadSemesterGroupViewModel
{
    /// <summary>Database ID of the semester.</summary>
    public required string SemesterId { get; init; }

    /// <summary>Display name of the semester, e.g., "Fall 2025".</summary>
    public required string SemesterName { get; init; }

    /// <summary>
    /// Stored hex color for the semester (e.g. "#C65D1E"), or empty for name-based fallback.
    /// The view resolves the actual brush via <c>SemesterBackgroundBrushConverter</c>.
    /// </summary>
    public string SemesterColor { get; init; } = string.Empty;

    /// <summary>Sections and releases for this instructor in this semester.</summary>
    public ObservableCollection<WorkloadItemViewModel> Items { get; init; } = new();

    /// <summary>Sum of all workload values for this semester.</summary>
    public decimal SemesterTotal => Items.Sum(i => i.WorkloadValue);
}

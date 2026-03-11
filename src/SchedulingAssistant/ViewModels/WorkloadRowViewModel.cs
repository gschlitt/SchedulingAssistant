using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels;

public class WorkloadRowViewModel
{
    public required string InstructorId { get; init; }
    public required string FullName { get; init; }
    public required string Initials { get; init; }

    /// <summary>
    /// Sections and releases for this instructor in the current semester(s).
    /// In single-semester mode, this contains all items for that semester.
    /// In multi-semester mode, this is empty; items live in SemesterGroups instead.
    /// </summary>
    public ObservableCollection<WorkloadItemViewModel> Items { get; init; } = new();

    /// <summary>Computed sum of workload values. In single-semester mode, reflects that semester's total.</summary>
    public decimal SemesterTotal => Items.Sum(i => i.WorkloadValue);

    /// <summary>Academic year total workload across all semesters in the year.</summary>
    public decimal AcademicYearTotal { get; set; }

    /// <summary>
    /// True when the workload panel is in multi-semester mode (multiple semesters selected).
    /// Drives the visibility of single-semester vs. multi-semester layouts in the view.
    /// </summary>
    public bool IsMultiSemesterMode { get; init; }

    /// <summary>
    /// In multi-semester mode, one group per selected semester (always present, even if empty).
    /// In single-semester mode, this is empty.
    /// </summary>
    public IReadOnlyList<WorkloadSemesterGroupViewModel> SemesterGroups { get; init; } = Array.Empty<WorkloadSemesterGroupViewModel>();
}

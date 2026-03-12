using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Represents a hierarchical item in the course history tree.
/// Three levels: Academic Year (IsYear), Semester (IsSemester), Section (IsSection).
/// </summary>
public partial class CourseHistoryItemViewModel : ObservableObject
{
    /// <summary>The unique identifier (course ID, semester ID, or section ID depending on level).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display text for this item (e.g., "2024–2025", "Fall 2024", "AB1 (Smith, Jones)").</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>Child items in the hierarchy.</summary>
    public ObservableCollection<CourseHistoryItemViewModel> Children { get; set; } = new();

    /// <summary>True if this item represents an academic year level.</summary>
    public bool IsYear { get; set; }

    /// <summary>True if this item represents a semester level.</summary>
    public bool IsSemester { get; set; }

    /// <summary>True if this item represents a section (leaf node).</summary>
    public bool IsSection { get; set; }

    /// <summary>Total section count for academic year items (displayed in header as "Year (N sections)").</summary>
    public int SectionCount { get; set; }
}

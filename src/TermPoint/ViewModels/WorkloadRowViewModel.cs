using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels;

public partial class WorkloadRowViewModel : ObservableObject
{
    public required string InstructorId { get; init; }
    public required string FullName { get; init; }
    public required string Initials { get; init; }

    /// <summary>
    /// Scheduling note text for this instructor in the selected semester (single-semester mode).
    /// Empty when no note exists.
    /// </summary>
    public string NoteText { get; init; } = string.Empty;

    /// <summary>True when a non-empty scheduling note exists; drives the note-icon's visibility.</summary>
    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);

    /// <summary>Whether the inline read-only note area is expanded. Toggled by the note icon.</summary>
    [ObservableProperty] private bool _isNoteExpanded;

    /// <summary>True when this instructor has at least one scheduling conflict in the loaded semester(s).</summary>
    [ObservableProperty] private bool _hasConflict;

    /// <summary>Human-readable description of the instructor's scheduling conflicts, shown as a tooltip.</summary>
    [ObservableProperty] private string? _conflictTooltip;

    /// <summary>Displays the instructor's full name followed by their initials in brackets, e.g. "Jim Bertrand (JB)".</summary>
    public string NameWithInitials => string.IsNullOrWhiteSpace(Initials) ? FullName : $"{FullName} ({Initials})";

    /// <summary>
    /// All section IDs assigned to this instructor across all loaded semesters.
    /// Spans both <see cref="Items"/> (single-semester) and <see cref="SemesterGroups"/> (multi-semester).
    /// Used by the name-click command to multi-select all of their sections at once.
    /// </summary>
    public IReadOnlyList<string> SectionIds =>
        Items.Where(i => i.Kind == WorkloadItemKind.Section).Select(i => i.Id)
             .Concat(SemesterGroups.SelectMany(g => g.Items)
                                   .Where(i => i.Kind == WorkloadItemKind.Section)
                                   .Select(i => i.Id))
             .Distinct()
             .ToList();

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

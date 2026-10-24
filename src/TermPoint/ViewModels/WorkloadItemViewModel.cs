using CommunityToolkit.Mvvm.ComponentModel;

namespace TermPoint.ViewModels;

public enum WorkloadItemKind { Section, Release }

public partial class WorkloadItemViewModel : ObservableObject
{
    public WorkloadItemKind Kind { get; init; }
    public required string Id { get; init; }
    public required string Label { get; init; }
    public decimal WorkloadValue { get; init; }
    public bool IsRelease => Kind == WorkloadItemKind.Release;

    /// <summary>
    /// True for a section outside the chosen subjects (spec item 20): still listed and still counted in the
    /// totals, but shown in the lighter out-of-focus colour. Always false for releases and under All. Set once,
    /// when the item is built.
    /// </summary>
    public bool IsOutOfFocus { get; init; }

    [ObservableProperty] private bool _isSelected;
}

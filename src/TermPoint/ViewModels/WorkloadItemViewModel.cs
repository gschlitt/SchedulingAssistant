using CommunityToolkit.Mvvm.ComponentModel;

namespace TermPoint.ViewModels;

public enum WorkloadItemKind { Section, Release, SharedSection }

public partial class WorkloadItemViewModel : ObservableObject
{
    public WorkloadItemKind Kind { get; init; }
    public required string Id { get; init; }
    public required string Label { get; init; }
    public decimal WorkloadValue { get; init; }
    public bool IsRelease => Kind == WorkloadItemKind.Release;
    public bool IsShared => Kind == WorkloadItemKind.SharedSection;

    [ObservableProperty] private bool _isSelected;
}

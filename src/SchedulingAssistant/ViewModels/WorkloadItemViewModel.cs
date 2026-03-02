using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels;

public enum WorkloadItemKind { Section, Release }

public partial class WorkloadItemViewModel : ObservableObject
{
    public WorkloadItemKind Kind { get; init; }
    public required string Id { get; init; }
    public required string Label { get; init; }
    public decimal WorkloadValue { get; init; }
    public bool IsRelease => Kind == WorkloadItemKind.Release;

    [ObservableProperty] private bool _isSelected;
}

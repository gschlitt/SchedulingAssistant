using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Represents a single assigned section's workload contribution.
/// </summary>
public class AssignedSectionWorkload
{
    public required string CourseCode { get; init; }
    public required string SectionCode { get; init; }
    public required decimal WorkloadValue { get; init; }
    public string Display => $"{CourseCode} {SectionCode}";
}

/// <summary>
/// Represents a single release's workload contribution.
/// </summary>
public class ReleaseWorkload
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required decimal WorkloadValue { get; init; }
}

/// <summary>
/// Displays an instructor's total workload for a semester:
/// sections assigned to them + releases they have.
/// </summary>
public partial class InstructorWorkloadViewModel : ViewModelBase
{
    [ObservableProperty] private ObservableCollection<AssignedSectionWorkload> _assignedSections = new();
    [ObservableProperty] private ObservableCollection<ReleaseWorkload> _releases = new();
    [ObservableProperty] private decimal _totalWorkload;

    public void LoadWorkload(List<AssignedSectionWorkload> sections, List<ReleaseWorkload> releases)
    {
        AssignedSections = new ObservableCollection<AssignedSectionWorkload>(sections);
        Releases = new ObservableCollection<ReleaseWorkload>(releases);
        TotalWorkload = sections.Sum(s => s.WorkloadValue) + releases.Sum(r => r.WorkloadValue);
    }

    public void Clear()
    {
        AssignedSections.Clear();
        Releases.Clear();
        TotalWorkload = 0;
    }
}

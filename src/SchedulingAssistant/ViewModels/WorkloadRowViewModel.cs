using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels;

public class WorkloadRowViewModel
{
    public required string InstructorId { get; init; }
    public required string FullName { get; init; }
    public required string Initials { get; init; }
    public ObservableCollection<WorkloadItemViewModel> Items { get; init; } = new();
    public decimal SemesterTotal => Items.Sum(i => i.WorkloadValue);
    public decimal AcademicYearTotal { get; set; }
}

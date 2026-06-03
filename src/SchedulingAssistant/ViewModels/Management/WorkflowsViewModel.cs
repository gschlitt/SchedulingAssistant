namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Workflows flyout. The cards themselves are authored directly in
/// AXAML (<see cref="Views.Management.WorkflowsView"/>) as
/// <see cref="Controls.WorkflowCardChrome"/> instances, so this VM holds no data — it
/// exists only as the flyout's page type for the navigation plumbing.
/// </summary>
public sealed class WorkflowsViewModel : ViewModelBase
{
}

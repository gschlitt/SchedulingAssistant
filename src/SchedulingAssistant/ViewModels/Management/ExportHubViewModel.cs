using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Export flyout hub.
/// Hosts a left-sidebar list of export categories; selecting one displays
/// its view in the right panel via the ViewLocator.
/// </summary>
public partial class ExportHubViewModel : ViewModelBase, IDismissableEditor
{
    /// <summary>Ordered list of export category ViewModels shown in the left sidebar.</summary>
    public ObservableCollection<ViewModelBase> Categories { get; }

    [ObservableProperty]
    private ViewModelBase? _selectedCategory;

    /// <param name="exportVm">PNG export category.</param>
    /// <param name="workloadReportVm">Workload report category.</param>
    /// <param name="workloadMailerVm">Workload mailer category.</param>
    /// <param name="courseHistoryExportVm">Course history CSV export category.</param>
    public ExportHubViewModel(
        ExportViewModel exportVm,
        WorkloadReportViewModel workloadReportVm,
        WorkloadMailerViewModel workloadMailerVm,
        CourseHistoryExportViewModel courseHistoryExportVm)
    {
        Categories = new ObservableCollection<ViewModelBase>
        {
            exportVm,
            workloadReportVm,
            workloadMailerVm,
            courseHistoryExportVm,
        };

        SelectedCategory = Categories[0];
    }

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        return SelectedCategory is IDismissableEditor editor && editor.DismissActiveEditor();
    }
}

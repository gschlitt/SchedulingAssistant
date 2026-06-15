using Avalonia.Controls;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.Views.Management;

public partial class SemesterManagerView : UserControl
{
    public SemesterManagerView()
    {
        InitializeComponent();

        if (!PlatformCapabilities.IsDesktop)
            SemesterGrid.Columns[1].IsVisible = false;
    }
}

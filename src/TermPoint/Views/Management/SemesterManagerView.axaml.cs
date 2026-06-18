using Avalonia.Controls;
using TermPoint.Services;

namespace TermPoint.Views.Management;

public partial class SemesterManagerView : UserControl
{
    public SemesterManagerView()
    {
        InitializeComponent();

        if (!PlatformCapabilities.IsDesktop)
            SemesterGrid.Columns[1].IsVisible = false;
    }
}

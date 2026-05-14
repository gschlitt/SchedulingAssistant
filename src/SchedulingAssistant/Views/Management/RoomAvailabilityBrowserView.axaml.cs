using Avalonia.Controls;
using Avalonia.Input;

namespace SchedulingAssistant.Views.Management;

public partial class RoomAvailabilityBrowserView : UserControl
{
    public RoomAvailabilityBrowserView()
    {
        InitializeComponent();

        // Prevent rapid clicks on Next/Previous from bubbling as DoubleTapped
        // to the parent ListBox, which would trigger the "already editing" guard.
        DoubleTapped += (_, e) => e.Handled = true;
    }
}

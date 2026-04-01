using Avalonia.Controls;

namespace SchedulingAssistant.Views.Management;

/// <summary>
/// Code-behind for <see cref="MeetingListView"/>. No logic required here;
/// the view is fully declarative and relies entirely on <see cref="ViewModels.Management.MeetingListViewModel"/>.
/// </summary>
public partial class MeetingListView : UserControl
{
    public MeetingListView()
    {
        InitializeComponent();
    }
}

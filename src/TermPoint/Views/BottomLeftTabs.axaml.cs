using Avalonia.Controls;

namespace TermPoint.Views;

/// <summary>
/// The Workload / Access tabs in the bottom-left panels' headers. Behaviour lives in
/// <see cref="TermPoint.ViewModels.MainWindowViewModel"/>; there is no code-behind logic.
/// </summary>
public partial class BottomLeftTabs : UserControl
{
    /// <summary>Creates the tab strip.</summary>
    public BottomLeftTabs()
    {
        InitializeComponent();
    }
}

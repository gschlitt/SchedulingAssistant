using Avalonia.Controls;

namespace TermPoint.Views;

/// <summary>
/// The Access tab in the bottom-left panel (Access Analyzer, D17). All behaviour is in
/// <see cref="TermPoint.ViewModels.Access.AccessTabViewModel"/>; there is no code-behind logic.
/// </summary>
public partial class AccessTabView : UserControl
{
    /// <summary>Creates the view.</summary>
    public AccessTabView()
    {
        InitializeComponent();
    }
}

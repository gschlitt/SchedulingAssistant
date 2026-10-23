using Avalonia.Controls;

namespace TermPoint.Controls;

/// <summary>
/// The "Focus" button plus the picker popup it opens (spec item 20). All behaviour lives in
/// <see cref="ViewModels.FocusViewModel"/>, which is this control's DataContext.
/// </summary>
public partial class FocusButton : UserControl
{
    /// <summary>Creates the control and loads its AXAML.</summary>
    public FocusButton()
    {
        InitializeComponent();
    }
}

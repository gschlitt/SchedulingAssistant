using Avalonia.Controls;

namespace TermPoint.Controls;

/// <summary>
/// The reusable subject-focus statement ("Focus: BIOL, CHEM") with its "Show all" link (spec item 20). All behaviour lives in
/// <see cref="ViewModels.FocusViewModel"/>, which is this control's DataContext.
/// </summary>
public partial class FocusIndicator : UserControl
{
    /// <summary>Creates the control and loads its AXAML.</summary>
    public FocusIndicator()
    {
        InitializeComponent();
    }
}

using Avalonia.Controls;

namespace TermPoint.Controls;

/// <summary>
/// The reusable subject-focus statement ("Subjects:" + one chip per subject, capped with "+N")
/// All behaviour lives in
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

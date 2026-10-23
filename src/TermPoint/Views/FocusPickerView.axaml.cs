using Avalonia.Controls;

namespace TermPoint.Views;

/// <summary>
/// The subject checklist shown in the Focus popup (spec item 20). All behaviour lives in
/// <see cref="ViewModels.FocusViewModel"/>, which is this control's DataContext.
/// </summary>
public partial class FocusPickerView : UserControl
{
    /// <summary>Creates the view and loads its AXAML.</summary>
    public FocusPickerView()
    {
        InitializeComponent();
    }
}

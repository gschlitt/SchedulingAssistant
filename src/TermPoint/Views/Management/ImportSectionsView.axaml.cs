using Avalonia.Controls;

namespace TermPoint.Views.Management;

/// <summary>
/// The Import Sections flyout (Files, Import Sections…). All behaviour lives in
/// <c>ImportSectionsViewModel</c>; the view is bindings only.
/// </summary>
public partial class ImportSectionsView : UserControl
{
    /// <summary>Creates the view and loads its AXAML.</summary>
    public ImportSectionsView()
    {
        InitializeComponent();
    }
}

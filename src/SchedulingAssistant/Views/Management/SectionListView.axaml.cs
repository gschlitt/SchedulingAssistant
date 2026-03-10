using Avalonia;
using Avalonia.Controls;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.ComponentModel;

namespace SchedulingAssistant.Views.Management;

public partial class SectionListView : UserControl
{
    private SectionListViewModel? _vm;

    public SectionListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Measure content width after the control is first attached to the visual tree,
        // so the left panel column is sized to fit the section cards at startup.
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateColumnWidth);

        // Note: DoubleTapped (open inline editor) and LostFocus forwarding (commit section
        // code) are handled declaratively via DoubleTapCommandBehavior and
        // LostFocusForwardBehavior attached to the AXAML elements. No code-behind needed.
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as SectionListViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-measure the content width when the section list changes, in case new cards
        // are wider than the current column. Deferred to the next layout pass.
        if (e.PropertyName == nameof(SectionListViewModel.SectionItems))
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateColumnWidth);
    }

    /// <summary>
    /// Measures the desired (unconstrained) width of the section list's content stack,
    /// then widens ThreePanelGrid's left column if the content would be clipped.
    /// Only runs when the editor is not open (ConditionalColumnWidthBehavior owns the
    /// column width while editing). A 20px hysteresis threshold avoids constant small
    /// adjustments as items load.
    /// </summary>
    private void UpdateColumnWidth()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("ListScrollViewer");
        var stackPanel = scrollViewer?.Content as StackPanel;

        if (stackPanel is null) return;

        // Force an unconstrained layout pass to get the content's natural desired width.
        stackPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = stackPanel.DesiredSize.Width;

        // Add a safety margin for margins and the scrollbar track.
        var requiredWidth = Math.Ceiling(desiredWidth) + 12;

        var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
        if (mainWindow is null) return;

        var threePanelGrid = mainWindow.FindControl<Grid>("ThreePanelGrid");
        if (threePanelGrid is null) return;

        // Do not adjust while the editor is open; ConditionalColumnWidthBehavior
        // owns the column width in that state.
        if (_vm?.IsEditing ?? false) return;

        var currentWidth = threePanelGrid.ColumnDefinitions[0].Width.Value;
        if (requiredWidth > currentWidth + 20)
            threePanelGrid.ColumnDefinitions[0].Width = new GridLength(requiredWidth, GridUnitType.Pixel);
    }
}

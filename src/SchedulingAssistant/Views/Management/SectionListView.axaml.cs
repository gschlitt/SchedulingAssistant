using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

        // Handle keyboard shortcuts: Ctrl+S to save, Esc to cancel
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

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

    /// <summary>
    /// Responds to property changes on the ViewModel that require the view to take action.
    /// Two cases are handled here rather than in the ViewModel because both are purely visual
    /// concerns — they require direct access to named controls or Avalonia layout APIs that
    /// a ViewModel must not know about:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>SectionItems changed</b> — re-measures the content stack to widen the left panel
    ///     column if new cards are wider than the current allocation.
    ///   </description></item>
    ///   <item><description>
    ///     <b>EditVm changed</b> — re-scrolls the selected item into view after the inline
    ///     editor opens or closes. See <see cref="ScrollSelectedItemIntoView"/> for the full
    ///     rationale.
    ///   </description></item>
    /// </list>
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-measure the content width when the section list changes, in case new cards
        // are wider than the current column. Deferred to the next layout pass.
        if (e.PropertyName == nameof(SectionListViewModel.SectionItems))
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateColumnWidth);

        // When the inline editor opens (e.g. via double-click from the Schedule Grid),
        // the expanding item changes the list layout AFTER any BringIntoView call that
        // was queued by the selection change. The item may therefore end up outside the
        // visible area. Re-scroll to the selected item after the layout has settled.
        if (e.PropertyName == nameof(SectionListViewModel.EditVm))
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                ScrollSelectedItemIntoView,
                Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Brings the active editor item into the visible area of the scroll viewer.
    /// Called after the inline editor opens or closes, since the layout shift caused by
    /// expanding/collapsing the editor form can push the item out of the viewport.
    /// Uses BringIntoView() which bubbles up to the outer ScrollViewer automatically.
    /// For existing sections the list item is selected; for new Add/Copy placeholders it is
    /// not (SelectedItem is null), so we fall back to the VM's ExpandedItem.
    /// </summary>
    private void ScrollSelectedItemIntoView()
    {
        var listBox = this.FindControl<ListBox>("SectionListBox");
        if (listBox is null) return;

        // For existing sections the item is selected; for new Add/Copy placeholders it is not —
        // fall back to the VM's ExpandedItem so the editor scrolls into view either way.
        var target = listBox.SelectedItem ?? _vm?.ExpandedItem;
        if (target is null) return;

        var container = listBox.ContainerFromItem(target) as Control;
        container?.BringIntoView();
    }

    /// <summary>
    /// Measures the desired (unconstrained) width of the section list's content stack,
    /// then widens ThreePanelGrid's left column if the content would be clipped.

    /// Only runs when the editor is not open (ConditionalColumnWidthBehavior owns the
    /// column width while editing). A 20px hysteresis threshold avoids constant small
    /// adjustments as items load.
    /// 
    /// Cards are temporarily reset to <c>NaN</c> width before
    /// measuring so they report their natural content width rather than their previously
    /// assigned uniform width.
    /// </summary>
    private void UpdateColumnWidth()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("ListScrollViewer");
        var stackPanel = scrollViewer?.Content as StackPanel;
        var listBox = this.FindControl<ListBox>("SectionListBox");

        if (stackPanel is null) return;

        // Do not adjust while the editor is open; ConditionalColumnWidthBehavior
        // owns the column width in that state.
        if (_vm?.IsEditing ?? false) return;

        var mainWindow = TopLevel.GetTopLevel(this) as TopLevel;
        if (mainWindow is null) return;

        // Reset uniform card width to NaN so cards report their natural content width
        // during the unconstrained measure pass below, not their previously assigned width.
        //if (_vm is not null)
        //    _vm.UniformCardWidth = double.NaN;

        // Force an unconstrained layout pass to get the content's natural desired width.
        stackPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = stackPanel.DesiredSize.Width;

        // desiredWidth includes the ListBox's own horizontal margins (e.g. Margin="5,0" → +10px).
        // Cards live inside the ListBox, so subtract those margins so the card width
        // matches the natural content width rather than being inflated by the ListBox gutter.
        //var lbMargin = listBox?.Margin ?? new Thickness(0);
        //if (_vm is not null)
         //   _vm.UniformCardWidth = desiredWidth - lbMargin.Left - lbMargin.Right;

        // Column width still uses the full desiredWidth (which includes the ListBox margins)
        // plus a safety margin for the scrollbar track.
        var requiredWidth = Math.Ceiling(desiredWidth) + 12;
        
        var threePanelGrid = mainWindow.FindControl<Grid>("ThreePanelGrid");
        if (threePanelGrid is null) return;
     
        var currentWidth = threePanelGrid.ColumnDefinitions[0].Width.Value;
        if (requiredWidth > currentWidth + 20)
            threePanelGrid.ColumnDefinitions[0].Width = new GridLength(requiredWidth, GridUnitType.Pixel);
    }

    /// <summary>
    /// Handles keyboard shortcuts for the inline section editor.
    /// Ctrl+S: Execute SaveCommand (Apply button)
    /// Esc: Execute CancelCommand (Cancel button)
    /// When either command completes, focus is restored to the section list.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Only handle shortcuts if an editor is open
        if (_vm?.EditVm is null)
            return;

        var sectionListBox = this.FindControl<ListBox>("SectionListBox");
        if (sectionListBox is null)
            return;

        // Ctrl+S (Windows/Linux) or Cmd+S (macOS) to save
        if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            if (_vm.EditVm.SaveCommand.CanExecute(null))
            {
                _vm.EditVm.SaveCommand.Execute(null);
                e.Handled = true;
                // Restore focus to the list after editor closes
                RestoreFocusToList(sectionListBox);
            }
        }
        // Esc to cancel
        else if (e.Key == Key.Escape)
        {
            if (_vm.EditVm.CancelCommand.CanExecute(null))
            {
                _vm.EditVm.CancelCommand.Execute(null);
                e.Handled = true;
                // Restore focus to the list after editor closes
                RestoreFocusToList(sectionListBox);
            }
        }
    }

    /// <summary>
    /// Restores keyboard focus to the section list or selected item.
    /// Called after the editor closes via keyboard shortcut to prevent focus from
    /// moving to an unrelated element in the tab order.
    /// </summary>
    private void RestoreFocusToList(ListBox listBox)
    {
        // Defer to the next render pass so the editor has time to fully close
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                // Focus the selected item if possible, otherwise focus the list itself
                var selectedContainer = listBox.SelectedItem is not null
                    ? listBox.ContainerFromItem(listBox.SelectedItem) as Control
                    : null;

                if (selectedContainer?.Focus() == true)
                    return; // Successfully focused the item

                // Fallback: focus the list itself
                listBox.Focus();
            },
            Avalonia.Threading.DispatcherPriority.Normal);
    }
}

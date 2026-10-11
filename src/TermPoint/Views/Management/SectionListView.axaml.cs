using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using System;
using System.ComponentModel;
using System.IO;

namespace TermPoint.Views.Management;

public partial class SectionListView : UserControl
{
    private SectionListViewModel? _vm;

    public SectionListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Handle keyboard shortcuts: Ctrl+S to save, Esc to cancel
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Intercept Ctrl+Click on section cards before the ListBox processes it.
        // Tunnel fires first (outer → inner), so marking handled here suppresses
        // the ListBox's bubble-phase selection-change handler.
        AttachedToVisualTree += OnAttachedToVisualTree;

        DetachedFromVisualTree += OnDetachedFromVisualTree;

        // Note: DoubleTapped (open inline editor) and LostFocus forwarding (commit section
        // code) are handled declaratively via DoubleTapCommandBehavior and
        // LostFocusForwardBehavior attached to the AXAML elements. No code-behind needed.
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SectionListBox");
        listBox?.AddHandler(
            InputElement.PointerPressedEvent,
            OnSectionListCtrlClick,
            RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Tunnel-phase PointerPressed on the ListBox. When Ctrl is held on a left-click over
    /// a section card, toggles that section's membership in the multi-selection and marks
    /// the event handled so the ListBox does not change <see cref="SectionListViewModel.SelectedItem"/>.
    /// </summary>
    private void OnSectionListCtrlClick(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;

        // Walk up the visual tree from the clicked element to find the ListBoxItem.
        Visual? visual = e.Source as Visual;
        while (visual is not null && visual is not ListBoxItem)
            visual = visual.GetVisualParent();

        if (visual is ListBoxItem lbi && lbi.DataContext is SectionListItemViewModel svm)
        {
            _vm.ToggleSectionCommand.Execute(svm.Section.Id);
            e.Handled = true;
        }
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
    /// Cleanup when removed from the visual tree (e.g. detached window closing).
    /// Unsubscribes from the long-lived VM so the discarded view can be garbage-collected.
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SectionListBox");
        listBox?.RemoveHandler(InputElement.PointerPressedEvent, OnSectionListCtrlClick);

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = null;
        }
    }

    /// <summary>
    /// Responds to property changes on the ViewModel that require the view to take action.
    /// These are purely visual concerns that require direct access to named controls or
    /// Avalonia layout APIs that a ViewModel must not know about.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When the selected item changes (e.g. cross-view selection sync from the
        // Schedule Grid), scroll it into view. The ListBox's own scrolling on selection is
        // suppressed (SuppressPopupScrollBehavior), so this goes through the one scroll path:
        // ListScrollAnchorBehavior.ScrollItemIntoView.
        if (e.PropertyName == nameof(SectionListViewModel.SelectedItem))
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                ScrollSelectedItemIntoView,
                Avalonia.Threading.DispatcherPriority.Background);

        // When the inline editor opens or closes, re-scroll the selected item into view
        // since the layout shift from expanding/collapsing the editor can push it out
        // of the viewport.
        if (e.PropertyName == nameof(SectionListViewModel.EditVm))
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                ScrollSelectedItemIntoView,
                Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Brings the active card (the selected section, or the card being edited) into the visible
    /// area of the list. Called when the selection changes (for example cross-view selection from
    /// the schedule grid) and after the inline editor opens or closes, since the layout shift from
    /// expanding/collapsing the editor can push the card out of the viewport.
    ///
    /// All the mechanics (built or virtualized-away card, anchoring pause, refinement) live in
    /// <see cref="Behaviors.ListScrollAnchorBehavior.ScrollItemIntoView"/>, which is the one way to
    /// scroll this list: never touch the list's ScrollViewer from here.
    ///
    /// For existing sections the list item is selected; for new Add/Copy placeholders it is
    /// not (SelectedItem is null), so we fall back to the VM's ExpandedItem.
    /// </summary>
    private void ScrollSelectedItemIntoView()
    {
        var listBox = this.FindControl<ListBox>("SectionListBox");
        var target = listBox?.SelectedItem ?? _vm?.ExpandedItem;
        if (listBox is not null && target is not null)
            Behaviors.ListScrollAnchorBehavior.ScrollItemIntoView(listBox, target);
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

    /// <summary>
    /// Opens the Room Browser help article in the system default browser.
    /// </summary>
    private void OnRoomBrowserHelpClick(object? sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Help", "rooming-tool.html");
        PlatformProcess.OpenUri(new Uri(path).AbsoluteUri);
    }
}

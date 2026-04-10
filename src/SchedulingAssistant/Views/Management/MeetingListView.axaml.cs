using Avalonia.Controls;
using Avalonia.Input;
using SchedulingAssistant.ViewModels.Management;
using System;

namespace SchedulingAssistant.Views.Management;

/// <summary>
/// Code-behind for <see cref="MeetingListView"/>.
/// Handles keyboard shortcuts for the inline meeting editor: Ctrl+S to save, Esc to cancel.
/// </summary>
public partial class MeetingListView : UserControl
{
    private MeetingListViewModel? _vm;

    public MeetingListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Handle keyboard shortcuts: Ctrl+S to save, Esc to cancel
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm = DataContext as MeetingListViewModel;
    }

    /// <summary>
    /// Handles keyboard shortcuts for the inline meeting editor.
    /// Ctrl+S: Execute SaveCommand (Apply button)
    /// Esc: Execute CancelCommand (Cancel button)
    /// When either command completes, focus is restored to the meeting list.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Only handle shortcuts if an editor is open
        if (_vm?.EditVm is null)
            return;

        ListBox? meetingListBox = null;

        // Find the ListBox by walking up from the source
        if (e.Source is Control source)
        {
            var element = source;
            while (element is not null)
            {
                if (element is ListBox lb)
                {
                    meetingListBox = lb;
                    break;
                }
                element = element.Parent as Control;
            }
        }

        // Ctrl+S to save
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_vm.EditVm.SaveCommand.CanExecute(null))
            {
                _vm.EditVm.SaveCommand.Execute(null);
                e.Handled = true;
                // Restore focus to the list after editor closes
                if (meetingListBox is not null)
                    RestoreFocusToList(meetingListBox);
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
                if (meetingListBox is not null)
                    RestoreFocusToList(meetingListBox);
            }
        }
    }

    /// <summary>
    /// Restores keyboard focus to the meeting list or selected item.
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

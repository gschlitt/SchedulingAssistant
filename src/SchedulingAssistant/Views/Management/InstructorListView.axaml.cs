using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.ComponentModel;

namespace SchedulingAssistant.Views.Management;

public partial class InstructorListView : UserControl
{
    private InstructorListViewModel? _vm;

    public InstructorListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as InstructorListViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Responds to property changes on the ViewModel that require the view to take action.
    /// Handles EditVm property changes: when the editor closes (EditVm becomes null),
    /// restore keyboard focus to the DataGrid.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstructorListViewModel.EditVm) && _vm?.EditVm is null)
        {
            var instructorDataGrid = this.FindControl<DataGrid>("InstructorDataGrid");
            if (instructorDataGrid is not null)
                RestoreFocusToGrid(instructorDataGrid);
        }
    }

    /// <summary>
    /// Handles KeyDown to intercept keys:
    /// - Enter: Opens editor for the selected row (when no editor is open)
    /// Uses Tunnel routing strategy to intercept before other handlers.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = _vm;
        if (vm is null)
            return;

        // Only process Enter to open editor if no editor is open
        if (e.Key != Key.Return || vm.EditVm is not null)
            return;

        if (e.Source is not Control source)
            return;

        // Walk up the visual tree to find the parent DataGrid for Enter key
        var element = source;
        DataGrid? dataGrid = null;
        while (element is not null)
        {
            if (element is DataGrid dg)
            {
                dataGrid = dg;
                break;
            }
            element = element.Parent as Control;
        }

        if (dataGrid is null)
            return;

        // Open editor for selected row
        if (vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
            e.Handled = true;
        }
    }


    /// <summary>
    /// Handles column-header clicks on the instructor DataGrid.
    /// Translates the clicked column's <c>Tag</c> to an <see cref="InstructorSortMode"/>,
    /// delegates to <see cref="InstructorListViewModel.SetSortMode"/> (which persists the
    /// setting and reloads from the database), then sets <c>e.Handled = true</c> to
    /// suppress the DataGrid's built-in client-side sort — ordering is applied at the
    /// DB level so it propagates to all other instructor loads in the app.
    /// Columns without a Tag (Email, Active) are ignored.
    /// </summary>
    private void DataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;   // suppress DataGrid's built-in in-memory sort

        if (DataContext is not InstructorListViewModel vm) return;

        var mode = (e.Column.Tag as string) switch
        {
            "FirstName" => InstructorSortMode.FirstName,
            "Initials"  => InstructorSortMode.Initials,
            "StaffType" => InstructorSortMode.StaffType,
            "LastName"  => InstructorSortMode.LastName,
            _           => (InstructorSortMode?)null,   // Email / Active — no sort change
        };

        if (mode.HasValue)
            vm.SetSortMode(mode.Value);
    }

    /// <summary>
    /// Restores keyboard focus to the DataGrid after an editor closes.
    /// Called when EditVm becomes null to prevent focus from jumping to an unrelated
    /// element in the tab order.
    /// </summary>
    private void RestoreFocusToGrid(DataGrid dataGrid)
    {
        // Defer to the next render pass so the editor has time to fully close
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                dataGrid.Focus();

                // Scroll the selected row into view and position the grid's internal
                // cursor so arrow-key navigation continues from the correct row.
                if (dataGrid.SelectedItem is not null)
                    dataGrid.ScrollIntoView(dataGrid.SelectedItem, null);
            },
            Avalonia.Threading.DispatcherPriority.Normal);
    }
}

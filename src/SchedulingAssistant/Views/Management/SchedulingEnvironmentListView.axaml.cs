using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.ComponentModel;

namespace SchedulingAssistant.Views.Management;

public partial class SchedulingEnvironmentListView : UserControl
{
    private SchedulingEnvironmentListViewModel? _vm;

    public SchedulingEnvironmentListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as SchedulingEnvironmentListViewModel;

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
        if (e.PropertyName == nameof(SchedulingEnvironmentListViewModel.EditVm) && _vm?.EditVm is null)
        {
            var grid = this.FindControl<DataGrid>("EnvironmentDataGrid");
            if (grid is not null)
                RestoreFocusToGrid(grid);
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

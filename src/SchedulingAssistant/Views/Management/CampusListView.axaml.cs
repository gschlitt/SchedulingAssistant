using Avalonia.Controls;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.ComponentModel;

namespace SchedulingAssistant.Views.Management;

public partial class CampusListView : UserControl
{
    private CampusListViewModel? _vm;

    public CampusListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as CampusListViewModel;

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
        if (e.PropertyName == nameof(CampusListViewModel.EditVm) && _vm?.EditVm is null)
        {
            var campusesDataGrid = this.FindControl<DataGrid>("CampusesDataGrid");
            if (campusesDataGrid is not null)
                RestoreFocusToGrid(campusesDataGrid);
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

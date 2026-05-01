using Avalonia.Controls;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.ComponentModel;

namespace SchedulingAssistant.Views.Management;

public partial class SectionCodePatternListView : UserControl
{
    private SectionCodePatternListViewModel? _vm;

    public SectionCodePatternListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as SectionCodePatternListViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Restores keyboard focus to the DataGrid when the editor closes.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SectionCodePatternListViewModel.EditVm) && _vm?.EditVm is null)
        {
            var grid = this.FindControl<DataGrid>("PatternsDataGrid");
            if (grid is not null)
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        grid.Focus();
                        if (grid.SelectedItem is not null)
                            grid.ScrollIntoView(grid.SelectedItem, null);
                    },
                    Avalonia.Threading.DispatcherPriority.Normal);
        }
    }
}

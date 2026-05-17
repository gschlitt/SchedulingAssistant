using Avalonia.Controls;
using Avalonia.Input;
using SchedulingAssistant.Controls;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views.GridView;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SchedulingAssistant.Views;

/// <summary>
/// Top-level UserControl containing the full application UI.
/// Extracted from MainWindow so the same UI can run both inside a desktop Window
/// and as the root view in the WASM browser build (ISingleViewApplicationLifetime).
/// </summary>
public partial class MainView : UserControl
{
    private MainWindowViewModel? _previousVm;
    private ResponsiveMenuPanel? _topMenuPanel;

    /// <summary>
    /// Reference to the schedule grid view, resolved after DataContext is set.
    /// Exposed so MainWindow can access it for PNG export (Print Schedule).
    /// </summary>
    public ScheduleGridView? ScheduleGridViewInstance { get; set; }

    public MainView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires cross-ViewModel callbacks and event subscriptions when the DataContext
    /// changes (including on first bind and on database switch).
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            if (_previousVm is not null)
                _previousVm.PropertyChanged -= OnMainWindowVmPropertyChanged;

            vm.ScheduleGridVm.EditRequested = vm.SectionListVm.EditSectionById;
            vm.ScheduleGridVm.MeetingEditRequested = vm.MeetingListVm.EditMeetingById;

            vm.PropertyChanged += OnMainWindowVmPropertyChanged;

            _previousVm = vm;

            ScheduleGridViewInstance = this.FindControl<ScheduleGridView>("ScheduleGridViewControl");

#if DEBUG
            var debugMenu = this.FindControl<Menu>("DebugMenu");
            if (debugMenu is not null)
                debugMenu.IsVisible = true;
#endif

            // Wire overflow-set changes so the More flyout populates in both desktop and WASM.
            if (_topMenuPanel is not null)
                _topMenuPanel.HiddenOverflowItemsChanged -= OnTopMenuOverflowChanged;

            _topMenuPanel = this.FindControl<ResponsiveMenuPanel>("TopMenuPanel");
            if (_topMenuPanel is not null)
                _topMenuPanel.HiddenOverflowItemsChanged += OnTopMenuOverflowChanged;

            SyncOverflowToViewModel();
        }
    }

    private void OnMainWindowVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.FlyoutPage)
            && DataContext is MainWindowViewModel vm
            && vm.FlyoutPage is null)
        {
            vm.WorkloadPanelVm.Reload();
        }
    }

    private void OnTopMenuOverflowChanged(object? sender, EventArgs e) => SyncOverflowToViewModel();

    /// <summary>
    /// Reads the currently-hidden overflow items from the menu panel and forwards
    /// their x:Name keys to <see cref="MoreMenuViewModel.SetHiddenKeys"/>.
    /// </summary>
    private void SyncOverflowToViewModel()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (_topMenuPanel is null) return;

        var keys = new List<string>(_topMenuPanel.HiddenOverflowItems.Count);
        foreach (var c in _topMenuPanel.HiddenOverflowItems)
        {
            if (!string.IsNullOrEmpty(c.Name)) keys.Add(c.Name);
        }
        vm.MoreMenuVm.SetHiddenKeys(keys);
    }

    /// <summary>
    /// Right-click on the Section View header shows the sort-mode context menu.
    /// Sort modes are driven by <see cref="SectionListViewModel.SelectedSortModeIndex"/>.
    /// </summary>
    public void OnSectionViewHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
            return;

        if (DataContext is not MainWindowViewModel mainVm)
            return;

        var vm = mainVm.SectionListVm;
        var curIdx = vm.SelectedSortModeIndex;

        var menu = new ContextMenu();
        var labels = SectionListViewModel.SortModeOptions;
        for (int i = 0; i < labels.Count; i++)
        {
            var idx = i;
            var mark = curIdx == idx ? "  " : "    ";
            var item = new MenuItem { Header = mark + labels[idx] };
            item.Click += (_, _) => vm.SelectedSortModeIndex = idx;
            menu.Items.Add(item);
        }

        if (sender is Control ctrl)
            menu.Open(ctrl);

        e.Handled = true;
    }
}

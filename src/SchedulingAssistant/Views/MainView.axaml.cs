// MainView.axaml.cs
//
// WHY THIS CLASS EXISTS (WASM / browser demo):
// The application UI is split into two layers so that the same UI can run on both
// the desktop (inside a Window) and the browser (WebAssembly / ISingleViewApplicationLifetime):
//
//   MainView   — everything the *user sees* (this file + MainView.axaml)
//   MainWindow — everything the *desktop OS needs*: startup splash, DB-path dialog,
//                window chrome, and panel-detach secondary windows
//
// Code that belongs here: anything that responds to ViewModel state changes and
// manipulates view-level elements that live inside MainView (e.g. editing callbacks,
// section-sort context menu, workload-item selection sync).
//
// Code that stays in MainWindow: anything that requires a Window — file dialogs,
// secondary detached windows, OnClosing, OnOpened.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SchedulingAssistant.Controls;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views.GridView;
using System;
using System.ComponentModel;
using System.Linq;

namespace SchedulingAssistant.Views;

/// <summary>
/// Code-behind for <see cref="MainView"/>.
/// Handles ViewModel-driven view wiring: grid-double-click-to-edit callback,
/// workload-item selection sync, flyout-close refresh, and section-sort context menu.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>
    /// Tracks the previously bound ViewModel so event subscriptions can be
    /// cleaned up when the DataContext is replaced (e.g. after a database switch).
    /// </summary>
    private MainWindowViewModel? _previousVm;

    /// <summary>
    /// Reference to the schedule grid view, resolved after DataContext is set.
    /// Exposed so MainWindow can pass it to other consumers if needed.
    /// </summary>
    public ScheduleGridView? ScheduleGridViewInstance { get; private set; }

    public MainView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Called when the DataContext changes (including on first bind and on database switch).
    /// Wires cross-ViewModel callbacks and event subscriptions that require access to view
    /// instances (i.e. things that cannot be expressed purely in AXAML bindings).
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            // Unsubscribe from the previous VM to prevent memory leaks and stale callbacks
            // when the DataContext is replaced on a database switch.
            if (_previousVm is not null)
            {
                _previousVm.PropertyChanged -= OnMainWindowVmPropertyChanged;
                _previousVm.WorkloadPanelVm.ItemClicked -= OnWorkloadItemClicked;
            }

            // Wire the grid's double-click-to-edit callback at the view level so that
            // ScheduleGridViewModel and SectionListViewModel remain decoupled from each other.
            vm.ScheduleGridVm.EditRequested = vm.SectionListVm.EditSectionById;

            vm.PropertyChanged += OnMainWindowVmPropertyChanged;
            vm.WorkloadPanelVm.ItemClicked += OnWorkloadItemClicked;

            _previousVm = vm;

            ScheduleGridViewInstance = this.FindControl<ScheduleGridView>("ScheduleGridViewControl");

#if DEBUG
            // Make the Debug menu visible in DEBUG builds.
            var debugMenu = this.FindControl<Menu>("DebugMenu");
            if (debugMenu is not null)
                debugMenu.IsVisible = true;
#endif
        }
    }

    /// <summary>
    /// Reloads workload data whenever the flyout panel is dismissed,
    /// so any release changes made in the flyout are immediately reflected.
    /// </summary>
    private void OnMainWindowVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.FlyoutPage)
            && DataContext is MainWindowViewModel vm
            && vm.FlyoutPage is null)
        {
            vm.WorkloadPanelVm.Reload();
        }
    }

    /// <summary>
    /// When a workload item is clicked, selects the matching section in the Section View
    /// so both panels stay in sync. Only section-type items are handled; releases have
    /// no corresponding row in the Section View.
    /// </summary>
    private void OnWorkloadItemClicked(WorkloadItemViewModel item)
    {
        if (item.Kind != WorkloadItemKind.Section)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        var sectionItem = vm.SectionListVm.SectionItems
            .OfType<SectionListItemViewModel>()
            .FirstOrDefault(s => s.Section.Id == item.Id);

        if (sectionItem is not null)
            vm.SectionListVm.SelectedItem = sectionItem;
    }

    /// <summary>
    /// Handles right-click on the Section View header to show the sort-mode context menu.
    /// Left-click is ignored so normal pointer interactions on the header are unaffected.
    /// </summary>
    private void OnSectionViewHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
            return;

        if (DataContext is not MainWindowViewModel mainVm)
            return;

        var vm = mainVm.SectionListVm;
        var cur = vm.CurrentSortMode;
        string Mark(SectionSortMode m) => cur == m ? "✓  " : "    ";

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header  = Mark(SectionSortMode.SubjectCourseCode) + "Sort by Subject / Course Code",
            Command = vm.SortBySubjectCourseCodeCommand,
        });
        menu.Items.Add(new MenuItem
        {
            Header  = Mark(SectionSortMode.Instructor) + "Sort by Instructor",
            Command = vm.SortByInstructorCommand,
        });
        menu.Items.Add(new MenuItem
        {
            Header  = Mark(SectionSortMode.SectionType) + "Sort by Section Type",
            Command = vm.SortBySectionTypeCommand,
        });

        if (sender is Control ctrl)
            menu.Open(ctrl);

        e.Handled = true;
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TermPoint.ViewModels.Management;
using System;
using System.ComponentModel;

namespace TermPoint.Views.Management;

public partial class CourseListView : UserControl
{
    private CourseListViewModel? _vm;

    public CourseListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (this.FindControl<DataGrid>("CoursesDataGrid") is { } courses)
            courses.Loaded += (_, _) => ScrollSelectedIntoView(courses);
    }

    /// <summary>
    /// Scrolls the grid's selected row into view. Run when the Courses grid first appears, because a course can be
    /// selected before the list is shown (<see cref="CourseListViewModel.SelectCourse"/>, from Check all courses) and
    /// Avalonia's DataGrid does not scroll to its selected item by itself. Beside <see cref="RestoreFocusToGrid"/>,
    /// which scrolls the same way, rather than in a behavior used by this one grid.
    /// </summary>
    /// <param name="grid">The grid.</param>
    private static void ScrollSelectedIntoView(DataGrid grid)
    {
        if (grid.SelectedItem is { } item)
            grid.ScrollIntoView(item, null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as CourseListViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        SyncComponentsColumn();
    }

    /// <summary>
    /// Shows or hides the Components column (Access Analyzer) and folds it to a narrow strip. DataGrid columns are not
    /// in the visual tree, so their IsVisible and Width can't be bound; this reads them from the view model whenever
    /// <see cref="CourseListViewModel.ShowComponentsColumn"/> or <see cref="CourseListViewModel.ComponentsColumnOpen"/>
    /// changes. Resetting the width to Auto when it opens lets the column size to its text again.
    /// </summary>
    private void SyncComponentsColumn()
    {
        if (_vm is null || this.FindControl<DataGrid>("CoursesDataGrid") is not { } grid) return;
        foreach (var column in grid.Columns)
        {
            if (!Equals(column.Tag, "Components")) continue;
            column.IsVisible = _vm.ShowComponentsColumn;
            column.Width = _vm.ComponentsColumnOpen ? DataGridLength.Auto : DataGridLength.SizeToHeader;
        }
    }

    /// <summary>
    /// Responds to property changes on the ViewModel that require the view to take action.
    /// Handles EditVm property changes: when the editor closes (EditVm becomes null),
    /// restore keyboard focus to the DataGrid.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CourseListViewModel.ShowComponentsColumn) or nameof(CourseListViewModel.ComponentsColumnOpen))
        {
            SyncComponentsColumn();
            return;
        }
        if (e.PropertyName == nameof(CourseListViewModel.EditVm) && _vm?.EditVm is null)
        {
            var coursesDataGrid = this.FindControl<DataGrid>("CoursesDataGrid");
            if (coursesDataGrid is not null)
                RestoreFocusToGrid(coursesDataGrid);
        }
        else if (e.PropertyName == nameof(CourseListViewModel.SubjectEditVm) && _vm?.SubjectEditVm is null)
        {
            var subjectsDataGrid = this.FindControl<DataGrid>("SubjectsDataGrid");
            if (subjectsDataGrid is not null)
                RestoreFocusToGrid(subjectsDataGrid);
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
        if (e.Key != Key.Return || vm.EditVm is not null || vm.SubjectEditVm is not null)
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

        // Determine which list the DataGrid belongs to and open editor
        if (dataGrid.ItemsSource == vm.Courses && vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
            e.Handled = true;
        }
        // The Subjects grid binds to SubjectRows (row view models), not Subjects.
        else if (dataGrid.ItemsSource == vm.SubjectRows && vm.EditSubjectCommand.CanExecute(null))
        {
            vm.EditSubjectCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Restores keyboard focus to the DataGrid after an editor closes.
    /// Called when EditVm or SubjectEditVm becomes null to prevent focus from
    /// jumping to an unrelated element in the tab order.
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

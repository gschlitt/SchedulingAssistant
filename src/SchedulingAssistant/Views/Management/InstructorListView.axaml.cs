using Avalonia.Controls;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Views.Management;

public partial class InstructorListView : UserControl
{
    public InstructorListView()
    {
        InitializeComponent();
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
}

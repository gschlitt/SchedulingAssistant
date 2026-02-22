using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Views.Management;

public partial class SectionListView : UserControl
{
    public SectionListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        var listBox = this.FindControl<ListBox>("SectionListBox")!;
        listBox.DoubleTapped += OnListBoxDoubleTapped;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SectionListViewModel vm)
            vm.ShowEditWindow = ShowEditWindowHandler;
    }

    private void OnListBoxDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SectionListViewModel vm && vm.SelectedItem is { } item)
            vm.EditItem(item);
    }

    private void ShowEditWindowHandler(SectionEditViewModel editVm)
    {
        var win = new SectionEditWindow();
        editVm.RequestClose = () => win.Close();
        win.DataContext = editVm;
        if (TopLevel.GetTopLevel(this) is Window ownerWindow)
            win.Show(ownerWindow);
        else
            win.Show();
    }
}

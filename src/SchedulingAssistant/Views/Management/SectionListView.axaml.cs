using Avalonia.Controls;
using Avalonia.Input;
using SchedulingAssistant.ViewModels.Management;
using System.ComponentModel;

namespace SchedulingAssistant.Views.Management;

public partial class SectionListView : UserControl
{
    private SectionListViewModel? _vm;

    public SectionListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        var listBox = this.FindControl<ListBox>("SectionListBox")!;
        listBox.DoubleTapped += OnListBoxDoubleTapped;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as SectionListViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;

        UpdateAddFormHost();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SectionListViewModel.EditVm) or nameof(SectionListViewModel.ExpandedItem))
            UpdateAddFormHost();
    }

    private void UpdateAddFormHost()
    {
        var addFormHost = this.FindControl<Border>("AddFormHost");
        var addFormContent = this.FindControl<ContentControl>("AddFormContent");
        if (addFormHost is null || addFormContent is null) return;

        // Show "Add" form at top only when adding a new section (EditVm set, no existing item expanded)
        bool isAddMode = _vm?.EditVm is not null && _vm.ExpandedItem is null;
        addFormHost.IsVisible = isAddMode;
        addFormContent.Content = isAddMode ? _vm!.EditVm : null;
    }

    private void OnListBoxDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is not null && _vm.SelectedItem is { } item)
            _vm.EditItem(item);
    }
}

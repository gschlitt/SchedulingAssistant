using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

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

        // The Section Code TextBox lives inside a DataTemplate, so we can't attach to it
        // directly. Instead we listen for the routed LostFocus event bubbling up from any
        // child named "SectionCodeBox" and forward it to the VM's CommitSectionCode().
        // CommitSectionCode validates uniqueness and, if clean, records the validated
        // course+code snapshot that unlocks the rest of the form (see SectionEditViewModel).
        AddHandler(LostFocusEvent, OnAnyLostFocus, RoutingStrategies.Bubble);

        // Measure content width after layout completes
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateColumnWidth);
    }

    // Triggered whenever any child TextBox loses focus; we filter on the control name.
    private void OnAnyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { Name: "SectionCodeBox" })
            _vm?.EditVm?.CommitSectionCode();
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _vm = DataContext as SectionListViewModel;

        if (_vm is not null)
        {
            // Wire the ShowError delegate so the VM can show modal notices (e.g. copy
            // conflicts) without taking a hard dependency on Avalonia Window APIs.
            _vm.ShowError = ShowErrorAsync;

            // Subscribe to list changes to update column width
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SectionListViewModel.SectionItems))
        {
            // Defer measurement until after layout is updated
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateColumnWidth);
        }
    }

    private void UpdateColumnWidth()
    {
        // Measure the content's desired width
        var scrollViewer = this.FindControl<ScrollViewer>("ListScrollViewer");
        var stackPanel = scrollViewer?.Content as StackPanel;

        if (stackPanel is null) return;

        // Force layout pass to get accurate measurements
        stackPanel.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = stackPanel.DesiredSize.Width;

        // Add padding for margins and scrollbar space (safety margin)
        var requiredWidth = Math.Ceiling(desiredWidth) + 12;

        // Find the MainWindow and ThreePanelGrid
        var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
        if (mainWindow is null) return;

        var threePanelGrid = mainWindow.FindControl<Grid>("ThreePanelGrid");
        if (threePanelGrid is null) return;

        // Only update if not editing (when editing, it's already 500px)
        var isEditing = _vm?.IsEditing ?? false;
        if (isEditing) return;

        var currentWidth = threePanelGrid.ColumnDefinitions[0].Width.Value;

        // Only update if the required width is significantly larger than current
        // (use a threshold of 20px to avoid constant small adjustments)
        if (requiredWidth > currentWidth + 20)
        {
            threePanelGrid.ColumnDefinitions[0].Width = new GridLength(requiredWidth, GridUnitType.Pixel);
        }
    }

    // Displays a simple modal notice window. Used by SectionListViewModel.ShowError.
    private async Task ShowErrorAsync(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var msg = new Window
        {
            Title = "Notice",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var body = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        };

        var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        okBtn.Click += (_, _) => msg.Close();

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(okBtn);
        msg.Content = panel;

        await msg.ShowDialog(owner);
    }

    private void OnListBoxDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is not null && _vm.SelectedItem is { } item)
            _vm.EditItem(item);
    }
}

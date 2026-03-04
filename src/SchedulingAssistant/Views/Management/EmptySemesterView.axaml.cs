using Avalonia.Controls;
using Avalonia.Layout;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Threading.Tasks;

namespace SchedulingAssistant.Views.Management;

public partial class EmptySemesterView : UserControl
{
    public EmptySemesterView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is EmptySemesterViewModel vm)
        {
            vm.ConfirmEmpty = ShowConfirmEmptyAsync;
            vm.ShowError = ShowErrorAsync;
        }
    }

    private async Task<bool> ShowConfirmEmptyAsync(string semesterName, int sectionCount)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return false;

        var result = false;

        var msg = new Window
        {
            Title = "Empty Semester — No Undo",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var bodyText = $"Remove all {sectionCount} section{(sectionCount == 1 ? "" : "s")} from \"{semesterName}\"?\n\nThis cannot be undone.";

        var body = new TextBlock
        {
            Text = bodyText,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        };

        var deleteBtn = new Button { Content = "Delete All Sections" };
        var cancelBtn = new Button { Content = "Cancel" };

        deleteBtn.Click += (_, _) => { result = true; msg.Close(); };
        cancelBtn.Click += (_, _) => { result = false; msg.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(deleteBtn);
        buttons.Children.Add(cancelBtn);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        msg.Content = panel;

        await msg.ShowDialog(owner);
        return result;
    }

    private async Task ShowErrorAsync(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var msg = new Window
        {
            Title = "Error",
            Width = 420,
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
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Interactivity;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Threading.Tasks;

namespace SchedulingAssistant.Views.Management;

public partial class AcademicYearListView : UserControl
{
    public AcademicYearListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AcademicYearListViewModel vm)
        {
            vm.ConfirmCopyStartTimes = ShowCopyStartTimesConfirmAsync;
            vm.ConfirmCopySemesters  = ShowCopySemestersConfirmAsync;
        }
    }

    private async Task<string?> ShowCopyStartTimesConfirmAsync(string prevAyName, string prevAyId)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return null;

        string? result = null;

        var msg = new Window
        {
            Title = "Copy Start Times",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var body = new TextBlock
        {
            Text = $"Would you like to copy the block length / start time setup from {prevAyName}?\n\nClick \"Yes\" to copy, or \"No\" to create this year with no start times configured.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        };

        var yesBtn = new Button { Content = "Yes" };
        var noBtn = new Button { Content = "No" };

        yesBtn.Click += (_, _) => { result = prevAyId; msg.Close(); };
        noBtn.Click += (_, _) => { result = null; msg.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(yesBtn);
        buttons.Children.Add(noBtn);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        msg.Content = panel;

        await msg.ShowDialog(owner);
        return result;
    }

    private async Task<bool> ShowCopySemestersConfirmAsync(string prevAyName)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return false;

        bool result = false;

        var msg = new Window
        {
            Title = "Copy Semesters",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var body = new TextBlock
        {
            Text = $"Would you like to copy the semester names from {prevAyName}?\n\nClick \"Yes\" to copy, or \"No\" to start with an empty semester list.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        };

        var yesBtn = new Button { Content = "Yes" };
        var noBtn  = new Button { Content = "No" };

        yesBtn.Click += (_, _) => { result = true;  msg.Close(); };
        noBtn.Click  += (_, _) => { result = false; msg.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(yesBtn);
        buttons.Children.Add(noBtn);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        msg.Content = panel;

        await msg.ShowDialog(owner);
        return result;
    }
}

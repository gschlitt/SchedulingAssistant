using Avalonia.Controls;
using Avalonia.Layout;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Threading.Tasks;

namespace SchedulingAssistant.Views.Management;

public partial class InstructorListView : UserControl
{
    public InstructorListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is InstructorListViewModel vm)
        {
            vm.ShowError = ShowErrorAsync;
            vm.ShowConfirmation = ShowConfirmationAsync;
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var msg = new Window
        {
            Title = "Cannot Delete",
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

    private async Task<bool> ShowConfirmationAsync(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return false;

        bool confirmed = false;
        var dlg = new Window
        {
            Title = "Confirm",
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

        var yesBtn = new Button { Content = "Delete", HorizontalAlignment = HorizontalAlignment.Right };
        yesBtn.Click += (_, _) =>
        {
            confirmed = true;
            dlg.Close();
        };

        var noBtn = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
        noBtn.Click += (_, _) => dlg.Close();

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        btnPanel.Children.Add(noBtn);
        btnPanel.Children.Add(yesBtn);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(btnPanel);
        dlg.Content = panel;

        await dlg.ShowDialog(owner);
        return confirmed;
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Threading.Tasks;

namespace SchedulingAssistant.Views.Management;

public partial class SectionPropertyListView : UserControl
{
    public SectionPropertyListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SectionPropertyListViewModel vm)
        {
            vm.ConfirmDelete = ShowDeleteConfirmAsync;
            vm.ShowError     = ShowErrorAsync;
        }
    }

    private async Task<bool> ShowDeleteConfirmAsync(string propertyName, string propertyType)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return false;

        var affected = propertyType == SectionPropertyTypes.StaffType
            ? "all instructors that reference it"
            : "all sections in all semesters that reference it";

        var bodyText = $"Delete \"{propertyName}\"?\n\n" +
                       $"This will also remove it from {affected}.";

        var result = false;
        var dialog = new Window
        {
            Title = "Confirm Delete",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var body      = new TextBlock { Text = bodyText, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var deleteBtn = new Button { Content = "Delete" };
        var cancelBtn = new Button { Content = "Cancel" };

        deleteBtn.Click += (_, _) => { result = true;  dialog.Close(); };
        cancelBtn.Click += (_, _) => { result = false; dialog.Close(); };

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
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
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

        var body  = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        okBtn.Click += (_, _) => msg.Close();

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(okBtn);
        msg.Content = panel;

        await msg.ShowDialog(owner);
    }
}

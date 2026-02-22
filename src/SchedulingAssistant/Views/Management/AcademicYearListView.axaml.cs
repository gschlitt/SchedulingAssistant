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
            vm.ConfirmDelete = ShowDeleteConfirmAsync;
    }

    private async Task<bool> ShowDeleteConfirmAsync(string ayName, int sectionCount)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return false;

        var result = false;

        var msg = new Window
        {
            Title = "Confirm Delete",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var bodyText = sectionCount > 0
            ? $"Delete academic year \"{ayName}\"?\n\nWarning: this academic year has {sectionCount} section{(sectionCount == 1 ? "" : "s")} across its semesters. They will all be permanently deleted."
            : $"Delete academic year \"{ayName}\" and all its semesters?";

        var body = new TextBlock
        {
            Text = bodyText,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        };

        var deleteBtn = new Button { Content = "Delete" };
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
}

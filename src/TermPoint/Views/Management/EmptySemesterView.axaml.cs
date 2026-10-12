using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TermPoint.ViewModels.Management;
using System;
using System.Threading.Tasks;

namespace TermPoint.Views.Management;

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

    /// <summary>
    /// Shows the "no undo" confirmation for emptying a semester, listing everything that will be deleted.
    /// </summary>
    /// <param name="semesterLabel">Academic year and semester about to be emptied, e.g. "2026-2027 Early Summer"; shown in bold.</param>
    /// <param name="contentsDescription">What it contains, e.g. "12 sections, 3 meetings" (from <c>SemesterContents.Describe()</c>).</param>
    /// <returns>True if the user chose Delete Everything; false if they cancelled or the dialog could not be shown.</returns>
    private async Task<bool> ShowConfirmEmptyAsync(string semesterLabel, string contentsDescription)
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
            ShowInTaskbar = false,
            // Modal dialogs must be Topmost: they block all app input, so if one opens
            // beneath a Topmost sticky note (or loses an activation race and lands behind
            // the main window) the app appears frozen with nothing clickable.
            Topmost = true
        };

        // "Permanently delete everything in <b>2026-2027 Early Summer</b>: 12 sections, 3 meetings?"
        // The year + semester is bold so the user can see at a glance exactly what is about to go.
        var body = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13,
            Inlines = new InlineCollection
            {
                new Run("Permanently delete everything in "),
                new Run(semesterLabel) { FontWeight = FontWeight.Bold },
                new Run($": {contentsDescription}?"),
                new LineBreak(),
                new LineBreak(),
                new Run("This cannot be undone.")
            }
        };

        var deleteBtn = new Button { Content = "Delete Everything" };
        var cancelBtn = new Button { Content = "Cancel" };

        deleteBtn.Click += (_, _) => { result = true; Dispatcher.UIThread.Post(() => msg.Close()); };
        cancelBtn.Click += (_, _) => { result = false; Dispatcher.UIThread.Post(() => msg.Close()); };

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
            ShowInTaskbar = false,
            Topmost = true // see ShowConfirmEmptyAsync — modal dialogs must never be occluded
        };

        var body = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        };

        var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        okBtn.Click += (_, _) => Dispatcher.UIThread.Post(() => msg.Close());

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        panel.Children.Add(body);
        panel.Children.Add(okBtn);
        msg.Content = panel;

        await msg.ShowDialog(owner);
    }
}

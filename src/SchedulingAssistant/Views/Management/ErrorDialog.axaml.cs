using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SchedulingAssistant.Views.Management;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public ErrorDialog(string message) : this()
    {
        MessageText.Text = message;
    }

    private void OK_Click(object? sender, RoutedEventArgs e) => Close();
}

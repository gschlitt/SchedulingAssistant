using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SchedulingAssistant.Views.Management;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string message, string confirmLabel = "Delete") : this()
    {
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}

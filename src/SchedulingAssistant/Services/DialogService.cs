using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SchedulingAssistant.Views.Management;
using System;
using System.Threading.Tasks;

namespace SchedulingAssistant.Services;

public class DialogService : IDialogService
{
    public async Task<bool> Confirm(string message, string confirmLabel = "Delete")
    {
        var result = await new ConfirmDialog(message, confirmLabel).ShowDialog<bool?>(GetActiveWindow());
        return result == true;
    }

    public async Task ShowError(string message)
    {
        await new ErrorDialog(message).ShowDialog(GetActiveWindow());
    }

    private static Window GetActiveWindow()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return window ?? throw new InvalidOperationException("No active window available.");
    }
}

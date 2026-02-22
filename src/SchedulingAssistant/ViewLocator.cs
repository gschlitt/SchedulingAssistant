using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SchedulingAssistant.ViewModels;
using System;

namespace SchedulingAssistant;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;

        var name = param.GetType().FullName!
            .Replace(".ViewModels.", ".Views.")
            .Replace("ViewModel", "View");

        var type = Type.GetType(name);
        if (type != null)
            return (Control)Activator.CreateInstance(type)!;

        // Fallback: if the VM has a Message property (e.g. ErrorViewModel), show it
        var messageProp = param.GetType().GetProperty("Message");
        if (messageProp != null)
        {
            return new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = messageProp.GetValue(param)?.ToString(),
                    Foreground = Avalonia.Media.Brushes.Red,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(16),
                    FontFamily = new Avalonia.Media.FontFamily("Consolas")
                }
            };
        }

        return new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}

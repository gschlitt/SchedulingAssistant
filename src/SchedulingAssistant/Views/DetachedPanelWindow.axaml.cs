using Avalonia;
using Avalonia.Controls;
using System;

namespace SchedulingAssistant.Views;

public partial class DetachedPanelWindow : Window
{
    public static readonly StyledProperty<bool> HasHeaderContextProperty =
        AvaloniaProperty.Register<DetachedPanelWindow, bool>(nameof(HasHeaderContext), false);

    public bool HasHeaderContext
    {
        get => GetValue(HasHeaderContextProperty);
        set => SetValue(HasHeaderContextProperty, value);
    }

    public Action? OnReattach { get; init; }

    public DetachedPanelWindow()
    {
        InitializeComponent();
    }

    public void SetContent(string title, Control content, object? headerContext = null)
    {
        Title = title;
        this.FindControl<ContentControl>("ContentArea")!.Content = content;

        if (headerContext is not null)
        {
            HasHeaderContext = true;
            var headerContextArea = this.FindControl<Control>("HeaderContextArea");
            if (headerContextArea is not null)
            {
                dynamic dyn = headerContextArea;
                dyn.Content = headerContext;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        OnReattach?.Invoke();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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

    /// <summary>
    /// When true, <see cref="OnReattach"/> is NOT invoked on close. Set during app shutdown
    /// so closing this window doesn't try to reattach its content to a main window that is
    /// itself tearing down (its DI container may already be disposed).
    /// </summary>
    public bool SuppressReattach { get; set; }

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
        // Clear content before reattach so child views receive a null DataContext,
        // which triggers their OnDataContextChanged to unsubscribe PropertyChanged
        // handlers from the long-lived ViewModels. Without this, every detach/reattach
        // cycle leaks the entire discarded view tree (canvas, tile controls, timers, etc.)
        // because the VM holds a strong reference via PropertyChanged.
        var contentArea = this.FindControl<ContentControl>("ContentArea");
        if (contentArea is not null) contentArea.Content = null;

        var headerArea = this.FindControl<ContentPresenter>("HeaderContextArea");
        if (headerArea is not null) headerArea.Content = null;

        base.OnClosed(e);
        if (!SuppressReattach) OnReattach?.Invoke();
    }
}

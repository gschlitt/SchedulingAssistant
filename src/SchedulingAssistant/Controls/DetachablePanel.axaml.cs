using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SchedulingAssistant.Controls;

public partial class DetachablePanel : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<DetachablePanel, string>(nameof(Header), "Panel");

    public static readonly StyledProperty<object?> PanelContentProperty =
        AvaloniaProperty.Register<DetachablePanel, object?>(nameof(PanelContent));

    public static readonly StyledProperty<object?> HeaderExtraProperty =
        AvaloniaProperty.Register<DetachablePanel, object?>(nameof(HeaderExtra));

    /// <summary>
    /// Optional content placed inline to the right of the Header text (left-justified).
    /// Used for contextual info like semester name and stats.
    /// </summary>
    public static readonly StyledProperty<object?> HeaderContextProperty =
        AvaloniaProperty.Register<DetachablePanel, object?>(nameof(HeaderContext));

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? PanelContent
    {
        get => GetValue(PanelContentProperty);
        set => SetValue(PanelContentProperty, value);
    }

    public object? HeaderExtra
    {
        get => GetValue(HeaderExtraProperty);
        set => SetValue(HeaderExtraProperty, value);
    }

    public object? HeaderContext
    {
        get => GetValue(HeaderContextProperty);
        set => SetValue(HeaderContextProperty, value);
    }

    /// <summary>
    /// Raised when the user clicks the pop-out button.
    /// The sender is this DetachablePanel instance.
    /// </summary>
    public event EventHandler? DetachRequested;

    public DetachablePanel()
    {
        InitializeComponent();
    }

    private void OnDetachClicked(object? sender, RoutedEventArgs e)
    {
        DetachRequested?.Invoke(this, EventArgs.Empty);
    }
}

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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows.Input;

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

    /// <summary>
    /// Raised when the user right-clicks on the header bar.
    /// The sender is this DetachablePanel instance.
    /// Consumers (e.g. MainWindow) subscribe to this to open context menus.
    /// </summary>
    public event EventHandler<PointerPressedEventArgs>? HeaderRightClicked;

    /// <summary>
    /// Internal command bound to RightClickCommandBehavior on HeaderBorder in the AXAML.
    /// When the header receives a right-click, the behavior invokes this command, which
    /// fires the public HeaderRightClicked event. This avoids a FindControl call in the
    /// constructor and keeps the pointer-event wiring declarative in AXAML.
    /// The command receives the PointerPressedEventArgs so callers can inspect position.
    /// </summary>
    public ICommand InternalHeaderRightClickCommand { get; }

    public DetachablePanel()
    {
        InitializeComponent();

        // Wire the internal command that bridges the AXAML behavior to the public event.
        InternalHeaderRightClickCommand =
            new RelayCommand<PointerPressedEventArgs>(e => HeaderRightClicked?.Invoke(this, e!));
    }

    private void OnDetachClicked(object? sender, RoutedEventArgs e)
    {
        DetachRequested?.Invoke(this, EventArgs.Empty);
    }
}

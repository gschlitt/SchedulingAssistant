using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that executes a command when the Escape key is pressed on a control.
///
/// Commonly used on a Window or top-level container to dismiss flyouts, modals, or overlays.
/// The command receives no parameter.
///
/// This replaces the Escape key handling in MainWindow.axaml.cs's KeyDown handler.
///
/// Usage in AXAML:
///   <Window b:DismissBehaviors.EscapeCommand="{Binding CloseFlyoutCommand}" />
/// </summary>
public static class DismissBehaviors
{
    // ── Escape key dismiss ───────────────────────────────────────────────────

    /// <summary>
    /// Command to execute when Escape is pressed. Set to null to disable.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> EscapeCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "EscapeCommand", typeof(DismissBehaviors));

    /// <summary>Gets the Escape key command.</summary>
    public static ICommand? GetEscapeCommand(Control c) => c.GetValue(EscapeCommandProperty);

    /// <summary>Sets the Escape key command.</summary>
    public static void SetEscapeCommand(Control c, ICommand? value) => c.SetValue(EscapeCommandProperty, value);

    // ── Pointer-press dismiss ────────────────────────────────────────────────

    /// <summary>
    /// Command to execute when the control receives a PointerPressed event.
    /// Typically attached to a backdrop overlay to dismiss on click-outside.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "ClickCommand", typeof(DismissBehaviors));

    /// <summary>Gets the click dismiss command.</summary>
    public static ICommand? GetClickCommand(Control c) => c.GetValue(ClickCommandProperty);

    /// <summary>Sets the click dismiss command.</summary>
    public static void SetClickCommand(Control c, ICommand? value) => c.SetValue(ClickCommandProperty, value);

    // ── Static constructor ───────────────────────────────────────────────────

    static DismissBehaviors()
    {
        EscapeCommandProperty.Changed.AddClassHandler<Control>(OnEscapeCommandChanged);
        ClickCommandProperty.Changed.AddClassHandler<Control>(OnClickCommandChanged);
    }

    // ── Escape key wiring ────────────────────────────────────────────────────

    /// <summary>
    /// Wires (or unwires) the KeyDown listener when the EscapeCommand property changes.
    /// </summary>
    private static void OnEscapeCommandChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.KeyDown -= OnKeyDown;
        if (e.NewValue is ICommand)
            c.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Handles KeyDown: if Escape is pressed and the command can execute, runs it
    /// and marks the event as handled.
    /// <para>
    /// The explicit <c>e.Handled</c> check is intentional: Avalonia's
    /// <c>handledEventsToo = false</c> guarantee does not always suppress cross-phase
    /// calls (tunnel → bubble). Inner tunnel handlers on flyout content (e.g.
    /// InstructorListView, RoomListView) set <c>e.Handled = true</c> when an inline
    /// editor is open, so that Esc closes only the editor rather than the whole flyout.
    /// Without this check, the bubble-phase handler here would still fire.
    /// </para>
    /// </summary>
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || sender is not Control c)
            return;

        var cmd = GetEscapeCommand(c);
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
            e.Handled = true;
        }
    }

    // ── Click-to-dismiss wiring ──────────────────────────────────────────────

    /// <summary>
    /// Wires (or unwires) the PointerPressed listener when the ClickCommand property changes.
    /// </summary>
    private static void OnClickCommandChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.PointerPressed -= OnPointerPressed;
        if (e.NewValue is ICommand)
            c.PointerPressed += OnPointerPressed;
    }

    /// <summary>
    /// Handles PointerPressed: executes the click command if available.
    /// </summary>
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c)
            return;

        var cmd = GetClickCommand(c);
        if (cmd?.CanExecute(null) == true)
            cmd.Execute(null);
    }
}

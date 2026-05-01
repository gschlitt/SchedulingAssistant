using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Windows.Input;

namespace AutoCompleteBoxRepro;

/// <summary>
/// Attached behavior that executes a command when focus leaves the control it is attached to.
///
/// Unlike <see cref="LostFocusForwardBehavior"/> (which listens on a parent and filters by
/// a child control's Name), this behavior attaches directly to the target control — making
/// it suitable for controls inside DataTemplates where the target is directly accessible.
///
/// Avalonia's <see cref="InputElement.LostFocusEvent"/> is a bubbling event: when an inner
/// element (e.g. the TextBox inside an AutoCompleteBox) loses focus, the event bubbles up
/// through all ancestors. We therefore listen with <see cref="RoutingStrategies.Bubble"/> and
/// guard with <see cref="InputElement.IsKeyboardFocusWithin"/> so the command only fires when
/// focus has truly left the attached control, not merely shifted between its children.
///
/// Usage in AXAML:
///   <AutoCompleteBox b:LostFocusCommandBehavior.Command="{Binding CommitStartTimeCommand}" />
/// </summary>
public static class LostFocusCommandBehavior
{
    /// <summary>The command to execute when focus leaves the attached control.</summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "Command", typeof(LostFocusCommandBehavior));

    /// <summary>Gets the command attached to the specified control.</summary>
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    /// <summary>Sets the command attached to the specified control.</summary>
    public static void SetCommand(Control c, ICommand? value) => c.SetValue(CommandProperty, value);

    static LostFocusCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    private static void OnCommandChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        // Always remove first to avoid double-subscription when the property is updated.
        c.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);

        if (e.NewValue is ICommand)
            c.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    private static void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is not Control c) return;
        // The LostFocus event bubbles from inner elements (e.g. the TextBox inside an
        // AutoCompleteBox). Only execute the command when focus has left this control entirely,
        // not when it merely moved between its own children (e.g. to the dropdown popup).
        if (c.IsKeyboardFocusWithin) return;
        var cmd = GetCommand(c);
        if (cmd?.CanExecute(null) == true)
            cmd.Execute(null);
    }
}

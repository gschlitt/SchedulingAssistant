using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that executes a command when the user right-clicks on a control.
/// The command parameter is the PointerPressedEventArgs, which allows the command handler
/// to inspect pointer position, open context menus, etc.
///
/// This replaces the PointerPressed right-button filter in DetachablePanel.axaml.cs.
///
/// Usage in AXAML:
///   <Border b:RightClickCommandBehavior.Command="{Binding SomeRightClickCommand}" />
/// </summary>
public static class RightClickCommandBehavior
{
    /// <summary>
    /// The command to execute when the control is right-clicked.
    /// The command parameter is the PointerPressedEventArgs.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "Command", typeof(RightClickCommandBehavior));

    /// <summary>Gets the right-click command.</summary>
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    /// <summary>Sets the right-click command.</summary>
    public static void SetCommand(Control c, ICommand? value) => c.SetValue(CommandProperty, value);

    static RightClickCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    /// <summary>
    /// Wires (or unwires) the PointerPressed listener when the Command property changes.
    /// </summary>
    private static void OnCommandChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.PointerPressed -= OnPointerPressed;
        if (e.NewValue is ICommand)
            c.PointerPressed += OnPointerPressed;
    }

    /// <summary>
    /// Handles PointerPressed: checks for right-button press and executes the command.
    /// </summary>
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c)
            return;

        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
            return;

        var cmd = GetCommand(c);
        if (cmd?.CanExecute(e) == true)
            cmd.Execute(e);
    }
}

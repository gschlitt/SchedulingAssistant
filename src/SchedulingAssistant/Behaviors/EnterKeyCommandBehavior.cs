using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that executes a command when the Enter key is pressed on a control.
/// Commonly used on ListBox to trigger an edit action when an item is selected and Enter is pressed.
///
/// The command parameter is the control's currently-selected item (for ListBox/Selector)
/// or null for non-selector controls.
///
/// Usage in AXAML:
///   <ListBox b:EnterKeyCommandBehavior.Command="{Binding EditCommand}" />
/// </summary>
public static class EnterKeyCommandBehavior
{
    /// <summary>
    /// The command to execute when Enter is pressed.
    /// For Selector-based controls, the command parameter is the selected item.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "Command", typeof(EnterKeyCommandBehavior));

    /// <summary>Gets the command bound to the control.</summary>
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    /// <summary>Sets the command bound to the control.</summary>
    public static void SetCommand(Control c, ICommand? value) => c.SetValue(CommandProperty, value);

    static EnterKeyCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    /// <summary>
    /// Wires (or unwires) the KeyDown listener when the Command property changes.
    /// </summary>
    private static void OnCommandChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.KeyDown -= OnKeyDown;

        if (e.NewValue is ICommand)
            c.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Handles KeyDown: if Enter is pressed, extracts the selected item from Selector-based
    /// controls and executes the command with it as the parameter.
    /// </summary>
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return || sender is not Control c)
            return;

        var cmd = GetCommand(c);
        if (cmd is null)
            return;

        // For Selector-based controls (ListBox, etc.), pass the selected item.
        var parameter = (c as SelectingItemsControl)?.SelectedItem;

        if (cmd.CanExecute(parameter))
        {
            cmd.Execute(parameter);
            e.Handled = true;
        }
    }
}

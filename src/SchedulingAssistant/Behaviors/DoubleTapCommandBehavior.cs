using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that executes a command when a control is double-tapped.
/// Commonly used on ListBox to trigger an edit action when an item is double-clicked.
///
/// The command parameter is the control's currently-selected item (for ListBox/Selector)
/// or null for non-selector controls.
///
/// This replaces the OnListBoxDoubleTapped code-behind handler in SectionListView.axaml.cs.
///
/// Usage in AXAML:
///   <ListBox b:DoubleTapCommandBehavior.Command="{Binding EditCommand}" />
/// </summary>
public static class DoubleTapCommandBehavior
{
    /// <summary>
    /// The command to execute on double-tap.
    /// For Selector-based controls, the command parameter is the selected item.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "Command", typeof(DoubleTapCommandBehavior));

    /// <summary>Gets the command bound to the control.</summary>
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    /// <summary>Sets the command bound to the control.</summary>
    public static void SetCommand(Control c, ICommand? value) => c.SetValue(CommandProperty, value);

    static DoubleTapCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    /// <summary>
    /// Wires (or unwires) the DoubleTapped listener when the Command property changes.
    /// </summary>
    private static void OnCommandChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.DoubleTapped -= OnDoubleTapped;

        if (e.NewValue is ICommand)
            c.DoubleTapped += OnDoubleTapped;
    }

    /// <summary>
    /// Handles the DoubleTapped event: extracts the selected item from Selector-based
    /// controls and executes the command with it as the parameter.
    /// </summary>
    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control c)
            return;

        var cmd = GetCommand(c);
        if (cmd is null)
            return;

        // For Selector-based controls (ListBox, etc.), pass the selected item.
        var parameter = (c as SelectingItemsControl)?.SelectedItem;

        if (cmd.CanExecute(parameter))
            cmd.Execute(parameter);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior for ListBox controls that converts a single-select action
/// into a command invocation. When an item is selected, the behavior:
///   1. Extracts the item (expected to have an "Id" property via reflection).
///   2. Clears the ListBox selection immediately to prevent Avalonia's selection
///      model from crashing when the backing collection is rebuilt.
///   3. Unchecks an optional ToggleButton (e.g. the popup trigger).
///   4. Executes the bound ICommand with the extracted Id.
///
/// This replaces the three identical SelectionChanged code-behind handlers
/// (OnInstructorOverlayChanged, OnRoomOverlayChanged, OnTagOverlayChanged)
/// that were in GridFilterView.axaml.cs.
///
/// Usage in AXAML:
///   <ListBox b:SelectionCommandBehavior.Command="{Binding SomeCommand}"
///            b:SelectionCommandBehavior.DismissToggle="{Binding #MyToggle}" />
/// </summary>
public static class SelectionCommandBehavior
{
    /// <summary>
    /// The command to execute when an item is selected.
    /// The command parameter is the Id property of the selected item.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<ListBox, ICommand?>(
            "Command", typeof(SelectionCommandBehavior));

    /// <summary>
    /// Optional ToggleButton to uncheck when a selection is made
    /// (typically the popup trigger that should close after selection).
    /// </summary>
    public static readonly AttachedProperty<ToggleButton?> DismissToggleProperty =
        AvaloniaProperty.RegisterAttached<ListBox, ToggleButton?>(
            "DismissToggle", typeof(SelectionCommandBehavior));

    /// <summary>Gets the command bound to the ListBox.</summary>
    public static ICommand? GetCommand(ListBox lb) => lb.GetValue(CommandProperty);

    /// <summary>Sets the command bound to the ListBox.</summary>
    public static void SetCommand(ListBox lb, ICommand? value) => lb.SetValue(CommandProperty, value);

    /// <summary>Gets the dismiss toggle bound to the ListBox.</summary>
    public static ToggleButton? GetDismissToggle(ListBox lb) => lb.GetValue(DismissToggleProperty);

    /// <summary>Sets the dismiss toggle bound to the ListBox.</summary>
    public static void SetDismissToggle(ListBox lb, ToggleButton? value) => lb.SetValue(DismissToggleProperty, value);

    static SelectionCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<ListBox>(OnCommandChanged);
    }

    /// <summary>
    /// Wires (or unwires) the SelectionChanged listener when the Command property changes.
    /// </summary>
    private static void OnCommandChanged(ListBox lb, AvaloniaPropertyChangedEventArgs e)
    {
        // Remove previous handler to avoid double-subscription.
        lb.SelectionChanged -= OnSelectionChanged;

        if (e.NewValue is ICommand)
            lb.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>
    /// Handles the SelectionChanged event: extracts the Id from the selected item,
    /// clears the selection, closes the toggle, and executes the command.
    /// </summary>
    private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || sender is not ListBox lb)
            return;

        var selected = e.AddedItems[0];
        // Extract the Id via reflection — FilterItemViewModel.Id is the expected property.
        var id = selected?.GetType().GetProperty("Id")?.GetValue(selected) as string;

        // Clear selection before executing the command to prevent Avalonia's
        // selection model from crashing when the backing collection is rebuilt.
        lb.SelectedItem = null;

        // Dismiss the popup by unchecking the toggle.
        var toggle = GetDismissToggle(lb);
        if (toggle is not null)
            toggle.IsChecked = false;

        // Execute the command.
        var cmd = GetCommand(lb);
        if (cmd?.CanExecute(id) == true)
            cmd.Execute(id);
    }
}

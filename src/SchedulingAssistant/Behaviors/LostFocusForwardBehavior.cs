using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Windows.Input;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that listens for the bubbling LostFocus event from any descendant
/// whose Name matches a specified target, and executes a command when it fires.
///
/// This solves the problem of attaching to controls that live inside DataTemplates
/// and cannot be referenced directly. The behavior registers on a parent control and
/// filters the bubbled event by the source control's Name property.
///
/// This replaces the OnAnyLostFocus code-behind handler in SectionListView.axaml.cs,
/// which intercepted LostFocus from "SectionCodeBox" inside a DataTemplate to trigger
/// the section code uniqueness validation.
///
/// Usage in AXAML:
///   <UserControl b:LostFocusForwardBehavior.TargetName="SectionCodeBox"
///                b:LostFocusForwardBehavior.Command="{Binding EditVm.CommitSectionCodeCommand}" />
/// </summary>
public static class LostFocusForwardBehavior
{
    /// <summary>
    /// The Name of the control whose LostFocus event should be intercepted.
    /// Only bubbled LostFocus events from controls with this name will trigger the command.
    /// </summary>
    public static readonly AttachedProperty<string?> TargetNameProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "TargetName", typeof(LostFocusForwardBehavior));

    /// <summary>
    /// The command to execute when the target control loses focus.
    /// Called with no parameter.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "Command", typeof(LostFocusForwardBehavior));

    /// <summary>Gets the target control name.</summary>
    public static string? GetTargetName(Control c) => c.GetValue(TargetNameProperty);

    /// <summary>Sets the target control name.</summary>
    public static void SetTargetName(Control c, string? value) => c.SetValue(TargetNameProperty, value);

    /// <summary>Gets the command to execute.</summary>
    public static ICommand? GetCommand(Control c) => c.GetValue(CommandProperty);

    /// <summary>Sets the command to execute.</summary>
    public static void SetCommand(Control c, ICommand? value) => c.SetValue(CommandProperty, value);

    static LostFocusForwardBehavior()
    {
        // We wire up when either property is set. The handler checks both at runtime.
        TargetNameProperty.Changed.AddClassHandler<Control>(OnPropertyChanged);
        CommandProperty.Changed.AddClassHandler<Control>(OnPropertyChanged);
    }

    /// <summary>
    /// Adds or removes the LostFocus handler based on whether both TargetName and Command
    /// are set. Uses AddHandler with RoutingStrategies.Bubble to catch events from
    /// deeply nested DataTemplate content.
    /// </summary>
    private static void OnPropertyChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        // Always remove first to avoid double-subscription.
        c.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);

        var name = GetTargetName(c);
        var cmd = GetCommand(c);

        if (!string.IsNullOrEmpty(name) && cmd is not null)
            c.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Filters the bubbled LostFocus event by the source control's Name, then
    /// executes the command if it matches.
    /// </summary>
    private static void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is not Control parent)
            return;

        var targetName = GetTargetName(parent);
        if (e.Source is Control source && source.Name == targetName)
        {
            var cmd = GetCommand(parent);
            if (cmd?.CanExecute(null) == true)
                cmd.Execute(null);
        }
    }
}

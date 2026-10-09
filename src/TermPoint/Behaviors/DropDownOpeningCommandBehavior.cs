using Avalonia;
using Avalonia.Controls;
using System.ComponentModel;
using System.Windows.Input;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior that executes a command just before an <see cref="AutoCompleteBox"/>
/// opens its suggestion dropdown (the control's <see cref="AutoCompleteBox.DropDownOpening"/>
/// event).
///
/// <b>The rule this behavior exists to support:</b> a suggestion list is rebuilt only when its
/// own dropdown opens — never as a side effect of committing some other field. Commit-time
/// rebuilds are unsafe because an <see cref="AutoCompleteBox"/> reacts to every change in its
/// bound <c>ItemsSource</c> by rebuilding its internal dropdown list.
///
/// <b>The Avalonia 12.1 "lost selection" mechanism:</b> when the user mouse-picks an item, the
/// control keeps that item selected in its internal list. Since Avalonia 12.1, a collection
/// Reset (e.g. <c>Clear()</c> on the bound collection) that removes the selected item raises
/// <c>SelectionChanged</c> with a null selection. The control reacts by setting
/// <c>SelectedItem = null</c> and falling back to <c>Text = SearchText</c> — the last
/// <em>typed</em> text, which is "" after a mouse pick. A two-way <c>Text</c> binding then
/// writes "" back to the view model and the field blanks itself. In the meeting editor this
/// happened to the Start box when a Length was picked (and vice-versa), because each commit
/// rebuilt the sibling's list. The same family of rebuild-mid-pick problems also produced an
/// <see cref="ArgumentOutOfRangeException"/> inside the control (spec item 22).
///
/// <b>Why DropDownOpening is the safe moment:</b> before the control raises
/// <c>DropDownOpening</c> it has already run its populate step, which sets
/// <c>SearchText = Text</c>. So if the handler mutates this box's own <c>ItemsSource</c> and
/// the control falls back to <c>Text = SearchText</c>, it writes back the very text it is
/// already showing — a harmless no-op. No other field's list is touched, and nothing is
/// rebuilt while a pick is still being committed.
///
/// The command is executed only when <see cref="ICommand.CanExecute"/> allows it. The event is
/// a plain CLR event on the control (not a routed event), so the handler is attached directly
/// to the control carrying the property.
///
/// Usage in AXAML:
///   <AutoCompleteBox b:DropDownOpeningCommandBehavior.Command="{Binding RefreshStartTimesCommand}" />
/// </summary>
public static class DropDownOpeningCommandBehavior
{
    /// <summary>The command to execute just before the attached control's dropdown opens.</summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, ICommand?>(
            "Command", typeof(DropDownOpeningCommandBehavior));

    /// <summary>Gets the command attached to the specified AutoCompleteBox.</summary>
    public static ICommand? GetCommand(AutoCompleteBox c) => c.GetValue(CommandProperty);

    /// <summary>Sets the command attached to the specified AutoCompleteBox.</summary>
    public static void SetCommand(AutoCompleteBox c, ICommand? value) => c.SetValue(CommandProperty, value);

    static DropDownOpeningCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<AutoCompleteBox>(OnCommandChanged);
    }

    /// <summary>
    /// Re-wires the <see cref="AutoCompleteBox.DropDownOpening"/> subscription whenever the
    /// attached command changes. Always unsubscribes first so repeated changes never
    /// double-subscribe, then subscribes only when the new value is an <see cref="ICommand"/>.
    /// </summary>
    /// <param name="acb">The AutoCompleteBox whose attached command changed.</param>
    /// <param name="e">Change details; <see cref="AvaloniaPropertyChangedEventArgs.NewValue"/> is the new command.</param>
    private static void OnCommandChanged(AutoCompleteBox acb, AvaloniaPropertyChangedEventArgs e)
    {
        // Always remove first to avoid double-subscription when the property is updated.
        acb.DropDownOpening -= OnDropDownOpening;

        if (e.NewValue is ICommand)
            acb.DropDownOpening += OnDropDownOpening;
    }

    /// <summary>
    /// Executes the attached command, if it allows execution, as the dropdown is about to open.
    /// The event is never cancelled — the dropdown still opens; the command merely gets a chance
    /// to refresh the suggestion list first.
    /// </summary>
    /// <param name="sender">The AutoCompleteBox that is opening its dropdown.</param>
    /// <param name="e">Cancel args for the opening; deliberately left untouched.</param>
    private static void OnDropDownOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not AutoCompleteBox acb) return;
        var cmd = GetCommand(acb);
        if (cmd?.CanExecute(null) == true)
            cmd.Execute(null);
    }
}

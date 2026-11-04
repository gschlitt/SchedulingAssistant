using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using System.Windows.Input;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior that executes a command when the flyout owned by a <see cref="Button"/> closes.
///
/// <b>Why it attaches to the Button, not the Flyout:</b> a <see cref="FlyoutBase"/> is not part of the visual or
/// logical tree, so a binding placed on one cannot see the row's DataContext. The Button can, so the command is
/// bound there and the behavior listens to <c>button.Flyout.Closed</c> on its behalf.
///
/// <b>Why it waits for the visual tree:</b> in AXAML the <c>Flyout</c> property element may be applied after the
/// attached property, so the flyout is looked up only when the button joins the tree (by then both are set).
/// Rows in a virtualized grid come and go, so the subscription is removed again when the button leaves the tree.
///
/// Usage in AXAML:
///   <Button b:FlyoutClosedCommandBehavior.Command="{Binding CommitCommand}"> <Button.Flyout>…</Button.Flyout> </Button>
/// </summary>
public static class FlyoutClosedCommandBehavior
{
    /// <summary>The command to execute when the button's flyout has closed.</summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Button, ICommand?>("Command", typeof(FlyoutClosedCommandBehavior));

    /// <summary>Gets the command attached to the specified button.</summary>
    public static ICommand? GetCommand(Button b) => b.GetValue(CommandProperty);

    /// <summary>Sets the command attached to the specified button.</summary>
    public static void SetCommand(Button b, ICommand? value) => b.SetValue(CommandProperty, value);

    /// <summary>
    /// One live subscription: the flyout listened to and the handler attached to it, kept so exactly that handler
    /// can be removed later even if the button's <see cref="Button.Flyout"/> was replaced meanwhile.
    /// </summary>
    private sealed record Subscription(FlyoutBase Flyout, EventHandler Handler);

    /// <summary>The live subscription of each button. Weak so recycled rows can be collected.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, Subscription> Subscribed = new();

    static FlyoutClosedCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Button>(OnCommandChanged);
    }

    /// <summary>
    /// Wires or unwires the tree-attachment handlers when a command is set or cleared, and subscribes straight
    /// away if the button is already in the tree.
    /// </summary>
    /// <param name="button">The button whose attached command changed.</param>
    /// <param name="e">Change details; <see cref="AvaloniaPropertyChangedEventArgs.NewValue"/> is the new command.</param>
    private static void OnCommandChanged(Button button, AvaloniaPropertyChangedEventArgs e)
    {
        // Remove first so repeated changes never double-subscribe.
        button.AttachedToVisualTree -= OnAttached;
        button.DetachedFromVisualTree -= OnDetached;
        Unsubscribe(button);

        if (e.NewValue is not ICommand) return;

        button.AttachedToVisualTree += OnAttached;
        button.DetachedFromVisualTree += OnDetached;
        if (button.IsAttachedToVisualTree()) Subscribe(button);
    }

    /// <summary>Subscribes to the button's flyout once the button is in the tree.</summary>
    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Button b) Subscribe(b);
    }

    /// <summary>Drops the flyout subscription when the button leaves the tree.</summary>
    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Button b) Unsubscribe(b);
    }

    /// <summary>Listens to the button's current flyout (if any), replacing any earlier subscription.</summary>
    /// <param name="button">The button owning the flyout.</param>
    private static void Subscribe(Button button)
    {
        Unsubscribe(button);
        if (button.Flyout is not { } flyout) return;

        // The handler closes over the button, so no back-reference from the flyout is needed.
        void Handler(object? sender, EventArgs e)
        {
            var cmd = GetCommand(button);
            if (cmd?.CanExecute(null) == true)
                cmd.Execute(null);
        }

        flyout.Closed += Handler;
        Subscribed.Add(button, new Subscription(flyout, Handler));
    }

    /// <summary>Stops listening to the flyout this button was subscribed to, if any.</summary>
    /// <param name="button">The button owning the flyout.</param>
    private static void Unsubscribe(Button button)
    {
        if (!Subscribed.TryGetValue(button, out var sub)) return;
        sub.Flyout.Closed -= sub.Handler;
        Subscribed.Remove(button);
    }
}

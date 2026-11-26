using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TermPoint.Behaviors;

/// <summary>A view model that wants to know when the pointer enters or leaves the control showing it.</summary>
public interface IHoverTarget
{
    /// <summary>Called when the pointer enters (true) or leaves (false) the control whose DataContext this is.</summary>
    /// <param name="isHovered">True on enter; false on leave.</param>
    void OnHoverChanged(bool isHovered);
}

/// <summary>
/// Attached behaviors for lists shown in a flyout (the undo history list):
/// <list type="bullet">
///   <item><c>IsEnabled</c> tells the control's DataContext, when it is an <see cref="IHoverTarget"/>, that the pointer
///   entered or left, so the view model can highlight more than the one line under the pointer.</item>
///   <item><c>ClosesFlyout</c> closes the popup the control sits in when a button inside it is clicked (a Flyout does
///   not close on its own when its content's buttons are clicked).</item>
/// </list>
/// Usage: <c>&lt;Button b:HoverTracking.IsEnabled="True" b:HoverTracking.ClosesFlyout="True" /&gt;</c>
/// </summary>
public static class HoverTracking
{
    /// <summary>When true, pointer enter and leave are reported to the control's <see cref="IHoverTarget"/> DataContext.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(HoverTracking));

    /// <summary>When true, a button click inside the control closes the popup it sits in.</summary>
    public static readonly AttachedProperty<bool> ClosesFlyoutProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ClosesFlyout", typeof(HoverTracking));

    /// <summary>Gets IsEnabled.</summary>
    /// <param name="c">The control.</param>
    /// <returns>The value.</returns>
    public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);

    /// <summary>Sets IsEnabled.</summary>
    /// <param name="c">The control.</param>
    /// <param name="value">The value.</param>
    public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

    /// <summary>Gets ClosesFlyout.</summary>
    /// <param name="c">The control.</param>
    /// <returns>The value.</returns>
    public static bool GetClosesFlyout(Control c) => c.GetValue(ClosesFlyoutProperty);

    /// <summary>Sets ClosesFlyout.</summary>
    /// <param name="c">The control.</param>
    /// <param name="value">The value.</param>
    public static void SetClosesFlyout(Control c, bool value) => c.SetValue(ClosesFlyoutProperty, value);

    static HoverTracking()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.PointerEntered -= OnEntered;
            c.PointerExited -= OnExited;
            if (e.NewValue is true)
            {
                c.PointerEntered += OnEntered;
                c.PointerExited += OnExited;
            }
        });
        ClosesFlyoutProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.RemoveHandler(Button.ClickEvent, OnClick);
            if (e.NewValue is true)
                c.AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble, handledEventsToo: true);
        });
    }

    /// <summary>Reports the pointer entering.</summary>
    private static void OnEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: IHoverTarget t }) t.OnHoverChanged(true);
    }

    /// <summary>Reports the pointer leaving.</summary>
    private static void OnExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: IHoverTarget t }) t.OnHoverChanged(false);
    }

    /// <summary>
    /// Closes the popup the clicked control sits in (walking up the logical tree to it), once the click is finished.
    /// <para>Posted, not immediate: <c>Button.OnClick</c> raises Click first and reads <c>Command</c> after. Closing the
    /// popup inside Click detaches the list from the flyout's DataContext, so the button's Command binding goes null and
    /// the click does nothing (found in Greg's hand check, 2026-11-26).</para>
    /// </summary>
    private static void OnClick(object? sender, RoutedEventArgs e)
    {
        for (var current = sender as StyledElement; current is not null; current = current.Parent)
        {
            if (current is Popup popup)
            {
                Dispatcher.UIThread.Post(() => popup.Close());
                return;
            }
        }
    }
}

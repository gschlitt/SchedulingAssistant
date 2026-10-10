using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace TermPoint.Behaviors;

/// <summary>
/// App-wide fix that keeps a mouse-wheel gesture inside the popup it started in, so wheeling past
/// the end of a dropdown never scrolls the area underneath it.
///
/// <b>The problem:</b> Avalonia routes events raised inside popup content back out through the
/// popup's owner. A wheel tick over a dropdown therefore bubbles out of the popup to the
/// <see cref="Popup"/>, then to the control that owns it, and on up into any enclosing
/// <see cref="ScrollViewer"/> - in the section editor, the section list's own ScrollViewer. The
/// dropdown's own ScrollViewer marks the tick handled only while it can still move. Once it
/// reaches the end of its range (or when its list is too short to scroll at all) it lets the tick
/// go, so the section list underneath scrolls while the dropdown stays fixed on screen.
///
/// <b>Why the handler sits on the popup host types:</b> every popup's content lives inside a host
/// control, and that host is the single point every wheel event from the popup passes through on
/// its way out. The hosts are <see cref="PopupRoot"/> (a native popup window, used on macOS) and
/// <see cref="OverlayPopupHost"/> (an in-window overlay, used on Windows because the app sets
/// <c>OverlayPopups = true</c>, and in the WASM demo). Both must be covered. The popup's own
/// ScrollViewer sits deeper in the route than the host, so it still scrolls normally and marks the
/// tick handled itself while it can move; the host handler only stops the leftover tick from
/// escaping. The handlers are registered with the default routes (Direct and Bubble) and without
/// <c>handledEventsToo</c>, so a tick that was already handled is simply left alone.
///
/// <b>The ToolTip exception:</b> a <see cref="ToolTip"/> is also shown through a Popup, so its host's
/// content is the ToolTip itself. A tooltip has nothing to scroll, and a user who wheels while the
/// pointer happens to be over one still expects the control it belongs to to scroll. The handler
/// therefore does nothing when the host's content is a ToolTip, which keeps that pass-through
/// behaviour exactly as it was.
///
/// <b>Scope:</b> because the handlers are registered on the host types rather than on individual
/// controls, this covers every ComboBox, AutoCompleteBox, custom Popup + ScrollViewer picker,
/// flyout, context menu and any future popup in the app, with no AXAML wiring.
///
/// <b>Registration:</b> call <see cref="Register"/> once at startup. It is invoked from
/// <c>App.OnFrameworkInitializationCompleted</c>, outside the desktop/browser conditional, so both
/// the desktop app and the WASM demo get it.
///
/// This is unrelated to <see cref="SuppressPopupScrollBehavior"/>, which handles
/// <c>RequestBringIntoView</c>, not wheel events.
///
/// See spec_version_1.2.3.md, item 19.
/// </summary>
public static class PopupWheelContainment
{
    /// <summary>
    /// Guards <see cref="Register"/> so the class handlers are added at most once, however many
    /// times it is called. Class handlers cannot be removed by the caller, so a second
    /// registration would otherwise just stack a duplicate handler.
    /// </summary>
    private static bool _registered;

    /// <summary>
    /// Adds the wheel-containment class handler for both popup host types
    /// (<see cref="PopupRoot"/> and <see cref="OverlayPopupHost"/>). Idempotent: calls after the
    /// first do nothing. Must be called on the UI thread after Avalonia has been initialized.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Default routes (Direct | Bubble) and handledEventsToo = false: the handler runs only for
        // ticks that no control inside the popup has already handled.
        InputElement.PointerWheelChangedEvent.AddClassHandler<PopupRoot>(OnPopupHostWheel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<OverlayPopupHost>(OnPopupHostWheel);
    }

    /// <summary>
    /// Marks a wheel event that reached a popup host as handled so it cannot bubble out of the
    /// popup into the owner's scrolling ancestors. Does nothing when the host is showing a
    /// <see cref="ToolTip"/>, so wheeling over a tooltip still reaches the control it belongs to.
    /// </summary>
    /// <param name="host">
    /// The popup host that received the event. Both <see cref="PopupRoot"/> and
    /// <see cref="OverlayPopupHost"/> derive from <see cref="ContentControl"/>, so one method serves
    /// both; its <see cref="ContentControl.Content"/> is the popup's child.
    /// </param>
    /// <param name="e">The wheel event; <see cref="Avalonia.Interactivity.RoutedEventArgs.Handled"/> is set to true unless the popup is a tooltip.</param>
    private static void OnPopupHostWheel(ContentControl host, PointerWheelEventArgs e)
    {
        // A tooltip has nothing to scroll; let the wheel pass through to its owner as before.
        if (host.Content is ToolTip) return;

        e.Handled = true;
    }
}

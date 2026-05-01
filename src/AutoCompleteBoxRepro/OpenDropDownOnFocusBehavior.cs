using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AutoCompleteBoxRepro;

/// <summary>
/// Attached behavior for <see cref="AutoCompleteBox"/> that provides two UX improvements
/// needed when the control is used inside a <see cref="DataTemplate"/> within an
/// <see cref="Avalonia.Controls.ItemsControl"/>.
///
/// <b>1. Open on click</b><br/>
/// A <see cref="RoutingStrategies.Tunnel"/> <c>PointerPressed</c> handler sets
/// <see cref="AutoCompleteBox.IsDropDownOpen"/> to true whenever the user clicks the control,
/// whether or not it already has focus — so the full preset list appears immediately with no
/// keystroke required. Tab navigation does not generate <c>PointerPressed</c> events, so it
/// naturally avoids triggering the dropdown; see behaviour 2.
///
/// <b>2. Tab-through (manual sibling navigation)</b><br/>
/// Two Avalonia quirks combine to make Tab navigation unreliable for AutoCompleteBox inside a
/// DataTemplate:
/// <list type="bullet">
///   <item>
///     AutoCompleteBox marks the Tab key as <c>Handled</c> when the dropdown is open (it uses
///     Tab to close/commit the popup). Even after our tunnel handler closes the dropdown first,
///     Avalonia's own handler still runs on the bubble phase and may consume the key.
///   </item>
///   <item>
///     AutoCompleteBox is a composite control: the focusable element is its inner TextBox, which
///     sits deeper in the visual tree than its siblings. When Tab propagates from that TextBox,
///     Avalonia computes "next tab stop" from the TextBox's position in the <em>global</em> tree
///     rather than relative to the DataTemplate row. In practice this causes focus to jump
///     completely out of the schedule row — confirmed to land on a <c>GridSplitter</c> in the
///     main window layout.
///   </item>
/// </list>
/// The fix: a <see cref="RoutingStrategies.Tunnel"/> <c>KeyDown</c> handler fires before
/// AutoCompleteBox's own handler. It closes the dropdown, marks the event <c>Handled</c> (so
/// Avalonia's navigation never runs), and explicitly focuses the next/previous focusable sibling
/// by walking the parent <see cref="Panel"/>'s children. This keeps Tab within the row.
///
/// Usage in AXAML:
///   <AutoCompleteBox b:OpenDropDownOnFocusBehavior.IsEnabled="True" ... />
/// </summary>
public static class OpenDropDownOnFocusBehavior
{
    /// <summary>Set to <c>True</c> on an AutoCompleteBox to enable both behaviours.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, bool>(
            "IsEnabled", typeof(OpenDropDownOnFocusBehavior));

    /// <summary>Gets the IsEnabled value from the specified AutoCompleteBox.</summary>
    public static bool GetIsEnabled(AutoCompleteBox c) => c.GetValue(IsEnabledProperty);

    /// <summary>Sets the IsEnabled value on the specified AutoCompleteBox.</summary>
    public static void SetIsEnabled(AutoCompleteBox c, bool value) => c.SetValue(IsEnabledProperty, value);

    static OpenDropDownOnFocusBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<AutoCompleteBox>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(AutoCompleteBox c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        c.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

        if (e.NewValue is true)
        {
            // Tunnel so we run before the inner TextBox marks the event Handled.
            c.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            // Tunnel so we run before the AutoCompleteBox's own Tab handler.
            c.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Open the dropdown on any click. Works whether the control already has focus or not.
        // Skip if already open — let the AutoCompleteBox handle that click normally.
        if (sender is AutoCompleteBox acb && !acb.IsDropDownOpen)
            acb.IsDropDownOpen = true;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AutoCompleteBox acb || e.Key != Key.Tab) return;

        acb.IsDropDownOpen = false;

        // AutoCompleteBox's own Tab handler consumes the key (to close the popup), preventing
        // normal focus navigation. We take ownership: mark Tab handled and move focus manually
        // by walking the parent Panel's children to find the next/previous focusable sibling.
        // This keeps navigation within the schedule row regardless of the global visual tree.
        e.Handled = true;

        var parent = acb.Parent as Panel;
        if (parent is null) return;

        bool reverse = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var siblings = parent.Children.OfType<Control>().ToList();
        int idx = siblings.IndexOf(acb);
        if (idx < 0) return;

        var target = reverse
            ? siblings.Take(idx).LastOrDefault(c => c.Focusable && c.IsVisible && c.IsEnabled)
            : siblings.Skip(idx + 1).FirstOrDefault(c => c.Focusable && c.IsVisible && c.IsEnabled);

        target?.Focus(NavigationMethod.Tab);
    }
}

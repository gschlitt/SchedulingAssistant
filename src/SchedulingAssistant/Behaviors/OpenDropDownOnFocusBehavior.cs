using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior for <see cref="AutoCompleteBox"/> that provides two UX improvements
/// needed when the control is used inside a <see cref="DataTemplate"/> within an
/// <see cref="Avalonia.Controls.ItemsControl"/>.
///
/// <b>1. Open on click</b><br/>
/// When the control receives focus via mouse/touch, <see cref="AutoCompleteBox.IsDropDownOpen"/>
/// is set to true so the full preset list appears immediately — no keystroke required. Tab
/// navigation intentionally does NOT trigger this; see behaviour 2.
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
        c.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        c.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

        if (e.NewValue is true)
        {
            c.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
            // Tunnel so we run before the AutoCompleteBox's own Tab handler.
            c.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private static void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        // Only open on pointer (mouse/touch) focus. Tab navigation should move through
        // fields cleanly without triggering the dropdown.
        if (sender is AutoCompleteBox acb && e.NavigationMethod == NavigationMethod.Pointer)
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

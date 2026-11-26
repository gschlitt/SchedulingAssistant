using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior: when the control becomes visible, keyboard focus moves to its first focusable descendant.
///
/// <para><b>Why.</b> It is for pages that replace one another in place, like the Access tab's list, detail and Check
/// all courses. The click that switched pages was on a control of the page now hidden, and Avalonia moves focus out of
/// a control that is hidden, often to the window itself. Keys such as Escape are then raised on the window and never
/// reach the page now showing, so its own Escape handler (<see cref="DismissBehaviors"/>) would not run. Moving focus
/// into the new page keeps them there, in the main window and in a detached panel's window alike. Focus set this way
/// draws no focus rectangle (only Tab does).</para>
///
/// <para>Usage in AXAML: <c>&lt;DockPanel b:FocusWhenShownBehavior.IsEnabled="True" IsVisible="{Binding IsDetailPage}"&gt;</c></para>
/// </summary>
public static class FocusWhenShownBehavior
{
    /// <summary>Whether the control moves focus into itself each time it becomes visible.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(FocusWhenShownBehavior));

    /// <summary>Gets whether the behavior is on.</summary>
    /// <param name="c">The control.</param>
    /// <returns>True when the control moves focus into itself when shown.</returns>
    public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);

    /// <summary>Turns the behavior on or off.</summary>
    /// <param name="c">The control.</param>
    /// <param name="value">True to move focus into the control each time it becomes visible.</param>
    public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

    static FocusWhenShownBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    /// <summary>Wires (or unwires) the visibility listener when <see cref="IsEnabledProperty"/> changes.</summary>
    /// <param name="c">The control.</param>
    /// <param name="e">The change.</param>
    private static void OnIsEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.PropertyChanged -= OnControlPropertyChanged;
        if (e.NewValue is true)
            c.PropertyChanged += OnControlPropertyChanged;
    }

    /// <summary>
    /// When the control's <see cref="Visual.IsVisibleProperty"/> turns true, focuses the first focusable tab stop
    /// inside it, in visual-tree order (Avalonia 12 keeps its tab-order search internal; for a page whose first
    /// control is its back button the two agree). Nothing happens when it has none.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The property change.</param>
    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || e.NewValue is not true || sender is not Control c)
            return;
        var first = c.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(x =>
            x.Focusable && x.IsTabStop && x.IsEffectivelyEnabled && x.IsEffectivelyVisible);
        first?.Focus();
    }
}

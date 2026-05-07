using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior for semester picker rows. Intercepts left-clicks and routes
/// them to <see cref="SemesterContext.HandleItemClick"/> with modifier-key awareness.
/// Plain click selects exclusively; Ctrl+click (Cmd on macOS) toggles multi-select.
/// Also provides a pointer-hover highlight via PointerEnter/PointerLeave (reliable
/// inside Popups, which have a separate visual tree that breaks style-based :pointerover).
///
/// Usage:
///   Set <c>Context</c> on a parent (e.g. the ItemsControl) and <c>Enabled</c> on each row.
///   The behavior walks the visual tree to find the nearest <c>Context</c> value.
///
///   &lt;ItemsControl b:SemesterClickBehavior.Context="{Binding SemesterContext}" ...&gt;
///     &lt;DataTemplate&gt;
///       &lt;Border b:SemesterClickBehavior.Enabled="True" ... /&gt;
///     &lt;/DataTemplate&gt;
///   &lt;/ItemsControl&gt;
/// </summary>
public static class SemesterClickBehavior
{
    /// <summary>Subtle hover background applied on PointerEnter, cleared on PointerLeave.</summary>
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.Parse("#22000000"));

    /// <summary>
    /// The <see cref="SemesterContext"/> that handles click routing.
    /// Set on a parent element; child rows walk the tree to find it.
    /// </summary>
    public static readonly AttachedProperty<SemesterContext?> ContextProperty =
        AvaloniaProperty.RegisterAttached<Control, SemesterContext?>(
            "Context", typeof(SemesterClickBehavior), inherits: true);

    public static SemesterContext? GetContext(Control c) => c.GetValue(ContextProperty);
    public static void SetContext(Control c, SemesterContext? value) => c.SetValue(ContextProperty, value);

    /// <summary>
    /// When true, wires PointerPressed, PointerEnter, and PointerLeave on this control
    /// to handle semester selection and hover highlighting.
    /// </summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "Enabled", typeof(SemesterClickBehavior));

    public static bool GetEnabled(Control c) => c.GetValue(EnabledProperty);
    public static void SetEnabled(Control c, bool value) => c.SetValue(EnabledProperty, value);

    static SemesterClickBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    /// <summary>
    /// Wires (or unwires) pointer listeners when the Enabled property changes.
    /// </summary>
    private static void OnEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.PointerPressed -= OnPointerPressed;
        c.PointerEntered -= OnPointerEntered;
        c.PointerExited  -= OnPointerExited;

        if (e.NewValue is true)
        {
            c.PointerPressed += OnPointerPressed;
            c.PointerEntered += OnPointerEntered;
            c.PointerExited  += OnPointerExited;
        }
    }

    /// <summary>
    /// Handles left-click: routes to <see cref="SemesterContext.HandleItemClick"/>
    /// with Ctrl/Cmd modifier detection for multi-select.
    /// </summary>
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c)
            return;

        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;

        if (c.DataContext is not SemesterCheckItem item)
            return;

        var context = GetContext(c) ?? FindContextInTree(c);
        if (context == null)
            return;

        bool isMultiSelect = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                          || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        context.HandleItemClick(item, isMultiSelect);
        e.Handled = true;
    }

    /// <summary>Applies a subtle highlight when the pointer enters a row.</summary>
    private static void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
            border.Background = HoverBrush;
    }

    /// <summary>Restores transparent background when the pointer leaves a row.</summary>
    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
            border.Background = Brushes.Transparent;
    }

    /// <summary>Walks up the visual tree looking for an ancestor with Context set.</summary>
    private static SemesterContext? FindContextInTree(Control c)
    {
        var current = c.GetVisualParent();
        while (current is Control ctrl)
        {
            var ctx = GetContext(ctrl);
            if (ctx != null) return ctx;
            current = ctrl.GetVisualParent();
        }
        return null;
    }
}

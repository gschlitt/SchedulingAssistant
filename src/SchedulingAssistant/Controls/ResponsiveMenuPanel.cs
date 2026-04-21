using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SchedulingAssistant.Controls;

/// <summary>
/// Single-row horizontal panel that dynamically hides low-priority children into
/// a trailing "More…" button when horizontal space is insufficient.
///
/// Children declare their role via two attached properties:
/// <list type="bullet">
///   <item><see cref="IsPriorityProperty"/> — when true, the child never collapses (priority items).</item>
///   <item><see cref="IsMoreButtonProperty"/> — marks the trailing More button; its IsVisible
///   is toggled by the panel based on whether any item has overflowed.</item>
/// </list>
/// Low-priority children collapse right-to-left (in declared order) as width shrinks.
/// Priority children never hide; if their combined desired width exceeds the panel's
/// allocated width, the parent Border's clipping takes over.
/// </summary>
public class ResponsiveMenuPanel : Panel
{
    /// <summary>
    /// When true, the child is never collapsed into the More button.
    /// </summary>
    public static readonly AttachedProperty<bool> IsPriorityProperty =
        AvaloniaProperty.RegisterAttached<ResponsiveMenuPanel, Control, bool>("IsPriority");

    /// <summary>Gets the IsPriority attached-property value.</summary>
    public static bool GetIsPriority(Control c) => c.GetValue(IsPriorityProperty);

    /// <summary>Sets the IsPriority attached-property value.</summary>
    public static void SetIsPriority(Control c, bool value) => c.SetValue(IsPriorityProperty, value);

    /// <summary>
    /// Marks a child as the trailing "More…" button. Exactly one child should set this.
    /// The panel toggles the button's <see cref="Control.IsVisible"/> based on overflow state.
    /// </summary>
    public static readonly AttachedProperty<bool> IsMoreButtonProperty =
        AvaloniaProperty.RegisterAttached<ResponsiveMenuPanel, Control, bool>("IsMoreButton");

    /// <summary>Gets the IsMoreButton attached-property value.</summary>
    public static bool GetIsMoreButton(Control c) => c.GetValue(IsMoreButtonProperty);

    /// <summary>Sets the IsMoreButton attached-property value.</summary>
    public static void SetIsMoreButton(Control c, bool value) => c.SetValue(IsMoreButtonProperty, value);

    // ── state ────────────────────────────────────────────────────────────────

    private readonly List<Control> _hidden = new();

    /// <summary>
    /// Snapshot of currently-hidden low-priority children, in declared (left-to-right) order.
    /// Populated after each measure pass; changes are announced via
    /// <see cref="HiddenOverflowItemsChanged"/>.
    /// </summary>
    public IReadOnlyList<Control> HiddenOverflowItems => _hidden;

    /// <summary>
    /// Raised after a measure pass when the set of hidden children has changed
    /// (reference-equal comparison in declared order).
    /// </summary>
    public event EventHandler? HiddenOverflowItemsChanged;

    // Per-pass flags: which children are currently overflowed? Read during Arrange.
    private readonly HashSet<Control> _overflowedThisPass = new();

    // ── measure / arrange ────────────────────────────────────────────────────

    /// <summary>
    /// Measures children and computes the overflow set. Priority items and the More button
    /// are never overflowed. Low-priority items are overflowed from right to left until the
    /// remaining visible content + More button fits the available width.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        _overflowedThisPass.Clear();

        // Split children into content + optional More button.
        Control? moreButton = null;
        var content = new List<Control>(Children.Count);
        foreach (var child in Children)
        {
            if (child is not Control c) continue;
            if (GetIsMoreButton(c)) moreButton = c;
            else content.Add(c);
        }

        // Initial unconstrained measure to capture each child's desired width.
        var unconstrained = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
            child.Measure(unconstrained);

        // Sum desired widths of currently-visible content children.
        double total = 0;
        foreach (var c in content)
            if (c.IsVisible) total += c.DesiredSize.Width;

        // If the container gave us an infinite width (e.g. inside a ScrollViewer),
        // nothing overflows — skip the collapse loop.
        bool hasFiniteWidth = !double.IsInfinity(availableSize.Width) && !double.IsNaN(availableSize.Width);

        if (hasFiniteWidth && moreButton is not null && total > availableSize.Width)
        {
            double moreWidth = moreButton.DesiredSize.Width;

            // Collapse low-priority items right-to-left (reverse declared order) until fit.
            for (int i = content.Count - 1; i >= 0; i--)
            {
                if (total + moreWidth <= availableSize.Width) break;

                var c = content[i];
                if (!c.IsVisible) continue;
                if (GetIsPriority(c)) continue;

                _overflowedThisPass.Add(c);
                total -= c.DesiredSize.Width;
            }
        }

        // Toggle More button visibility based on whether any item overflowed.
        bool anyOverflowed = _overflowedThisPass.Count > 0;
        if (moreButton is not null && moreButton.IsVisible != anyOverflowed)
            moreButton.IsVisible = anyOverflowed;

        // Publish the hidden list in declared order; raise change event only if it differs.
        var newHidden = new List<Control>(_overflowedThisPass.Count);
        foreach (var c in content)
            if (_overflowedThisPass.Contains(c)) newHidden.Add(c);

        if (!SameOrder(_hidden, newHidden))
        {
            _hidden.Clear();
            _hidden.AddRange(newHidden);
            HiddenOverflowItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        // Compute final desired size.
        double width = 0;
        double height = 0;
        foreach (var c in content)
        {
            if (!c.IsVisible) continue;
            if (_overflowedThisPass.Contains(c)) continue;
            width += c.DesiredSize.Width;
            height = Math.Max(height, c.DesiredSize.Height);
        }
        if (moreButton is not null && moreButton.IsVisible)
        {
            width += moreButton.DesiredSize.Width;
            height = Math.Max(height, moreButton.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    /// <summary>
    /// Arranges visible children in a single horizontal row, left-to-right, at their
    /// desired widths. Overflowed children are arranged at zero size (invisible, inert).
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        Control? moreButton = null;

        foreach (var child in Children)
        {
            if (child is not Control c) continue;
            if (GetIsMoreButton(c)) { moreButton = c; continue; }
            if (!c.IsVisible) continue;

            if (_overflowedThisPass.Contains(c))
            {
                c.Arrange(new Rect(0, 0, 0, 0));
            }
            else
            {
                c.Arrange(new Rect(x, 0, c.DesiredSize.Width, finalSize.Height));
                x += c.DesiredSize.Width;
            }
        }

        if (moreButton is not null)
        {
            if (moreButton.IsVisible)
            {
                moreButton.Arrange(new Rect(x, 0, moreButton.DesiredSize.Width, finalSize.Height));
            }
            else
            {
                moreButton.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        return finalSize;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Reference-equal sequence comparison between two control lists.</summary>
    private static bool SameOrder(List<Control> a, List<Control> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }
}

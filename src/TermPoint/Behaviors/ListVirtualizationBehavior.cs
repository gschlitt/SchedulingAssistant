using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior that switches a <see cref="ListBox"/> between a <b>virtualizing</b> panel and a
/// <b>classic</b> panel at run time, and keeps scroll anchoring in step with the choice. It is the
/// single place that decides what the section list's panel looks like (spec_version_1.2.3.md, item 23).
/// <list type="bullet">
///   <item><b>Virtualized</b> (<c>true</c>): a <see cref="VirtualizingStackPanel"/> builds only the
///         cards on screen, and <see cref="ListScrollAnchorBehavior"/> is enabled to compensate for
///         the panel's scroll drift.</item>
///   <item><b>Not virtualized</b> (<c>false</c>): a plain <see cref="StackPanel"/> builds every card,
///         and anchoring is disabled (a classic panel does not drift).</item>
/// </list>
/// Both panels carry <see cref="SuppressPopupScrollBehavior"/>, so incidental
/// <c>RequestBringIntoView</c> requests from the cards (focus changes, ComboBox clicks) are swallowed
/// before the list's own ScrollContentPresenter scrolls on them. Code that needs to scroll the list
/// must use <see cref="ListScrollAnchorBehavior.ScrollItemIntoView"/> in either mode.
///
/// <b>Why C# builds the panels:</b> one attribute in AXAML then replaces an <c>ItemsPanel</c> block
/// per mode, and the two modes cannot drift apart. The property is nullable and has no default, so
/// the first bound value (even <c>false</c>) always counts as a change and sets the panel; the
/// ListBox's default panel (itself virtualizing) is never left in place by accident.
///
/// Changing the value later rebuilds the panel (Avalonia's ItemsPresenter handles an
/// <c>ItemsPanel</c> change by recreating it and all containers) and re-reveals the selected card,
/// because the old scroll offset means something else in the new panel.
///
/// Usage in AXAML (add xmlns:b="using:TermPoint.Behaviors"):
///   <![CDATA[<ListBox b:ListVirtualizationBehavior.IsVirtualized="{Binding IsListVirtualized}" ...>]]>
/// and give the ListBox no <c>ItemsPanel</c> of its own.
/// </summary>
public static class ListVirtualizationBehavior
{
    /// <summary>
    /// True to virtualize the list (virtualizing panel plus scroll anchoring), false for the classic
    /// panel. Null (unset) leaves the ListBox's own panel alone.
    /// </summary>
    public static readonly AttachedProperty<bool?> IsVirtualizedProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool?>(
            "IsVirtualized", typeof(ListVirtualizationBehavior));

    /// <summary>Gets whether the list is set to virtualize (null when unset).</summary>
    public static bool? GetIsVirtualized(ListBox c) => c.GetValue(IsVirtualizedProperty);

    /// <summary>Sets whether the list virtualizes.</summary>
    public static void SetIsVirtualized(ListBox c, bool? value) => c.SetValue(IsVirtualizedProperty, value);

    /// <summary>Builds the virtualizing panel, carrying the popup-scroll suppression.</summary>
    private static readonly ITemplate<Panel?> VirtualizingPanelTemplate =
        new FuncTemplate<Panel?>(() => WithSuppression(new VirtualizingStackPanel()));

    /// <summary>Builds the classic, non-virtualizing panel, carrying the popup-scroll suppression.</summary>
    private static readonly ITemplate<Panel?> ClassicPanelTemplate =
        new FuncTemplate<Panel?>(() => WithSuppression(new StackPanel()));

    static ListVirtualizationBehavior()
    {
        IsVirtualizedProperty.Changed.AddClassHandler<ListBox>(OnIsVirtualizedChanged);
    }

    /// <summary>Turns on <see cref="SuppressPopupScrollBehavior"/> for a freshly built panel.</summary>
    /// <param name="panel">The panel to configure.</param>
    /// <returns>The same panel, for use in a template factory.</returns>
    private static Panel WithSuppression(Panel panel)
    {
        SuppressPopupScrollBehavior.SetIsEnabled(panel, true);
        return panel;
    }

    /// <summary>
    /// Applies the chosen mode: swaps the panel, switches anchoring to match, and (when something is
    /// selected) re-reveals it once the new panel has laid out.
    /// </summary>
    /// <param name="list">The ListBox whose attached property changed.</param>
    /// <param name="e">Change details; <see cref="AvaloniaPropertyChangedEventArgs.NewValue"/> is the new value.</param>
    private static void OnIsVirtualizedChanged(ListBox list, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not bool virtualized) return; // cleared: leave the list as it is

        list.ItemsPanel = virtualized ? VirtualizingPanelTemplate : ClassicPanelTemplate;
        ListScrollAnchorBehavior.SetIsEnabled(list, virtualized);

        if (list.SelectedItem is { } selected)
            Dispatcher.UIThread.Post(
                () => ListScrollAnchorBehavior.ScrollItemIntoView(list, selected),
                DispatcherPriority.Background);
    }
}

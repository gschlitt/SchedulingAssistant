using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior that unconditionally suppresses <see cref="Control.RequestBringIntoViewEvent"/>
/// to prevent ComboBox popup mispositioning in WASM.
///
/// In WASM, when a <see cref="ComboBox"/> inside a <see cref="ListBox"/> is clicked,
/// two <c>RequestBringIntoView</c> events fire in quick succession — one from the
/// <see cref="ListBoxItem"/> and one from the <see cref="ComboBox"/> itself. Each
/// causes the parent <see cref="ScrollViewer"/>'s <c>ScrollContentPresenter</c> to
/// scroll, but the popup position was already calculated from pre-scroll coordinates.
/// The popup then appears at the wrong location ("floating above").
///
/// Because suppression is unconditional, <see cref="Control.BringIntoView()"/> does nothing on a
/// list carrying this behavior. Code that needs programmatic scroll-to-item (e.g. cross-view
/// selection sync in the section list) must go through
/// <see cref="ListScrollAnchorBehavior.ScrollItemIntoView"/>, which sets the offset directly or
/// uses <see cref="Bypass"/> as needed.
///
/// <b>The scoped bypass:</b> <see cref="Bypass"/> returns a disposable scope; while at least one
/// scope is open on the UI thread the handler does <em>not</em> mark the event handled, so the
/// request reaches the <c>ScrollContentPresenter</c> as it would without this behavior. It exists
/// for <em>explicit</em> scroll-into-view of a virtualized list: when the target card is not
/// realized, <c>VirtualizingStackPanel.ScrollIntoView</c> has to create the element, estimate where
/// it sits and call <see cref="Control.BringIntoView()"/> on it synchronously. That call raises
/// <c>RequestBringIntoView</c>, which the suppression would swallow. The bypass lets that one
/// deliberate request through while every <em>incidental</em> request (focus changes, the WASM
/// ComboBox clicks the suppression was written for) stays suppressed, because those happen
/// outside the scope. Scopes nest; keep them as short as the one call that needs them.
///
/// The behavior must be placed on a control INSIDE the ScrollViewer (e.g. the content
/// StackPanel), not on the ScrollViewer itself. The <c>ScrollContentPresenter</c> sits
/// between the content and the ScrollViewer in the visual tree; a handler on the
/// ScrollViewer fires too late.
///
/// Known Avalonia bug: GitHub #18203, #19356, #16762.
/// See also WORKAROUNDS.md entry #2.
///
/// Usage in AXAML:
///   <![CDATA[<StackPanel b:SuppressPopupScrollBehavior.IsEnabled="True">]]>
/// </summary>
public static class SuppressPopupScrollBehavior
{
    /// <summary>
    /// Set to <c>True</c> on a control inside a ScrollViewer to suppress all
    /// <c>RequestBringIntoView</c> events before they reach the ScrollContentPresenter.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled", typeof(SuppressPopupScrollBehavior));

    /// <summary>Gets whether <c>RequestBringIntoView</c> suppression is enabled on the control.</summary>
    public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);

    /// <summary>Sets whether <c>RequestBringIntoView</c> suppression is enabled on the control.</summary>
    public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

    static SuppressPopupScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    /// <summary>
    /// Adds or removes the <c>RequestBringIntoView</c> handler when the attached flag changes.
    /// Always removes first, so repeated changes never register the handler twice.
    /// </summary>
    /// <param name="c">The control whose flag changed.</param>
    /// <param name="e">Change details; <see cref="AvaloniaPropertyChangedEventArgs.NewValue"/> is the new flag.</param>
    private static void OnIsEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RemoveHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);

        if (e.NewValue is true)
            c.AddHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
    }

    /// <summary>
    /// Marks the request handled so it never reaches the <c>ScrollContentPresenter</c>, unless a
    /// <see cref="Bypass"/> scope is open on this thread, in which case the request is left alone
    /// and scrolls normally.
    /// </summary>
    /// <param name="sender">The control carrying the behavior.</param>
    /// <param name="e">The request; <c>Handled</c> is set to true unless a bypass is active.</param>
    private static void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (_bypassDepth > 0) return;
        e.Handled = true;
    }

    /// <summary>
    /// Number of open <see cref="Bypass"/> scopes on the current thread. Thread-static because
    /// both <see cref="Bypass"/> and the routed-event handler run on the UI thread, and a
    /// per-thread counter can never be corrupted by another thread (or another test) opening a
    /// scope concurrently.
    /// </summary>
    [ThreadStatic]
    private static int _bypassDepth;

    /// <summary>
    /// The number of bypass scopes currently open on this thread (0 when suppression is in full
    /// effect). Exposed for unit tests, which can check the counter without any UI.
    /// </summary>
    internal static int BypassDepth => _bypassDepth;

    /// <summary>
    /// Temporarily lets <c>RequestBringIntoView</c> through on every control that has this
    /// behavior enabled, so that an explicit programmatic scroll-into-view can work while
    /// incidental requests (outside the scope) stay suppressed.
    ///
    /// Used by <see cref="ListScrollAnchorBehavior.ScrollItemIntoView"/>:
    /// <c>using (SuppressPopupScrollBehavior.Bypass()) listBox.ScrollIntoView(item);</c>
    /// For a virtualized list, <c>VirtualizingStackPanel.ScrollIntoView</c> creates the target
    /// element, estimates its position and calls <c>BringIntoView()</c> on it synchronously, i.e.
    /// before <c>ScrollIntoView</c> returns - so the scope only has to span that one call.
    /// Scopes nest: suppression resumes when the outermost scope is disposed.
    /// </summary>
    /// <returns>
    /// A scope to dispose when the bypass should end. Disposing it more than once is harmless: only
    /// the first call decrements the counter.
    /// </returns>
    public static IDisposable Bypass()
    {
        _bypassDepth++;
        return new ScopeToken(() =>
        {
            if (_bypassDepth > 0) _bypassDepth--;
        });
    }
}

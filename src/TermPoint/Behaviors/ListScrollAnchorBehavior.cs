using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TermPoint.Behaviors;

/// <summary>
/// Keeps one chosen on-screen item of a (virtualized) <see cref="ListBox"/> at the same place in
/// its viewport while the layout shifts underneath it, and provides THE way for code to scroll
/// such a list.
///
/// <b>THE SCROLL RULE: to show an item, set <c>SelectedItem</c> (and let the view react) or call
/// <see cref="ScrollItemIntoView"/>. Never scroll the list's ScrollViewer directly (its Offset,
/// <c>ScrollIntoView</c>, <c>BringIntoView</c>): anchoring will undo it.</b>
/// <see cref="ScrollItemIntoView"/> works with or without anchoring, virtualized or not.
///
/// <b>Why it exists</b> (spec_version_1.2.3.md, items 21 and 23). A
/// <see cref="VirtualizingStackPanel"/> builds only the cards on screen (800 sections: heap 475 MB
/// down to 160 MB) but estimates the rest from the average height of the cards it has built.
/// Whenever a built card changes height (selection thickens its border, a card expands, the inline
/// editor opens) that estimate moves and the content under an <em>unchanged</em> scroll offset
/// slides, so a double-click lands on a different card. The drift is inside the panel; this
/// behavior puts the card the user is working with back where it was.
///
/// <b>The anchor</b> is chosen again on every layout pass, highest priority first. Only attached,
/// visible, arranged containers qualify. The anchor is identified by its <b>data item</b>, not by
/// its container: every pass the container is looked up again, so a card stays the anchor (and drift
/// across it is corrected) even when the panel recycles or re-creates its container, up to all of them
/// at once.
/// <list type="number">
///   <item>The card the user last <b>pressed</b>, captured by a tunnelling PointerPressed before
///         selection can change its height. Forgotten when it leaves the list, when the user scrolls
///         it off screen, or when a press or a selection of a different card replaces it.</item>
///   <item>The <b>selected</b> card, if it is on screen when it first becomes the anchor.</item>
///   <item>The <b>first visible</b> card, sticky while it stays realized (the drift being corrected
///         would otherwise change "the first visible card" in the very pass that needs correcting).</item>
/// </list>
///
/// <b>The algorithm</b> runs from <c>LayoutUpdated</c>. Each pass compares the anchor's top (relative
/// to the list's ScrollViewer) and the vertical offset with the previous pass:
/// <list type="bullet">
///   <item><b>New anchor</b> (a different item): baseline only.</item>
///   <item><b>Scroll</b> (the offset changed and the anchor moved by exactly the opposite amount, or
///         the user is scrolling): start afresh, no correction.</item>
///   <item><b>Drift</b> (the anchor's top is not where it was pinned): set the offset by the
///         difference. Only the top is pinned, so a card growing downward (the editor opening) needs
///         no correction; drift from re-estimated cards <em>above</em> it does. A clamped result (top
///         or bottom of the list) re-baselines on the next pass instead of retrying.</item>
///   <item><b>Chasing:</b> a correction triggers another pass. Until it converges the target stays
///         fixed and offset changes since our last write are not scrolls (the ScrollViewer coerces its
///         offset when the panel's extent estimate shrinks, and treating that as a scroll abandoned
///         the chase). Cost: a deliberate scroll that arrives together with a re-estimation is undone.</item>
///   <item><b>Virtualized away</b> (spec_version_1.2.3.md, item 27): the pinned (pressed or selected)
///         card has no container at all in a pass. A big growth, such as a meeting row added to the open
///         editor, makes the panel re-estimate the cards it has not built, which moves the card's estimated
///         position outside the realized range, and the panel recycles it in that same pass. There is
///         nothing to measure, and falling back to another card would leave the card where the estimates
///         put it, far down the list. So the baseline is kept, the panel is asked to build the card again
///         (<c>ScrollIntoView</c> inside <see cref="SuppressPopupScrollBehavior.Bypass"/>), and the next pass
///         corrects it back to its pinned top as for any drift. Not while the user is scrolling (a card
///         scrolled out of the realized range is meant to be gone), nor while paused or suspended. Each
///         restoration counts in the rate limiter, which bounds a panel that keeps throwing the card out.</item>
///   <item><b>Otherwise:</b> refresh the baseline, so sub-pixel movement never accumulates.</item>
/// </list>
///
/// <b>"The user is scrolling"</b> means: within <see cref="UserScrollGraceMs"/> of a mouse-wheel
/// tick, a <c>ScrollGesture</c> event or PageUp / PageDown (<see cref="IsScrollKey"/>), or while a
/// scrollbar is held. Offset changes then count as a scroll whatever the anchor did; otherwise a long
/// scroll away from the pressed card (which the panel keeps built because it has focus) would be
/// corrected straight back. Touch and pen scrolling is detected from the gesture events: Avalonia's
/// ScrollGestureRecognizer raises <c>ScrollGestureEvent</c> on every pan move and on every
/// animation frame of the fling's inertia until the speed dies away (checked against the 12.1.3
/// source), so the grace period extends through the inertia. The events only bubble and the
/// ScrollContentPresenter marks them handled, hence <c>handledEventsToo</c>.
///
/// <b>Limits (measured, not a promise).</b> One 600 px editor among ten built cards moves the
/// estimate of the whole list by thousands of pixels, and a correction can itself change which cards
/// are built. The chase usually converges (the off-screen probe held the pressed card within 1 px in
/// 17 of 17 start positions) but it is a fixed-point iteration on a function the panel controls.
///
/// <b>Guards.</b> Corrections never nest. If more than <see cref="MaxCorrectionsPerWindow"/>
/// happen within <see cref="OscillationWindowMs"/> the behavior stops correcting and logs once,
/// resuming at the next press, wheel tick or page key, so a layout that fights the correction cannot
/// lock the UI in a layout loop. <see cref="Pause"/> suspends it around explicit scrolls.
///
/// <b>Plumbing.</b> The ScrollViewer is the ListBox's own template ScrollViewer. The pass hook is
/// the ListBox's <c>LayoutUpdated</c>, raised after a complete layout pass of the visual root with
/// the layout manager's "running" flag cleared (checked against the 12.1.3 source), so writing the
/// offset queues a fresh pass and the correction lands before the frame is drawn. It is also raised
/// from the <em>nested</em> pass that <c>VirtualizingStackPanel.ScrollIntoView</c> executes
/// mid-call, which is why <see cref="Pause"/> exists.
///
/// Usage in AXAML (add xmlns:b="using:TermPoint.Behaviors"; the section list sets this through
/// <see cref="ListVirtualizationBehavior"/>):
///   <![CDATA[<ListBox b:ListScrollAnchorBehavior.IsEnabled="True" ...>]]>
/// </summary>
public static class ListScrollAnchorBehavior
{
    /// <summary>
    /// Set to <c>True</c> on a <see cref="ListBox"/> to keep its scroll position anchored as
    /// described in the class summary. Setting it back to <c>False</c> (or removing the control)
    /// unhooks everything.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>(
            "IsEnabled", typeof(ListScrollAnchorBehavior));

    /// <summary>Gets whether scroll anchoring is enabled on the specified ListBox.</summary>
    public static bool GetIsEnabled(ListBox c) => c.GetValue(IsEnabledProperty);

    /// <summary>Sets whether scroll anchoring is enabled on the specified ListBox.</summary>
    public static void SetIsEnabled(ListBox c, bool value) => c.SetValue(IsEnabledProperty, value);

    /// <summary>
    /// Private storage for the per-ListBox state object, so the behavior can find and dispose it
    /// when it is disabled and when <see cref="Pause"/> is called. Held on the control itself, so
    /// it lives and dies with it.
    /// </summary>
    private static readonly AttachedProperty<AnchorState?> StateProperty =
        AvaloniaProperty.RegisterAttached<ListBox, AnchorState?>(
            "State", typeof(ListScrollAnchorBehavior));

    /// <summary>
    /// Counts <see cref="ScrollItemIntoView"/> requests per list, so that a refinement sequence
    /// still in flight for an older request stops when a newer one arrives. Works with anchoring
    /// disabled, which is why it is not part of <see cref="AnchorState"/>.
    /// </summary>
    private static readonly AttachedProperty<int> RevealSerialProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int>(
            "RevealSerial", typeof(ListScrollAnchorBehavior));

    /// <summary>Pixel tolerance for every comparison; sub-half-pixel differences are layout rounding noise.</summary>
    private const double Tolerance = 0.5;

    /// <summary>Corrections allowed inside <see cref="OscillationWindowMs"/> before the behavior gives up.</summary>
    internal const int MaxCorrectionsPerWindow = 20;

    /// <summary>Length, in milliseconds, of the window the oscillation guard counts corrections in.</summary>
    internal const long OscillationWindowMs = 500;

    /// <summary>
    /// How long after the last user-scroll signal (wheel tick, scroll-gesture event, page key,
    /// scrollbar release) an offset change still counts as the user's own scroll.
    /// </summary>
    internal const long UserScrollGraceMs = 400;

    /// <summary>
    /// How many times, at most, <see cref="ScrollItemIntoView"/> re-checks and re-applies the
    /// scroll after the first move. Each refinement is one Background-priority dispatch, so this
    /// bounds the work if the panel's estimates never settle.
    /// </summary>
    private const int MaxRevealRefinements = 4;

    static ListScrollAnchorBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ListBox>(OnIsEnabledChanged);
    }

    /// <summary>
    /// Creates or tears down the per-ListBox state when <see cref="IsEnabledProperty"/> changes.
    /// Always disposes any existing state first so repeated changes never double-subscribe.
    /// </summary>
    /// <param name="list">The ListBox whose attached property changed.</param>
    /// <param name="e">Change details; <see cref="AvaloniaPropertyChangedEventArgs.NewValue"/> is the new flag.</param>
    private static void OnIsEnabledChanged(ListBox list, AvaloniaPropertyChangedEventArgs e)
    {
        list.GetValue(StateProperty)?.Dispose();
        list.ClearValue(StateProperty);

        if (e.NewValue is true)
        {
            var state = new AnchorState(list);
            list.SetValue(StateProperty, state);
            state.Attach();
        }
    }

    /// <summary>
    /// Suspends anchoring on a list until the returned scope is disposed, then discards the
    /// remembered anchor so the next layout pass starts from a fresh baseline. Used by
    /// <see cref="ScrollItemIntoView"/> around its scrolling: such a move forces layout passes
    /// part-way through, and correcting during them would fight the scroll. Scopes nest.
    /// </summary>
    /// <param name="list">The list to pause. If the behavior is not enabled on it, nothing happens.</param>
    /// <returns>A scope to dispose when anchoring should resume; disposing it twice is harmless.</returns>
    public static IDisposable Pause(ListBox list) =>
        list.GetValue(StateProperty)?.Pause() ?? ScopeToken.None;

    /// <summary>
    /// The list's own template ScrollViewer (<c>PART_ScrollViewer</c>, which is what
    /// <see cref="ListBox.Scroll"/> exposes), as opposed to any ScrollViewer further up the tree or
    /// inside the items.
    /// </summary>
    /// <param name="list">The list to inspect.</param>
    /// <returns>The list's ScrollViewer, or null when the template has not been applied.</returns>
    internal static ScrollViewer? GetTemplateScrollViewer(ListBox list) => list.Scroll as ScrollViewer;

    /// <summary>
    /// THE way for code to scroll a list so an item is shown. It applies one rule:
    /// <list type="bullet">
    ///   <item>a card that fits in the viewport is made <em>fully</em> visible, moving the minimum
    ///         distance;</item>
    ///   <item>a card <em>taller</em> than the viewport (typically with its editor open) has its
    ///         <em>top</em> aligned with the viewport top, so the heading and first fields are shown;</item>
    ///   <item>a card already shown that way is left alone.</item>
    /// </list>
    /// <b>Built card:</b> the ScrollViewer offset is set from the card's geometry (see
    /// <see cref="ComputeRevealOffset"/>); setting the offset directly is deliberate, because
    /// <see cref="SuppressPopupScrollBehavior"/> suppresses <c>RequestBringIntoView</c> (a WASM
    /// ComboBox-popup workaround), so <c>BringIntoView</c> would do nothing.
    /// <b>Virtualized away:</b> <see cref="ItemsControl.ScrollIntoView(object)"/> makes the panel
    /// create the card, estimate its position and request that it be brought into view; the call runs
    /// inside <see cref="SuppressPopupScrollBehavior.Bypass"/> so that one deliberate request is not
    /// swallowed.
    /// <b>Refinement:</b> after a move, the panel's idea of where the card is remains an estimate,
    /// and it can re-estimate as soon as the offset changes (a single direct set left the card 900 px
    /// out in one case in five in the off-screen probe). So, at Background priority (after layout), the
    /// built-card rule is applied again, up to <see cref="MaxRevealRefinements"/> times, until it needs
    /// no further move. Anchoring stays paused for the whole sequence, and a newer request for the same
    /// list supersedes a sequence still in flight.
    /// Must be called on the UI thread. Does nothing if the list has no applied template, or if the
    /// item is not in the list.
    /// </summary>
    /// <param name="list">The list to scroll. Anchoring may or may not be enabled on it, and its panel may be virtualizing or not.</param>
    /// <param name="item">The data item (not its container) to show.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void ScrollItemIntoView(ListBox list, object item)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(item);

        // A newer request supersedes any refinement sequence still in flight for an older one.
        int serial = list.GetValue(RevealSerialProperty) + 1;
        list.SetValue(RevealSerialProperty, serial);

        // Pauses anchoring, runs the first move, and schedules the refinement. The pause is ended
        // by the refinement sequence, or here if the first move throws (no refinement will run).
        void MoveThenRefine(Action firstMove)
        {
            var pause = Pause(list);
            try { firstMove(); }
            catch { pause.Dispose(); throw; }
            RefineLater(list, item, pause, serial, MaxRevealRefinements);
        }

        if (TryPlanReveal(list, item, out var scroll, out var newOffset))
        {
            // Built. Nothing to do when it is already shown as wanted (or there is nothing to scroll).
            if (scroll is null || newOffset is not { } y) return;
            MoveThenRefine(() => scroll.Offset = scroll.Offset.WithY(y));
        }
        else
        {
            // Virtualized away: let the panel realize and approximately place it; refine afterwards.
            MoveThenRefine(() =>
            {
                using (SuppressPopupScrollBehavior.Bypass())
                    list.ScrollIntoView(item);
            });
        }
    }

    /// <summary>
    /// Posts, at Background priority (after the layout caused by the previous move), one refinement
    /// of <see cref="ScrollItemIntoView"/>: if the card is now built but not shown as wanted, move
    /// again and schedule another check; otherwise finish and end the anchoring pause. The pause is
    /// always ended, including when an exception escapes or a newer request has superseded this one.
    /// </summary>
    /// <param name="list">The list being scrolled.</param>
    /// <param name="item">The data item whose card should be shown.</param>
    /// <param name="pause">The open anchoring pause; disposed when the sequence ends.</param>
    /// <param name="serial">The request number this sequence belongs to (see <see cref="RevealSerialProperty"/>).</param>
    /// <param name="attemptsLeft">Refinements still allowed, including this one.</param>
    private static void RefineLater(ListBox list, object item, IDisposable pause, int serial, int attemptsLeft)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                bool again = false;
                try
                {
                    if (list.GetValue(RevealSerialProperty) == serial
                        && TryPlanReveal(list, item, out var scroll, out var newOffset)
                        && scroll is not null && newOffset is { } y)
                    {
                        scroll.Offset = scroll.Offset.WithY(y);
                        again = attemptsLeft > 1;
                    }
                }
                catch
                {
                    pause.Dispose();
                    throw;
                }

                if (again) RefineLater(list, item, pause, serial, attemptsLeft - 1);
                else pause.Dispose();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Works out, without scrolling, whether and where the list must scroll for the item's card to
    /// be shown as described on <see cref="ScrollItemIntoView"/>.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="item">The data item whose card should be shown.</param>
    /// <param name="scroll">The list's ScrollViewer; null when there is none to move.</param>
    /// <param name="newOffset">The vertical offset to set, or null when no scrolling is needed.</param>
    /// <returns>
    /// True when the card is built (whether or not any scrolling is needed); false when it is not
    /// built, so the caller must ask the panel to create it first.
    /// </returns>
    private static bool TryPlanReveal(ListBox list, object item, out ScrollViewer? scroll, out double? newOffset)
    {
        scroll = null;
        newOffset = null;
        if (list.ContainerFromItem(item) is not Control container) return false;

        scroll = GetTemplateScrollViewer(list);
        var topLeft = scroll is null ? null : container.TranslatePoint(default, scroll);
        if (scroll is null || !topLeft.HasValue) return true; // built, but nothing we can scroll

        newOffset = ComputeRevealOffset(
            topLeft.Value.Y, container.Bounds.Height, scroll.Viewport.Height, scroll.Offset.Y);
        return true;
    }

    /// <summary>
    /// Works out the vertical offset needed to show an item in a viewport, or says that nothing
    /// needs to change: an item that fits is made fully visible (minimum move); an item taller
    /// than the viewport has its top aligned with the viewport top. A difference of half a pixel
    /// or less counts as already in place. The result is never below zero; the ScrollViewer
    /// applies the upper clamp itself.
    /// </summary>
    /// <param name="itemTop">The item's top edge relative to the viewport's top edge (negative = above the viewport).</param>
    /// <param name="itemHeight">The item's height.</param>
    /// <param name="viewportHeight">The viewport's height.</param>
    /// <param name="currentOffsetY">The ScrollViewer's current vertical offset.</param>
    /// <returns>The new vertical offset, or null when the item is already shown as described.</returns>
    public static double? ComputeRevealOffset(
        double itemTop, double itemHeight, double viewportHeight, double currentOffsetY)
    {
        if (viewportHeight <= 0 || itemHeight <= 0) return null;

        double delta;
        if (itemHeight > viewportHeight)
            delta = itemTop;                                    // too tall: pin its top to the viewport top
        else if (itemTop < 0)
            delta = itemTop;                                    // clipped at the top: bring its top down to 0
        else if (itemTop + itemHeight > viewportHeight)
            delta = itemTop + itemHeight - viewportHeight;      // clipped at the bottom: align its bottom
        else
            return null;                                        // fully visible already

        if (Math.Abs(delta) <= Tolerance) return null;
        return Math.Max(0, currentOffsetY + delta);
    }

    /// <summary>
    /// Whether a key pressed in the list makes the list's ScrollViewer scroll by itself, so offset
    /// changes right after it are the user's. Only PageUp and PageDown qualify: they are the only keys
    /// the ScrollViewer turns into scrolling (checked against the 12.1.3 source), and without the hint a
    /// page scroll is corrected straight back (probe E6a). Home, End, Up and Down are selection
    /// navigation: the ListBox moves <c>SelectedItem</c> (its own bring-into-view is suppressed) and the
    /// view scrolls through <see cref="ScrollItemIntoView"/>, so nothing needs protecting. A hint only
    /// matters when the offset moves, so adding them would be harmless in the simple case (probe E6b), but
    /// it could make a ScrollViewer offset coercion during a chase look like a user scroll and abandon
    /// the chase.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <returns>True for PageUp and PageDown.</returns>
    internal static bool IsScrollKey(Key key) => key is Key.PageUp or Key.PageDown;

    /// <summary>The priority level an anchor was chosen at (see the class summary).</summary>
    private enum AnchorKind
    {
        /// <summary>The container of the item the user last pressed.</summary>
        Pressed,

        /// <summary>The container of the list's selected item.</summary>
        Selected,

        /// <summary>The first realized container that intersects the viewport (sticky).</summary>
        FirstVisible,
    }

    /// <summary>
    /// A chosen anchor. Its identity is <paramref name="Item"/>; the container is only the one showing
    /// that item when the choice was made, and may be replaced by a new one on the next pass.
    /// </summary>
    /// <param name="Container">The realized container showing <paramref name="Item"/> now (the one to measure).</param>
    /// <param name="Item">The data item being pinned: what makes two choices "the same anchor".</param>
    /// <param name="Kind">The priority level that produced this choice.</param>
    private readonly record struct AnchorChoice(Control Container, object Item, AnchorKind Kind);

    /// <summary>
    /// Counts corrections in a sliding time window and reports when there have been too many.
    /// Separated from the layout code, and taking the time as a parameter, so it can be unit
    /// tested without a UI or a real clock.
    /// </summary>
    internal sealed class CorrectionRateLimiter
    {
        /// <summary>Corrections that may happen inside one window before the limit is exceeded.</summary>
        private readonly int _maxPerWindow;

        /// <summary>Length of the sliding window, in milliseconds.</summary>
        private readonly long _windowMs;

        /// <summary>Timestamps (ms) of the corrections still inside the window, oldest first.</summary>
        private readonly Queue<long> _stamps = new();

        /// <summary>Creates a limiter that tolerates <paramref name="maxPerWindow"/> corrections per <paramref name="windowMs"/>.</summary>
        /// <param name="maxPerWindow">Corrections allowed in one window; one more than this trips the limiter.</param>
        /// <param name="windowMs">Window length in milliseconds.</param>
        public CorrectionRateLimiter(int maxPerWindow, long windowMs)
        {
            _maxPerWindow = maxPerWindow;
            _windowMs = windowMs;
        }

        /// <summary>
        /// Records a correction at the given time and tells whether the limit is now exceeded,
        /// i.e. whether more than the allowed number have happened within the window ending now.
        /// </summary>
        /// <param name="nowMs">The current time in milliseconds (any monotonic clock).</param>
        /// <returns>True when this correction is one too many and should not be performed.</returns>
        public bool RegisterAndCheck(long nowMs)
        {
            _stamps.Enqueue(nowMs);
            while (_stamps.Count > 0 && nowMs - _stamps.Peek() > _windowMs)
                _stamps.Dequeue();
            return _stamps.Count > _maxPerWindow;
        }

        /// <summary>Forgets all recorded corrections (used when the user acts and anchoring resumes).</summary>
        public void Reset() => _stamps.Clear();
    }

    /// <summary>
    /// Everything the behavior knows about one <see cref="ListBox"/>: its subscriptions, the
    /// current anchor and baseline, and the guards. One instance per enabled ListBox, stored in
    /// <see cref="StateProperty"/>. All members run on the UI thread.
    /// </summary>
    private sealed class AnchorState : IDisposable
    {
        /// <summary>The list being anchored.</summary>
        private readonly ListBox _list;

        // ---- Press tracking (anchor priority 1) -------------------------------------------

        /// <summary>
        /// The data item the user last pressed; null when none/forgotten. An item, not a container:
        /// the container showing it is looked up again on every pass (see <see cref="ChooseAnchor"/>).
        /// </summary>
        private object? _pressedItem;

        // ---- Baseline from the previous pass ----------------------------------------------

        /// <summary>
        /// The anchor pinned by the baseline; null when there is no baseline. Only its
        /// <see cref="AnchorChoice.Item"/> and kind are used later; its container may since have been
        /// recycled.
        /// </summary>
        private AnchorChoice? _anchor;

        /// <summary>The anchor's top, relative to the ScrollViewer, that corrections try to preserve.</summary>
        private double _anchorTop;

        /// <summary>The vertical offset the ScrollViewer had when the baseline was taken, or that we last set.</summary>
        private double _lastOffsetY;

        /// <summary>
        /// True while we are chasing a correction we made on a previous pass: <see cref="_anchorTop"/>
        /// is then the fixed target, and offset changes since are not treated as scrolls (see
        /// <see cref="OnLayoutUpdated"/>).
        /// </summary>
        private bool _inChain;

        // ---- Guards -----------------------------------------------------------------------

        /// <summary>True while we are writing the offset; keeps corrections from nesting.</summary>
        private bool _correcting;

        /// <summary>Number of open <see cref="Pause"/> scopes.</summary>
        private int _pauseDepth;

        /// <summary>True after the oscillation guard tripped, until the next press, wheel tick or page key.</summary>
        private bool _suspended;

        /// <summary>True while a press that started on a scrollbar has not yet been released.</summary>
        private bool _scrollbarHeld;

        /// <summary>
        /// Time (<see cref="Environment.TickCount64"/>, ms) until which offset changes still count as
        /// the user's scroll: the grace period after the last wheel tick, scroll-gesture event, page
        /// key or scrollbar release.
        /// </summary>
        private long _userScrollUntilMs;

        /// <summary>Counts corrections in a sliding window to detect a layout that fights them.</summary>
        private readonly CorrectionRateLimiter _limiter =
            new(MaxCorrectionsPerWindow, OscillationWindowMs);

        /// <summary>Creates the state for a list. Call <see cref="Attach"/> to start listening.</summary>
        /// <param name="list">The list to anchor.</param>
        public AnchorState(ListBox list) => _list = list;

        /// <summary>The list's own ScrollViewer; null until the template is applied.</summary>
        private ScrollViewer? Scroll => GetTemplateScrollViewer(_list);

        // ============================ Wiring ================================================

        /// <summary>
        /// Subscribes to everything needed (see <see cref="Wire"/>) and hooks layout immediately when
        /// the control is already attached to the visual tree.
        /// </summary>
        public void Attach()
        {
            Wire(attach: true);
            if (_list.IsAttachedToVisualTree()) HookLayout();
        }

        /// <summary>Unsubscribes everything and forgets all state. Safe to call more than once.</summary>
        public void Dispose()
        {
            Wire(attach: false);
            UnhookLayout();
            ClearPressed();
            Reset();
            _scrollbarHeld = false;
        }

        /// <summary>
        /// Adds or removes every subscription in one place, so the two lists can never diverge.
        /// The routed events use <c>handledEventsToo</c> because we must see everything: pointer
        /// and key events tunnel so we run before the ListBox's own selection logic, and the
        /// scroll-gesture events bubble but are marked handled by the ScrollContentPresenter.
        /// </summary>
        /// <param name="attach">True to subscribe, false to unsubscribe.</param>
        private void Wire(bool attach)
        {
            void Route<TArgs>(RoutedEvent<TArgs> routedEvent, EventHandler<TArgs> handler, RoutingStrategies routes)
                where TArgs : RoutedEventArgs
            {
                if (attach) _list.AddHandler(routedEvent, handler, routes, handledEventsToo: true);
                else _list.RemoveHandler(routedEvent, handler);
            }

            Route(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            Route(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
            Route(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel);
            Route(InputElement.PointerWheelChangedEvent, OnUserScrollInput, RoutingStrategies.Tunnel);
            Route(InputElement.ScrollGestureEvent, OnUserScrollInput, RoutingStrategies.Bubble);
            Route(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

            if (attach)
            {
                _list.AttachedToVisualTree += OnAttachedToVisualTree;
                _list.DetachedFromVisualTree += OnDetachedFromVisualTree;
                _list.SelectionChanged += OnSelectionChanged;
            }
            else
            {
                _list.AttachedToVisualTree -= OnAttachedToVisualTree;
                _list.DetachedFromVisualTree -= OnDetachedFromVisualTree;
                _list.SelectionChanged -= OnSelectionChanged;
            }
        }

        /// <summary>Subscribes to the list's <c>LayoutUpdated</c> (removes first, so it is never subscribed twice).</summary>
        private void HookLayout()
        {
            _list.LayoutUpdated -= OnLayoutUpdated;
            _list.LayoutUpdated += OnLayoutUpdated;
        }

        /// <summary>Unsubscribes from the list's <c>LayoutUpdated</c> (harmless if not subscribed).</summary>
        private void UnhookLayout() => _list.LayoutUpdated -= OnLayoutUpdated;

        /// <summary>Starts listening to layout, from a fresh baseline.</summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">Attachment details (unused).</param>
        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Reset();
            HookLayout();
        }

        /// <summary>Stops listening to layout and forgets the anchor while the list is off screen.</summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">Detachment details (unused).</param>
        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            UnhookLayout();
            ClearPressed();
            Reset();
            _scrollbarHeld = false;
        }

        // ============================ Pause =================================================

        /// <summary>Opens a pause scope; see <see cref="ListScrollAnchorBehavior.Pause"/>.</summary>
        /// <returns>The scope; disposing it closes the pause.</returns>
        public IDisposable Pause()
        {
            _pauseDepth++;
            return new ScopeToken(EndPause);
        }

        /// <summary>Closes one pause scope; when the last closes, the baseline is discarded.</summary>
        private void EndPause()
        {
            if (_pauseDepth > 0) _pauseDepth--;
            if (_pauseDepth == 0) Reset();
        }

        // ============================ User input ============================================

        /// <summary>
        /// Remembers the card the user pressed and captures the baseline immediately, before the
        /// press can change the card's height (selection thickens its border) and before any
        /// layout pass, so that the very first drift after a click is measured against a baseline.
        /// Also lifts an oscillation suspension (a press is the user acting) and ends any stale
        /// user-scroll mark; a press that lands on a scrollbar starts a drag instead.
        /// </summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">The press; its source is walked up to the item container.</param>
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            Resume();

            bool onScrollBar = IsWithinScrollBar(e.Source as Visual);
            _scrollbarHeld = onScrollBar;
            if (!onScrollBar) _userScrollUntilMs = 0;
            if (onScrollBar || _pauseDepth > 0) return;

            var container = FindContainer(e.Source as Visual);
            if (container is null || _list.ItemFromContainer(container) is not { } item) return; // empty space: no anchor

            _pressedItem = item;
            CaptureBaseline();
        }

        /// <summary>A pointer was released; if a scrollbar drag was in progress it ends.</summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">The release (unused).</param>
        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => EndScrollbarDrag();

        /// <summary>The pointer capture was lost (it can end a scrollbar drag without a release being seen).</summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">The event (unused).</param>
        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndScrollbarDrag();

        /// <summary>Ends a scrollbar drag, leaving the usual grace period for the final layout passes.</summary>
        private void EndScrollbarDrag()
        {
            if (!_scrollbarHeld) return;
            _scrollbarHeld = false;
            NoteUserScroll();
        }

        /// <summary>
        /// A mouse-wheel tick or a scroll-gesture event (a touch / pen pan, or one frame of its
        /// inertia) is the user scrolling. Shared by both events because it ignores their arguments.
        /// </summary>
        /// <typeparam name="TArgs">The event's argument type.</typeparam>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">The event (unused).</param>
        private void OnUserScrollInput<TArgs>(object? sender, TArgs e) where TArgs : RoutedEventArgs
        {
            Resume();
            NoteUserScroll();
        }

        /// <summary>A key pressed in the list; a page key is the ScrollViewer scrolling on the user's behalf (see <see cref="IsScrollKey"/>).</summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">The key event.</param>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!IsScrollKey(e.Key)) return;
            Resume();
            NoteUserScroll();
        }

        /// <summary>Opens (or extends) the grace period during which offset changes are the user's.</summary>
        private void NoteUserScroll() =>
            _userScrollUntilMs = Math.Max(_userScrollUntilMs, Environment.TickCount64 + UserScrollGraceMs);

        /// <summary>
        /// Whether the user is scrolling right now: a user-scroll signal arrived within
        /// <see cref="UserScrollGraceMs"/>, or a scrollbar is held. This is how a scroll the user asked
        /// for is told apart from an offset change that is only a consequence of the panel
        /// re-estimating its extent.
        /// </summary>
        private bool IsUserScrolling => _scrollbarHeld || Environment.TickCount64 < _userScrollUntilMs;

        /// <summary>Lifts an oscillation suspension and forgets the correction history.</summary>
        private void Resume()
        {
            _suspended = false;
            _limiter.Reset();
        }

        /// <summary>Whether a visual lies inside one of the list's scrollbars.</summary>
        /// <param name="source">Where a pointer event originated.</param>
        /// <returns>True when the source is a <see cref="ScrollBar"/> or part of one.</returns>
        private bool IsWithinScrollBar(Visual? source)
        {
            for (var v = source; v is not null && !ReferenceEquals(v, _list); v = v.GetVisualParent())
                if (v is ScrollBar) return true;
            return false;
        }

        /// <summary>
        /// Walks up the visual tree from a pointer event's source to the nearest realized
        /// container of this list.
        /// </summary>
        /// <param name="source">Where the pointer event originated.</param>
        /// <returns>The item container (a <see cref="ListBoxItem"/> of this list), or null.</returns>
        private Control? FindContainer(Visual? source)
        {
            for (var v = source; v is not null && !ReferenceEquals(v, _list); v = v.GetVisualParent())
                if (v is ListBoxItem lbi && _list.IndexFromContainer(lbi) >= 0)
                    return lbi;
            return null;
        }

        /// <summary>
        /// The list's selection changed (keyboard, cross-view sync, click). A selection of a
        /// <em>different</em> card than the pressed one replaces the pressed anchor (the user has moved
        /// on, for example by arrowing down after a click). Then re-captures the baseline now, while
        /// the previous layout is still on screen, so the card that just became the anchor is measured
        /// before its selection styling changes its height.
        /// </summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">Selection details. Changes bubbling up from controls inside the cards
        /// (ComboBoxes in the editor) are ignored; only the list's own selection counts.</param>
        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, _list) || _pauseDepth > 0) return;
            if (!ReferenceEquals(_pressedItem, _list.SelectedItem)) ClearPressed();
            CaptureBaseline();
        }

        // ============================ The layout pass =======================================

        /// <summary>
        /// The heart of the behavior; runs after every layout pass of the visual root. See the
        /// class summary for the rules. In short: if the pinned card was just virtualized away, ask the
        /// panel for it back; otherwise pick the anchor; if it is new, or the offset changed and the
        /// anchor moved with it (a scroll), just take a baseline; otherwise, if the anchor's top is not
        /// where it should be (drift), scroll by the difference.
        /// </summary>
        /// <param name="sender">The ListBox.</param>
        /// <param name="e">Unused.</param>
        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (_correcting || _pauseDepth > 0) return;

            // A hidden or not yet templated list has no meaningful geometry (and must never be
            // scrolled to restore a card, so this check comes before the restoration below).
            var scroll = Scroll;
            if (scroll is null || !HasGeometry(scroll))
            {
                Reset();
                return;
            }

            // The pinned card may have been virtualized away in this very pass. This has to be decided
            // BEFORE the anchor is chosen: with no container to measure, TryChooseAnchor would not fail,
            // it would quietly return the first visible card as a fallback, and the baseline would move
            // to that card.
            if (TryRestoreVirtualizedAnchor()) return;

            if (!TryChooseAnchor(scroll, out var choice, out double top))
            {
                Reset();
                return;
            }

            double offsetY = scroll.Offset.Y;

            // A different anchor: baseline only, never a correction. The same ITEM is the same anchor
            // even if its container is a new one (the panel recycled it): its top is then compared
            // with the stored baseline exactly as for an unchanged container.
            bool sameAnchor = _anchor is { } known && ReferenceEquals(choice.Item, known.Item);
            if (!sameAnchor)
            {
                SetBaseline(choice, top, offsetY);
                return;
            }

            // Is the anchor where a mere scroll would have left it?
            //  - While one of OUR corrections is still being chased (_inChain) the only acceptable
            //    place is the pinned _anchorTop: any offset change since our last write is ours, or
            //    a coercion by the ScrollViewer (the extent was re-estimated), not a scroll.
            //  - Otherwise an offset change is taken to be somebody's scroll: scrolling by d moves
            //    the anchor's top by exactly -d. Anything else is the panel re-estimating (drift).
            //  - Unless the USER is scrolling, when an offset change is a scroll whatever the
            //    anchor did, and any chase is over.
            double offsetDelta = offsetY - _lastOffsetY;
            bool moved = Math.Abs(offsetDelta) > Tolerance;
            bool userScroll = IsUserScrolling && moved;
            double expectedTop = _inChain ? _anchorTop : _anchorTop - offsetDelta;
            bool whereAScrollLeftIt = Math.Abs(top - expectedTop) <= Tolerance;

            if (userScroll || (whereAScrollLeftIt && !_inChain && moved))
            {
                // A pure scroll (the user, the scrollbar, other code). Do not fight it. Start
                // afresh: drop a sticky anchor, and forget a pressed card scrolled off screen.
                _anchor = null;
                if (_pressedItem is { } pressed
                    && !(_list.ContainerFromItem(pressed) is Control pc && IntersectsViewport(pc, scroll)))
                    ClearPressed();

                if (TryChooseAnchor(scroll, out var fresh, out double freshTop))
                    SetBaseline(fresh, freshTop, offsetY);
                else
                    Reset();
                return;
            }

            // No movement (or the chase converged): refresh the baseline so sub-pixel changes never accumulate.
            if (whereAScrollLeftIt)
            {
                SetBaseline(choice, top, offsetY);
                return;
            }

            // Drift: the anchor is not where it should be.
            if (_suspended)
            {
                SetBaseline(choice, top, offsetY);
                return;
            }

            if (_limiter.RegisterAndCheck(Environment.TickCount64))
            {
                SuspendForOscillation();
                SetBaseline(choice, top, offsetY);
                return;
            }

            // Put the anchor back where it was pinned (not merely where a scroll would have left it:
            // an offset change that arrived together with drift is as likely to be the ScrollViewer
            // coercing its offset as a deliberate scroll, and the pinned position is the one the user
            // last saw).
            double wanted = offsetY + (top - _anchorTop);
            _correcting = true;
            try
            {
                scroll.Offset = scroll.Offset.WithY(wanted);
            }
            finally
            {
                _correcting = false;
            }

            double actual = scroll.Offset.Y;
            if (Math.Abs(actual - wanted) > Tolerance)
            {
                // Clamped (top or bottom of the list): the target is unreachable. Do not retry;
                // take a fresh baseline on the next pass.
                Reset();
                return;
            }

            // Remember what we set, so the next pass can tell this change from a scroll, and keep
            // chasing the same target: _anchorTop is deliberately left as it was, and the next pass
            // verifies that the anchor got there.
            _lastOffsetY = actual;
            _inChain = true;
        }

        /// <summary>
        /// Handles "the pinned card was virtualized away by a re-estimate" (spec item 27): the baseline's
        /// anchor is the pressed or selected card, the card is still in the list, but the panel has no
        /// container for it. This happens when the card grows a lot (a meeting row added to the open
        /// editor): the panel re-estimates the cards it has not built from an average that now includes
        /// the huge card, which moves the card's estimated position outside the realized range, so the
        /// card is recycled in that same layout pass.
        /// <para>Without this the anchor silently moved to another card and the editor was left wherever
        /// the estimates put it. Instead the baseline (<see cref="_anchor"/>, <see cref="_anchorTop"/>) is
        /// kept exactly as it is, the panel is asked to build the card again with
        /// <c>ScrollIntoView</c> (inside <see cref="SuppressPopupScrollBehavior.Bypass"/>: the request to
        /// bring the new container into view would otherwise be swallowed, see
        /// <see cref="ScrollItemIntoView"/>), and the chase is marked (<see cref="_inChain"/>), so the next
        /// pass, which finds the container, compares its top with the pinned <see cref="_anchorTop"/> and
        /// corrects the difference through the ordinary drift code. If the card's new container lands far
        /// off, that chase corrects it; if the panel throws the card out again, the next pass restores it
        /// again, and the rate limiter (shared with the drift corrections) ends the loop.</para>
        /// <para>Not done, so the ordinary flow runs: while the user is scrolling (a card scrolled away
        /// by the user is legitimately recycled and must not be pulled back, and the user-scroll rules
        /// then forget it), while anchoring is suspended, when the anchor is not the pressed or selected
        /// card, or when the card is no longer in the list. The pressed item is deliberately kept
        /// (<see cref="ClearPressed"/> is not called). The caller has already checked that the list
        /// has geometry, so a hidden list is never scrolled.</para>
        /// </summary>
        /// <returns>
        /// True when the panel was asked to build the card again and this pass is finished; false when the
        /// ordinary flow should go on, because nothing needed restoring or because the rate limiter
        /// tripped (anchoring is then suspended and the ordinary flow takes a fresh baseline).
        /// </returns>
        private bool TryRestoreVirtualizedAnchor()
        {
            if (_suspended || IsUserScrolling) return false;
            if (_anchor is not { Kind: AnchorKind.Pressed or AnchorKind.Selected } pinned) return false;

            // The anchor must still be the card the user is working with (a stale one would pull the
            // list to a card nobody cares about), still be in the list, and really have no container.
            object? current = pinned.Kind == AnchorKind.Pressed ? _pressedItem : _list.SelectedItem;
            if (!ReferenceEquals(pinned.Item, current)) return false;
            if (_list.ContainerFromItem(pinned.Item) is not null) return false;
            if (_list.Items.IndexOf(pinned.Item) < 0) return false;

            if (_limiter.RegisterAndCheck(Environment.TickCount64))
            {
                SuspendForOscillation();
                return false;
            }

            // _correcting: the layout this call runs (and raises LayoutUpdated from) is ours, not a
            // pass to react to.
            _correcting = true;
            try
            {
                using (SuppressPopupScrollBehavior.Bypass())
                    _list.ScrollIntoView(pinned.Item);
            }
            finally
            {
                _correcting = false;
            }

            _inChain = true;
            return true;
        }

        /// <summary>
        /// Stops correcting after the rate limiter reported too many corrections within the window,
        /// and logs that once. Anchoring resumes at the next press, mouse-wheel tick or page key
        /// (see <see cref="Resume"/>). Shared by the drift correction and the restoration of a
        /// virtualized-away card, so both end a layout that fights them the same way.
        /// </summary>
        private void SuspendForOscillation()
        {
            _suspended = true;
            App.Logger.LogInfo(
                $"[ScrollAnchor] More than {MaxCorrectionsPerWindow} corrections within " +
                $"{OscillationWindowMs} ms on list '{_list.Name}'; scroll anchoring paused until " +
                "the next press, mouse-wheel tick or page key.");
        }

        // ============================ Anchor choice and geometry ============================

        /// <summary>
        /// Chooses the anchor by priority (pressed, selected, first visible) and measures its top;
        /// see the class summary. Each candidate is found again through its data item, so a recycled
        /// container does not lose the anchor; a pressed item that has left the list is forgotten here.
        /// </summary>
        /// <param name="scroll">The list's ScrollViewer, whose viewport defines "on screen".</param>
        /// <param name="choice">The chosen anchor (default when none).</param>
        /// <param name="top">The chosen anchor's top relative to the ScrollViewer.</param>
        /// <returns>False when no realized container is usable, or the choice cannot be measured.</returns>
        private bool TryChooseAnchor(ScrollViewer scroll, out AnchorChoice choice, out double top)
        {
            choice = default;
            top = 0;
            if (ChooseAnchor(scroll) is not { } picked || !TryGetTop(picked.Container, scroll, out top))
                return false;

            choice = picked;
            return true;
        }

        /// <summary>The priority rules of <see cref="TryChooseAnchor"/>, without measuring.</summary>
        /// <param name="scroll">The list's ScrollViewer, whose viewport defines "on screen".</param>
        /// <returns>The chosen anchor, or null when no realized container is usable.</returns>
        private AnchorChoice? ChooseAnchor(ScrollViewer scroll)
        {
            // 1. The card the user last pressed, found again through its item. If the panel has no
            // container for it right now, it is only forgotten when it has left the list; otherwise
            // (virtualized away, or its new container not arranged yet) it just cannot be the anchor
            // this pass.
            if (_pressedItem is { } pressed)
            {
                if (_list.ContainerFromItem(pressed) is Control pressedContainer)
                {
                    if (IsUsable(pressedContainer))
                        return new AnchorChoice(pressedContainer, pressed, AnchorKind.Pressed);
                }
                else if (_list.Items.IndexOf(pressed) < 0)
                {
                    ClearPressed();
                }
            }

            // 2. The selected card. When it is not already the anchor it must be on screen, so
            // that a selection scrolled far away does not hijack an anchor the user is looking at.
            var selected = _list.SelectedItem;
            if (selected is not null
                && _list.ContainerFromItem(selected) is Control selectedContainer
                && IsUsable(selectedContainer)
                && (ReferenceEquals(selected, _anchor?.Item) || IntersectsViewport(selectedContainer, scroll)))
                return new AnchorChoice(selectedContainer, selected, AnchorKind.Selected);

            // 3a. Keep a first-visible anchor chosen earlier while it is still the same card.
            if (_anchor is { Kind: AnchorKind.FirstVisible } sticky
                && _list.ContainerFromItem(sticky.Item) is Control stickyContainer
                && IsUsable(stickyContainer))
                return new AnchorChoice(stickyContainer, sticky.Item, AnchorKind.FirstVisible);

            // 3b. Otherwise the realized container nearest the top that intersects the viewport.
            Control? best = null;
            double bestTop = double.MaxValue;
            foreach (var container in _list.GetRealizedContainers())
            {
                if (!IsUsable(container) || !TryGetTop(container, scroll, out double t)) continue;
                if (!Overlaps(t, container.Bounds.Height, scroll.Viewport.Height)) continue;
                if (t < bestTop) { best = container; bestTop = t; }
            }

            return best is not null && _list.ItemFromContainer(best) is { } item
                ? new AnchorChoice(best, item, AnchorKind.FirstVisible)
                : null;
        }

        /// <summary>
        /// Chooses the anchor now and takes a baseline from the current geometry. Used by the
        /// press and selection handlers, which run between layout passes while the geometry is
        /// still what the user sees.
        /// </summary>
        private void CaptureBaseline()
        {
            var scroll = Scroll;
            if (scroll is not null && HasGeometry(scroll) && TryChooseAnchor(scroll, out var choice, out double top))
                SetBaseline(choice, top, scroll.Offset.Y);
        }

        /// <summary>Stores a new baseline: which card is pinned, where its top is, and the offset it was measured at.</summary>
        /// <param name="choice">The anchor being pinned.</param>
        /// <param name="top">The anchor's top relative to the ScrollViewer.</param>
        /// <param name="offsetY">The ScrollViewer's vertical offset at measurement time.</param>
        private void SetBaseline(AnchorChoice choice, double top, double offsetY)
        {
            _anchor = choice;
            _anchorTop = top;
            _lastOffsetY = offsetY;
            _inChain = false;
        }

        /// <summary>Discards the baseline, so the next layout pass takes a fresh one (no correction).</summary>
        private void Reset()
        {
            _anchor = null;
            _inChain = false;
        }

        /// <summary>Forgets the last-pressed card.</summary>
        private void ClearPressed() => _pressedItem = null;

        /// <summary>Whether the list is visible and its ScrollViewer has a viewport, i.e. its geometry means something.</summary>
        /// <param name="scroll">The list's ScrollViewer.</param>
        private bool HasGeometry(ScrollViewer scroll) => _list.IsEffectivelyVisible && scroll.Viewport.Height > 0;

        /// <summary>
        /// Whether a container can serve as an anchor: it must be attached to the visual tree,
        /// effectively visible (a recycled container is hidden) and have been arranged.
        /// </summary>
        /// <param name="container">The container to test.</param>
        /// <returns>True when its geometry is meaningful.</returns>
        private static bool IsUsable(Control container) =>
            container.IsAttachedToVisualTree() && container.IsEffectivelyVisible && container.Bounds.Height > 0;

        /// <summary>Whether any part of a container lies inside the ScrollViewer's viewport.</summary>
        /// <param name="container">The container to test.</param>
        /// <param name="scroll">The ScrollViewer whose viewport is used.</param>
        /// <returns>True when the container's vertical extent overlaps the viewport.</returns>
        private static bool IntersectsViewport(Control container, ScrollViewer scroll) =>
            IsUsable(container)
            && TryGetTop(container, scroll, out double top)
            && Overlaps(top, container.Bounds.Height, scroll.Viewport.Height);

        /// <summary>Whether a vertical span (top and height, relative to the viewport top) overlaps a viewport of the given height.</summary>
        /// <param name="top">The span's top edge relative to the viewport top.</param>
        /// <param name="height">The span's height.</param>
        /// <param name="viewportHeight">The viewport's height.</param>
        private static bool Overlaps(double top, double height, double viewportHeight) =>
            top + height > 0 && top < viewportHeight;

        /// <summary>
        /// Measures a container's top edge relative to the ScrollViewer: 0 when it is flush with
        /// the viewport top, negative when scrolled partly above it.
        /// </summary>
        /// <param name="container">The container to measure.</param>
        /// <param name="scroll">The ScrollViewer to measure against.</param>
        /// <param name="top">The measured top edge in device-independent pixels.</param>
        /// <returns>False when the two are not in the same visual tree (no usable measurement).</returns>
        private static bool TryGetTop(Control container, ScrollViewer scroll, out double top)
        {
            var point = container.TranslatePoint(default, scroll);
            top = point?.Y ?? 0;
            return point.HasValue;
        }
    }
}

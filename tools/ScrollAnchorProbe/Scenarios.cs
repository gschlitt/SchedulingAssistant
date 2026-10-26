using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
using TermPoint.Behaviors;

/// <summary>
/// The probe's scenarios. Each prints what it measured (so a human can read the trend) and records
/// pass / fail checks through <see cref="Report"/>. The numbers come from real layout passes of a real
/// <see cref="VirtualizingStackPanel"/> in an off-screen window; nothing is simulated except input.
/// </summary>
static class Scenarios
{
    /// <summary>Formats a nullable measurement.</summary>
    private static string F(double? v) => v.HasValue ? v.Value.ToString("0.0") : "n/a";

    // ---------------------------------------------------------------------------------------------

    /// <summary>The rig builds, virtualizes, and finds the list's own ScrollViewer.</summary>
    public static void Sanity()
    {
        Console.WriteLine("=== SANITY");
        var r = Rig.Build(anchoring: true);
        int realized = r.List.GetRealizedContainers().Count();
        Console.WriteLine($"window shown to get a visual root: {Rig.WindowWasShown}");
        Console.WriteLine($"attached={r.List.IsAttachedToVisualTree()}  templateScroll found={(r.Scroll != null)}  viewport={r.Scroll?.Viewport}  extent={r.Scroll?.Extent}");
        Console.WriteLine($"realized containers: {realized} of {r.Cards.Count}");
        Console.WriteLine($"panel type: {r.List.ItemsPanelRoot?.GetType().Name}");
        Report.Check(r.Scroll is not null, "the list's template ScrollViewer is found");
        Report.Check(r.List.ItemsPanelRoot is VirtualizingStackPanel, "the production switch builds a VirtualizingStackPanel");
        Report.Check(realized is > 0 and < 50, "only the cards near the viewport are built");
        r.Close();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// E1: press a mid-viewport card (selection thickens its border), then grow it by 500 px (the
    /// editor opening). Reports how far the card's top moved relative to the viewport. Informational:
    /// the last row jumps straight to offset 15000 without scrolling there, which the panel has not
    /// calibrated for and which trips the oscillation guard; the SWEEP below is the pass criterion.
    /// </summary>
    public static void PressAndGrow()
    {
        Console.WriteLine();
        Console.WriteLine("=== E1  press a card, then grow it by 500 px   (top = card top relative to viewport; 'drift' = after - before)");
        Console.WriteLine("anchoring  startOffset  card  topBefore  topAfterPress  topAfterGrow  driftPress  driftGrow  passes  offsetNow");
        foreach (var anchoring in new[] { false, true })
        {
            foreach (var startOffset in new double[] { 3000, 6000, 9000, 12000, 15000 })
            {
                var r = Rig.Build(anchoring);
                r.Scroll!.Offset = new Vector(0, startOffset);
                r.Settle(6);
                var card = r.PickMidViewportCard();
                if (card is null) { Console.WriteLine($"{anchoring,-9} {startOffset,11}  (no mid-viewport card)"); r.Close(); continue; }

                double? top0 = r.TopOf(card);
                r.Press(card);
                r.Settle(12);
                double? top1 = r.TopOf(card);
                card.Height += 500;
                int passes = r.SettleUntilStable(card, 80);
                double? top2 = r.TopOf(card);
                Console.WriteLine($"{anchoring,-9} {startOffset,11}  {card.Index,4}  {F(top0),9}  {F(top1),13}  {F(top2),12}  {F(top1 - top0),10}  {F(top2 - top0),9}  {passes,6}  {r.Scroll.Offset.Y,9:0.0}   selected={r.List.SelectedItem == card}");
                r.Close();
            }
        }
    }

    /// <summary>E1b: the same, but the card is selected by code (keyboard / cross-view style), with no pointer press.</summary>
    public static void SelectByCodeAndGrow()
    {
        Console.WriteLine();
        Console.WriteLine("=== E1b select by code (no press), then grow by 500 px");
        Console.WriteLine("anchoring  startOffset  card  topBefore  topAfterGrow  driftGrow  passes");
        foreach (var anchoring in new[] { false, true })
        {
            foreach (var startOffset in new double[] { 6000, 12000 })
            {
                var r = Rig.Build(anchoring);
                r.Scroll!.Offset = new Vector(0, startOffset);
                r.Settle(6);
                var card = r.PickMidViewportCard()!;
                double? top0 = r.TopOf(card);
                r.List.SelectedItem = card;
                r.Settle(12);
                card.Height += 500;
                int passes = r.SettleUntilStable(card, 80);
                double? top2 = r.TopOf(card);
                Console.WriteLine($"{anchoring,-9} {startOffset,11}  {card.Index,4}  {F(top0),9}  {F(top2),12}  {F(top2 - top0),9}  {passes,6}");
                if (anchoring) Report.Check(Math.Abs((top2 ?? double.NaN) - (top0 ?? double.NaN)) <= 1, $"E1b: selected card held within 1 px (start {startOffset})");
                r.Close();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>E2: a scroll by the user must not be undone, however far it goes.</summary>
    public static void UserScroll()
    {
        Console.WriteLine();
        Console.WriteLine("=== E2  user scroll after anchoring (+300 px) must stick");
        var r = Rig.Build(anchoring: true);
        r.Scroll!.Offset = new Vector(0, 6000);
        r.Settle(6);
        var card = r.PickMidViewportCard()!;
        r.Press(card);
        r.Settle(6);
        card.Height += 500;
        r.SettleUntilStable(card, 80);
        double off = r.Scroll.Offset.Y;
        r.Wheel();
        r.Scroll.Offset = new Vector(0, off + 300);
        r.Settle(20);
        Console.WriteLine($"  USER (wheel) +300: offset before {off:0.0}; asked for {off + 300:0.0}; after 20 passes {r.Scroll.Offset.Y:0.0}  (expected ~{off + 300:0.0})");
        Report.Check(Math.Abs(r.Scroll.Offset.Y - (off + 300)) <= 1, "E2: a wheel scroll of +300 px sticks");

        // And a long user scroll that takes the pressed card off screen: the anchor must be dropped, not chased.
        double off2 = r.Scroll.Offset.Y;
        r.Wheel();
        r.Scroll.Offset = new Vector(0, off2 + 5000);
        r.Settle(20);
        Console.WriteLine($"  USER (wheel) +5000 px, pressed card now far away: offset {r.Scroll.Offset.Y:0.0} (expected ~{off2 + 5000:0.0}), new anchor takes over");
        Report.Check(Math.Abs(r.Scroll.Offset.Y - (off2 + 5000)) <= 1, "E2: a wheel scroll of +5000 px sticks");

        // Control: the same 300 px jump made by code with no user input moves the anchor with it, so it is accepted as a scroll too.
        var r2 = Rig.Build(anchoring: true);
        r2.Scroll!.Offset = new Vector(0, 6000);
        r2.Settle(6);
        var card2 = r2.PickMidViewportCard()!;
        r2.Press(card2);
        r2.Settle(12);
        double offA = r2.Scroll.Offset.Y;
        double? topA = r2.TopOf(card2);
        r2.Scroll.Offset = new Vector(0, offA + 300);
        r2.Settle(20);
        Console.WriteLine($"  CODE (no input) +300, pressed card visible: offset {offA:0.0} -> {r2.Scroll.Offset.Y:0.0}; card top {topA:0.0} -> {r2.TopOf(card2):0.0}   (a consistent move is accepted as a scroll)");
        r2.Close();
        r.Close();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// E3: the raw mechanism behind <see cref="ListScrollAnchorBehavior.ScrollItemIntoView"/>. A
    /// virtualized-away card is reached with Pause + Bypass + ListBox.ScrollIntoView; without the bypass
    /// the panel builds the card but the request to bring it into view is swallowed.
    /// </summary>
    public static void UnrealizedScrollIntoView()
    {
        Console.WriteLine();
        Console.WriteLine("=== E3  ScrollIntoView of an unrealized card (index 250)");
        foreach (var bypass in new[] { false, true })
        {
            var r = Rig.Build(anchoring: true);
            var target = r.Cards[250];
            bool realizedBefore = r.List.ContainerFromItem(target) != null;
            double offBefore = r.Scroll!.Offset.Y;
            using (var pause = ListScrollAnchorBehavior.Pause(r.List))
            {
                IDisposable? b = bypass ? SuppressPopupScrollBehavior.Bypass() : null;
                r.List.ScrollIntoView(target);
                b?.Dispose();
                r.Settle(10);
            }
            r.Settle(10);
            var c = r.List.ContainerFromItem(target) as Control;
            double? top = c is null ? null : r.TopOf(c);
            Console.WriteLine($"  bypass={bypass,-5} realizedBefore={realizedBefore} offset {offBefore:0.0} -> {r.Scroll.Offset.Y:0.0}  realizedAfter={c != null}  top={F(top)} (viewport {r.Scroll.Viewport.Height:0})");
            if (c != null)
            {
                var reveal = ListScrollAnchorBehavior.ComputeRevealOffset(top!.Value, c.Bounds.Height, r.Scroll.Viewport.Height, r.Scroll.Offset.Y);
                Console.WriteLine($"      refinement (ComputeRevealOffset) would {(reveal.HasValue ? "move offset to " + reveal.Value.ToString("0.0") : "do nothing")}");
            }

            if (bypass) Report.Check(top is >= 0 && top < r.Scroll.Viewport.Height, "E3: with the bypass the request scrolls the card into the viewport");
            else Report.Check(Math.Abs(r.Scroll.Offset.Y - offBefore) < 1, "E3: without the bypass the request is swallowed (offset unchanged)");
            r.Close();
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// E4: oscillation guard. A saboteur resizes the card above the anchor by +-8 px on every layout
    /// pass, so every pass needs a correction. The guard must stop after ~20, log once, and resume at
    /// the next wheel tick.
    /// </summary>
    public static void OscillationGuard()
    {
        Console.WriteLine();
        Console.WriteLine("=== E4  oscillation guard");
        var r = Rig.Build(anchoring: true);
        r.Scroll!.Offset = new Vector(0, 6000);
        r.Settle(6);
        var card = r.PickMidViewportCard()!;
        r.Press(card);
        r.Settle(6);
        var above = r.Cards[card.Index - 1];
        int offsetWrites = 0;
        double lastOff = r.Scroll.Offset.Y;
        bool toggle = false;
        int sab = 0;
        r.List.LayoutUpdated += (_, _) => { if (sab++ >= 60) return; toggle = !toggle; above.Height += toggle ? 8 : -8; };
        int logBefore = TermPoint.App.Logger.Lines.Count;
        for (int i = 0; i < 90; i++)
        {
            r.Settle(1);
            if (Math.Abs(r.Scroll.Offset.Y - lastOff) > 0.4) offsetWrites++;
            lastOff = r.Scroll.Offset.Y;
        }
        int logged = TermPoint.App.Logger.Lines.Count - logBefore;
        Console.WriteLine($"  90 passes with a layout-fighting saboteur: offset changed in {offsetWrites} passes; log lines written: {logged}");
        Report.Check(logged == 1, "E4: the guard trips and logs exactly once");
        Report.Check(offsetWrites <= 3, "E4: corrections stop after the guard trips");

        // A mouse-wheel tick resumes anchoring.
        int writes2 = 0;
        lastOff = r.Scroll.Offset.Y;
        r.Wheel();
        for (int i = 0; i < 10; i++)
        {
            r.Settle(1);
            if (Math.Abs(r.Scroll.Offset.Y - lastOff) > 0.4) writes2++;
            lastOff = r.Scroll.Offset.Y;
        }
        Console.WriteLine($"  after a wheel tick, 10 more passes: offset changed in {writes2} passes (corrections resumed)");
        r.Close();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// E5: <see cref="ListScrollAnchorBehavior.ScrollItemIntoView"/>, the one scroll path, in every
    /// situation it must handle: an unbuilt card, a clipped card, a card taller than the viewport, a
    /// card already in place, a classic (non-virtualized) list, and two requests in a row.
    /// </summary>
    public static void RevealHelper()
    {
        Console.WriteLine();
        Console.WriteLine("=== E5  ScrollItemIntoView, the one scroll path (virtualizing panel with anchoring, unless stated)");

        // a. A card the panel has not built.
        {
            var r = Rig.Build(anchoring: true);
            var far = r.Cards[250];
            bool builtBefore = r.List.ContainerFromItem(far) != null;
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, far);
            r.Settle(12);
            Console.WriteLine($"  a. unbuilt card 250 (built before: {builtBefore}): offset {r.Scroll!.Offset.Y:0.0}, top {F(r.TopOf(far))}, viewport {r.Scroll.Viewport.Height:0}");
            Report.Check(!builtBefore && r.IsFullyVisible(far), "E5a: an unbuilt card is built and made fully visible");
            r.Close();
        }

        // b. A built card clipped by the bottom of the viewport (30 px of it shown).
        {
            var r = Rig.Build(anchoring: true);
            r.Scroll!.Offset = new Vector(0, 6000);
            r.Settle(6);
            double viewport = r.Scroll.Viewport.Height;
            var lowest = r.List.GetRealizedContainers()
                .Where(c => r.TopOf(c) is double t && t < viewport)
                .OrderBy(c => r.TopOf(c)).Last();
            var clipped = (Card)r.List.ItemFromContainer(lowest)!;
            r.Scroll.Offset = new Vector(0, r.Scroll.Offset.Y + r.TopOf(lowest)!.Value - (viewport - 30));
            r.Settle(6);
            double? before = r.TopOf(clipped);
            bool isClipped = before is double b && b < viewport && b + ((Control)r.List.ContainerFromItem(clipped)!).Bounds.Height > viewport + 5;
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, clipped);
            r.Settle(12);
            Console.WriteLine($"  b. clipped card {clipped.Index}: top {F(before)} -> {F(r.TopOf(clipped))}");
            Report.Check(isClipped && r.IsFullyVisible(clipped), "E5b: a card clipped at the bottom is made fully visible");
            r.Close();
        }

        // c. A card taller than the viewport is top-aligned (the editor open).
        {
            var r = Rig.Build(anchoring: true);
            r.Scroll!.Offset = new Vector(0, 6000);
            r.Settle(6);
            var card = r.PickMidViewportCard()!;
            r.Press(card);
            r.Settle(12);
            card.Height += 900;
            r.SettleUntilStable(card, 80);
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, card);
            r.Settle(12);
            var c = (Control)r.List.ContainerFromItem(card)!;
            Console.WriteLine($"  c. card {card.Index} grown to {c.Bounds.Height:0} px (viewport {r.Scroll!.Viewport.Height:0}): top {F(r.TopOf(card))}");
            Report.Check(c.Bounds.Height > r.Scroll.Viewport.Height && Math.Abs(r.TopOf(card) ?? double.NaN) <= 1,
                "E5c: a card taller than the viewport has its top aligned with the viewport top");
            r.Close();
        }

        // d. A card already fully visible is left alone.
        {
            var r = Rig.Build(anchoring: true);
            r.Scroll!.Offset = new Vector(0, 6000);
            r.Settle(6);
            var card = r.PickMidViewportCard()!;
            r.Settle(6);
            double offBefore = r.Scroll.Offset.Y;
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, card);
            r.Settle(12);
            Console.WriteLine($"  d. card {card.Index} already in place: offset {offBefore:0.0} -> {r.Scroll.Offset.Y:0.0}");
            Report.Check(Math.Abs(r.Scroll.Offset.Y - offBefore) <= 0.5, "E5d: an already-placed card is left alone");
            r.Close();
        }

        // e. A classic panel (every card built), anchoring disabled.
        {
            var r = Rig.Build(anchoring: false, virtualized: false);
            int built = r.List.GetRealizedContainers().Count();
            var far = r.Cards[250];
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, far);
            r.Settle(12);
            Console.WriteLine($"  e. classic panel ({built} of {r.Cards.Count} cards built, anchoring {ListScrollAnchorBehavior.GetIsEnabled(r.List)}): card 250 top {F(r.TopOf(far))}");
            Report.Check(built == r.Cards.Count && !ListScrollAnchorBehavior.GetIsEnabled(r.List) && r.IsFullyVisible(far),
                "E5e: with the classic panel and no anchoring the card is made fully visible");
            r.Close();
        }

        // f. Two requests in a row: the later one wins.
        {
            var r = Rig.Build(anchoring: true);
            var first = r.Cards[250];
            var second = r.Cards[20];
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, first);
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, second);
            r.Settle(20);
            Console.WriteLine($"  f. requests for 250 then 20: card 20 top {F(r.TopOf(second))}");
            Report.Check(r.IsFullyVisible(second), "E5f: the later of two quick requests wins");
            r.Close();
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// E6: keyboard. PageDown goes through the real tunnel/bubble route: the ScrollViewer scrolls a page
    /// and our behavior treats it as the user scrolling. The arrow keys only move the selection, so the
    /// selection drift they cause must still be corrected (they are deliberately not scroll hints).
    /// </summary>
    public static void Keys()
    {
        Console.WriteLine();
        Console.WriteLine("=== E6  keyboard");

        // a. PageDown.
        {
            var r = Rig.Build(anchoring: true);
            r.Scroll!.Offset = new Vector(0, 6000);
            r.Settle(6);
            var card = r.PickMidViewportCard()!;
            r.Press(card);
            r.Settle(12);
            double off0 = r.Scroll.Offset.Y;
            r.KeyDown(card, Key.PageDown);
            double immediately = r.Scroll.Offset.Y;
            r.Settle(20);
            double end = r.Scroll.Offset.Y;
            Console.WriteLine($"  a. PageDown key: offset {off0:0.0} -> {immediately:0.0} straight away -> {end:0.0} after 20 passes");
            Report.Check(immediately > off0 + 100, "E6a: the PageDown key makes the list's ScrollViewer scroll a page");
            Report.Check(Math.Abs(end - immediately) <= 1, "E6a: ... and the page scroll sticks (it is treated as the user scrolling)");
            r.Close();

            // Control, informational: the same page scroll made through the API, with no key event, so no hint.
            var r2 = Rig.Build(anchoring: true);
            r2.Scroll!.Offset = new Vector(0, 6000);
            r2.Settle(6);
            var card2 = r2.PickMidViewportCard()!;
            r2.Press(card2);
            r2.Settle(12);
            double c0 = r2.Scroll.Offset.Y;
            r2.Scroll.PageDown();
            double cImmediately = r2.Scroll.Offset.Y;
            r2.Settle(20);
            Console.WriteLine($"     control, ScrollViewer.PageDown() with no key event: offset {c0:0.0} -> {cImmediately:0.0} straight away -> {r2.Scroll.Offset.Y:0.0} after 20 passes");
            r2.Close();
        }

        // b. Down arrow after a selection made by code: pure selection navigation. The newly selected
        // card becomes the anchor, so the border-thickening drift is corrected (arrow keys are
        // deliberately NOT scroll hints; see ListScrollAnchorBehavior.IsScrollKey).
        {
            var r = Rig.Build(anchoring: true);
            r.Scroll!.Offset = new Vector(0, 6000);
            r.Settle(6);
            var card = r.PickMidViewportCard()!;
            r.List.SelectedItem = card;
            r.Settle(12);
            var next = r.Cards[card.Index + 1];
            double? before = r.TopOf(next);
            r.KeyDown(card, Key.Down);
            bool listHandledIt = r.List.SelectedItem == next;
            if (!listHandledIt) r.List.SelectedItem = next; // the off-screen window has no focus; select as the ListBox would
            r.Settle(12);
            double? after = r.TopOf(next);
            Console.WriteLine($"  b. Down key after a code selection: the ListBox moved the selection itself: {listHandledIt}; next card top {F(before)} -> {F(after)}");
            Report.Check(Math.Abs((after ?? double.NaN) - (before ?? double.NaN)) <= 1,
                "E6b: after an arrow key the newly selected card stays put (selection drift is corrected)");
            r.Close();
        }

        // c. Click a card, then arrow to the next one. The panel reacts to the border change by recycling
        // every element, so the pressed card gets a new container. The anchor is the ITEM, so it survives
        // that, and the selection of a different card replaces the pressed anchor: the newly selected
        // card must stay put. (Before the anchor was identified by its item this drifted by about 390 px.)
        {
            var r = Rig.Build(anchoring: true);
            r.Scroll!.Offset = new Vector(0, 6000);
            r.Settle(6);
            var card = r.PickMidViewportCard()!;
            r.Press(card);
            r.Settle(12);
            var next = r.Cards[card.Index + 1];
            double? before = r.TopOf(next);
            r.KeyDown(card, Key.Down);
            if (r.List.SelectedItem != next) r.List.SelectedItem = next;
            r.Settle(12);
            double? after = r.TopOf(next);
            Console.WriteLine($"  c. click, then Down key: next card top {F(before)} -> {F(after)}");
            Report.Check(Math.Abs((after ?? double.NaN) - (before ?? double.NaN)) <= 1,
                "E6c: after a click and then an arrow key the newly selected card stays put (anchor survives the container recycle)");
            r.Close();
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>E7: <see cref="ListVirtualizationBehavior"/> swaps the panel and the anchoring flag at run time.</summary>
    public static void PanelSwitch()
    {
        Console.WriteLine();
        Console.WriteLine("=== E7  ListVirtualizationBehavior switches panel and anchoring at run time");

        // Checks one rig against the state the switch should have produced.
        void Describe(Rig rig, string label, bool expectVirtualized)
        {
            var panel = rig.List.ItemsPanelRoot;
            int built = rig.List.GetRealizedContainers().Count();
            bool suppress = panel is not null && SuppressPopupScrollBehavior.GetIsEnabled(panel);
            bool anchoring = ListScrollAnchorBehavior.GetIsEnabled(rig.List);
            Console.WriteLine($"  {label}: panel {panel?.GetType().Name}, built {built} of {rig.Cards.Count}, suppression {suppress}, anchoring {anchoring}");
            Report.Check(
                (panel is VirtualizingStackPanel) == expectVirtualized
                && suppress
                && anchoring == expectVirtualized
                && (expectVirtualized ? built < 50 : built == rig.Cards.Count),
                $"E7: {label} gives the expected panel, suppression, anchoring and card count");
        }

        // Set directly.
        var r = Rig.Build(anchoring: false, virtualized: false);
        Describe(r, "first value false", expectVirtualized: false);
        ListVirtualizationBehavior.SetIsVirtualized(r.List, true);
        r.Settle(10);
        Describe(r, "switched to true", expectVirtualized: true);
        ListVirtualizationBehavior.SetIsVirtualized(r.List, false);
        r.Settle(10);
        Describe(r, "switched back to false", expectVirtualized: false);
        r.Close();

        // Driven by a binding to a view model's bool, exactly as SectionListView.axaml does it
        // (b:ListVirtualizationBehavior.IsVirtualized="{Binding IsListVirtualized}").
        var vm = new SwitchViewModel();
        var bound = Rig.Build(anchoring: false, virtualized: null);
        bound.List.DataContext = vm;
        bound.List.Bind(ListVirtualizationBehavior.IsVirtualizedProperty, new Binding(nameof(SwitchViewModel.IsListVirtualized)));
        bound.Settle(10);
        Describe(bound, "bound, view model false", expectVirtualized: false);
        vm.IsListVirtualized = true;
        bound.Settle(10);
        Describe(bound, "bound, view model changed to true", expectVirtualized: true);
        vm.IsListVirtualized = false;
        bound.Settle(10);
        Describe(bound, "bound, view model changed to false", expectVirtualized: false);
        bound.Close();
    }

    /// <summary>Stand-in for SectionListViewModel: one bool that raises PropertyChanged, like <c>IsListVirtualized</c>.</summary>
    private sealed class SwitchViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isListVirtualized;

        /// <summary>The preference as the list sees it.</summary>
        public bool IsListVirtualized
        {
            get => _isListVirtualized;
            set
            {
                _isListVirtualized = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsListVirtualized)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// E8: scrolling to a card while the list is hidden (the Events view is showing) must not throw;
    /// what happens when the list is shown again is reported for information.
    /// </summary>
    public static void HiddenList()
    {
        Console.WriteLine();
        Console.WriteLine("=== E8  ScrollItemIntoView while the list is hidden");
        var r = Rig.Build(anchoring: true);
        r.List.IsVisible = false;
        r.Settle(6);
        Exception? error = null;
        try
        {
            ListScrollAnchorBehavior.ScrollItemIntoView(r.List, r.Cards[250]);
            r.Settle(12);
        }
        catch (Exception ex) { error = ex; }
        Console.WriteLine($"  hidden: exception {error?.GetType().Name ?? "none"}");
        Report.Check(error is null, "E8: ScrollItemIntoView on a hidden list does not throw");
        r.List.IsVisible = true;
        r.Settle(12);
        Console.WriteLine($"  shown again: offset {r.Scroll!.Offset.Y:0.0}, card 250 built {r.List.ContainerFromItem(r.Cards[250]) != null}, fully visible {r.IsFullyVisible(r.Cards[250])}  (information only)");
        r.Close();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>One growth step of an E9 trial.</summary>
    /// <param name="Growth">Pixels added to the card's height.</param>
    /// <param name="ClickInside">True when, before the card grows, keyboard focus is put on the button inside it (the click on "Add meeting"); false when only the card itself is pressed (the click that opens the editor).</param>
    private readonly record struct GrowthStep(double Growth, bool ClickInside);

    /// <summary>What one <see cref="RunPinnedGrowth"/> trial measured.</summary>
    /// <param name="CardIndex">Index of the pinned card.</param>
    /// <param name="Pinned">The card's top (relative to the viewport) just before the last growth, i.e. where it was pinned.</param>
    /// <param name="Final">The card's top once layout settled; null when it has no container.</param>
    /// <param name="PassesWithoutContainer">Layout passes, during the growth steps, that left the card with no container (virtualized away).</param>
    /// <param name="Offset">The scroll offset once layout settled.</param>
    /// <param name="FocusInside">True when keyboard focus was on the card's button at every growth step (the situation the scenario is about).</param>
    private readonly record struct PinTrial(int CardIndex, double Pinned, double? Final, int PassesWithoutContainer, double Offset, bool FocusInside)
    {
        /// <summary>True when the card's top is back within 1 px of where it was pinned.</summary>
        public bool Held => Final.HasValue && Math.Abs(Final.Value - Pinned) <= 1;

        /// <summary>True when the trial really tests the bug: the panel threw the card away at some pass.</summary>
        public bool VirtualizedAway => PassesWithoutContainer > 0;
    }

    /// <summary>
    /// One E9 trial: scroll to <paramref name="startOffset"/> the way a user does and pick a card near the
    /// bottom of the viewport. Then, for each growth step: press the card (or select it by code), let
    /// layout settle, put keyboard focus on the button inside it (the click on "Add meeting"), and grow the
    /// card by that many pixels. Counts the layout passes that leave the card with no container, using a
    /// handler that runs before the anchoring's own (see <see cref="Rig.Build"/>), so the count does not
    /// depend on what the behavior does next.
    /// <para>Why the button matters (measured, and consistent with the panel's code, whose <c>RecycleElement</c>
    /// consults <c>KeyboardNavigation.GetTabOnceActiveElement</c>): a card that is the list's "tab once active
    /// element" (the card itself, while only the card is focused or selected) was never recycled in the probe, so it survived
    /// any drift and the bug did not occur. Once focus is on a control <em>inside</em> the card, as after a click
    /// on a button in the editor, the card is an ordinary realized element and the panel throws it away as soon
    /// as a re-estimate moves it out of the realized range.</para>
    /// </summary>
    /// <param name="startOffset">Where to scroll to before picking the card.</param>
    /// <param name="steps">The growth steps, one after the other; two entries model "the editor opens, then a meeting row is added".</param>
    /// <param name="selectByCode">True to select the card by code instead of pressing it (the anchor is then the selected card, not the pressed one).</param>
    /// <returns>What was measured, or null when no suitable card was on screen or an earlier step already lost the card.</returns>
    private static PinTrial? RunPinnedGrowth(double startOffset, GrowthStep[] steps, bool selectByCode = false)
    {
        Card? watched = null;
        int withoutContainer = 0;
        var r = Rig.Build(anchoring: true, cardsHaveButton: true, beforeAttached: list =>
            list.LayoutUpdated += (_, _) => { if (watched is not null && list.ContainerFromItem(watched) is null) withoutContainer++; });
        try
        {
            r.ScrollLikeAUser(startOffset);
            var card = r.PickNearBottomViewportCard();
            if (card is null) return null;
            watched = card;

            double pinned = 0;
            bool focusInside = true;
            foreach (var step in steps)
            {
                if (r.TopOf(card) is null) return null; // lost in an earlier step; not what this trial is about
                if (selectByCode) r.List.SelectedItem = card; else r.Press(card);
                r.Settle(12);
                if (step.ClickInside)
                {
                    r.FocusButtonIn(card);
                    r.Settle(4);
                    focusInside &= r.IsFocusInside(card);
                }
                if (r.TopOf(card) is not double pinnedNow) return null;
                pinned = pinnedNow;
                card.Height += step.Growth;
                r.SettleUntilStable(card, 80);
            }

            return new PinTrial(card.Index, pinned, r.TopOf(card), withoutContainer, r.Scroll!.Offset.Y, focusInside);
        }
        finally { r.Close(); }
    }

    /// <summary>
    /// E9 (spec item 27): the pinned card is virtualized away by its own growth. The user has pressed (or
    /// selected) a card near the bottom of the viewport and clicked a button inside it, and the card grows
    /// by a lot (the editor opening, or a meeting row being added to an open editor). The panel re-estimates
    /// the cards it has not built from an average that now includes the huge card, which moves the card's
    /// estimated position outside the realized range, so the panel throws the card away in that same
    /// layout pass. Without a container there is nothing to measure, and the anchoring used to fall back
    /// to the first visible card and leave the editor far down the list. Success is the card's top back
    /// within 1 px of where it was pinned. A trial only counts as testing the bug when the card really lost
    /// its container at some pass (the check at the end insists on that for every case). A last check makes
    /// sure the restoration does not undo a user's scroll away from a pinned card (negative control: it fails
    /// when the <c>IsUserScrolling</c> guard is removed from the behavior).
    /// </summary>
    public static void PinnedCardVirtualizedAway()
    {
        Console.WriteLine();
        Console.WriteLine("=== E9  the pinned card is virtualized away by its own growth; success = its top back within 1 px of where it was pinned");
        Console.WriteLine("case                                   startOffset  card  pinnedTop  finalTop  noContainerPasses  offsetNow  focusInside  held");

        var cases = new (string Name, GrowthStep[] Steps, bool SelectByCode)[]
        {
            ("click inside, +1500",                new[] { new GrowthStep(1500, true) }, false),
            ("click inside, +4000",                new[] { new GrowthStep(4000, true) }, false),
            ("open editor +500, click inside +900", new[] { new GrowthStep(500, false), new GrowthStep(900, true) }, false),
            ("select by code, click inside +1500", new[] { new GrowthStep(1500, true) }, true),
        };
        double[] startOffsets = { 3000, 6000, 9000, 12000, 15000, 18000 };
        foreach (var c in cases)
        {
            var trials = new List<PinTrial>();
            foreach (double startOffset in startOffsets)
            {
                var t = RunPinnedGrowth(startOffset, c.Steps, c.SelectByCode);
                if (t is not { } trial) { Console.WriteLine($"{c.Name,-37} {startOffset,11}  (no suitable card, or lost in an earlier step)"); continue; }
                trials.Add(trial);
                Console.WriteLine($"{c.Name,-37} {startOffset,11}  {trial.CardIndex,4}  {trial.Pinned,9:0.0}  {F(trial.Final),8}  {trial.PassesWithoutContainer,17}  {trial.Offset,9:0.0}  {trial.FocusInside,11}  {trial.Held}");
            }

            // A trial where the card kept its container is not a test of this bug, so it fails the first check.
            bool complete = trials.Count == startOffsets.Length;
            Report.Check(complete && trials.All(t => t.FocusInside && t.VirtualizedAway),
                $"E9 ({c.Name}): the card is virtualized away at some pass in all {startOffsets.Length} trials (the case tests the bug)");
            Report.Check(complete && trials.All(t => t.Held),
                $"E9 ({c.Name}): the card's top is back within 1 px of where it was pinned in all {startOffsets.Length} trials");
        }

        // The restoration must not take the list away from a user who scrolls a pinned card off screen: with
        // focus inside the card the panel really does recycle it then (nothing keeps it), and it must not be
        // pulled back (the user-scroll rule; negative control: without the IsUserScrolling guard this fails).
        {
            var r = Rig.Build(anchoring: true, cardsHaveButton: true);
            r.ScrollLikeAUser(9000);
            var card = r.PickNearBottomViewportCard()!;
            r.Press(card);
            r.Settle(12);
            r.FocusButtonIn(card);
            r.Settle(4);
            double off = r.Scroll!.Offset.Y;
            r.Wheel();
            r.Scroll.Offset = new Vector(0, off + 6000);
            r.Settle(20);
            Thread.Sleep((int)ListScrollAnchorBehavior.UserScrollGraceMs + 200); // past the user-scroll grace period
            r.Settle(10);
            Console.WriteLine($"  user scroll away from the pinned card: offset {off:0.0} + 6000 asked, now {r.Scroll.Offset.Y:0.0}; card built: {r.List.ContainerFromItem(card) != null}");
            Report.Check(Math.Abs(r.Scroll.Offset.Y - (off + 6000)) <= 1, "E9: a user scroll of +6000 px away from the pinned card sticks (it is not pulled back)");
            r.Close();
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// SWEEP (the pass criterion): press a card, then grow it by 500 px, at 17 start offsets reached the
    /// way a user gets there (wheel notches). Success is the card's top within 1 px of where it was.
    /// </summary>
    public static void Sweep()
    {
        Console.WriteLine();
        Console.WriteLine("=== SWEEP  press, then grow +500, anchoring ON; success = card top within 1 px of where it was");
        int ok = 0, total = 0, okPress = 0;
        var fails = new List<string>();
        for (double startOffset = 2000; startOffset <= 26000; startOffset += 1500)
        {
            var r = Rig.Build(anchoring: true);
            r.ScrollLikeAUser(startOffset);
            var card = r.PickMidViewportCard();
            if (card is null) { r.Close(); continue; }

            double top0 = r.TopOf(card)!.Value;
            int log0 = TermPoint.App.Logger.Lines.Count;
            r.Press(card);
            r.Settle(12);
            double? top1 = r.TopOf(card);
            if (top1.HasValue && Math.Abs(top1.Value - top0) <= 1) okPress++;
            card.Height += 500;
            r.SettleUntilStable(card, 80);
            double? top2 = r.TopOf(card);
            total++;
            bool good = top2.HasValue && Math.Abs(top2.Value - top0) <= 1;
            if (good) ok++;
            else fails.Add($"{startOffset:0}(card {card.Index}: {top0:0}->{top2?.ToString("0")}, guard={(TermPoint.App.Logger.Lines.Count > log0)})");
            r.Close();
        }

        Console.WriteLine($"  press held within 1 px: {okPress}/{total};  press+grow held within 1 px: {ok}/{total}");
        if (fails.Count > 0) Console.WriteLine("  failures: " + string.Join("; ", fails));
        Report.Check(total == 17 && okPress == total, "SWEEP: the pressed card holds within 1 px at all 17 start offsets");
        Report.Check(total == 17 && ok == total, "SWEEP: press + 500 px growth holds within 1 px at all 17 start offsets");
    }
}

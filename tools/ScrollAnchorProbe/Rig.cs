using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TermPoint.Behaviors;

/// <summary>
/// A stand-in section card: variable height, and a border that thickens from 0 to 3 px when selected
/// (the real card does the same, and that 3 px is enough to start the panel's drift).
/// </summary>
sealed class Card : INotifyPropertyChanged
{
    /// <summary>Position in the list.</summary>
    public int Index { get; init; }

    /// <summary>Text shown in the card.</summary>
    public string Name => $"Card {Index}";

    private double _height;

    /// <summary>The card's content height; raise it to simulate the inline editor opening.</summary>
    public double Height { get => _height; set { _height = value; Raise(nameof(Height)); } }

    private bool _selected;

    /// <summary>Whether the card is selected (drives <see cref="BorderThickness"/>).</summary>
    public bool IsSelected { get => _selected; set { _selected = value; Raise(nameof(IsSelected)); Raise(nameof(BorderThickness)); } }

    /// <summary>0 when unselected, 3 when selected.</summary>
    public Thickness BorderThickness => _selected ? new Thickness(3) : new Thickness(0);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A ListBox of <see cref="Card"/>s in an off-screen window, wired exactly as the section list is
/// (through <see cref="ListVirtualizationBehavior"/>), plus helpers to drive real layout passes and
/// synthetic input.
/// </summary>
sealed class Rig
{
    private const string ItemXaml = """
        <DataTemplate xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Border Margin="0,2,0,2" BorderBrush="Red" BorderThickness="{Binding BorderThickness}" HorizontalAlignment="Left" MinWidth="300">
            <Border Background="LightSteelBlue" Height="{Binding Height}"><TextBlock Text="{Binding Name}" /></Border>
          </Border>
        </DataTemplate>
        """;

    /// <summary>The same card with a button in it, standing in for the editor's "Add meeting" button (scenario E9): a control inside a card that can take keyboard focus.</summary>
    private const string ItemWithButtonXaml = """
        <DataTemplate xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Border Margin="0,2,0,2" BorderBrush="Red" BorderThickness="{Binding BorderThickness}" HorizontalAlignment="Left" MinWidth="300">
            <Border Background="LightSteelBlue" Height="{Binding Height}">
              <StackPanel><TextBlock Text="{Binding Name}" /><Button Content="Add meeting" /></StackPanel>
            </Border>
          </Border>
        </DataTemplate>
        """;

    /// <summary>True once any rig had to show its window to get a visual root.</summary>
    public static bool WindowWasShown { get; private set; }

    public Window Window { get; }
    public ListBox List { get; }
    public ObservableCollection<Card> Cards { get; }

    /// <summary>The list's own ScrollViewer.</summary>
    public ScrollViewer? Scroll => List.Scroll as ScrollViewer;

    private Rig(Window window, ListBox list, ObservableCollection<Card> cards)
    {
        Window = window;
        List = list;
        Cards = cards;
    }

    /// <summary>
    /// Builds a rig. <paramref name="virtualized"/> goes through the production switch
    /// (<see cref="ListVirtualizationBehavior"/>); <paramref name="anchoring"/> then overrides the
    /// anchoring flag it set, so a virtualizing panel can also be probed WITHOUT anchoring.
    /// </summary>
    /// <param name="anchoring">Whether scroll anchoring is enabled.</param>
    /// <param name="count">Number of cards.</param>
    /// <param name="virtualized">True for the virtualizing panel, false for the classic one, null to leave the switch unset (a test then drives it, for example through a binding).</param>
    /// <param name="cardsHaveButton">True to give every card a button (see <see cref="FocusButtonIn"/>), so keyboard focus can be inside a card.</param>
    /// <param name="beforeAttached">Called with the list right after it is created, before anchoring is enabled and before the window is shown. A <c>LayoutUpdated</c> handler added here runs BEFORE the anchoring's own handler on every layout pass, so it sees the list as the layout left it, before the behavior reacts (scenario E9 uses this to see a card the panel has just virtualized away).</param>
    public static Rig Build(bool anchoring, int count = 300, bool? virtualized = true, Action<ListBox>? beforeAttached = null, bool cardsHaveButton = false)
    {
        var cards = new ObservableCollection<Card>(
            Enumerable.Range(0, count).Select(i => new Card { Index = i, Height = 50 + (i * 37) % 90 }));
        var list = new ListBox
        {
            ItemsSource = cards,
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(5, 0),
            ItemTemplate = AvaloniaRuntimeXamlLoader.Parse<DataTemplate>(cardsHaveButton ? ItemWithButtonXaml : ItemXaml),
        };
        beforeAttached?.Invoke(list);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)) },
        });
        if (virtualized is bool v) ListVirtualizationBehavior.SetIsVirtualized(list, v);
        ListScrollAnchorBehavior.SetIsEnabled(list, anchoring);
        list.SelectionChanged += (s, e) =>
        {
            if (!ReferenceEquals(e.Source, list)) return;
            foreach (Card c in e.RemovedItems) c.IsSelected = false;
            foreach (Card c in e.AddedItems) c.IsSelected = true;
        };

        var window = new Window { Width = 420, Height = 700, Content = list };
        window.ApplyTemplate();
        if (!list.IsAttachedToVisualTree())
        {
            // A visual root and layout manager are needed. Off-screen, no focus, no taskbar.
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(-20000, -20000);
            window.Show();
            WindowWasShown = true;
        }

        var rig = new Rig(window, list, cards);
        rig.Settle(6);
        return rig;
    }

    /// <summary>Runs <paramref name="passes"/> full layout passes, draining the dispatcher (all priorities) between them.</summary>
    /// <param name="passes">How many passes.</param>
    public void Settle(int passes)
    {
        for (int i = 0; i < passes; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
    }

    /// <summary>Layout passes until the card's top and the offset stop changing for three passes in a row.</summary>
    /// <param name="card">The card to watch.</param>
    /// <param name="max">Give up after this many passes.</param>
    /// <returns>The number of passes used.</returns>
    public int SettleUntilStable(Card card, int max)
    {
        int stable = 0;
        double? lastTop = TopOf(card);
        double lastOff = Scroll!.Offset.Y;
        for (int i = 1; i <= max; i++)
        {
            Settle(1);
            double? top = TopOf(card);
            double off = Scroll.Offset.Y;
            bool same = top.HasValue && lastTop.HasValue && Math.Abs(top.Value - lastTop.Value) < 0.05 && Math.Abs(off - lastOff) < 0.05;
            stable = same ? stable + 1 : 0;
            if (stable >= 3) return i;
            lastTop = top;
            lastOff = off;
        }
        return max;
    }

    /// <summary>Scrolls down in wheel-notch steps (as a user does) so the panel's size estimates are calibrated, then lands on <paramref name="offset"/>.</summary>
    /// <param name="offset">The final vertical offset.</param>
    public void ScrollLikeAUser(double offset)
    {
        for (double o = 0; o < offset; o += 250) { Scroll!.Offset = new Vector(0, o); Settle(2); }
        Scroll!.Offset = new Vector(0, offset);
        Settle(6);
    }

    /// <summary>The card's top relative to the ScrollViewer (null when it is not realized).</summary>
    /// <param name="card">The card.</param>
    public double? TopOf(Card card) => List.ContainerFromItem(card) is Control c ? c.TranslatePoint(default, Scroll!)?.Y : null;

    /// <summary>A container's top relative to the ScrollViewer.</summary>
    /// <param name="c">The container.</param>
    public double? TopOf(Control c) => c.TranslatePoint(default, Scroll!)?.Y;

    /// <summary>True when the card is built and lies entirely inside the viewport.</summary>
    /// <param name="card">The card.</param>
    public bool IsFullyVisible(Card card) =>
        List.ContainerFromItem(card) is Control c
        && TopOf(c) is double top
        && top >= -0.5
        && top + c.Bounds.Height <= Scroll!.Viewport.Height + 0.5;

    /// <summary>A realized, unclipped card whose top is in the upper-middle of the viewport.</summary>
    public Card? PickMidViewportCard()
    {
        foreach (var c in List.GetRealizedContainers())
        {
            var top = TopOf(c);
            if (top is >= 150 and <= 380 && c.Bounds.Height < 200 && List.ItemFromContainer(c) is Card card) return card;
        }
        return null;
    }

    /// <summary>
    /// A realized card, not much taller than a normal one, whose top is in the lower part of the viewport:
    /// where a user usually is when they open a section's editor and then click Add meeting.
    /// </summary>
    public Card? PickNearBottomViewportCard()
    {
        foreach (var c in List.GetRealizedContainers())
        {
            var top = TopOf(c);
            if (top is >= 430 and <= 560 && c.Bounds.Height < 200 && List.ItemFromContainer(c) is Card card) return card;
        }
        return null;
    }

    /// <summary>Raises a real left-button PointerPressed on the card's container, so the ListBox selects it.</summary>
    /// <param name="card">The card to press.</param>
    public void Press(Card card)
    {
        var container = (Control)List.ContainerFromItem(card)!;
        var source = container.GetVisualDescendants().OfType<Border>().First();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var props = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var pos = source.TranslatePoint(new Point(5, 5), Window) ?? default;
        var e = new PointerPressedEventArgs(source, pointer, Window, pos, (ulong)Environment.TickCount64, props, KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        };
        source.RaiseEvent(e);
        if (List.SelectedItem != card) List.SelectedItem = card; // fallback if the synthetic press did not select
    }

    /// <summary>Gives keyboard focus to the button inside a card (a rig built with <c>cardsHaveButton</c>), as clicking "Add meeting" does.</summary>
    /// <param name="card">The card whose button to focus.</param>
    /// <returns>True when the button took focus.</returns>
    public bool FocusButtonIn(Card card) =>
        List.ContainerFromItem(card) is Control c && c.GetVisualDescendants().OfType<Button>().FirstOrDefault() is { } b && b.Focus();

    /// <summary>True when keyboard focus is on a control inside the card (not on the card itself).</summary>
    /// <param name="card">The card to test.</param>
    public bool IsFocusInside(Card card) =>
        List.ContainerFromItem(card) is Control c
        && TopLevel.GetTopLevel(List)?.FocusManager?.GetFocusedElement() is Visual focused
        && !ReferenceEquals(focused, c)
        && c.IsVisualAncestorOf(focused);

    /// <summary>Raises a real mouse-wheel event on the list (marks "the user is scrolling").</summary>
    public void Wheel()
    {
        var wheel = new PointerWheelEventArgs(List, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), Window, new Point(50, 50),
            (ulong)Environment.TickCount64, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None, new Vector(0, 0)) { RoutedEvent = InputElement.PointerWheelChangedEvent };
        List.RaiseEvent(wheel);
    }

    /// <summary>Raises a KeyDown on a card's container, so it travels the real tunnel-then-bubble route (ListBox, ScrollViewer and our behavior all see it).</summary>
    /// <param name="card">The card whose container is the event source.</param>
    /// <param name="key">The key.</param>
    public void KeyDown(Card card, Key key)
    {
        var container = (Control)List.ContainerFromItem(card)!;
        container.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = container });
    }

    /// <summary>Closes the window.</summary>
    public void Close() { try { Window.Close(); } catch { /* best effort */ } }
}

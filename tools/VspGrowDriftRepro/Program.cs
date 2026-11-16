using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

// Repro: an item of a ListBox (default VirtualizingStackPanel) that is ON SCREEN grows, nothing else
// changes, and yet the item jumps up or down the viewport. Nothing above it changed size, so its top
// should stay where it was. A plain StackPanel (the control) keeps it in place.
//
//   dotnet run -c Release                    measures and prints a table; exit code 1 if the item moved
//   dotnet run -c Release -- --interactive   a window to see it by hand (select an item, press Grow)
//   add -- --classic to the interactive run to use a plain StackPanel instead

if (args.Contains("--interactive"))
{
    AppBuilder.Configure<ReproApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    return 0;
}

AppBuilder.Configure<ReproApp>().UsePlatformDetect().SetupWithoutStarting();
int exitCode = Measure.Run();
Environment.Exit(exitCode); // Avalonia keeps background threads alive
return exitCode;

/// <summary>The application: Fluent theme; in interactive mode, opens the demo window.</summary>
sealed class ReproApp : Application
{
    /// <summary>Adds the Fluent theme.</summary>
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <summary>Opens the interactive window when started with a desktop lifetime.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Interactive.BuildWindow(virtualizing: !desktop.Args!.Contains("--classic"));
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>A list item with a changeable height (stands in for a card whose inline editor opens).</summary>
sealed class Item : INotifyPropertyChanged
{
    private double _height;

    /// <summary>Position in the list.</summary>
    public int Index { get; init; }

    /// <summary>The item's height in pixels.</summary>
    public double Height
    {
        get => _height;
        set { _height = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Height))); }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The item's view: a coloured box whose height follows <see cref="Item.Height"/>. Written without
/// bindings so the repro has no dependency on binding flavours; it behaves as a {Binding Height} would.
/// </summary>
sealed class ItemView : Border
{
    private Item? _item;

    /// <summary>Creates the view.</summary>
    public ItemView()
    {
        Background = Brushes.LightSteelBlue;
        Margin = new Thickness(0, 2);
        Child = new TextBlock { Margin = new Thickness(4) };
    }

    /// <summary>Follows the item the (possibly recycled) container now shows.</summary>
    /// <param name="e">Unused.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = DataContext as Item;
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
        Refresh();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_item is null) return;
        Height = _item.Height;
        ((TextBlock)Child!).Text = $"Item {_item.Index}   ({_item.Height:0} px)";
    }
}

/// <summary>Builds the list used by both modes.</summary>
static class ListFactory
{
    /// <summary>
    /// 300 items of varied heights (50 to 139 px) in a ListBox. With <paramref name="virtualizing"/> the
    /// ListBox keeps its default VirtualizingStackPanel; otherwise it gets a plain StackPanel.
    /// </summary>
    /// <param name="virtualizing">True for the default VirtualizingStackPanel, false for a StackPanel.</param>
    /// <returns>The list and its items.</returns>
    public static (ListBox List, ObservableCollection<Item> Items) Build(bool virtualizing)
    {
        var items = new ObservableCollection<Item>(
            Enumerable.Range(0, 300).Select(i => new Item { Index = i, Height = 50 + (i * 37) % 90 }));
        var list = new ListBox
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<Item>((_, _) => new ItemView()),
        };
        if (!virtualizing) list.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        return (list, items);
    }

    /// <summary>The item's top relative to the list's ScrollViewer, or null when it has no container.</summary>
    /// <param name="list">The list.</param>
    /// <param name="item">The item.</param>
    /// <returns>The top in pixels, or null.</returns>
    public static double? TopOf(ListBox list, Item item) =>
        list.ContainerFromItem(item) is Control c && list.Scroll is Visual sv ? c.TranslatePoint(default, sv)?.Y : null;
}

/// <summary>Automatic mode: grows an on-screen item at several scroll positions and prints how far it moved.</summary>
static class Measure
{
    /// <summary>Runs every case for both panels and prints the table.</summary>
    /// <returns>1 if the virtualizing panel moved the item by more than 1 px in any case, else 0.</returns>
    public static int Run()
    {
        Console.WriteLine($"Avalonia {typeof(AvaloniaObject).Assembly.GetName().Version}, {Environment.OSVersion}, .NET {Environment.Version}");
        Console.WriteLine("Each row: scroll to startOffset in 250 px steps (like a mouse wheel), pick an item whose top is");
        Console.WriteLine("150-380 px down the viewport, add 'grow' px to its height, run layout until it settles.");
        Console.WriteLine("Nothing above the item changed, so 'moved' (its top after minus before) should be 0.");
        Console.WriteLine();
        Console.WriteLine("panel                   startOffset  grow  item  topBefore  topAfter   moved  offsetBefore  offsetAfter  afterwards");
        bool bad = false;
        foreach (bool virtualizing in new[] { true, false })
            foreach (double grow in new double[] { 3, 500 })
                foreach (double startOffset in new double[] { 3000, 6000, 9000, 12000 })
                {
                    double? moved = RunCase(virtualizing, startOffset, grow);
                    if (virtualizing && (moved is null || Math.Abs(moved.Value) > 1)) bad = true;
                }
        Console.WriteLine();
        Console.WriteLine(bad ? "RESULT: the VirtualizingStackPanel moved the grown item (bug reproduced)."
                              : "RESULT: the grown item stayed put with both panels (bug not reproduced).");
        return bad ? 1 : 0;
    }

    /// <summary>One case: build, scroll, grow, settle, print one row.</summary>
    /// <param name="virtualizing">Which panel.</param>
    /// <param name="startOffset">Where to scroll before growing.</param>
    /// <param name="grow">How many pixels to add to the item's height.</param>
    /// <returns>How far the item's top moved, or null when it no longer has a container.</returns>
    private static double? RunCase(bool virtualizing, double startOffset, double grow)
    {
        var (list, _) = ListFactory.Build(virtualizing);
        var window = new Window
        {
            Width = 420, Height = 700, Content = list,
            // Off-screen and not activated, so the run does not disturb the desktop.
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-20000, -20000),
        };
        window.Show();
        Settle(window, 6);
        var sv = (ScrollViewer)list.Scroll!;

        for (double o = 0; o < startOffset; o += 250) { sv.Offset = new Vector(0, o); Settle(window, 2); }
        sv.Offset = new Vector(0, startOffset);
        Settle(window, 6);

        Item? item = list.GetRealizedContainers()
            .Select(c => list.ItemFromContainer(c) as Item)
            .FirstOrDefault(i => i is not null && ListFactory.TopOf(list, i) is >= 150 and <= 380);
        string panel = virtualizing ? "VirtualizingStackPanel" : "StackPanel (control)";
        if (item is null) { Console.WriteLine($"{panel,-22}  {startOffset,11}  (no item in range)"); window.Close(); return null; }

        double? before = ListFactory.TopOf(list, item);
        double offsetBefore = sv.Offset.Y;
        item.Height += grow;
        Settle(window, 40);
        double? after = ListFactory.TopOf(list, item);
        double? moved = after - before;

        Console.WriteLine($"{panel,-22}  {startOffset,11}  {grow,4}  {item.Index,4}  {F(before),9}  {F(after),8}  {F(moved),6}  {offsetBefore,12:0.0}  {sv.Offset.Y,11:0.0}  {VisibleRange(list, sv)}");
        window.Close();
        return moved;
    }

    /// <summary>Runs full layout passes, draining the dispatcher between them.</summary>
    /// <param name="window">The window to lay out.</param>
    /// <param name="passes">How many passes.</param>
    private static void Settle(Window window, int passes)
    {
        for (int i = 0; i < passes; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    /// <summary>The first and last item at least partly inside the viewport, e.g. "items 50-56".</summary>
    /// <param name="list">The list.</param>
    /// <param name="sv">Its ScrollViewer.</param>
    /// <returns>The range as text.</returns>
    private static string VisibleRange(ListBox list, ScrollViewer sv)
    {
        var shown = list.GetRealizedContainers()
            .Where(c => c.IsVisible && c.TranslatePoint(default, sv)?.Y is double y && y < sv.Viewport.Height && y + c.Bounds.Height > 0)
            .Select(c => (list.ItemFromContainer(c) as Item)?.Index ?? -1)
            .Where(i => i >= 0).OrderBy(i => i).ToList();
        return shown.Count == 0 ? "nothing visible" : $"items {shown[0]}-{shown[^1]} visible";
    }

    private static string F(double? v) => v.HasValue ? v.Value.ToString("0.0") : "n/a";
}

/// <summary>Interactive mode: a window to see the jump by hand.</summary>
static class Interactive
{
    /// <summary>Builds the window: Grow / Shrink buttons above the list, and a line showing the selected item's top.</summary>
    /// <param name="virtualizing">True for the default VirtualizingStackPanel, false for a StackPanel.</param>
    /// <returns>The window.</returns>
    public static Window BuildWindow(bool virtualizing)
    {
        var (list, _) = ListFactory.Build(virtualizing);
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        double? topBeforeGrow = null;

        var grow = new Button { Content = "Grow selected +500" };
        var shrink = new Button { Content = "Shrink selected -500" };
        grow.Click += (_, _) => Change(+500);
        shrink.Click += (_, _) => Change(-500);

        void Change(double delta)
        {
            if (list.SelectedItem is not Item item || item.Height + delta < 20) return;
            topBeforeGrow = ListFactory.TopOf(list, item);
            item.Height += delta;
        }

        list.SelectionChanged += (_, _) => topBeforeGrow = null;
        list.LayoutUpdated += (_, _) =>
        {
            if (list.SelectedItem is not Item item) { status.Text = "Scroll down a long way, select an item, press Grow."; return; }
            var top = ListFactory.TopOf(list, item);
            string now = top is double t ? $"{t:0}" : "off screen";
            string was = topBeforeGrow is double b ? $"   (was {b:0} before the last change)" : "";
            status.Text = $"Item {item.Index} top: {now} px{was}";
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6), Children = { grow, shrink, status } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(list);
        return new Window
        {
            Title = $"VirtualizingStackPanel grow drift ({(virtualizing ? "VirtualizingStackPanel" : "StackPanel")})",
            Width = 560, Height = 760, Content = root,
        };
    }
}

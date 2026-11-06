using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Xml.Linq;
using TermPoint.Controls;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.Views.GridView;

namespace HelpShots;

/// <summary>
/// Entry point: draws the REAL filter bar (<see cref="GridFilterView"/> over a real <see cref="GridFilterViewModel"/>)
/// filled with made-up data, ticks the choices each Help example needs, and saves one PNG per example.
/// <para>
/// In the app only one chooser can be open at a time (each is a light-dismiss popup). For Help we want to show every
/// choice an example makes at once, so each chooser named for a shot has its popup content lifted out of the popup and
/// drawn in place under its button, inside the same view so all the view's own styles still apply.
/// </para>
/// Usage: <c>HelpShots &lt;repo-root&gt; [scale] [output-folder]</c>. Writes into <c>src/TermPoint/Help/images/filters/</c>
/// unless another folder is given; scale defaults to 2.
/// </summary>
static class Program
{
    /// <summary>
    /// Render scale: 2 = drawn at twice the size so the pictures stay sharp on high-DPI screens and when zoomed; the
    /// Help page shows them at half their pixel width. Done with a layout transform, NOT a higher bitmap dpi: at 192 dpi
    /// RenderTargetBitmap draws the contents of the choosers' scrolling lists at double size again (seen in Avalonia 12.1).
    /// </summary>
    private static double Scale = 2;

    /// <summary>Blank space kept around the lifted popups, so their drop shadows are not cut off.</summary>
    private const double ShadowRoom = 10;

    /// <summary>Where the PNGs go.</summary>
    private static string OutDir = "";

    /// <summary>Path of MainView.axaml, the source of the Schedule View title bar's "Filtered by:" statement.</summary>
    private static string MainViewPath = "";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: HelpShots <repo-root> [scale] [output-folder]");
            return 1;
        }
        string repo = args[0];
        OutDir = Path.Combine(repo, "src", "TermPoint", "Help", "images", "filters");
        // Optional: another scale (e.g. 1, to see exactly what a 100% screen shows) and another folder for such checks.
        if (args.Length > 1) Scale = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        if (args.Length > 2) OutDir = args[2];
        MainViewPath = Path.Combine(repo, "src", "TermPoint", "Views", "MainView.axaml");
        Directory.CreateDirectory(OutDir);

        // Never touch the developer's real settings.json: the filter view model reads (and may save) settings.
        string settingsDir = Path.Combine(Path.GetTempPath(), "termpoint-helpshots-settings");
        typeof(AppSettings).GetProperty("SettingsDirectoryOverride", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, settingsDir);

        ShotsBootstrap.Start(Path.Combine(repo, "src", "TermPoint", "App.axaml"));

        // ── The shots. Each names the choosers to draw open. ────────────────────────────────────────────

        // The bar with nothing chosen: the starting point.
        Shot("bar_empty", _ => { });

        // Example 1: lectures taught by Joe Smith or Sally Ford.
        Shot("ex1_open", vm =>
        {
            Check(vm.Instructors, "Joe Smith", "Sally Ford");
            Check(vm.SectionTypes, "Lecture");
        }, "Instructor", "SectionType");

        // Example 2: sections tagged Cohort1 or Cohort2 (or both) — "any"; and the "all" contrast.
        Shot("ex2_any_open", vm =>
        {
            Check(vm.Tags, "Cohort1", "Cohort2");
            vm.TagsMatchAny = true;
        }, "Tags");
        Shot("ex2_all_open", vm => Check(vm.Tags, "Cohort1", "Cohort2"), "Tags");

        // Example 3: second-year Biology, Chemistry and Mathematics.
        Shot("ex3_open", vm =>
        {
            Check(vm.Subjects, "Biology", "Chemistry", "Mathematics");
            Check(vm.Levels, "200");
        }, "Subject", "Level");

        // Example 4: sections still needing an instructor (the special item; named people are greyed out).
        Shot("ex4_unstaffed_open", vm => Check(vm.Instructors, "Unstaffed (Isolate)"), "Instructor");

        // Example 5: an overlay on top of a filter: all Mathematics, with Joe Smith's teaching added in red.
        Shot("ex5_overlay_open", vm =>
        {
            Check(vm.Subjects, "Mathematics");
            vm.SetInstructorOverlay("i-smith");
        }, "InstructorOverlay");

        Console.WriteLine($"Wrote PNGs to {OutDir}");
        return 0;
    }

    // ── Made-up data ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a filter view model over a small made-up department (no database). The ids are readable so shots can
    /// address items directly (e.g. the overlay instructor).
    /// </summary>
    /// <returns>A view model whose option lists are filled, with nothing chosen.</returns>
    private static GridFilterViewModel NewViewModel()
    {
        var instructors = new[]
        {
            Person("i-smith", "Joe", "Smith", "JS"),       Person("i-chen", "Marcus", "Chen", "MC"),
            Person("i-ford", "Sally", "Ford", "SF"),     Person("i-kim", "Grace", "Kim", "GK"),
            Person("i-okafor", "Lena", "Okafor", "LO"),  Person("i-patel", "Anya", "Patel", "AP"),
            Person("i-singh", "Ravi", "Singh", "RS"),
        }.ToDictionary(i => i.Id);

        var rooms = new[] { ("SCI", "101"), ("SCI", "204"), ("LIB", "110"), ("ENG", "220") }
            .Select(r => new Room { Id = $"r-{r.Item1}{r.Item2}", Building = r.Item1, RoomNumber = r.Item2 })
            .ToDictionary(r => r.Id);

        var subjects = new[] { "Biology", "Chemistry", "English", "History", "Mathematics", "Physics" }
            .Select(n => new Subject { Id = "s-" + n, Name = n, CalendarAbbreviation = n[..4].ToUpperInvariant() })
            .ToDictionary(s => s.Id);

        var campuses = new[] { "North Campus", "South Campus" }
            .Select(n => new Campus { Id = "c-" + n, Name = n }).ToDictionary(c => c.Id);

        // Courses: a few per subject, coded as in a calendar (e.g. "BIOL210").
        var courses = new[]
            {
                "BIOL101", "BIOL210", "BIOL220", "CHEM110", "CHEM201", "MATH101", "MATH225", "MATH310",
                "ENGL105", "HIST240", "PHYS120",
            }
            .Select(code => new Course { Id = "k-" + code, CalendarCode = code })
            .ToDictionary(c => c.Id);

        var vm = new GridFilterViewModel();
        vm.PopulateOptions(
            instructors, rooms, subjects, campuses,
            Values("Lecture", "Lab", "Seminar", "Tutorial"),
            Values("Cohort1", "Cohort2", "First-year", "Writing-intensive"),
            Values("In person", "Online", "Hybrid"),
            CourseLevelParser.AllLevels.ToDictionary(l => l, l => l),
            courses);
        return vm;
    }

    /// <summary>Makes one made-up instructor.</summary>
    private static Instructor Person(string id, string first, string last, string initials) =>
        new() { Id = id, FirstName = first, LastName = last, Initials = initials };

    /// <summary>Makes a lookup of simple named values (section types, tags, meeting types), keyed by a readable id.</summary>
    private static Dictionary<string, SchedulingEnvironmentValue> Values(params string[] names) =>
        names.Select((n, i) => new SchedulingEnvironmentValue { Id = "v-" + n, Name = n, SortOrder = i })
             .ToDictionary(v => v.Id);

    /// <summary>Ticks the named items of one chooser.</summary>
    /// <param name="items">The chooser's items.</param>
    /// <param name="names">Display names to tick.</param>
    /// <exception cref="ArgumentException">A name is not in the list (a typo in a shot).</exception>
    private static void Check(IEnumerable<FilterItemViewModel> items, params string[] names)
    {
        var list = items.ToList();
        foreach (var name in names)
            (list.FirstOrDefault(i => i.Name == name) ?? throw new ArgumentException($"No filter item '{name}'"))
                .IsSelected = true;
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws one picture: a fresh view model and the real filter bar, the example's choices made, the named choosers
    /// shown open, saved as <c>&lt;name&gt;.png</c>.
    /// </summary>
    /// <param name="name">File name without extension.</param>
    /// <param name="setup">Makes the example's choices on the view model.</param>
    /// <param name="openChoosers">
    /// Choosers to show open, by the prefix of their names in GridFilterView.axaml (e.g. "Instructor" for
    /// InstructorToggle / InstructorPopup, "InstructorOverlay" for the overlay chooser).
    /// </param>
    private static void Shot(string name, Action<GridFilterViewModel> setup, params string[] openChoosers)
    {
        var vm = NewViewModel();
        var view = new GridFilterView { DataContext = vm };
        setup(vm);

        // Re-parent the bar into a grid: row 0 the bar itself, row 1 room for the open choosers, and a canvas over
        // both on which the lifted popup contents are placed exactly where the popups would open.
        var bar = (Control)view.Content!;
        view.Content = null;
        var spacer = new Border();
        var overlay = new Canvas();
        var host = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Background = Brush("AppBackground"),
        };
        Grid.SetRow(bar, 0);
        Grid.SetRow(spacer, 1);
        Grid.SetRowSpan(overlay, 2);
        host.Children.Add(bar);
        host.Children.Add(spacer);
        host.Children.Add(overlay);
        view.Content = new Border { Padding = new Thickness(4), Background = Brush("AppBackground"), Child = host };

        // The Schedule View panel around it: its real title bar ("Schedule View", unit, semester, "Filtered by: …",
        // overlay), with the filter bar as the panel's content, as in the app.
        var header = LoadScheduleHeader();
        header.DataContext = new HeaderData(vm);
        var panel = new DetachablePanel { Header = "Schedule View", HeaderContext = header, PanelContent = view };

        var scaled = new LayoutTransformControl { LayoutTransform = new ScaleTransform(Scale, Scale), Child = panel };
        var window = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight, Content = scaled,
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-20000, -20000),
        };
        window.Show();
        Pump(window);

        double right = bar.Bounds.Right, bottom = bar.Bounds.Bottom;
        foreach (var chooser in openChoosers)
        {
            var popup = view.FindControl<Popup>(chooser + "Popup") ?? throw new ArgumentException($"No {chooser}Popup");
            var toggle = view.FindControl<ToggleButton>(chooser + "Toggle") ?? throw new ArgumentException($"No {chooser}Toggle");

            // Cut the popup loose from its button first, so ticking the button (for its "open" look) opens nothing.
            popup.ClearValue(Popup.IsOpenProperty);
            var content = popup.Child!;
            popup.Child = null;
            toggle.IsChecked = true;

            var at = toggle.TranslatePoint(new Point(0, toggle.Bounds.Height), host)!.Value;
            Canvas.SetLeft(content, at.X);
            Canvas.SetTop(content, at.Y + 1);
            overlay.Children.Add(content);
            ShowAllItems(content);
            content.Measure(Size.Infinity);
            right = Math.Max(right, at.X + content.DesiredSize.Width);
            bottom = Math.Max(bottom, at.Y + 1 + content.DesiredSize.Height);
        }
        if (openChoosers.Length > 0)
        {
            spacer.Height = bottom - bar.Bounds.Bottom + ShadowRoom;
            host.MinWidth = right + ShadowRoom;
        }
        Pump(window);

        var size = new PixelSize((int)Math.Ceiling(scaled.Bounds.Width), (int)Math.Ceiling(scaled.Bounds.Height));
        using (var bmp = new RenderTargetBitmap(size))
        {
            bmp.Render(scaled);
            bmp.Save(Path.Combine(OutDir, name + ".png"), PngBitmapEncoderOptions.Default);
        }
        Console.WriteLine($"  {name}.png  shown at {scaled.Bounds.Width / Scale:0}x{scaled.Bounds.Height / Scale:0}");
        window.Close();
    }

    /// <summary>
    /// Builds the Schedule View title bar's statement strip from the REAL MainView.axaml: the WrapPanel inside the
    /// ScheduleGridPanel's HeaderContext is lifted out and loaded with the runtime XAML loader, so its wording, fonts
    /// and separators are the shipping ones. Its bindings read <see cref="HeaderData"/>.
    /// </summary>
    /// <returns>The strip, ready for a DataContext.</returns>
    /// <exception cref="InvalidOperationException">MainView.axaml no longer has the ScheduleGridPanel header.</exception>
    private static Control LoadScheduleHeader()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = XDocument.Load(MainViewPath).Root!;
        var panel = root.Descendants().FirstOrDefault(e =>
                        e.Name.LocalName == "DetachablePanel" && (string?)e.Attribute(x + "Name") == "ScheduleGridPanel")
                    ?? throw new InvalidOperationException("MainView.axaml: no DetachablePanel named ScheduleGridPanel.");
        var strip = panel.Elements().FirstOrDefault(e => e.Name.LocalName == "DetachablePanel.HeaderContext")?.Elements().First()
                    ?? throw new InvalidOperationException("MainView.axaml: ScheduleGridPanel has no HeaderContext.");
        var element = new XElement(strip);
        foreach (var ns in root.Attributes().Where(a => a.IsNamespaceDeclaration))
            if (element.Attribute(ns.Name) is null)
                element.Add(new XAttribute(ns.Name, ns.Value));
        return (Control)AvaloniaRuntimeXamlLoader.Load(element.ToString(), typeof(TermPoint.App).Assembly);
    }

    /// <summary>
    /// Gives every list inside a lifted chooser a plain (non-virtualizing) item panel. Under the 2x layout transform
    /// the virtualizing panel misjudges what is on screen and creates only the first item or two; a plain StackPanel
    /// lays the items out exactly as the app shows them, all of them.
    /// </summary>
    /// <param name="content">The lifted popup content.</param>
    private static void ShowAllItems(Control content)
    {
        foreach (var list in content.GetLogicalDescendants().OfType<ListBox>())
            list.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
    }

    /// <summary>Runs pending UI work and layout so the window's contents are realised and measured.</summary>
    private static void Pump(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>Looks up an app brush by key (transparent when missing).</summary>
    private static IBrush Brush(string key) =>
        Application.Current!.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Transparent;
}

/// <summary>
/// What the Schedule View title bar's bindings read in MainView (<c>ScheduleGridVm.…</c>, <c>FocusVm.…</c>), made up
/// for the pictures except <see cref="ScheduleData.Filter"/>, which is the shot's real filter view model, so
/// "Filtered by: …" and the overlay statement are the app's own wording.
/// </summary>
public sealed class HeaderData(GridFilterViewModel filter)
{
    /// <summary>Stands in for MainViewModel.ScheduleGridVm.</summary>
    public ScheduleData ScheduleGridVm { get; } = new(filter);

    /// <summary>Stands in for MainViewModel.FocusVm: no subject focus, so the "Subjects:" statement stays hidden.</summary>
    public FocusData FocusVm { get; } = new();
}

/// <summary>The parts of ScheduleGridViewModel the title bar shows.</summary>
public sealed class ScheduleData(GridFilterViewModel filter)
{
    /// <summary>The made-up department name.</summary>
    public string AcademicUnitName => "Faculty of Science";

    /// <summary>One semester, shown as the app shows a single semester (an uncoloured pill).</summary>
    public IReadOnlyList<SemesterLineSegment> SemesterLineSegments { get; } = [new("Fall 2026", "", "")];

    /// <summary>The shot's real filter view model.</summary>
    public GridFilterViewModel Filter { get; } = filter;

    /// <summary>No shared schedules in the pictures.</summary>
    public string SharedScheduleBadge => "";
}

/// <summary>The part of FocusViewModel the title bar reads.</summary>
public sealed class FocusData
{
    /// <summary>Always false: the pictures have no subject focus.</summary>
    public bool IsIndicatorVisible => false;
}

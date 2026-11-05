using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using TermPoint.Views.GridView;

namespace TileWidthProbe;

/// <summary>What one render produced: sizes, per-day widths, and the clipping check.</summary>
sealed record RenderResult(string Png, double CanvasWidth, double CanvasHeight, List<(string Day, double Width)> Days,
                           List<(string Label, double Need, double Got)> Clipped,
                           List<(string Label, double Bottom, double Limit)> VClipped,
                           double DayGap, double InDayGap);

/// <summary>
/// Entry point: opens a COPY of a real database, builds the real grid view model and view in an off-screen
/// window, renders the slices at every text size with the CURRENT tile-width code, checks every label for clipping,
/// and writes PNGs plus REPORT.md.
/// Usage: <c>TileWidthProbe &lt;copy-of-db&gt; &lt;output-folder&gt; &lt;repo-root&gt; [measuring-margin-px]</c>.
/// </summary>
static class Program
{
    /// <summary>Overrides the <c>TileMeasuringMargin</c> resource when given (to find the smallest margin with no clipping); null keeps the app's value.</summary>
    private static double? MeasuringMarginOverride;

    [STAThread]
    static int Main(string[] args)
    {
        string dbCopy = args[0], outDir = args[1], repo = args[2];
        if (args.Length > 3) MeasuringMarginOverride = double.Parse(args[3], CultureInfo.InvariantCulture);
        Directory.CreateDirectory(outDir);

        // Never touch the developer's real settings.json: SemesterContext and the colour store save there.
        typeof(AppSettings).GetProperty("SettingsDirectoryOverride", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, Path.Combine(outDir, "settings"));

        ProbeBootstrap.Start(Path.Combine(repo, "src", "TermPoint", "App.axaml"));

        // ── The real data, real repositories, real view model ─────────────────────────────────────────
        var db = new DatabaseContext(dbCopy);
        var sectionRepo = new SectionRepository(db);
        var courseRepo = new CourseRepository(db);
        var subjectRepo = new SubjectRepository(db);
        var semRepo = new SemesterRepository(db);
        var ayRepo = new AcademicYearRepository(db);
        var instrRepo = new InstructorRepository(db);
        var envRepo = new SchedulingEnvironmentRepository(db);
        var roomRepo = new RoomRepository(db);
        var semCtx = new SemesterContext();
        var store = new SectionStore();
        var settings = new AppSettings();
        var colours = new SubjectColorStore([], () => settings);
        var focus = new FocusService(subjectRepo, courseRepo, sectionRepo, semRepo, ayRepo, semCtx, store, null);
        var presenter = new SubjectColorPresenter(colours, focus, store, courseRepo, subjectRepo);
        var notifier = new GridChangeNotifier();
        var vm = new ScheduleGridViewModel(
            sectionRepo, courseRepo, instrRepo, roomRepo, subjectRepo, envRepo, new CampusRepository(db), semCtx,
            new AcademicUnitService(new AcademicUnitRepository(db)), store, new MeetingStore(),
            new MeetingRepository(db), notifier, new InstructorCommitmentRepository(db), new WriteLockService(),
            new SharedScheduleService(),
            new AccessPanelViewModel(new ProgramWatchRepository(db), semCtx, notifier, envRepo, courseRepo, () => false),
            focus, presenter, new BlockPatternRepository(db));

        // Which semesters exist?
        var allSems = semRepo.GetAll().ToList();
        var years = ayRepo.GetAll().ToList();
        foreach (var s in allSems)
            Console.WriteLine($"semester: '{s.Name}' year={years.FirstOrDefault(y => y.Id == s.AcademicYearId)?.Name} id={s.Id}");
        var t1 = allSems.First(s => s.Name.Contains("Term 1", StringComparison.OrdinalIgnoreCase));
        var t2 = allSems.First(s => s.Name.Contains("Term 2", StringComparison.OrdinalIgnoreCase) && s.AcademicYearId == t1.AcademicYearId);
        Console.WriteLine($"Using '{t1.Name}' and '{t2.Name}'");
        void ShowSemesters(params Semester[] sems)
        {
            semCtx.Reload(ayRepo, semRepo, t1.AcademicYearId, sems.Select(s => s.Id).ToHashSet());
            store.Reload(sectionRepo, sems.Select(s => s.Id));
        }

        // ── The real view in an off-screen window ──────────────────────────────────────────────────────
        var view = new ScheduleGridView();
        var window = new Window
        {
            Width = 1800, Height = 1000, Content = view, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-20000, -20000),
        };
        window.Show();
        view.DataContext = vm;
        var renderMethod = typeof(ScheduleGridView).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var res = Application.Current!.Resources;
        if (MeasuringMarginOverride is { } margin) res["TileMeasuringMargin"] = margin;   // top-level entry wins over the merged AppLayout one
        double Layout(string key) => Application.Current!.TryGetResource(key, null, out var v) ? Convert.ToDouble(v) : double.NaN;
        Console.WriteLine($"TilePaddingHorizontal = {Layout("TilePaddingHorizontal")}, TileMeasuringMargin = {Layout("TileMeasuringMargin")}");

        // ── Filtering helpers (the same filter ticks a user makes) ────────────────────────────────────
        var subjects = subjectRepo.GetAll().ToList();

        // Ticks one filter item by id. Every tick makes the view model rebuild its filter lists (new item
        // objects), so the item is looked up afresh each time and never held across a tick.
        void Tick(Func<IEnumerable<TermPoint.ViewModels.GridView.FilterItemViewModel>> list, string id) =>
            list().First(i => i.Id == id).IsSelected = true;

        // Shows only the given subjects (by calendar abbreviation) and levels (by hundreds, "" for any level).
        void Filter(string[] abbreviations, params string[] levels)
        {
            vm.Filter.ClearFilters();
            foreach (var l in levels.Where(l => l != "")) Tick(() => vm.Filter.Levels, l);
            foreach (var a in abbreviations)
                Tick(() => vm.Filter.Subjects, subjects.First(s => s.CalendarAbbreviation == a).Id);
        }

        // TILEPROBE_FONTCHECK=1: compares text width and line height measured off the tree (default font, then the
        // theme's font the grid now borrows) with the same text in the tree, then stops.
        if (Environment.GetEnvironmentVariable("TILEPROBE_FONTCHECK") == "1")
        {
            Application.Current!.TryGetResource("ContentControlThemeFontFamily", null, out var themeObj);
            var themeFont = (FontFamily)themeObj!;
            Size Measure(TextBlock t) { t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return t.DesiredSize; }
            foreach (var (text, weight) in new[] { ("MECH463 102  MA2", FontWeight.Bold), ("MECH421 101  HM", FontWeight.SemiBold), ("Mg", FontWeight.SemiBold) })
            {
                var offDefault = Measure(new TextBlock { Text = text, FontSize = 11, FontWeight = weight });
                var offTheme = Measure(new TextBlock { Text = text, FontSize = 11, FontWeight = weight, FontFamily = themeFont });
                var inTree = new TextBlock { Text = text, FontSize = 11, FontWeight = weight };
                var host = new StackPanel(); host.Children.Add(inTree);
                window.Content = host; window.UpdateLayout();
                var onTree = Measure(inTree);
                Console.WriteLine($"FONTCHECK '{text}' {weight} 11px: off-tree default {offDefault.Width}x{offDefault.Height}, "
                                + $"off-tree theme font {offTheme.Width}x{offTheme.Height}, in-tree {onTree.Width}x{onTree.Height} (family {inTree.FontFamily.Name})");
            }
            Console.WriteLine($"FONTCHECK default font family {FontManager.Current.DefaultFontFamily}; theme font {themeFont}");
            Environment.Exit(0);
            return 0;
        }
        // ── Scenarios ─────────────────────────────────────────────────────────────────────────────────
        var results = new List<(string Slice, double Font, RenderResult R)>();
        double[] fonts = ScheduleGridViewModel.FontSizeOptions.ToArray();
        double defaultFont = new AppSettings().TileFontSize;
        Console.WriteLine("Font sizes offered: " + string.Join(", ", fonts) + $" (default {defaultFont})");

        void Run(string slice, Action? prepare = null)
        {
            foreach (var font in fonts)
            {
                vm.TileFontSize = font;
                prepare?.Invoke();
                renderMethod.Invoke(view, null);
                var r = Capture(window, view, Path.Combine(outDir, $"{slice}_f{font:0}.png"));
                results.Add((slice, font, r));
                Console.WriteLine($"{slice} font {font:0}: width {r.CanvasWidth:0.##} x height {r.CanvasHeight:0.##}, clipped {r.Clipped.Count}, vertically clipped {r.VClipped.Count}, day gap {r.DayGap:0.##}, in-day gap {r.InDayGap:0.##}");
            }
        }

        // 1. 400-level CIVL + MECH
        ShowSemesters(t1);
        Filter(["CIVL", "MECH"], "400");
        Run("s1_400_CIVL_MECH");
        Console.WriteLine($"slice 1 entries: {vm.GridData.DayColumns.Sum(c => c.Tiles.Sum(t => t.Entries.Count))}");

        // 1b. Same slice with a few entries SELECTED and FLAGGED (in memory only).
        var picks = vm.GridData.DayColumns.SelectMany(c => c.Tiles)
            .Where(t => t.Entries.Count > 0)
            .OrderByDescending(t => t.OverlapCount).ThenBy(t => t.StartMinutes)
            .SelectMany(t => t.Entries).Where(e => !e.IsCommitment && !string.IsNullOrEmpty(e.SectionId))
            .GroupBy(e => e.SectionId).Select(g => g.First()).Take(8).ToList();
        var selectedIds = picks.Take(4).Concat(picks.Skip(6)).Select(e => e.SectionId).ToList();
        var flagged = picks.Take(6).Select((e, i) => (e.SectionId, Flag: (SectionFlag)(1 + i % 3)))
                           .ToDictionary(x => x.SectionId, x => x.Flag);
        Console.WriteLine("selected: " + string.Join(", ", picks.Where(e => selectedIds.Contains(e.SectionId)).Select(e => e.Label)));
        Console.WriteLine("flagged:  " + string.Join(", ", picks.Where(e => flagged.ContainsKey(e.SectionId)).Select(e => e.Label)));
        foreach (var s in store.Sections)
            if (flagged.TryGetValue(s.Id, out var f)) s.Flag = f;   // in memory only; the database copy is never written
        Run("s1b_400_selected_flagged", () =>
        {
            vm.Reload();                               // rebuild the grid so the flags reach the tiles
            store.SetMultiSelection(selectedIds);      // selection restyles rows in place, like a click
        });
        store.SetMultiSelection([]);
        foreach (var s in store.Sections) s.Flag = SectionFlag.None;

        // 2. 300-level CIVL + ELEC + MECH
        Filter(["CIVL", "ELEC", "MECH"], "300");
        Run("s2_300_CIVL_ELEC_MECH");

        // 4. Multi-semester (Term 1 + Term 2) of slice 1
        ShowSemesters(t1, t2);
        Filter(["CIVL", "MECH"], "400");
        Run("s4_multisem_400_CIVL_MECH");

        // 5. Collapse days (1.2.4 item 2.13, task 4): slice 1 with Tue+Thu folded (MWF open) and Mon/Wed/Fri folded
        // (TR open), first with both semesters, then with one. A semester switch clears the folds, so each fold is
        // applied after the semesters and filter are set; ShowPattern is the same call the header menu makes.
        vm.ShowAllDays();
        Run("s5_fold_MWF_2sem", () => vm.ShowPattern([1, 3, 5]));
        Run("s5_fold_TR_2sem", () => vm.ShowPattern([2, 4]));
        ShowSemesters(t1);
        Filter(["CIVL", "MECH"], "400");
        vm.ShowAllDays();
        Run("s5_fold_MWF_1sem", () => vm.ShowPattern([1, 3, 5]));
        Run("s5_fold_TR_1sem", () => vm.ShowPattern([2, 4]));
        vm.ShowAllDays();

        WriteReport(outDir, results, defaultFont, Layout("TileMeasuringMargin"), Layout("TilePaddingHorizontal"));
        Console.WriteLine("Done.");
        Environment.Exit(0);
        return 0;
    }

    /// <summary>
    /// Captures the grid canvas to a PNG (screen palette, no title strip) and runs the clipping check.
    /// </summary>
    /// <param name="window">The off-screen host window.</param>
    /// <param name="view">The real grid view.</param>
    /// <param name="png">Output file.</param>
    /// <returns>Canvas width, day-group widths and any clipped labels.</returns>
    private static RenderResult Capture(Window window, ScheduleGridView view, string png)
    {
        var canvas = view.FindControl<Canvas>("GridCanvas")!;
        window.UpdateLayout();
        // Same steps the view's own ExportToPng takes before capturing.
        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();

        using (var bmp = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(canvas.Width), (int)Math.Ceiling(canvas.Height))))
        {
            bmp.Render(canvas);
            bmp.Save(png, PngBitmapEncoderOptions.Default);
        }

        // Day-group widths: each day name TextBlock is given the full day-group width by the view.
        double headerSize = Application.Current!.Resources.TryGetResource("FontSizeXLarge", null, out var h) ? (double)h! : 18;
        var days = canvas.Children.OfType<TextBlock>()
            .Where(t => t.FontSize == headerSize && !double.IsNaN(t.Width) && Canvas.GetTop(t) == 1)
            .OrderBy(t => Canvas.GetLeft(t)).Select(t => (t.Text ?? "", t.Width)).ToList();

        // Clipping check: every ellipsis-trimming label must have been given the width its text needs.
        var clipped = new List<(string, double, double)>();
        foreach (var tb in canvas.GetVisualDescendants().OfType<TextBlock>())
        {
            if (tb.TextTrimming != TextTrimming.CharacterEllipsis || string.IsNullOrEmpty(tb.Text)) continue;
            var probe = new TextBlock { Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight,
                                        FontFamily = tb.FontFamily, FontStyle = tb.FontStyle };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double need = probe.DesiredSize.Width, got = tb.Bounds.Width;
            if (need - got > 0.01)
            {
                clipped.Add((tb.Text!, need, got));
            }
        }

        // Vertical clipping check: every line's row (text, marker and flag together) must end inside its tile's
        // inner content area. The limit is the tile's height minus, for each border and padding between the row and the
        // tile edge (including a dual overlay's two borders), its bottom thickness and bottom padding.
        var vclipped = new List<(string, double, double)>();
        foreach (var tile in canvas.Children.OfType<Border>().Where(b => b.Child is Border or StackPanel))
        {
            foreach (var row in tile.GetVisualDescendants().OfType<Border>().Where(b => b.Tag != null))
            {
                double limit = tile.Bounds.Height;
                for (Visual? v = row.GetVisualParent(); v != null && v != tile.GetVisualParent(); v = v.GetVisualParent())
                    if (v is Border b) limit -= b.BorderThickness.Bottom + b.Padding.Bottom;
                double bottom = row.TranslatePoint(new Point(0, row.Bounds.Height), tile)?.Y ?? double.NaN;
                if (bottom - limit > 0.01)
                {
                    string text = row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "?";
                    vclipped.Add((text, bottom, limit));
                }
            }
        }

        // Gap measurement. Day-to-day: for each pair of neighbouring day groups that both hold tiles, the left border
        // edge of the next day's leftmost tile minus the right border edge of the previous day's rightmost tile
        // (minimum over all day boundaries). In-day: the smallest horizontal gap between two side-by-side tiles
        // (overlapping in time) of the same day group.
        var headers = canvas.Children.OfType<TextBlock>()
            .Where(t => t.FontSize == headerSize && !double.IsNaN(t.Width) && Canvas.GetTop(t) == 1)
            .Select(t => (Left: Canvas.GetLeft(t), Right: Canvas.GetLeft(t) + t.Width)).OrderBy(t => t.Left).ToList();
        var tiles = canvas.Children.OfType<Border>().Where(b => b.Child is Border or StackPanel)
            .Select(b => (L: Canvas.GetLeft(b), R: Canvas.GetLeft(b) + b.Width, T: Canvas.GetTop(b), B: Canvas.GetTop(b) + b.Height)).ToList();
        int GroupOf(double x) => headers.FindIndex(h => x >= h.Left - 0.01 && x < h.Right - 0.01);
        double dayGap = double.NaN, inDay = double.NaN;
        var groups = tiles.GroupBy(t => GroupOf(t.L)).Where(g => g.Key >= 0).OrderBy(g => g.Key).ToList();
        for (int i = 1; i < groups.Count; i++)
            if (groups[i].Key == groups[i - 1].Key + 1)
            {
                double g = groups[i].Min(t => t.L) - groups[i - 1].Max(t => t.R);
                dayGap = double.IsNaN(dayGap) ? g : Math.Min(dayGap, g);
            }
        foreach (var g in groups)
            foreach (var a in g)
                foreach (var b in g)
                    if (b.L >= a.R - 0.5 && a.T < b.B - 0.5 && b.T < a.B - 0.5)
                    {
                        double gap = b.L - a.R;
                        inDay = double.IsNaN(inDay) ? gap : Math.Min(inDay, gap);
                    }
        return new RenderResult(png, canvas.Width, canvas.Height, days, clipped, vclipped, dayGap, inDay);
    }

    /// <summary>Writes REPORT.md: the width table, per-day widths and every clipped label.</summary>
    /// <param name="outDir">Folder to write into.</param>
    /// <param name="results">Every render made.</param>
    /// <param name="defaultFont">The app's default tile font size, marked in the table.</param>
    /// <param name="margin">The <c>TileMeasuringMargin</c> in force.</param>
    /// <param name="gap">The <c>TilePaddingHorizontal</c> in force.</param>
    private static void WriteReport(string outDir, List<(string Slice, double Font, RenderResult R)> results,
                                    double defaultFont, double margin, double gap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tile width probe report").AppendLine();
        sb.AppendLine($"Current code, TilePaddingHorizontal {gap} px, TileMeasuringMargin {margin} px. Measured by rendering the real ScheduleGridView. "
                    + "A clipped label is an ellipsis-trimmed label that was given less width than its text needs; the check must show none.").AppendLine();
        sb.AppendLine("| Slice | Font | Canvas width px | Canvas height px | Width-clipped labels | Vertically clipped lines | Min day-to-day gap px | Min in-day gap px |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
            sb.AppendLine($"| {r.Slice} | {r.Font:0}{(r.Font == defaultFont ? " (default)" : "")} | {r.R.CanvasWidth:0.##} | {r.R.CanvasHeight:0.##} | {r.R.Clipped.Count} | {r.R.VClipped.Count} | {r.R.DayGap:0.##} | {r.R.InDayGap:0.##} |");
        sb.AppendLine().AppendLine("## Per-day widths (day-group width in px)").AppendLine();
        foreach (var r in results)
            sb.AppendLine($"- **{r.Slice} @ {r.Font:0}**: " + string.Join(", ", r.R.Days.Select(d => $"{d.Day} {d.Width:0.#}")));
        sb.AppendLine().AppendLine("## Clipped labels (text needs more than it got)").AppendLine();
        foreach (var r in results.Where(r => r.R.Clipped.Count > 0))
            sb.AppendLine($"- **{r.Slice} @ {r.Font:0}**: " + string.Join("; ",
                r.R.Clipped.Select(c => $"\"{c.Label}\" needs {c.Need:0.##}, got {c.Got:0.##}")));
        if (results.All(r => r.R.Clipped.Count == 0)) sb.AppendLine("none");
        sb.AppendLine().AppendLine("## Vertically clipped lines (row ends below the tile's inner bottom)").AppendLine();
        foreach (var r in results.Where(r => r.R.VClipped.Count > 0))
            sb.AppendLine($"- **{r.Slice} @ {r.Font:0}**: " + string.Join("; ",
                r.R.VClipped.Select(c => $"\"{c.Label}\" ends {c.Bottom:0.##}, limit {c.Limit:0.##}")));
        if (results.All(r => r.R.VClipped.Count == 0)) sb.AppendLine("none");
        File.WriteAllText(Path.Combine(outDir, "REPORT.md"), sb.ToString());
    }
}

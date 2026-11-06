using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.GridView;
using System.ComponentModel;

namespace TermPoint.Views.GridView;

public partial class ScheduleGridView : UserControl
{
    private record TileClickContext(string SectionId, int Day, int StartMinutes);

    /// <summary>
    /// Snapshot of every entry row rendered during the last full <see cref="Render"/> call.
    /// Used by <see cref="UpdateSelectionHighlight"/> to repaint selection state without
    /// triggering a full layout pass. A line's subject marker needs no restyle: selection changes only the text.
    /// </summary>
    private record EntryRowInfo(Border Row, TextBlock Label, string SectionId, bool IsOverlay, bool IsDeemphasized, IBrush BaseBg);
    private readonly List<EntryRowInfo> _entryRowRegistry = new();

    /// <summary>
    /// Subject marker brushes by <c>#RRGGBB</c> string (case-insensitive), so a reload re-uses one brush per subject
    /// colour instead of allocating one per tile line. Colours come from the user's settings, so the cache stays tiny.
    /// </summary>
    private static readonly Dictionary<string, IBrush> _subjectMarkerBrushes = new(StringComparer.OrdinalIgnoreCase);
    
    // Layout constants
    private const double TimeGutterWidth  = 52;
    private const double DayHeaderHeight  = 21;   // height of the day-name row
    private const double SemesterBarHeight = 5;   // thin colored bar below day name in multi-semester mode
    private const double HalfHourHeight   = 30;   // pixels per 30-minute slot
    private const double GridBottomPadding = 12;  // extra space below the last gridline so the label and rule aren't clipped
    
    /// <summary>
    /// Padding added to each side of a day header's text when computing the minimum
    /// width of a day column. The column will be at least (headerTextWidth + 2 × this)
    /// wide, even if the day has no sections. Adjust this value to control how much
    /// breathing room empty or lightly-loaded day columns receive.
    /// </summary>
    private const double DayHeaderSidePadding = 15; // 15px left + 15px right = 30px total

    /// <summary>
    /// Reads the tile chrome (borders, paddings, flag icon, measuring margin) from <c>AppLayout.axaml</c>. Both the
    /// tile drawing (Phase 4) and the tile width calculation (Phases 1 and 1.5) use the returned values, so they
    /// cannot drift apart. Call once per render and keep the result.
    /// </summary>
    /// <returns>The chrome for the current resources.</returns>
    private static TileChrome ReadTileChrome() => new(
        Gap:                TilePaddingH,
        BorderThin:         Layout("TileBorderThickness"),
        BorderThick:        Layout("TileThickBorderThickness"),
        OverlayOuterBorder: Layout("TileOverlayOuterBorderThickness"),
        ContentPadding:     Resource<Thickness>("TileContentPadding", default),
        EntryRowPadding:    Resource<Thickness>("TileEntryRowPadding", default),
        SelectionBorder:    Layout("TileSelectionBorderThickness"),
        FlagIconSize:       Layout("TileFlagIconSize"),
        FlagIconMargin:     Resource<Thickness>("TileFlagIconMargin", default),
        MeasuringMargin:    Layout("TileMeasuringMargin"));

    /// <summary>
    /// The font family the tiles are actually drawn in, which the width and height measuring must use too.
    /// Tile text blocks inherit it from the window (the theme sets it; the app registers Inter). A text block
    /// measured OFF the tree would get the platform default font instead (a different, narrower face), so the
    /// measuring always borrows this one. Before the view is attached it has not inherited anything yet, so the
    /// theme's own value is read instead.
    /// </summary>
    private FontFamily DrawnFontFamily =>
        !this.IsAttachedToVisualTree()
        && Application.Current!.TryGetResource("ContentControlThemeFontFamily", null, out var themeFont)
        && themeFont is FontFamily themeFamily
            ? themeFamily
            : FontFamily;
    
    /// <summary>
    /// Cached export palette loaded from PrintColors.axaml on first export.
    /// </summary>
    private static ResourceDictionary? _printPalette;

    /// <summary>
    /// When true, <see cref="Res"/> resolves brushes from <see cref="_printPalette"/> first,
    /// falling back to the normal application resources for any key not overridden.
    /// Set only during <see cref="ExportToPng"/> to produce higher-contrast output.
    /// </summary>
    private static bool _useExportPalette;

    /// <summary>
    /// Resolves a brush from the application resource dictionary. When the export palette
    /// is active, checks the print-specific overrides first.
    /// </summary>
    private static IBrush Res(string key)
    {
        if (_useExportPalette && _printPalette != null
            && _printPalette.TryGetResource(key, null, out var pv) && pv is IBrush pb)
            return pb;

        return Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : Brushes.Transparent;
    }
    private static double FontSizeFromResource(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is double d
            ? d : 12;
    private static double Layout(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is double d
            ? d : 0;

    private static double TilePaddingV   => Layout("TilePaddingVertical");
    private static double TilePaddingH   => Layout("TilePaddingHorizontal");
    private static double MultiSemPad    => Layout("MultiSemesterInternalTilePadding");
    private static double DayEdgeGap     => Layout("DayEdgeGap");

    /// <summary>Border thickness applied to the entry row of a user-selected section tile (<c>TileSelectionBorderThickness</c>, AppLayout.axaml).</summary>
    private static double TileSelectionBorderThickness => Layout("TileSelectionBorderThickness");

    private static IBrush TileFill              => Res("TileFill");
    private static IBrush MeetingTileFill       => Res("MeetingTile");
    private static IBrush ToolTipBorder            => Res("ToolTipBorder");
    private static IBrush TileExternalBorder    => Res("TileExternalBorder");
    private static IBrush TileInternalBorder    => Res("TileInternalBorder");
    private static IBrush SectionMeetingTextSelected    => Res("SectionMeetingTextSelected");
    private static IBrush UserSelectedBorder    => Res("UserSelectedSectionBorderColor");
    private static IBrush OverlayFrameBorder    => Res("OverlayFrameBorder");
    private static IBrush TileDeemphasizedText  => Res("TileDeemphasizedText");
    private static IBrush TileEntryHoverOverlay => Res("CardHoverOverlay");
    private static IBrush RuleBrush          => Res("GridRuleLine");
    private static IBrush HourRuleBrush      => Res("GridHourRuleLine");
    private static IBrush HeaderFill         => Res("WeekDayBar");
    private static IBrush HeaderBorder       => Res("DaySeparators");
    private static IBrush GutterBg           => Res("TimesColumn");
    private static IBrush TileText => Res("TextPrimary");
    private static IBrush HalfHourText => Res("GridTimeHalfHourText");
    private static IBrush ScheduleBackground => Res("AppBackground");
    private static IBrush FilterEmphasizedBg  => Res("FilterSelectedSectionBackgroundColor");

    // Ghost tile brushes (Room Availability Browser candidate slots)
    private static IBrush GhostOutlineBrush => Res("GhostOutline");
    private static IBrush GhostTabFill      => Res("GhostTabFill");
    private static IBrush GhostTabText      => Res("GhostTabText");
    private static IBrush GhostBodyFill     => Res("GhostBodyFill");

    // Shared schedule tile brushes (cross-department CSV overlay)
    private static IBrush SharedScheduleBorder  => Res("SharedScheduleBorder");
    private static IBrush SharedScheduleText    => Res("SharedScheduleText");

    // Program conflict overlay (student-access time overlap indicator)
    private static IBrush ProgramConflictBrush  => Res("ProgramConflictBrush");
    private static Color ResColor(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is Color c
            ? c : Colors.Transparent;
    private static Color ProgramConflictGlowCenter => ResColor("ProgramConflictGlowCenter");
    private static Color ProgramConflictGlowEdge   => ResColor("ProgramConflictGlowEdge");

    // Section attention-flag geometry (resolved once; brush is per-flag via FlagVisuals).
    private static Geometry? FlagGeometry =>
        Application.Current!.Resources.TryGetResource("FlagIcon", null, out var v) && v is Geometry g ? g : null;

    /// <summary>The fold icon shown on a hovered day header (<c>FoldDayIcon</c>, Icons.axaml); null if the resource is missing.</summary>
    private static Geometry? FoldDayGeometry =>
        Application.Current!.Resources.TryGetResource("FoldDayIcon", null, out var v) && v is Geometry g ? g : null;


    private Canvas? _canvas;
    private ScheduleGridViewModel? _vm;
    private Border? _zoomContainer;
    private Popup? _tileContextPopup;
    private double _zoomLevel = 1.0;

    // Ghost overlay: cached layout parameters from the last full Render() pass, enabling
    // ghost tiles to be added/removed without re-running the entire grid pipeline.
    private double[]? _cachedDayXOffsets;
    private double[]? _cachedDayColWidths;
    private Dictionary<int, double>? _cachedGridlineYOffsets;
    private double _cachedEffectiveHeaderHeight;
    private int _cachedFirstRowMinutes;
    private int _cachedSemesterCount;
    private readonly List<Control> _ghostControls = new();

    // Folded-strip attention marks (collapse days, task 5). The strips are drawn by Render(); the marks on them are
    // drawn (and redrawn) by RefreshStripMarks(), which the light repaints (selection, ghosts) can call without a
    // full Render. These hold what it needs from the last Render.
    /// <summary>What <see cref="RefreshStripMarks"/> needs about one folded strip: its hit area (for the tooltip), its summary and its geometry.</summary>
    private sealed record StripVisual(Border Hit, string DayName, FoldedDaySummary? Summary, double X, double Width, double BodyY, double BodyHeight);
    private readonly Dictionary<int, StripVisual> _stripVisuals = new();
    /// <summary>The mark bars currently on the canvas; removed and redrawn by every <see cref="RefreshStripMarks"/>.</summary>
    private readonly List<Control> _markControls = new();
    /// <summary>Remembers each strip's last marks so only NEW marks flash (P2-4).</summary>
    private readonly StripAttention _stripAttention = new();
    /// <summary>Cancelled by the next <see cref="Render"/> (which clears the canvas and so any flash overlay on it) or on detach.</summary>
    private CancellationTokenSource _flashCts = new();

    // Tile font size lives on ScheduleGridViewModel.TileFontSize (initialized from AppSettings).
    // The in-grid ComboBox binds to it directly; changes trigger OnVmPropertyChanged → Render().

    // Resize debounce: SizeChanged fires per pixel during an interactive drag. A short tail
    // collapses the burst into a single Render() once the user has stopped resizing.
    private DispatcherTimer? _resizeDebounce;
    private Size _lastRenderedBounds;

    public ScheduleGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChangedDebounced;

        // Wire up zoom slider to ScaleTransform on the zoom container
        var slider = this.FindControl<Slider>("ZoomSlider");
        _zoomContainer = this.FindControl<Border>("ZoomContainer");
        _tileContextPopup = this.FindControl<Popup>("TileContextPopup");

        if (slider is not null)
        {
            slider.Value = _zoomLevel;
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == nameof(Slider.Value))
                {
                    _zoomLevel = slider.Value;
                    UpdateZoomTransform();
                    UpdateZoomLabel();
                }
            };
        }
        UpdateZoomLabel();

        // TileFontSizeBox is bound in AXAML to ScheduleGridViewModel.TileFontSize / FontSizeOptions.
    }   

    
    /// <summary>
    /// SizeChanged handler with a 50 ms debounce. Coalesces per-pixel resize events into
    /// a single Render() call once the resize has been quiet, eliminating jank during
    /// interactive window/panel drags. No-op resizes (bounds identical to the last
    /// rendered size) are skipped outright.
    /// </summary>
    private void OnSizeChangedDebounced(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize == _lastRenderedBounds) return;

        if (_resizeDebounce is null)
        {
            _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _resizeDebounce.Tick += (_, _) =>
            {
                _resizeDebounce!.Stop();
                _lastRenderedBounds = Bounds.Size;
                Render();
            };
        }

        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void UpdateZoomTransform()
    {
        if (_zoomContainer?.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = _zoomLevel;
            scale.ScaleY = _zoomLevel;
        }
        UpdateZoomContainerSize();
    }

    /// <summary>
    /// Keeps the ZoomContainer's layout dimensions in sync with the scaled canvas size.
    /// <para>
    /// RenderTransform is a visual-only operation — it scales rendered pixels but does not
    /// participate in Avalonia's layout pass. As a result, the ScrollViewer would otherwise
    /// measure the unscaled canvas size and never show scrollbars when zoomed in.
    /// Explicitly setting ZoomContainer.Width/Height to canvas × zoomLevel gives the
    /// ScrollViewer the correct scrollable extent at every zoom level.
    /// </para>
    /// <para>Must be called whenever either the zoom level or the canvas dimensions change.</para>
    /// </summary>
    private void UpdateZoomContainerSize()
    {
        if (_zoomContainer is null || _canvas is null) return;
        if (double.IsNaN(_canvas.Width) || _canvas.Width <= 0) return;

        _zoomContainer.Width  = _canvas.Width  * _zoomLevel;
        _zoomContainer.Height = _canvas.Height * _zoomLevel;
    }

    private void UpdateZoomLabel()
    {
        var label = this.FindControl<TextBlock>("ZoomPercentLabel");
        if (label is not null)
            label.Text = $"{(int)(_zoomLevel * 100)}%";
    }


    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ScheduleGridViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;

        Render();
    }

    /// <summary>
    /// Cleanup when removed from the visual tree (e.g. detached window closing).
    /// Unsubscribes from the long-lived VM and releases heavy rendering state so
    /// the discarded view can be garbage-collected.
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }
        _resizeDebounce?.Stop();
        _canvas?.Children.Clear();
        _entryRowRegistry.Clear();
        _ghostControls.Clear();
        _flashCts.Cancel();
        _markControls.Clear();
        _stripVisuals.Clear();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleGridViewModel.GridData))
            Render();
        else if (e.PropertyName == nameof(ScheduleGridViewModel.SelectedSectionIds))
            UpdateSelectionHighlight();
        else if (e.PropertyName == nameof(ScheduleGridViewModel.TileFontSize))
            Render();
        else if (e.PropertyName == nameof(ScheduleGridViewModel.GhostBlocks))
        {
            RenderGhostOverlay();
            RefreshStripMarks();   // ghosts on a folded day are strip marks, not tiles
        }
    }

    /// <summary>
    /// Repaints entry-row selection styling without a full layout pass. Called when only
    /// <see cref="ScheduleGridViewModel.SelectedSectionIds"/> changes — the tile geometry
    /// is unchanged, so only Background, FontWeight, and Foreground need updating.
    /// </summary>
    private void UpdateSelectionHighlight()
    {
        var selectedIds = _vm?.SelectedSectionIds ?? new HashSet<string>();

        foreach (var info in _entryRowRegistry)
        {
            bool isSelected = selectedIds.Contains(info.SectionId);

            info.Row.Background      = info.BaseBg;
            info.Row.BorderBrush     = isSelected ? UserSelectedBorder : Brushes.Transparent;
            info.Row.BorderThickness = new Thickness(isSelected ? TileSelectionBorderThickness : 0);
            info.Label.FontWeight    = isSelected ? FontWeight.Bold : FontWeight.SemiBold;
            info.Label.Foreground = isSelected         ? SectionMeetingTextSelected
                                  : info.IsOverlay      ? OverlayFrameBorder
                                  : info.IsDeemphasized ? TileDeemphasizedText
                                  : TileText;
        }

        // Selected sections on folded days are marked on their strips (a light redraw, no Render).
        RefreshStripMarks();
    }

    private void Render()
    {
        _canvas ??= this.FindControl<Canvas>("GridCanvas");
        if (_canvas is null) return;

        _canvas.Children.Clear();
        _entryRowRegistry.Clear();
        _stripVisuals.Clear();
        _markControls.Clear();
        // The canvas (and any flash overlay on it) is gone: stop the running flash and start a fresh token.
        _flashCts.Cancel();
        _flashCts = new CancellationTokenSource();

        var data = _vm?.GridData ?? GridData.Empty;
        if (data.DayColumns.Count == 0) { ShowEmpty(); return; }

        try
        {

        // ── Resource snapshot ───────────────────────────────────────────────
        // Each static accessor below (TileFill, TilePaddingH, etc.) calls
        // Application.Current.Resources.TryGetResource, which walks the merged resource
        // dictionaries on every access. Without this snapshot the per-tile loop in
        // Phase 4 would issue dozens of those lookups per tile — measurable cost when
        // multiplied across a full grid render.
        //
        // C# language note: a local variable may use the SAME identifier as a static
        // member of the enclosing class. Inside the method body, unqualified references
        // resolve to the local (it "shadows" the static property). The compiler accepts
        // this without warning. We exploit that here: by naming each local exactly the
        // same as its source property, every existing reference inside Render() — there
        // are dozens — automatically picks up the snapshot with zero call-site churn.
        // Other methods in this class (UpdateSelectionHighlight, BuildTileTooltipContent,
        // AddRect, etc.) are outside this scope and continue to read the static properties
        // directly, which is the correct behaviour: their call frequency is low and they
        // would not benefit from snapshotting.
        //
        // The fully-qualified "ScheduleGridView.X" on the right-hand side is required —
        // the local is in scope from its declaration point, so a bare "X" on the RHS
        // would be a self-reference (use-before-assignment).
        TileChrome chrome              = ReadTileChrome();
        FontFamily drawnFont           = DrawnFontFamily;
        double TilePaddingV            = ScheduleGridView.TilePaddingV;
        double TilePaddingH            = ScheduleGridView.TilePaddingH;
        double MultiSemPad             = ScheduleGridView.MultiSemPad;
        double DayEdgeGap              = ScheduleGridView.DayEdgeGap;
        IBrush TileFill                = ScheduleGridView.TileFill;
        IBrush MeetingTileFill         = ScheduleGridView.MeetingTileFill;
        IBrush TileExternalBorder      = ScheduleGridView.TileExternalBorder;
        IBrush TileInternalBorder      = ScheduleGridView.TileInternalBorder;
        IBrush SectionMeetingTextSelected = ScheduleGridView.SectionMeetingTextSelected;
        IBrush UserSelectedBorder      = ScheduleGridView.UserSelectedBorder;
        IBrush OverlayFrameBorder      = ScheduleGridView.OverlayFrameBorder;
        IBrush TileDeemphasizedText    = ScheduleGridView.TileDeemphasizedText;
        IBrush TileEntryHoverOverlay   = ScheduleGridView.TileEntryHoverOverlay;
        IBrush RuleBrush               = ScheduleGridView.RuleBrush;
        IBrush HourRuleBrush           = ScheduleGridView.HourRuleBrush;
        IBrush HeaderFill              = ScheduleGridView.HeaderFill;
        IBrush HeaderBorder            = ScheduleGridView.HeaderBorder;
        IBrush GutterBg                = ScheduleGridView.GutterBg;
        IBrush TileText                = ScheduleGridView.TileText;
        IBrush HalfHourText            = ScheduleGridView.HalfHourText;
        IBrush ScheduleBackground      = ScheduleGridView.ScheduleBackground;
        IBrush FilterEmphasizedBg      = ScheduleGridView.FilterEmphasizedBg;

        int dayCount        = data.DayColumns.Count;
        int semCount        = data.SemesterCount;
        int logicalDayCount = dayCount / semCount;
        // Position arithmetic below (ld * semCount + s, d % semCount, firstCol + semCount) is safe: it
        // only groups the semCount sub-columns of ONE day, which GridData always stores contiguously, and
        // it never says WHICH day. For the real day use data.DayOfColumn / data.FirstColumnOfDay.

        // In multi-semester mode a thin colored bar sits below the day-name row so users
        // can quickly identify which sub-column belongs to which semester.
        // effectiveHeaderHeight is the combined height of both the day-name row and the bar
        // (equals DayHeaderHeight in single-semester mode where no bar is drawn).
        double effectiveHeaderHeight = DayHeaderHeight + (data.IsMultiSemester ? SemesterBarHeight : 0);

        
        // ── Phase 1: Measure tile content (heights and per-tile label widths) ─
        // tileMaxTextWidths maps (flatColumn, startMinutes, endMinutes) → the widest
        // Bold label text width across all entries in that tile. Used in Phase 1.5 to
        // compute per-overlap-slot natural widths.
        var tileMaxTextWidths = new Dictionary<(int d, int start, int end), double>();
        // tileHeightMap maps (startMinutes, endMinutes) → (timeBasedHeight, actualHeight).
        // Key is the tile's time span only — column is intentionally omitted because in
        // multi-semester mode co-scheduled tiles across semester sub-columns share the same
        // time slot and must expand to the same height so they stay vertically aligned.
        // timeBasedHeight is the pixel height derived purely from the time span (before any
        // content expansion); actualHeight is the measured StackPanel height. When
        // actualHeight > timeBasedHeight the difference is distributed across the covered
        // 30-minute gridline slots in Phase 3 to push later rows down. Only the tallest
        // tile at each (start, end) pair is kept — hence the Max comparison at insert.
        var tileHeightMap     = new Dictionary<(int, int), (double timeBasedHeight, double actualHeight)>();
        var selectedIds       = _vm?.SelectedSectionIds ?? new HashSet<string>();
        var entryCursor       = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        // Collect all tiles; used later for gridline expansion calculation.
        var allTiles = new List<(int day, GridTile tile, double timeBasedHeight)>();

        // Calibrate single-entry line height once. The Phase 4 visual tree wraps each entry
        // in a Border + TextBlock; the only height contributor that varies with content is
        // the TextBlock's own line metrics (the wrapping Border has zero vertical padding).
        // Because labels use CharacterEllipsis trimming (no wrapping), every entry is one
        // line tall, so a single measurement supplies the per-entry baseline for all tiles.
        // Using TextBlock.Measure keeps us on the same measurement basis as Phase 4 — the
        // existing WASM per-entry fudge (PlatformCapabilities.TileHeightMarginPerEntry) is
        // applied identically below.
        double entryLineH = MeasureLineHeight(drawnFont, _vm!.TileFontSize, FontWeight.SemiBold);
        const double EntrySeparatorH = 5; // 1 px rule + 2 px top margin + 2 px bottom margin (see Phase 4)

        for (int d = 0; d < dayCount; d++)
        {
            foreach (var tile in data.DayColumns[d].Tiles)
            {
                // Time-proportional height before any content expansion
                double timeBasedH = TimeToY(tile.EndMinutes, data.FirstRowMinutes)
                                  - TimeToY(tile.StartMinutes, data.FirstRowMinutes)
                                  - TilePaddingV * 2;
                timeBasedH = Math.Max(timeBasedH, 18);

                // Compute natural tile height arithmetically rather than by building a
                // throwaway visual tree. Components, matching the Phase 4 layout exactly:
                //   • per entry        : its line (entryLineH, or the flag icon if taller) + row padding top/bottom
                //   • per selected row : 2 × TileSelectionBorderThickness (top + bottom border)
                //   • between entries  : EntrySeparatorH
                //   • the tile's own   : borders + content padding, top and bottom (TileChrome.ChromeHeight)
                //   • WASM compensation: TileHeightMarginPerEntry × entry count
                // This is the tile's drawn (outer) height; the tileHeightMap key spans semester sub-columns, so each
                // tile's value is computed here first and only then maxed into the map below.
                int entryCount     = tile.Entries.Count;
                int selectedCount  = 0;
                double entriesH    = 0;
                for (int ei = 0; ei < entryCount; ei++)
                {
                    if (selectedIds.Contains(tile.Entries[ei].SectionId)) selectedCount++;
                    entriesH += chrome.EntryHeight(entryLineH, tile.Entries[ei].Flag != SectionFlag.None);
                }

                double actualH = entriesH
                               + selectedCount * 2 * chrome.SelectionBorder
                               + Math.Max(0, entryCount - 1) * EntrySeparatorH
                               + chrome.ChromeHeight(tile.Entries.Any(e => e.IsOverlay),
                                                     data.IsMultiSemester && !string.IsNullOrEmpty(tile.SemesterName))
                               + entryCount * PlatformCapabilities.TileHeightMarginPerEntry;

                // Measure the widest entry label in this tile using Bold (the heavier weight
                // applied when selected), guaranteeing the column fits in both render states.
                // The measuring uses the font the tiles are drawn in (drawnFont); a marker and a flag icon
                // add their own fixed widths to a line (TileChrome.EntryWidth).
                double tileTextW = 0;
                foreach (var e in tile.Entries)
                {
                    string lbl = BuildTileLabel(e.Label, e.Initials, e.FrequencyAnnotation);
                    // A subject marker adds its side and the gap after it to the line (colour by subject).
                    double markerW = SubjectMarkerBrush(e.SubjectColor) is null ? 0 : SubjectMarkerSide + SubjectMarkerGap;
                    tileTextW = Math.Max(tileTextW, chrome.EntryWidth(
                        MeasureTextWidth(drawnFont, lbl, _vm!.TileFontSize, FontWeight.Bold), markerW,
                        e.Flag != SectionFlag.None));
                }
                tileMaxTextWidths[(d, tile.StartMinutes, tile.EndMinutes)] = tileTextW;

                // In multi-semester mode multiple columns share time spans; the tallest
                // measured height governs gridline expansion for that slot.
                var key = (tile.StartMinutes, tile.EndMinutes);
                if (!tileHeightMap.TryGetValue(key, out var existing) || actualH > existing.actualHeight)
                    tileHeightMap[key] = (timeBasedH, actualH);
                allTiles.Add((d, tile, timeBasedH));
            }
        }

        // ── Phase 1.5: Compute per-overlap-slot natural widths ───────────────
        // Within each flat column, tiles are grouped into overlap clusters (sets of tiles
        // whose time ranges mutually overlap). Within each cluster, each overlap slot
        // (identified by OverlapIndex) is sized to the widest tile in that slot rather
        // than to the widest tile in the entire column.
        //
        // For each tile we precompute three values needed for rendering:
        //   tileSlotNaturalW — natural pixel width of this tile's own overlap slot
        //   tileSumNaturalW  — sum of all slot natural widths in the tile's cluster
        //   tilePredNaturalW — sum of natural widths for slots preceding this tile's slot
        //
        // "Natural width" for a slot = (max Bold text width across all tiles in that slot)
        // + the tile's overhead (TileChrome.TileOverhead: gap, borders, paddings, selection border, margin).
        var tileSlotNaturalW = new Dictionary<(int, int, int), double>();
        var tileSumNaturalW  = new Dictionary<(int, int, int), double>();
        var tilePredNaturalW = new Dictionary<(int, int, int), double>();

        for (int d = 0; d < dayCount; d++)
        {
            var colTiles = data.DayColumns[d].Tiles;
            if (colTiles.Count == 0) continue;

            // ComputeTiles emits tiles globally sorted by StartMinutes, so this sort is
            // defensive. We reconstruct clusters with the same interval-sweep used there.
            var sortedTiles   = colTiles.OrderBy(t => t.StartMinutes).ThenBy(t => t.EndMinutes).ToList();
            var clusters      = new List<List<GridTile>>();
            var clusterMaxEnd = new List<int>();

            foreach (var tile in sortedTiles)
            {
                int ci = -1;
                for (int c = 0; c < clusters.Count; c++)
                {
                    if (tile.StartMinutes < clusterMaxEnd[c]) { ci = c; break; }
                }
                if (ci == -1)
                {
                    clusters.Add([tile]);
                    clusterMaxEnd.Add(tile.EndMinutes);
                }
                else
                {
                    clusters[ci].Add(tile);
                    clusterMaxEnd[ci] = Math.Max(clusterMaxEnd[ci], tile.EndMinutes);
                }
            }

            foreach (var cluster in clusters)
            {
                // All tiles in a cluster share the same OverlapCount (fixed by ComputeTiles).
                int slotCount = cluster[0].OverlapCount;

                // Per-slot natural width = max text width among tiles at that slot + overhead.
                // Initialise each slot to its tile's overhead so it is never zero.
                var slotNaturalW = new double[slotCount];
                // A tile is drawn in a semester colour when several semesters are shown and it has a semester.
                double TileOverheadOf(GridTile t) => chrome.TileOverhead(
                    t.Entries.Any(e => e.IsOverlay),
                    data.IsMultiSemester && !string.IsNullOrEmpty(t.SemesterName));
                for (int i = 0; i < slotCount; i++) slotNaturalW[i] = TileOverheadOf(cluster[0]);

                foreach (var t in cluster)
                {
                    double textW = tileMaxTextWidths[(d, t.StartMinutes, t.EndMinutes)];
                    slotNaturalW[t.OverlapIndex] = Math.Max(slotNaturalW[t.OverlapIndex],
                                                            textW + TileOverheadOf(t));
                }

                // Prefix sums give each tile its left-offset within the cluster.
                var prefixW = new double[slotCount + 1];
                for (int i = 0; i < slotCount; i++) prefixW[i + 1] = prefixW[i] + slotNaturalW[i];

                double sumNatW = prefixW[slotCount];

                foreach (var t in cluster)
                {
                    var k = (d, t.StartMinutes, t.EndMinutes);
                    tileSlotNaturalW[k] = slotNaturalW[t.OverlapIndex];
                    tileSumNaturalW[k]  = sumNatW;
                    tilePredNaturalW[k] = prefixW[t.OverlapIndex];
                }
            }
        }

        // ── Phase 2: Compute content-driven column widths ────────────────────
        // Each flat column is sized independently to its own widest overlap cluster.
        // In multi-semester mode semester sub-columns within a day are no longer forced
        // to the same width — a semester with few (or short) sections gets a narrower
        // column than one with many. The day-header label spans the combined total and
        // centres automatically regardless of the individual sub-column widths.
        //
        // The minimum per sub-column is headerTextWidth / semCount, which guarantees the
        // day header text fits across the group even when all sub-columns are at their floor.
        var dayColWidths  = new double[dayCount];
        double headerFontSize = FontSizeFromResource("FontSizeXLarge");

        // Folded days (collapse days): read once per render. A folded day has no tiles, so Phase 1 and 1.5 above
        // measured nothing for it; it only needs its strip width below.
        FontWeight stripLetterWeight = Resource("FoldedDayLetterFontWeight", FontWeight.SemiBold);
        FontWeight stripCountWeight  = Resource("FoldedDayCountFontWeight", FontWeight.SemiBold);
        double stripPadding          = Layout("FoldedDayStripPadding");

        for (int ld = 0; ld < logicalDayCount; ld++)
        {
            // A folded day: the whole strip is as wide as its letter (header font) or its section count (tile text
            // font), whichever is wider, plus padding (FoldedDayStrip.StripWidth), shared evenly by the day's
            // semester sub-columns so the flat per-column arrays keep working.
            if (data.DayColumns[ld * semCount].IsFolded)
            {
                var folded    = data.DayColumns[ld * semCount];
                double letterW = MeasureTextWidth(drawnFont, FoldedDayStrip.Letter(folded.DayNumber), headerFontSize, stripLetterWeight);
                double countW  = MeasureTextWidth(drawnFont, (folded.Folded?.SectionCount ?? 0).ToString(), _vm!.TileFontSize, stripCountWeight);
                double stripW  = FoldedDayStrip.StripWidth(letterW, countW, stripPadding);
                for (int s = 0; s < semCount; s++)
                    dayColWidths[ld * semCount + s] = stripW / semCount;
                continue;
            }

            // Per-sub-column floor: day-header text width divided evenly so the label
            // always fits even if some or all semester sub-columns have no content.
            string dayName         = data.DayColumns[ld * semCount].Header;
            double headerTextWidth = MeasureTextWidth(drawnFont, dayName, headerFontSize, FontWeight.SemiBold)
                                     + DayHeaderSidePadding * 2;
            double minSubColWidth  = headerTextWidth / semCount;

            // Size each semester sub-column independently from its own content.
            // Cluster required width = sum of slot natural widths + the column's slot insets
            // (TileChrome.SlotInsets: the day-edge gap on outer edges, MultiSemPad at semester boundaries).
            // tileSumNaturalW is the same for all tiles in a given cluster, so the max across
            // all tiles in the column is the widest cluster's required width.
            for (int s = 0; s < semCount; s++)
            {
                int flatCol = ld * semCount + s;
                var dayCol  = data.DayColumns[flatCol];

                var (colLeft, colRight) = TileChrome.SlotInsets(s, semCount, TilePaddingH, MultiSemPad, DayEdgeGap);
                double colContentWidth = dayCol.Tiles.Count == 0 ? 0 :
                    dayCol.Tiles.Max(t =>
                        tileSumNaturalW[(flatCol, t.StartMinutes, t.EndMinutes)] + colLeft + colRight);

                dayColWidths[flatCol] = Math.Max(minSubColWidth, colContentWidth);
            }
        }

        // Canvas width is purely content-driven; it may be narrower than the panel,
        // which reduces horizontal scrolling on days that have little or no content.
        double totalWidth = TimeGutterWidth + dayColWidths.Sum();

        // Pre-calculate cumulative X-offsets for each flat column.
        var dayXOffsets = new double[dayCount + 1];
        dayXOffsets[0] = TimeGutterWidth;
        for (int d = 0; d < dayCount; d++)
            dayXOffsets[d + 1] = dayXOffsets[d] + dayColWidths[d];

        // ── Phase 3: Calculate cumulative gridline offsets ───────────────────
        // Delegates to the static method on ScheduleGridViewModel so the algorithm
        // can be unit-tested independently of the Avalonia canvas.
        var gridlineYOffsets = ScheduleGridViewModel.ComputeGridlineOffsets(
            tileHeightMap, data.FirstRowMinutes, data.LastRowMinutes);

        // ── Phase 4: Redraw with adjusted height accounting for expansions ───
        double totalHeight = effectiveHeaderHeight + TimeToY(data.LastRowMinutes, data.FirstRowMinutes)
                           + gridlineYOffsets[data.LastRowMinutes] + GridBottomPadding;

        _canvas.Width  = totalWidth;
        _canvas.Height = totalHeight;
        UpdateZoomContainerSize();

        // ── Canvas base background ────────────────────────────────────────
        // Fills the entire canvas with white before any other content is drawn.
        // Required for PNG export: RenderTargetBitmap renders only the canvas itself
        // (without the parent ScrollViewer's background), so any unpainted area would
        // appear transparent — and thus dark — in the exported file.
        AddRect(_canvas, 0, 0, totalWidth, totalHeight, ScheduleBackground, null);

        // ── Gutter background ──────────────────────────────────────────────
        AddRect(_canvas, 0, 0, TimeGutterWidth, totalHeight, GutterBg, null);

        // ── Day header row (day name + optional semester bars) ─────────────
        // Header background covers the full effective header height (day-name row plus
        // semester bar strip in multi-semester mode). The bottom border sits at the
        // boundary between the header area and the scrollable time body.
        AddRect(_canvas, TimeGutterWidth, 0, totalWidth - TimeGutterWidth, effectiveHeaderHeight,
            HeaderFill, HeaderBorder, borderThickness: new Thickness(0, 0, 0, 1));

        // Draw one day header per logical day, spanning all its semester sub-columns.
        // In multi-semester mode, also draw a thin colored bar below the day name for
        // each semester sub-column so users can quickly orient to the semester layout.
        for (int dayIdx = 0; dayIdx < logicalDayCount; dayIdx++)
        {
            int firstCol     = dayIdx * semCount;
            double dayGroupX = dayXOffsets[firstCol];
            double dayGroupW = dayXOffsets[firstCol + semCount] - dayGroupX;

            // Vertical separator between day groups
            if (dayIdx > 0)
                AddLine(_canvas, dayGroupX, 0, dayGroupX, totalHeight, HeaderBorder, 1);

            // Day name label centred over the full day group width.
            // Vertical centering is within the day-name row only (DayHeaderHeight),
            // not the full effectiveHeaderHeight, so the bar doesn't push the text up.
            // A folded day shows its short strip letter (M T W R F Sa Su) in the same header row instead.
            bool   dayFolded = data.DayColumns[firstCol].IsFolded;
            int    dayNumber = data.DayOfColumn(firstCol);
            string dayName   = data.DayColumns[firstCol].Header;
            var tb = new TextBlock
            {
                Text = dayFolded ? FoldedDayStrip.Letter(dayNumber) : dayName,
                FontWeight = dayFolded ? stripLetterWeight : FontWeight.SemiBold,
                FontSize = headerFontSize,
                Width = dayGroupW,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(tb, dayGroupX);
            Canvas.SetTop(tb, 1);
            _canvas.Children.Add(tb);

            // In multi-semester mode draw a colored semester indicator bar immediately
            // below the day-name row for each semester sub-column. The bar spans the
            // exact width of that sub-column's content area and always appears, even
            // when the sub-column has no sections scheduled for that day.
            // A folded day is one strip across all its semesters, so it gets no semester bars.
            if (data.IsMultiSemester && !dayFolded)
            {
                for (int s = 0; s < semCount; s++)
                {
                    int    flatCol   = firstCol + s;
                    string semName   = data.DayColumns[flatCol].SemesterName;
                    string semColor  = data.DayColumns[flatCol].SemesterColor;
                    IBrush barFill   = SemesterBrushResolver.Resolve(semName, semColor)
                                       ?? HeaderBorder;
                    var (barX, barW) = GetDayGroupContentBounds(flatCol, semCount, dayXOffsets, dayColWidths);
                    AddRect(_canvas, barX, DayHeaderHeight,
                            barW, SemesterBarHeight, barFill, null);
                }
            }

            // The header is the control (collapse days): a transparent hit area over the header row, drawn last so
            // it sits above the label and the bars. An open day folds on click (unless it is the last open day);
            // a folded day's strip opens on click; right-click opens the header menu on either.
            if (dayFolded)
            {
                AddDayHit(dayGroupX, 0, dayGroupW, effectiveHeaderHeight, Brushes.Transparent, dayNumber, dayName,
                          FoldedDayStrip.StripTooltip(dayName, data.DayColumns[firstCol].Folded), clickToggles: true, entryCursor);
            }
            else
            {
                bool canFold = _vm!.CanFold(dayNumber);
                var hit = AddDayHit(dayGroupX, 0, dayGroupW, effectiveHeaderHeight, Brushes.Transparent, dayNumber, dayName,
                                    FoldedDayStrip.HeaderTooltip(dayName, canFold), clickToggles: canFold, entryCursor);
                if (canFold && FoldDayGeometry is { } foldGeo)
                {
                    // Hover feedback: the fill and the fold icon at the right end (a separate, click-through
                    // element so it never steals the pointer from the hit area).
                    double iconSize = Layout("FoldDayIconSize");
                    var icon = new Avalonia.Controls.Shapes.Path
                    {
                        Data = foldGeo,
                        Stroke = Res("FoldDayIconBrush"),
                        StrokeThickness = Layout("FoldDayIconStrokeThickness"),
                        StrokeLineCap = PenLineCap.Round,
                        StrokeJoin = PenLineJoin.Round,
                        Width = iconSize,
                        Height = iconSize,
                        Stretch = Stretch.Uniform,
                        IsVisible = false,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(icon, dayGroupX + dayGroupW - iconSize - Layout("FoldDayIconRightMargin"));
                    Canvas.SetTop(icon, (DayHeaderHeight - iconSize) / 2);
                    _canvas.Children.Add(icon);

                    IBrush hoverFill = Res("GridHeaderHoverFill");
                    hit.PointerEntered += (_, _) => { hit.Background = hoverFill; icon.IsVisible = true; };
                    hit.PointerExited  += (_, _) => { hit.Background = Brushes.Transparent; icon.IsVisible = false; };
                }
            }
        }

        // Closing vertical line on the right edge of the last day column.
        AddLine(_canvas, totalWidth, 0, totalWidth, totalHeight, HeaderBorder, 1);

        // ── Time rows + horizontal rules (with adjusted Y-coordinates) ───────
        for (int mins = data.FirstRowMinutes; mins <= data.LastRowMinutes; mins += 30)
        {
            double y = effectiveHeaderHeight + TimeToY(mins, data.FirstRowMinutes) + gridlineYOffsets[mins];
            bool isHour = mins % 60 == 0;

            // Rule line
            var ruleBrush = isHour ? HourRuleBrush : RuleBrush;
            AddLine(_canvas, 0, y, totalWidth, y, ruleBrush, isHour ? 1 : 0.5);

            // Time label in gutter
            string label = $"{mins / 60:D2}{mins % 60:D2}";
            var timeTb = new TextBlock
            {
                Text = label,
                FontSize = isHour ? 11 : 9,
                Foreground = isHour ? TileText : HalfHourText,
                Width = TimeGutterWidth - 8,
                TextAlignment = TextAlignment.Left,
            };
            Canvas.SetLeft(timeTb, 4);
            Canvas.SetTop(timeTb, y - (isHour ? 7 : 6));
            _canvas.Children.Add(timeTb);
        }

        // ── Folded day strips (collapse days, Revision 1: a sheet folded almost shut) ────────────────
        // Drawn after the gridlines so a strip covers them: full grid height below the header. A light left face, a
        // shadowed right face (flat fills) and a crease down the middle. Where any section meets the day, a band of tile
        // blue on each face (R1-2); where a meeting, commitment, shared tile or overlay tile sits, a narrower bar on the
        // crease in that kind's own tile colour (R1-3). Bands and bars are placed with the SAME time-to-Y mapping as the
        // tiles (TimeToY plus the gridline expansion), so they line up with the open days' tiles at the same times.
        // The strip is one control across all the day's semester sub-columns; clicking it opens the day. The two day
        // separators are redrawn on top of its edges. Everything on top of the hit area is click-through.
        {
            IBrush faceLight   = Res("FoldedDayFaceLight");
            IBrush faceShadow  = Res("FoldedDayFaceShadow");
            IBrush creaseBrush = Res("FoldedDayCrease");
            IBrush bandLight   = Res("FoldedDayBandLight");
            IBrush bandShadow  = Res("FoldedDayBandShadow");
            IBrush stripText   = Res("FoldedDayStripText");
            double contentGap  = Layout("FoldedDayStripContentGap");
            double creaseW     = Layout("FoldedDayCreaseThickness");
            double edgeInset   = Layout("FoldedDayBandEdgeInset");
            double vInset      = Layout("FoldedDayBandVerticalInset");
            double laneW       = Layout("FoldedDayLaneWidth");
            double laneGap     = Layout("FoldedDayLaneGap");

            // Canvas Y of a time, exactly as the tiles compute it.
            double YOf(int minutes) => effectiveHeaderHeight + TimeToY(minutes, data.FirstRowMinutes)
                                     + GetGridlineOffset(gridlineYOffsets, minutes);

            // One click-through rectangle (the hit area underneath stays the only element that takes the pointer).
            void AddFlat(double x, double y, double w, double h, IBrush fill)
            {
                if (w <= 0 || h <= 0) return;
                var r = new Border { Width = w, Height = h, Background = fill, IsHitTestVisible = false };
                Canvas.SetLeft(r, x);
                Canvas.SetTop(r, y);
                _canvas.Children.Add(r);
            }

            // The brush the grid's own tiles use for each kind of other thing.
            IBrush OtherBrush(FoldedOtherKind kind) => kind switch
            {
                FoldedOtherKind.Meeting        => Res("FoldedDayMeetingBar"),
                FoldedOtherKind.SharedSchedule => Res("SharedScheduleBorder"),
                _                              => OverlayFrameBorder,   // commitments and overlay sections are drawn in the overlay style
            };

            for (int ld = 0; ld < logicalDayCount; ld++)
            {
                int firstCol = ld * semCount;
                var col = data.DayColumns[firstCol];
                if (!col.IsFolded) continue;

                double stripX = dayXOffsets[firstCol];
                double stripW = dayXOffsets[firstCol + semCount] - stripX;
                double bodyY  = effectiveHeaderHeight;
                double bodyH  = totalHeight - bodyY;
                double half   = stripW / 2;
                string stripDayName = col.Header;

                // The hit area carries the light face; the shadowed half goes on top, click-through.
                var stripHit = AddDayHit(stripX, bodyY, stripW, bodyH, faceLight, col.DayNumber, stripDayName,
                          FoldedDayStrip.StripTooltip(stripDayName, col.Folded), clickToggles: true, entryCursor);
                _stripVisuals[col.DayNumber] = new StripVisual(stripHit, stripDayName, col.Folded, stripX, stripW, bodyY, bodyH);
                AddFlat(stripX + half, bodyY, half, bodyH, faceShadow);

                // Section remnants: one band per merged time span, on each face, inset from the outer edge and
                // meeting at the crease; the vertical inset leaves a hairline between spans that only touch.
                var sectionSpans = FoldedDayStrip.MergeSpans(
                    (col.Folded?.SectionMeetings ?? []).Select(m => (m.StartMinutes, m.EndMinutes)));
                foreach (var (start, end) in sectionSpans)
                {
                    double top = YOf(start) + vInset;
                    double h   = YOf(end) - vInset - top;
                    AddFlat(stripX + edgeInset, top, half - edgeInset, h, bandLight);
                    AddFlat(stripX + half, top, half - edgeInset, h, bandShadow);
                }

                // The section count, centred under the letter (on top of the faces and bands).
                var countTb = new TextBlock
                {
                    Text = (col.Folded?.SectionCount ?? 0).ToString(),
                    FontSize = _vm!.TileFontSize,
                    FontWeight = stripCountWeight,
                    Foreground = stripText,
                    Width = stripW,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false,
                };
                double countY = bodyY + contentGap;
                Canvas.SetLeft(countTb, stripX);
                Canvas.SetTop(countTb, countY);
                _canvas.Children.Add(countTb);

                // The crease starts below the count so it never strikes through the number.
                double creaseTop = countY + entryLineH + contentGap;
                AddFlat(stripX + half - creaseW / 2, creaseTop, creaseW, totalHeight - creaseTop, creaseBrush);

                // Other things (Revision 3): side-by-side lanes centred on the crease, on top of the section bands. The
                // lane of each kind comes from FoldedDayStrip.OtherLanes; kinds sharing a lane are merged together.
                foreach (var laneGroup in (col.Folded?.OtherSpans ?? []).GroupBy(o => FoldedDayStrip.LaneOf(o.Kind)))
                {
                    var brush = OtherBrush(laneGroup.First().Kind);
                    double laneX = stripX + FoldedDayStrip.LaneLeft(laneGroup.Key, stripW, laneW, laneGap);
                    foreach (var (start, end) in FoldedDayStrip.MergeSpans(laneGroup.Select(o => (o.StartMinutes, o.EndMinutes))))
                    {
                        double top = YOf(start) + vInset;
                        AddFlat(laneX, top, laneW, YOf(end) - vInset - top, brush);
                    }
                }

                if (ld > 0) AddLine(_canvas, stripX, 0, stripX, totalHeight, HeaderBorder, 1);
                AddLine(_canvas, stripX + stripW, 0, stripX + stripW, totalHeight, HeaderBorder, 1);
            }
        }

        // ── Multi-semester column wash ────────────────────────────────────
        // Drawn after the gridlines so the wash sits above them in Z-order, making
        // the colored columns more prominent than the rules.
        //if (data.IsMultiSemester)
        //{
        //    for (int d = 0; d < dayCount; d++)
        //    {
        //        var col       = data.DayColumns[d];
        //        var baseBrush = SemesterBrushResolver.Resolve(col.SemesterName, col.SemesterColor);
        //        if (baseBrush is SolidColorBrush scb)
        //        {
        //            var washBrush = new SolidColorBrush(DesaturateColor(scb.Color, 0.25));
        //            var (washX, washW) = GetDayGroupContentBounds(d, semCount, dayXOffsets, dayColWidths);
        //            AddRect(_canvas, washX, effectiveHeaderHeight,
        //                    washW, totalHeight - effectiveHeaderHeight, washBrush, null);
        //        }
        //    }
        //}

        // ── Pre-compute program conflict entry lookup ────────────────────────
        // Build a set of (sectionId, dayNumber) pairs involved in program conflicts
        // so the tile loop can apply conflict borders to entry rows and draw connecting lines.
        IBrush ProgramConflictBrush = ScheduleGridView.ProgramConflictBrush;
        var conflictSectionDays = new HashSet<(string sectionId, int day)>();
        foreach (var pc in data.ProgramConflicts)
        {
            conflictSectionDays.Add((pc.MeetingA.SectionId, pc.MeetingA.Day));
            conflictSectionDays.Add((pc.MeetingB.SectionId, pc.MeetingB.Day));
        }
        // Entry row Borders involved in conflicts, keyed for the connecting-line pass.
        // Key: (sectionId, day, startMinutes, endMinutes) → tile position + entry index within the StackPanel.
        var conflictEntryRows = new Dictionary<(string, int, int, int), (int entryIndex, double tileX, double tileY, double tileW)>();

        // ── Section tiles (with adjusted heights and positions) ──────────────
        for (int d = 0; d < dayCount; d++)
        {
            double dayX = dayXOffsets[d];
            double dayColWidth = dayColWidths[d];

            foreach (var tile in data.DayColumns[d].Tiles)
            {
                // Each overlap slot is allocated width proportional to its natural content
                // width rather than an equal 1/N share. If the column is wider than the
                // cluster minimum (because another cluster in the same column — or the day
                // header — needs more space), all slot widths scale up proportionally so
                // the tiles collectively fill the full column width.
                var    tileKey   = (d, tile.StartMinutes, tile.EndMinutes);
                double natW      = tileSlotNaturalW[tileKey];
                double sumNatW   = tileSumNaturalW[tileKey];
                double predNatW  = tilePredNaturalW[tileKey];
                int    subIdx    = d % semCount;
                bool   isMultiSem = semCount > 1;
                var (leftInset, rightInset) = TileChrome.SlotInsets(subIdx, semCount, TilePaddingH, MultiSemPad, DayEdgeGap);
                double scale     = (dayColWidth - leftInset - rightInset) / sumNatW;
                double tileW     = natW * scale;
                double tileX     = dayX + leftInset + predNatW * scale;

                // Get adjusted Y position and height using adjusted gridline positions
                var (timeBasedH, actualH) = tileHeightMap[(tile.StartMinutes, tile.EndMinutes)];

                double startY = effectiveHeaderHeight + TimeToY(tile.StartMinutes, data.FirstRowMinutes)
                              + GetGridlineOffset(gridlineYOffsets, tile.StartMinutes);
                double endY = effectiveHeaderHeight + TimeToY(tile.EndMinutes, data.FirstRowMinutes)
                            + GetGridlineOffset(gridlineYOffsets, tile.EndMinutes);

                double adjustedTileY = startY + TilePaddingV;
                // Height is the distance between adjusted gridlines, minus padding; but at least the measured content height
                double gridlineSpanH = endY - startY - TilePaddingV * 2;
                double adjustedTileH = Math.Max(gridlineSpanH, actualH);

                // Rebuild the StackPanel (we need fresh instance with event handlers)
                var stack = new StackPanel { Spacing = 0 };

                for (int ei = 0; ei < tile.Entries.Count; ei++)
                {
                    var entry = tile.Entries[ei];
                    bool entrySelected = selectedIds.Contains(entry.SectionId);

                    if (ei > 0)
                        stack.Children.Add(new Border
                        {
                            Height = 1,
                            Background = TileInternalBorder,
                            Margin = new Thickness(0, 2, 0, 2),
                        });

                    var labelText = BuildTileLabel(entry.Label, entry.Initials, entry.FrequencyAnnotation);

                    var entryId = entry.SectionId;
                    // The real day comes from the column's DayNumber, never its position: the shown
                    // days can have gaps (e.g. Sunday on, Saturday off). Also keys the conflict lookups.
                    int clickDay = data.DayOfColumn(d);
                    var clickCtx = new TileClickContext(entryId, clickDay, tile.StartMinutes);
                    var entryLabel = new TextBlock
                    {
                        Text            = labelText,
                        FontSize        = _vm!.TileFontSize,
                        FontWeight      = entrySelected ? FontWeight.Bold
                                       : FontWeight.SemiBold,
                        Foreground      = entrySelected         ? SectionMeetingTextSelected
                                       : entry.IsSharedSchedule ? SharedScheduleText
                                       : entry.IsOverlay        ? OverlayFrameBorder
                                       : entry.IsDeemphasized   ? TileDeemphasizedText
                                       : TileText,
                        TextTrimming    = TextTrimming.CharacterEllipsis,
                        TextDecorations = entry.IsDeemphasized ? TextDecorations.Strikethrough : null,
                    };

                    // Colour by subject (Revision 1): a coloured line gets a small rounded square of the subject's
                    // colour before the text. The text stays one trimming block, so a narrow tile trims the initials
                    // first. A line with no colour is the text block alone, exactly as before the feature.
                    Control entryText = entryLabel;
                    if (SubjectMarkerBrush(entry.SubjectColor) is { } markerBrush)
                    {
                        var marker = NewSubjectMarker(markerBrush);
                        DockPanel.SetDock(marker, Dock.Left);
                        var markerLine = new DockPanel();
                        markerLine.Children.Add(marker);      // docked left
                        markerLine.Children.Add(entryLabel);  // fills the remaining width, trims with ellipsis
                        entryText = markerLine;
                    }
                    IBrush entryRowBg = entry.IsMeeting   ? MeetingTileFill
                                      : entry.IsEmphasized ? FilterEmphasizedBg
                                      : Brushes.Transparent;

                    // When the section carries an attention flag, draw a small colored flag icon
                    // at the trailing edge of the line (after the initials). A DockPanel lets the
                    // label keep its ellipsis trimming while the flag stays pinned and visible.
                    Control entryContent = entryText;
                    if (entry.Flag != SectionFlag.None
                        && FlagVisuals.ResolveBrush(entry.Flag) is { } flagBrush
                        && FlagGeometry is { } flagGeo)
                    {
                        var flagIcon = new Avalonia.Controls.Shapes.Path
                        {
                            Data              = flagGeo,
                            Fill              = flagBrush,
                            Width             = chrome.FlagIconSize,
                            Height            = chrome.FlagIconSize,
                            Stretch           = Stretch.Uniform,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin            = chrome.FlagIconMargin,
                        };
                        DockPanel.SetDock(flagIcon, Dock.Right);
                        var dock = new DockPanel();
                        dock.Children.Add(flagIcon);   // docked right (added first so it reserves space)
                        dock.Children.Add(entryText);  // fills remaining width, trims with ellipsis
                        entryContent = dock;
                    }

                    bool isConflict = conflictSectionDays.Contains((entry.SectionId, clickDay));
                    IBrush conflictGlow = isConflict
                        ? new RadialGradientBrush
                          {
                              Center  = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                              RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                              RadiusY = new RelativeScalar(0.9,  RelativeUnit.Relative),
                              GradientStops =
                              {
                                  new GradientStop(ProgramConflictGlowCenter, 0),
                                  new GradientStop(ProgramConflictGlowEdge, 1),
                              },
                          }
                        : Brushes.Transparent;
                    var entryRow = new Border
                    {
                        Background      = isConflict ? conflictGlow : entryRowBg,
                        BorderBrush     = entrySelected ? UserSelectedBorder : Brushes.Transparent,
                        BorderThickness = new Thickness(entrySelected ? chrome.SelectionBorder : 0),
                        CornerRadius    = new CornerRadius(2),
                        Padding         = chrome.EntryRowPadding,
                        // Commitment tiles are display-only — no hand cursor.
                        // Meetings are flagged IsCommitment to suppress the right-click menu
                        // but are still interactive, so they keep the hand cursor.
                        Cursor       = entry.IsCommitment && !entry.IsMeeting ? null : entryCursor,
                        Tag          = clickCtx,
                        Child        = entryContent,
                    };
                    // A line whose instructor initials were shortened to "+N" shows every instructor's full name
                    // on hover (spec 1.2.4 item 2.5). Lines that weren't shortened carry no names and get no tooltip,
                    // so most section lines pay nothing. Set directly on the row, never via a style class.
                    if (!string.IsNullOrEmpty(entry.InstructorNames))
                        ToolTip.SetTip(entryRow, BuildTileTooltipContent(new TileTooltip([], entry.InstructorNames)));

                    if (isConflict)
                        conflictEntryRows[(entry.SectionId, clickDay, tile.StartMinutes, tile.EndMinutes)] =
                            (ei, tileX, adjustedTileY, tileW - TilePaddingH);

                    // Hover tint: darken the individual section row on pointer-over,
                    // matching the card-hover effect in Section List view.
                    // Plain commitment tiles are display-only and don't receive hover feedback.
                    if (!entry.IsCommitment || entry.IsMeeting)
                    {
                        var baseBg = isConflict ? conflictGlow : entryRowBg;
                        entryRow.PointerEntered += (s, _) => ((Border)s!).Background = TileEntryHoverOverlay;
                        entryRow.PointerExited  += (s, _) => ((Border)s!).Background = baseBg;
                    }

                    // Register for lightweight selection repainting (avoids full Render() on selection change).
                    if (!entry.IsCommitment)
                        _entryRowRegistry.Add(new EntryRowInfo(entryRow, entryLabel, entryId, entry.IsOverlay, entry.IsDeemphasized, isConflict ? conflictGlow : entryRowBg));

                    entryRow.PointerPressed += (sender, e) =>
                    {
                        // Plain commitment tiles (instructor blocks) are display-only: they carry
                        // no entity ID, so clicks would fall through with bad data.  Meeting tiles
                        // also carry IsCommitment=true to suppress the section right-click menu,
                        // but they ARE clickable — the IsMeeting flag distinguishes them.
                        if (entry.IsCommitment && !entry.IsMeeting) { e.Handled = true; return; }

                        if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
                        {
                            // Right-click context menu is section-only; suppress for meetings.
                            if (!entry.IsMeeting && _vm is not null && _vm.IsWriteEnabled)
                            {
                                var ctx = (TileClickContext)((Border)sender!).Tag!;
                                _vm.PrepareContextMenu(ctx.SectionId, ctx.Day, ctx.StartMinutes);
                                _vm.ContextMenu.IsOpen = true;
                            }
                            e.Handled = true;
                            return;
                        }
                        if (e.ClickCount >= 2 && _vm?.IsWriteEnabled == true)
                        {
                            if (entry.IsMeeting)
                                _vm?.MeetingEditRequested?.Invoke(entryId);
                            else
                                _vm?.EditRequested?.Invoke(entryId);
                        }
                        else
                        {
                            if (!entry.IsMeeting)
                            {
                                bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                                         || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
                                if (ctrl)
                                    _vm?.ToggleSection(entryId);
                                else
                                    _vm?.SelectSection(entryId);
                            }
                        }
                        e.Handled = true;
                    };
                    stack.Children.Add(entryRow);
                }

                bool tileHasOverlay = tile.Entries.Any(e => e.IsOverlay);
                bool isMultiSemester = data.IsMultiSemester;

                // In multi-semester mode, resolve the semester color for this tile
                IBrush? semesterBrush = null;
                if (isMultiSemester && !string.IsNullOrEmpty(tile.SemesterName))
                {
                    semesterBrush = SemesterBrushResolver.Resolve(tile.SemesterName, tile.SemesterColor);
                }

                Border border;
                if (tileHasOverlay && isMultiSemester && semesterBrush is not null)
                {
                    // Dual-border approach for overlay tiles in multi-semester mode:
                    // Outer border (red) for overlay status, inner border (semester color) for semester identification
                    var innerBorder = new Border
                    {
                        Background      = TileFill,
                        BorderBrush     = semesterBrush,
                        BorderThickness = new Thickness(chrome.BorderThick),
                        CornerRadius    = new CornerRadius(3),
                        Padding         = chrome.ContentPadding,
                        Child           = stack,
                    };

                    border = new Border
                    {
                        Width           = tileW - TilePaddingH,
                        Height          = adjustedTileH,
                        Background      = TileFill,
                        BorderBrush     = OverlayFrameBorder,
                        BorderThickness = new Thickness(chrome.OverlayOuterBorder),
                        CornerRadius    = new CornerRadius(3),
                        BoxShadow       = new BoxShadows(new BoxShadow { Blur = 4, Spread = 0, Color = new Color(128, 0, 0, 0), OffsetX = 1, OffsetY = 2 }),
                        ClipToBounds    = false,
                        Child           = innerBorder,
                    };
                }
                else
                {
                    // Standard tile: use semester color in multi-semester mode, else gray border
                    border = new Border
                    {
                        Width           = tileW - TilePaddingH,
                        Height          = adjustedTileH,
                        Background      = TileFill,
                        BorderBrush     = tileHasOverlay ? OverlayFrameBorder
                                        : semesterBrush ?? TileExternalBorder,
                        BorderThickness = new Thickness(tileHasOverlay || semesterBrush is not null
                                                        ? chrome.BorderThick : chrome.BorderThin),
                        CornerRadius    = new CornerRadius(3),
                        BoxShadow       = new BoxShadows(new BoxShadow { Blur = 4, Spread = 0, Color = new Color(128, 0, 0, 0), OffsetX = 1, OffsetY = 2 }),
                        Padding         = chrome.ContentPadding,
                        ClipToBounds    = false,
                        Child           = stack,
                    };
                }


                // Meeting tiles get an attendee tooltip. Section tiles get no tooltip.
                var tileTooltip = ScheduleGridViewModel.BuildTileTooltip(tile);
                if (!string.IsNullOrEmpty(tileTooltip.AttendeeList))
                    ToolTip.SetTip(border, BuildTileTooltipContent(tileTooltip));

                Canvas.SetLeft(border, tileX);
                Canvas.SetTop(border, adjustedTileY);
                _canvas.Children.Add(border);

            }
        }

        // ── Program conflict connecting lines ────────────────────────────────
        // Conflict borders are applied directly on entry row Borders above. For cross-tile
        // conflicts, draw a connecting line between the two entry rows. Entry-row canvas
        // positions are estimated from the tile position + StackPanel offset.
        foreach (var pc in data.ProgramConflicts)
        {
            var keyA = (pc.MeetingA.SectionId, pc.MeetingA.Day, pc.MeetingA.StartMinutes, pc.MeetingA.EndMinutes);
            var keyB = (pc.MeetingB.SectionId, pc.MeetingB.Day, pc.MeetingB.StartMinutes, pc.MeetingB.EndMinutes);
            if (!conflictEntryRows.TryGetValue(keyA, out var infoA)) continue;
            if (!conflictEntryRows.TryGetValue(keyB, out var infoB)) continue;

            bool sameTile = pc.MeetingA.Day == pc.MeetingB.Day
                         && pc.MeetingA.StartMinutes == pc.MeetingB.StartMinutes
                         && pc.MeetingA.EndMinutes   == pc.MeetingB.EndMinutes;
            if (sameTile) continue;

            // Vertical center of an entry row within its tile: tile border (1) + padding (2) +
            // entryIndex * (entryLineH + separator) + half the entry height.
            double EntryCenterY((int entryIndex, double tileX, double tileY, double tileW) info) =>
                info.tileY + 1 + 2 + info.entryIndex * (entryLineH + EntrySeparatorH) + entryLineH / 2;

            double yA = EntryCenterY(infoA);
            double yB = EntryCenterY(infoB);

            var (left, right) = infoA.tileX < infoB.tileX ? (infoA, infoB) : (infoB, infoA);
            double leftY  = infoA.tileX < infoB.tileX ? yA : yB;
            double rightY = infoA.tileX < infoB.tileX ? yB : yA;
            AddLine(_canvas, left.tileX + left.tileW, leftY, right.tileX, rightY, ProgramConflictBrush, 2);
        }

        // Cache layout parameters for the ghost overlay (avoids full re-render when stepping).
        _cachedDayXOffsets = dayXOffsets;
        _cachedDayColWidths = dayColWidths;
        _cachedGridlineYOffsets = gridlineYOffsets;
        _cachedEffectiveHeaderHeight = effectiveHeaderHeight;
        _cachedFirstRowMinutes = data.FirstRowMinutes;
        _cachedSemesterCount = semCount;

        RenderGhostOverlay();
        RefreshStripMarks();

        }
        catch (Exception ex)
        {
            _canvas.Children.Clear();
            App.Logger.LogError(ex, "Schedule grid failed to render");
        }
    }

    /// <summary>
    /// Renders ghost tiles as a lightweight overlay on the canvas without re-running the
    /// grid pipeline or re-rendering existing tiles. Uses layout parameters cached from the
    /// most recent full <see cref="Render"/> call to position ghost tiles in the correct
    /// day columns and time slots. Called when the user steps through Room Availability
    /// Browser solutions.
    /// </summary>
    private void RenderGhostOverlay()
    {
        if (_canvas is null) return;

        // Remove previous ghost controls.
        foreach (var ctrl in _ghostControls)
            _canvas.Children.Remove(ctrl);
        _ghostControls.Clear();

        var ghosts = _vm?.GhostBlocks;
        if (ghosts is null || ghosts.Count == 0) return;
        if (_cachedDayXOffsets is null || _cachedDayColWidths is null || _cachedGridlineYOffsets is null) return;

        var data = _vm?.GridData ?? GridData.Empty;
        if (data.DayColumns.Count == 0) return;

        try
        {

        int semCount = _cachedSemesterCount;
        double TilePaddingV = ScheduleGridView.TilePaddingV;

        foreach (var ghost in ghosts)
        {
            // Map the ghost's day number to its flat column by DayNumber. Ghost blocks target the
            // first semester sub-column of that day; a day that isn't shown is skipped.
            if (data.FirstColumnOfDay(ghost.Day) is not int flatCol) continue;
            // A ghost on a folded day has no tile to sit in; it is a Proposal mark on the strip instead
            // (FoldedDayMarks.Build, drawn by RefreshStripMarks).
            if (data.DayColumns[flatCol].IsFolded) continue;

            // The ghost spans the same edges as the tiles of that day (first tile left edge to last tile right edge).
            var (ghostX, ghostW) = GetDayGroupContentBounds(flatCol, semCount, _cachedDayXOffsets, _cachedDayColWidths);

            double startY = _cachedEffectiveHeaderHeight
                          + TimeToY(ghost.StartMinutes, _cachedFirstRowMinutes)
                          + GetGridlineOffset(_cachedGridlineYOffsets, ghost.StartMinutes);
            double endY   = _cachedEffectiveHeaderHeight
                          + TimeToY(ghost.EndMinutes, _cachedFirstRowMinutes)
                          + GetGridlineOffset(_cachedGridlineYOffsets, ghost.EndMinutes);

            double tileY = startY + TilePaddingV;
            double tileH = Math.Max(endY - startY - TilePaddingV * 2, 18);
            double tileW = ghostW;

            // Tab: small opaque strip with room label, positioned above the outline
            var tabLabel = new TextBlock
            {
                Text              = ghost.RoomLabel,
                FontSize          = Math.Max(_vm!.TileFontSize - 1, 8),
                FontWeight        = FontWeight.SemiBold,
                Foreground        = GhostTabText,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var tab = new Border
            {
                Background          = GhostTabFill,
                CornerRadius        = new CornerRadius(3, 3, 0, 0),
                Padding             = new Thickness(5, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child               = tabLabel,
            };

            // Measure the tab so we know its height for positioning
            tab.Measure(new Size(tileW, double.PositiveInfinity));
            double tabH = tab.DesiredSize.Height;

            // Outer outline: transparent body with colored border
            var outline = new Border
            {
                Width           = tileW,
                Height          = tileH,
                Background      = GhostBodyFill,
                BorderBrush     = GhostOutlineBrush,
                BorderThickness = new Thickness(3),
                CornerRadius    = new CornerRadius(3),
                ClipToBounds    = true,
            };

            // Wrapper canvas: tab sits above the outline at negative Y
            var wrapper = new Canvas
            {
                Width  = tileW,
                Height = tileH,
                ClipToBounds = false,
            };
            Canvas.SetLeft(tab, 0);
            Canvas.SetTop(tab, -tabH);
            wrapper.Children.Add(tab);

            Canvas.SetLeft(outline, 0);
            Canvas.SetTop(outline, 0);
            wrapper.Children.Add(outline);

            ToolTip.SetTip(wrapper, $"{ghost.RoomLabel}  {ghost.TimeLabel}");

            Canvas.SetLeft(wrapper, ghostX);
            Canvas.SetTop(wrapper, tileY);
            _canvas.Children.Add(wrapper);
            _ghostControls.Add(wrapper);
        }

        }
        catch (Exception ex)
        {
            _ghostControls.Clear();
            App.Logger.LogError(ex, "Ghost overlay failed to render");
        }
    }

    /// <summary>
    /// Redraws the attention marks on every folded strip and flashes the strips that gained a NEW mark (collapse days,
    /// task 5). Cheap and self-contained: it only removes and re-adds the small bars of folded strips, so the light
    /// repaints (selection, ghosts) call it without a full <see cref="Render"/>; Render calls it at its end.
    /// The marks come from the pure <see cref="FoldedDayMarks"/> (the user's current pointers: selection, ghosts,
    /// conflict ends); <see cref="StripAttention"/> decides which strips flash. Each strip's tooltip is rebuilt to
    /// name its marks. Positions use the layout cached by the last Render, with the same time-to-Y mapping as tiles.
    /// </summary>
    private void RefreshStripMarks()
    {
        if (_canvas is null) return;

        foreach (var c in _markControls) _canvas.Children.Remove(c);
        _markControls.Clear();

        var data = _vm?.GridData ?? GridData.Empty;
        var inputs = new MarkInputs(_vm?.SelectedSectionIds ?? new HashSet<string>(),
                                    _vm?.GhostBlocks ?? [], data.ProgramConflicts);
        var marksByDay = new Dictionary<int, IReadOnlyList<AttentionMark>>();
        foreach (var (day, strip) in _stripVisuals)
            marksByDay[day] = FoldedDayMarks.Build(day, strip.Summary, inputs);

        // Always updated (also with no folded days, which makes it forget the days that opened).
        var flashDays = _stripAttention.Update(marksByDay);
        if (_stripVisuals.Count == 0 || _cachedGridlineYOffsets is null) return;

        try
        {
            double markHeight = Layout("FoldedDayMarkHeight");
            double edgeW      = Layout("FoldedDayMarkEdgeWidth");
            double lineT      = Layout("FoldedDayMarkLineThickness");
            double YOf(int minutes) => _cachedEffectiveHeaderHeight + TimeToY(minutes, _cachedFirstRowMinutes)
                                     + GetGridlineOffset(_cachedGridlineYOffsets, minutes);

            // The flash first, so it sits under the marks. Never while exporting (the export re-renders the canvas).
            if (!_useExportPalette)
                foreach (int day in flashDays)
                    FlashStrip(_stripVisuals[day]);

            foreach (var (day, strip) in _stripVisuals)
            {
                var marks = marksByDay[day];
                foreach (var mark in marks)
                {
                    // A bracket (Revision 3): a bar down BOTH outer edges for the span, and a thin line across the
                    // strip at its start and end, so the bands and lanes inside stay visible. Everything stays inside
                    // the strip's own bounds.
                    double top = YOf(mark.Start);
                    double height = Math.Max(YOf(mark.End) - top, markHeight);
                    var brush = Res(FoldedDayMarks.InfoFor(mark.Kind).BrushKey);
                    double side = Math.Min(edgeW, strip.Width / 2);
                    AddMarkPiece(strip.X, top, side, height, brush);
                    AddMarkPiece(strip.X + strip.Width - side, top, side, height, brush);
                    AddMarkPiece(strip.X, top, strip.Width, lineT, brush);
                    AddMarkPiece(strip.X, top + height - lineT, strip.Width, lineT, brush);
                }
                ToolTip.SetTip(strip.Hit, BuildTileTooltipContent(new TileTooltip(
                    FoldedDayStrip.StripTooltip(strip.DayName, strip.Summary, FoldedDayMarks.TooltipLines(marks)))));
            }
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Strip marks failed to render");
        }
    }

    /// <summary>
    /// Adds one click-through rectangle of an attention-mark bracket to the canvas and records it, so the next
    /// <see cref="RefreshStripMarks"/> can remove it.
    /// </summary>
    /// <param name="x">Left edge on the canvas.</param>
    /// <param name="y">Top edge on the canvas.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="fill">The mark kind's brush.</param>
    private void AddMarkPiece(double x, double y, double width, double height, IBrush fill)
    {
        var piece = new Border { Width = width, Height = height, Background = fill, IsHitTestVisible = false };
        Canvas.SetLeft(piece, x);
        Canvas.SetTop(piece, y);
        _canvas!.Children.Add(piece);
        _markControls.Add(piece);
    }

    /// <summary>
    /// Pulses a folded strip toward <c>FoldedDayFlashBrush</c> and back, a few times in about a second (all three
    /// numbers are resources). A translucent click-through overlay over the strip has its opacity animated 0 to 1 to 0
    /// per pulse with an Avalonia <see cref="Animation"/> (plain dispatcher-driven, so it also runs in WASM). It never
    /// takes input and never re-renders the grid; it removes itself when finished, and is cancelled by the next
    /// <see cref="Render"/>, which clears the canvas.
    /// </summary>
    /// <param name="strip">The strip to flash.</param>
    private async void FlashStrip(StripVisual strip)
    {
        if (_canvas is null) return;
        var canvas = _canvas;
        var token = _flashCts.Token;
        int pulses = Math.Max(1, (int)Layout("FoldedDayFlashCount"));
        double totalMs = Layout("FoldedDayFlashDurationMs");

        var overlay = new Border
        {
            Width = strip.Width, Height = strip.BodyHeight, Opacity = 0,
            Background = Res("FoldedDayFlashBrush"), IsHitTestVisible = false,
        };
        Canvas.SetLeft(overlay, strip.X);
        Canvas.SetTop(overlay, strip.BodyY);
        canvas.Children.Add(overlay);

        try
        {
            // One pulse = 0 -> 1 -> 0; the animation repeats it `pulses` times within the total duration.
            var pulse = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(totalMs / pulses),
                IterationCount = new IterationCount((ulong)pulses),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                },
            };
            await pulse.RunAsync(overlay, token);
        }
        catch (OperationCanceledException) { /* a Render replaced the canvas: nothing to clean up */ }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Strip flash failed");
        }
        finally
        {
            canvas.Children.Remove(overlay);
        }
    }

    /// <summary>
    /// Adds one clickable area of a day header or a folded strip to the canvas (collapse days). Like the tiles, it is a
    /// real element with pointer handlers, drawn by this view. A left click toggles the day when
    /// <paramref name="clickToggles"/> is set; a right click prepares and opens the day-header menu.
    /// </summary>
    /// <param name="x">Left edge on the canvas.</param>
    /// <param name="y">Top edge on the canvas.</param>
    /// <param name="width">Width of the area.</param>
    /// <param name="height">Height of the area.</param>
    /// <param name="background">Fill: transparent for a header (it must not be null, or the area would not be hit), the hatch for a strip body.</param>
    /// <param name="day">The REAL day number (1 = Monday ... 7 = Sunday), taken from the column's <c>DayNumber</c>.</param>
    /// <param name="dayName">The day's full name, for the menu text.</param>
    /// <param name="tooltipLines">The hover tooltip, one string per line.</param>
    /// <param name="clickToggles">True when a left click folds or opens the day; false for the last open day, which only offers the menu.</param>
    /// <param name="handCursor">The hand cursor, shown only when <paramref name="clickToggles"/> is true.</param>
    /// <returns>The area, so the caller can add hover feedback.</returns>
    private Border AddDayHit(double x, double y, double width, double height, IBrush background, int day, string dayName,
                             IReadOnlyList<string> tooltipLines, bool clickToggles, Cursor handCursor)
    {
        var hit = new Border
        {
            Width = width, Height = height, Background = background,
            Cursor = clickToggles ? handCursor : null,
        };
        ToolTip.SetTip(hit, BuildTileTooltipContent(new TileTooltip(tooltipLines)));
        hit.PointerPressed += (_, e) =>
        {
            if (_vm is null) return;
            // Handled first: a toggle redraws the whole canvas, including this very element.
            e.Handled = true;
            if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
            {
                if (_vm.DayHeaderMenu.Prepare(day, dayName)) _vm.DayHeaderMenu.IsOpen = true;
            }
            else if (clickToggles)
            {
                _vm.ToggleDay(day);
            }
        };
        Canvas.SetLeft(hit, x);
        Canvas.SetTop(hit, y);
        _canvas!.Children.Add(hit);
        return hit;
    }

    private void ShowEmpty()
    {
        if (_canvas is null) return;
        var tb = new TextBlock
        {
            Text = "No sections scheduled for this semester.",
            Foreground = Res("GridDayHeaderText"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Canvas.SetLeft(tb, 20);
        Canvas.SetTop(tb, 20);
        _canvas.Children.Add(tb);
    }

    /// <summary>
    /// Exports the full schedule grid canvas to a PNG file using the higher-contrast
    /// export palette defined in PrintColors.axaml. Prepends a title banner showing the
    /// semester name(s) and, when days are folded, which days are open (the strips export as drawn). A running strip
    /// flash is cancelled by the re-render, and none starts while exporting. Re-renders the canvas with export colors, captures the bitmap,
    /// then restores the on-screen palette.
    /// </summary>
    /// <param name="outputPath">Absolute path for the output PNG file.</param>
    public void ExportToPng(string outputPath)
    {
        _canvas ??= this.FindControl<Canvas>("GridCanvas");
        if (_canvas is null || double.IsNaN(_canvas.Width) || _canvas.Width <= 0) return;

        _printPalette ??= (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://TermPoint/PrintColors.axaml"));

        _useExportPalette = true;
        try
        {
            Render();

            // ── Title banner ────────────────────────────────────────────────
            // Unit, semester line, and (when days are folded) the open days: "Mon, Wed, Fri only".
            var title = ScheduleGridViewModel.ComposeExportTitle(
                _vm?.AcademicUnitName, _vm?.SemesterLine, _vm?.FoldedDaysCaption);
            double titleStripHeight = 0;

            if (!string.IsNullOrWhiteSpace(title))
            {
                const double titleFontSize = 16;
                const double titlePadding  = 10;
                titleStripHeight = titleFontSize + titlePadding * 2;

                // Shift every existing child down to make room for the title strip.
                foreach (Control child in _canvas.Children)
                    Canvas.SetTop(child, Canvas.GetTop(child) + titleStripHeight);

                _canvas.Height += titleStripHeight;

                // Title background (matches the export AppBackground).
                AddRect(_canvas, 0, 0, _canvas.Width, titleStripHeight, Res("AppBackground"), null);

                // Title text, centered horizontally.
                var titleBlock = new TextBlock
                {
                    Text = title,
                    FontSize = titleFontSize,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Res("TextPrimary"),
                };
                titleBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double titleX = (_canvas.Width - titleBlock.DesiredSize.Width) / 2;
                Canvas.SetLeft(titleBlock, Math.Max(0, titleX));
                Canvas.SetTop(titleBlock, titlePadding);
                _canvas.Children.Add(titleBlock);
            }

            // Force a layout pass so every child is measured and arranged
            // before the bitmap capture.
            _canvas.Measure(new Size(_canvas.Width, _canvas.Height));
            _canvas.Arrange(new Rect(0, 0, _canvas.Width, _canvas.Height));
            _canvas.UpdateLayout();

            var pixelSize = new PixelSize((int)_canvas.Width, (int)_canvas.Height);
            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(_canvas);
            // Explicit PNG options: Avalonia 12.1 obsoleted Save(string, int?), which now just
            // forwards to this overload with PngBitmapEncoderOptions.Default — same output.
            bitmap.Save(outputPath, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            _useExportPalette = false;
            Render();
        }
    }

    /// <summary>
    /// Converts a <see cref="TileTooltip"/> into a <see cref="ToolTip"/> instance suitable
    /// for <see cref="ToolTip.SetTip"/>. Passing a <see cref="ToolTip"/> directly (rather
    /// than arbitrary content) lets us own the background and padding — otherwise Avalonia
    /// wraps the content in its own default white-background ToolTip.
    /// </summary>
    private static ToolTip BuildTileTooltipContent(TileTooltip tooltip)
    {
        // Use a StackPanel whenever there are multiple lines or a full attendee list to show.
        bool hasAttendeeList = !string.IsNullOrEmpty(tooltip.AttendeeList);

        object content;
        if (tooltip.Lines.Count == 1 && !hasAttendeeList)
        {
            content = new TextBlock { Text = tooltip.Lines[0] };
        }
        else
        {
            var panel = new StackPanel { Spacing = 2 };
            foreach (var line in tooltip.Lines)
                panel.Children.Add(new TextBlock { Text = line });

            // Full attendee list: wrap at 300 px so it forms a readable box rather than a
            // single long line. Separated from the time range (when there is one) by a small top margin;
            // a names-only tooltip (shortened section line) has no lines above it, so no margin.
            if (hasAttendeeList)
                panel.Children.Add(new TextBlock
                {
                    Text        = tooltip.AttendeeList,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth    = 300,
                    Margin      = tooltip.Lines.Count > 0 ? new Thickness(0, 2, 0, 0) : default,
                });

            content = panel;
        }

        return new ToolTip
        {
            Background      = TileFill,
            BorderBrush     = ToolTipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(6, 4),
            Content         = content,
        };
    }

    private static double TimeToY(int minutes, int firstRowMinutes) =>
        (minutes - firstRowMinutes) / 30.0 * HalfHourHeight;

    /// <summary>
    /// Returns the horizontal content bounds (X, Width) for a flat sub-column: the span from the first tile's left
    /// edge to the last tile's right edge, used by the semester bars and ghost outlines so they line up with the
    /// tiles. Outer day-group edges are inset by half the <c>DayEdgeGap</c>; boundaries between semester
    /// sub-columns of one day use <c>MultiSemesterInternalTilePadding</c> (see <see cref="TileChrome.ContentInsets"/>).
    /// </summary>
    /// <param name="flatCol">Flat column index.</param>
    /// <param name="semCount">Semester sub-columns per day.</param>
    /// <param name="dayXOffsets">Cumulative X offset of each flat column.</param>
    /// <param name="dayColWidths">Width of each flat column.</param>
    /// <returns>The left X and the width of the content span.</returns>
    private static (double X, double Width) GetDayGroupContentBounds(
        int flatCol, int semCount, double[] dayXOffsets, double[] dayColWidths)
    {
        var (leftInset, rightInset) = TileChrome.ContentInsets(flatCol % semCount, semCount, MultiSemPad, DayEdgeGap);
        return (dayXOffsets[flatCol] + leftInset,
                dayColWidths[flatCol] - leftInset - rightInset);
    }

    /// <summary>
    /// Returns the cumulative expansion offset for <paramref name="minutes"/>.
    /// The dictionary is keyed at 30-minute intervals; if <paramref name="minutes"/>
    /// doesn't land on a 30-minute mark (e.g., a section with a non-standard duration
    /// imported from legacy data), this method linearly interpolates between the two
    /// surrounding gridline entries so rendering degrades gracefully instead of crashing.
    /// </summary>
    /// <param name="offsets">The gridlineYOffsets dictionary built during Phase 3.</param>
    /// <param name="minutes">The time value in minutes from midnight to look up.</param>
    /// <returns>Cumulative Y expansion offset in pixels.</returns>
    private static double GetGridlineOffset(Dictionary<int, double> offsets, int minutes)
    {
        if (offsets.TryGetValue(minutes, out var exact)) return exact;

        // Interpolate between the two nearest 30-minute gridlines.
        var lower = (minutes / 30) * 30;
        var upper = lower + 30;
        var lo    = offsets.TryGetValue(lower, out var loVal) ? loVal : 0;
        var hi    = offsets.TryGetValue(upper, out var hiVal) ? hiVal : lo;
        return lo + (hi - lo) * ((minutes - lower) / 30.0);
    }

    /// <summary>
    /// Measures the natural (unconstrained) pixel width of a text string
    /// rendered with the given font parameters.
    /// </summary>
    /// <param name="text">The string to measure.</param>
    /// <param name="fontSize">Font size in logical pixels.</param>
    /// <param name="weight">Font weight.</param>
    /// <returns>The desired width of the TextBlock in logical pixels.</returns>
    /// <summary>
    /// Assembles the single-line display text for a tile entry.
    /// Format: "MATH105 AB1  JS  (odd)" — label, then initials (if any), then frequency annotation (if any).
    /// </summary>
    /// <param name="label">Course+section code, e.g. "MATH105 AB1".</param>
    /// <param name="initials">Instructor initials, e.g. "JS". May be empty.</param>
    /// <param name="frequencyAnnotation">Parenthesised frequency, e.g. "(odd)". Empty for weekly meetings.</param>
    /// <returns>The assembled one-line string.</returns>
    private static string BuildTileLabel(string label, string initials, string frequencyAnnotation)
    {
        var text = string.IsNullOrEmpty(initials) ? label : $"{label}  {initials}";
        return string.IsNullOrEmpty(frequencyAnnotation) ? text : $"{text}  {frequencyAnnotation}";
    }

    /// <summary>
    /// The marker brush for a subject colour, taken from <see cref="_subjectMarkerBrushes"/> (created on first use).
    /// </summary>
    /// <param name="hex">The entry's <see cref="TileEntry.SubjectColor"/>: <c>#RRGGBB</c>, or empty for none.</param>
    /// <returns>The brush, or null when the line has no marker (empty or unparsable colour).</returns>
    private static IBrush? SubjectMarkerBrush(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if (_subjectMarkerBrushes.TryGetValue(hex, out var cached)) return cached;
        if (!Color.TryParse(hex, out var color)) return null;
        var brush = new SolidColorBrush(color).ToImmutable();
        _subjectMarkerBrushes[hex] = brush;
        return brush;
    }

    /// <summary>
    /// The side of a tile's subject marker in pixels: the tile font size times <c>SubjectMarkerSizeRatio</c>
    /// (App.axaml), so the marker is about a capital letter high and follows the font-size selector.
    /// </summary>
    private double SubjectMarkerSide => Math.Round(_vm!.TileFontSize * Layout("SubjectMarkerSizeRatio"));

    /// <summary>Pixels between a tile's subject marker and its text (<c>SubjectMarkerGap</c>, App.axaml).</summary>
    private static double SubjectMarkerGap => Layout("SubjectMarkerGap");

    /// <summary>
    /// Builds a tile line's subject marker: a rounded square of the subject colour with the thin
    /// <c>SubjectMarkerOutline</c>, vertically centred on the line, followed by <see cref="SubjectMarkerGap"/>.
    /// Size, corner radius, outline thickness and gap all come from App.axaml / AppColors.axaml.
    /// </summary>
    /// <param name="fill">The subject colour brush, from <see cref="SubjectMarkerBrush"/>.</param>
    /// <returns>A new, unparented marker.</returns>
    private Border NewSubjectMarker(IBrush fill)
    {
        var side = SubjectMarkerSide;
        return new Border
        {
            Width             = side,
            Height            = side,
            Background        = fill,
            BorderBrush       = Res("SubjectMarkerOutline"),
            BorderThickness   = Resource("SubjectMarkerBorderThickness", new Thickness(1)),
            CornerRadius      = Resource("SubjectMarkerCornerRadius", new CornerRadius(2)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, SubjectMarkerGap, 0),
        };
    }

    /// <summary>
    /// Reads a typed value (a <see cref="Thickness"/>, <see cref="CornerRadius"/> and so on) from the application
    /// resources.
    /// </summary>
    /// <typeparam name="T">The resource's type.</typeparam>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">Returned when the key is missing or holds another type.</param>
    /// <returns>The resource value, or <paramref name="fallback"/>.</returns>
    private static T Resource<T>(string key, T fallback) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is T t ? t : fallback;

    /// <summary>
    /// Measures the width of a single line of text, in the font the tiles are drawn in. The text block is not in the
    /// visual tree, so it must be given the font explicitly (see <see cref="DrawnFontFamily"/>); left alone it would
    /// use the platform default font and under-measure by about 5 percent.
    /// </summary>
    /// <param name="family">The font family the text is drawn in.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="weight">Font weight.</param>
    /// <returns>The text's desired width in pixels.</returns>
    private static double MeasureTextWidth(FontFamily family, string text, double fontSize, FontWeight weight)
    {
        var tb = new TextBlock { Text = text, FontFamily = family, FontSize = fontSize, FontWeight = weight };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Width;
    }

    /// <summary>
    /// Measures the height of a single-line <see cref="TextBlock"/> at the given font size
    /// and weight. Sample text is intentionally non-empty so the measurement reflects an
    /// actual rendered line box (an empty TextBlock measures to zero on some platforms).
    /// Used by Phase 1 to compute tile heights without building a full visual tree per tile.
    /// </summary>
    /// <param name="family">The font family the text is drawn in (see <see cref="DrawnFontFamily"/>).</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="weight">Font weight.</param>
    /// <returns>The line's desired height in pixels.</returns>
    private static double MeasureLineHeight(FontFamily family, double fontSize, FontWeight weight)
    {
        var tb = new TextBlock { Text = "Mg", FontFamily = family, FontSize = fontSize, FontWeight = weight };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Height;
    }


    private static void AddRect(Canvas canvas, double x, double y,
        double w, double h, IBrush fill, IBrush? stroke,
        Thickness? borderThickness = null)
    {
        var border = new Border
        {
            Width = w, Height = h,
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = borderThickness ?? (stroke is not null ? new Thickness(1) : new Thickness(0)),
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        canvas.Children.Add(border);
    }

    /// <summary>
    /// Returns a copy of <paramref name="color"/> with its HSL saturation reduced by
    /// <paramref name="amount"/> (0–1). Alpha and lightness are preserved.
    /// </summary>
    private static Color DesaturateColor(Color color, double amount)
    {
        // Convert RGB → HSL
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l   = (max + min) / 2.0;
        double s   = (max == min) ? 0.0
                   : l < 0.5 ? (max - min) / (max + min)
                              : (max - min) / (2.0 - max - min);
        double h   = 0.0;
        if (max != min)
        {
            double delta = max - min;
            if      (max == r) h = ((g - b) / delta + (g < b ? 6 : 0)) / 6.0;
            else if (max == g) h = ((b - r) / delta + 2) / 6.0;
            else               h = ((r - g) / delta + 4) / 6.0;
        }

        s = Math.Max(0, s - amount);

        // Convert HSL → RGB
        static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        double nr, ng, nb;
        if (s == 0)
        {
            nr = ng = nb = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            nr = Hue2Rgb(p, q, h + 1.0 / 3);
            ng = Hue2Rgb(p, q, h);
            nb = Hue2Rgb(p, q, h - 1.0 / 3);
        }

        return new Color(color.A,
            (byte)Math.Round(nr * 255),
            (byte)Math.Round(ng * 255),
            (byte)Math.Round(nb * 255));
    }

    private static void AddLine(Canvas canvas, double x1, double y1,
        double x2, double y2, IBrush brush, double thickness)
    {
        var line = new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint   = new Point(x2, y2),
            Stroke     = brush,
            StrokeThickness = thickness,
        };
        canvas.Children.Add(line);
    }

}

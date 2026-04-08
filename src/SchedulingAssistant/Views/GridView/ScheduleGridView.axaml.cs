using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SchedulingAssistant.ViewModels.GridView;
using System.ComponentModel;

namespace SchedulingAssistant.Views.GridView;

public partial class ScheduleGridView : UserControl
{
    private record TileClickContext(string SectionId, int Day, int StartMinutes);

    /// <summary>
    /// Snapshot of every entry row rendered during the last full <see cref="Render"/> call.
    /// Used by <see cref="UpdateSelectionHighlight"/> to repaint selection state without
    /// triggering a full layout pass.
    /// </summary>
    private record EntryRowInfo(Border Row, TextBlock Label, string SectionId, bool IsOverlay, bool IsDeemphasized);
    private readonly List<EntryRowInfo> _entryRowRegistry = new();
    
    // Layout constants
    private const double TimeGutterWidth  = 52;
    private const double DayHeaderHeight  = 28;   // height of the day-name row
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
    /// Total horizontal pixels consumed by the tile render layers between the day-column
    /// edge and the TextBlock that displays the label. Breakdown:
    ///   2 × TilePaddingH (slot-allocation gap)         =  6 px
    ///   2 × tile BorderThickness (max 3 px, multi-sem) =  6 px
    ///   2 × tile Padding left/right (3 px each)        =  6 px
    ///   2 × entryRow Padding left/right (1 px each)    =  2 px
    ///   Safety margin for off-tree font measurement     =  8 px
    ///                                              Total = 28 px
    /// Used in Phase 2 to turn a raw Bold text width into a required column width.
    /// </summary>
    private const double TileTextOverhead = 28;

    
    // Resources resolved from App.axaml at first render (after resources are loaded).
    private static IBrush Res(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : Brushes.Transparent;
    private static double FontSizeFromResource(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is double d
            ? d : 12;
    private static double Layout(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is double d
            ? d : 0;

    private static double TilePaddingV => Layout("TilePaddingVertical");
    private static double TilePaddingH => Layout("TilePaddingHorizontal");

    /// <summary>Border thickness applied to the entry row of a user-selected section tile.</summary>
    private const double TileSelectionBorderThickness = 3;

    private static IBrush TileFill              => Res("TileFill");
    private static IBrush TileBorder            => Res("TileBorder");
    private static IBrush TileBorderSelected    => Res("TileBorderSelected");
    private static IBrush UserSelectedBorder    => Res("UserSelectedSectionBorderColor");
    private static IBrush OverlayFrameBorder    => Res("OverlayFrameBorder");
    private static IBrush TileDeemphasizedText  => Res("TileDeemphasizedText");
    private static IBrush RuleBrush          => Res("GridRuleLine");
    private static IBrush HourRuleBrush      => Res("GridHourRuleLine");
    private static IBrush HeaderFill         => Res("ChromeBackground");
    private static IBrush HeaderBorder       => Res("DaySeparators");
    private static IBrush GutterBg           => Res("GridGutterBackground");


    // ── Apply-save flash animation ─────────────────────────────────────────────
    // When the user clicks Apply on an edit, the saved section's tile(s) briefly
    // pulse to draw the eye to their (possibly new) position on the grid.
    // The background of each matching TextBlock fades from TileFill to a tint
    // (FlashMaxIntensity of the way toward the selection-framing color) then back.

    /// <summary>Duration of the fade-in phase of the flash animation.</summary>
    private static readonly TimeSpan FlashFadeIn      = TimeSpan.FromSeconds(3);

    /// <summary>Duration of the fade-out phase of the flash animation.</summary>
    private static readonly TimeSpan FlashFadeOut     = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Fraction (0–1) of the way from <c>TileFill</c> toward the selection-framing
    /// color at the peak of the flash. 0.5 produces a midpoint tint.
    /// </summary>
    private const double FlashMaxIntensity = 0.5;

    private DispatcherTimer? _flashTimer;
    private DateTime         _flashStartTime;
    private List<Border>?    _flashEntryRows;
    // ──────────────────────────────────────────────────────────────────────────

    private Canvas? _canvas;
    private ScheduleGridViewModel? _vm;
    private Border? _zoomContainer;
    private Popup? _tileContextPopup;
    private double _zoomLevel = 1.0;

    public ScheduleGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => Render();

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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleGridViewModel.GridData))
            Render();
        else if (e.PropertyName == nameof(ScheduleGridViewModel.SelectedSectionId))
            UpdateSelectionHighlight();
        else if (e.PropertyName == nameof(ScheduleGridViewModel.FlashSectionId))
        {
            // Defer to after all pending layout work (DispatcherPriority.Background is lower
            // than Layout). When the section editor collapses on save, the grid panel's width
            // changes, triggering SizeChanged → Render(), which rebuilds the canvas with fresh
            // Borders. If we start the animation synchronously here, our tile Border references
            // are orphaned by that second Render(). Posting at Background priority ensures we
            // run after layout, so _entryRowRegistry contains the final, canvas-attached Borders.
            var flashId = _vm?.FlashSectionId;
            Dispatcher.UIThread.Post(
                () => OnFlashSectionIdChanged(flashId),
                DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Repaints entry-row selection styling without a full layout pass. Called when only
    /// <see cref="ScheduleGridViewModel.SelectedSectionId"/> changes — the tile geometry
    /// is unchanged, so only Background, FontWeight, and Foreground need updating.
    /// </summary>
    private void UpdateSelectionHighlight()
    {
        var selectedId = _vm?.SelectedSectionId;

        foreach (var info in _entryRowRegistry)
        {
            bool isSelected = selectedId is not null && info.SectionId == selectedId;

            info.Row.Background      = Brushes.Transparent;
            info.Row.BorderBrush     = isSelected ? UserSelectedBorder : Brushes.Transparent;
            info.Row.BorderThickness = new Thickness(isSelected ? TileSelectionBorderThickness : 0);
            info.Label.FontWeight    = isSelected ? FontWeight.Bold : FontWeight.SemiBold;
            info.Label.Foreground = isSelected       ? TileBorderSelected
                                  : info.IsOverlay    ? OverlayFrameBorder
                                  : info.IsDeemphasized ? TileDeemphasizedText
                                  : Brushes.Black;
        }
    }

    /// <summary>
    /// Starts or cancels the apply-save flash animation on the tile Borders of every
    /// grid card that contains an entry for the saved section.
    /// <para>
    /// A null <paramref name="sectionId"/> cancels any running animation. A non-null ID
    /// starts a new one: the tile Border's background fades from <c>TileFill</c> to a
    /// tint at <see cref="FlashMaxIntensity"/> of the way toward
    /// <c>UserSelectedSectionBorderColor</c> (over <see cref="FlashFadeIn"/>), then fades
    /// back (over <see cref="FlashFadeOut"/>).
    /// </para>
    /// <para>
    /// This method is invoked via <see cref="Dispatcher.Post"/> at
    /// <see cref="DispatcherPriority.Background"/> so it runs <em>after</em> any pending
    /// layout pass. When the section editor collapses on save, the grid panel's width
    /// changes and <c>SizeChanged → Render()</c> rebuilds the canvas with fresh Borders.
    /// Deferring guarantees we read <see cref="_entryRowRegistry"/> in its final state and
    /// obtain tile Borders that are still attached to the live visual tree.
    /// </para>
    /// </summary>
    /// <param name="sectionId">The section whose tiles should flash, or null to cancel.</param>
    private void OnFlashSectionIdChanged(string? sectionId)
    {
        // Stop any previous animation and restore entry row backgrounds to transparent.
        // DispatcherTimer.Stop() is synchronous on the UI thread, so there is no race
        // between the Tick handler and this cleanup.
        _flashTimer?.Stop();
        _flashTimer = null;
        if (_flashEntryRows is not null)
        {
            foreach (var row in _flashEntryRows)
                row.Background = Brushes.Transparent;
            _flashEntryRows = null;
        }

        if (sectionId is null) return;

        // Target the entry-row Borders for the saved section — one per meeting day.
        // These are the small inner Borders inside the tile (the ones that carry the
        // purple selection border). Animating their Background pulses only the saved
        // section's row, leaving co-scheduled sections in the same tile unaffected.
        var entryRows = _entryRowRegistry
            .Where(e => e.SectionId == sectionId)
            .Select(e => e.Row)
            .ToList();

        if (entryRows.Count == 0) return;

        var tileFillColor = ((SolidColorBrush)TileFill).Color;
        var framingColor  = ((SolidColorBrush)UserSelectedBorder).Color;
        var peakColor     = LerpColor(tileFillColor, framingColor, FlashMaxIntensity);

        _flashEntryRows = entryRows;
        _flashStartTime = DateTime.UtcNow;

        // Use a DispatcherTimer that directly reassigns the entry row's Background on
        // every tick. Reassigning a fresh SolidColorBrush instance unconditionally
        // triggers the property-changed → InvalidateVisual chain at ~60 fps.
        _flashTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _flashTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _flashStartTime;
            var total   = FlashFadeIn + FlashFadeOut;

            if (elapsed >= total)
            {
                // Animation complete — restore entry row backgrounds to transparent.
                _flashTimer!.Stop();
                _flashTimer = null;
                foreach (var row in _flashEntryRows!)
                    row.Background = Brushes.Transparent;
                _flashEntryRows = null;
                return;
            }

            // Compute blend factor t ∈ [0, 1]:
            //   Fade-in  (elapsed ≤ FlashFadeIn):  t rises from 0 to 1.
            //   Fade-out (elapsed > FlashFadeIn):  t falls from 1 to 0.
            double t = elapsed <= FlashFadeIn
                ? elapsed.TotalSeconds / FlashFadeIn.TotalSeconds
                : 1.0 - (elapsed - FlashFadeIn).TotalSeconds / FlashFadeOut.TotalSeconds;

            var color = LerpColor(tileFillColor, peakColor, t);
            var brush = new SolidColorBrush(color);
            foreach (var row in _flashEntryRows!)
                row.Background = brush;
        };

        _flashTimer.Start();
    }

    /// <summary>
    /// Linearly interpolates between two <see cref="Color"/> values.
    /// </summary>
    /// <param name="a">The color at t = 0.</param>
    /// <param name="b">The color at t = 1.</param>
    /// <param name="t">Blend factor in [0, 1].</param>
    /// <returns>The interpolated color.</returns>
    private static Color LerpColor(Color a, Color b, double t)
        => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    private void Render()
    {
        _canvas ??= this.FindControl<Canvas>("GridCanvas");
        if (_canvas is null) return;

        _canvas.Children.Clear();
        _entryRowRegistry.Clear();

        var data = _vm?.GridData ?? GridData.Empty;
        if (data.DayColumns.Count == 0) { ShowEmpty(); return; }

        int dayCount        = data.DayColumns.Count;
        int semCount        = data.SemesterCount;
        int logicalDayCount = dayCount / semCount;

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
        var selectedId        = _vm?.SelectedSectionId;
        var entryCursor       = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        // Collect all tiles; used later for gridline expansion calculation.
        var allTiles = new List<(int day, GridTile tile, double timeBasedHeight)>();

        for (int d = 0; d < dayCount; d++)
        {
            foreach (var tile in data.DayColumns[d].Tiles)
            {
                // Time-proportional height before any content expansion
                double timeBasedH = TimeToY(tile.EndMinutes, data.FirstRowMinutes)
                                  - TimeToY(tile.StartMinutes, data.FirstRowMinutes)
                                  - TilePaddingV * 2;
                timeBasedH = Math.Max(timeBasedH, 18);

                // Build a StackPanel matching the render layout to measure natural height.
                var stack = new StackPanel { Spacing = 0 };
                for (int ei = 0; ei < tile.Entries.Count; ei++)
                {
                    var entry = tile.Entries[ei];
                    bool entrySelected = selectedId is not null && entry.SectionId == selectedId;

                    if (ei > 0)
                        stack.Children.Add(new Border
                        {
                            Height     = 1,
                            Background = TileBorder,
                            Margin     = new Thickness(0, 2, 0, 2),
                        });

                    var entryRow = new Border
                    {
                        Background      = Brushes.Transparent,
                        BorderBrush     = entrySelected ? UserSelectedBorder : Brushes.Transparent,
                        BorderThickness = new Thickness(entrySelected ? TileSelectionBorderThickness : 0),
                        CornerRadius    = new CornerRadius(2),
                        Padding         = new Thickness(1, 0),
                        Child           = new TextBlock
                        {
                            Text            = BuildTileLabel(entry.Label, entry.Initials, entry.FrequencyAnnotation),
                            FontSize        = 11,
                            FontWeight      = entrySelected ? FontWeight.Bold : FontWeight.SemiBold,
                            Foreground      = entrySelected        ? TileBorderSelected
                                           : entry.IsDeemphasized  ? TileDeemphasizedText
                                           : Brushes.Black,
                            TextTrimming    = TextTrimming.CharacterEllipsis,
                            TextDecorations = entry.IsDeemphasized ? TextDecorations.Strikethrough : null,
                        },
                    };
                    stack.Children.Add(entryRow);
                }

                // Measure StackPanel for HEIGHT only — drives gridline expansion logic.
                stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double actualH = stack.DesiredSize.Height;

                // Measure the widest entry label in this tile using Bold (the heavier weight
                // applied when selected), guaranteeing the column fits in both render states.
                // MeasureTextWidth avoids subtle width under-reporting from off-tree controls.
                double tileTextW = 0;
                foreach (var e in tile.Entries)
                {
                    string lbl = BuildTileLabel(e.Label, e.Initials, e.FrequencyAnnotation);
                    tileTextW = Math.Max(tileTextW, MeasureTextWidth(lbl, 11, FontWeight.Bold));
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
        // + TileTextOverhead (which accounts for tile padding, border, and safety margin).
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
                // Initialise each slot to TileTextOverhead so it is never zero.
                var slotNaturalW = new double[slotCount];
                for (int i = 0; i < slotCount; i++) slotNaturalW[i] = TileTextOverhead;

                foreach (var t in cluster)
                {
                    double textW = tileMaxTextWidths[(d, t.StartMinutes, t.EndMinutes)];
                    slotNaturalW[t.OverlapIndex] = Math.Max(slotNaturalW[t.OverlapIndex],
                                                            textW + TileTextOverhead);
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

        for (int ld = 0; ld < logicalDayCount; ld++)
        {
            // Per-sub-column floor: day-header text width divided evenly so the label
            // always fits even if some or all semester sub-columns have no content.
            string dayName         = data.DayColumns[ld * semCount].Header;
            double headerTextWidth = MeasureTextWidth(dayName, headerFontSize, FontWeight.SemiBold)
                                     + DayHeaderSidePadding * 2;
            double minSubColWidth  = headerTextWidth / semCount;

            // Size each semester sub-column independently from its own content.
            // Cluster required width = sum of slot natural widths + 2 × TilePaddingH (left + right gaps).
            // tileSumNaturalW is the same for all tiles in a given cluster, so the max across
            // all tiles in the column is the widest cluster's required width.
            for (int s = 0; s < semCount; s++)
            {
                int flatCol = ld * semCount + s;
                var dayCol  = data.DayColumns[flatCol];

                double colContentWidth = dayCol.Tiles.Count == 0 ? 0 :
                    dayCol.Tiles.Max(t =>
                        tileSumNaturalW[(flatCol, t.StartMinutes, t.EndMinutes)] + 2 * TilePaddingH);

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
        AddRect(_canvas, 0, 0, totalWidth, totalHeight, Brushes.White, null);

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
            var tb = new TextBlock
            {
                Text = data.DayColumns[firstCol].Header,
                FontWeight = FontWeight.SemiBold,
                FontSize = FontSizeFromResource("FontSizeXLarge"),
                Width = dayGroupW,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(tb, dayGroupX);
            Canvas.SetTop(tb, (DayHeaderHeight - 14) / 2);
            _canvas.Children.Add(tb);

            // In multi-semester mode draw a colored semester indicator bar immediately
            // below the day-name row for each semester sub-column. The bar spans the
            // exact width of that sub-column's content area and always appears, even
            // when the sub-column has no sections scheduled for that day.
            if (data.IsMultiSemester)
            {
                for (int s = 0; s < semCount; s++)
                {
                    int    flatCol   = firstCol + s;
                    string semName   = data.DayColumns[flatCol].SemesterName;
                    string semColor  = data.DayColumns[flatCol].SemesterColor;
                    IBrush barFill   = ScheduleGridViewModel.ResolveSemesterBorderBrush(semName, semColor)
                                       ?? HeaderBorder;
                    AddRect(_canvas, dayXOffsets[flatCol], DayHeaderHeight,
                            dayColWidths[flatCol], SemesterBarHeight, barFill, null);
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
                Foreground = isHour ? Brushes.Black : Res("GridTimeHalfHourText"),
                Width = TimeGutterWidth - 4,
                TextAlignment = TextAlignment.Right,
            };
            Canvas.SetLeft(timeTb, 0);
            Canvas.SetTop(timeTb, y - (isHour ? 7 : 6));
            _canvas.Children.Add(timeTb);
        }

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
                double scale     = (dayColWidth - TilePaddingH) / sumNatW;
                double tileW     = natW * scale;
                double tileX     = dayX + TilePaddingH + predNatW * scale;

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
                    bool entrySelected = selectedId is not null && entry.SectionId == selectedId;

                    if (ei > 0)
                        stack.Children.Add(new Border
                        {
                            Height = 1,
                            Background = TileBorder,
                            Margin = new Thickness(0, 2, 0, 2),
                        });

                    var labelText = BuildTileLabel(entry.Label, entry.Initials, entry.FrequencyAnnotation);

                    var entryId = entry.SectionId;
                    // In multi-semester mode columns are interleaved: [Mon/Sem1, Mon/Sem2, Tue/Sem1, ...].
                    // The day number is the day-group index + 1, not the raw column index + 1.
                    int clickDay = (d / semCount) + 1;
                    var clickCtx = new TileClickContext(entryId, clickDay, tile.StartMinutes);
                    var entryLabel = new TextBlock
                    {
                        Text            = labelText,
                        FontSize        = 11,
                        FontWeight      = entrySelected ? FontWeight.Bold : FontWeight.SemiBold,
                        Foreground      = entrySelected        ? TileBorderSelected
                                       : entry.IsOverlay       ? OverlayFrameBorder
                                       : entry.IsDeemphasized  ? TileDeemphasizedText
                                       : Brushes.Black,
                        TextTrimming    = TextTrimming.CharacterEllipsis,
                        TextDecorations = entry.IsDeemphasized ? TextDecorations.Strikethrough : null,
                    };
                    var entryRow = new Border
                    {
                        Background      = Brushes.Transparent,
                        BorderBrush     = entrySelected ? UserSelectedBorder : Brushes.Transparent,
                        BorderThickness = new Thickness(entrySelected ? TileSelectionBorderThickness : 0),
                        CornerRadius    = new CornerRadius(2),
                        Padding         = new Thickness(1, 0),
                        // Commitment tiles are display-only — no hand cursor.
                        // Meetings are flagged IsCommitment to suppress the right-click menu
                        // but are still interactive, so they keep the hand cursor.
                        Cursor       = entry.IsCommitment && !entry.IsMeeting ? null : entryCursor,
                        Tag          = clickCtx,
                        Child        = entryLabel,
                    };

                    // Register for lightweight selection repainting (avoids full Render() on selection change).
                    if (!entry.IsCommitment)
                        _entryRowRegistry.Add(new EntryRowInfo(entryRow, entryLabel, entryId, entry.IsOverlay, entry.IsDeemphasized));

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
                        if (e.ClickCount >= 2)
                        {
                            if (entry.IsMeeting)
                                _vm?.MeetingEditRequested?.Invoke(entryId);
                            else
                                _vm?.EditRequested?.Invoke(entryId);
                        }
                        else
                        {
                            if (!entry.IsMeeting)
                                _vm?.SelectSection(entryId);
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
                    semesterBrush = ScheduleGridViewModel.ResolveSemesterBorderBrush(tile.SemesterName, tile.SemesterColor);
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
                        BorderThickness = new Thickness(3),
                        CornerRadius    = new CornerRadius(3),
                        Padding         = new Thickness(3, 2),
                        Child           = stack,
                    };

                    border = new Border
                    {
                        Width           = tileW - TilePaddingH,
                        Height          = adjustedTileH,
                        Background      = TileFill,
                        BorderBrush     = OverlayFrameBorder,
                        BorderThickness = new Thickness(3),
                        CornerRadius    = new CornerRadius(3),
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
                                        : semesterBrush ?? TileBorder,
                        BorderThickness = tileHasOverlay ? new Thickness(2)
                                        : semesterBrush is not null ? new Thickness(3) : new Thickness(1),
                        CornerRadius    = new CornerRadius(3),
                        Padding         = new Thickness(3, 2),
                        ClipToBounds    = false,
                        Child           = stack,
                    };
                }

                ToolTip.SetTip(border, BuildTileTooltipContent(
                    ScheduleGridViewModel.BuildTileTooltip(tile)));

                Canvas.SetLeft(border, tileX);
                Canvas.SetTop(border, adjustedTileY);
                _canvas.Children.Add(border);
            }
        }
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

    public void ExportToPng(string outputPath)
    {
        _canvas ??= this.FindControl<Canvas>("GridCanvas");
        if (_canvas is null || double.IsNaN(_canvas.Width) || _canvas.Width <= 0) return;

        const double scale = 2.0;
        var pixelSize = new PixelSize((int)(_canvas.Width * scale), (int)(_canvas.Height * scale));
        var dpi = new Vector(96 * scale, 96 * scale);

        using var bitmap = new RenderTargetBitmap(pixelSize, dpi);
        bitmap.Render(_canvas);
        bitmap.Save(outputPath);
    }

    /// <summary>
    /// Converts a <see cref="TileTooltip"/> into a <see cref="ToolTip"/> instance suitable
    /// for <see cref="ToolTip.SetTip"/>. Passing a <see cref="ToolTip"/> directly (rather
    /// than arbitrary content) lets us own the background and padding — otherwise Avalonia
    /// wraps the content in its own default white-background ToolTip.
    /// </summary>
    private static ToolTip BuildTileTooltipContent(TileTooltip tooltip)
    {
        object content;
        if (tooltip.Lines.Count == 1)
        {
            content = new TextBlock { Text = tooltip.Lines[0] };
        }
        else
        {
            var panel = new StackPanel { Spacing = 2 };
            foreach (var line in tooltip.Lines)
                panel.Children.Add(new TextBlock { Text = line });
            content = panel;
        }

        return new ToolTip
        {
            Background      = TileFill,
            BorderBrush     = TileBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(6, 4),
            Content         = content,
        };
    }

    private static double TimeToY(int minutes, int firstRowMinutes) =>
        (minutes - firstRowMinutes) / 30.0 * HalfHourHeight;

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

    private static double MeasureTextWidth(string text, double fontSize, FontWeight weight)
    {
        var tb = new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Width;
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

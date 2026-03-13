using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SchedulingAssistant.ViewModels.GridView;
using System.ComponentModel;

namespace SchedulingAssistant.Views.GridView;

public partial class ScheduleGridView : UserControl
{
    private record TileClickContext(string SectionId, int Day, int StartMinutes);
    // Layout constants
    private const double TimeGutterWidth  = 52;
    private const double DayHeaderHeight  = 28;
    private const double HalfHourHeight   = 30;   // pixels per 30-minute slot
    private const double TilePadding      = 3;
    private const double MinTileWidth        = 115; // Minimum horizontal space per tile to accommodate multiple instructor initials
    private const double DayColumnMinSpacing = 15;  // Minimum spacing for visual separation between days

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
    ///   2 × TilePadding (slot-allocation gap)         =  6 px
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

    private static IBrush TileFill           => Res("TileFill");
    private static IBrush TileFillSelected   => Res("TileFillSelected");
    private static IBrush TileBorder         => Res("TileBorder");
    private static IBrush TileBorderSelected => Res("TileBorderSelected");
    private static IBrush OverlayFrameBorder => Res("OverlayFrameBorder");
    private static IBrush RuleBrush          => Res("GridRuleLine");
    private static IBrush HourRuleBrush      => Res("GridHourRuleLine");
    private static IBrush HeaderFill         => Res("ChromeBackground");
    private static IBrush HeaderBorder       => Res("ChromeBorder");
    private static IBrush GutterBg           => Res("GridGutterBackground");

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
        if (e.PropertyName == nameof(ScheduleGridViewModel.GridData) ||
            e.PropertyName == nameof(ScheduleGridViewModel.SelectedSectionId))
            Render();
    }

    private void Render()
    {
        _canvas ??= this.FindControl<Canvas>("GridCanvas");
        if (_canvas is null) return;

        _canvas.Children.Clear();

        var data = _vm?.GridData ?? GridData.Empty;
        if (data.DayColumns.Count == 0) { ShowEmpty(); return; }

        int dayCount    = data.DayColumns.Count;
        int semCount    = data.SemesterCount;
        int logicalDayCount = dayCount / semCount;

        // Tracks the widest unconstrained tile label per flat column; populated in Phase 1.
        var dayContentWidths = new double[dayCount];

        // ── Phase 1: Measure tile content (heights and natural label widths) ──
        var tileHeightMap = new Dictionary<(int, int), (double timeBasedHeight, double actualHeight)>();
        var selectedId    = _vm?.SelectedSectionId;
        var entryCursor   = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        // Collect all tiles; used later for gridline expansion calculation.
        var allTiles = new List<(int day, GridTile tile, double timeBasedHeight)>();

        for (int d = 0; d < dayCount; d++)
        {
            foreach (var tile in data.DayColumns[d].Tiles)
            {
                // Time-proportional height before any content expansion
                double timeBasedH = TimeToY(tile.EndMinutes, data.FirstRowMinutes)
                                  - TimeToY(tile.StartMinutes, data.FirstRowMinutes)
                                  - TilePadding * 2;
                timeBasedH = Math.Max(timeBasedH, 18);

                // Build a StackPanel matching the render layout, then measure natural size.
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

                    var labelText = BuildTileLabel(entry.Label, entry.Initials, entry.FrequencyAnnotation);

                    var entryRow = new Border
                    {
                        Background   = entrySelected ? TileFillSelected : Brushes.Transparent,
                        CornerRadius = new CornerRadius(2),
                        Padding      = new Thickness(1, 0),
                        Child        = new TextBlock
                        {
                            Text         = labelText,
                            FontSize     = 11,
                            FontWeight   = entrySelected ? FontWeight.Bold : FontWeight.SemiBold,
                            Foreground   = entrySelected ? TileBorderSelected : Brushes.Black,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    };
                    stack.Children.Add(entryRow);
                }

                // Measure the StackPanel for HEIGHT only (drives gridline expansion logic).
                stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double actualH = stack.DesiredSize.Height;

                // Track the widest label text in this column using a direct Bold measurement.
                // We use Bold (the heavier weight applied when a section is selected) because
                // it is always >= SemiBold width, guaranteeing the column fits the label in
                // both selected (Bold) and unselected (SemiBold) rendering states.
                // Measuring via MeasureTextWidth rather than StackPanel.DesiredSize avoids
                // subtle width under-reporting that can occur for detached (off-tree) controls.
                foreach (var e in tile.Entries)
                {
                    string lbl = BuildTileLabel(e.Label, e.Initials, e.FrequencyAnnotation);
                    dayContentWidths[d] = Math.Max(dayContentWidths[d],
                        MeasureTextWidth(lbl, 11, FontWeight.Bold));
                }

                // In multi-semester mode multiple columns share time spans; the tallest
                // measured height governs gridline expansion for that slot.
                var key = (tile.StartMinutes, tile.EndMinutes);
                if (!tileHeightMap.TryGetValue(key, out var existing) || actualH > existing.actualHeight)
                    tileHeightMap[key] = (timeBasedH, actualH);
                allTiles.Add((d, tile, timeBasedH));
            }
        }

        // ── Phase 2: Compute content-driven column widths ────────────────────
        // Each logical day is sized to its widest tile label across all of its
        // semester sub-columns. All sub-columns within a logical day get the same
        // width so that day-header centering is preserved. The minimum width for a
        // logical day is the pixel width of its header text (e.g. "Wednesday").
        var dayColWidths = new double[dayCount];
        double headerFontSize = FontSizeFromResource("FontSizeXLarge");

        for (int ld = 0; ld < logicalDayCount; ld++)
        {
            // Floor: the header text must fit inside the combined day-group width,
            // with DayHeaderSidePadding on each side for visual breathing room.
            string dayName         = data.DayColumns[ld * semCount].Header;
            double headerTextWidth = MeasureTextWidth(dayName, headerFontSize, FontWeight.SemiBold)
                                     + DayHeaderSidePadding * 2;
            double minSubColWidth  = headerTextWidth / semCount;

            // Find the widest tile content across every semester sub-column for this day.
            double maxSubColContentWidth = 0;
            for (int s = 0; s < semCount; s++)
            {
                int    flatCol = ld * semCount + s;
                var    dayCol  = data.DayColumns[flatCol];
                if (dayCol.Tiles.Count == 0) continue;

                // dayContentWidths holds the raw Bold text width (no wrapper overhead).
                // required = N × (textWidth + TileTextOverhead) + TilePadding, where N is
                // the maximum number of side-by-side overlapping tiles in this column.
                // This formula works for all N: for N=1 it gives textWidth + 31 px of column,
                // leaving ≥ 11 px of actual TextBlock breathing room after all render layers.
                int    maxOverlap = dayCol.Tiles.Max(t => t.OverlapCount);
                double required   = maxOverlap * (dayContentWidths[flatCol] + TileTextOverhead)
                                    + TilePadding;

                maxSubColContentWidth = Math.Max(maxSubColContentWidth, required);
            }

            double subColWidth = Math.Max(minSubColWidth, maxSubColContentWidth);

            // Assign identical width to every sub-column of this logical day.
            for (int s = 0; s < semCount; s++)
                dayColWidths[ld * semCount + s] = subColWidth;
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
        var    gridlineYOffsets  = new Dictionary<int, double>();
        double cumulativeOffset  = 0;

        for (int mins = data.FirstRowMinutes; mins <= data.LastRowMinutes; mins += 30)
        {
            gridlineYOffsets[mins] = cumulativeOffset;

            // For this half-hour slot, find the maximum expansion needed.
            double expansionThisSlot = 0;

            foreach (var (_, tile, timeBasedH) in allTiles)
            {
                // Check if this tile spans this half-hour slot.
                if (tile.StartMinutes <= mins && mins < tile.EndMinutes)
                {
                    var (_, actualH) = tileHeightMap[(tile.StartMinutes, tile.EndMinutes)];

                    if (actualH > timeBasedH)
                    {
                        // Distribute this tile's expansion proportionally across its slots.
                        int    tileSpanMinutes  = tile.EndMinutes - tile.StartMinutes;
                        double expansionFraction = 1.0 / (tileSpanMinutes / 30.0);
                        double tileExpansion     = actualH - timeBasedH;
                        double slotExpansion     = tileExpansion * expansionFraction;

                        expansionThisSlot = Math.Max(expansionThisSlot, slotExpansion);
                    }
                }
            }

            cumulativeOffset += expansionThisSlot;
        }

        // ── Phase 4: Redraw with adjusted height accounting for expansions ───
        double totalHeight = DayHeaderHeight + TimeToY(data.LastRowMinutes, data.FirstRowMinutes)
                           + gridlineYOffsets[data.LastRowMinutes];

        _canvas.Width  = totalWidth;
        _canvas.Height = totalHeight;

        // ── Gutter background ──────────────────────────────────────────────
        AddRect(_canvas, 0, 0, TimeGutterWidth, totalHeight, GutterBg, null);

        // ── Day header row ─────────────────────────────────────────────────
        // Header background strip
        AddRect(_canvas, TimeGutterWidth, 0, totalWidth - TimeGutterWidth, DayHeaderHeight,
            HeaderFill, HeaderBorder, borderThickness: new Thickness(0, 0, 0, 1));

        // Draw one day header per logical day, spanning all its semester sub-columns.
        // Then draw any semester sub-column borders in the time body below the header.
        for (int dayIdx = 0; dayIdx < logicalDayCount; dayIdx++)
        {
            int firstCol     = dayIdx * semCount;
            double dayGroupX = dayXOffsets[firstCol];
            double dayGroupW = dayXOffsets[firstCol + semCount] - dayGroupX;

            // Vertical separator between day groups
            if (dayIdx > 0)
                AddLine(_canvas, dayGroupX, 0, dayGroupX, totalHeight, HeaderBorder, 1);

            // Day name label centered over the full day group width
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
        }

        // ── Time rows + horizontal rules (with adjusted Y-coordinates) ───────
        for (int mins = data.FirstRowMinutes; mins <= data.LastRowMinutes; mins += 30)
        {
            double y = DayHeaderHeight + TimeToY(mins, data.FirstRowMinutes) + gridlineYOffsets[mins];
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
                double tileW = (dayColWidth - TilePadding) / tile.OverlapCount;
                double tileX = dayX + tile.OverlapIndex * tileW + TilePadding / 2;

                // Get adjusted Y position and height using adjusted gridline positions
                var (timeBasedH, actualH) = tileHeightMap[(tile.StartMinutes, tile.EndMinutes)];

                double startY = DayHeaderHeight + TimeToY(tile.StartMinutes, data.FirstRowMinutes)
                              + gridlineYOffsets[tile.StartMinutes];
                double endY = DayHeaderHeight + TimeToY(tile.EndMinutes, data.FirstRowMinutes)
                            + gridlineYOffsets[tile.EndMinutes];

                double adjustedTileY = startY + TilePadding;
                // Height is the distance between adjusted gridlines, minus padding; but at least the measured content height
                double gridlineSpanH = endY - startY - TilePadding * 2;
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
                    var entryRow = new Border
                    {
                        Background   = entrySelected ? TileFillSelected : Brushes.Transparent,
                        CornerRadius = new CornerRadius(2),
                        Padding      = new Thickness(1, 0),
                        // Commitment tiles are display-only — no hand cursor.
                        Cursor       = entry.IsCommitment ? null : entryCursor,
                        Tag          = clickCtx,
                        Child        = new TextBlock
                        {
                            Text = labelText,
                            FontSize = 11,
                            FontWeight = entrySelected ? FontWeight.Bold : FontWeight.SemiBold,
                            Foreground = entrySelected ? TileBorderSelected
                                       : entry.IsOverlay ? OverlayFrameBorder
                                       : Brushes.Black,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    };
                    entryRow.PointerPressed += (sender, e) =>
                    {
                        // Commitment tiles are display-only. They have no SectionId, so
                        // left-clicking them would select nothing, and right-clicking would
                        // open the context menu with an empty ID. Guard here instead of
                        // letting those calls fall through with bad data.
                        if (entry.IsCommitment) { e.Handled = true; return; }

                        if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
                        {
                            var ctx = (TileClickContext)((Border)sender!).Tag!;
                            _vm?.PrepareContextMenu(ctx.SectionId, ctx.Day, ctx.StartMinutes);
                            if (_vm is not null)
                                _vm.ContextMenu.IsOpen = true;
                            e.Handled = true;
                            return;
                        }
                        if (e.ClickCount >= 2)
                            _vm?.EditRequested?.Invoke(entryId);
                        else
                            _vm?.SelectSection(entryId);
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
                    semesterBrush = ScheduleGridViewModel.ResolveSemesterBorderBrush(tile.SemesterName);
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
                        Width           = tileW - TilePadding,
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
                        Width           = tileW - TilePadding,
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

    private static double TimeToY(int minutes, int firstRowMinutes) =>
        (minutes - firstRowMinutes) / 30.0 * HalfHourHeight;

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

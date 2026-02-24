using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SchedulingAssistant.ViewModels.GridView;
using System.ComponentModel;

namespace SchedulingAssistant.Views.GridView;

public partial class ScheduleGridView : UserControl
{
    // Layout constants
    private const double TimeGutterWidth  = 52;
    private const double DayHeaderHeight  = 28;
    private const double HalfHourHeight   = 30;   // pixels per 30-minute slot
    private const double TilePadding      = 3;
    private const double DayColumnMinWidth = 120;

    // Brushes resolved from AppColors.axaml at first render (after resources are loaded).
    private static IBrush Res(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : Brushes.Transparent;

    private static IBrush TileFill           => Res("TileFill");
    private static IBrush TileFillSelected   => Res("TileFillSelected");
    private static IBrush TileBorder         => Res("TileBorder");
    private static IBrush TileBorderSelected => Res("TileBorderSelected");
    private static IBrush RuleBrush          => Res("GridRuleLine");
    private static IBrush HourRuleBrush      => Res("GridHourRuleLine");
    private static IBrush HeaderFill         => Res("ChromeBackground");
    private static IBrush HeaderBorder       => Res("ChromeBorder");
    private static IBrush GutterBg           => Res("GridGutterBackground");

    private Canvas? _canvas;
    private ScheduleGridViewModel? _vm;

    public ScheduleGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => Render();
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

        // Available width for day columns
        double availWidth = Math.Max(_canvas.Bounds.Width, Bounds.Width);
        int dayCount = data.DayColumns.Count;
        double dayColWidth = Math.Max(DayColumnMinWidth,
            (availWidth - TimeGutterWidth) / dayCount);

        double totalWidth  = TimeGutterWidth + dayColWidth * dayCount;
        double totalHeight = DayHeaderHeight + TimeToY(data.LastRowMinutes, data.FirstRowMinutes);

        _canvas.Width  = totalWidth;
        _canvas.Height = totalHeight;

        // ── Gutter background ──────────────────────────────────────────────
        AddRect(_canvas, 0, 0, TimeGutterWidth, totalHeight, GutterBg, null);

        // ── Day header row ─────────────────────────────────────────────────
        // Header background strip
        AddRect(_canvas, TimeGutterWidth, 0, dayColWidth * dayCount, DayHeaderHeight,
            HeaderFill, HeaderBorder, borderThickness: new Thickness(0, 0, 0, 1));

        for (int d = 0; d < dayCount; d++)
        {
            double x = TimeGutterWidth + d * dayColWidth;
            // Vertical separator between day columns
            if (d > 0)
                AddLine(_canvas, x, 0, x, totalHeight, HeaderBorder, 1);

            var tb = new TextBlock
            {
                Text = data.DayColumns[d].Header,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = dayColWidth,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, (DayHeaderHeight - 14) / 2);
            _canvas.Children.Add(tb);
        }

        // ── Time rows + horizontal rules ───────────────────────────────────
        for (int mins = data.FirstRowMinutes; mins <= data.LastRowMinutes; mins += 30)
        {
            double y = DayHeaderHeight + TimeToY(mins, data.FirstRowMinutes);
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

        // ── Section tiles ──────────────────────────────────────────────────
        var selectedId = _vm?.SelectedSectionId;
        var entryCursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        for (int d = 0; d < dayCount; d++)
        {
            double dayX = TimeGutterWidth + d * dayColWidth;
            foreach (var tile in data.DayColumns[d].Tiles)
            {
                double tileW = (dayColWidth - TilePadding) / tile.OverlapCount;
                double tileX = dayX + tile.OverlapIndex * tileW + TilePadding / 2;
                double tileY = DayHeaderHeight + TimeToY(tile.StartMinutes, data.FirstRowMinutes) + TilePadding;
                double tileH = TimeToY(tile.EndMinutes, data.FirstRowMinutes)
                             - TimeToY(tile.StartMinutes, data.FirstRowMinutes)
                             - TilePadding * 2;

                tileH = Math.Max(tileH, 18);

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

                    var labelText = string.IsNullOrEmpty(entry.Initials)
                        ? entry.Label
                        : $"{entry.Label}  {entry.Initials}";

                    var entryId = entry.SectionId;
                    var entryRow = new Border
                    {
                        Background  = entrySelected ? TileFillSelected : Brushes.Transparent,
                        CornerRadius = new CornerRadius(2),
                        Padding     = new Thickness(1, 0),
                        Cursor      = entryCursor,
                        Child       = new TextBlock
                        {
                            Text = labelText,
                            FontSize = 11,
                            FontWeight = entrySelected ? FontWeight.Bold : FontWeight.SemiBold,
                            Foreground = entrySelected ? TileBorderSelected : Brushes.Black,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    };
                    entryRow.PointerPressed += (_, e) =>
                    {
                        if (e.ClickCount >= 2)
                            _vm?.EditRequested?.Invoke(entryId);
                        else
                            _vm?.SelectSection(entryId);
                        e.Handled = true;
                    };
                    stack.Children.Add(entryRow);
                }

                var border = new Border
                {
                    Width           = tileW - TilePadding,
                    Height          = tileH,
                    Background      = TileFill,
                    BorderBrush     = TileBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(3),
                    Padding         = new Thickness(3, 2),
                    ClipToBounds    = true,
                    Child           = stack,
                };

                Canvas.SetLeft(border, tileX);
                Canvas.SetTop(border, tileY);
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

    private static double TimeToY(int minutes, int firstRowMinutes) =>
        (minutes - firstRowMinutes) / 30.0 * HalfHourHeight;

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

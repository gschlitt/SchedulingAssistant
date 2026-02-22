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

    private static readonly IBrush TileFill        = new SolidColorBrush(Color.Parse("#C8DFF8"));
    private static readonly IBrush TileBorder      = new SolidColorBrush(Color.Parse("#7AAAD4"));
    private static readonly IBrush RuleBrush       = new SolidColorBrush(Color.Parse("#E0E0E0"));
    private static readonly IBrush HourRuleBrush   = new SolidColorBrush(Color.Parse("#C8C8C8"));
    private static readonly IBrush HeaderFill      = new SolidColorBrush(Color.Parse("#AECBF0"));
    private static readonly IBrush HeaderBorder    = new SolidColorBrush(Color.Parse("#7AAAD4"));
    private static readonly IBrush GutterBg        = new SolidColorBrush(Color.Parse("#F5F5F5"));

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
        if (e.PropertyName == nameof(ScheduleGridViewModel.GridData))
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
                Foreground = isHour ? Brushes.Black : new SolidColorBrush(Color.Parse("#666666")),
                Width = TimeGutterWidth - 4,
                TextAlignment = TextAlignment.Right,
            };
            Canvas.SetLeft(timeTb, 0);
            Canvas.SetTop(timeTb, y - (isHour ? 7 : 6));
            _canvas.Children.Add(timeTb);
        }

        // ── Section tiles ──────────────────────────────────────────────────
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

                var border = new Border
                {
                    Width            = tileW - TilePadding,
                    Height           = tileH,
                    Background       = TileFill,
                    BorderBrush      = TileBorder,
                    BorderThickness  = new Thickness(1),
                    CornerRadius     = new CornerRadius(3),
                    Padding          = new Thickness(3, 2),
                    ClipToBounds     = true,
                };

                var stack = new StackPanel { Spacing = 0 };

                if (!string.IsNullOrEmpty(tile.Line1))
                    stack.Children.Add(new TextBlock
                    {
                        Text = tile.Line1,
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });

                if (!string.IsNullOrEmpty(tile.Line2))
                    stack.Children.Add(new TextBlock
                    {
                        Text = tile.Line2,
                        FontSize = 10,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });

                if (!string.IsNullOrEmpty(tile.Line3))
                    stack.Children.Add(new TextBlock
                    {
                        Text = tile.Line3,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.Parse("#444444")),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });

                border.Child = stack;
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
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
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

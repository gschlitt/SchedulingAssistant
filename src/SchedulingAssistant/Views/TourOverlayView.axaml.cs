using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SchedulingAssistant.Helpers;
using SchedulingAssistant.Models.Tour;
using SchedulingAssistant.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Path = Avalonia.Controls.Shapes.Path;

namespace SchedulingAssistant.Views;

/// <summary>
/// Code-behind for the tour overlay. Handles target resolution (visual tree walking),
/// arrow positioning, keyboard shortcuts, and debug toolbar construction.
/// </summary>
public partial class TourOverlayView : UserControl
{
    private TourOverlayViewModel? _vm;
    private Border? _tourCard;
    private Canvas? _arrowCanvas;
    private Path? _arrowPath;
    private Button? _nextButton;


    public TourOverlayView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from previous VM
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as TourOverlayViewModel;
        if (_vm is null) return;

        // Inject the target resolution delegate
        _vm.ResolveTargetAsync = ResolveTargetBoundsAsync;

        // Subscribe to property changes for arrow repositioning
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Resolve named children
        _tourCard = this.FindControl<Border>("TourCard");
        _arrowCanvas = this.FindControl<Canvas>("ArrowCanvas");
        _arrowPath = this.FindControl<Path>("ArrowPath");
        _nextButton = this.FindControl<Button>("NextButton");


#if DEBUG
        BuildDebugToolbar();
#endif
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _vm?.UpdateOverlaySize(e.NewSize);
    }

    /// <summary>
    /// Intercepts keyboard input for tour navigation.
    /// Escape: dismiss. Enter/Space/Right: advance.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_vm is null || !_vm.IsVisible)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _ = _vm.DismissAsync();
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Space:
            case Key.Right:
                _ = _vm.AdvanceAsync();
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);
                break;
        }
    }

    /// <summary>
    /// When the overlay becomes visible, move keyboard focus to the Next button.
    /// </summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TourOverlayViewModel.IsVisible):
                if (_vm?.IsVisible == true && _nextButton is not null)
                    _nextButton.Focus();
                break;

            case nameof(TourOverlayViewModel.CardMargin):
            case nameof(TourOverlayViewModel.ActualPlacement):
            case nameof(TourOverlayViewModel.ArrowOffset):
                UpdateArrow();
                break;
        }
    }


    /// <summary>
    /// Positions the arrow Path just outside the card border, pointing at the target.
    /// Called whenever the card margin, placement, or arrow offset changes.
    /// </summary>
    private void UpdateArrow()
    {
        if (_vm is null || _arrowPath is null || _arrowCanvas is null || _tourCard is null)
            return;

        var placement = _vm.ActualPlacement;
        var offset = _vm.ArrowOffset;
        var cardMargin = _vm.CardMargin;

        // Update arrow path geometry via Classes
        _arrowPath.Classes.Clear();
        _arrowPath.Classes.Add(_vm.ArrowClass);

        // Position arrow adjacent to the card edge
        double arrowX = 0, arrowY = 0;
        const double arrowWidth = 32;
        const double arrowHeight = 16;

        switch (placement)
        {
            case TourPlacement.Right:
                // Arrow on the left edge of the card, pointing left
                arrowX = cardMargin.Left - arrowWidth;
                arrowY = cardMargin.Top + offset - arrowHeight / 2;
                break;

            case TourPlacement.Left:
                // Arrow on the right edge of the card, pointing right
                arrowX = cardMargin.Left + (_vm?.CardWidth ?? 320);
                arrowY = cardMargin.Top + offset - arrowHeight / 2;
                break;

            case TourPlacement.Below:
                // Arrow on the top edge of the card, pointing up
                arrowX = cardMargin.Left + offset - arrowHeight / 2;
                arrowY = cardMargin.Top - arrowWidth;
                break;

            case TourPlacement.Above:
                // Card is bottom-anchored; arrow sits at the card's bottom edge
                arrowX = cardMargin.Left + offset - arrowHeight / 2;
                arrowY = this.Bounds.Height - cardMargin.Bottom;
                break;

            default:
                _arrowPath.IsVisible = false;
                return;
        }

        Canvas.SetLeft(_arrowPath, arrowX);
        Canvas.SetTop(_arrowPath, arrowY);
        _arrowPath.IsVisible = true;
    }

    // ── Target resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Walks the visual tree from the parent MainView to resolve a <see cref="TourTarget"/>
    /// to bounds in this overlay's coordinate space.
    /// </summary>
    /// <param name="target">The tour step's target specification.</param>
    /// <returns>Bounds in overlay coordinates, or null if unresolvable.</returns>
    private Task<Rect?> ResolveTargetBoundsAsync(TourTarget target)
    {
        // Walk up to find the MainView ancestor
        var mainView = this.FindAncestorOfType<MainView>();
        if (mainView is null)
            return Task.FromResult<Rect?>(null);

        Control? targetControl = null;

        switch (target.Kind)
        {
            case TourTargetKind.NamedControl:
                targetControl = mainView.FindControl<Control>(target.Value);
                break;

            case TourTargetKind.Region:
                targetControl = ResolveRegion(mainView, target.Value);
                break;

            case TourTargetKind.MenuButton:
                // Same resolution as NamedControl but may use different placement hints
                targetControl = mainView.FindControl<Control>(target.Value);
                break;
        }

        if (targetControl is null || !targetControl.IsVisible)
            return Task.FromResult<Rect?>(null);

        // Translate target bounds to this overlay's coordinate space
        var topLeft = targetControl.TranslatePoint(new Point(0, 0), this);
        var bottomRight = targetControl.TranslatePoint(
            new Point(targetControl.Bounds.Width, targetControl.Bounds.Height), this);

        if (topLeft is null || bottomRight is null)
            return Task.FromResult<Rect?>(null);

#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[Tour] Resolved '{target.Value}': type={targetControl.GetType().Name}, " +
            $"Bounds={targetControl.Bounds}, " +
            $"translated topLeft={topLeft}, bottomRight={bottomRight}");
#endif

        var rect = new Rect(topLeft.Value, bottomRight.Value);
        return Task.FromResult<Rect?>(rect);
    }

    /// <summary>
    /// Resolves a dot-notation region target (e.g. "ScheduleGrid.Canvas").
    /// First part is the named parent control; second part maps to a known child.
    /// </summary>
    private static Control? ResolveRegion(MainView mainView, string regionPath)
    {
        var parts = regionPath.Split('.', 2);
        if (parts.Length < 2) return null;

        var parent = mainView.FindControl<Control>(parts[0]);
        if (parent is null) return null;

        // Walk visual children to find the named sub-control
        return FindDescendantByName(parent, parts[1]);
    }

    /// <summary>
    /// Recursively searches the visual tree for a descendant with the given Name.
    /// </summary>
    private static Control? FindDescendantByName(Visual parent, string name)
    {
        var count = parent.GetVisualChildren();
        foreach (var child in count)
        {
            if (child is Control c && c.Name == name)
                return c;

            var found = FindDescendantByName(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    // ── Debug toolbar ────────────────────────────────────────────────────────

#if DEBUG
    /// <summary>
    /// Builds the debug authoring toolbar (jump-to-step dropdown + placement cycler)
    /// and adds it to the overlay panel. Excluded from release builds.
    /// </summary>
    private void BuildDebugToolbar()
    {
        if (_vm is null || Content is not Panel rootPanel) return;

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 8, 0),
            Spacing = 4,
            IsVisible = true,
            Background = Avalonia.Media.Brushes.White,
            Opacity = 0.9
        };

        // Jump-to-step dropdown
        var combo = new ComboBox
        {
            Width = 200,
            FontSize = 10,
            MinHeight = 0,
            Padding = new Thickness(4, 2)
        };
        combo.Bind(ComboBox.ItemsSourceProperty,
            new Avalonia.Data.Binding("DebugStepKeys"));
        combo.Bind(ComboBox.SelectedItemProperty,
            new Avalonia.Data.Binding("DebugSelectedStepKey") { Mode = Avalonia.Data.BindingMode.TwoWay });
        toolbar.Children.Add(combo);

        // Placement cycler buttons
        foreach (var label in new[] { "R", "B", "L", "A" })
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 10,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(6, 2),
                Background = Avalonia.Media.Brushes.LightGray
            };

            var placementName = label switch
            {
                "R" => "Right",
                "B" => "Below",
                "L" => "Left",
                "A" => "Above",
                _ => "Right"
            };

            btn.Bind(Button.CommandProperty,
                new Avalonia.Data.Binding("DebugSetPlacementCommand"));
            btn.CommandParameter = placementName;
            toolbar.Children.Add(btn);
        }

        rootPanel.Children.Add(toolbar);
    }
#endif
}

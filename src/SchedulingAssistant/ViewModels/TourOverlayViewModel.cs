using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Helpers;
using SchedulingAssistant.Models.Tour;
using SchedulingAssistant.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Drives the tour overlay presentation layer. Subscribes to <see cref="TourRunner"/>
/// events and computes all positioning, content, and state for data binding.
/// Target resolution is delegated to the view via <see cref="ResolveTargetAsync"/>.
/// </summary>
public partial class TourOverlayViewModel : ViewModelBase
{
    private readonly TourRunner _runner;

    /// <summary>Maximum attempts to resolve a target after a PreAction changes layout.</summary>
    private const int MaxResolveRetries = 3;

    /// <summary>Delay between target resolution retries.</summary>
    private const int ResolveRetryDelayMs = 100;

    /// <summary>The current tour step (kept for PostAction/PreAction access).</summary>
    private TourStep? _currentStep;

    /// <summary>
    /// Index into <see cref="TourStep.MidActions"/> for the current step.
    /// Tracks which mid-action fires on the next user click. Reset to 0 on
    /// new step, dismiss, and hide.
    /// </summary>
    private int _midActionIndex;

    /// <summary>
    /// True while <see cref="AdvanceAsync"/> is handling the step transition.
    /// Prevents the fire-and-forget <see cref="ShowStepAsync"/> (triggered by
    /// the StepChanged event) from racing with the explicit positioning call
    /// in AdvanceAsync.
    /// </summary>
    private bool _isAdvancing;

    /// <summary>Size of the overlay panel, updated by the view on SizeChanged.</summary>
    private Size _overlaySize;

    // ── Bindable properties ──────────────────────────────────────────────────

    /// <summary>True when the overlay should be visible (a tour is active).</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Step title displayed in the card header.</summary>
    [ObservableProperty]
    private string? _title;

    /// <summary>Step body text.</summary>
    [ObservableProperty]
    private string? _body;

    /// <summary>Segment name shown beneath the title.</summary>
    [ObservableProperty]
    private string? _segmentTitle;

    /// <summary>"Step N of M" counter text.</summary>
    [ObservableProperty]
    private string _stepCounterText = string.Empty;

    /// <summary>True on the last step — changes Next button to Done.</summary>
    [ObservableProperty]
    private bool _isLastStep;

    /// <summary>True when the current step is a welcome/introduction card (wider, centered).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardWidth))]
    private bool _isWelcomeStep;

    /// <summary>Normal card width in pixels.</summary>
    public const double NormalCardWidth = 320;

    /// <summary>Wider card width for welcome/introduction steps.</summary>
    public const double WelcomeCardWidth = 480;

    /// <summary>Card width: wider for welcome steps, normal otherwise.</summary>
    public double CardWidth => IsWelcomeStep ? WelcomeCardWidth : NormalCardWidth;

    /// <summary>Button label: "Next" or "Done" depending on <see cref="IsLastStep"/>.</summary>
    public string AdvanceButtonLabel => IsLastStep ? "Done" : "Next >";

    // ── Highlight ring positioning ───────────────────────────────────────────

    /// <summary>Margin (Left, Top) for absolute positioning of the highlight ring.</summary>
    [ObservableProperty]
    private Thickness _highlightMargin;

    /// <summary>Width of the highlight ring border.</summary>
    [ObservableProperty]
    private double _highlightWidth;

    /// <summary>Height of the highlight ring border.</summary>
    [ObservableProperty]
    private double _highlightHeight;

    /// <summary>False when the target couldn't be resolved — hides the highlight ring.</summary>
    [ObservableProperty]
    private bool _isHighlightVisible;

    // ── Card positioning ─────────────────────────────────────────────────────

    /// <summary>Margin for absolute positioning of the card.</summary>
    [ObservableProperty]
    private Thickness _cardMargin;

    /// <summary>
    /// Vertical alignment for the card. Top for most placements;
    /// Bottom for Above so the card grows upward as content increases.
    /// </summary>
    [ObservableProperty]
    private Avalonia.Layout.VerticalAlignment _cardVerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;

    /// <summary>Resolved placement of the card relative to the target.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArrowClass))]
    private TourPlacement _actualPlacement;

    /// <summary>Arrow offset along the card edge (vertical for Left/Right, horizontal for Above/Below).</summary>
    [ObservableProperty]
    private double _arrowOffset;

    /// <summary>
    /// CSS-style class name for the arrow direction, used by the view to select
    /// the correct arrow style (e.g. "arrowLeft" when card is on the Right).
    /// </summary>
    public string ArrowClass => ActualPlacement switch
    {
        TourPlacement.Right => "arrowLeft",
        TourPlacement.Left  => "arrowRight",
        TourPlacement.Below => "arrowUp",
        TourPlacement.Above => "arrowDown",
        _                   => "arrowNone"
    };

    // ── View-injected delegates ──────────────────────────────────────────────

    /// <summary>
    /// Delegate injected by the view to resolve a <see cref="TourTarget"/> to bounds
    /// in the overlay's coordinate space. Returns null if the target is unresolvable.
    /// </summary>
    public Func<TourTarget, Task<Rect?>>? ResolveTargetAsync { get; set; }

    // ── Debug authoring tools ────────────────────────────────────────────────

#if DEBUG
    /// <summary>All step keys in the catalog, for the debug jump-to-step dropdown.</summary>
    [ObservableProperty]
    private ObservableCollection<string> _debugStepKeys = new();

    /// <summary>Selected step key in the debug dropdown. Setting this jumps to that step.</summary>
    [ObservableProperty]
    private string? _debugSelectedStepKey;

    /// <summary>Debug placement override — null means use the step's defined placement.</summary>
    private TourPlacement? _debugPlacementOverride;

    partial void OnDebugSelectedStepKeyChanged(string? value)
    {
        if (value is not null && _runner.IsActive)
            _ = JumpToStepAsync(value);
    }

    /// <summary>Overrides the card placement for the current step (debug authoring tool).</summary>
    [RelayCommand]
    private async Task DebugSetPlacement(string placement)
    {
        if (!Enum.TryParse<TourPlacement>(placement, out var p)) return;
        _debugPlacementOverride = p;
        if (_currentStep is not null)
            await PositionOverlayAsync(_currentStep);
    }

    /// <summary>
    /// Jumps directly to the given step key, running PostAction on the current step
    /// and PreAction on the target step. Used by the debug dropdown.
    /// </summary>
    private async Task JumpToStepAsync(string stepKey)
    {
        var step = TourCatalog.GetStep(stepKey);
        if (step is null || _runner.ActiveTour is null) return;

        // Run current step's PostAction
        if (_currentStep?.PostAction is not null)
        {
            try { await _currentStep.PostAction(); }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[Tour] PostAction on '{_currentStep.Key}' threw during debug jump: {ex.Message}");
            }
        }
        TourActionDefinitions.RestoreAllLightDismiss();

        // Find the segment/step indices for the target step
        var tour = _runner.ActiveTour;
        for (int s = 0; s < tour.SegmentKeys.Count; s++)
        {
            var seg = TourCatalog.GetSegment(tour.SegmentKeys[s]);
            if (seg is null) continue;
            for (int i = 0; i < seg.StepKeys.Count; i++)
            {
                if (seg.StepKeys[i] == stepKey && _runner.Progress is not null)
                {
                    _runner.Progress.CurrentSegmentIndex = s;
                    _runner.Progress.CurrentStepIndex = i;

                    // Run arriving step's PreAction
                    if (step.PreAction is not null)
                    {
                        try { await step.PreAction(); }
                        catch (Exception ex)
                        {
                            App.Logger.LogInfo($"[Tour] PreAction on '{step.Key}' threw during debug jump: {ex.Message}");
                        }
                    }

                    await ShowStepAsync(step);
                    return;
                }
            }
        }
    }

    private void PopulateDebugStepKeys()
    {
        DebugStepKeys.Clear();
        if (_runner.ActiveTour is null) return;
        foreach (var segKey in _runner.ActiveTour.SegmentKeys)
        {
            var seg = TourCatalog.GetSegment(segKey);
            if (seg is null) continue;
            foreach (var sk in seg.StepKeys)
                DebugStepKeys.Add(sk);
        }
    }
#endif

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <param name="runner">The singleton <see cref="TourRunner"/> from DI.</param>
    public TourOverlayViewModel(TourRunner runner)
    {
        _runner = runner;

        _runner.StepChanged  += OnStepChanged;
        _runner.TourCompleted += OnTourCompleted;
        _runner.TourDismissed += OnTourDismissed;
        _runner.BodyChanged  += OnRunnerBodyChanged;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnStepChanged(TourStep step)
    {
        _ = ShowStepAsync(step);

#if DEBUG
        PopulateDebugStepKeys();
#endif
    }

    private void OnTourCompleted()
    {
        HideOverlay();
    }

    private void OnTourDismissed()
    {
        HideOverlay();
    }

    private void OnRunnerBodyChanged(string newBody)
    {
        Body = newBody;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances to the next step (or completes on the last step).
    /// When the current step has MidActions, each click runs the next mid-action
    /// and auto-advances the body text. After all mid-actions complete, the next
    /// click runs PostAction and advances. Steps without MidActions advance on
    /// a single click.
    /// </summary>
    /// <remarks>
    /// All action invocations are wrapped in try/catch so a thrown exception
    /// never crashes the app during a demo. On error the tour continues.
    /// </remarks>
    [RelayCommand]
    public async Task AdvanceAsync()
    {
        if (!_runner.IsActive) return;

        // Run the next mid-action if any remain
        if (_currentStep?.MidActions is { Count: > 0 } midActions
            && _midActionIndex < midActions.Count)
        {
            try
            {
                await midActions[_midActionIndex]();
            }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[Tour] MidAction[{_midActionIndex}] on '{_currentStep.Key}' threw: {ex.Message}");
            }
            _midActionIndex++;
            _runner.AdvanceBody();
            return;
        }

        // Hide while transitioning
        IsHighlightVisible = false;

        // Run departing step's PostAction
        if (_currentStep?.PostAction is not null)
        {
            try
            {
                await _currentStep.PostAction();
            }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[Tour] PostAction on '{_currentStep.Key}' threw: {ex.Message}");
            }
        }

        // Safety net: restore any popups whose light-dismiss was suppressed
        TourActionDefinitions.RestoreAllLightDismiss();

#if DEBUG
        _debugPlacementOverride = null;
#endif

        // Prevent the fire-and-forget ShowStepAsync (triggered by StepChanged)
        // from racing with our explicit positioning below.
        _isAdvancing = true;
        try
        {
            // Advance the state machine (fires StepChanged or TourCompleted)
            _runner.Advance();

            // If still active, run arriving step's PreAction
            var newStep = _runner.ResolveCurrentStep();
            if (newStep?.PreAction is not null)
            {
                try
                {
                    await newStep.PreAction();
                }
                catch (Exception ex)
                {
                    App.Logger.LogInfo($"[Tour] PreAction on '{newStep.Key}' threw: {ex.Message}");
                }
            }

            if (_runner.IsActive && newStep is not null)
                await PositionOverlayAsync(newStep);
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    /// <summary>
    /// Dismisses the active tour. Runs the current step's PostAction first.
    /// </summary>
    [RelayCommand]
    public async Task DismissAsync()
    {
        if (!_runner.IsActive) return;

        // Run departing step's PostAction
        if (_currentStep?.PostAction is not null)
        {
            try
            {
                await _currentStep.PostAction();
            }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[Tour] PostAction on '{_currentStep.Key}' threw during dismiss: {ex.Message}");
            }
        }

        // Safety net: restore any popups whose light-dismiss was suppressed
        TourActionDefinitions.RestoreAllLightDismiss();

        _runner.Dismiss();
    }

    // ── Core display logic ───────────────────────────────────────────────────

    /// <summary>
    /// Updates all content properties and positions the overlay for the given step.
    /// </summary>
    private async Task ShowStepAsync(TourStep step)
    {
        _currentStep = step;
        _midActionIndex = 0;

        // Update content
        Title = step.Title;
        Body = step.Body;
        IsWelcomeStep = step.IsWelcome;
        SegmentTitle = _runner.GetCurrentSegmentTitle();

        var globalIndex = _runner.GetGlobalStepIndex();
        var globalCount = _runner.GetGlobalStepCount();
        StepCounterText = $"Step {globalIndex + 1} of {globalCount}";
        IsLastStep = (globalIndex + 1) >= globalCount;
        OnPropertyChanged(nameof(AdvanceButtonLabel));

        // Position highlight and card — but skip if AdvanceAsync is handling
        // positioning to avoid a race between two concurrent PositionOverlayAsync calls.
        if (!_isAdvancing)
            await PositionOverlayAsync(step);

        IsVisible = true;
    }

    /// <summary>
    /// Resolves the target, computes highlight and card positions, and updates bindings.
    /// Retries up to <see cref="MaxResolveRetries"/> times with a delay for layout settling.
    /// </summary>
    private async Task PositionOverlayAsync(TourStep step)
    {
        // Untargeted steps (welcome/intro cards) always center — skip resolution entirely
        if (step.Target.Kind == TourTargetKind.None)
        {
            ShowCentered();
            return;
        }

        if (ResolveTargetAsync is null)
        {
            ShowCentered();
            return;
        }

        Rect? bounds = null;
        for (int attempt = 0; attempt < MaxResolveRetries; attempt++)
        {
            bounds = await ResolveTargetAsync(step.Target);
            if (bounds is not null) break;
            await Task.Delay(ResolveRetryDelayMs);
        }

        if (bounds is null || _overlaySize.Width < 1 || _overlaySize.Height < 1)
        {
            ShowCentered();
            return;
        }

        var targetRect = bounds.Value;

        // Highlight ring
        var ring = TourPositionCalculator.ComputeHighlightRect(targetRect);
        HighlightMargin = new Thickness(ring.X, ring.Y, 0, 0);
        HighlightWidth = ring.Width;
        HighlightHeight = ring.Height;
        IsHighlightVisible = true;

        // Card position
        var preferred = step.Placement;
#if DEBUG
        if (_debugPlacementOverride is not null)
            preferred = _debugPlacementOverride.Value;
#endif

        var cardPos = TourPositionCalculator.ComputeCardPosition(
            targetRect, _overlaySize, preferred);

        ActualPlacement = cardPos.ActualPlacement;
        ArrowOffset = cardPos.ArrowOffset;

        if (cardPos.ActualPlacement == TourPlacement.Above)
        {
            // Anchor from bottom so the card grows upward with content.
            // The calculator's intended bottom edge is cardMargin.Top + EstimatedCardHeight.
            var desiredBottom = cardPos.CardMargin.Top + TourPositionCalculator.EstimatedCardHeight;
            var bottomMargin = _overlaySize.Height - desiredBottom;
            CardVerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            CardMargin = new Thickness(cardPos.CardMargin.Left, 0, 0, bottomMargin);
        }
        else
        {
            CardVerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            CardMargin = cardPos.CardMargin;
        }
    }

    /// <summary>Centers the card with no highlight (target unresolvable).</summary>
    private void ShowCentered()
    {
        IsHighlightVisible = false;
        CardVerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var size = _overlaySize.Width > 0 ? _overlaySize : new Size(1400, 800);
        var pos = TourPositionCalculator.ComputeCenteredPosition(size, CardWidth);
        CardMargin = pos.CardMargin;
        ActualPlacement = TourPlacement.Auto;
        ArrowOffset = 0;
    }

    /// <summary>Hides the overlay and clears state.</summary>
    private void HideOverlay()
    {
        IsVisible = false;
        IsHighlightVisible = false;
        IsWelcomeStep = false;
        _currentStep = null;
        _midActionIndex = 0;

        // Safety net on hide
        TourActionDefinitions.RestoreAllLightDismiss();

#if DEBUG
        _debugPlacementOverride = null;
        DebugStepKeys.Clear();
#endif
    }

    // ── View callbacks ───────────────────────────────────────────────────────

    /// <summary>
    /// Called by the view when the overlay panel resizes. Triggers repositioning
    /// so the card stays correctly placed after window resize.
    /// </summary>
    /// <param name="newSize">New overlay size.</param>
    public void UpdateOverlaySize(Size newSize)
    {
        _overlaySize = newSize;
        if (_currentStep is not null && IsVisible)
            _ = PositionOverlayAsync(_currentStep);
    }

    partial void OnIsLastStepChanged(bool value) =>
        OnPropertyChanged(nameof(AdvanceButtonLabel));
}

using SchedulingAssistant.Models.Tour;

namespace SchedulingAssistant.Services;

/// <summary>
/// Coordinates tour lifecycle: start, advance, dismiss, and auto-trigger evaluation.
/// DI-registered singleton — follows the <see cref="SharedScheduleService"/> pattern.
/// Holds in-memory state only; persists terminal states to <see cref="AppSettings.CompletedTourKeys"/>.
/// </summary>
public class TourRunner
{
    /// <summary>The currently active tour definition, or null when no tour is running.</summary>
    public TourDefinition? ActiveTour { get; private set; }

    /// <summary>Progress tracker for the active tour, or null when idle.</summary>
    public TourProgress? Progress { get; private set; }

    /// <summary>True when a tour is currently in progress.</summary>
    public bool IsActive => ActiveTour is not null;

    /// <summary>Fires when the tour advances to a new step. Carries the resolved step.</summary>
    public event Action<TourStep>? StepChanged;

    /// <summary>Fires when the active tour reaches its final step and completes.</summary>
    public event Action? TourCompleted;

    /// <summary>Fires when the user dismisses the active tour at any point.</summary>
    public event Action? TourDismissed;

    /// <summary>
    /// Fires when <see cref="AdvanceBody"/> moves to the next body message.
    /// Carries the new message text.
    /// </summary>
    public event Action<string>? BodyChanged;

    /// <summary>Tracks the current body message index within the active step.</summary>
    private int _bodyIndex;

    /// <summary>
    /// Starts a tour by key. If another tour is currently active, it is dismissed first
    /// (invariant 11: at most one active tour at a time). Creates a fresh
    /// <see cref="TourProgress"/> and fires <see cref="StepChanged"/> with the first step.
    /// </summary>
    /// <param name="tourKey">Key of the tour to start. Must exist in <see cref="TourCatalog"/>.</param>
    /// <returns>True if the tour was started successfully; false if the tour key was not found.</returns>
    public bool Start(string tourKey)
    {
        var tour = TourCatalog.GetTour(tourKey);
        if (tour is null) return false;

        // Dismiss any active tour first
        if (IsActive)
            DismissInternal(persist: true);

        ActiveTour = tour;
        Progress = TourProgress.Start(tourKey);
        _bodyIndex = 0;

        var step = ResolveCurrentStep();
        if (step is not null)
            FireStepChanged(step);

        return true;
    }

    /// <summary>
    /// Advances to the next step. Crosses segment boundaries automatically.
    /// When the last step of the last segment is reached, the tour completes.
    /// </summary>
    public void Advance()
    {
        if (ActiveTour is null || Progress is null) return;

        var currentSegment = TourCatalog.GetSegment(
            ActiveTour.SegmentKeys[Progress.CurrentSegmentIndex]);
        if (currentSegment is null) return;

        // Try to advance within the current segment
        if (Progress.CurrentStepIndex < currentSegment.StepKeys.Count - 1)
        {
            Progress.CurrentStepIndex++;
        }
        // Try to advance to the next segment
        else if (Progress.CurrentSegmentIndex < ActiveTour.SegmentKeys.Count - 1)
        {
            Progress.CurrentSegmentIndex++;
            Progress.CurrentStepIndex = 0;
        }
        // Last step of last segment — tour complete
        else
        {
            MarkCompleted();
            return;
        }

        _bodyIndex = 0;
        var step = ResolveCurrentStep();
        if (step is not null)
            FireStepChanged(step);
    }

    /// <summary>
    /// Dismisses the active tour. Marks it as dismissed, persists to
    /// <see cref="AppSettings.CompletedTourKeys"/>, and fires <see cref="TourDismissed"/>.
    /// Safe to call when no tour is active (no-op).
    /// </summary>
    public void Dismiss()
    {
        if (!IsActive) return;
        DismissInternal(persist: true);
    }

    /// <summary>
    /// Advances to the next body message within the current step.
    /// Called by tour action callbacks between sub-actions to update the card text.
    /// No-op if there are no more messages.
    /// </summary>
    public void AdvanceBody()
    {
        var step = ResolveCurrentStep();
        if (step is null) return;
        _bodyIndex++;
        if (_bodyIndex < step.BodyMessages.Count)
        {
            try { BodyChanged?.Invoke(step.BodyMessages[_bodyIndex]); }
            catch (Exception ex)
            {
                App.Logger.LogInfo($"[Tour] BodyChanged handler threw: {ex.Message}");
                DismissInternal(persist: false);
            }
        }
    }

    /// <summary>
    /// Evaluates auto-trigger rules for all tours in catalog order.
    /// Starts the first eligible tour whose key is not already in
    /// <see cref="AppSettings.CompletedTourKeys"/>. Call after the main window
    /// loads and semester context initializes.
    /// </summary>
    public void EvaluateAutoTriggers()
    {
        var completedKeys = AppSettings.Current.CompletedTourKeys;

        foreach (var tour in TourCatalog.AllTours.Values)
        {
            if (tour.AutoTrigger is null) continue;
            if (completedKeys.Contains(tour.Key)) continue;

            if (ShouldAutoTrigger(tour))
            {
                Start(tour.Key);
                return; // First match wins
            }
        }
    }

    /// <summary>
    /// Resolves the current step by looking up the segment and step keys
    /// in the catalog. Returns null if the step key is missing (the
    /// presentation layer should skip unresolvable steps).
    /// </summary>
    public TourStep? ResolveCurrentStep()
    {
        if (ActiveTour is null || Progress is null) return null;

        if (Progress.CurrentSegmentIndex >= ActiveTour.SegmentKeys.Count)
            return null;

        var segKey = ActiveTour.SegmentKeys[Progress.CurrentSegmentIndex];
        var segment = TourCatalog.GetSegment(segKey);
        if (segment is null) return null;

        if (Progress.CurrentStepIndex >= segment.StepKeys.Count)
            return null;

        var stepKey = segment.StepKeys[Progress.CurrentStepIndex];
        return TourCatalog.GetStep(stepKey);
    }

    /// <summary>
    /// Returns the current step's global index across all segments (zero-based).
    /// Used for "Step N of M" display in the tour card.
    /// </summary>
    public int GetGlobalStepIndex()
    {
        if (ActiveTour is null || Progress is null) return 0;

        int index = 0;
        for (int s = 0; s < Progress.CurrentSegmentIndex; s++)
        {
            var seg = TourCatalog.GetSegment(ActiveTour.SegmentKeys[s]);
            if (seg is not null) index += seg.StepKeys.Count;
        }
        index += Progress.CurrentStepIndex;
        return index;
    }

    /// <summary>
    /// Returns the total number of steps across all segments in the active tour.
    /// Used for "Step N of M" display in the tour card.
    /// </summary>
    public int GetGlobalStepCount()
    {
        if (ActiveTour is null) return 0;

        int count = 0;
        foreach (var segKey in ActiveTour.SegmentKeys)
        {
            var seg = TourCatalog.GetSegment(segKey);
            if (seg is not null) count += seg.StepKeys.Count;
        }
        return count;
    }

    /// <summary>
    /// Returns the title of the segment containing the current step.
    /// Used for the segment label in the tour card.
    /// </summary>
    public string? GetCurrentSegmentTitle()
    {
        if (ActiveTour is null || Progress is null) return null;
        if (Progress.CurrentSegmentIndex >= ActiveTour.SegmentKeys.Count) return null;

        var segKey = ActiveTour.SegmentKeys[Progress.CurrentSegmentIndex];
        return TourCatalog.GetSegment(segKey)?.Title;
    }

    /// <summary>
    /// Safely fires <see cref="StepChanged"/>. If a subscriber throws, the tour
    /// is dismissed so the user is never left with a stuck overlay.
    /// </summary>
    private void FireStepChanged(TourStep step)
    {
        try { StepChanged?.Invoke(step); }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[Tour] StepChanged handler threw on '{step.Key}': {ex.Message}");
            DismissInternal(persist: false);
        }
    }

    private void MarkCompleted()
    {
        if (Progress is null || ActiveTour is null) return;

        Progress.Status = TourStatus.Completed;
        Progress.CompletedAt = DateTime.UtcNow;

        PersistCompletion(ActiveTour.Key);

        var tour = ActiveTour;
        ActiveTour = null;
        Progress = null;

        try { TourCompleted?.Invoke(); }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[Tour] TourCompleted handler threw: {ex.Message}");
        }
    }

    private void DismissInternal(bool persist)
    {
        if (Progress is null || ActiveTour is null) return;

        Progress.Status = TourStatus.Dismissed;
        Progress.CompletedAt = DateTime.UtcNow;

        if (persist)
            PersistCompletion(ActiveTour.Key);

        ActiveTour = null;
        Progress = null;

        try { TourDismissed?.Invoke(); }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[Tour] TourDismissed handler threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a tour key to <see cref="AppSettings.CompletedTourKeys"/> and saves.
    /// On WASM this is a no-op (settings reset each session).
    /// </summary>
    private static void PersistCompletion(string tourKey)
    {
        var settings = AppSettings.Current;
        if (!settings.CompletedTourKeys.Contains(tourKey))
        {
            settings.CompletedTourKeys.Add(tourKey);
            settings.Save();
        }
    }

    /// <summary>
    /// Evaluates whether a tour's auto-trigger condition is satisfied
    /// given the current app state.
    /// </summary>
    private static bool ShouldAutoTrigger(TourDefinition tour)
    {
        return tour.AutoTrigger switch
        {
            TourTriggerRule.PostWizardFirstLaunch => AppSettings.Current.IsInitialSetupComplete,
            TourTriggerRule.PreWizardFirstLaunch => !AppSettings.Current.IsInitialSetupComplete,
            TourTriggerRule.EverySession => !PlatformCapabilities.SupportsFileDialogs,
            _ => false
        };
    }
}

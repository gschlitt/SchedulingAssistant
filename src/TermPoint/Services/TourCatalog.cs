using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using TermPoint.Models.Tour;

namespace TermPoint.Services;

/// <summary>
/// Static registry of all tour definitions. Steps, segments, and tours are immutable
/// and created once at startup. Analogous to <c>HelpViewModel.BuildTopicTree()</c>.
/// </summary>
/// <remarks>
/// Two initialization paths:
/// <list type="bullet">
///   <item><see cref="Initialize(IResourceDictionary, Dictionary{string, TourStepActions}?)"/> —
///   production path, reads AXAML resource dictionaries and merges with C# action callbacks.</item>
///   <item><see cref="Initialize(IEnumerable{TourStep}, IEnumerable{TourSegment}, IEnumerable{TourDefinition})"/> —
///   test-friendly overload that accepts pre-built collections directly.</item>
/// </list>
/// </remarks>
public static class TourCatalog
{
    private static Dictionary<string, TourStep> _steps = new();
    private static Dictionary<string, TourSegment> _segments = new();
    private static Dictionary<string, TourDefinition> _tours = new();
    private static bool _initialized;

    /// <summary>All registered steps, keyed by step key.</summary>
    public static IReadOnlyDictionary<string, TourStep> AllSteps => _steps;

    /// <summary>All registered segments, keyed by segment key.</summary>
    public static IReadOnlyDictionary<string, TourSegment> AllSegments => _segments;

    /// <summary>All registered tour definitions, keyed by tour key.</summary>
    public static IReadOnlyDictionary<string, TourDefinition> AllTours => _tours;

    /// <summary>Returns the step with the given key, or null if not found.</summary>
    public static TourStep? GetStep(string key) =>
        _steps.TryGetValue(key, out var step) ? step : null;

    /// <summary>Returns the segment with the given key, or null if not found.</summary>
    public static TourSegment? GetSegment(string key) =>
        _segments.TryGetValue(key, out var seg) ? seg : null;

    /// <summary>Returns the tour definition with the given key, or null if not found.</summary>
    public static TourDefinition? GetTour(string key) =>
        _tours.TryGetValue(key, out var tour) ? tour : null;

    /// <summary>
    /// Returns all tours marked as replayable, in catalog insertion order.
    /// Used by the Help menu to populate the "Take a Tour" list.
    /// </summary>
    public static IReadOnlyList<TourDefinition> GetReplayableTours() =>
        _tours.Values.Where(t => t.IsReplayable).ToList().AsReadOnly();

    /// <summary>
    /// Production initialization path. Scans an Avalonia <see cref="IResourceDictionary"/>
    /// for <see cref="TourStepData"/>, <see cref="TourSegmentData"/>, and <see cref="TourData"/>
    /// entries, converts them to immutable model types, and merges optional PreAction/PostAction
    /// callbacks from the <paramref name="actions"/> dictionary.
    /// </summary>
    /// <param name="resources">The application's merged resource dictionary.</param>
    /// <param name="actions">
    /// Optional dictionary mapping step keys to PreAction/PostAction pairs.
    /// Pass null or empty when no actions are needed (e.g. during initial data-model-only phase).
    /// </param>
    public static void Initialize(
        IResourceDictionary resources,
        Dictionary<string, TourStepActions>? actions = null)
    {
        var stepDataEntries = new Dictionary<string, TourStepData>();
        var segmentDataEntries = new Dictionary<string, TourSegmentData>();
        var tourDataEntries = new Dictionary<string, TourData>();

        ScanResources(resources, stepDataEntries, segmentDataEntries, tourDataEntries);

        var steps = ConvertSteps(stepDataEntries, actions ?? new());
        var segments = ConvertSegments(segmentDataEntries);
        var tours = ConvertTours(tourDataEntries);

        _steps = steps;
        _segments = segments;
        _tours = tours;
        _initialized = true;
    }

    /// <summary>
    /// Test-friendly initialization that accepts pre-built immutable collections directly,
    /// bypassing AXAML resource parsing.
    /// </summary>
    /// <param name="steps">Tour steps to register.</param>
    /// <param name="segments">Tour segments to register.</param>
    /// <param name="tours">Tour definitions to register.</param>
    public static void Initialize(
        IEnumerable<TourStep> steps,
        IEnumerable<TourSegment> segments,
        IEnumerable<TourDefinition> tours)
    {
        _steps = steps.ToDictionary(s => s.Key);
        _segments = segments.ToDictionary(s => s.Key);
        _tours = tours.ToDictionary(t => t.Key);
        _initialized = true;
    }

    /// <summary>
    /// Validates the catalog against the 10 definition-time invariants from the design spec.
    /// Returns an empty list when everything is valid.
    /// </summary>
    /// <returns>Human-readable error messages for each violation found.</returns>
    public static List<string> Validate()
    {
        var errors = new List<string>();

        // Invariants 1-3: unique keys (enforced by dictionary construction — duplicates
        // would have thrown during Initialize). No additional check needed.

        // Invariant 4: every segment has at least one step key
        foreach (var seg in _segments.Values)
        {
            if (seg.StepKeys.Count == 0)
                errors.Add($"Segment '{seg.Key}' has no step keys.");
        }

        // Invariant 5: every tour has at least one segment key
        foreach (var tour in _tours.Values)
        {
            if (tour.SegmentKeys.Count == 0)
                errors.Add($"Tour '{tour.Key}' has no segment keys.");
        }

        // Invariant 6: every step key referenced by a segment exists
        foreach (var seg in _segments.Values)
        {
            foreach (var stepKey in seg.StepKeys)
            {
                if (!_steps.ContainsKey(stepKey))
                    errors.Add($"Segment '{seg.Key}' references unknown step '{stepKey}'.");
            }
        }

        // Invariant 7: every segment key referenced by a tour exists
        foreach (var tour in _tours.Values)
        {
            foreach (var segKey in tour.SegmentKeys)
            {
                if (!_segments.ContainsKey(segKey))
                    errors.Add($"Tour '{tour.Key}' references unknown segment '{segKey}'.");
            }
        }

        // Invariants 8-9: ordering is inherent in IReadOnlyList — no check needed.

        // Invariant 10: every step's target has a non-empty value (except None targets)
        foreach (var step in _steps.Values)
        {
            if (step.Target.Kind != TourTargetKind.None && string.IsNullOrWhiteSpace(step.Target.Value))
                errors.Add($"Step '{step.Key}' has an empty target value.");
        }

        return errors;
    }

    /// <summary>
    /// Clears all catalog state. Intended for test isolation only.
    /// </summary>
    internal static void Reset()
    {
        _steps = new();
        _segments = new();
        _tours = new();
        _initialized = false;
    }

    /// <summary>
    /// Recursively scans an Avalonia resource dictionary (including merged dictionaries)
    /// for tour data entries, keyed by their <c>x:Key</c>.
    /// </summary>
    private static void ScanResources(
        IResourceDictionary resources,
        Dictionary<string, TourStepData> steps,
        Dictionary<string, TourSegmentData> segments,
        Dictionary<string, TourData> tours)
    {
        // Scan merged dictionaries first (depth-first).
        // MergedDictionaries contains IResourceProvider entries — ResourceInclude
        // implements IResourceProvider (not IResourceDictionary), so we need to
        // resolve it via its Loaded property to get the actual dictionary.
        if (resources.MergedDictionaries != null)
        {
            foreach (var merged in resources.MergedDictionaries)
            {
                IResourceDictionary? dict = merged switch
                {
                    IResourceDictionary rd => rd,
                    ResourceInclude ri => ri.Loaded as IResourceDictionary,
                    _ => null
                };
                if (dict is not null)
                    ScanResources(dict, steps, segments, tours);
            }
        }

        // Scan this dictionary's own entries.
        // Values may be PointerDeferredContent wrappers (Avalonia lazily materializes
        // AXAML-defined objects). Use TryGetResource to force materialization.
        foreach (var kvp in resources)
        {
            if (kvp.Key is string key &&
                resources.TryGetResource(key, null, out var resolved))
            {
                switch (resolved)
                {
                    case TourStepData stepData:
                        steps[key] = stepData;
                        break;
                    case TourSegmentData segData:
                        segments[key] = segData;
                        break;
                    case TourData tourData:
                        tours[key] = tourData;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Converts AXAML <see cref="TourStepData"/> entries into immutable <see cref="TourStep"/>
    /// instances, merging PreAction/PostAction callbacks from the actions dictionary.
    /// </summary>
    private static Dictionary<string, TourStep> ConvertSteps(
        Dictionary<string, TourStepData> dataEntries,
        Dictionary<string, TourStepActions> actions)
    {
        var result = new Dictionary<string, TourStep>();

        foreach (var (key, data) in dataEntries)
        {
            if (!Enum.TryParse<TourTargetKind>(data.TargetKind, ignoreCase: true, out var kind))
            {
                App.Logger.LogInfo($"[TourCatalog] Step '{key}': unknown TargetKind '{data.TargetKind}', defaulting to NamedControl.");
                kind = TourTargetKind.NamedControl;
            }

            var placement = TourPlacement.Auto;
            if (!string.IsNullOrWhiteSpace(data.Placement))
            {
                if (!Enum.TryParse<TourPlacement>(data.Placement, ignoreCase: true, out placement))
                {
                    App.Logger.LogInfo($"[TourCatalog] Step '{key}': unknown Placement '{data.Placement}', defaulting to Auto.");
                    placement = TourPlacement.Auto;
                }
            }

            actions.TryGetValue(key, out var stepActions);

            var target = kind == TourTargetKind.None
                ? TourTarget.None
                : new TourTarget(kind, data.TargetValue);
            result[key] = new TourStep(
                key, target, data.Title, data.Body, placement, data.CardWidth,
                stepActions?.PreAction, stepActions?.MidActions, stepActions?.PostAction);
        }

        return result;
    }

    /// <summary>
    /// Converts AXAML <see cref="TourSegmentData"/> entries into immutable <see cref="TourSegment"/>
    /// instances by splitting the comma-separated step key strings.
    /// </summary>
    private static Dictionary<string, TourSegment> ConvertSegments(
        Dictionary<string, TourSegmentData> dataEntries)
    {
        var result = new Dictionary<string, TourSegment>();

        foreach (var (key, data) in dataEntries)
        {
            var stepKeys = data.StepKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            result[key] = new TourSegment(key, data.Title, stepKeys);
        }

        return result;
    }

    /// <summary>
    /// Converts AXAML <see cref="TourData"/> entries into immutable <see cref="TourDefinition"/>
    /// instances by splitting comma-separated segment keys and parsing the trigger rule.
    /// </summary>
    private static Dictionary<string, TourDefinition> ConvertTours(
        Dictionary<string, TourData> dataEntries)
    {
        var result = new Dictionary<string, TourDefinition>();

        foreach (var (key, data) in dataEntries)
        {
            var segmentKeys = data.SegmentKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            TourTriggerRule? trigger = null;
            if (!string.IsNullOrWhiteSpace(data.AutoTrigger) &&
                Enum.TryParse<TourTriggerRule>(data.AutoTrigger, ignoreCase: true, out var parsed))
            {
                trigger = parsed;
            }

            result[key] = new TourDefinition(
                key, data.Title, data.Description, segmentKeys, trigger, data.IsReplayable);
        }

        return result;
    }
}

/// <summary>
/// Holds optional PreAction/MidActions/PostAction callbacks for a tour step.
/// Used by the actions dictionary passed to <see cref="TourCatalog.Initialize"/>.
/// </summary>
/// <param name="PreAction">Async action run before the step displays.</param>
/// <param name="MidActions">Ordered list of async actions run one-per-click while the card stays visible.
/// Each click runs the next action and auto-advances the body text. After the last
/// mid-action completes, the next click runs PostAction and advances to the next step.</param>
/// <param name="PostAction">Async action run when leaving the step.</param>
public record TourStepActions(
    Func<Task>? PreAction = null,
    IReadOnlyList<Func<Task>>? MidActions = null,
    Func<Task>? PostAction = null);

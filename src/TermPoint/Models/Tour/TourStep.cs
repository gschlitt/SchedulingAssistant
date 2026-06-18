using System.Linq;

namespace TermPoint.Models.Tour;

/// <summary>
/// The atomic unit of a tour: one moment of explanation that identifies a UI element
/// and provides content to display there. Immutable after construction.
/// </summary>
/// <remarks>
/// Steps are defined independently and referenced by key from <see cref="TourSegment"/>.
/// The same step can appear in multiple segments (uncommon — sharing at segment level
/// is the primary reuse mechanism).
/// </remarks>
public class TourStep
{
    /// <summary>
    /// Unique key across the entire step catalog (e.g. <c>"layout.section-panel"</c>).
    /// Dot-separated by convention for visual grouping; dots carry no structural meaning.
    /// </summary>
    public string Key { get; }

    /// <summary>Identifies the UI element this step points at.</summary>
    public TourTarget Target { get; }

    /// <summary>Short heading displayed in the tour card (e.g. "The Section List").</summary>
    public string Title { get; }

    /// <summary>First (or only) body message. Shorthand for <c>BodyMessages[0]</c>.</summary>
    public string Body { get; }

    /// <summary>
    /// All body messages for this step, parsed from pipe-delimited text in AXAML.
    /// Single-message steps produce a one-element list. Actions call
    /// <see cref="Services.TourRunner.AdvanceBody"/> to step through them.
    /// </summary>
    public IReadOnlyList<string> BodyMessages { get; }

    /// <summary>Preferred position of the tour card relative to the target.</summary>
    public TourPlacement Placement { get; }

    /// <summary>Card width in pixels. Defaults to 320.</summary>
    public double CardWidth { get; }

    /// <summary>
    /// Optional async action that runs before this step displays
    /// (e.g. open a flyout, expand an editor, apply a filter).
    /// </summary>
    public Func<Task>? PreAction { get; }

    /// <summary>
    /// Ordered list of async actions run one-per-click while the card stays visible.
    /// Each click runs the next action and auto-advances the body text. After the last
    /// mid-action completes, the next click runs PostAction and advances to the next step.
    /// Null or empty means the step advances on a single click.
    /// </summary>
    public IReadOnlyList<Func<Task>>? MidActions { get; }

    /// <summary>
    /// Optional async action that runs when leaving this step
    /// (e.g. close a flyout, collapse an editor, clear a filter).
    /// Always runs on dismiss/complete to avoid leaving the app in a tour-modified state.
    /// </summary>
    public Func<Task>? PostAction { get; }

    /// <summary>
    /// Creates an immutable tour step.
    /// </summary>
    /// <param name="key">Unique step key. Must not be empty.</param>
    /// <param name="target">UI element this step highlights.</param>
    /// <param name="title">Card heading text.</param>
    /// <param name="body">Card body text.</param>
    /// <param name="placement">Preferred card position relative to target.</param>
    /// <param name="cardWidth">Card width in pixels (default 320).</param>
    /// <param name="preAction">Optional setup action run before the step displays.</param>
    /// <param name="midActions">Optional ordered list of actions run one-per-click (card stays visible).</param>
    /// <param name="postAction">Optional cleanup action run when leaving the step.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    public TourStep(
        string key,
        TourTarget target,
        string title,
        string body,
        TourPlacement placement = TourPlacement.Auto,
        double cardWidth = 320,
        Func<Task>? preAction = null,
        IReadOnlyList<Func<Task>>? midActions = null,
        Func<Task>? postAction = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Step key must not be empty.", nameof(key));

        Key = key;
        Target = target;
        Title = title;
        BodyMessages = body.Split('|', StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Select(s => s.Replace("{p}", "\n\n"))
            .ToList()
            .AsReadOnly();
        Body = BodyMessages[0];
        Placement = placement;
        CardWidth = cardWidth;
        PreAction = preAction;
        MidActions = midActions;
        PostAction = postAction;
    }
}

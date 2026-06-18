using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TermPoint.Services;

namespace TermPoint.Controls;

/// <summary>
/// Visual chrome for a single workflow card. Holds the card's content as inline-markup
/// <b>strings</b> (<see cref="StoryText"/>, <see cref="StepsText"/>) rendered via
/// <see cref="Helpers.InlineFormatter"/>, plus a <see cref="Title"/> and an
/// <see cref="Accent"/> background. Authored directly in AXAML (no recompile to edit
/// card prose).
///
/// <para>In the flyout the steps collapse behind a chevron; <see cref="Clone"/> produces a
/// detached copy (steps always shown, no chevron/detach button) for a floating sticky note.
/// Because the content is plain strings, cloning is a trivial property copy — no per-card
/// control types needed.</para>
/// </summary>
public partial class WorkflowCardChrome : UserControl
{
    /// <summary>Raised (bubbling) when the user clicks the card's detach button.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> DetachRequestedEvent =
        RoutedEvent.Register<WorkflowCardChrome, RoutedEventArgs>(
            nameof(DetachRequested), RoutingStrategies.Bubble);

    public event System.EventHandler<RoutedEventArgs> DetachRequested
    {
        add => AddHandler(DetachRequestedEvent, value);
        remove => RemoveHandler(DetachRequestedEvent, value);
    }

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WorkflowCardChrome, string?>(nameof(Title));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<WorkflowCardChrome, IBrush?>(nameof(Accent));

    /// <summary>User-story line, shown in the always-visible header (inline markup).</summary>
    public static readonly StyledProperty<string?> StoryTextProperty =
        AvaloniaProperty.Register<WorkflowCardChrome, string?>(nameof(StoryText));

    /// <summary>Step-by-step body, shown when expanded/detached (inline markup, newline-separated).</summary>
    public static readonly StyledProperty<string?> StepsTextProperty =
        AvaloniaProperty.Register<WorkflowCardChrome, string?>(nameof(StepsText));

    /// <summary>Whether the steps section is currently expanded (flyout mode only).</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<WorkflowCardChrome, bool>(nameof(IsExpanded));

    /// <summary>True when shown as a floating sticky note: steps always visible, no chevron/detach.</summary>
    public static readonly StyledProperty<bool> DetachedProperty =
        AvaloniaProperty.Register<WorkflowCardChrome, bool>(nameof(Detached));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string? StoryText { get => GetValue(StoryTextProperty); set => SetValue(StoryTextProperty, value); }
    public string? StepsText { get => GetValue(StepsTextProperty); set => SetValue(StepsTextProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool Detached { get => GetValue(DetachedProperty); set => SetValue(DetachedProperty, value); }

    public WorkflowCardChrome()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    /// <summary>Creates a detached copy for a floating sticky note (steps shown, chrome hidden).</summary>
    public WorkflowCardChrome Clone() => new()
    {
        Title = Title,
        Accent = Accent,
        StoryText = StoryText,
        StepsText = StepsText,
        IsExpanded = true,
        Detached = true,
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty || change.Property == DetachedProperty)
            UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        // StepsArea / ExpandToggle / DetachButton are named elements in the AXAML.
        if (StepsArea is not null)
            StepsArea.IsVisible = IsExpanded || Detached;
        if (ExpandToggle is not null)
            ExpandToggle.IsVisible = !Detached;
        if (DetachButton is not null)
            DetachButton.IsVisible = !Detached && PlatformCapabilities.SupportsDetachedWindows;
    }

    private void OnDetachClick(object? sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(DetachRequestedEvent));
}

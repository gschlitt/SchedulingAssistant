using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.Behaviors;

/// <summary>
/// Attached behavior that provides a styled help tooltip for any <see cref="Control"/>.
///
/// Set <see cref="TextProperty"/> to show a brief description on hover. Optionally set
/// <see cref="MoreHelpUrlProperty"/> to add a "More help →" link that opens in the
/// default browser when clicked.
///
/// The tooltip appears after a short delay (600 ms) and is styled with the application
/// palette colors <c>HelpTipBackground</c> and <c>HelpTipBorder</c> from AppColors.axaml.
///
/// Usage in AXAML (add xmlns:b="using:SchedulingAssistant.Behaviors" to the root element):
///   <![CDATA[
///   <Button Content="Save"
///           b:HelpTip.Text="Saves the current semester configuration." />
///
///   <ComboBox b:HelpTip.Text="Select the academic year to display."
///             b:HelpTip.MoreHelpUrl="https://docs.example.com/academic-year" />
///   ]]>
/// </summary>
public static class HelpTip
{
    // ── Attached properties ────────────────────────────────────────────────────

    /// <summary>
    /// The brief description shown in the tooltip body.
    /// Setting this to null or whitespace removes the tooltip entirely.
    /// </summary>
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "Text", typeof(HelpTip));

    /// <summary>
    /// Alternate description shown when the control is disabled (<c>IsEnabled = false</c>).
    /// When null the normal <see cref="TextProperty"/> text is always used regardless of state.
    /// </summary>
    public static readonly AttachedProperty<string?> DisabledTextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "DisabledText", typeof(HelpTip));

    /// <summary>
    /// Optional URL opened when the user clicks "More help →" in the tooltip.
    /// When null or empty the link row is not rendered.
    /// </summary>
    public static readonly AttachedProperty<string?> MoreHelpUrlProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "MoreHelpUrl", typeof(HelpTip));

    /// <summary>
    /// Internal property to track the <c>IsEnabled</c> event handler per control,
    /// so it can be unsubscribed when <see cref="DisabledTextProperty"/> is cleared or overwritten.
    /// </summary>
    private static readonly AttachedProperty<EventHandler<AvaloniaPropertyChangedEventArgs>?>
        IsEnabledHandlerProperty =
            AvaloniaProperty.RegisterAttached<Control, EventHandler<AvaloniaPropertyChangedEventArgs>?>(
                "IsEnabledHandler", typeof(HelpTip));

    // ── Getters / setters (required by Avalonia for attached properties) ───────

    /// <summary>Gets the tooltip description text attached to <paramref name="c"/>.</summary>
    public static string? GetText(Control c) => c.GetValue(TextProperty);

    /// <summary>Sets the tooltip description text on <paramref name="c"/>.</summary>
    public static void SetText(Control c, string? value) => c.SetValue(TextProperty, value);

    /// <summary>Gets the disabled-state tooltip text attached to <paramref name="c"/>.</summary>
    public static string? GetDisabledText(Control c) => c.GetValue(DisabledTextProperty);

    /// <summary>Sets the disabled-state tooltip text on <paramref name="c"/>.</summary>
    public static void SetDisabledText(Control c, string? value) => c.SetValue(DisabledTextProperty, value);

    /// <summary>Gets the "More help" URL attached to <paramref name="c"/>.</summary>
    public static string? GetMoreHelpUrl(Control c) => c.GetValue(MoreHelpUrlProperty);

    /// <summary>Sets the "More help" URL on <paramref name="c"/>.</summary>
    public static void SetMoreHelpUrl(Control c, string? value) => c.SetValue(MoreHelpUrlProperty, value);

    // ── Static constructor: wire property-changed callbacks ───────────────────

    static HelpTip()
    {
        TextProperty.Changed.AddClassHandler<Control>(OnPropertyChanged);
        DisabledTextProperty.Changed.AddClassHandler<Control>(OnDisabledTextChanged);
        MoreHelpUrlProperty.Changed.AddClassHandler<Control>(OnPropertyChanged);
    }

    // ── Internal logic ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the tooltip whenever <see cref="TextProperty"/> or <see cref="MoreHelpUrlProperty"/> changes.
    /// </summary>
    private static void OnPropertyChanged(Control c, AvaloniaPropertyChangedEventArgs e)
        => RebuildTooltip(c);

    /// <summary>
    /// When <see cref="DisabledTextProperty"/> is set, subscribes to the control's
    /// <c>PropertyChanged</c> to detect <c>IsEnabled</c> transitions and swap tooltip text.
    /// Removes any prior handler to avoid duplicates.
    /// </summary>
    private static void OnDisabledTextChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        // Remove prior handler if any
        var oldHandler = c.GetValue(IsEnabledHandlerProperty);
        if (oldHandler is not null)
            c.PropertyChanged -= oldHandler;
        c.SetValue(IsEnabledHandlerProperty, null);

        if (e.NewValue is string s && !string.IsNullOrWhiteSpace(s))
        {
            EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, pe) =>
            {
                if (pe.Property == InputElement.IsEnabledProperty)
                    RebuildTooltip(c);
            };
            c.PropertyChanged += handler;
            c.SetValue(IsEnabledHandlerProperty, handler);
        }

        RebuildTooltip(c);
    }

    /// <summary>
    /// Constructs and assigns a styled <see cref="ToolTip"/> to <paramref name="c"/>,
    /// or removes any existing tooltip when <see cref="TextProperty"/> is empty.
    /// </summary>
    private static void RebuildTooltip(Control c)
    {
        var disabledText = GetDisabledText(c);
        var text = !c.IsEnabled && !string.IsNullOrWhiteSpace(disabledText)
            ? disabledText
            : GetText(c);

        if (string.IsNullOrWhiteSpace(text))
        {
            ToolTip.SetTip(c, null);
            return;
        }

        var url = GetMoreHelpUrl(c);

        // ── Content panel ──────────────────────────────────────────────────────

        var panel = new StackPanel
        {
            Spacing  = 6,
            MaxWidth = 260,
        };

        // Description body text — wraps if long
        panel.Children.Add(new TextBlock
        {
            Text         = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground   = Brush("TextPrimary"),
            FontSize     = FontSize("FontSizeNormal"),
        });

        // "More help →" link row — only rendered when a URL is provided
        if (!string.IsNullOrWhiteSpace(url))
        {
            var link = new TextBlock
            {
                Text                 = "More help →",
                Foreground           = Brush("ToolTipTextBrush"),
                FontSize             = FontSize("FontSizeNormal"),
                HorizontalAlignment  = HorizontalAlignment.Right,
                Cursor               = new Cursor(StandardCursorType.Hand),
                TextDecorations      = TextDecorations.Underline,
            };

            // Capture url into a local so the lambda doesn't close over a mutable variable
            var capturedUrl = url;
            link.PointerPressed += (_, _) =>
            {
                try
                {
                    PlatformProcess.OpenUri(capturedUrl);
                }
                catch
                {
                    // Silently swallow — browser may be unavailable (e.g. sandboxed environment)
                }
            };

            panel.Children.Add(link);
        }

        // ── Styled tooltip shell ───────────────────────────────────────────────

        var tooltip = new ToolTip
        {
            Background      = Brush("HelpTipBackground"),
            BorderBrush     = Brush("HelpTipBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 7),
            Content         = panel,
        };

        ToolTip.SetTip(c, tooltip);
        ToolTip.SetShowDelay(c, 900);
    }

    // ── Resource helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Looks up a named brush from the application resource dictionary.
    /// Returns <see cref="Brushes.Transparent"/> if the key is not found.
    /// </summary>
    private static IBrush Brush(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : Brushes.Transparent;

    /// <summary>
    /// Looks up a named font-size double from the application resource dictionary.
    /// Returns 12 as a safe fallback if the key is not found.
    /// </summary>
    private static double FontSize(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is double d
            ? d : 12;
}

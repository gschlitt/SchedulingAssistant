using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using TermPoint.Services;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior that provides a styled, hoverable help tooltip for any <see cref="Control"/>.
///
/// Set <see cref="TextProperty"/> to show a brief description on hover. Optionally set
/// <see cref="MoreHelpUrlProperty"/> to add a "More help →" link that opens in the
/// default browser when clicked.
///
/// <see cref="MoreHelpUrlProperty"/> accepts either a full external URL (starting with
/// "http") or a bare file name (e.g. <c>"access.html"</c>), which is resolved against the
/// application's <c>Help/</c> folder the same way <c>HelpViewModel.OpenArticle</c> does —
/// an absolute path on desktop, a relative <c>Help/</c> URL on WASM.
///
/// Unlike Avalonia's built-in <see cref="ToolTip"/> — which closes the instant the pointer
/// leaves the owning control, making its content unreachable — this behavior manages its
/// own <see cref="Popup"/> and keeps it open while the pointer is over either the owner or
/// the popup itself, with a short grace period between the two, so the "More help →" link
/// can actually be reached and clicked.
///
/// The tooltip appears after a short delay (900 ms) and is styled with the application
/// palette colors <c>HelpTipBackground</c> and <c>HelpTipBorder</c> from AppColors.axaml.
///
/// Usage in AXAML (add xmlns:b="using:TermPoint.Behaviors" to the root element):
///   <![CDATA[
///   <Button Content="Save"
///           b:HelpTip.Text="Saves the current semester configuration." />
///
///   <ComboBox b:HelpTip.Text="Select the academic year to display."
///             b:HelpTip.MoreHelpUrl="https://docs.example.com/academic-year" />
///
///   <TextBlock Text="Access"
///              b:HelpTip.Text="'Watches' which can advise you if student access is affected by a scheduling decision."
///              b:HelpTip.MoreHelpUrl="access.html" />
///   ]]>
/// </summary>
public static class HelpTip
{
    private const int ShowDelayMs = 630;
    private const int HideGraceMs = 250;

    // ── Attached properties ────────────────────────────────────────────────────

    /// <summary>
    /// The brief description shown in the tooltip body.
    /// Setting this to null or whitespace removes the tooltip entirely.
    /// </summary>
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "Text", typeof(HelpTip));

    /// <summary>
    /// Optional URL opened when the user clicks "More help →" in the tooltip.
    /// When null or empty the link row is not rendered.
    /// </summary>
    public static readonly AttachedProperty<string?> MoreHelpUrlProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "MoreHelpUrl", typeof(HelpTip));

    // ── Getters / setters (required by Avalonia for attached properties) ───────

    /// <summary>Gets the tooltip description text attached to <paramref name="c"/>.</summary>
    public static string? GetText(Control c) => c.GetValue(TextProperty);

    /// <summary>Sets the tooltip description text on <paramref name="c"/>.</summary>
    public static void SetText(Control c, string? value) => c.SetValue(TextProperty, value);

    /// <summary>Gets the "More help" URL attached to <paramref name="c"/>.</summary>
    public static string? GetMoreHelpUrl(Control c) => c.GetValue(MoreHelpUrlProperty);

    /// <summary>Sets the "More help" URL on <paramref name="c"/>.</summary>
    public static void SetMoreHelpUrl(Control c, string? value) => c.SetValue(MoreHelpUrlProperty, value);

    // ── Static constructor: wire property-changed callbacks ───────────────────

    static HelpTip()
    {
        TextProperty.Changed.AddClassHandler<Control>(OnPropertyChanged);
        MoreHelpUrlProperty.Changed.AddClassHandler<Control>(OnPropertyChanged);
    }

    // ── Per-control state ──────────────────────────────────────────────────────

    /// <summary>
    /// Tracks the popup and pointer/timer state for one owner control. Held in a
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> so entries are collected
    /// automatically when the owning control is.
    /// </summary>
    private sealed class TipState
    {
        public Popup? Popup;
        public DispatcherTimer? ShowTimer;
        public DispatcherTimer? HideTimer;
        public bool PointerOverOwner;
        public bool PointerOverPopup;
    }

    private static readonly ConditionalWeakTable<Control, TipState> States = [];

    // ── Internal logic ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wires up pointer tracking the first time either attached property is set on a
    /// control, or closes an already-open popup when the text is cleared.
    /// </summary>
    private static void OnPropertyChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GetText(c)))
        {
            if (States.TryGetValue(c, out var existing) && existing.Popup is { } p)
                p.IsOpen = false;
            return;
        }

        EnsureWired(c);
    }

    /// <summary>
    /// Attaches pointer-enter/exit handlers to the owner control, once. Safe to call
    /// repeatedly — later calls are no-ops.
    /// </summary>
    private static void EnsureWired(Control c)
    {
        if (States.TryGetValue(c, out _)) return;

        var state = new TipState();
        States.Add(c, state);

        c.PointerEntered += (_, _) => OnOwnerPointerEntered(c, state);
        c.PointerExited += (_, _) => OnOwnerPointerExited(state);
    }

    private static void OnOwnerPointerEntered(Control c, TipState state)
    {
        state.PointerOverOwner = true;
        state.HideTimer?.Stop();

        if (string.IsNullOrWhiteSpace(GetText(c))) return;

        state.ShowTimer?.Stop();
        state.ShowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowDelayMs) };
        state.ShowTimer.Tick += (_, _) =>
        {
            state.ShowTimer!.Stop();
            ShowPopup(c, state);
        };
        state.ShowTimer.Start();
    }

    private static void OnOwnerPointerExited(TipState state)
    {
        state.PointerOverOwner = false;
        state.ShowTimer?.Stop();
        ScheduleHide(state);
    }

    /// <summary>
    /// Starts (or restarts) the hide-grace timer. The popup only actually closes once
    /// the timer elapses with the pointer still off both the owner and the popup —
    /// this is what lets the user move the mouse from the owner into the popup without
    /// it disappearing first.
    /// </summary>
    private static void ScheduleHide(TipState state)
    {
        state.HideTimer?.Stop();
        state.HideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HideGraceMs) };
        state.HideTimer.Tick += (_, _) =>
        {
            state.HideTimer!.Stop();
            if (!state.PointerOverOwner && !state.PointerOverPopup && state.Popup is { } p)
                p.IsOpen = false;
        };
        state.HideTimer.Start();
    }

    private static void ShowPopup(Control c, TipState state)
    {
        state.Popup ??= BuildPopup(c, state);
        RebuildContent(c, state.Popup, state);
        state.Popup.IsOpen = true;
    }

    /// <summary>
    /// Creates the popup shell once per owner control and wires its own pointer
    /// tracking so hovering into the popup cancels any pending hide.
    ///
    /// Avalonia requires a <see cref="Popup"/> to be part of the visual tree before
    /// it can open — unlike WPF, setting <see cref="Popup.PlacementTarget"/> alone is
    /// not sufficient. We walk up from the owner control and add the popup to the
    /// nearest <see cref="Panel"/> ancestor. <see cref="Popup"/> measures to zero, so
    /// it does not affect the panel's layout.
    /// </summary>
    private static Popup BuildPopup(Control c, TipState state)
    {
        var popup = new Popup
        {
            PlacementTarget = c,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 0,
            VerticalOffset = 4,
            IsLightDismissEnabled = false,
        };

        for (var p = c.Parent; p != null; p = (p as Control)?.Parent)
        {
            if (p is Panel panel)
            {
                panel.Children.Add(popup);
                break;
            }
        }

        // Pointer events are wired on the child Border in RebuildContent, not here.
        // The Popup control itself has zero rendered area (content lives in a
        // separate PopupRoot), so PointerEntered/Exited on the Popup never fire.

        return popup;
    }

    /// <summary>
    /// (Re)builds the popup's content from the owner's current <see cref="TextProperty"/>
    /// and <see cref="MoreHelpUrlProperty"/> values.
    /// </summary>
    private static void RebuildContent(Control c, Popup popup, TipState state)
    {
        var text = GetText(c) ?? string.Empty;
        var url = GetMoreHelpUrl(c);

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
                    OpenMoreHelp(capturedUrl);
                }
                catch
                {
                    // Silently swallow — browser may be unavailable (e.g. sandboxed environment)
                }
                finally
                {
                    popup.IsOpen = false;
                }
            };

            panel.Children.Add(link);
        }

        var border = new Border
        {
            Background      = Brush("HelpTipBackground"),
            BorderBrush     = Brush("HelpTipBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 7),
            Child           = panel,
        };

        // Pointer tracking lives on the Border, not the Popup: the Popup control
        // itself has zero rendered area (its content is hosted in a separate
        // PopupRoot window), so PointerEntered/Exited on the Popup never fire.
        border.PointerEntered += (_, _) =>
        {
            state.PointerOverPopup = true;
            state.HideTimer?.Stop();
        };
        border.PointerExited += (_, _) =>
        {
            state.PointerOverPopup = false;
            ScheduleHide(state);
        };

        popup.Child = border;
    }

    /// <summary>
    /// Opens the "More help →" target. A value starting with "http" is treated as an
    /// external URL and opened directly. Anything else is treated as a bare file name
    /// inside the application's <c>Help/</c> folder — resolved to an absolute path on
    /// desktop, or a relative <c>Help/</c> URL on WASM, mirroring
    /// <c>HelpViewModel.OpenArticle</c>.
    /// </summary>
    /// <param name="target">An external URL or a bare Help/ file name (e.g. "access.html").</param>
    private static void OpenMoreHelp(string target)
    {
        if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            PlatformProcess.OpenUri(target);
            return;
        }

#if BROWSER
        PlatformProcess.OpenLocalFile("Help/" + target);
#else
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Help", target);
        PlatformProcess.OpenLocalFile(path);
#endif
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

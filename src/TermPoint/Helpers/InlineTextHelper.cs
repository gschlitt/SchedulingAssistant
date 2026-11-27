using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TermPoint.Services;

namespace TermPoint.Helpers
{
    /// <summary>
    /// Attached behavior that renders a lightweight inline-markup string into a
    /// <see cref="TextBlock"/>'s <c>Inlines</c>. Bind a string to
    /// <c>InlineFormatter.Text</c> instead of <c>TextBlock.Text</c>.
    ///
    /// <para>The delimiters are chosen so the markup can be authored directly in AXAML
    /// attribute values with no XML escaping:</para>
    /// <list type="bullet">
    ///   <item><c>**text**</c> — bold.</item>
    ///   <item><c>*text*</c> — italic (one asterisk each side, not part of a <c>**</c>).</item>
    ///   <item><c>[[text]]</c> or <c>[[text|color]]</c> — a rounded colored "pill" badge.
    ///         color = <c>accent</c>(default)/<c>info</c>/<c>warn</c>/<c>success</c>/<c>neutral</c> or a <c>#hex</c>.</item>
    ///   <item><c>[label](url)</c> — an underlined link; click opens the URL via the OS handler. A target that
    ///         isn't a web URL is an in-app link: plain text that underlines on hover, its click sent to
    ///         <c>InlineFormatter.LinkCommand</c>.</item>
    ///   <item><c>{color}…{/}</c> — legacy colored text (named color or <c>#hex</c>); still used elsewhere.</item>
    /// </list>
    ///
    /// <para>Markup does not nest. Pills and web links render as atomic inline boxes
    /// (they do not wrap across a line break), which suits the short phrases they target.</para>
    /// </summary>
    public static class InlineFormatter
    {
        /// <summary>The markup string to render into the target <see cref="TextBlock"/>.</summary>
        public static readonly AttachedProperty<string> TextProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, string>(
                "Text",
                typeof(InlineFormatter));

        public static string GetText(TextBlock element) =>
            element.GetValue(TextProperty);

        public static void SetText(TextBlock element, string value) =>
            element.SetValue(TextProperty, value);

        /// <summary>
        /// A command that receives a link's target when a link is clicked, instead of the OS opening it. Set it on a
        /// TextBlock whose links are in-app actions (the Access detail's section names, <c>[CHEM 213 A](section:id)</c>).
        /// Such links are drawn as the surrounding text (its colour and weight), underlined only while the pointer is
        /// over them (the r9 wireframe's <c>sref</c>), because a run of blue underlined names would swamp a sentence.
        /// Read when a link is clicked, so the binding may arrive after the text.
        /// </summary>
        public static readonly AttachedProperty<System.Windows.Input.ICommand?> LinkCommandProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, System.Windows.Input.ICommand?>(
                "LinkCommand",
                typeof(InlineFormatter));

        /// <summary>Gets the command that receives clicked links' targets.</summary>
        /// <param name="element">The TextBlock.</param>
        /// <returns>The command, or null when links open in the OS.</returns>
        public static System.Windows.Input.ICommand? GetLinkCommand(TextBlock element) =>
            element.GetValue(LinkCommandProperty);

        /// <summary>Sets the command that receives clicked links' targets.</summary>
        /// <param name="element">The TextBlock.</param>
        /// <param name="value">The command, or null to open links in the OS.</param>
        public static void SetLinkCommand(TextBlock element, System.Windows.Input.ICommand? value) =>
            element.SetValue(LinkCommandProperty, value);

        // One pass over the text, matching any supported token. Ordered alternation;
        // pill (`[[ ]]`) is tried before link (`[ ]( )`) so the double-bracket wins.
        // The break token matches a real newline OR a literal [br], eating the
        // surrounding horizontal whitespace XAML leaves around it.
        private static readonly Regex TokenRegex = new(
            @"(?<brk>[ \t]*(?:\n|\[br\])[ \t]*)" +
            @"|(?<bold>\*\*(?<bcontent>.+?)\*\*)" +
            @"|(?<ital>(?<!\*)\*(?<icontent>[^*\n]+?)\*(?!\*))" +
            @"|(?<pill>\[\[(?<pcontent>[^\]|]+?)(?:\|(?<pcolor>[^\]]+?))?\]\])" +
            @"|(?<link>\[(?<lcontent>[^\]]+?)\]\((?<href>[^)]+?)\))" +
            @"|(?<color>\{(?<cname>[^{}]*?)\}(?<ccontent>.*?)\{/\})",
            RegexOptions.Singleline);

        static InlineFormatter()
        {
            TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) =>
            {
                tb.Inlines?.Clear();

                var text = e.NewValue as string;
                if (string.IsNullOrEmpty(text) || tb.Inlines is null)
                    return;

                var lastIndex = 0;

                foreach (Match m in TokenRegex.Matches(text))
                {
                    // Plain text preceding this token.
                    if (m.Index > lastIndex)
                        tb.Inlines.Add(new Run { Text = text.Substring(lastIndex, m.Index - lastIndex) });

                    if (m.Groups["brk"].Success)
                    {
                        tb.Inlines.Add(new LineBreak());
                    }
                    else if (m.Groups["bold"].Success)
                    {
                        tb.Inlines.Add(new Run
                        {
                            Text = m.Groups["bcontent"].Value,
                            FontWeight = FontWeight.Bold
                        });
                    }
                    else if (m.Groups["ital"].Success)
                    {
                        tb.Inlines.Add(new Run
                        {
                            Text = m.Groups["icontent"].Value,
                            FontStyle = FontStyle.Italic
                        });
                    }
                    else if (m.Groups["pill"].Success)
                    {
                        tb.Inlines.Add(BuildPill(m.Groups["pcontent"].Value, m.Groups["pcolor"].Value));
                    }
                    else if (m.Groups["link"].Success)
                    {
                        string href = m.Groups["href"].Value, label = m.Groups["lcontent"].Value;
                        if (IsWebLink(href))
                            tb.Inlines.Add(BuildLink(label, href));
                        else
                        {
                            tb.Inlines.Add(new AppLinkRun { Text = label, Target = href });
                            HookAppLinks(tb);
                        }
                    }
                    else if (m.Groups["color"].Success)
                    {
                        tb.Inlines.Add(new Run
                        {
                            Text = m.Groups["ccontent"].Value,
                            Foreground = ParseColorBrush(m.Groups["cname"].Value)
                        });
                    }

                    lastIndex = m.Index + m.Length;
                }

                // Trailing plain text.
                if (lastIndex < text.Length)
                    tb.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
            });
        }

        /// <summary>Builds a rounded colored pill badge as an inline box.</summary>
        private static InlineUIContainer BuildPill(string content, string color)
        {
            var (bg, fg) = ResolvePillColors(color);

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                // Symmetric, tight padding so the pill height tracks the text height
                // (vertical alignment is governed by the InlineUIContainer below).
                Padding = new Thickness(5, 0),
                Background = bg,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = content,
                    Foreground = fg,
                    // No explicit FontSize: inherit the host TextBlock's size so pill
                    // text matches the surrounding prose.
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            return new InlineUIContainer(border) { BaselineAlignment = BaselineAlignment.TextBottom };
        }

        /// <summary>True for a web link's target ("://" or "mailto:"); any other target is an in-app link.</summary>
        /// <param name="url">The link's target.</param>
        /// <returns>Whether the OS should open it.</returns>
        private static bool IsWebLink(string url) =>
            url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Builds a web link: blue and underlined, and a click opens it via the OS default handler (gated by
        /// <see cref="PlatformCapabilities.SupportsLinks"/>), so AXAML-authored cards need no command wiring.
        /// </summary>
        /// <param name="label">The link's text.</param>
        /// <param name="url">The link's target.</param>
        /// <returns>The link as an inline box.</returns>
        private static InlineUIContainer BuildLink(string label, string url)
        {
            var link = new TextBlock
            {
                Text = label,
                Foreground = GetResourceBrush("WorkflowLinkText") ?? Brushes.Blue,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center
            };

            link.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                if (!PlatformCapabilities.SupportsLinks || string.IsNullOrWhiteSpace(url))
                    return;
                try
                {
                    PlatformProcess.OpenUri(url);
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, $"Failed to open inline link {url}");
                }
            };

            return new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.Center };
        }

        /// <summary>
        /// An in-app link (any target that isn't a web link): a plain Run, so it takes the host's colour, size and
        /// weight and sits exactly on the line's baseline (an inline box drifts off it). The host finds it by
        /// hit-testing its text (<see cref="AppLinkAt"/>): a hand cursor and an underline while the pointer is over it,
        /// and a click hands <see cref="Target"/> to the host's <see cref="LinkCommandProperty"/>.
        /// </summary>
        private sealed class AppLinkRun : Run
        {
            /// <summary>The link's target, e.g. <c>section:id</c>.</summary>
            public string Target { get; init; } = string.Empty;
        }

        /// <summary>Marks a TextBlock whose pointer handlers for in-app links are already attached.</summary>
        private static readonly AttachedProperty<bool> AppLinksHookedProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, bool>("AppLinksHooked", typeof(InlineFormatter));

        /// <summary>Attaches the pointer handlers that make the TextBlock's in-app links work (once per TextBlock).</summary>
        /// <param name="tb">The host TextBlock.</param>
        private static void HookAppLinks(TextBlock tb)
        {
            if (tb.GetValue(AppLinksHookedProperty)) return;
            tb.SetValue(AppLinksHookedProperty, true);
            tb.PointerMoved += (_, e) => ShowHover(tb, AppLinkAt(tb, e.GetPosition(tb)));
            tb.PointerExited += (_, _) => ShowHover(tb, null);
            tb.PointerPressed += (_, e) =>
            {
                if (AppLinkAt(tb, e.GetPosition(tb)) is not { } link) return;
                e.Handled = true;
                var command = GetLinkCommand(tb);
                if (command?.CanExecute(link.Target) == true) command.Execute(link.Target);
            };
        }

        /// <summary>Underlines the hovered in-app link (and no other) and shows a hand cursor over it.</summary>
        /// <param name="tb">The host TextBlock.</param>
        /// <param name="hovered">The link under the pointer, or null.</param>
        private static void ShowHover(TextBlock tb, AppLinkRun? hovered)
        {
            if (tb.Inlines is null) return;
            foreach (var link in tb.Inlines.OfType<AppLinkRun>())
            {
                // ClearValue, not null: a decoration the host's style sets (a dotted underline) comes back
                if (link == hovered) link.TextDecorations = TextDecorations.Underline;
                else link.ClearValue(Inline.TextDecorationsProperty);
            }
            if (hovered is not null) tb.Cursor = new Cursor(StandardCursorType.Hand);
            else tb.ClearValue(InputElement.CursorProperty);
        }

        /// <summary>The in-app link under a point of the TextBlock, if any.</summary>
        /// <param name="tb">The host TextBlock.</param>
        /// <param name="point">The point, relative to the TextBlock.</param>
        /// <returns>The link, or null.</returns>
        private static AppLinkRun? AppLinkAt(TextBlock tb, Point point)
        {
            if (tb.Inlines is null) return null;
            var hit = tb.TextLayout.HitTestPoint(new Point(point.X - tb.Padding.Left, point.Y - tb.Padding.Top));
            if (!hit.IsInside) return null;
            int offset = 0;
            foreach (var inline in tb.Inlines)
            {
                // Each inline's length in the laid-out text: a Run its text, a box one character, a line break a newline
                int length = inline switch
                {
                    Run r => r.Text?.Length ?? 0,
                    LineBreak => Environment.NewLine.Length,
                    _ => 1,
                };
                if (hit.TextPosition >= offset && hit.TextPosition < offset + length) return inline as AppLinkRun;
                offset += length;
            }
            return null;
        }

        /// <summary>
        /// Maps a pill color spec to (background, foreground) brushes.
        /// Empty/unknown → default accent; a named palette entry → its theme keys;
        /// a <c>#hex</c> → that background with primary text.
        /// </summary>
        private static (IBrush bg, IBrush fg) ResolvePillColors(string color)
        {
            if (!string.IsNullOrWhiteSpace(color) && color.StartsWith("#", StringComparison.Ordinal))
            {
                try
                {
                    return (new SolidColorBrush(Color.Parse(color)),
                            GetResourceBrush("TextPrimary") ?? Brushes.Black);
                }
                catch
                {
                    // fall through to default
                }
            }

            var (bgKey, fgKey) = color?.ToLowerInvariant() switch
            {
                "info" => ("NotificationInfoBackground", "NotificationInfoText"),
                "warn" or "warning" => ("NotificationWarningBackground", "NotificationWarningText"),
                "success" => ("WorkflowPillSuccessBackground", "WorkflowPillSuccessText"),
                "neutral" => ("SubtleBackground", "TextFaint"),
                _ => ("WorkflowPillBackground", "WorkflowPillText"),
            };

            return (GetResourceBrush(bgKey) ?? Brushes.LightGray,
                    GetResourceBrush(fgKey) ?? Brushes.Black);
        }

        /// <summary>Parses the legacy {color} name or #hex into a foreground brush (red fallback).</summary>
        private static IBrush ParseColorBrush(string colorText)
        {
            try
            {
                if (colorText.StartsWith("#", StringComparison.Ordinal))
                    return new SolidColorBrush(Color.Parse(colorText));

                var prop = typeof(Brushes).GetProperty(colorText,
                    System.Reflection.BindingFlags.IgnoreCase |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);

                if (prop?.GetValue(null) is IBrush brush)
                    return brush;
            }
            catch
            {
                // fall through
            }

            return Brushes.Red;
        }

        private static IBrush? GetResourceBrush(string key) =>
            Application.Current?.TryFindResource(key, out var v) == true && v is IBrush b ? b : null;
    }
}

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
    ///   <item><c>[[text]]</c> or <c>[[text|color]]</c> — a rounded colored "pill" badge.
    ///         color = <c>accent</c>(default)/<c>info</c>/<c>warn</c>/<c>success</c>/<c>neutral</c> or a <c>#hex</c>.</item>
    ///   <item><c>[label](url)</c> — an underlined link; click opens the URL via the OS handler.</item>
    ///   <item><c>{color}…{/}</c> — legacy colored text (named color or <c>#hex</c>); still used elsewhere.</item>
    /// </list>
    ///
    /// <para>Markup does not nest. Pills and links render as atomic inline boxes
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

        // One pass over the text, matching any supported token. Ordered alternation;
        // pill (`[[ ]]`) is tried before link (`[ ]( )`) so the double-bracket wins.
        // The break token matches a real newline OR a literal [br], eating the
        // surrounding horizontal whitespace XAML leaves around it.
        private static readonly Regex TokenRegex = new(
            @"(?<brk>[ \t]*(?:\n|\[br\])[ \t]*)" +
            @"|(?<bold>\*\*(?<bcontent>.+?)\*\*)" +
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
                    else if (m.Groups["pill"].Success)
                    {
                        tb.Inlines.Add(BuildPill(m.Groups["pcontent"].Value, m.Groups["pcolor"].Value));
                    }
                    else if (m.Groups["link"].Success)
                    {
                        tb.Inlines.Add(BuildLink(m.Groups["lcontent"].Value, m.Groups["href"].Value));
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

        /// <summary>
        /// Builds an underlined inline link. Clicking opens the URL via the OS default
        /// handler (gated by <see cref="PlatformCapabilities.SupportsLinks"/>); self-contained,
        /// so AXAML-authored cards need no command wiring.
        /// </summary>
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

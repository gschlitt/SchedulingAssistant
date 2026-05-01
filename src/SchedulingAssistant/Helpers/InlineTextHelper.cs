using System;
using System.Collections.Generic;
using System.Text;

namespace SchedulingAssistant.Helpers
{
    using System;
    using System.Text.RegularExpressions;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Documents;
    using Avalonia.Media;

    public static class InlineFormatter
    {
        public static readonly AttachedProperty<string> TextProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, string>(
                "Text",
                typeof(InlineFormatter));

        public static string GetText(TextBlock element) =>
            element.GetValue(TextProperty);

        public static void SetText(TextBlock element, string value) =>
            element.SetValue(TextProperty, value);

        static InlineFormatter()
        {
            TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) =>
            {
                tb.Inlines?.Clear();

                var text = e.NewValue as string;
                if (string.IsNullOrEmpty(text))
                    return;

                // Matches {color}text{/}
                var regex = new Regex(@"\{(.*?)\}(.*?)\{\/\}", RegexOptions.IgnoreCase);
                int lastIndex = 0;

                foreach (Match match in regex.Matches(text))
                {
                    // Add normal text before match
                    if (match.Index > lastIndex)
                    {
                        tb.Inlines.Add(new Run
                        {
                            Text = text.Substring(lastIndex, match.Index - lastIndex)
                        });
                    }

                    var colorText = match.Groups[1].Value;
                    var content = match.Groups[2].Value;

                    // Try to parse color
                    IBrush brush = Brushes.Red; // fallback

                    try
                    {
                        if (colorText.StartsWith("#"))
                        {
                            brush = new SolidColorBrush(Color.Parse(colorText));
                        }
                        else
                        {
                            var prop = typeof(Brushes).GetProperty(colorText,
                                System.Reflection.BindingFlags.IgnoreCase |
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.Static);

                            if (prop != null)
                                brush = (IBrush)prop.GetValue(null);
                        }
                    }
                    catch
                    {
                        // fallback stays red
                    }

                    tb.Inlines.Add(new Run
                    {
                        Text = content,
                        Foreground = brush
                    });

                    lastIndex = match.Index + match.Length;
                }

                // Remaining text
                if (lastIndex < text.Length)
                {
                    tb.Inlines.Add(new Run
                    {
                        Text = text.Substring(lastIndex)
                    });
                }
            });
        }
    }
}


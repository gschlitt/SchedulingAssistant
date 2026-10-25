using System.Net;
using System.Text;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Builds the web page that "Save instructions…" writes: the section import instructions as one
/// self-contained HTML5 document (headings, paragraphs and bullet lists, with a little inline CSS), readable
/// in any browser and printable. Pure and free of platform code, so it is compiled in every build and unit-tested.
/// </summary>
/// <remarks>
/// This is an exported document, not Avalonia UI, so the app's rule that fonts and colours come from
/// <c>AppColors.axaml</c> and <c>App.axaml</c> does not apply to its CSS. The CSS stays minimal on purpose.
/// </remarks>
public static class ImportInstructionsHtml
{
    /// <summary>The page's title (the browser tab) and its top heading.</summary>
    public const string PageTitle = "Importing sections into TermPoint";

    /// <summary>
    /// The page's CSS: a system sans-serif font, a line length of about 46 em, a comfortable line height,
    /// modest heading sizes, plain dark text on white, and nothing that would spoil printing.
    /// </summary>
    private const string Css =
        "body { font-family: system-ui, -apple-system, \"Segoe UI\", Roboto, Helvetica, Arial, sans-serif;" +
        " color: #1a1a1a; background: #ffffff; line-height: 1.55; max-width: 46em; margin: 2em auto; padding: 0 1em; }\n" +
        "h1 { font-size: 1.6em; margin: 0 0 0.8em; }\n" +
        "h2 { font-size: 1.2em; margin: 1.6em 0 0.4em; }\n" +
        "p { margin: 0.6em 0; }\n" +
        "ul { margin: 0.6em 0; padding-left: 1.4em; }\n" +
        "li { margin: 0.3em 0; }\n" +
        "@media print { body { max-width: none; margin: 0; padding: 0; } h2 { break-after: avoid; } li, p { break-inside: avoid; } }\n";

    /// <summary>
    /// Builds the complete HTML document. The title becomes the <c>&lt;h1&gt;</c>; each heading becomes an
    /// <c>&lt;h2&gt;</c>; each paragraph a <c>&lt;p&gt;</c>; and each run of consecutive bullets one <c>&lt;ul&gt;</c> of
    /// <c>&lt;li&gt;</c> items. Every piece of text is HTML-encoded, so characters such as <c>&lt;</c> and <c>&amp;</c> are safe.
    /// </summary>
    /// <param name="blocks">The instructions, as <see cref="ImportInstructions.Parse"/> returns them.</param>
    /// <returns>The document text, starting with <c>&lt;!DOCTYPE html&gt;</c>; use UTF-8 when writing it (it declares that charset).</returns>
    public static string Build(IReadOnlyList<InstructionBlock> blocks)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n");
        html.Append("<html lang=\"en\">\n");
        html.Append("<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Encode(PageTitle)).Append("</title>\n");
        html.Append("<style>\n").Append(Css).Append("</style>\n");
        html.Append("</head>\n");
        html.Append("<body>\n");
        html.Append("<h1>").Append(Encode(PageTitle)).Append("</h1>\n");

        // True while a <ul> is open, so a run of bullets shares one list and any other block closes it.
        var inList = false;
        foreach (var block in blocks)
        {
            if (block.IsBullet)
            {
                if (!inList)
                {
                    html.Append("<ul>\n");
                    inList = true;
                }
                html.Append("<li>").Append(Encode(block.Text)).Append("</li>\n");
                continue;
            }

            if (inList)
            {
                html.Append("</ul>\n");
                inList = false;
            }

            var tag = block.IsHeading ? "h2" : "p";
            html.Append('<').Append(tag).Append('>').Append(Encode(block.Text)).Append("</").Append(tag).Append(">\n");
        }

        if (inList) html.Append("</ul>\n");

        html.Append("</body>\n");
        html.Append("</html>\n");
        return html.ToString();
    }

    /// <summary>HTML-encodes a piece of text (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and quotes).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The encoded text.</returns>
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}

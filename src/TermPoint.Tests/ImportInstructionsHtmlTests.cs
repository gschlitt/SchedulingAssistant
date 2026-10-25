using System.Text.RegularExpressions;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="ImportInstructionsHtml.Build"/>, the web page "Save instructions…" writes: a complete
/// HTML5 document, every piece of text encoded, and one list per run of bullets.
/// </summary>
public sealed class ImportInstructionsHtmlTests
{
    /// <summary>Shorthand for a heading block.</summary>
    private static InstructionBlock H(string text) => new(InstructionBlockKind.Heading, text);

    /// <summary>Shorthand for a paragraph block.</summary>
    private static InstructionBlock P(string text) => new(InstructionBlockKind.Paragraph, text);

    /// <summary>Shorthand for a bullet block.</summary>
    private static InstructionBlock L(string text) => new(InstructionBlockKind.Bullet, text);

    /// <summary>Counts the non-overlapping occurrences of a piece of text.</summary>
    private static int Count(string html, string part) => Regex.Matches(html, Regex.Escape(part)).Count;

    /// <summary>The page is a complete HTML5 document with the charset, the title and the top heading.</summary>
    [Fact]
    public void Build_IsACompleteHtml5Document()
    {
        var html = ImportInstructionsHtml.Build(new[] { H("A heading") });

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<title>Importing sections into TermPoint</title>", html);
        Assert.Contains("<h1>Importing sections into TermPoint</h1>", html);
        Assert.Contains("<style>", html);
        Assert.Contains("</html>", html);
    }

    /// <summary>A heading becomes an h2 and a paragraph a p, in order.</summary>
    [Fact]
    public void Build_HeadingAndParagraph_BecomeH2AndP()
    {
        var html = ImportInstructionsHtml.Build(new[] { H("The columns"), P("Every column must be in the file.") });

        Assert.Contains("<h2>The columns</h2>", html);
        Assert.Contains("<p>Every column must be in the file.</p>", html);
        Assert.True(html.IndexOf("<h2>", StringComparison.Ordinal) < html.IndexOf("<p>", StringComparison.Ordinal));
    }

    /// <summary>Angle brackets, ampersands and quotes in the text are encoded, so they can't break the page.</summary>
    [Fact]
    public void Build_EncodesSpecialCharactersInEveryKindOfBlock()
    {
        var html = ImportInstructionsHtml.Build(new[]
        {
            H("A <b>bold</b> & \"quoted\" heading"),
            P("1 < 2 && 3 > 2"),
            L("Use \"M,W\" & <i>not</i> 'x'"),
        });

        Assert.Contains("<h2>A &lt;b&gt;bold&lt;/b&gt; &amp; &quot;quoted&quot; heading</h2>", html);
        Assert.Contains("<p>1 &lt; 2 &amp;&amp; 3 &gt; 2</p>", html);
        Assert.Contains("<li>Use &quot;M,W&quot; &amp; &lt;i&gt;not&lt;/i&gt; &#39;x&#39;</li>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<i>", html);
    }

    /// <summary>Three bullets in a row make one list with three items.</summary>
    [Fact]
    public void Build_ConsecutiveBullets_AreOneList()
    {
        var html = ImportInstructionsHtml.Build(new[] { L("one"), L("two"), L("three") });

        Assert.Equal(1, Count(html, "<ul>"));
        Assert.Equal(1, Count(html, "</ul>"));
        Assert.Equal(3, Count(html, "<li>"));
        Assert.Matches(new Regex(@"<ul>\s*<li>one</li>\s*<li>two</li>\s*<li>three</li>\s*</ul>"), html);
    }

    /// <summary>Bullets with a paragraph between them make two lists.</summary>
    [Fact]
    public void Build_BulletsSeparatedByAParagraph_AreTwoLists()
    {
        var html = ImportInstructionsHtml.Build(new[] { L("one"), L("two"), P("between"), L("three") });

        Assert.Equal(2, Count(html, "<ul>"));
        Assert.Equal(2, Count(html, "</ul>"));
        Assert.Equal(3, Count(html, "<li>"));
    }

    /// <summary>A list that is the last block is still closed.</summary>
    [Fact]
    public void Build_ListAtTheEnd_IsClosed()
    {
        var html = ImportInstructionsHtml.Build(new[] { P("intro"), L("only") });

        Assert.Equal(1, Count(html, "<ul>"));
        Assert.Equal(1, Count(html, "</ul>"));
        Assert.True(html.IndexOf("</ul>", StringComparison.Ordinal) < html.IndexOf("</body>", StringComparison.Ordinal));
    }

    /// <summary>An empty list of blocks still gives a valid page (the title only).</summary>
    [Fact]
    public void Build_NoBlocks_StillGivesTheTitlePage()
    {
        var html = ImportInstructionsHtml.Build(Array.Empty<InstructionBlock>());

        Assert.Contains("<h1>Importing sections into TermPoint</h1>", html);
        Assert.DoesNotContain("<h2>", html);
        Assert.DoesNotContain("<ul>", html);
    }

    /// <summary>
    /// The shipped instructions build into a page with a heading, a list and a paragraph. The heading checked is
    /// whichever comes first in the file, so editing the instructions doesn't break this test.
    /// </summary>
    [Fact]
    public void Build_TheShippedInstructions_MakeAPageWithEveryKindOfBlock()
    {
        var blocks = ImportInstructions.Parse(FakeImportSectionsPlatform.ShippedInstructionsText());
        var firstHeading = blocks.First(b => b.IsHeading).Text;

        var html = ImportInstructionsHtml.Build(blocks);

        Assert.Contains($"<h2>{System.Net.WebUtility.HtmlEncode(firstHeading)}</h2>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<p>", html);
        Assert.Equal(blocks.Count(b => b.IsHeading), Count(html, "<h2>"));
        Assert.Equal(blocks.Count(b => b.IsBullet), Count(html, "<li>"));
    }
}

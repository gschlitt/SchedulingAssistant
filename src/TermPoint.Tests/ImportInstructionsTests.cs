using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="ImportInstructions.Parse"/>, the reader of the section import instructions text. Its
/// rules must stay exactly those of <c>parse_instructions</c> in <c>tools/section-import/make_template.py</c>,
/// so each rule has a test, and the shipped text is parsed once as a whole.
/// </summary>
public sealed class ImportInstructionsTests
{
    /// <summary>Shorthand for the block a test expects.</summary>
    private static InstructionBlock B(InstructionBlockKind kind, string text) => new(kind, text);

    /// <summary>A "# " line is a heading, a "- " line is a bullet, and plain lines are a paragraph.</summary>
    [Fact]
    public void Parse_HeadingBulletAndParagraph_AreRecognised()
    {
        var blocks = ImportInstructions.Parse("# A heading\n\nSome words.\n\n- First\n- Second\n");

        Assert.Equal(
            new[]
            {
                B(InstructionBlockKind.Heading, "A heading"),
                B(InstructionBlockKind.Paragraph, "Some words."),
                B(InstructionBlockKind.Bullet, "First"),
                B(InstructionBlockKind.Bullet, "Second"),
            },
            blocks.ToArray());
    }

    /// <summary>The convenience flags agree with the kind.</summary>
    [Fact]
    public void Block_IsFlags_FollowTheKind()
    {
        var heading = B(InstructionBlockKind.Heading, "h");
        var paragraph = B(InstructionBlockKind.Paragraph, "p");
        var bullet = B(InstructionBlockKind.Bullet, "b");

        Assert.True(heading.IsHeading);
        Assert.False(heading.IsParagraph || heading.IsBullet);
        Assert.True(paragraph.IsParagraph);
        Assert.False(paragraph.IsHeading || paragraph.IsBullet);
        Assert.True(bullet.IsBullet);
        Assert.False(bullet.IsHeading || bullet.IsParagraph);
    }

    /// <summary>The lines of one paragraph are joined with single spaces, with no leftover indentation.</summary>
    [Fact]
    public void Parse_ParagraphOverSeveralLines_JoinsWithSingleSpaces()
    {
        var blocks = ImportInstructions.Parse("first line\n   second line   \nthird line");

        var block = Assert.Single(blocks);
        Assert.Equal(B(InstructionBlockKind.Paragraph, "first line second line third line"), block);
    }

    /// <summary>A blank line ends a paragraph, however many there are; whitespace-only lines count as blank.</summary>
    [Fact]
    public void Parse_BlankLines_SeparateParagraphs()
    {
        var blocks = ImportInstructions.Parse("one\n\n\n   \ntwo\nstill two");

        Assert.Equal(
            new[]
            {
                B(InstructionBlockKind.Paragraph, "one"),
                B(InstructionBlockKind.Paragraph, "two still two"),
            },
            blocks.ToArray());
    }

    /// <summary>A heading or a bullet ends the paragraph before it even without a blank line.</summary>
    [Fact]
    public void Parse_HeadingOrBullet_EndsTheParagraphBeforeIt()
    {
        var blocks = ImportInstructions.Parse("words\n- a bullet\nmore words\n# A heading\nlast");

        Assert.Equal(
            new[]
            {
                B(InstructionBlockKind.Paragraph, "words"),
                B(InstructionBlockKind.Bullet, "a bullet"),
                B(InstructionBlockKind.Paragraph, "more words"),
                B(InstructionBlockKind.Heading, "A heading"),
                B(InstructionBlockKind.Paragraph, "last"),
            },
            blocks.ToArray());
    }

    /// <summary>Windows (CR LF) and old Mac (CR) line endings read the same as LF.</summary>
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Parse_AnyLineEnding_GivesTheSameBlocks(string newline)
    {
        var text = string.Join(newline, "# Title", "", "line one", "line two", "", "- item", "");

        var blocks = ImportInstructions.Parse(text);

        Assert.Equal(
            new[]
            {
                B(InstructionBlockKind.Heading, "Title"),
                B(InstructionBlockKind.Paragraph, "line one line two"),
                B(InstructionBlockKind.Bullet, "item"),
            },
            blocks.ToArray());
    }

    /// <summary>Lines are trimmed first, so an indented "# " or "- " line is still a heading or a bullet.</summary>
    [Fact]
    public void Parse_IndentedMarkers_AreStillMarkers()
    {
        var blocks = ImportInstructions.Parse("   # Indented heading   \n\t- Indented bullet  ");

        Assert.Equal(
            new[]
            {
                B(InstructionBlockKind.Heading, "Indented heading"),
                B(InstructionBlockKind.Bullet, "Indented bullet"),
            },
            blocks.ToArray());
    }

    /// <summary>A marker needs its space: "#Heading" and "-item" are ordinary paragraph text.</summary>
    [Fact]
    public void Parse_MarkerWithoutSpace_IsParagraphText()
    {
        var blocks = ImportInstructions.Parse("#Heading\n\n-item");

        Assert.Equal(
            new[]
            {
                B(InstructionBlockKind.Paragraph, "#Heading"),
                B(InstructionBlockKind.Paragraph, "-item"),
            },
            blocks.ToArray());
    }

    /// <summary>Null, empty and blank text give no blocks.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  \n")]
    public void Parse_NoText_GivesNoBlocks(string? text)
    {
        Assert.Empty(ImportInstructions.Parse(text));
    }

    /// <summary>
    /// The shipped file has headings, bullets and paragraphs, and no block is empty. Only its shape is checked,
    /// not its wording, so editing the instructions doesn't break this test. (It is read from the test project's
    /// copy of the shipped asset.)
    /// </summary>
    [Fact]
    public void Parse_TheShippedInstructions_AreWellFormed()
    {
        var blocks = ImportInstructions.Parse(FakeImportSectionsPlatform.ShippedInstructionsText());

        Assert.Contains(blocks, b => b.IsHeading);
        Assert.Contains(blocks, b => b.IsBullet);
        Assert.Contains(blocks, b => b.IsParagraph);
        Assert.All(blocks, b => Assert.False(string.IsNullOrWhiteSpace(b.Text), "A block has no text."));
    }
}

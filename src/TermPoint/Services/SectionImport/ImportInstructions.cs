namespace TermPoint.Services.SectionImport;

/// <summary>
/// The three kinds of block the section import instructions are made of (see <see cref="ImportInstructions.Parse"/>).
/// </summary>
public enum InstructionBlockKind
{
    /// <summary>A heading: a line that starts with <c># </c>.</summary>
    Heading,

    /// <summary>A paragraph: one or more plain lines, joined into one run of text.</summary>
    Paragraph,

    /// <summary>A bullet: a single line that starts with <c>- </c>.</summary>
    Bullet,
}

/// <summary>
/// One block of the instructions: a heading, a paragraph or a bullet, with its text. The three
/// <c>Is...</c> properties let a view choose how to draw it without comparing enum values.
/// </summary>
/// <param name="Kind">What kind of block this is.</param>
/// <param name="Text">The block's text with the markup removed (no <c># </c> or <c>- </c>), trimmed, never empty when it comes from <see cref="ImportInstructions.Parse"/>.</param>
public sealed record InstructionBlock(InstructionBlockKind Kind, string Text)
{
    /// <summary>True when this block is a heading.</summary>
    public bool IsHeading => Kind == InstructionBlockKind.Heading;

    /// <summary>True when this block is a paragraph.</summary>
    public bool IsParagraph => Kind == InstructionBlockKind.Paragraph;

    /// <summary>True when this block is a bullet.</summary>
    public bool IsBullet => Kind == InstructionBlockKind.Bullet;
}

/// <summary>
/// Turns the text of the section import instructions (<c>Assets/Templates/SectionImportInstructions.txt</c>)
/// into blocks. That one file is the single source for the template workbook's Instructions sheet, the text
/// the Import Sections flyout shows and the web page "Save instructions…" writes, so every use reads it with
/// these rules. <b>The rules must stay exactly those of <c>parse_instructions</c> in
/// <c>tools/section-import/make_template.py</c></b> (a test compares the workbook's text with this parser's
/// output, so a difference shows up there). Pure and free of platform code, so it is compiled in every build.
/// </summary>
public static class ImportInstructions
{
    /// <summary>The prefix of a heading line.</summary>
    private const string HeadingMarker = "# ";

    /// <summary>The prefix of a bullet line.</summary>
    private const string BulletMarker = "- ";

    /// <summary>The line endings the text may use. CR LF comes first so it counts as one break, not two.</summary>
    private static readonly string[] LineEndings = { "\r\n", "\n", "\r" };

    /// <summary>
    /// Splits the instructions text into blocks. Each line is trimmed. A blank line ends the current paragraph.
    /// A line starting <c># </c> is a heading (the text after it, trimmed). A line starting <c>- </c> is a bullet,
    /// one line each (the text after it, trimmed). Any other line is added to the current paragraph, and the
    /// lines of one paragraph are joined with single spaces. The line endings CR LF, LF and CR all work.
    /// </summary>
    /// <param name="text">The instructions text; null or empty gives an empty list.</param>
    /// <returns>The blocks in the order they appear in the text. Never null.</returns>
    public static IReadOnlyList<InstructionBlock> Parse(string? text)
    {
        var blocks = new List<InstructionBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        // The lines of the paragraph being built; written out as one block when it ends.
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new InstructionBlock(InstructionBlockKind.Paragraph, string.Join(" ", paragraph)));
            paragraph.Clear();
        }

        // Split on any line ending (as Python's splitlines does for these three), then trim each line.
        foreach (var raw in text.Split(LineEndings, StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
            }
            else if (line.StartsWith(HeadingMarker, StringComparison.Ordinal))
            {
                Flush();
                blocks.Add(new InstructionBlock(InstructionBlockKind.Heading, line[HeadingMarker.Length..].Trim()));
            }
            else if (line.StartsWith(BulletMarker, StringComparison.Ordinal))
            {
                Flush();
                blocks.Add(new InstructionBlock(InstructionBlockKind.Bullet, line[BulletMarker.Length..].Trim()));
            }
            else
            {
                paragraph.Add(line);
            }
        }

        Flush();
        return blocks;
    }
}

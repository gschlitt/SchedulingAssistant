using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using TermPoint.ViewModels.Management;

// With ImplicitUsings on, "Path" is otherwise ambiguous with System.IO.Path.
using Path = Avalonia.Controls.Shapes.Path;

namespace TermPoint.Behaviors;

/// <summary>
/// Attached behavior that renders a list of <see cref="CardPropertyLine"/>s into a single
/// <see cref="TextBlock"/>'s <c>Inlines</c>. It is how a section card shows its conflict
/// warnings and its tag, reserve and resource lines.
///
/// <b>Why it exists (spec item 21, "lightweight section card"):</b> every section in the loaded
/// semester gets a card, and the section list is not virtualized, so each control on a card is
/// paid for hundreds of times over. The old card built a hidden row (a panel, an icon
/// <see cref="Viewbox"/>, a <see cref="Path"/> and a <see cref="TextBlock"/>) for every possible
/// property line whether or not it was shown, about 16 controls per card. This behavior replaces
/// them with one wrapping <see cref="TextBlock"/> whose inlines are built <em>only for the lines
/// that are present</em>: a card with no property lines creates no inlines and no icons, and an
/// icon line costs two controls (the <see cref="Viewbox"/> and its <see cref="Path"/>).
///
/// <b>Why not <c>InlineFormatter</c>:</b> that helper parses a markup string, and tag, reserve and
/// resource names are user-entered, so they could contain its markup characters. The line list is
/// structured data (kind plus text) and the text is never parsed.
///
/// <b>What is built, per line</b> (lines are separated by a <see cref="LineBreak"/>, none after the
/// last):
/// <list type="bullet">
///   <item><b>Conflict kinds</b> (<see cref="CardPropertyKind.RoomConflict"/>,
///         <see cref="CardPropertyKind.InstructorConflict"/>): one <see cref="Run"/> with the
///         line's text and the style class <see cref="WarningClass"/>.</item>
///   <item><b>Icon kinds</b> (<see cref="CardPropertyKind.Tags"/>,
///         <see cref="CardPropertyKind.Reserves"/>, <see cref="CardPropertyKind.Resources"/>):
///         an <see cref="InlineUIContainer"/> holding a <see cref="Viewbox"/> (class
///         <see cref="IconClass"/>) around a <see cref="Path"/> (classes <see cref="IconClass"/>
///         plus <see cref="TagsClass"/>, <see cref="ReservesClass"/> or <see cref="ResourcesClass"/>),
///         followed by a <see cref="Run"/> with the line's text.</item>
/// </list>
///
/// <b>Presentation is not set here.</b> This code sets only structure and style classes (plus the
/// inline box's <see cref="BaselineAlignment"/>). Sizes, margins, stroke, geometry, weights and
/// colours come from the <c>TextBlock.CardPropertyLines</c> styles in SectionListView.axaml, so
/// they stay in AXAML and out of C#.
///
/// <b>Vertical alignment of icons:</b> the icon boxes use <see cref="BaselineAlignment.Center"/>,
/// the same value <c>InlineFormatter</c> uses for its link boxes, to approximate the old
/// vertically-centred icon-and-text rows. This is a starting point that may be tuned (for example
/// to <see cref="BaselineAlignment.TextBottom"/> or <see cref="BaselineAlignment.Bottom"/>) after
/// a visual check of the card.
///
/// The inlines are rebuilt from scratch every time <see cref="LinesProperty"/> changes. Setting it
/// to null or an empty list leaves the <see cref="TextBlock"/> with no inlines.
///
/// Usage in AXAML (add xmlns:b="using:TermPoint.Behaviors" to the root element):
///   <![CDATA[
///   <TextBlock Classes="CardPropertyLines"
///              TextWrapping="Wrap"
///              IsVisible="{Binding HasPropertyLines}"
///              b:PropertyLinesBehavior.Lines="{Binding PropertyLines}" />
///   ]]>
/// </summary>
public static class PropertyLinesBehavior
{
    // ── Style classes emitted for the AXAML styles to target ───────────────────

    /// <summary>Class on the <see cref="Run"/> of a conflict-warning line (<c>Run.warning</c>).</summary>
    public const string WarningClass = "warning";

    /// <summary>
    /// Class on both the icon <see cref="Viewbox"/> and its <see cref="Path"/>
    /// (<c>Viewbox.cardPropertyIcon</c>, <c>Path.cardPropertyIcon</c>).
    /// </summary>
    public const string IconClass = "cardPropertyIcon";

    /// <summary>Extra class on the <see cref="Path"/> of a tags line; selects the tag geometry.</summary>
    public const string TagsClass = "tags";

    /// <summary>Extra class on the <see cref="Path"/> of a reserves line; selects the ticket geometry.</summary>
    public const string ReservesClass = "reserves";

    /// <summary>Extra class on the <see cref="Path"/> of a resources line; selects the tools geometry.</summary>
    public const string ResourcesClass = "resources";

    // ── Attached property ──────────────────────────────────────────────────────

    /// <summary>
    /// The lines to render into the attached <see cref="TextBlock"/>'s inlines, in order.
    /// Null or empty renders nothing.
    /// </summary>
    public static readonly AttachedProperty<IEnumerable<CardPropertyLine>?> LinesProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IEnumerable<CardPropertyLine>?>(
            "Lines", typeof(PropertyLinesBehavior));

    /// <summary>Gets the lines attached to the specified TextBlock.</summary>
    public static IEnumerable<CardPropertyLine>? GetLines(TextBlock tb) => tb.GetValue(LinesProperty);

    /// <summary>Sets the lines attached to the specified TextBlock.</summary>
    public static void SetLines(TextBlock tb, IEnumerable<CardPropertyLine>? value) => tb.SetValue(LinesProperty, value);

    static PropertyLinesBehavior()
    {
        LinesProperty.Changed.AddClassHandler<TextBlock>(OnLinesChanged);
    }

    // ── Building ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the TextBlock's inlines from the new line list: clears them, then adds the inlines
    /// for each line (see the class summary). A null or empty list leaves no inlines.
    /// </summary>
    /// <param name="tb">The TextBlock whose attached <see cref="LinesProperty"/> changed.</param>
    /// <param name="e">Change details; <see cref="AvaloniaPropertyChangedEventArgs.NewValue"/> is the new line list.</param>
    private static void OnLinesChanged(TextBlock tb, AvaloniaPropertyChangedEventArgs e)
    {
        // Avalonia's TextBlock always owns an InlineCollection, but its setter accepts null.
        var inlines = tb.Inlines;
        if (inlines is null)
            return;

        inlines.Clear();

        if (e.NewValue is not IEnumerable<CardPropertyLine> lines)
            return;

        var first = true;
        foreach (var line in lines)
        {
            // A line break between lines only, never before the first or after the last.
            if (!first)
                inlines.Add(new LineBreak());
            first = false;

            AddLine(inlines, line);
        }
    }

    /// <summary>
    /// Adds the inline(s) for a single line: a styled run for a conflict warning, or an icon box
    /// followed by a run for a tags, reserves or resources line. A kind with no known look is
    /// shown as a plain unstyled run rather than failing, so a new kind added to
    /// <see cref="CardPropertyKind"/> without view support still displays its text.
    /// </summary>
    /// <param name="inlines">The collection being built.</param>
    /// <param name="line">The line to add.</param>
    private static void AddLine(InlineCollection inlines, CardPropertyLine line)
    {
        switch (line.Kind)
        {
            case CardPropertyKind.RoomConflict:
            case CardPropertyKind.InstructorConflict:
                var warning = new Run { Text = line.Text };
                warning.Classes.Add(WarningClass);
                inlines.Add(warning);
                break;

            case CardPropertyKind.Tags:
                AddIconLine(inlines, TagsClass, line.Text);
                break;

            case CardPropertyKind.Reserves:
                AddIconLine(inlines, ReservesClass, line.Text);
                break;

            case CardPropertyKind.Resources:
                AddIconLine(inlines, ResourcesClass, line.Text);
                break;

            default:
                inlines.Add(new Run { Text = line.Text });
                break;
        }
    }

    /// <summary>
    /// Adds an icon line: an <see cref="InlineUIContainer"/> holding the icon
    /// <see cref="Viewbox"/> and <see cref="Path"/>, then a <see cref="Run"/> with the text. The
    /// controls carry only style classes; size, margin, stroke and geometry come from AXAML styles.
    /// </summary>
    /// <param name="inlines">The collection being built.</param>
    /// <param name="kindClass">The per-kind class on the Path (<see cref="TagsClass"/>, <see cref="ReservesClass"/> or <see cref="ResourcesClass"/>).</param>
    /// <param name="text">The line's text, shown after the icon.</param>
    private static void AddIconLine(InlineCollection inlines, string kindClass, string text)
    {
        var path = new Path();
        path.Classes.Add(IconClass);
        path.Classes.Add(kindClass);

        var box = new Viewbox { Child = path };
        box.Classes.Add(IconClass);

        // Center aligns the icon box with the text line, approximating the old centred
        // icon-and-text rows. May be tuned after a visual check (see class summary).
        inlines.Add(new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center });
        inlines.Add(new Run { Text = text });
    }
}

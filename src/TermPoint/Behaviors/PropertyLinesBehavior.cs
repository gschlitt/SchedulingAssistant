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
///         line's text, coloured by <see cref="WarningForegroundProperty"/> and weighted by
///         <see cref="WarningFontWeightProperty"/>.</item>
///   <item><b>Icon kinds</b> (<see cref="CardPropertyKind.Tags"/>,
///         <see cref="CardPropertyKind.Reserves"/>, <see cref="CardPropertyKind.Resources"/>):
///         an <see cref="InlineUIContainer"/> holding a <see cref="Viewbox"/> (sized by
///         <see cref="IconSizeProperty"/>, followed by <see cref="IconSpacingProperty"/> of space)
///         around a <see cref="Path"/> (stroked with <see cref="IconBrushProperty"/>, geometry from
///         <see cref="TagIconProperty"/>, <see cref="ReserveIconProperty"/> or
///         <see cref="ResourceIconProperty"/>), followed by a <see cref="Run"/> with the line's
///         text.</item>
/// </list>
///
/// <b>Presentation comes from AXAML, but is set directly, not through styles.</b> This code
/// hard-codes no font size, weight or colour: the weights, brushes, sizes and geometries are all
/// attached properties that the AXAML sets on the <see cref="TextBlock"/> (see the usage example),
/// and the behavior copies them onto the elements it builds. It sets no style classes either.
/// That is deliberate. In Avalonia a style whose selector has a class condition (for example
/// <c>Run.warning</c> or <c>Path.cardPropertyIcon</c>) attaches a style instance and class
/// activators to <em>every</em> element of the target type in the view, whether it matches or not,
/// which with hundreds of section cards cost real memory (spec item 21, step 4b). Values set
/// directly on the elements cost nothing extra.
///
/// <b>Rebuilding:</b> the inlines are rebuilt from scratch whenever <em>any</em> of the attached
/// properties here changes, not only <see cref="LinesProperty"/>. That makes the result independent
/// of the order in which AXAML attributes and bindings are applied: a card can never be left built
/// with default values because the line list happened to arrive before the look did. Setting the
/// lines to null or an empty list leaves the <see cref="TextBlock"/> with no inlines.
///
/// <b>Vertical alignment of icons:</b> the icon boxes use <see cref="BaselineAlignment.Center"/>,
/// the same value <c>InlineFormatter</c> uses for its link boxes, to approximate the old
/// vertically-centred icon-and-text rows. This is a starting point that may be tuned (for example
/// to <see cref="BaselineAlignment.TextBottom"/> or <see cref="BaselineAlignment.Bottom"/>) after
/// a visual check of the card.
///
/// Usage in AXAML (add xmlns:b="using:TermPoint.Behaviors" to the root element):
///   <![CDATA[
///   <TextBlock b:PropertyLinesBehavior.Lines="{Binding PropertyLines}"
///              b:PropertyLinesBehavior.WarningForeground="{StaticResource TextError}"
///              b:PropertyLinesBehavior.WarningFontWeight="SemiBold"
///              b:PropertyLinesBehavior.IconBrush="{StaticResource TextMuted}"
///              b:PropertyLinesBehavior.IconSize="16"
///              b:PropertyLinesBehavior.IconSpacing="4"
///              b:PropertyLinesBehavior.TagIcon="{StaticResource TagIcon}"
///              b:PropertyLinesBehavior.ReserveIcon="{StaticResource TicketIcon}"
///              b:PropertyLinesBehavior.ResourceIcon="{StaticResource ToolsIcon}"
///              FontSize="11"
///              TextWrapping="Wrap"
///              IsVisible="{Binding HasPropertyLines}" />
///   ]]>
/// </summary>
public static class PropertyLinesBehavior
{
    // ── Fixed icon-stroke look ─────────────────────────────────────────────────
    //
    // These three are neither colours, fonts nor weights, and they are the same for every icon,
    // so they are constants here rather than attached properties. They match the markup of the
    // card's icons before this behavior existed (and the old TextBlock.CardPropertyLines style).

    /// <summary>Stroke thickness of every icon <see cref="Path"/>.</summary>
    private const double IconStrokeThickness = 2;

    /// <summary>Line cap of every icon <see cref="Path"/>'s stroke.</summary>
    private const PenLineCap IconStrokeLineCap = PenLineCap.Round;

    /// <summary>Line join of every icon <see cref="Path"/>'s stroke.</summary>
    private const PenLineJoin IconStrokeLineJoin = PenLineJoin.Round;

    // ── Attached properties ────────────────────────────────────────────────────

    /// <summary>
    /// The lines to render into the attached <see cref="TextBlock"/>'s inlines, in order.
    /// Null or empty renders nothing.
    /// </summary>
    public static readonly AttachedProperty<IEnumerable<CardPropertyLine>?> LinesProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IEnumerable<CardPropertyLine>?>(
            "Lines", typeof(PropertyLinesBehavior));

    /// <summary>
    /// Foreground of a conflict-warning line's <see cref="Run"/>. Null (the default) leaves the
    /// run inheriting the <see cref="TextBlock"/>'s own foreground.
    /// </summary>
    public static readonly AttachedProperty<IBrush?> WarningForegroundProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IBrush?>(
            "WarningForeground", typeof(PropertyLinesBehavior));

    /// <summary>
    /// Font weight of a conflict-warning line's <see cref="Run"/>. Defaults to
    /// <see cref="FontWeight.Normal"/>, i.e. a warning is not emphasised unless the AXAML says so.
    /// </summary>
    public static readonly AttachedProperty<FontWeight> WarningFontWeightProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, FontWeight>(
            "WarningFontWeight", typeof(PropertyLinesBehavior), FontWeight.Normal);

    /// <summary>
    /// Stroke brush of the icons. Null (the default) uses the <see cref="TextBlock"/>'s own
    /// foreground at the time the inlines are built, so an icon is never invisible by default.
    /// </summary>
    public static readonly AttachedProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IBrush?>(
            "IconBrush", typeof(PropertyLinesBehavior));

    /// <summary>
    /// Width and height of the icon <see cref="Viewbox"/>, in device-independent pixels.
    /// Defaults to 16, the size the section card uses.
    /// </summary>
    public static readonly AttachedProperty<double> IconSizeProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, double>(
            "IconSize", typeof(PropertyLinesBehavior), 16d);

    /// <summary>
    /// Space between an icon and the text after it: the icon <see cref="Viewbox"/>'s right margin,
    /// in device-independent pixels. Defaults to 4, the spacing the section card uses.
    /// </summary>
    public static readonly AttachedProperty<double> IconSpacingProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, double>(
            "IconSpacing", typeof(PropertyLinesBehavior), 4d);

    /// <summary>
    /// Icon geometry for a <see cref="CardPropertyKind.Tags"/> line. Null (the default) shows an
    /// empty icon box, so the text still lines up as it would with an icon.
    /// </summary>
    public static readonly AttachedProperty<Geometry?> TagIconProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Geometry?>(
            "TagIcon", typeof(PropertyLinesBehavior));

    /// <summary>
    /// Icon geometry for a <see cref="CardPropertyKind.Reserves"/> line. Null (the default) shows
    /// an empty icon box.
    /// </summary>
    public static readonly AttachedProperty<Geometry?> ReserveIconProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Geometry?>(
            "ReserveIcon", typeof(PropertyLinesBehavior));

    /// <summary>
    /// Icon geometry for a <see cref="CardPropertyKind.Resources"/> line. Null (the default) shows
    /// an empty icon box.
    /// </summary>
    public static readonly AttachedProperty<Geometry?> ResourceIconProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Geometry?>(
            "ResourceIcon", typeof(PropertyLinesBehavior));

    // ── Accessors (required by Avalonia's XAML for attached properties) ────────

    /// <summary>Gets the lines attached to the specified TextBlock.</summary>
    public static IEnumerable<CardPropertyLine>? GetLines(TextBlock tb) => tb.GetValue(LinesProperty);

    /// <summary>Sets the lines attached to the specified TextBlock.</summary>
    public static void SetLines(TextBlock tb, IEnumerable<CardPropertyLine>? value) => tb.SetValue(LinesProperty, value);

    /// <summary>Gets the warning-line foreground attached to the specified TextBlock.</summary>
    public static IBrush? GetWarningForeground(TextBlock tb) => tb.GetValue(WarningForegroundProperty);

    /// <summary>Sets the warning-line foreground attached to the specified TextBlock.</summary>
    public static void SetWarningForeground(TextBlock tb, IBrush? value) => tb.SetValue(WarningForegroundProperty, value);

    /// <summary>Gets the warning-line font weight attached to the specified TextBlock.</summary>
    public static FontWeight GetWarningFontWeight(TextBlock tb) => tb.GetValue(WarningFontWeightProperty);

    /// <summary>Sets the warning-line font weight attached to the specified TextBlock.</summary>
    public static void SetWarningFontWeight(TextBlock tb, FontWeight value) => tb.SetValue(WarningFontWeightProperty, value);

    /// <summary>Gets the icon stroke brush attached to the specified TextBlock.</summary>
    public static IBrush? GetIconBrush(TextBlock tb) => tb.GetValue(IconBrushProperty);

    /// <summary>Sets the icon stroke brush attached to the specified TextBlock.</summary>
    public static void SetIconBrush(TextBlock tb, IBrush? value) => tb.SetValue(IconBrushProperty, value);

    /// <summary>Gets the icon box size attached to the specified TextBlock.</summary>
    public static double GetIconSize(TextBlock tb) => tb.GetValue(IconSizeProperty);

    /// <summary>Sets the icon box size attached to the specified TextBlock.</summary>
    public static void SetIconSize(TextBlock tb, double value) => tb.SetValue(IconSizeProperty, value);

    /// <summary>Gets the icon-to-text spacing attached to the specified TextBlock.</summary>
    public static double GetIconSpacing(TextBlock tb) => tb.GetValue(IconSpacingProperty);

    /// <summary>Sets the icon-to-text spacing attached to the specified TextBlock.</summary>
    public static void SetIconSpacing(TextBlock tb, double value) => tb.SetValue(IconSpacingProperty, value);

    /// <summary>Gets the tags-line icon geometry attached to the specified TextBlock.</summary>
    public static Geometry? GetTagIcon(TextBlock tb) => tb.GetValue(TagIconProperty);

    /// <summary>Sets the tags-line icon geometry attached to the specified TextBlock.</summary>
    public static void SetTagIcon(TextBlock tb, Geometry? value) => tb.SetValue(TagIconProperty, value);

    /// <summary>Gets the reserves-line icon geometry attached to the specified TextBlock.</summary>
    public static Geometry? GetReserveIcon(TextBlock tb) => tb.GetValue(ReserveIconProperty);

    /// <summary>Sets the reserves-line icon geometry attached to the specified TextBlock.</summary>
    public static void SetReserveIcon(TextBlock tb, Geometry? value) => tb.SetValue(ReserveIconProperty, value);

    /// <summary>Gets the resources-line icon geometry attached to the specified TextBlock.</summary>
    public static Geometry? GetResourceIcon(TextBlock tb) => tb.GetValue(ResourceIconProperty);

    /// <summary>Sets the resources-line icon geometry attached to the specified TextBlock.</summary>
    public static void SetResourceIcon(TextBlock tb, Geometry? value) => tb.SetValue(ResourceIconProperty, value);

    /// <summary>
    /// Subscribes every attached property above to the one rebuild handler, so a change to any of
    /// them (lines or look) rebuilds the inlines from the TextBlock's current values.
    /// </summary>
    static PropertyLinesBehavior()
    {
        LinesProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        WarningForegroundProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        WarningFontWeightProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        IconBrushProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        IconSizeProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        IconSpacingProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        TagIconProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        ReserveIconProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
        ResourceIconProperty.Changed.AddClassHandler<TextBlock>(OnAnyPropertyChanged);
    }

    // ── Building ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The presentation values read from a TextBlock's attached properties, captured once per
    /// rebuild so they are not re-read for every line.
    /// </summary>
    /// <param name="WarningForeground">Foreground for warning runs; null leaves them inheriting.</param>
    /// <param name="WarningFontWeight">Font weight for warning runs.</param>
    /// <param name="IconBrush">Stroke brush for icons (already resolved; never null unless the TextBlock has no foreground).</param>
    /// <param name="IconSize">Width and height of the icon box.</param>
    /// <param name="IconSpacing">Right margin of the icon box.</param>
    /// <param name="TagIcon">Geometry for tags lines.</param>
    /// <param name="ReserveIcon">Geometry for reserves lines.</param>
    /// <param name="ResourceIcon">Geometry for resources lines.</param>
    private readonly record struct Look(
        IBrush? WarningForeground,
        FontWeight WarningFontWeight,
        IBrush? IconBrush,
        double IconSize,
        double IconSpacing,
        Geometry? TagIcon,
        Geometry? ReserveIcon,
        Geometry? ResourceIcon);

    /// <summary>
    /// Reads the current presentation attached properties from the TextBlock.
    /// </summary>
    /// <param name="tb">The TextBlock the behavior is attached to.</param>
    /// <returns>The captured values; an unset icon brush falls back to the TextBlock's foreground.</returns>
    private static Look ReadLook(TextBlock tb) => new(
        WarningForeground: GetWarningForeground(tb),
        WarningFontWeight: GetWarningFontWeight(tb),
        IconBrush: GetIconBrush(tb) ?? tb.Foreground,
        IconSize: GetIconSize(tb),
        IconSpacing: GetIconSpacing(tb),
        TagIcon: GetTagIcon(tb),
        ReserveIcon: GetReserveIcon(tb),
        ResourceIcon: GetResourceIcon(tb));

    /// <summary>
    /// The single change handler for every attached property of this behavior: whichever one
    /// changed, the inlines are rebuilt from the TextBlock's current lines and look.
    /// </summary>
    /// <param name="tb">The TextBlock whose attached property changed.</param>
    /// <param name="e">Change details (unused; the rebuild reads the current values).</param>
    private static void OnAnyPropertyChanged(TextBlock tb, AvaloniaPropertyChangedEventArgs e) => Rebuild(tb);

    /// <summary>
    /// Rebuilds the TextBlock's inlines from its current line list and look: clears them, then adds
    /// the inlines for each line (see the class summary). A null or empty list leaves no inlines.
    /// </summary>
    /// <param name="tb">The TextBlock to rebuild.</param>
    private static void Rebuild(TextBlock tb)
    {
        // Avalonia's TextBlock always owns an InlineCollection, but its setter accepts null.
        var inlines = tb.Inlines;
        if (inlines is null)
            return;

        // Skipping Clear() when there is nothing to clear keeps the usual card-construction path
        // (several look properties set while the line list is still empty) free of churn.
        if (inlines.Count > 0)
            inlines.Clear();

        var lines = GetLines(tb);
        if (lines is null)
            return;

        var look = ReadLook(tb);

        var first = true;
        foreach (var line in lines)
        {
            // A line break between lines only, never before the first or after the last.
            if (!first)
                inlines.Add(new LineBreak());
            first = false;

            AddLine(inlines, line, look);
        }
    }

    /// <summary>
    /// Adds the inline(s) for a single line: a warning-look run for a conflict, or an icon box
    /// followed by a run for a tags, reserves or resources line. A kind with no known look is
    /// shown as a plain run rather than failing, so a new kind added to
    /// <see cref="CardPropertyKind"/> without view support still displays its text.
    /// </summary>
    /// <param name="inlines">The collection being built.</param>
    /// <param name="line">The line to add.</param>
    /// <param name="look">The presentation values to apply.</param>
    private static void AddLine(InlineCollection inlines, CardPropertyLine line, in Look look)
    {
        switch (line.Kind)
        {
            case CardPropertyKind.RoomConflict:
            case CardPropertyKind.InstructorConflict:
                var warning = new Run { Text = line.Text, FontWeight = look.WarningFontWeight };
                // Only set a foreground when one was supplied: assigning null would override the
                // inherited foreground with "no brush" rather than leaving it inherited.
                if (look.WarningForeground is not null)
                    warning.Foreground = look.WarningForeground;
                inlines.Add(warning);
                break;

            case CardPropertyKind.Tags:
                AddIconLine(inlines, look.TagIcon, line.Text, look);
                break;

            case CardPropertyKind.Reserves:
                AddIconLine(inlines, look.ReserveIcon, line.Text, look);
                break;

            case CardPropertyKind.Resources:
                AddIconLine(inlines, look.ResourceIcon, line.Text, look);
                break;

            default:
                inlines.Add(new Run { Text = line.Text });
                break;
        }
    }

    /// <summary>
    /// Adds an icon line: an <see cref="InlineUIContainer"/> holding the icon
    /// <see cref="Viewbox"/> and <see cref="Path"/>, then a <see cref="Run"/> with the text. Size,
    /// margin, brush and geometry are set directly from <paramref name="look"/>; the stroke
    /// thickness, cap and join are the fixed constants of this class.
    /// </summary>
    /// <param name="inlines">The collection being built.</param>
    /// <param name="geometry">The icon geometry for this line's kind (may be null for an empty icon box).</param>
    /// <param name="text">The line's text, shown after the icon.</param>
    /// <param name="look">The presentation values to apply.</param>
    private static void AddIconLine(InlineCollection inlines, Geometry? geometry, string text, in Look look)
    {
        var path = new Path
        {
            Data = geometry,
            Fill = Brushes.Transparent,
            Stroke = look.IconBrush,
            StrokeThickness = IconStrokeThickness,
            StrokeLineCap = IconStrokeLineCap,
            StrokeJoin = IconStrokeLineJoin,
        };

        var box = new Viewbox
        {
            Child = path,
            Width = look.IconSize,
            Height = look.IconSize,
            Margin = new Thickness(0, 0, look.IconSpacing, 0),
        };

        // Center aligns the icon box with the text line, approximating the old centred
        // icon-and-text rows. May be tuned after a visual check (see class summary).
        inlines.Add(new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center });
        inlines.Add(new Run { Text = text });
    }
}

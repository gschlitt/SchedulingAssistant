namespace TermPoint.ViewModels.Management;

/// <summary>
/// Identifies what kind of information a <see cref="CardPropertyLine"/> carries on a section
/// card. The view model states only <em>what</em> a line is; the view decides how each kind
/// looks (icon, weight, colour) through AXAML styles keyed on this kind
/// (see <c>PropertyLinesBehavior</c>).
/// </summary>
public enum CardPropertyKind
{
    /// <summary>
    /// An advisory warning that the section's room overlaps another section in the same
    /// semester. Rendered as emphasised warning text with no icon.
    /// </summary>
    RoomConflict,

    /// <summary>
    /// An advisory warning that one of the section's instructors is double-booked in the same
    /// semester. Rendered as emphasised warning text with no icon.
    /// </summary>
    InstructorConflict,

    /// <summary>The section's tag names, comma-separated. Rendered with the tag icon.</summary>
    Tags,

    /// <summary>The section's reserved-seat blocks (name and code). Rendered with the ticket icon.</summary>
    Reserves,

    /// <summary>The section's required resource names, comma-separated. Rendered with the tools icon.</summary>
    Resources
}

/// <summary>
/// One line of the "property lines" block on a section card: a <see cref="Kind"/> plus the
/// already-formatted <see cref="Text"/> to show.
///
/// The view model supplies only the structure (which kind of line, and its text). It deliberately
/// carries no brush, icon geometry, font weight or other presentation: the view maps each
/// <see cref="CardPropertyKind"/> to a look through AXAML styles. Because the text travels as
/// plain data (never parsed as markup), user-entered tag, reserve and resource names are
/// displayed exactly as typed, whatever characters they contain.
///
/// Immutable, so a list of lines can be shared and compared safely.
/// </summary>
/// <param name="Kind">What the line is; selects its icon (if any) and styling in the view.</param>
/// <param name="Text">The display text for the line. Never null or whitespace when produced by
/// <see cref="SectionListItemViewModel.PropertyLines"/>, which omits blank lines.</param>
public sealed record CardPropertyLine(CardPropertyKind Kind, string Text);

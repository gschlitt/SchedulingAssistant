namespace TermPoint.Models;

/// <summary>
/// The shape of the JSON stored in the <c>Semesters.data</c> column: just <see cref="Color"/>, plus
/// whatever unknown fields a different app version wrote (<see cref="PersistedRecord.ExtensionData"/>).
/// The semester's identity fields (id, academic year, name, sort order) live in dedicated columns, so
/// they are deliberately not part of this payload.
/// <para>
/// <b>Format caution.</b> This payload has always been written with <c>System.Text.Json</c> default
/// options (property names exactly as declared, so <c>"Color"</c>), unlike every other table, which
/// goes through <see cref="Data.JsonHelpers"/> (camelCase). <c>SemesterRepository</c> must keep using
/// the default options for this type, or existing databases would change shape. The property name
/// here must therefore stay <c>Color</c>.
/// </para>
/// </summary>
internal sealed class SemesterData : PersistedRecord
{
    /// <summary>
    /// Hex color string for grid display; empty when the semester has no color of its own.
    /// Mirrors <see cref="Semester.Color"/>.
    /// </summary>
    public string Color { get; set; } = string.Empty;
}

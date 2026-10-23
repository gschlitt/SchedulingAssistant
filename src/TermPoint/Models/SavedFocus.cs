namespace TermPoint.Models;

/// <summary>
/// The persisted shape of a <see cref="SubjectFocus"/>, stored per database in
/// <see cref="Services.AppSettings.FocusByDatabase"/>. A plain serializable class (public settable properties, a
/// parameterless constructor) so <c>System.Text.Json</c> reflection serialization, which the browser build also
/// uses, handles it with no source-generation attributes. Convert with <see cref="SubjectFocus.FromSaved"/> and
/// <see cref="SubjectFocus.ToSaved"/>; do not interpret the fields directly.
/// </summary>
public class SavedFocus
{
    /// <summary>Which kind of focus was saved. Defaults to All (the value 0), which hides nothing.</summary>
    public FocusKind Kind { get; set; } = FocusKind.All;

    /// <summary>The focused subject ids; meaningful only when <see cref="Kind"/> is <see cref="FocusKind.Subjects"/>.</summary>
    public List<string> SubjectIds { get; set; } = new();
}

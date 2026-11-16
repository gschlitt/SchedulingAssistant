namespace TermPoint.Services;

/// <summary>
/// A narrow, read-only view of the Section List's inline editor: is a section card open, and which one?
/// Implemented by <c>SectionListViewModel</c>. Other places that save sections (the Schedule View right-click menu,
/// the Course editor's "Apply tags to all sections", a later drag-to-move) ask this before saving, because a save
/// reloads the section store, which collapses the open card and silently discards what the user typed.
/// </summary>
public interface ISectionCardState
{
    /// <summary>
    /// The heading of the section whose card is open in the Section List, or "a new section" for an unsaved new
    /// one; <c>null</c> when no card is open.
    /// </summary>
    string? OpenCardHeading { get; }
}

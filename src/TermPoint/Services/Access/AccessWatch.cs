using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// An access watch (decision D28): a tag marked as watched. It covers exactly the view's sections that carry the
/// tag. Its name is the tag's, read live elsewhere, so it is not held here. Immutable.
/// </summary>
/// <param name="TagId">The watched tag's id.</param>
/// <param name="MultiCampus">The watch's Multi-campus box: slates may mix campuses when ticked.</param>
public sealed record AccessWatch(string TagId, bool MultiCampus)
{
    /// <summary>Builds a watch from a tag's record.</summary>
    /// <param name="tag">The tag (that it is a tag is not checked).</param>
    /// <returns>A watch with the tag's id and its Multi-campus box.</returns>
    public static AccessWatch FromTag(SchedulingEnvironmentValue tag) => new(tag.Id, tag.WatchMultiCampus);

    /// <summary>Whether a section is covered by this watch: it carries the tag.</summary>
    /// <param name="section">The section to test.</param>
    /// <returns><c>true</c> when the section's tags include the watched tag.</returns>
    public bool Covers(Section section) => section.TagIds.Contains(TagId);

    /// <summary>The view's sections covered by this watch, in the view's order.</summary>
    /// <param name="inputs">The semester view.</param>
    /// <returns>The covered sections.</returns>
    public IReadOnlyList<Section> CoveredSections(AccessInputs inputs) => inputs.Sections.Where(Covers).ToList();
}

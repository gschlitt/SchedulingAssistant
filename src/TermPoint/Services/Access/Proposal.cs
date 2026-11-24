using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// A what-if laid over one semester's saved sections (Access Analyzer rules 28 and 30): sections added or
/// replaced, and sections removed. An empty proposal IS the saved state, so there is one code path for both.
/// Immutable.
/// </summary>
public sealed class Proposal
{
    /// <summary>The empty proposal: the view is exactly the saved sections.</summary>
    public static readonly Proposal Empty = new([], []);

    /// <summary>
    /// Sections that replace the saved section with the same id, or are added when no saved section has that id.
    /// </summary>
    public IReadOnlyList<Section> UpsertedSections { get; }

    /// <summary>Ids of saved sections that are taken out of the view.</summary>
    public IReadOnlySet<string> RemovedSectionIds { get; }

    /// <summary>Whether the proposal changes nothing.</summary>
    public bool IsEmpty => UpsertedSections.Count == 0 && RemovedSectionIds.Count == 0;

    /// <summary>Creates a proposal.</summary>
    /// <param name="upsertedSections">Sections to add or to replace saved ones of the same id.</param>
    /// <param name="removedSectionIds">Ids of saved sections to remove.</param>
    public Proposal(IEnumerable<Section> upsertedSections, IEnumerable<string> removedSectionIds)
    {
        UpsertedSections = upsertedSections.ToList();
        RemovedSectionIds = removedSectionIds.ToHashSet();
    }
}

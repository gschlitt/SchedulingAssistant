namespace TermPoint.ViewModels.GridView;

/// <summary>
/// The filter bar's selections at one moment: the ticked ids of each dimension (in
/// <see cref="GridFilterViewModel"/>'s dimension order) and the tag logic. Used to put the filters back after an
/// access watch was open (<see cref="AccessWatchFilter"/>). The overlay is not part of it.
/// </summary>
/// <param name="SelectedIds">The ticked ids of each dimension.</param>
/// <param name="TagsMatchAny">True for "any of the checked tags".</param>
public sealed record FilterSelection(IReadOnlyList<IReadOnlySet<string>> SelectedIds, bool TagsMatchAny)
{
    /// <summary>Whether two selections tick the same ids in every dimension, with the same tag logic.</summary>
    /// <param name="other">The other selection.</param>
    /// <returns>True when they are the same.</returns>
    public bool SameAs(FilterSelection? other) =>
        other is not null
        && TagsMatchAny == other.TagsMatchAny
        && SelectedIds.Count == other.SelectedIds.Count
        && SelectedIds.Zip(other.SelectedIds).All(p => p.First.SetEquals(p.Second));
}

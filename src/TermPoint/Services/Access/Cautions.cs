using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>What a caution says about a section.</summary>
public enum CautionKind
{
    /// <summary>In some slates, but under a third of its fair share: few combinations of the other courses fit around it.</summary>
    HardToReach,
    /// <summary>In over twice its fair share of slates (in a component of three or more sections), short of every slate.</summary>
    HeavyLoad,
}

/// <summary>
/// A yellow caution about one section (round 3 review, point 5). Structured data only; the sentence is
/// <c>AccessText.CautionText</c>.
/// </summary>
/// <param name="Section">The section the caution is about.</param>
/// <param name="Kind">What kind.</param>
/// <param name="Group">The component the section is in.</param>
/// <param name="BlockingGroup">
/// For <see cref="CautionKind.HardToReach"/>: the other component where the section fits with the smallest share of
/// its sections; null when no other component has a section that conflicts with it.
/// </param>
/// <param name="FitsWith">For hard to reach: the sections of <paramref name="BlockingGroup"/> the section fits with.</param>
/// <param name="Leaners">For <see cref="CautionKind.HeavyLoad"/>: sections of other components that fit with no other section of this component (they lean on this one).</param>
public sealed record Caution(
    Section Section,
    CautionKind Kind,
    ComponentGroup Group,
    ComponentGroup? BlockingGroup,
    IReadOnlyList<Section> FitsWith,
    IReadOnlyList<Section> Leaners);

/// <summary>
/// Cautions from each section's share of slates (a port of the r9 wireframe's <c>cautionsFor</c>). Within one
/// component's group every slate uses exactly one section, so the shares add up to 1 and the fair share is
/// 1 / (sections in the group). Hard to reach: in some slates, but under a third of its fair share. Heavy load: over
/// twice its fair share in a group of three or more, short of every slate (that case is "depends on"). Pinned groups,
/// single sections and sections with no times never get one. Pure and static.
/// </summary>
public static class Cautions
{
    /// <summary>
    /// The cautions of an analysis, judged against the slates that count (<see cref="Analysis.CountingSkipSets"/>: the
    /// full slates, or with none the most complete partial slates; a share is of the counting slates that fill the
    /// section's group). Judged only when the count is exact: when nothing counts, or a count stopped at its cap (the
    /// per-section tallies are then partial), there are none.
    /// </summary>
    /// <param name="analysis">The analysis (with its ticks and pins).</param>
    /// <returns>The cautions in group order, then section order.</returns>
    public static IReadOnlyList<Caution> For(Analysis analysis)
    {
        var result = new List<Caution>();
        if (analysis.CountingSkipSets.Count == 0 || analysis.CountingSlatesCapped) return result;
        var groups = analysis.Groups;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            int slates = gi < analysis.SlatesFillingGroup.Count ? analysis.SlatesFillingGroup[gi] : 0;
            if (g.Pinned || g.Candidates.Count < 2 || slates == 0) continue;
            double fair = 1.0 / g.Candidates.Count;
            foreach (var s in g.Candidates)
            {
                if (s.Schedule.Count == 0) continue; // a section with no times fits anything; a caution would be noise
                double share = analysis.SlatesPerSection.GetValueOrDefault(s.Id) / (double)slates;
                if (share > 0 && share < fair / 3)
                {
                    // The reason: the group where it fits with the fewest sections
                    ComponentGroup? best = null;
                    List<Section> bestFits = [];
                    foreach (var h in groups)
                    {
                        if (ReferenceEquals(h, g)) continue;
                        var fits = h.Candidates.Where(t => !SectionOverlap.Conflicts(s, t)).ToList();
                        if (fits.Count < h.Candidates.Count
                            && (best is null || fits.Count / (double)h.Candidates.Count < bestFits.Count / (double)best.Candidates.Count))
                        {
                            best = h;
                            bestFits = fits;
                        }
                    }
                    result.Add(new Caution(s, CautionKind.HardToReach, g, best, bestFits, []));
                }
                else if (g.Candidates.Count >= 3 && share > 2 * fair && share < 1)
                {
                    var leaners = groups.Where(h => !ReferenceEquals(h, g)).SelectMany(h => h.Candidates)
                        .Where(u =>
                        {
                            var fits = g.Candidates.Where(t => !SectionOverlap.Conflicts(u, t)).ToList();
                            return fits.Count == 1 && fits[0].Id == s.Id;
                        }).ToList();
                    result.Add(new Caution(s, CautionKind.HeavyLoad, g, null, [], leaners));
                }
            }
        }
        return result;
    }
}

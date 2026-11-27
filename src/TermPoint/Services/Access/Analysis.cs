using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// Everything the access engine knows about one set of covered sections (a watch, or one course in Check all
/// courses) for chosen courses and pins: the groups, the slates counted, each section's role, the reasons there
/// is no slate, the campus counts and the findings. Immutable, never stored, and carries data only: ids, lists
/// and kinds, no sentences. Produced by <see cref="AccessAnalyzer"/>.
/// </summary>
public sealed class Analysis
{
    /// <summary>Courses with at least one covered section, in stable order (course code).</summary>
    public IReadOnlyList<string> OfferedCourseIds { get; internal init; } = [];

    /// <summary>The courses analysed: the offered ones, narrowed to the chosen subset when there is one.</summary>
    public IReadOnlyList<string> ChosenCourseIds { get; internal init; } = [];

    /// <summary>Whether the analysis covers fewer courses than are offered (a subset was chosen).</summary>
    public bool IsSubset => ChosenCourseIds.Count < OfferedCourseIds.Count;

    /// <summary>Ids of the pinned sections the analysis was run with.</summary>
    public IReadOnlySet<string> PinnedSectionIds { get; internal init; } = new HashSet<string>();

    /// <summary>One group per component (or per course), in stable order; courses with a missing component have none.</summary>
    public IReadOnlyList<ComponentGroup> Groups { get; internal init; } = [];

    /// <summary>Components with no sections (rule 10); their courses are left out.</summary>
    public IReadOnlyList<MissingComponent> MissingComponents { get; internal init; } = [];

    /// <summary>Sections of multi-component courses that fill no component (rule 8).</summary>
    public IReadOnlyList<Section> NotCountedSections { get; internal init; } = [];

    /// <summary>Sections of a component type their course does not list (rule 8a), over the courses' semester sections.</summary>
    public IReadOnlyList<ComponentMismatch> Mismatches { get; internal init; } = [];

    /// <summary>Slates counted, up to the cap. Zero when there are no groups (nothing to take).</summary>
    public int SlateCount { get; internal init; }

    /// <summary>Whether the count stopped at the cap ("more than N").</summary>
    public bool SlateCountCapped { get; internal init; }

    /// <summary>Whether there is at least one full slate for the groups.</summary>
    public bool HasSlate => SlateCount > 0;

    /// <summary>Whether there is nothing to find a slate of: no groups.</summary>
    public bool HasNoGroups => Groups.Count == 0;

    /// <summary>
    /// The role of every candidate section, by section id, judged against <see cref="CountingSkipSets"/>
    /// (<see cref="SectionRole.None"/> for all when that is empty, and for the sections of a group every counting slate leaves out).
    /// </summary>
    public IReadOnlyDictionary<string, SectionRole> Roles { get; internal init; } = new Dictionary<string, SectionRole>();

    /// <summary>Sections essential by timing (their component's other sections are stranded), in group order.</summary>
    public IReadOnlyList<Section> EssentialByTimingSections { get; internal init; } = [];

    /// <summary>The only sections of their components, in group order; sections of pinned groups are left out.</summary>
    public IReadOnlyList<Section> OnlySections { get; internal init; } = [];

    /// <summary>Stranded sections (in no slate that counts, of a group some counting slate fills), in group order.</summary>
    public IReadOnlyList<Section> StrandedSections { get; internal init; } = [];

    /// <summary>
    /// Slates that count each section is in, by section id (sections in none are absent). Exact unless
    /// <see cref="CountingSlatesCapped"/>, when it is a partial tally; the cautions are only judged when not capped.
    /// </summary>
    public IReadOnlyDictionary<string, int> SlatesPerSection { get; internal init; } = new Dictionary<string, int>();

    /// <summary>
    /// The slates the roles, cautions and stranded marks are judged against, as the groups each leaves out (Greg,
    /// 2026-11-27: every watch reports stranded and depends on, full slate or not). With a full slate one empty set
    /// (the full slates). With none, the most complete partial slates: every smallest set of one or two groups whose
    /// leaving out lets the rest be filled (<see cref="SlateSearch.FewestSkipSets"/>). Empty when nothing is judged:
    /// no groups, pins or the campus rule to blame, or more than two groups must be left out.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> CountingSkipSets { get; internal init; } = [];

    /// <summary>
    /// For each group (by index), how many slates that count fill it: the shares behind the cautions. Equal to
    /// <see cref="SlateCount"/> for every group when there is a full slate; 0 for a group every counting slate leaves out.
    /// </summary>
    public IReadOnlyList<int> SlatesFillingGroup { get; internal init; } = [];

    /// <summary>Whether a count of the slates that count stopped at its cap (the tallies are then partial and no caution is judged).</summary>
    public bool CountingSlatesCapped { get; internal init; }

    /// <summary>Round 6c: every group holds exactly one section (before pins), so there is one slate or none.</summary>
    public bool IsSingleSectionCase { get; internal init; }

    /// <summary>In the single-section case, the pairs that conflict in time; empty otherwise.</summary>
    public IReadOnlyList<SectionPair> SingleSectionConflicts { get; internal init; } = [];

    /// <summary>How many pairs of sections from different groups conflict (what Mark all conflicts would mark).</summary>
    public int ConflictCount { get; internal init; }

    /// <summary>Whether the groups' in-person sections are on more than one campus (the Multi-campus box shows only then).</summary>
    public bool SpansCampuses { get; internal init; }

    /// <summary>Whether the one-campus rule applied (the watch's Multi-campus box was off).</summary>
    public bool OneCampusRule { get; internal init; }

    /// <summary>When pins stop a full slate that exists without them: the pins to blame; empty otherwise.</summary>
    public IReadOnlyList<PinBlock> PinBlocks { get; internal init; } = [];

    /// <summary>Courses that cannot be taken even alone (no full slate, no pin block), with reasons.</summary>
    public IReadOnlyList<SelfFailCourse> SelfFailCourses { get; internal init; } = [];

    /// <summary>The best partial slate; null when there is a full slate, no groups, or pins explain the failure.</summary>
    public BestPartial? BestPartial { get; internal init; }

    /// <summary>With the one-campus rule and no course failing alone: set when the rule alone leaves no slate (slates exist when campuses may mix).</summary>
    public CampusReason? CampusReason { get; internal init; }

    /// <summary>Multi-campus on: slates that keep to one campus (counted up to the cap); null when Multi-campus is off or there is no slate.</summary>
    public int? OneCampusSlateCount { get; internal init; }

    /// <summary>Multi-campus on: slates that mix campuses; null when Multi-campus is off, there is no slate, or the count was capped.</summary>
    public int? MixedSlateCount { get; internal init; }

    /// <summary>Multi-campus on and every slate mixes campuses: why (6e); null otherwise.</summary>
    public CampusReason? MixedCampusReason { get; internal init; }

    /// <summary>Candidate sections with no meeting times that matter (no slate, or in some slate).</summary>
    public IReadOnlyList<Section> SectionsWithoutTimes { get; internal init; } = [];

    /// <summary>The list's mark: Attention, Fragile or Ok.</summary>
    public AccessHealth Health { get; internal init; }

    /// <summary>
    /// Decision 8g, watches only: some semester section of a watched course lacks the watched tag. False for
    /// analyses that were not given a watch.
    /// </summary>
    public bool HasUntaggedSections { get; internal init; }

    /// <summary>
    /// Why a section is stranded (as the mock's <c>strandedReason</c>): the always-used section it conflicts with in
    /// time, or at another campus under the one-campus rule, or no combination fits around it.
    /// </summary>
    /// <param name="section">A section of this analysis (normally one in <see cref="StrandedSections"/>).</param>
    /// <returns>The structured reason.</returns>
    public StrandedReason StrandedReasonFor(Section section) =>
        AccessReasons.Stranded(section, Groups, Roles, OneCampusRule);
}

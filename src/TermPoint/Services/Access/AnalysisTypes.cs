using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>A section's role in the slates of an analysis (rules 20 and 21).</summary>
public enum SectionRole
{
    /// <summary>There is no full slate, so no section has a role.</summary>
    None,
    /// <summary>In at least one slate, and not essential.</summary>
    Usable,
    /// <summary>In no slate: no student taking these courses can use it.</summary>
    Stranded,
    /// <summary>In every slate because its component's other sections are all stranded.</summary>
    EssentialByTiming,
    /// <summary>In every slate because it is the only section of its component.</summary>
    OnlySection,
}

/// <summary>How well a set of courses is doing, for the list's mark (the mock's "attn", "fragile" and "ok").</summary>
public enum AccessHealth
{
    /// <summary>Nothing needs a look.</summary>
    Ok,
    /// <summary>A full slate exists but depends on one section, or every slate mixes campuses.</summary>
    Fragile,
    /// <summary>No full slate, a missing component, or sections not counted.</summary>
    Attention,
    /// <summary>
    /// More courses than <see cref="AccessAnalyzer.MaxCourses"/>: not analysed (Q53). The watch says it is too big
    /// to check.
    /// </summary>
    TooLarge,
}

/// <summary>Why a course cannot be taken even on its own (rule 6d, gap 1).</summary>
public enum SelfFailKind
{
    /// <summary>One component has a single section, and every section of another component conflicts with it in time.</summary>
    TimeConflict,
    /// <summary>One component has a single section, and every section of another is at a different campus (one-campus rule).</summary>
    Campus,
    /// <summary>No set of the course's sections fits together, and no single section is to blame.</summary>
    NoFit,
}

/// <summary>
/// A structured reason a course cannot be taken on its own.
/// </summary>
/// <param name="Kind">What kind of reason.</param>
/// <param name="OnlyGroup">For <see cref="SelfFailKind.TimeConflict"/> and <see cref="SelfFailKind.Campus"/>: the component with a single section (its one candidate is "its only X").</param>
/// <param name="OtherGroup">For those kinds: the other component whose sections all fail against that section (one or many candidates).</param>
public sealed record SelfFailReason(SelfFailKind Kind, ComponentGroup? OnlyGroup, ComponentGroup? OtherGroup);

/// <summary>A course that cannot be taken on its own, with the reason.</summary>
/// <param name="CourseId">The course id.</param>
/// <param name="Reason">Why.</param>
public sealed record SelfFailCourse(string CourseId, SelfFailReason Reason);

/// <summary>
/// The best partial slate when there is no full one (rule 23): a slate for the largest subset of courses that
/// has one.
/// </summary>
/// <param name="Covers">How many courses the best partial slate covers (courses that fail alone are excluded first).</param>
/// <param name="Of">How many courses are in the analysis.</param>
/// <param name="Alternatives">
/// For each largest subset that fits, the courses it leaves out (all the same size). One alternative says
/// "A and B can't fit"; several say "A or B can't fit". Empty when everything outside the self-failing courses fits.
/// </param>
/// <param name="CantFit">The self-failing courses plus every course in some alternative.</param>
/// <param name="Complete">
/// False when the search was bounded (Q52, very large watches): <paramref name="Covers"/> is then "at least", and
/// the alternatives may be only some of them.
/// </param>
public sealed record BestPartial(
    int Covers,
    int Of,
    IReadOnlyList<IReadOnlyList<string>> Alternatives,
    IReadOnlyList<string> CantFit,
    bool Complete = true);

/// <summary>What the campus rule says about why there is no (one-campus) slate (rules 6d, 6e).</summary>
public enum CampusReasonKind
{
    /// <summary>Components with a single section sit on different campuses: the less common campus's and the more common campus's are named.</summary>
    OnlySectionsOnDifferentCampuses,
    /// <summary>No slate keeps to one campus, and no single-section components explain it.</summary>
    NoSlateKeepsToOneCampus,
}

/// <summary>
/// A structured campus reason. For <see cref="CampusReasonKind.OnlySectionsOnDifferentCampuses"/> the two lists
/// name the in-person single-section components on the campus with the fewest and the most of them.
/// </summary>
/// <param name="Kind">What kind of reason.</param>
/// <param name="LessCommonCampusId">The campus with fewer only-sections (ties: fewer sections overall); null for the general kind.</param>
/// <param name="LessCommonOnlySections">The only-sections on that campus.</param>
/// <param name="MoreCommonCampusId">The campus with the most only-sections; null for the general kind.</param>
/// <param name="MoreCommonOnlySections">The only-sections on that campus.</param>
public sealed record CampusReason(
    CampusReasonKind Kind,
    string? LessCommonCampusId,
    IReadOnlyList<Section> LessCommonOnlySections,
    string? MoreCommonCampusId,
    IReadOnlyList<Section> MoreCommonOnlySections);

/// <summary>What kept a stranded section out of every slate.</summary>
public enum StrandedKind
{
    /// <summary>It conflicts in time with a section every slate uses.</summary>
    ConflictsWithAlwaysUsed,
    /// <summary>One-campus rule: it is at another campus than a section every slate uses.</summary>
    CampusConflictsWithAlwaysUsed,
    /// <summary>No single section is to blame: no combination of the other courses fits around it.</summary>
    NoCombination,
}

/// <summary>A structured reason a section is stranded.</summary>
/// <param name="Kind">What kind of reason.</param>
/// <param name="Other">The always-used section it conflicts with; null for <see cref="StrandedKind.NoCombination"/>.</param>
public sealed record StrandedReason(StrandedKind Kind, Section? Other);

/// <summary>A pinned section that stops a full slate (rule 18), or "these pins don't fit together".</summary>
/// <param name="Pin">The pinned section; <c>null</c> when the pins conflict with each other and none alone is to blame.</param>
/// <param name="Reason">Why the pin is stranded when the other pins are kept; null when <paramref name="Pin"/> is null.</param>
public sealed record PinBlock(Section? Pin, StrandedReason? Reason);

/// <summary>Two sections that conflict in time.</summary>
/// <param name="A">The first section.</param>
/// <param name="B">The second section.</param>
public sealed record SectionPair(Section A, Section B);

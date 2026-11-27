using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// The wording of the picture the grid draws for an open watch: each tile's hover line (a port of the r9 wireframe's
/// <c>entry</c>) and the legend bar. Approved wording from rounds 3 to 9.
/// </summary>
public static partial class AccessText
{
    /// <summary>The legend bar's button that unpins every section.</summary>
    // approved wording (round 3)
    public const string UnpinAllText = "Unpin all";

    /// <summary>
    /// The Access tab's quiet line when some of the open watch's sections are not on the grid, e.g. "2 of Cohort 1's
    /// sections are hidden by the filters." DRAFT wording, for Greg.
    /// </summary>
    /// <param name="count">How many are hidden (at least 1).</param>
    /// <param name="name">The watch's (or course's) name.</param>
    /// <param name="byFocus">Whether the subject focus hides any of them.</param>
    /// <returns>The line.</returns>
    public static string HiddenWatchSectionsText(int count, string name, bool byFocus) =>
        $"{count} of {name}'s sections {(count == 1 ? "is" : "are")} hidden by the filters{(byFocus ? " or the chosen Subjects" : "")}.";

    /// <summary>The label of the "mixes campuses" tag on a slate.</summary>
    // approved wording (round 6e)
    public const string MixesCampusesText = "mixes campuses";

    /// <summary>The right-click menu's text for a section that is already pinned.</summary>
    // approved wording (round 3)
    public const string UnpinText = "Unpin";

    /// <summary>The right-click menu's text for pinning a section in the open watch's slates.</summary>
    /// <param name="watchName">The open watch's name.</param>
    /// <returns>"Pin in slates for Chem Y2".</returns>
    // approved wording (round 3)
    public static string PinInSlatesText(string watchName) => $"Pin in slates for {watchName}";

    /// <summary>The right-click menu's text for keeping a section in place: no-full-slate suggestions won't move it.</summary>
    // approved wording (fewest moves, 2026-11-26)
    public const string KeepInPlaceText = "Don't suggest moving this";

    /// <summary>The right-click menu's text for a section kept in place, to let suggestions move it again.</summary>
    // approved wording (fewest moves, 2026-11-26)
    public const string LetMoveText = "Suggest moving this again";

    /// <summary>
    /// The hover line of a section in a move suggestion, on its tile and on its conflict marks: what it conflicts
    /// with and which sections to move (<see cref="MoveSuggestions"/>). Read with the ▲ on the sections to move.
    /// </summary>
    /// <param name="inputs">The semester view (for course codes).</param>
    /// <param name="section">The section the line is about.</param>
    /// <param name="conflictsWith">The sections it conflicts with in the suggestion.</param>
    /// <param name="problem">The problem: moving any one of its sections fixes it.</param>
    /// <param name="problemCount">How many separate problems the watch has (1 or 2).</param>
    /// <param name="watchName">The watch's name, already bold where it should be.</param>
    /// <returns>"CIVL303 T1A conflicts with CIVL315 101, so CIVL-Y3 has no full slate. Try moving CIVL303 T1A or CIVL315 101."</returns>
    // approved wording (fewest moves, 2026-11-26)
    public static string MoveTip(AccessInputs inputs, Section section, IReadOnlyList<Section> conflictsWith,
        MoveProblem problem, int problemCount, string watchName)
    {
        string movers = ByCourseOr(inputs, problem.Options.Select(o => o.Section));
        string conflict = $"{SectionLabel(inputs, section)} conflicts with {ByCourse(inputs, conflictsWith)}";
        return problemCount <= 1
            ? $"{conflict}, so {watchName} has no full slate. Try moving {movers}."
            : $"{conflict}. This is one of {problemCount} separate problems keeping {watchName} from a full slate; moving {movers} fixes it.";
    }

    /// <summary>The hover line of a section the user has kept in place, while the watch has no full slate.</summary>
    /// <param name="label">The section's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (fewest moves, 2026-11-26)
    public static string KeptInPlaceTip(string label) => $"{label} is kept in place: suggestions won't ask to move it.";

    /// <summary>"Slate 3 of 12" (or "Slate 3 of more than 10,000" when the count was capped).</summary>
    /// <param name="index">The slate's position, counted from zero.</param>
    /// <param name="total">How many slates there are.</param>
    /// <param name="capped">Whether the count stopped at the cap.</param>
    /// <returns>The text.</returns>
    // approved wording (round 3)
    public static string SlatePosition(int index, int total, bool capped) =>
        $"Slate {(index + 1).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} of "
        + (capped ? "more than 10,000" : total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The tooltip of a slate's "mixes campuses" tag.</summary>
    /// <param name="campusNames">The names of the campuses its in-person sections are on.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 6e)
    public static string MixesCampusesTip(IReadOnlyList<string> campusNames) =>
        $"This slate has in-person sections on {JoinAnd(campusNames)}.";

    // ── A tile's hover line ──────────────────────────────────────────────────────────────────────────

    /// <summary>The hover line of a section of an unticked course.</summary>
    /// <param name="courseCode">The course's code.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string UntickedTip(string courseCode) => $"{courseCode} is unticked, so it isn't part of these slates.";

    /// <summary>The hover line of a section left out because another section of its component is pinned.</summary>
    /// <param name="pinnedLabel">The pinned section's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string NotUsedPinnedTip(string pinnedLabel) => $"Not used: {pinnedLabel} is pinned.";

    /// <summary>The hover line of a section in the slate being shown.</summary>
    /// <param name="label">The section's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string InThisSlateTip(string label) => $"{label} is in this slate.";

    /// <summary>The hover line of a section that cannot be part of any slate.</summary>
    /// <param name="label">The section's name.</param>
    /// <param name="watchName">The watch's name (or the course's code).</param>
    /// <param name="why">Why (<see cref="StrandedWhy"/>).</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 6)
    public static string StrandedTip(string label, string watchName, string why) =>
        $"{label} can't be part of any slate for {watchName}: {why}";

    /// <summary>The hover line of a pinned section that no slate can use.</summary>
    /// <param name="label">The section's name.</param>
    /// <param name="why">Why (<see cref="StrandedWhy"/>).</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string PinnedCantFitTip(string label, string why) => $"Pinned {label} can't be part of any slate: {why}";

    /// <summary>The hover line of a section of a course that cannot be taken even alone.</summary>
    /// <param name="courseCode">The course's code.</param>
    /// <param name="why">Why (<see cref="SelfFailWhy"/>).</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 6d)
    public static string CourseCantBeTakenTip(string courseCode, string why) => $"{courseCode} can't be taken: {why}.";

    /// <summary>The hover line of a section of a course that cannot fit with the rest of the watch.</summary>
    /// <param name="courseCode">The course's code.</param>
    /// <param name="watchName">The watch's name.</param>
    /// <param name="why">Why: the campus reason, or null when no section fits around the other courses.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 6d)
    public static string CourseCantFitTip(string courseCode, string watchName, string? why) =>
        $"{courseCode} can't fit with the rest of {watchName}: {why ?? $"no {courseCode} section fits around the other courses"}.";

    /// <summary>The hover line of a section every slate uses because its component's other sections can't fit.</summary>
    /// <param name="label">The section's name.</param>
    /// <param name="courseCode">The course's code.</param>
    /// <param name="watchName">The watch's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string EssentialTip(string label, string courseCode, string watchName) =>
        $"Every slate for {watchName} uses {label}: {courseCode}'s other sections can't fit. If it moves to a conflicting time, students lose every full slate.";

    /// <summary>The hover line of a pinned section.</summary>
    /// <param name="label">The section's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string PinnedTip(string label) => $"{label} is pinned: only slates with it count.";

    /// <summary>The hover line of the only section of its component.</summary>
    /// <param name="label">The section's name.</param>
    /// <param name="courseCode">The course's code.</param>
    /// <param name="componentWord">The component's name in lower case, or "section" for a course with fewer than two components.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string OnlySectionTip(string label, string courseCode, string componentWord) =>
        $"{label} is the only {courseCode} {componentWord}, so every slate uses it.";

    /// <summary>The hover line of a section that is in at least one slate.</summary>
    /// <param name="label">The section's name.</param>
    /// <param name="watchName">The watch's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 3)
    public static string UsableTip(string label, string watchName) => $"{label} can be part of a slate for {watchName}.";

    /// <summary>The hover line of a section that fills no component.</summary>
    /// <param name="label">The section's name.</param>
    /// <returns>The sentence.</returns>
    // approved wording (round 6)
    public static string NotCountedTip(string label) => $"{label} isn't counted (no section type).";

    /// <summary>
    /// The hover line of a section whose course is left out of the slates because one of its components has no
    /// sections in the watch (rules 10 and 6d), e.g. "MANU330 L0A isn't counted: MANU330's lecture sections aren't in
    /// CHBE-Y2." DRAFT wording, for Greg (2026-11-25).
    /// </summary>
    /// <param name="label">The section's name.</param>
    /// <param name="courseCode">The course's code.</param>
    /// <param name="componentWords">The missing components' names in lower case, e.g. ["lecture"].</param>
    /// <param name="watchName">The watch's name (already bold where wanted).</param>
    /// <returns>The sentence.</returns>
    public static string MissingComponentTip(string label, string courseCode, IReadOnlyList<string> componentWords, string watchName) =>
        $"{label} isn't counted: {courseCode}'s {JoinAnd(componentWords)} sections aren't in {watchName}.";

    /// <summary>
    /// A watch's name in bold, as <see cref="Helpers.InlineFormatter"/> markup, for the grid's hover lines (Greg,
    /// 2026-11-25: every mention of a watch's name is bold there). Asterisks inside the name are dropped, since the
    /// markup cannot carry them.
    /// </summary>
    /// <param name="watchName">The watch's name.</param>
    /// <returns>The marked-up name.</returns>
    public static string Bold(string watchName) => $"**{watchName.Replace("*", string.Empty)}**";
}

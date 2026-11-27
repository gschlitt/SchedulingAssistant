using System.Text.RegularExpressions;
using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// One phrase of a course row in a watch's detail: what a student taking the course can pick from one component
/// (round 6c), such as "Lab: all 3", "only A" or "Lab: 4 of 8 ▸".
/// </summary>
/// <param name="Key">Identifies the phrase for expanding it ("courseId|componentTypeId").</param>
/// <param name="Label">The component's name and a colon ("Lab: ") for a course with two or more components; empty otherwise.</param>
/// <param name="Text">The phrase: "all 4 sections", "only A", "all but L2", "LEC1, LEC2", "4 of 8 labs" or "none".</param>
/// <param name="IsExpandable">True for the "N of M" form, which expands into <paramref name="ExpandedLine"/>.</param>
/// <param name="ExpandedLine">The line shown when expanded: the sections the watch uses and the ones left out; null unless expandable.</param>
/// <param name="IsNone">True when no section of this component is in the watch (drawn quietly).</param>
public sealed record CandidatePhrase(string Key, string Label, string Text, bool IsExpandable, string? ExpandedLine, bool IsNone);

/// <summary>
/// The wording of a watch's detail in the Access tab (a port of the r9 wireframe's <c>detailView</c>,
/// <c>candText</c>, <c>cautionsFor</c> messages and <c>strandedReason</c>), and of the lines the grid's tiles say
/// about a watch. Approved wording from rounds 5 to 9; "access checks" is "access watches" in user text.
/// </summary>
public static partial class AccessText
{
    /// <summary>The very quiet line under the flags when a watched course has sections without the tag (decision 8g).</summary>
    // approved wording (Greg, 2026-11-23)
    public const string UntaggedLine = "Some sections of these courses are not tagged";

    /// <summary>The hint under the courses while there are slates to step through.</summary>
    // approved wording (round 3)
    public const string StepHint = "Right-click a section on the grid to pin it.";

    /// <summary>The tooltip of the green eye that stops a watch (the detail's Watching button, and the eye at the left of each row of the list) when it can be clicked.</summary>
    // approved wording (round 5)
    public const string UnwatchTip = "Click to stop watching (the tag stays)";

    /// <summary>What a watch's eye says when stopping the watch failed.</summary>
    public const string WatchNotChangedText = "The watch couldn't be changed. Your work is safe; try again.";

    /// <summary>The tooltip of a control that cannot change anything while this computer cannot save.</summary>
    // approved wording (round 5)
    public const string ReadOnlyTip = "Read-only: you can look at watches but not change them.";

    /// <summary>The Multi-campus box's label.</summary>
    // approved wording (round 6e)
    public const string MultiCampusLabel = "Multi-campus";

    /// <summary>The Multi-campus box's tooltip.</summary>
    // approved wording (round 6e)
    public const string MultiCampusTip =
        "Ticked: count slates whose in-person sections are on more than one campus. Unticked: a slate keeps to one campus. Saved with the watch.";

    /// <summary>The link that ticks every course again.</summary>
    // approved wording (round 3)
    public const string TickAllText = "Tick all";

    /// <summary>The tooltip of the last ticked course's box.</summary>
    // approved wording (round 3)
    public const string LastTickTip = "At least one course stays ticked.";

    /// <summary>The detail's back button when it was opened from the list.</summary>
    // approved wording (round 3)
    public const string BackToWatchesText = "All watches";

    /// <summary>The detail's back button when it was opened from Check all courses.</summary>
    // approved wording (round 9)
    public const string BackToCheckText = "Check all courses";

    /// <summary>The heading of the Access tab's list of watches.</summary>
    // approved wording (Greg, 2026-11-26)
    public const string ListHeadingText = "Access Watches";

    /// <summary>The Access tab's button that makes a watch (it opens the Tags popup with its note).</summary>
    // approved wording (round 5)
    public const string AddWatchButtonText = "+ Add a watch";

    /// <summary>The Mark all conflicts box, at the foot of the Access tab and in the legend bar.</summary>
    // approved wording (round 6b)
    public const string MarkAllConflictsText = "Mark all conflicts";

    /// <summary>The Mark all conflicts box's hover.</summary>
    // approved wording (round 6, Greg's own)
    public const string MarkAllConflictsTip =
        "Marks all conflicts, even those students can avoid by registering in different slates. Removing them can still improve access.";

    /// <summary>A notice's link that opens the watch it is about.</summary>
    // approved wording (round 7)
    public const string NoticeOpenText = "Open";

    /// <summary>The hover of a notice's ✕.</summary>
    // approved wording (round 7)
    public const string NoticeDismissTip = "Dismiss";

    /// <summary>The hover of the stepper's ‹ button.</summary>
    // approved wording (round 3)
    public const string PreviousSlateTip = "Previous slate";

    /// <summary>The hover of the stepper's › button.</summary>
    // approved wording (round 3)
    public const string NextSlateTip = "Next slate";

    /// <summary>The bold title above a watch's courses.</summary>
    // approved wording (Greg, 2026-11-26)
    public const string SectionsWatchedText = "Sections Watched";

    /// <summary>The source line of a course checked on its own.</summary>
    /// <param name="courseCode">The course's calendar code.</param>
    /// <returns>The line.</returns>
    // approved wording (round 9)
    public static string SourceCheckText(string courseCode) =>
        $"{courseCode} checked on its own: all its sections this term. Not a watch; nothing is saved.";

    /// <summary>The start of the pin banner (the pinned sections follow as chips).</summary>
    // approved wording (round 3)
    public const string PinBannerLead = "Only slates with ";

    /// <summary>The end of the pin banner.</summary>
    // approved wording (round 3)
    public const string PinBannerTail = " are shown.";

    /// <summary>The label of the fold that holds the quieter facts.</summary>
    /// <param name="isCheck">True for a course checked on its own.</param>
    /// <param name="count">How many facts it holds.</param>
    /// <returns>The label without a chevron.</returns>
    // approved wording (round 3)
    public static string FoldMoreText(bool isCheck, int count) => $"More about this {(isCheck ? "course" : "watch")} ({count})";

    /// <summary>The label of the open fold.</summary>
    // approved wording (round 3)
    public const string FoldLessText = "Less";

    /// <summary>"12 of 30 courses": what a ticked subset says about itself (the mock's <c>subset</c>).</summary>
    /// <param name="analysis">The analysis (a subset).</param>
    /// <returns>The text; empty when every course is ticked.</returns>
    // approved wording (round 3)
    public static string SubsetText(Analysis analysis) =>
        analysis.IsSubset ? $"{analysis.ChosenCourseIds.Count} of {analysis.OfferedCourseIds.Count} courses" : string.Empty;

    // ── Why a section is stranded ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why a section can't be part of any slate (the mock's <c>strandedReason</c>): the section every slate uses that
    /// it conflicts with, or that is at another campus (the one-campus rule), or that no combination fits around it.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="section">The stranded section.</param>
    /// <param name="reason">The structured reason (<see cref="Analysis.StrandedReasonFor"/>).</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <param name="mentionMultiCampus">True to add "(Multi-campus is off)" to a campus reason; false for a course checked alone, where the box does not exist.</param>
    /// <returns>A sentence starting with "it" and ending with a full stop.</returns>
    // approved wording (round 6d)
    public static string StrandedWhy(
        AccessInputs inputs, Section section, StrandedReason reason, Func<string?, string> campusName, bool mentionMultiCampus = true)
    {
        switch (reason.Kind)
        {
            case StrandedKind.ConflictsWithAlwaysUsed when reason.Other is { } other:
                return $"it conflicts with {SectionLabel(inputs, other)}, which every slate uses.";
            case StrandedKind.CampusConflictsWithAlwaysUsed when reason.Other is { } away:
                return $"it is at {campusName(section.CampusId)}, and every slate uses {SectionLabel(inputs, away)} at {campusName(away.CampusId)}"
                     + $"{(mentionMultiCampus ? " (Multi-campus is off)" : string.Empty)}.";
            default:
                return "no combination of the other courses fits around it.";
        }
    }

    /// <summary>
    /// A pin that stops a full slate, as a phrase of the health line: "CHEM 213 A (pinned): conflicts with PSYCH 101 A",
    /// or "these pins don't fit together." when no one pin is to blame.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="block">The pin block.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The phrase.</returns>
    // approved wording (round 3)
    public static string PinBlockPhrase(AccessInputs inputs, PinBlock block, Func<string?, string> campusName)
    {
        if (block.Pin is not { } pin || block.Reason is not { } reason) return "these pins don't fit together.";
        var why = StrandedWhy(inputs, pin, reason, campusName);
        why = Regex.Replace(why, "^it ", string.Empty);
        why = Regex.Replace(why, @", which every slate uses\.$", string.Empty);
        why = Regex.Replace(why, @"\.$", string.Empty);
        return $"{SectionLabel(inputs, pin)} (pinned): {why}";
    }

    // ── Flags of the detail ──────────────────────────────────────────────────────────────────────────

    /// <summary>The flag for a stranded section.</summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="analysis">The analysis (for its stranded reasons and subset).</param>
    /// <param name="section">The stranded section.</param>
    /// <param name="name">The watch's name, or the checked course's code.</param>
    /// <param name="isCheck">True for a course checked on its own.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The sentence (without the mark).</returns>
    // approved wording (round 7)
    public static string StrandedFlagText(
        AccessInputs inputs, Analysis analysis, Section section, string name, bool isCheck, Func<string?, string> campusName)
    {
        string why = StrandedWhy(inputs, section, analysis.StrandedReasonFor(section), campusName, !isCheck);
        string whom = analysis.IsSubset ? "these courses" : isCheck ? name : "all of " + name;
        return $"{SectionLabel(inputs, section)} is stranded: {why} No student taking {whom} can use it.";
    }

    /// <summary>The flag for a component with no sections in the watch.</summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="missing">The missing component.</param>
    /// <returns>The sentence (without the mark).</returns>
    // approved wording (round 6d)
    public static string MissingComponentFlagText(AccessInputs inputs, MissingComponent missing)
    {
        string course = inputs.CourseCode(missing.CourseId);
        string type = inputs.SectionTypes.FirstOrDefault(t => t.Id == missing.ComponentTypeId)?.Name ?? "component";
        return $"{course}'s {type.ToLowerInvariant()} sections aren't in this watch. Add one, or change {course}'s components under Courses if it no longer has one.";
    }

    /// <summary>The flag for a section that fills no component because it has no section type.</summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="section">The section.</param>
    /// <returns>The sentence (without the mark).</returns>
    // approved wording (round 6)
    public static string NotCountedFlagText(AccessInputs inputs, Section section) =>
        $"{SectionLabel(inputs, section)} has no section type, so it isn't counted.";

    /// <summary>
    /// A yellow caution as a sentence (the mock's <c>cautionsFor</c> text). Another section of the same course is
    /// named by its code alone; sections of other courses by course and code.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="caution">The caution.</param>
    /// <returns>The sentence (without the mark).</returns>
    // approved wording (round 3 review)
    public static string CautionText(AccessInputs inputs, Caution caution)
    {
        var s = caution.Section;
        string F(Section t, Section? relativeTo = null) =>
            relativeTo is not null && t.CourseId == relativeTo.CourseId ? CodeOf(t) : SectionLabel(inputs, t);
        if (caution.Kind == CautionKind.HardToReach)
        {
            if (caution.BlockingGroup is not { } h)
                return $"{F(s)} is hard to reach: few combinations of the other courses fit around it.";
            var fits = caution.FitsWith;
            var conflicting = h.Candidates.Where(t => !fits.Any(f => f.Id == t.Id)).Select(t => F(t, s)).ToList();
            return fits.Count == 1
                ? $"{F(s)} fits only with {F(fits[0], s)}: it conflicts with {JoinAnd(conflicting)}."
                : $"{F(s)} fits with only {fits.Count} of {h.Candidates.Count} {inputs.CourseCode(h.CourseId)} sections: it conflicts with {JoinAnd(conflicting)}.";
        }
        if (caution.Leaners.Count > 0)
            return $"{F(s)} carries a heavy load: {JoinAnd(caution.Leaners.Select(u => F(u, s)).ToList())} "
                 + $"{(caution.Leaners.Count == 1 ? "fits" : "fit")} only with it.";
        return $"{F(s)} carries a heavy load: it is in far more slates than the other {inputs.CourseCode(caution.Group.CourseId)} "
             + $"{PluralComponentWord(inputs, caution.Group.ComponentTypeId)}, which conflict more.";
    }

    /// <summary>"labs", "lectures", "discussions" or "sections": the mock's fixed plurals for a component's name.</summary>
    private static string PluralComponentWord(AccessInputs inputs, string? typeId)
    {
        var name = typeId is null ? null : inputs.SectionTypes.FirstOrDefault(t => t.Id == typeId)?.Name;
        return name?.ToLowerInvariant() switch
        {
            "laboratory" => "labs",
            "lecture" => "lectures",
            "discussion" => "discussions",
            _ => "sections",
        };
    }

    // ── The quieter facts ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The facts folded under "More about this watch": the only sections every slate uses, and the sections with no
    /// times that the analysis assumes fit anywhere. (Seats are a later piece.)
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The lines; empty when there is nothing to say.</returns>
    // approved wording (round 3)
    public static IReadOnlyList<string> QuieterFacts(AccessInputs inputs, Analysis analysis)
    {
        var facts = new List<string>();
        if (analysis.OnlySections.Count > 0 && !analysis.IsSingleSectionCase)
            facts.Add($"Only sections, so every slate uses them: {string.Join(", ", analysis.OnlySections.Select(s => SectionLabel(inputs, s)))}.");
        if (analysis.SectionsWithoutTimes.Count > 0)
            facts.Add($"Assumes {string.Join(", ", analysis.SectionsWithoutTimes.Select(s => SectionLabel(inputs, s)))} (no times) fits anywhere.");
        return facts;
    }

    // ── A course's row: what a student takes ─────────────────────────────────────────────────────────

    /// <summary>
    /// The sections of a course that the watch's slates can use, one phrase per component, shortest that is exact (the
    /// mock's <c>candText</c>, round 6c): "all 4 sections", "only A", "all but L2" (at most two left out, fewer than
    /// are in), "LEC1, LEC2" (up to three), else "4 of 8 labs", which expands into a line naming them and the ones
    /// left out. "All" counts the course's sections in the whole semester (rule 29), so grid filters never change it.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="courseId">The course.</param>
    /// <param name="coveredSectionIds">The ids of the sections the watch covers.</param>
    /// <returns>One phrase per component (one phrase for a course with fewer than two components).</returns>
    // approved wording (round 6c)
    public static IReadOnlyList<CandidatePhrase> CandidatePhrases(
        AccessInputs inputs, string courseId, IReadOnlySet<string> coveredSectionIds)
    {
        var components = inputs.ComponentsOf(courseId);
        IReadOnlyList<string?> parts = components.Count >= 2 ? components.Cast<string?>().ToList() : [null];
        var inSemester = inputs.SectionsByCourse.TryGetValue(courseId, out var all) ? all : [];
        var phrases = new List<CandidatePhrase>();
        foreach (var part in parts)
        {
            var every = inSemester.Where(s => part is null || s.SectionTypeId == part).ToList();
            var mine = every.Where(s => coveredSectionIds.Contains(s.Id)).ToList();
            var left = every.Where(s => !coveredSectionIds.Contains(s.Id)).ToList();
            string noun = part is null ? "section" : PartWord(inputs, part);
            string nouns = part is null ? " sections" : string.Empty;
            // With a component label ("Lab: ") the noun is left out: "Lab: all 3", "Lab: 4 of 8 ▸", "Lab: none"
            string label = part is null ? string.Empty : $"{char.ToUpperInvariant(noun[0])}{noun[1..]}: ";
            string key = $"{courseId}|{part}";
            string Codes(IEnumerable<Section> xs) => string.Join(", ", xs.Select(CodeOf));

            if (mine.Count == 0)
                phrases.Add(new CandidatePhrase(key, label, part is null ? "no sections" : "none", false, null, true));
            else if (left.Count == 0)
                phrases.Add(new CandidatePhrase(key, label, every.Count == 1 ? $"only {CodeOf(mine[0])}" : $"all {every.Count}{nouns}", false, null, false));
            else if (mine.Count == 1)
                phrases.Add(new CandidatePhrase(key, label, $"only {CodeOf(mine[0])}", false, null, false));
            else if (left.Count <= 2 && left.Count < mine.Count)
                phrases.Add(new CandidatePhrase(key, label, $"all but {Codes(left)}", false, null, false));
            else if (mine.Count <= 3)
                phrases.Add(new CandidatePhrase(key, label, Codes(mine), false, null, false));
            else
                phrases.Add(new CandidatePhrase(key, label, $"{mine.Count} of {every.Count}{nouns}", true,
                    $"{label}{Codes(mine)} · not in this watch: {Codes(left)}", false));
        }
        return phrases;
    }
}

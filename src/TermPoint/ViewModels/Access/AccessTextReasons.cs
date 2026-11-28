using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// The wording of the narratives (<see cref="StrandedReasons"/>): why a section is stranded and why a move is needed,
/// as facts with their reasons, never what-ifs (Greg, 2026-11-27, "Clarity design"). The grid's hovers say the last
/// step; the Access panel lists the whole chain (<see cref="WhyLines"/>). All wording here is approved by Greg.
/// </summary>
public static partial class AccessText
{
    /// <summary>The day names of a common time, indexed by day number (1 = Monday).</summary>
    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    /// <summary>
    /// Names sections grouped by course, each course's codes joined with "and": "EOSC329 L1B and L1C", "EOSC321 L1A,
    /// EOSC323 L1A and L1D and EOSC329 L1A". The narratives' approved examples read this way; <see cref="ByCourse"/>
    /// keeps commas inside a course ("PSYCH 101 A, B, C") for the older approved sentences, so it is not changed.
    /// </summary>
    /// <param name="inputs">The semester view (for course codes).</param>
    /// <param name="sections">The sections, in the order to name them.</param>
    /// <returns>The text.</returns>
    private static string NamesAnd(AccessInputs inputs, IEnumerable<Section> sections) =>
        JoinAnd(sections.GroupBy(s => s.CourseId ?? string.Empty)
            .Select(g => (g.Key.Length == 0 ? string.Empty : inputs.CourseCode(g.Key) + " ") + JoinAnd(g.Select(CodeOf).ToList()))
            .ToList());

    /// <summary>
    /// The course code and component noun the sections of one group are named by: "EOSC321" and "lab" (or "labs"
    /// when there are several). Derived from the sections, since a group's component is its sections' type when the
    /// course has two or more components, and "section" otherwise.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="against">Sections of one group (at least one).</param>
    /// <returns>The code and the noun, singular or plural by the count.</returns>
    private static (string Code, string Noun) ComponentWords(AccessInputs inputs, IReadOnlyList<Section> against)
    {
        var first = against[0];
        string code = first.CourseId is null ? string.Empty : inputs.CourseCode(first.CourseId);
        string? typeId = first.CourseId is not null && inputs.ComponentsOf(first.CourseId).Count >= 2 ? first.SectionTypeId : null;
        string part = PartWord(inputs, typeId);
        return (code, against.Count > 1 ? part + "s" : part);
    }

    /// <summary>
    /// Says why the sections of a clash can't be avoided: "CIVL332's only discussion", "the only EOSC321 lab they can
    /// take" (earlier steps took the other sections away; no "still", Greg 2026-11-27: it suggested something had just
    /// changed), with plurals for several sections.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="against">The whole remaining domain of the group (at least one section).</param>
    /// <param name="narrowed">True when earlier steps removed sections from the group.</param>
    /// <param name="who">"they" for a hover that names the watch, "students" where the sentence has no earlier subject.</param>
    /// <returns>The qualifier, with no full stop.</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static string Qualifier(AccessInputs inputs, IReadOnlyList<Section> against, bool narrowed, string who = "they")
    {
        var (code, noun) = ComponentWords(inputs, against);
        return narrowed ? $"the only {code} {noun} {who} can take" : $"{code}'s only {noun}";
    }

    /// <summary>"Students in **W**" for a watch (the name already bold); "Students taking CIVL315" for a course checked alone.</summary>
    /// <param name="name">The watch's name (already bold) or the checked course's code.</param>
    /// <param name="isCourseOnly">True for a course checked on its own.</param>
    /// <returns>The subject of the hover sentence.</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static string StudentsIn(string name, bool isCourseOnly) =>
        isCourseOnly ? $"Students taking {name}" : $"Students in {name}";

    /// <summary>The hover of a stranded tile that the narrative explains: "Students in **W** can't take X: it conflicts with …".</summary>
    /// <param name="studentsIn">See <see cref="StudentsIn"/>.</param>
    /// <param name="label">The stranded section's name.</param>
    /// <param name="clause">Why (<see cref="StrandedClause"/>).</param>
    /// <returns>The sentence.</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static string StrandedNarrativeTip(string studentsIn, string label, string clause) =>
        $"{studentsIn} can't take {label}: {clause}";

    /// <summary>
    /// The sentence a clash gives for sections it rules out, as the grid's mark hover says it: "Students in **W** can't
    /// take X: it conflicts with …" for one section, "… can't take X or Y: they conflict with …" for several.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="watchName">The watch's name, already bold.</param>
    /// <param name="ruledOut">The stranded sections (at least one).</param>
    /// <param name="against">The clash's remaining domain.</param>
    /// <param name="narrowed">Whether earlier steps narrowed it.</param>
    /// <returns>The sentence.</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static string ClashTip(AccessInputs inputs, string watchName, IReadOnlyList<Section> ruledOut,
        IReadOnlyList<Section> against, bool narrowed) =>
        ruledOut.Count == 1
            ? $"Students in {watchName} can't take {SectionLabel(inputs, ruledOut[0])}: it conflicts with {NamesAnd(inputs, against)}, {Qualifier(inputs, against, narrowed)}."
            : $"Students in {watchName} can't take {ByCourseOr(inputs, ruledOut)}: they conflict with {NamesAnd(inputs, against)}, {Qualifier(inputs, against, narrowed)}.";

    /// <summary>
    /// Why a stranded section is out, as the clause after "can't take X:" or "X is stranded:", from its narrative's
    /// last step: the clash it can't get round, or the footprint every way of filling some components makes.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="a">The analysis (for its narrative and groups).</param>
    /// <param name="x">The stranded section.</param>
    /// <returns>The clause ending in a full stop; null when the narrative doesn't explain the section, or its clash is
    /// partly a campus split (the old wording then says it).</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static string? StrandedClause(AccessInputs inputs, Analysis a, Section x)
    {
        if (a.Reasons.StepFor(x) is not { } step) return null;
        if (step.Kind == ReasonStepKind.Clash)
            return step.Against.All(y => SectionOverlap.Conflicts(x, y))
                ? $"it conflicts with {NamesAnd(inputs, step.Against)}, {Qualifier(inputs, step.Against, step.Narrowed)}."
                : null;
        string comps = FootprintComponents(inputs, a, step);
        return StrandedReasons.OverlapsCommonTimes(step, x)
            ? $"whichever {comps} they take, {CommonTimesText(step.CommonTimes)} {(step.CommonTimes.Count == 1 ? "is" : "are")} taken."
            : $"whichever {comps} they take, one of them conflicts with it.";
    }

    /// <summary>The components of a footprint, named by course and part: "CIVL311 discussion, CIVL311 lab and EOSC350 lab".</summary>
    private static string FootprintComponents(AccessInputs inputs, Analysis a, ReasonStep step) =>
        JoinAnd(step.Groups.Select(g => $"{inputs.CourseCode(a.Groups[g].CourseId)} {PartWord(inputs, a.Groups[g].ComponentTypeId)}").ToList());

    /// <summary>
    /// Common times as "Mon, Tue and Wed 16:00–17:00": days that share a range are joined, separate ranges joined
    /// with "and".
    /// </summary>
    /// <param name="times">The common times (at least one).</param>
    /// <returns>The text.</returns>
    public static string CommonTimesText(IReadOnlyList<CommonTime> times) =>
        JoinAnd(times.GroupBy(t => (t.StartMinutes, t.EndMinutes))
            .OrderBy(g => g.Min(t => t.Day)).ThenBy(g => g.Key.StartMinutes)
            .Select(g => $"{JoinAnd(g.OrderBy(t => t.Day).Select(t => DayName(t.Day)).ToList())} {Clock(g.Key.StartMinutes)}–{Clock(g.Key.EndMinutes)}")
            .ToList());

    /// <summary>A day number as its short name ("Mon"); "day N" for a number outside 1-7.</summary>
    private static string DayName(int day) => day is >= 1 and <= 7 ? DayNames[day] : $"day {day}";

    /// <summary>Minutes from midnight as "H:mm".</summary>
    private static string Clock(int minutes) => $"{minutes / 60}:{minutes % 60:00}";

    // ── The "Why" list ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The numbered "Why" list of a stranded section: one line per step its narrative rests on, in order, ending with
    /// the step that rules the section out. Empty when the chain is a single step (the hover already says it all) or
    /// the section is not explained.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="a">The analysis.</param>
    /// <param name="section">The stranded section.</param>
    /// <returns>The lines, each prefixed "1. ", "2. " and so on; empty for none.</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static IReadOnlyList<string> WhyLines(AccessInputs inputs, Analysis a, Section section)
    {
        var chain = a.Reasons.ChainFor(section);
        if (chain.Count < 2) return [];
        return chain.Select((step, i) => $"{i + 1}. {WhyLine(inputs, a, step, i == 0)}").ToList();
    }

    /// <summary>
    /// The "Why" list of a section a move suggestion offers to move (Greg, 2026-11-27: the hover's "the only EOSC329
    /// labs students can take" begs "why not the others?"): the steps that took the group's other sections away,
    /// then "That leaves EOSC329 L1B and L1C, and EOSC331 101 conflicts with both."
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="a">The analysis (its groups name the components of a footprint).</param>
    /// <param name="option">The section to move.</param>
    /// <param name="reason">Its move reason.</param>
    /// <returns>The numbered lines; empty when nothing was ruled out first (the hover already says it all).</returns>
    // approved wording (clarity, Greg 2026-11-27)
    public static IReadOnlyList<string> MoveWhyLines(AccessInputs inputs, Analysis a, Section option, MoveReason reason)
    {
        if (reason.Chain.Count == 0) return [];
        var lines = reason.Chain.Select((step, i) => $"{i + 1}. {WhyLine(inputs, a, step, i == 0)}").ToList();
        string them = reason.Against.Count == 1 ? "it" : reason.Against.Count == 2 ? "both" : "all of them";
        lines.Add($"{lines.Count + 1}. That leaves {NamesAnd(inputs, reason.Against)}, and {SectionLabel(inputs, option)} conflicts with {them}.");
        return lines;
    }

    /// <summary>
    /// Ends a tile's hover when its section has a "Why" list in the Access panel: hovers can't be clicked, and nothing
    /// else says the list is there.
    /// </summary>
    // approved wording (clarity, Greg 2026-11-27)
    public const string ClickForWhyText = "Click it to see why.";

    /// <summary>One line of a "Why" list.</summary>
    private static string WhyLine(AccessInputs inputs, Analysis a, ReasonStep step, bool first)
    {
        var ruled = step.RuledOut;
        if (step.Kind == ReasonStepKind.Footprint)
        {
            string comps = FootprintComponents(inputs, a, step);
            string they = first ? "students" : "they";
            if (step.CommonTimes.Count > 0)
                return $"Whichever {comps} {they} take, {CommonTimesText(step.CommonTimes)} {(step.CommonTimes.Count == 1 ? "is" : "are")} taken. "
                     + $"That rules out {NamesAnd(inputs, ruled)}.";
            return $"Whichever {comps} {they} take, one of them conflicts with "
                 + $"{(ruled.Count == 1 ? SectionLabel(inputs, ruled[0]) : "each of " + NamesAnd(inputs, ruled))}.";
        }
        var against = step.Against;
        var (code, noun) = ComponentWords(inputs, against);
        if (against.Count == 1)
            return step.Narrowed
                ? $"That leaves {SectionLabel(inputs, against[0])} as the only {code} {noun}, which rules out {NamesAnd(inputs, ruled)}."
                : $"{SectionLabel(inputs, against[0])} is {Qualifier(inputs, against, false)}, so students can't take "
                  + $"{ByCourseOr(inputs, ruled)}, which {(ruled.Count == 1 ? "conflicts" : "conflict")} with it.";
        string both = against.Count == 2 ? "both" : "all";
        return step.Narrowed
            ? $"That leaves {NamesAnd(inputs, against)} as the only {code} {noun}, and {both} conflict with {NamesAnd(inputs, ruled)}."
            : $"{NamesAnd(inputs, against)} are {Qualifier(inputs, against, false)}, and {both} conflict with {NamesAnd(inputs, ruled)}.";
    }
}

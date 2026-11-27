using System.Globalization;
using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>One conflict mark together with the watch that produced it, as the hover wording needs it.</summary>
/// <param name="WatchId">The watch's id (the watched tag's id); marks of one watch are spoken together.</param>
/// <param name="WatchName">The watch's name (the tag's name), used in the sentences.</param>
/// <param name="Mark">The mark.</param>
public sealed record WatchMark(string WatchId, string WatchName, ConflictMark Mark);

/// <summary>
/// One phrase of a watch's health line (the mock's <c>healthSegs</c> entries): its text, an optional hover that
/// explains it, and whether it is a warning (every slate mixes campuses, round 6e). A plain string converts to a
/// phrase with no hover, so most phrases are written as strings.
/// </summary>
/// <param name="Text">The phrase.</param>
/// <param name="Tip">The hover; null for none.</param>
/// <param name="Warn">Whether the phrase is drawn in the warning colour.</param>
public sealed record HealthPhrase(string Text, string? Tip = null, bool Warn = false)
{
    /// <summary>A phrase with no hover and no warning.</summary>
    /// <param name="text">The phrase.</param>
    public static implicit operator HealthPhrase(string text) => new(text);
}

/// <summary>Why a conflict's other section is not on the grid (Q41).</summary>
public enum HiddenReason
{
    /// <summary>Its subject is not one of the subjects chosen in the top banner (the subject focus).</summary>
    Subjects,
    /// <summary>The grid's filters leave it out.</summary>
    Filters,
}

/// <summary>
/// The wording of Access Analyzer messages, kept in this one UI-side class and never in the engine (decision D29),
/// so the grid's hover and, later, the Access tab say the same thing. A port of the r9 wireframe's
/// <c>conflictTips</c>, <c>byCourse</c> and <c>joinAnd</c> (designs/wireframes/access-panel-r9.html). Pure and static.
/// </summary>
public static partial class AccessText
{
    /// <summary>
    /// The scheme of a section link's target in InlineFormatter markup: <c>[CHEM 213 A](section:id)</c>. The view
    /// draws such a link as the surrounding text with an underline on hover, and a click selects the section (the
    /// mock's <c>sref</c>).
    /// </summary>
    public const string SectionLinkScheme = "section:";

    /// <summary>
    /// True while <see cref="LinkSections"/> is in force on this thread: section names come out as section links.
    /// Ambient rather than a parameter so the many sentence builders keep one signature for the plain consumers (the
    /// grid's hover, the list rows, tests) and the linked one (the watch detail).
    /// </summary>
    [ThreadStatic] private static bool t_linkSections;

    /// <summary>
    /// Makes section names in the sentences built on this thread section links until the result is disposed. Used
    /// only by the watch detail, around building its text. Hovers built in the same pass must go through
    /// <see cref="Plain"/>, since a tooltip can't be clicked.
    /// </summary>
    /// <returns>Disposing it restores plain names.</returns>
    public static IDisposable LinkSections()
    {
        bool before = t_linkSections;
        t_linkSections = true;
        return new LinkScope(before);
    }

    /// <summary>Restores the earlier linking state when disposed.</summary>
    private sealed class LinkScope(bool before) : IDisposable
    {
        /// <summary>Puts the linking state back.</summary>
        public void Dispose() => t_linkSections = before;
    }

    /// <summary>The text with every section link replaced by its label (for hovers and other unclickable places).</summary>
    /// <param name="text">Text that may hold section links; null gives null.</param>
    /// <returns>The plain text.</returns>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public static string? Plain(string? text) =>
        text is null ? null : SectionLinkRegex().Replace(text, "$1");

    /// <summary>Matches a section link: <c>[label](section:id)</c>.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\[([^\]]+)\]\(section:[^)]*\)")]
    private static partial System.Text.RegularExpressions.Regex SectionLinkRegex();

    /// <summary>The label as a section link to <paramref name="section"/> while linking is on; else the label.</summary>
    /// <param name="section">The section the label names.</param>
    /// <param name="label">What the sentence says for it.</param>
    /// <returns>The label, linked or plain.</returns>
    private static string Link(Section section, string label) =>
        t_linkSections ? $"[{label}]({SectionLinkScheme}{section.Id})" : label;

    /// <summary>A section's code alone ("A"), linked while linking is on; for use where the course is already named.</summary>
    /// <param name="section">The section.</param>
    /// <returns>The code.</returns>
    public static string CodeOf(Section section) => Link(section, section.SectionCode);

    /// <summary>
    /// A section's name in a sentence: its course's calendar code and its section code, e.g. "CHEM 213 A"; just the
    /// section code when the section has no course.
    /// </summary>
    /// <param name="inputs">The semester view (for the course's code).</param>
    /// <param name="section">The section.</param>
    /// <returns>The label.</returns>
    public static string SectionLabel(AccessInputs inputs, Section section) =>
        Link(section, section.CourseId is null ? section.SectionCode : $"{inputs.CourseCode(section.CourseId)} {section.SectionCode}");

    /// <summary>Joins phrases as "a, b and c" (a single phrase stays as it is; none gives an empty string).</summary>
    /// <param name="items">The phrases.</param>
    /// <returns>The joined text.</returns>
    public static string JoinAnd(IReadOnlyList<string> items) => JoinWith(items, "and");

    /// <summary>Joins phrases as "a, b or c" (a single phrase stays as it is; none gives an empty string).</summary>
    /// <param name="items">The phrases.</param>
    /// <returns>The joined text.</returns>
    public static string JoinOr(IReadOnlyList<string> items) => JoinWith(items, "or");

    /// <summary>Joins phrases with commas and <paramref name="word"/> before the last.</summary>
    private static string JoinWith(IReadOnlyList<string> items, string word) =>
        items.Count <= 1 ? string.Concat(items) : $"{string.Join(", ", items.Take(items.Count - 1))} {word} {items[^1]}";

    /// <summary>
    /// Names sections as alternatives, the course code given once per course: "CIVL303 T1A or CIVL315 101",
    /// "MANU430 001, L0A or L0B".
    /// </summary>
    /// <param name="inputs">The semester view (for course codes).</param>
    /// <param name="sections">The sections, in the order to name them.</param>
    /// <returns>The text.</returns>
    public static string ByCourseOr(AccessInputs inputs, IEnumerable<Section> sections)
    {
        var named = new HashSet<string>();
        return JoinOr(sections.Select(s => named.Add(s.CourseId ?? string.Empty) ? SectionLabel(inputs, s) : CodeOf(s)).ToList());
    }

    /// <summary>
    /// Names sections grouped by course, in order of first appearance: "PSYCH 101 A, B, C and GEOG 250 A".
    /// </summary>
    /// <param name="inputs">The semester view (for course codes).</param>
    /// <param name="sections">The sections.</param>
    /// <returns>The grouped text.</returns>
    public static string ByCourse(AccessInputs inputs, IEnumerable<Section> sections)
    {
        var courses = new List<string>();
        var codes = new Dictionary<string, List<string>>();
        foreach (var s in sections)
        {
            var key = s.CourseId ?? string.Empty;
            if (!codes.TryGetValue(key, out var list)) { codes[key] = list = []; courses.Add(key); }
            list.Add(CodeOf(s));
        }
        return JoinAnd(courses.Select(c =>
            (c.Length == 0 ? string.Empty : inputs.CourseCode(c) + " ") + string.Join(", ", codes[c])).ToList());
    }

    /// <summary>
    /// The hover lines for one section's line on one day: what the conflict marks on it say. Conflicts of one kind
    /// are merged into one sentence ("CHEM 213 A conflicts with PSYCH 101 A, B, C, so ..."), each line is led by its
    /// watch's name in bold ("**Chem Y2:** ", <see cref="Helpers.InlineFormatter"/> markup; Greg, 2026-11-25) only when
    /// more than one watch speaks, and a partner that the grid is not showing gets a line of its own at the end.
    /// </summary>
    /// <param name="inputs">The semester view the marks were made on.</param>
    /// <param name="section">The section whose line is hovered.</param>
    /// <param name="day">The day number of the hovered line (1 = Monday).</param>
    /// <param name="marks">Every mark that involves <paramref name="section"/> on <paramref name="day"/>, with its watch.</param>
    /// <param name="isHidden">Why a section is not on the grid (null when it is shown); null when everything is shown.</param>
    /// <returns>The lines, in order; empty when no mark involves the section on that day.</returns>
    public static IReadOnlyList<string> ConflictTips(
        AccessInputs inputs, Section section, int day, IReadOnlyList<WatchMark> marks, Func<string, HiddenReason?>? isHidden = null)
    {
        var mine = marks.Where(m => (m.Mark.A.Id == section.Id || m.Mark.B.Id == section.Id)
                                    && m.Mark.Meetings.Any(p => p.A.Day == day)).ToList();
        var lines = new List<string>();
        if (mine.Count == 0) return lines;

        string id = SectionLabel(inputs, section);
        bool many = mine.Select(m => m.WatchId).Distinct().Count() > 1;
        Section OtherEnd(ConflictMark k) => k.A.Id == section.Id ? k.B : k.A;

        foreach (var watchId in mine.Select(m => m.WatchId).Distinct())
        {
            var ks = mine.Where(m => m.WatchId == watchId).ToList();
            // Every mention of the watch's name is bold (InlineFormatter markup; Greg, 2026-11-25).
            string w = Bold(ks[0].WatchName);
            void Say(string text) => lines.Add((many ? $"{w}: " : string.Empty) + text);

            // Stranded: one sentence per set of stranded sections that conflict with the same sections.
            // approved wording (round 6)
            var xOrder = new List<string>();
            var byX = new Dictionary<string, (Section X, List<Section> Others)>();
            foreach (var k in ks.Where(m => m.Mark.Kind == ConflictMarkKind.Stranded).Select(m => m.Mark))
            {
                var x = k.X!;
                if (!byX.ContainsKey(x.Id)) { byX[x.Id] = (x, []); xOrder.Add(x.Id); }
                byX[x.Id].Others.Add(k.A.Id == x.Id ? k.B : k.A);
            }
            var mergedOrder = new List<string>();
            var merged = new Dictionary<string, List<Section>>();
            foreach (var xid in xOrder)
            {
                var (x, others) = byX[xid];
                var key = ByCourse(inputs, others);
                if (!merged.ContainsKey(key)) { merged[key] = []; mergedOrder.Add(key); }
                merged[key].Add(x);
            }
            foreach (var others in mergedOrder)
            {
                var xs = merged[others];
                Say(xs.Count == 1
                    ? $"{SectionLabel(inputs, xs[0])} conflicts with {others}, so {SectionLabel(inputs, xs[0])} can't be part of any slate for {w}."
                    : $"{ByCourse(inputs, xs)} conflict with {others}, so they can't be part of any slate for {w}.");
            }

            // Move (no full slate): one sentence per problem, naming this line's conflicts and the sections to move.
            foreach (var problemMarks in ks.Where(m => m.Mark.Kind == ConflictMarkKind.Move).GroupBy(m => m.Mark.Problem))
                Say(MoveTip(inputs, section, problemMarks.Select(m => OtherEnd(m.Mark)).ToList(), problemMarks.Key!,
                    problemMarks.First().Mark.ProblemCount, w));

            // Other: one sentence per mark, repeats dropped.
            // approved wording (round 6)
            var seen = new HashSet<string>();
            foreach (var m in ks.Where(m => m.Mark.Kind == ConflictMarkKind.Other))
            {
                var k = m.Mark;
                string tip = $"{SectionLabel(inputs, k.A)} conflicts with {SectionLabel(inputs, k.B)}. Because of this conflict, {JoinAnd(FreedLabels(inputs, k))} can't be part of any slate for {w}.";
                if (seen.Add(tip)) Say(tip);
            }

            // approved wording (round 6)
            var hl = ks.Where(m => m.Mark.Kind == ConflictMarkKind.Harmless).Select(m => OtherEnd(m.Mark)).ToList();
            if (hl.Count > 0)
                Say($"{id} conflicts with {ByCourse(inputs, hl)}. Other sections still let students in {w} get around "
                    + $"{(hl.Count == 1 ? "this conflict" : "these conflicts")}.");

            // Plain (no full slate, Mark all conflicts): the conflict alone, no claim about its cost.
            // approved wording (Greg, 2026-11-26)
            var plain = ks.Where(m => m.Mark.Kind == ConflictMarkKind.Plain).Select(m => OtherEnd(m.Mark)).ToList();
            if (plain.Count > 0) Say($"{id} conflicts with {ByCourse(inputs, plain)}.");
        }

        // Q41: a partner the grid is not showing, and why: the subjects chosen in the top banner (Greg, 2026-11-24:
        // "hidden by the filters" was wrong for those) or the grid's filters. both lines approved wording (Greg, 2026-11-24)
        if (isHidden is not null)
        {
            var said = new HashSet<string>();
            foreach (var partner in mine.Select(m => OtherEnd(m.Mark)))
            {
                if (isHidden(partner.Id) is not { } why) continue;
                var label = SectionLabel(inputs, partner);
                if (said.Add(label))
                    lines.Add(why == HiddenReason.Subjects
                        ? $"{label} isn't shown: its subject isn't one of your chosen Subjects."
                        : $"{label} is hidden by the filters.");
            }
        }
        return lines;
    }

    /// <summary>The labels of the stranded sections an <see cref="ConflictMarkKind.Other"/> mark keeps out of every slate.</summary>
    private static List<string> FreedLabels(AccessInputs inputs, ConflictMark mark) =>
        mark.FreedSectionIds
            .Select(fid => inputs.Sections.FirstOrDefault(s => s.Id == fid))
            .OfType<Section>()
            .Select(s => SectionLabel(inputs, s))
            .ToList();

    // ── The Access tab's health line (port of the r9 wireframe's healthSegs) ────────────────────────────────────

    /// <summary>The separator between the phrases of a health line.</summary>
    private const string PhraseSeparator = " · ";

    /// <summary>
    /// "1 section" / "3 sections": a count with the right noun (the mock's <c>plural</c>).
    /// </summary>
    /// <param name="n">The count.</param>
    /// <param name="one">The noun for exactly one.</param>
    /// <param name="many">The noun for any other count.</param>
    /// <returns>The count and noun, e.g. "2 conflicts".</returns>
    public static string Plural(int n, string one, string many) =>
        $"{n.ToString("N0", CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";

    /// <summary>
    /// Names up to two items and counts the rest: "A, B" or "A, B +3 more" (the mock's <c>nameList</c>).
    /// </summary>
    /// <param name="names">The names.</param>
    /// <returns>The shortened list.</returns>
    public static string NameList(IReadOnlyList<string> names) =>
        names.Count <= 2 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(2))} +{names.Count - 2} more";

    /// <summary>
    /// A component's name inside a sentence, from its section type: "Laboratory" is "lab", any other type is its
    /// name in lower case; "section" when there is no type.
    /// </summary>
    /// <param name="inputs">The semester view (for the section type names).</param>
    /// <param name="typeId">The section type id; may be null.</param>
    /// <returns>The word.</returns>
    public static string PartWord(AccessInputs inputs, string? typeId)
    {
        var name = typeId is null ? null : inputs.SectionTypes.FirstOrDefault(t => t.Id == typeId)?.Name;
        if (string.IsNullOrWhiteSpace(name)) return "section";
        return name.Equals("Laboratory", StringComparison.OrdinalIgnoreCase) ? "lab" : name.ToLowerInvariant();
    }

    /// <summary>
    /// Why a course cannot be taken even on its own (the mock's <c>selfFailWhy</c>), in a phrase that follows
    /// "No full slate".
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="analysis">The analysis (for the course's groups).</param>
    /// <param name="failure">The failing course and its structured reason.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The phrase, e.g. "BMEG 230's labs all conflict with its only lecture (101)".</returns>
    public static string SelfFailWhy(AccessInputs inputs, Analysis analysis, SelfFailCourse failure, Func<string?, string> campusName)
    {
        string c = inputs.CourseCode(failure.CourseId);
        var r = failure.Reason;
        if (r.OnlyGroup is { } g && r.OtherGroup is { } h && g.Candidates.Count > 0 && h.Candidates.Count > 0)
        {
            var o = g.Candidates[0];
            string only = $"its only {PartWord(inputs, g.ComponentTypeId)} ({CodeOf(o)})";
            string hw = PartWord(inputs, h.ComponentTypeId);
            // approved wording (round 6d)
            if (r.Kind == SelfFailKind.TimeConflict)
                return h.Candidates.Count == 1
                    ? $"{c}'s only {hw} ({CodeOf(h.Candidates[0])}) conflicts with {only}"
                    : $"{c}'s {hw}s all conflict with {only}";
            if (r.Kind == SelfFailKind.Campus)
                return $"{c}'s {hw}s are all at {campusName(h.Candidates[0].CampusId)}, but {only} is at {campusName(o.CampusId)}";
        }
        var words = analysis.Groups.Where(x => x.CourseId == failure.CourseId)
            .Select(x => PartWord(inputs, x.ComponentTypeId)).ToList();
        // approved wording (round 6d)
        return $"no set of {c}'s {string.Join(", ", words)} sections fits together";
    }

    /// <summary>
    /// Why campus stands in the way (the mock's <c>campusWhy</c>): the only sections on the less common campus
    /// against those on the most common one, or a general sentence.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="reason">The structured campus reason.</param>
    /// <param name="mixed">True when Multi-campus is on and every slate mixes campuses; false when it is off and no slate keeps to one campus.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The phrase.</returns>
    public static string CampusWhy(AccessInputs inputs, CampusReason reason, bool mixed, Func<string?, string> campusName)
    {
        if (reason.Kind == CampusReasonKind.OnlySectionsOnDifferentCampuses)
        {
            var few = reason.LessCommonOnlySections.Select(s => SectionLabel(inputs, s)).ToList();
            var many = reason.MoreCommonOnlySections.Select(s => SectionLabel(inputs, s)).ToList();
            // approved wording (round 6e)
            return $"{JoinAnd(few)} {(few.Count > 1 ? "are" : "is")} at {campusName(reason.LessCommonCampusId)}, but "
                 + $"{(many.Count <= 3 ? JoinAnd(many) : NameList(many))} {(many.Count > 1 ? "are" : "is")} at {campusName(reason.MoreCommonCampusId)}";
        }
        // approved wording (round 6d, 6e)
        return mixed ? "no slate keeps to one campus" : "the slates that fit in time mix campuses";
    }

    /// <summary>
    /// The hover of a <see cref="CampusWhy"/> phrase (the mock's <c>campusWhy</c> tips).
    /// </summary>
    /// <param name="reason">The structured campus reason.</param>
    /// <param name="mixed">As for <see cref="CampusWhy"/>.</param>
    /// <returns>The hover.</returns>
    // approved wording (round 6d, 6e)
    private static string CampusTip(CampusReason reason, bool mixed) =>
        !mixed ? "A slate keeps its in-person sections at one campus. Tick Multi-campus, under the courses, to count slates across campuses."
        : reason.Kind == CampusReasonKind.OnlySectionsOnDifferentCampuses
            ? "Every slate uses these sections, so a student taking every course has classes on more than one campus."
            : "Each slate puts in-person sections on more than one campus, so a student taking every course has classes on more than one campus.";

    /// <summary>
    /// The hover of "depends on …" (the mock's <c>conflictText</c>): for each section every slate uses by timing, why
    /// its component's other sections can't be used, e.g. "CHEM 213 B conflicts with PSYCH 101 A; CHEM 213 C doesn't
    /// fit with the other courses."
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="a">The analysis.</param>
    /// <returns>The hover.</returns>
    private static string DependsOnTip(AccessInputs inputs, Analysis a)
    {
        bool Always(Section t) => a.Roles.TryGetValue(t.Id, out var r)
                                  && r is SectionRole.EssentialByTiming or SectionRole.OnlySection;
        var parts = new List<string>();
        foreach (var s in a.EssentialByTimingSections)
        {
            var group = a.Groups.FirstOrDefault(g => g.Candidates.Any(c => c.Id == s.Id));
            if (group is null) continue;
            foreach (var x in group.Candidates.Where(c => c.Id != s.Id))
            {
                var hit = a.Groups.Where(g => g != group).SelectMany(g => g.Candidates)
                    .FirstOrDefault(t => Always(t) && SectionOverlap.Conflicts(x, t));
                // approved wording (round 3)
                parts.Add(hit is not null
                    ? $"{SectionLabel(inputs, x)} conflicts with {SectionLabel(inputs, hit)}"
                    : $"{SectionLabel(inputs, x)} doesn't fit with the other courses");
            }
        }
        return string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// The phrases of a watch's health line, in order (see <see cref="HealthLine"/>).
    /// </summary>
    /// <param name="inputs">The semester view the analysis was made on.</param>
    /// <param name="a">The watch's analysis over the whole semester.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <param name="cautions">
    /// The analysis's cautions (<see cref="Cautions.For"/>) for the watch's detail, which counts them after the stranded
    /// sections; <c>null</c> (the list's rows) leaves them out. A watch whose pins stop a full slate names the pins
    /// instead of a best partial.
    /// </param>
    /// <param name="keptInPlace">Ids of sections the user has said can't move (<see cref="AccessState.KeptInPlace"/>),
    /// so a watch with no full slate names the same sections to move as the grid; null for none.</param>
    /// <returns>The phrases, some with a hover or a warning (the mock's tips and <c>warn</c>); never empty.</returns>
    public static IReadOnlyList<HealthPhrase> HealthPhrases(
        AccessInputs inputs, Analysis a, Func<string?, string> campusName, IReadOnlyList<Caution>? cautions = null,
        IReadOnlySet<string>? keptInPlace = null)
    {
        var segs = new List<HealthPhrase>();
        // approved wording (Q53, Greg 2026-11-24)
        if (a.Health == AccessHealth.TooLarge)
            return [$"Too many courses to check (more than {AccessAnalyzer.MaxCourses})"];

        void Tail()
        {
            if (a.NotCountedSections.Count > 0) segs.Add($"{Plural(a.NotCountedSections.Count, "section", "sections")} not counted");
            if (a.SectionsWithoutTimes.Count > 0) segs.Add($"{Plural(a.SectionsWithoutTimes.Count, "section", "sections")} without times");
        }
        string SlateCountText(bool lead) => a.SlateCountCapped
            ? $"{(lead ? "More" : "more")} than {a.SlateCount.ToString("N0", CultureInfo.InvariantCulture)} slates"
            : Plural(a.SlateCount, "slate", "slates");

        if (a.MissingComponents.Count > 0)
        {
            // approved wording (round 6d, gap 4)
            segs.Add("No full slate");
            foreach (var x in a.MissingComponents)
            {
                string course = inputs.CourseCode(x.CourseId), word = PartWord(inputs, x.ComponentTypeId);
                segs.Add(x.SectionsExistInView ? $"{course}'s {word}s aren't in this watch" : $"{course} has no {word}s this term");
            }
            if (a.HasSlate)
            {
                var gone = JoinAnd(a.MissingComponents.Select(x => inputs.CourseCode(x.CourseId)).Distinct().ToList());
                segs.Add(a.IsSingleSectionCase ? "the other sections fit together" : $"{SlateCountText(false)} without {gone}");
            }
            Tail();
            return segs;
        }

        if (a.IsSingleSectionCase)
        {
            // approved wording (round 6c)
            int n = a.Groups.Count;
            var p = a.SingleSectionConflicts;
            if (p.Count > 0)
            {
                segs.Add($"{SectionLabel(inputs, p[0].A)} conflicts with {SectionLabel(inputs, p[0].B)}");
                if (p.Count > 1)
                    segs.Add(new(Plural(p.Count - 1, "more conflict", "more conflicts"),
                        string.Join("; ", p.Skip(1).Select(x => $"{SectionLabel(inputs, x.A)} conflicts with {SectionLabel(inputs, x.B)}")) + "."));
            }
            else if (!a.HasSlate)
            {
                // approved wording (round 6d)
                segs.Add(a.SelfFailCourses.Count > 0 ? SelfFailWhy(inputs, a, a.SelfFailCourses[0], campusName)
                    : a.CampusReason is { } cr ? new HealthPhrase(CampusWhy(inputs, cr, false, campusName), CampusTip(cr, false))
                    : "these sections don't fit together");
            }
            else
            {
                segs.Add(n == 1 ? "1 section" : n == 2 ? "Both sections fit together" : $"All {n} sections fit together");
                if (a.MixedCampusReason is { } mr)
                    segs.Add(new(CampusWhy(inputs, mr, true, campusName), CampusTip(mr, true), Warn: true));
            }
            Tail();
            return segs;
        }

        if (a.HasSlate)
        {
            segs.Add(SlateCountText(true));
            // approved wording (round 6e)
            if (a.MixedCampusReason is { } mr)
            {
                segs.Add(new(a.SlateCount == 1 ? "it mixes campuses" : "every slate mixes campuses", CampusTip(mr, true), Warn: true));
                segs.Add(new(CampusWhy(inputs, mr, true, campusName), Warn: true));
            }
            else if (!a.OneCampusRule && a.MixedSlateCount != 0)
            {
                static string N(int x) => x.ToString("N0", CultureInfo.InvariantCulture);
                segs.Add(a.MixedSlateCount is not { } mixed
                    ? new HealthPhrase("some mix campuses", "Some slates put in-person sections on more than one campus.")
                    : new HealthPhrase($"{N(mixed)} {(mixed == 1 ? "mixes" : "mix")} campuses",
                        $"{N(mixed)} of {N(a.SlateCount)} slates put in-person sections on more than one campus; the other "
                        + $"{N(a.OneCampusSlateCount ?? a.SlateCount - mixed)} keep to one. Step through slates to see which."));
            }
            if (a.EssentialByTimingSections.Count > 0)
                segs.Add(new($"depends on {NameList(a.EssentialByTimingSections.Select(s => SectionLabel(inputs, s)).ToList())}",
                    DependsOnTip(inputs, a)));
            if (a.StrandedSections.Count > 0)
                segs.Add($"{Plural(a.StrandedSections.Count, "section", "sections")} stranded");
            if (cautions is { Count: > 0 })
            {
                int hard = cautions.Count(c => c.Kind == CautionKind.HardToReach), heavy = cautions.Count - hard;
                if (hard > 0) segs.Add($"{Plural(hard, "section", "sections")} hard to reach");
                if (heavy > 0) segs.Add($"{Plural(heavy, "section carries", "sections carry")} a heavy load");
            }
        }
        else if (a.PinBlocks.Count > 0)
        {
            // approved wording (round 3 review): the pins are why there is no full slate, so name them, not a best partial
            segs.Add("No slate with these pins");
            foreach (var block in a.PinBlocks) segs.Add(PinBlockPhrase(inputs, block, campusName));
        }
        else
        {
            // approved wording (round 6d)
            segs.Add("No full slate");
            foreach (var x in a.SelfFailCourses) segs.Add(SelfFailWhy(inputs, a, x, campusName));
            var moves = MoveSuggestions.For(a, keptInPlace);
            if (a.CampusReason is { } cr) segs.Add(new(CampusWhy(inputs, cr, false, campusName), CampusTip(cr, false)));
            // The same suggestion the grid's ▲ and lines show (Greg, 2026-11-26: the course-level "X or Y can't fit"
            // no longer matched them); with nothing suggested, the best partial as before.
            // approved wording (Greg, 2026-11-26)
            else if (moves.Problems.Count == 1)
                segs.Add($"try moving {ByCourseOr(inputs, moves.Problems[0].Options.Select(o => o.Section))}");
            else if (moves.Problems.Count > 1)
                segs.Add($"{moves.Problems.Count} separate problems");
            else if (a.BestPartial is { Alternatives.Count: > 0 } best)
            {
                segs.Add($"best partial covers {(best.Complete ? "" : "at least ")}{best.Covers} of {best.Of}");
                segs.Add(CantFitText(inputs, best));
            }
        }
        Tail();
        // approved wording (round 6b): every conflict between two components of the watch, harmless or not
        if (a.ConflictCount > 0) segs.Add(Plural(a.ConflictCount, "conflict", "conflicts"));
        return segs;
    }

    /// <summary>
    /// "A and B can't fit", "A or B can't fit", "only one of A, B and C fits" or "2 of A, B and C can't fit" (round 6d,
    /// gap 3), with the hover "Students can take all but A, or all but B." ("No A section fits with the other
    /// courses." when one course alone can't fit).
    /// </summary>
    private static HealthPhrase CantFitText(AccessInputs inputs, BestPartial best)
    {
        var alts = best.Alternatives.Select(alt => (IReadOnlyList<string>)alt.Select(inputs.CourseCode).ToList()).ToList();
        int k = alts[0].Count;
        // Sorted by code: the search finds the left-out courses in an order that means nothing to the reader.
        var union = alts.SelectMany(x => x).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        // approved wording (round 6d)
        string tip = $"Students can take all but {string.Join(", or all but ", alts.Select(JoinAnd).Order(StringComparer.OrdinalIgnoreCase))}.";
        if (alts.Count == 1)
            return new($"{JoinAnd(alts[0])} can't fit", k == 1 ? $"No {alts[0][0]} section fits with the other courses." : tip);
        if (k == 1) return new($"{string.Join(" or ", union)} can't fit", tip);
        if (union.Count - k == 1) return new($"only one of {JoinAnd(union)} fits", tip);
        return new($"{k} of {JoinAnd(union)} can't fit", tip);
    }

    /// <summary>
    /// The one-line health of a watch for the Access tab: phrases joined with " · " (a port of the r9 wireframe's
    /// <c>healthSegs</c> for the whole watch: no subset, no pins, no seats, no cautions). A watch with too many
    /// courses says so and nothing else.
    /// </summary>
    /// <param name="inputs">The semester view the analysis was made on.</param>
    /// <param name="analysis">The watch's analysis over the whole semester.</param>
    /// <param name="campusName">Gives a campus's name from its id (for the campus reasons).</param>
    /// <param name="keptInPlace">Ids of sections the user has said can't move; null for none.</param>
    /// <returns>The line.</returns>
    public static string HealthLine(AccessInputs inputs, Analysis analysis, Func<string?, string> campusName,
        IReadOnlySet<string>? keptInPlace = null) =>
        string.Join(PhraseSeparator, HealthPhrases(inputs, analysis, campusName, keptInPlace: keptInPlace).Select(p => p.Text));
}

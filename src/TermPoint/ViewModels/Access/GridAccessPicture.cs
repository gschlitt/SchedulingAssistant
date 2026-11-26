using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// How one section's line is drawn on the grid while a watch is open (the r9 wireframe's <c>entry</c>): at most one of
/// the looks below applies, plus <see cref="Pinned"/>, and the hover line says why. All false and no hover means
/// "drawn as usual". Immutable.
/// </summary>
/// <param name="Faded">Drawn faded: not in the watch, an unticked course, or not in the slate being shown.</param>
/// <param name="Stranded">In no slate: no student taking the watch's courses can use it.</param>
/// <param name="Essential">Every slate uses it (●): its component's other sections can't fit.</param>
/// <param name="CantFit">A course that can't fit with the rest (▲): this section is of it, or is a pin no slate can use.</param>
/// <param name="Caution">Hard to reach or heavy load (◆).</param>
/// <param name="InSlate">In the slate being stepped to: drawn as a ghost.</param>
/// <param name="Pinned">Pinned: only slates with it count.</param>
/// <param name="Hover">The hover line; empty when there is nothing to say.</param>
/// <param name="Usable">In at least one slate and nothing more to say (drawn as usual, with the "can be part of a slate" hover).</param>
public sealed record AccessTileLook(
    bool Faded = false,
    bool Stranded = false,
    bool Essential = false,
    bool CantFit = false,
    bool Caution = false,
    bool InSlate = false,
    bool Pinned = false,
    string Hover = "",
    bool Usable = false)
{
    /// <summary>
    /// Whether a conflict mark of the open watch already says what this look's hover says, so the tile's hover shows
    /// the mark's line alone (Greg, 2026-11-25: the two lines repeated each other). The mark's line is kept because
    /// it is the same with or without the watch open and names every conflict, where the look names one.
    /// Stranded repeats a Stranded mark about this section; can't fit repeats a no-full-slate mark on it; usable
    /// repeats a harmless mark ("other sections still let students get around"). An essential or only section and
    /// an Other mark say different things, so both stay.
    /// </summary>
    /// <param name="mark">A mark of the open watch that involves this section on the tile's day.</param>
    /// <param name="sectionId">This section's id.</param>
    /// <returns>True when the look's hover adds nothing to the mark's line.</returns>
    public bool IsRepeatedBy(ConflictMark mark, string sectionId) =>
        !string.IsNullOrEmpty(Hover) && mark.Kind switch
        {
            ConflictMarkKind.Stranded => Stranded && mark.X?.Id == sectionId,
            ConflictMarkKind.NoFull or ConflictMarkKind.NoSingleFix => CantFit,
            ConflictMarkKind.Harmless => Usable,
            _ => false,
        };

    /// <summary>
    /// The mark drawn after the label: "▲" for can't fit, "◆" for a caution, "●" for essential; empty for none.
    /// </summary>
    public string Mark => CantFit ? "▲" : Caution ? "◆" : Essential ? "●" : string.Empty;

    /// <summary>How a section of another course is drawn while a watch is open and Show other courses is on: faded, nothing to say.</summary>
    public static AccessTileLook OtherCourse { get; } = new(Faded: true);
}

/// <summary>
/// The picture of an open watch on the grid: for each section the watch covers, how its line is drawn and what its
/// hover says. A pure port of the r9 wireframe's <c>entry</c>, built from the picture's analysis, its ticks and pins
/// and the slate being stepped to. Sections the watch does not cover get no entry here: the grid draws them
/// <see cref="AccessTileLook.OtherCourse"/>.
/// </summary>
public static class GridAccessPicture
{
    /// <summary>
    /// Builds the looks of the open watch (or course) from <see cref="AccessPicture"/>.
    /// </summary>
    /// <param name="picture">The open watch.</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The looks by section id; empty when nothing is open.</returns>
    public static IReadOnlyDictionary<string, AccessTileLook> Build(AccessPicture picture, Func<string?, string> campusName)
    {
        var inputs = picture.Inputs;
        var analysis = picture.Analysis;
        if (!picture.IsOpen || inputs is null || analysis is null) return new Dictionary<string, AccessTileLook>();
        var cautions = Cautions.For(analysis);
        return Build(inputs, analysis, picture.Name, picture.CoveredSections, picture.CurrentSlate, cautions,
            picture.IsCourseOnly, campusName);
    }

    /// <summary>
    /// Builds the looks for a set of covered sections.
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="analysis">The analysis, with its ticks and pins.</param>
    /// <param name="name">The watch's name, or the checked course's code.</param>
    /// <param name="covered">The sections the watch covers.</param>
    /// <param name="slate">The slate being stepped to; null when not stepping.</param>
    /// <param name="cautions">The analysis's cautions.</param>
    /// <param name="isCourseOnly">True when one course is checked alone (no "Multi-campus is off" in reasons).</param>
    /// <param name="campusName">Gives a campus's name from its id.</param>
    /// <returns>The looks by section id.</returns>
    public static IReadOnlyDictionary<string, AccessTileLook> Build(
        AccessInputs inputs, Analysis analysis, string name, IReadOnlyList<Section> covered,
        IReadOnlyList<Section>? slate, IReadOnlyList<Caution> cautions, bool isCourseOnly, Func<string?, string> campusName)
    {
        var looks = new Dictionary<string, AccessTileLook>();
        var chosen = analysis.ChosenCourseIds.ToHashSet();
        var pins = analysis.PinnedSectionIds;
        var inSlate = slate?.Select(s => s.Id).ToHashSet();
        var cautionBySection = cautions.GroupBy(c => c.Section.Id).ToDictionary(g => g.Key, g => g.First());
        var blocking = BlockingSections(analysis, covered);
        foreach (var s in covered)
        {
            var look = LookOf(inputs, analysis, name, s, chosen, inSlate, cautionBySection, blocking, isCourseOnly, campusName);
            looks[s.Id] = pins.Contains(s.Id) ? look with { Pinned = true } : look;
        }
        return looks;
    }

    /// <summary>
    /// With no full slate, the sections of the courses that can't fit that get the ▲: those in a time conflict with a
    /// section of another component, which are the ones keeping the course out (Greg, 2026-11-25: the ▲ was on every
    /// section of the course, so lectures that fit fine said "no section fits"). A course with no such section (the
    /// clash is among three or more sections at once) keeps the ▲ on all of its sections, and so does every course
    /// when the campus rule is to blame, since then no time conflict is the reason (the conflict marks skip that
    /// case too).
    /// </summary>
    /// <param name="a">The analysis.</param>
    /// <param name="covered">The sections the watch covers.</param>
    /// <returns>The ids of the sections that get the ▲; empty when there is a full slate.</returns>
    private static IReadOnlySet<string> BlockingSections(Analysis a, IReadOnlyList<Section> covered)
    {
        var blocking = new HashSet<string>();
        if (a.HasSlate || a.BestPartial is not { } best) return blocking;
        foreach (var courseId in best.CantFit)
        {
            var own = covered.Where(s => s.CourseId == courseId).ToList();
            var inConflict = a.CampusReason is not null ? [] : a.Groups
                .Where(g => g.CourseId == courseId)
                .SelectMany(g => g.Candidates.Where(s => a.Groups.Any(other => other != g
                    && other.Candidates.Any(t => SectionOverlap.Conflicts(s, t)))))
                .ToList();
            foreach (var s in inConflict.Count > 0 ? inConflict : own) blocking.Add(s.Id);
        }
        return blocking;
    }

    /// <summary>One covered section's look (the body of the mock's <c>entry</c>).</summary>
    private static AccessTileLook LookOf(
        AccessInputs inputs, Analysis a, string name, Section s, IReadOnlySet<string> chosen, IReadOnlySet<string>? inSlate,
        IReadOnlyDictionary<string, Caution> cautions, IReadOnlySet<string> blocking, bool isCourseOnly,
        Func<string?, string> campusName)
    {
        string label = AccessText.SectionLabel(inputs, s);
        string code = s.CourseId is null ? string.Empty : inputs.CourseCode(s.CourseId);
        // The hovers are InlineFormatter markup: a watch's name is bold wherever it is mentioned (a course checked
        // alone is not a watch, so its code stays plain).
        name = isCourseOnly ? name : AccessText.Bold(name);
        var group = a.Groups.FirstOrDefault(g => g.All.Any(x => x.Id == s.Id));
        var role = a.Roles.GetValueOrDefault(s.Id, SectionRole.None);

        if (s.CourseId is null || !chosen.Contains(s.CourseId))
            return new AccessTileLook(Faded: true, Hover: AccessText.UntickedTip(code));
        // Not in any component group: checked before the slates, since it holds whether or not there is a slate (Greg,
        // 2026-11-25: with no full slate these sections were drawn plainly, with no hover).
        if (group is null)
        {
            // Its course is left out because a component has no sections in the watch (rules 10 and 6d).
            var missing = a.MissingComponents.Where(m => m.CourseId == s.CourseId)
                .Select(m => AccessText.PartWord(inputs, m.ComponentTypeId)).Distinct().ToList();
            return new AccessTileLook(Faded: true, Hover: missing.Count > 0
                ? AccessText.MissingComponentTip(label, code, missing, name)
                : AccessText.NotCountedTip(label));
        }
        if (group is not null && !group.Candidates.Any(c => c.Id == s.Id))
            return new AccessTileLook(Faded: true, Hover: AccessText.NotUsedPinnedTip(AccessText.SectionLabel(inputs, group.Candidates[0])));
        if (inSlate is not null)
            return inSlate.Contains(s.Id)
                ? new AccessTileLook(InSlate: true, Hover: AccessText.InThisSlateTip(label))
                : new AccessTileLook(Faded: true);

        if (!a.HasSlate)
        {
            var blockPin = a.PinBlocks.FirstOrDefault(b => b.Pin?.Id == s.Id);
            if (blockPin is { Pin: { } pin, Reason: { } reason })
                return new AccessTileLook(CantFit: true,
                    Hover: AccessText.PinnedCantFitTip(label, AccessText.StrandedWhy(inputs, pin, reason, campusName, !isCourseOnly)));
            if (a.BestPartial is { } best && best.CantFit.Contains(s.CourseId) && blocking.Contains(s.Id))
            {
                var selfFail = a.SelfFailCourses.FirstOrDefault(x => x.CourseId == s.CourseId);
                string hover = selfFail is not null
                    ? AccessText.CourseCantBeTakenTip(code, AccessText.SelfFailWhy(inputs, a, selfFail, campusName))
                    : AccessText.CourseCantFitTip(code, name,
                        a.CampusReason is { } cr ? AccessText.CampusWhy(inputs, cr, false, campusName) : null);
                return new AccessTileLook(CantFit: true, Hover: hover);
            }
            return new AccessTileLook();
        }

        if (role == SectionRole.Stranded)
            return new AccessTileLook(Stranded: true, Hover: AccessText.StrandedTip(label, name,
                AccessText.StrandedWhy(inputs, s, a.StrandedReasonFor(s), campusName, !isCourseOnly)));
        if (cautions.TryGetValue(s.Id, out var caution))
            return new AccessTileLook(Caution: true, Hover: AccessText.CautionText(inputs, caution));
        if (role == SectionRole.EssentialByTiming)
            return new AccessTileLook(Essential: true, Hover: AccessText.EssentialTip(label, code, name));
        if (role == SectionRole.OnlySection)
        {
            if (a.PinnedSectionIds.Contains(s.Id)) return new AccessTileLook(Hover: AccessText.PinnedTip(label));
            string word = s.CourseId is not null && inputs.ComponentsOf(s.CourseId).Count >= 2
                ? inputs.SectionTypes.FirstOrDefault(t => t.Id == s.SectionTypeId)?.Name.ToLowerInvariant() ?? "section"
                : "section";
            return new AccessTileLook(Hover: AccessText.OnlySectionTip(label, code, word));
        }
        if (role == SectionRole.Usable)
            return new AccessTileLook(Hover: AccessText.UsableTip(label, name), Usable: true);
        return new AccessTileLook();
    }
}

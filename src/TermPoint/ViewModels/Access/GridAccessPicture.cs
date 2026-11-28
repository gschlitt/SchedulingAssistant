using TermPoint.Models;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>
/// How one section's line is drawn on the grid while a watch is open (the r9 wireframe's <c>entry</c>): at most one of
/// the looks below applies, plus <see cref="Pinned"/>, and the hover line says why. All false and no hover means
/// "drawn as usual". Immutable.
/// </summary>
/// <param name="Faded">Drawn faded: an unticked course, or not in the slate being shown.</param>
/// <param name="Stranded">In no slate: no student taking the watch's courses can use it.</param>
/// <param name="Essential">Every slate uses it (●): its component's other sections can't fit.</param>
/// <param name="CantFit">A course that can't fit with the rest (▲): this section is of it, or is a pin no slate can use.</param>
/// <param name="Caution">Hard to reach or heavy load (◆).</param>
/// <param name="InSlate">In the slate being stepped to: drawn as a ghost.</param>
/// <param name="Pinned">Pinned: only slates with it count.</param>
/// <param name="Hover">The hover line; empty when there is nothing to say.</param>
/// <param name="Usable">In at least one slate and nothing more to say (drawn as usual, with the "can be part of a slate" hover).</param>
/// <param name="HasWhy">The Access panel has a "Why" list for this section (shown when it is selected), so the tile's
/// hover ends with <see cref="AccessText.ClickForWhyText"/>.</param>
public sealed record AccessTileLook(
    bool Faded = false,
    bool Stranded = false,
    bool Essential = false,
    bool CantFit = false,
    bool Caution = false,
    bool InSlate = false,
    bool Pinned = false,
    string Hover = "",
    bool Usable = false,
    bool HasWhy = false)
{
    /// <summary>
    /// Whether a conflict mark of the open watch already says what this look's hover says, so the tile's hover shows
    /// the mark's line alone (Greg, 2026-11-25: the two lines repeated each other). The mark's line is kept because
    /// it is the same with or without the watch open and names every conflict, where the look names one.
    /// Stranded repeats a Stranded mark about this section; can't fit (a section to move) repeats a move mark on it;
    /// usable repeats a harmless mark ("other sections still let students get around"). An essential or only section
    /// and an Other mark say different things, so both stay.
    /// </summary>
    /// <param name="mark">A mark of the open watch that involves this section on the tile's day.</param>
    /// <param name="sectionId">This section's id.</param>
    /// <returns>True when the look's hover adds nothing to the mark's line.</returns>
    public bool IsRepeatedBy(ConflictMark mark, string sectionId) =>
        !string.IsNullOrEmpty(Hover) && mark.Kind switch
        {
            ConflictMarkKind.Stranded => Stranded && mark.X?.Id == sectionId,
            ConflictMarkKind.Move => CantFit,
            ConflictMarkKind.Harmless => Usable,
            _ => false,
        };

    /// <summary>
    /// The mark drawn after the label: "▲" for can't fit, "◆" for a caution, "●" for essential; empty for none.
    /// </summary>
    public string Mark => CantFit ? "▲" : Caution ? "◆" : Essential ? "●" : string.Empty;
}

/// <summary>
/// The picture of an open watch on the grid: for each section the watch covers, how its line is drawn and what its
/// hover says. A pure port of the r9 wireframe's <c>entry</c>, built from the picture's analysis, its ticks and pins
/// and the slate being stepped to. Sections the watch does not cover get no entry here: the grid draws them as usual.
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
            picture.IsCourseOnly, campusName, picture.KeptInPlace);
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
    /// <param name="keptInPlace">Ids of sections the user has said can't move; null for none.</param>
    /// <returns>The looks by section id.</returns>
    public static IReadOnlyDictionary<string, AccessTileLook> Build(
        AccessInputs inputs, Analysis analysis, string name, IReadOnlyList<Section> covered,
        IReadOnlyList<Section>? slate, IReadOnlyList<Caution> cautions, bool isCourseOnly, Func<string?, string> campusName,
        IReadOnlySet<string>? keptInPlace = null)
    {
        var looks = new Dictionary<string, AccessTileLook>();
        var chosen = analysis.ChosenCourseIds.ToHashSet();
        var pins = analysis.PinnedSectionIds;
        var inSlate = slate?.Select(s => s.Id).ToHashSet();
        var cautionBySection = cautions.GroupBy(c => c.Section.Id).ToDictionary(g => g.Key, g => g.First());
        var kept = keptInPlace ?? new HashSet<string>();
        var movers = Movers(analysis, kept);
        // With no full slate the roles are judged against the slates the suggested move would bring.
        string? move = AccessText.SuggestedMove(analysis, kept);
        foreach (var s in covered)
        {
            var look = LookOf(inputs, analysis, name, s, chosen, inSlate, cautionBySection, movers, kept, move, isCourseOnly, campusName);
            looks[s.Id] = pins.Contains(s.Id) ? look with { Pinned = true } : look;
        }
        return looks;
    }

    /// <summary>One section to move, with its problem and how many separate problems the watch has.</summary>
    private sealed record Mover(MoveOption Option, MoveProblem Problem, int ProblemCount);

    /// <summary>
    /// With no full slate, the sections that get the ▲: the sections to move, the fewest moves that bring a full
    /// slate back (<see cref="MoveSuggestions"/>; Greg, 2026-11-25: show what can be done, not every conflict).
    /// Tangled problems give none. With the campus rule to blame no time conflict is the reason, so this is empty and
    /// <see cref="LookOf"/> puts the ▲ on every section of the courses that can't fit, as before.
    /// </summary>
    /// <param name="a">The analysis (with its ticks and pins).</param>
    /// <param name="kept">Ids of sections the user has said can't move.</param>
    /// <returns>The sections to move by id; empty when there is a full slate.</returns>
    private static IReadOnlyDictionary<string, Mover> Movers(Analysis a, IReadOnlySet<string> kept)
    {
        var movers = new Dictionary<string, Mover>();
        var moves = MoveSuggestions.For(a, kept);
        foreach (var problem in moves.Problems)
            foreach (var option in problem.Options)
                movers.TryAdd(option.Section.Id, new Mover(option, problem, moves.Problems.Count));
        return movers;
    }

    /// <summary>One covered section's look (the body of the mock's <c>entry</c>); <paramref name="move"/> is
    /// <see cref="AccessText.SuggestedMove"/>, null with a full slate.</summary>
    private static AccessTileLook LookOf(
        AccessInputs inputs, Analysis a, string name, Section s, IReadOnlySet<string> chosen, IReadOnlySet<string>? inSlate,
        IReadOnlyDictionary<string, Caution> cautions, IReadOnlyDictionary<string, Mover> movers, IReadOnlySet<string> kept,
        string? move, bool isCourseOnly, Func<string?, string> campusName)
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
            // The campus rule to blame: every section of a course that can't fit, as no time conflict is the reason.
            if (a.CampusReason is { } cr && a.BestPartial is { } best && best.CantFit.Contains(s.CourseId))
            {
                var selfFail = a.SelfFailCourses.FirstOrDefault(x => x.CourseId == s.CourseId);
                string hover = selfFail is not null
                    ? AccessText.CourseCantBeTakenTip(code, AccessText.SelfFailWhy(inputs, a, selfFail, campusName))
                    : AccessText.CourseCantFitTip(code, name, AccessText.CampusWhy(inputs, cr, false, campusName));
                return new AccessTileLook(CantFit: true, Hover: hover);
            }
            if (movers.TryGetValue(s.Id, out var mover))
                return new AccessTileLook(CantFit: true, Hover: AccessText.MoveTip(inputs, s,
                    mover.Option.ConflictsWith, mover.Problem, mover.ProblemCount, name),
                    HasWhy: mover.Option.Reason is { Chain.Count: > 0 });
            if (kept.Contains(s.Id))
                return new AccessTileLook(Hover: AccessText.KeptInPlaceTip(label));
            // Otherwise on to the roles, judged against the most complete partial slates (Greg, 2026-11-27: a
            // stranded section is marked whether or not there is a full slate).
        }

        if (role == SectionRole.Stranded)
            return new AccessTileLook(Stranded: true, Hover: AccessText.StrandedClause(inputs, a, s) is { } clause
                ? AccessText.StrandedNarrativeTip(AccessText.StudentsIn(name, isCourseOnly), label, clause)
                : AccessText.StrandedTip(label, name,
                    AccessText.StrandedWhy(inputs, s, a.StrandedReasonFor(s), campusName, !isCourseOnly, !a.HasSlate)),
                HasWhy: a.Reasons.ChainFor(s).Count >= 2);
        if (cautions.TryGetValue(s.Id, out var caution))
            return new AccessTileLook(Caution: true, Hover: AccessText.CautionText(inputs, caution));
        if (role == SectionRole.EssentialByTiming)
            return new AccessTileLook(Essential: true, Hover: AccessText.EssentialTip(label, code, name, move));
        if (role == SectionRole.OnlySection)
        {
            if (a.PinnedSectionIds.Contains(s.Id)) return new AccessTileLook(Hover: AccessText.PinnedTip(label));
            string word = s.CourseId is not null && inputs.ComponentsOf(s.CourseId).Count >= 2
                ? inputs.SectionTypes.FirstOrDefault(t => t.Id == s.SectionTypeId)?.Name.ToLowerInvariant() ?? "section"
                : "section";
            return new AccessTileLook(Hover: AccessText.OnlySectionTip(label, code, word, move is not null));
        }
        // "Can be part of a slate" would be false with no full slate.
        if (role == SectionRole.Usable && a.HasSlate)
            return new AccessTileLook(Hover: AccessText.UsableTip(label, name), Usable: true);
        return new AccessTileLook();
    }
}

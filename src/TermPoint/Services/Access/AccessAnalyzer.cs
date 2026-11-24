using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The access analyzer: for a set of covered sections it groups sections into components (rules 7-10), counts
/// slates (rules 16-23), assigns each section a role, and works out why there is no slate when there is none. A
/// port of the r9 wireframe's <c>analyse</c> that returns structured data only. Pure and static; nothing is stored.
/// </summary>
public static class AccessAnalyzer
{
    /// <summary>The default slate-count cap (rule 19).</summary>
    public const int DefaultCap = 10000;

    /// <summary>
    /// The most courses analysed together (Q53, Greg 2026-11-24). No student's term holds more, so a larger set is
    /// a tag that doesn't describe one, and its conflict marks get slow: measured on UBC Engineering (piece C.3),
    /// 32 courses took 19 ms for marks, 110 or more about 0.5 s, against 30 ms for recomputing every watch. Above
    /// it the analysis has <see cref="AccessHealth.TooLarge"/> and nothing else, and <see cref="ConflictMarks"/>
    /// marks nothing. Applies to the chosen courses, so a subset of a large watch can still be analysed.
    /// </summary>
    public const int MaxCourses = 30;

    private static readonly IReadOnlySet<string> NoIds = new HashSet<string>();

    /// <summary>
    /// Analyses a watch (decision D28): the sections of the view that carry its tag, with its Multi-campus box,
    /// and sets <see cref="Analysis.HasUntaggedSections"/> (8g).
    /// </summary>
    /// <param name="inputs">The semester view.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="subsetCourseIds">Courses to analyse; <c>null</c> for all the watch's offered courses.</param>
    /// <param name="pinnedSectionIds">Pinned section ids; <c>null</c> for none.</param>
    /// <param name="cap">The slate-count cap.</param>
    /// <returns>The analysis.</returns>
    public static Analysis Analyze(
        AccessInputs inputs, AccessWatch watch,
        IReadOnlySet<string>? subsetCourseIds = null, IReadOnlySet<string>? pinnedSectionIds = null, int cap = DefaultCap) =>
        AnalyzeCore(inputs, watch.CoveredSections(inputs), !watch.MultiCampus, subsetCourseIds, pinnedSectionIds, cap, watch.TagId);

    /// <summary>
    /// Analyses a set of covered sections.
    /// </summary>
    /// <param name="inputs">The semester view (components, setup, the missing check).</param>
    /// <param name="covered">The sections under analysis; courses are those of these sections.</param>
    /// <param name="multiCampus">True: slates may mix campuses (the one-campus slates are counted separately). False: the one-campus rule applies.</param>
    /// <param name="subsetCourseIds">Courses to analyse; <c>null</c> for all offered. Subsets are by course, never by component (rule 17).</param>
    /// <param name="pinnedSectionIds">Pinned section ids; <c>null</c> for none (rule 18).</param>
    /// <param name="cap">The slate-count cap; must be at least 1.</param>
    /// <returns>The analysis.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is less than 1.</exception>
    public static Analysis Analyze(
        AccessInputs inputs, IReadOnlyList<Section> covered, bool multiCampus,
        IReadOnlySet<string>? subsetCourseIds, IReadOnlySet<string>? pinnedSectionIds, int cap = DefaultCap) =>
        AnalyzeCore(inputs, covered, !multiCampus, subsetCourseIds, pinnedSectionIds, cap, null);

    /// <summary>The shared implementation; <paramref name="watchTagId"/> is set only for watches (the untagged flag).</summary>
    private static Analysis AnalyzeCore(
        AccessInputs inputs, IReadOnlyList<Section> covered, bool oneCampusRule,
        IReadOnlySet<string>? subsetCourseIds, IReadOnlySet<string>? pinnedSectionIds, int cap, string? watchTagId)
    {
        if (cap < 1) throw new ArgumentOutOfRangeException(nameof(cap), "The cap must be at least 1.");
        var pins = pinnedSectionIds ?? NoIds;
        var offered = covered.Where(s => s.CourseId is not null).Select(s => s.CourseId!)
            .Distinct().OrderBy(c => c, inputs.CourseOrder).ToList();
        var chosen = subsetCourseIds is null ? offered : offered.Where(subsetCourseIds.Contains).ToList();
        if (chosen.Count > MaxCourses)
            return new Analysis
            {
                OfferedCourseIds = offered,
                ChosenCourseIds = chosen,
                PinnedSectionIds = pins,
                OneCampusRule = oneCampusRule,
                Health = AccessHealth.TooLarge,
            };
        var grouping = ComponentGrouping.Build(inputs, covered, chosen, pins);
        var groups = grouping.Groups;
        var search = new SlateSearch(groups, oneCampusRule);

        bool hasGroups = groups.Count > 0;
        var counted = hasGroups ? search.Count(cap) : new SlateCountResult(0, false, NoIds, new Dictionary<string, int>());
        bool noSlate = hasGroups && counted.Count == 0;
        bool hasSlate = counted.Count > 0;

        // Roles (rules 20, 21)
        var roles = new Dictionary<string, SectionRole>();
        var essential = new List<Section>();
        var only = new List<Section>();
        var stranded = new List<Section>();
        foreach (var g in groups)
        {
            int usedInGroup = g.Candidates.Count(s => counted.UsedSectionIds.Contains(s.Id));
            foreach (var s in g.Candidates)
            {
                if (!hasSlate) roles[s.Id] = SectionRole.None;
                else if (!counted.UsedSectionIds.Contains(s.Id)) { roles[s.Id] = SectionRole.Stranded; stranded.Add(s); }
                else if (usedInGroup == 1 && g.Candidates.Count > 1) { roles[s.Id] = SectionRole.EssentialByTiming; essential.Add(s); }
                else if (usedInGroup == 1) { roles[s.Id] = SectionRole.OnlySection; if (!g.Pinned) only.Add(s); }
                else roles[s.Id] = SectionRole.Usable;
            }
        }

        // Round 6c: one section per component
        bool single = hasGroups && groups.All(g => g.All.Count == 1);
        var singlePairs = new List<SectionPair>();
        if (single)
        {
            var ones = groups.Select(g => g.All[0]).ToList();
            for (int i = 0; i < ones.Count; i++)
                for (int j = i + 1; j < ones.Count; j++)
                    if (SectionOverlap.Conflicts(ones[i], ones[j])) singlePairs.Add(new SectionPair(ones[i], ones[j]));
        }

        // Round 6b: conflicts between sections of different groups
        int conflictCount = 0;
        for (int i = 0; i < groups.Count; i++)
            for (int j = i + 1; j < groups.Count; j++)
                foreach (var s in groups[i].All)
                    foreach (var t in groups[j].All)
                        if (SectionOverlap.Conflicts(s, t)) conflictCount++;

        var pinBlocks = new List<PinBlock>();
        var selfFail = new List<SelfFailCourse>();
        BestPartial? best = null;
        CampusReason? campusReason = null;
        int? oneCampusCount = null, mixedCount = null;
        CampusReason? mixedReason = null;

        if (noSlate)
        {
            bool pinsCause = pins.Count > 0 && new SlateSearch(
                ComponentGrouping.Build(inputs, covered, chosen, NoIds).Groups, oneCampusRule).Exists();
            if (pinsCause)
            {
                // Did pins cause it? Name each pin that cannot fit while the others are kept.
                foreach (var pin in groups.SelectMany(g => g.Candidates).Where(s => pins.Contains(s.Id)))
                {
                    var rest = pins.Where(id => id != pin.Id).ToHashSet();
                    var without = AnalyzeCore(inputs, covered, oneCampusRule, subsetCourseIds, rest, cap, null);
                    if (without.HasSlate && without.Roles.TryGetValue(pin.Id, out var role) && role == SectionRole.Stranded)
                        pinBlocks.Add(new PinBlock(pin, without.StrandedReasonFor(pin)));
                }
                if (pinBlocks.Count == 0) pinBlocks.Add(new PinBlock(null, null));
            }
            else
            {
                // Round 6d: courses that cannot be taken even alone come first and are left out of the best partial
                var courseIds = groups.Select(g => g.CourseId).Distinct().ToList();
                foreach (var courseId in courseIds.Where(c => !search.ExistsForCourses([c])))
                    selfFail.Add(new SelfFailCourse(courseId, AccessReasons.SelfFail(courseId, groups, oneCampusRule)));
                best = AccessReasons.Best(groups, search, selfFail.Select(x => x.CourseId).ToList());
                // With the one-campus rule and no course failing alone: is the rule alone to blame?
                if (selfFail.Count == 0 && oneCampusRule && search.WithCampusRule(false).Exists())
                    campusReason = AccessReasons.Campus(groups);
            }
        }
        else if (hasSlate && !oneCampusRule)
        {
            // Round 6e (Multi-campus on): how many slates keep to one campus; none does: say why
            var oneCampus = search.WithCampusRule(true).Count(cap);
            oneCampusCount = oneCampus.Count;
            mixedCount = counted.Capped ? null : counted.Count - oneCampus.Count;
            if (oneCampus.Count == 0) mixedReason = AccessReasons.Campus(groups);
        }

        var noTimes = groups.SelectMany(g => g.Candidates)
            .Where(s => s.Schedule.Count == 0 && (!hasSlate || counted.UsedSectionIds.Contains(s.Id))).ToList();

        var health = noSlate || grouping.Missing.Count > 0 || grouping.NotCounted.Count > 0 ? AccessHealth.Attention
            : essential.Count > 0 || mixedReason is not null ? AccessHealth.Fragile
            : AccessHealth.Ok;

        bool untagged = watchTagId is not null && offered.Any(c =>
            inputs.SectionsByCourse.TryGetValue(c, out var inSemester) && inSemester.Any(s => !s.TagIds.Contains(watchTagId)));

        return new Analysis
        {
            OfferedCourseIds = offered,
            ChosenCourseIds = chosen,
            PinnedSectionIds = pins,
            Groups = groups,
            MissingComponents = grouping.Missing,
            NotCountedSections = grouping.NotCounted,
            Mismatches = ComponentGrouping.Mismatches(inputs, offered),
            SlateCount = counted.Count,
            SlateCountCapped = counted.Capped,
            Roles = roles,
            EssentialByTimingSections = essential,
            OnlySections = only,
            StrandedSections = stranded,
            SlatesPerSection = counted.SlatesPerSection,
            IsSingleSectionCase = single,
            SingleSectionConflicts = singlePairs,
            ConflictCount = conflictCount,
            SpansCampuses = groups.SelectMany(g => g.All).Where(CampusRule.IsInPerson).Select(s => s.CampusId).Distinct().Count() > 1,
            OneCampusRule = oneCampusRule,
            PinBlocks = pinBlocks,
            SelfFailCourses = selfFail,
            BestPartial = best,
            CampusReason = campusReason,
            OneCampusSlateCount = oneCampusCount,
            MixedSlateCount = mixedCount,
            MixedCampusReason = mixedReason,
            SectionsWithoutTimes = noTimes,
            Health = health,
            HasUntaggedSections = untagged,
        };
    }
}

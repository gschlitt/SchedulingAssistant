using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// The kinds of change to a watch that make a notice (rule 31, as the r9 wireframe's <c>accessChanges</c>). Declared
/// worst first: the declaration order is the ranking, so a watch's worst change is its smallest value and a list of
/// changes sorts by it.
/// </summary>
public enum AccessChangeKind
{
    /// <summary>No full slate now (for a one-section watch: its sections stop fitting together).</summary>
    Lost,
    /// <summary>A course left the watch (none of its sections carry the tag any more).</summary>
    Left,
    /// <summary>Multi-campus on, and every slate now mixes campuses.</summary>
    Mixes,
    /// <summary>Every slate now uses a section it did not have to use before (the component's other sections are stranded).</summary>
    Essential,
    /// <summary>A section is newly stranded: no student taking the watch's courses can use it.</summary>
    Stranded,
    /// <summary>Good news: the watch has a full slate again.</summary>
    Restored,
}

/// <summary>
/// One watch's change between two states of a semester, counting only the kinds that make a notice. Structured data
/// only (ids and kinds); the sentence is <c>AccessText.NoticeText</c>.
/// </summary>
/// <param name="WatchId">The watched tag's id.</param>
/// <param name="Kind">What changed.</param>
/// <param name="WasSingleSection">For <see cref="AccessChangeKind.Lost"/> and <see cref="AccessChangeKind.Restored"/>: the watch had one section per component before, so it speaks of fitting together, not of slates.</param>
/// <param name="SectionIds">For <see cref="AccessChangeKind.Essential"/> and <see cref="AccessChangeKind.Stranded"/>: the sections newly in that role.</param>
/// <param name="CourseIds">For <see cref="AccessChangeKind.Left"/>: the courses that left, in course order.</param>
public sealed record AccessChange(
    string WatchId,
    AccessChangeKind Kind,
    bool WasSingleSection,
    IReadOnlyList<string> SectionIds,
    IReadOnlyList<string> CourseIds)
{
    /// <summary>Whether the change is bad news (everything but <see cref="AccessChangeKind.Restored"/>).</summary>
    public bool IsBad => Kind != AccessChangeKind.Restored;
}

/// <summary>What a notice names as the cause of a change.</summary>
public enum NoticeCauseKind
{
    /// <summary>The one changed section moved and now conflicts with sections it did not conflict with before (<see cref="NoticeCause.Partners"/>).</summary>
    NewConflicts,
    /// <summary>The one changed section moved to another campus (<see cref="NoticeCause.CampusId"/>).</summary>
    MovedToCampus,
    /// <summary>The one changed section moved (day or time).</summary>
    Moved,
    /// <summary>The one changed section's section type changed.</summary>
    SectionType,
    /// <summary>One section was added. DRAFT wording.</summary>
    Added,
    /// <summary>One section was deleted. DRAFT wording.</summary>
    Deleted,
    /// <summary>The one changed section's tags changed. DRAFT wording.</summary>
    Tags,
    /// <summary>The one changed section's course changed. DRAFT wording.</summary>
    Course,
    /// <summary>Several sections changed at once (a reader refresh, an import), or the cause could not be told. DRAFT wording.</summary>
    LatestChanges,
    /// <summary>One or several courses' components changed (<see cref="NoticeCause.CourseIds"/>).</summary>
    Components,
}

/// <summary>
/// The cause of a change: what to say it was. Structured, so the wording stays in <c>AccessText</c>.
/// </summary>
/// <param name="Kind">What kind of cause.</param>
/// <param name="Section">The section the cause is about (the changed section as it is now; the deleted one for <see cref="NoticeCauseKind.Deleted"/>); null for the general causes.</param>
/// <param name="Partners">For <see cref="NoticeCauseKind.NewConflicts"/>: the sections it newly conflicts with.</param>
/// <param name="CampusId">For <see cref="NoticeCauseKind.MovedToCampus"/>: the campus it moved to.</param>
/// <param name="CourseIds">For <see cref="NoticeCauseKind.Components"/>: the courses whose components changed.</param>
public sealed record NoticeCause(
    NoticeCauseKind Kind,
    Section? Section,
    IReadOnlyList<Section> Partners,
    string? CampusId,
    IReadOnlyList<string> CourseIds)
{
    /// <summary>The general cause for several changes at once.</summary>
    public static NoticeCause Latest { get; } = new(NoticeCauseKind.LatestChanges, null, [], null, []);

    /// <summary>A cause about one section.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="section">The section.</param>
    /// <returns>The cause.</returns>
    public static NoticeCause Of(NoticeCauseKind kind, Section section) => new(kind, section, [], null, []);
}

/// <summary>
/// A notice in the Access tab: one watch's change in one semester and its cause. Session-only and immutable; the
/// tab words it. Ids that no longer play their role are dropped by <see cref="AccessNotices.Refresh"/>.
/// </summary>
/// <param name="Id">A number unique in the session, for Dismiss and Open.</param>
/// <param name="WatchId">The watched tag's id.</param>
/// <param name="SemesterId">The semester the change was seen in.</param>
/// <param name="Change">What changed.</param>
/// <param name="Cause">What caused it.</param>
public sealed record AccessNotice(int Id, string WatchId, string SemesterId, AccessChange Change, NoticeCause Cause)
{
    /// <summary>Whether the notice is bad news.</summary>
    public bool IsBad => Change.IsBad;
}

/// <summary>
/// The notices of the Access tab (rules 31 and 32): comparing a semester's results before and after a change, naming
/// the cause, and checking whether a notice is still true. Pure and static; the session list lives in
/// <see cref="AccessState"/>. A port of the r9 wireframe's <c>accessChanges</c> and <c>causeOf</c>.
/// </summary>
public static class AccessNotices
{
    /// <summary>
    /// Whether a watch's analysis has a full slate: a slate exists and no course is missing a component. The test
    /// every notice kind rests on.
    /// </summary>
    /// <param name="analysis">The watch's analysis.</param>
    /// <returns><c>true</c> when students can take every course.</returns>
    public static bool HasFullSlate(Analysis analysis) => analysis.HasSlate && analysis.MissingComponents.Count == 0;

    /// <summary>
    /// What a change did to each watch present in both states, counting only the kinds that make a notice: lost,
    /// restored, a course leaving, newly depends on a section, every slate now mixes campuses (Multi-campus on) and
    /// a section newly stranded. A smaller drop in slates is not one. Watches are compared whole (no ticks, no pins).
    /// A watch too large to analyse in either state is skipped. Forces both states' lazy analyses.
    /// </summary>
    /// <param name="before">The semester's results before the change.</param>
    /// <param name="after">The semester's results after it.</param>
    /// <returns>One change per watch (its worst), worst first: lost, a course leaving, mixed campuses, depends on, stranded, then good news.</returns>
    public static IReadOnlyList<AccessChange> Compare(SemesterAccess before, SemesterAccess after)
    {
        var found = new List<AccessChange>();
        foreach (var b in before.Watches)
        {
            var a = after.Watches.FirstOrDefault(w => w.Tag.Id == b.Tag.Id);
            if (a is null) continue;
            var was = b.Analysis;
            var now = a.Analysis;
            if (was.Health == AccessHealth.TooLarge || now.Health == AccessHealth.TooLarge) continue;
            var change = WorstChange(b.Tag.Id, was, now);
            if (change is not null) found.Add(change);
        }
        // OrderBy is stable, so watches of one kind keep the tags' order.
        return found.OrderBy(c => c.Kind).ToList();
    }

    /// <summary>The worst change between two analyses of one watch, or null when none of them makes a notice.</summary>
    private static AccessChange? WorstChange(string watchId, Analysis was, Analysis now)
    {
        var all = new List<AccessChange>();
        var left = was.OfferedCourseIds.Where(c => !now.OfferedCourseIds.Contains(c)).ToList();
        if (left.Count > 0) all.Add(new AccessChange(watchId, AccessChangeKind.Left, was.IsSingleSectionCase, [], left));

        bool hadFull = HasFullSlate(was), hasFull = HasFullSlate(now);
        if (hadFull && !hasFull) all.Add(new AccessChange(watchId, AccessChangeKind.Lost, was.IsSingleSectionCase, [], []));
        else if (!hadFull && hasFull) all.Add(new AccessChange(watchId, AccessChangeKind.Restored, was.IsSingleSectionCase, [], []));
        else if (hasFull)
        {
            var essential = now.EssentialByTimingSections.Select(s => s.Id)
                .Where(id => !was.EssentialByTimingSections.Any(s => s.Id == id)).ToList();
            if (essential.Count > 0)
                all.Add(new AccessChange(watchId, AccessChangeKind.Essential, was.IsSingleSectionCase, essential, []));
            if (now.MixedCampusReason is not null && was.MixedCampusReason is null)
                all.Add(new AccessChange(watchId, AccessChangeKind.Mixes, was.IsSingleSectionCase, [], []));
            // Round 7 (Greg, 2026-11-20): a section newly stranded speaks too
            var stranded = now.StrandedSections.Select(s => s.Id)
                .Where(id => !was.StrandedSections.Any(s => s.Id == id)).ToList();
            if (stranded.Count > 0)
                all.Add(new AccessChange(watchId, AccessChangeKind.Stranded, was.IsSingleSectionCase, stranded, []));
        }
        return all.OrderBy(c => c.Kind).FirstOrDefault();
    }

    /// <summary>
    /// What caused a change. Components first: when courses' components changed, that is the cause (no section
    /// changed). Otherwise the semester's section diff: exactly one section added, deleted or changed names the
    /// cause (as the mock's <c>causeOf</c>: the new conflicts the moved section makes in this watch, else a campus
    /// move, a move, a section type change, then tags and course); more than one, or none found, is the general
    /// "latest changes".
    /// </summary>
    /// <param name="before">The semester's results before.</param>
    /// <param name="after">The semester's results after.</param>
    /// <param name="change">The change to explain (its watch and kind steer the new-conflicts test).</param>
    /// <param name="componentCourseIds">The courses whose components changed since <paramref name="before"/>; null when none did.</param>
    /// <returns>The cause.</returns>
    public static NoticeCause CauseOf(
        SemesterAccess before, SemesterAccess after, AccessChange change, IReadOnlyList<string>? componentCourseIds)
    {
        if (componentCourseIds is not null)
            return componentCourseIds.Count == 0
                ? NoticeCause.Latest
                : new NoticeCause(NoticeCauseKind.Components, null, [], null, componentCourseIds);

        var was = before.Inputs.Sections.ToDictionary(s => s.Id);
        var now = after.Inputs.Sections.ToDictionary(s => s.Id);
        var added = now.Values.Where(s => !was.ContainsKey(s.Id)).ToList();
        var removed = was.Values.Where(s => !now.ContainsKey(s.Id)).ToList();
        var changed = now.Values.Where(s => was.TryGetValue(s.Id, out var old) && AccessRelevantlyDiffers(old, s)).ToList();
        if (added.Count + removed.Count + changed.Count != 1) return NoticeCause.Latest;
        if (added.Count == 1) return NoticeCause.Of(NoticeCauseKind.Added, added[0]);
        if (removed.Count == 1) return NoticeCause.Of(NoticeCauseKind.Deleted, removed[0]);

        var me = changed[0];
        var old1 = was[me.Id];
        bool moved = MeetingsKey(old1) != MeetingsKey(me);
        if (moved && change.Kind != AccessChangeKind.Restored)
        {
            var partners = NewConflictPartners(before, after, change.WatchId, me.Id);
            if (partners.Count > 0) return new NoticeCause(NoticeCauseKind.NewConflicts, me, partners, null, []);
        }
        if (old1.CampusId != me.CampusId) return new NoticeCause(NoticeCauseKind.MovedToCampus, me, [], me.CampusId, []);
        if (moved) return NoticeCause.Of(NoticeCauseKind.Moved, me);
        if (old1.SectionTypeId != me.SectionTypeId) return NoticeCause.Of(NoticeCauseKind.SectionType, me);
        if (!old1.TagIds.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(me.TagIds.OrderBy(t => t, StringComparer.Ordinal)))
            return NoticeCause.Of(NoticeCauseKind.Tags, me);
        return NoticeCause.Of(NoticeCauseKind.Course, me);
    }

    /// <summary>
    /// The sections of the watch, of another component than the moved one, that the moved section now conflicts
    /// with and did not before.
    /// </summary>
    private static IReadOnlyList<Section> NewConflictPartners(SemesterAccess before, SemesterAccess after, string watchId, string sectionId)
    {
        var wasWatch = before.Watches.FirstOrDefault(w => w.Tag.Id == watchId);
        var nowWatch = after.Watches.FirstOrDefault(w => w.Tag.Id == watchId);
        if (wasWatch is null || nowWatch is null) return [];
        var nowAll = nowWatch.Analysis.Groups.SelectMany(g => g.All).ToList();
        var me = nowAll.FirstOrDefault(s => s.Id == sectionId);
        var old = wasWatch.Analysis.Groups.SelectMany(g => g.All).FirstOrDefault(s => s.Id == sectionId);
        if (me is null || old is null) return [];
        return nowAll
            .Where(t => t.Id != sectionId
                        && (t.CourseId != me.CourseId || t.SectionTypeId != me.SectionTypeId)
                        && SectionOverlap.Conflicts(me, t) && !SectionOverlap.Conflicts(old, t))
            .ToList();
    }

    /// <summary>Whether two versions of a section differ in anything access reads: meetings, campus, section type, course or tags.</summary>
    private static bool AccessRelevantlyDiffers(Section a, Section b) =>
        a.CampusId != b.CampusId
        || a.SectionTypeId != b.SectionTypeId
        || a.CourseId != b.CourseId
        || MeetingsKey(a) != MeetingsKey(b)
        || !a.TagIds.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(b.TagIds.OrderBy(t => t, StringComparer.Ordinal));

    /// <summary>A comparable text of a section's meetings (day, start, length, weeks, remote): what makes a move.</summary>
    private static string MeetingsKey(Section s) =>
        string.Join("|", s.Schedule
            .Select(m => $"{m.Day},{m.StartMinutes},{m.DurationMinutes},{m.Frequency},{m.IsRemote}")
            .OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>
    /// Checks whether a notice is still true of the watch's current analysis, narrowing it when part of it no longer
    /// holds. Lost: the watch has no full slate. Restored: it has one. Essential and stranded: only the sections
    /// still in that role stay, and none left drops it. Mixes: every slate still mixes campuses. Left: only the
    /// courses still out of the watch stay.
    /// </summary>
    /// <param name="notice">The notice.</param>
    /// <param name="now">The watch's analysis in the notice's semester now.</param>
    /// <returns>The notice, possibly with fewer ids; <c>null</c> when it stopped being true.</returns>
    public static AccessNotice? Refresh(AccessNotice notice, Analysis now)
    {
        var c = notice.Change;
        switch (c.Kind)
        {
            case AccessChangeKind.Lost:
                return HasFullSlate(now) ? null : notice;
            case AccessChangeKind.Restored:
                return HasFullSlate(now) ? notice : null;
            case AccessChangeKind.Mixes:
                return now.MixedCampusReason is not null ? notice : null;
            case AccessChangeKind.Essential:
            case AccessChangeKind.Stranded:
            {
                var inRole = (c.Kind == AccessChangeKind.Essential ? now.EssentialByTimingSections : now.StrandedSections)
                    .Select(s => s.Id).ToHashSet();
                var kept = c.SectionIds.Where(inRole.Contains).ToList();
                if (kept.Count == 0) return null;
                return kept.Count == c.SectionIds.Count ? notice : notice with { Change = c with { SectionIds = kept } };
            }
            default: // Left
            {
                var kept = c.CourseIds.Where(id => !now.OfferedCourseIds.Contains(id)).ToList();
                if (kept.Count == 0) return null;
                return kept.Count == c.CourseIds.Count ? notice : notice with { Change = c with { CourseIds = kept } };
            }
        }
    }
}

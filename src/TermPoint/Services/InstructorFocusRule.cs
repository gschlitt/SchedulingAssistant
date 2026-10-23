using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// The one place that decides whether an instructor is "in focus" for a <see cref="SubjectFocus"/> (spec item 20,
/// "Who counts as an in-focus instructor"). A pure function of the focus, the instructor and their
/// <see cref="TeachingHistory"/>: it holds no state and reads nothing else, so it can be unit-tested on its own
/// and changed later in this one method if users find it too narrow or too generous, without touching any view.
/// Consumers never call it directly; they go through <see cref="FocusService.IsInFocus"/> so they all agree.
/// </summary>
public static class InstructorFocusRule
{
    /// <summary>
    /// Applies the six-step rule, in order:
    /// <list type="number">
    ///   <item>Focus is All: in focus.</item>
    ///   <item>Focus is None: not in focus.</item>
    ///   <item>Inactive instructor: not in focus.</item>
    ///   <item>Any section in the window (the viewed academic year Y and the year before it, whole years): in focus
    ///   when at least one of those years' subjects is in the focus. Earlier years are ignored.</item>
    ///   <item>Nothing in the window but sections in an earlier year: take the most recent year before the window
    ///   in which they had any section; in focus when that year's subjects intersect the focus.</item>
    ///   <item>Nothing in the window and nothing earlier (a new hire; also an instructor whose only sections are in
    ///   years AFTER Y): in focus under every subject focus, so a just-added instructor does not vanish.</item>
    /// </list>
    /// <para>
    /// <b>Unknown viewed year.</b> When <paramref name="viewedAcademicYearId"/> is null or not a known year, the
    /// window is treated as EMPTY, so steps 5 and 6 apply over all years: the most recent year with any section
    /// decides. (There is no meaningful "year before" to look at, and "in focus nowhere" would hide everyone.)
    /// </para>
    /// <para>Levels play no part; the rule is by subject only.</para>
    /// </summary>
    /// <param name="focus">The current focus.</param>
    /// <param name="instructor">The instructor to judge.</param>
    /// <param name="history">The database-wide teaching history (see <see cref="TeachingHistory.Build"/>).</param>
    /// <param name="viewedAcademicYearId">The academic year being viewed (Y); null when none is selected.</param>
    /// <returns>True when the instructor is in focus.</returns>
    public static bool IsInFocus(
        SubjectFocus focus, Instructor instructor, TeachingHistory history, string? viewedAcademicYearId)
    {
        // Steps 1-3: the decisions that need no history.
        if (focus.Kind == FocusKind.All) return true;
        if (focus.Kind == FocusKind.None) return false;
        if (!instructor.IsActive) return false;

        var byYear = history.GetSubjectsByYear(instructor.Id);
        var viewedYear = history.GetYearIndex(viewedAcademicYearId);

        // Step 4: the window is Y and Y-1 (positions, so a missing calendar year does not matter). With no known
        // Y there is no window, so this finds nothing and the earlier-year steps look at every year.
        if (viewedYear is { } y)
        {
            var inWindow = byYear.Where(kv => kv.Key == y || kv.Key == y - 1).ToList();
            if (inWindow.Count > 0)
                return inWindow.Any(kv => Intersects(focus, kv.Value));
        }

        // Step 5: the most recent year before the window. Years after Y are not "earlier" and are ignored.
        var firstWindowYear = viewedYear is { } v ? v - 1 : int.MaxValue;
        var earlier = byYear.Where(kv => kv.Key < firstWindowYear).ToList();
        if (earlier.Count > 0)
        {
            var mostRecent = earlier.Max(kv => kv.Key);
            return Intersects(focus, byYear[mostRecent]);
        }

        // Step 6: nothing in the window and nothing earlier.
        return true;
    }

    /// <summary>True when at least one of <paramref name="subjectIds"/> is in the (Subjects) focus.</summary>
    /// <param name="focus">The focus.</param>
    /// <param name="subjectIds">The subjects taught in a year.</param>
    private static bool Intersects(SubjectFocus focus, IReadOnlySet<string> subjectIds) =>
        subjectIds.Any(focus.ContainsSubject);
}

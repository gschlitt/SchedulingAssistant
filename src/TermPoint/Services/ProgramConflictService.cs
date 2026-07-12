using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Identifies which specific meeting slots of a conflict pair overlap, so the
/// renderer knows exactly which day-column boxes to highlight and connect.
/// </summary>
public readonly record struct ConflictMeeting(
    string SectionId,
    string? CourseId,
    int Day,
    int StartMinutes,
    int EndMinutes,
    string? Frequency);

/// <summary>
/// A single detected program conflict between two meetings from different courses
/// within the same enabled watch. The renderer uses these to draw colored boxes
/// and connecting lines on the grid canvas.
/// </summary>
public readonly record struct ProgramConflict(
    string WatchId,
    string WatchName,
    ConflictMeeting MeetingA,
    ConflictMeeting MeetingB);

/// <summary>
/// Pure-computation service that detects time-overlap conflicts among sections
/// covered by enabled program watches. Static, no state, no DI registration.
/// Follows the pattern of <see cref="RoomConflictService"/>.
/// </summary>
public static class ProgramConflictService
{
    /// <summary>
    /// Scans the given sections for time overlaps within each enabled watch.
    /// </summary>
    /// <param name="enabledWatches">Only watches with <c>IsEnabled == true</c>.</param>
    /// <param name="sections">The filtered sections currently visible on the grid.</param>
    /// <param name="tagIdsBySectionId">
    /// Lookup of tag IDs per section ID. For tag-based watches, a section is covered
    /// only if it carries <em>all</em> of the watch's tags (AND logic). Tags are matched
    /// against the <em>section's own</em> tag list.
    /// </param>
    /// <returns>All detected conflicts, one entry per overlapping meeting pair per watch.</returns>
    public static List<ProgramConflict> DetectConflicts(
        IReadOnlyList<ProgramWatch> enabledWatches,
        IReadOnlyList<Section> sections,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tagIdsBySectionId)
    {
        var results = new List<ProgramConflict>();

        foreach (var watch in enabledWatches)
        {
            var covered = ResolveCoveredSections(watch, sections, tagIdsBySectionId);

            // Group covered sections by course so we only compare across different courses.
            var byCourse = new Dictionary<string, List<Section>>();
            foreach (var sec in covered)
            {
                var cid = sec.CourseId ?? string.Empty;
                if (!byCourse.TryGetValue(cid, out var list))
                {
                    list = [];
                    byCourse[cid] = list;
                }
                list.Add(sec);
            }

            var courseIds = byCourse.Keys.ToList();
            for (int ci = 0; ci < courseIds.Count; ci++)
            {
                for (int cj = ci + 1; cj < courseIds.Count; cj++)
                {
                    foreach (var secA in byCourse[courseIds[ci]])
                    {
                        foreach (var secB in byCourse[courseIds[cj]])
                        {
                            FindOverlappingMeetings(watch, secA, secB, results);
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the subset of <paramref name="sections"/> covered by the watch's
    /// definition — either by tag-match (AND logic) or by course-list membership.
    /// </summary>
    private static List<Section> ResolveCoveredSections(
        ProgramWatch watch,
        IReadOnlyList<Section> sections,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tagIdsBySectionId)
    {
        var covered = new List<Section>();

        if (watch.Mode == ProgramWatchMode.Tag)
        {
            var hasTags = watch.TagIds.Count > 0;
            var hasLevels = watch.LevelIds.Count > 0;

            if (!hasTags && !hasLevels) return covered;

            var levelSet = hasLevels ? new HashSet<string>(watch.LevelIds) : null;

            foreach (var sec in sections)
            {
                // Tag filter (AND): section must carry all listed tags.
                if (hasTags)
                {
                    if (!tagIdsBySectionId.TryGetValue(sec.Id, out var sectionTags))
                        continue;
                    if (!watch.TagIds.All(t => sectionTags.Contains(t)))
                        continue;
                }

                // Level filter (OR): section's level must match any listed level.
                if (levelSet is not null)
                {
                    if (string.IsNullOrEmpty(sec.Level) || !levelSet.Contains(sec.Level))
                        continue;
                }

                covered.Add(sec);
            }
        }
        else
        {
            if (watch.CourseIds.Count == 0) return covered;
            var courseIdSet = new HashSet<string>(watch.CourseIds);

            foreach (var sec in sections)
            {
                if (sec.CourseId is not null && courseIdSet.Contains(sec.CourseId))
                    covered.Add(sec);
            }
        }

        return covered;
    }

    /// <summary>
    /// Compares every schedule slot of <paramref name="secA"/> against every slot of
    /// <paramref name="secB"/> and emits a <see cref="ProgramConflict"/> for each
    /// same-day, time-overlapping, frequency-overlapping pair.
    /// </summary>
    private static void FindOverlappingMeetings(
        ProgramWatch watch,
        Section secA,
        Section secB,
        List<ProgramConflict> results)
    {
        foreach (var schedA in secA.Schedule)
        {
            foreach (var schedB in secB.Schedule)
            {
                if (schedA.Day != schedB.Day) continue;

                // Half-open interval overlap: [startA, endA) ∩ [startB, endB)
                if (schedA.StartMinutes >= schedB.EndMinutes ||
                    schedB.StartMinutes >= schedA.EndMinutes)
                    continue;

                if (!SectionDaySchedule.FrequenciesOverlap(schedA.Frequency, schedB.Frequency))
                    continue;

                results.Add(new ProgramConflict(
                    watch.Id,
                    watch.Name,
                    new ConflictMeeting(secA.Id, secA.CourseId, schedA.Day, schedA.StartMinutes, schedA.EndMinutes, schedA.Frequency),
                    new ConflictMeeting(secB.Id, secB.CourseId, schedB.Day, schedB.StartMinutes, schedB.EndMinutes, schedB.Frequency)));
            }
        }
    }
}

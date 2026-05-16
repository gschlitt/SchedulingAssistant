using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Tracks room bookings for a semester and answers availability queries.
/// Built once per browser session; rebuilt when the user changes filters or semester.
/// </summary>
public class OccupancyIndex
{
    private readonly Dictionary<(string RoomId, int Day), List<(int Start, int End)>> _bookings = new();

    /// <summary>
    /// Populates the index from sections and meetings in the given semester,
    /// excluding the section or meeting currently being edited.
    /// </summary>
    /// <param name="sections">All sections in the target semester.</param>
    /// <param name="meetings">All meetings in the target semester.</param>
    /// <param name="excludeSectionId">The section being edited (excluded from bookings).</param>
    /// <param name="excludeMeetingId">The meeting being edited (excluded from bookings).</param>
    public void Build(IEnumerable<Section> sections, IEnumerable<Meeting> meetings,
        string? excludeSectionId, string? excludeMeetingId = null)
    {
        _bookings.Clear();

        foreach (var section in sections)
        {
            if (section.Id == excludeSectionId) continue;
            AddSchedule(section.Schedule);
        }

        foreach (var meeting in meetings)
        {
            if (meeting.Id == excludeMeetingId) continue;
            AddSchedule(meeting.Schedule);
        }
    }

    private void AddSchedule(List<SectionDaySchedule> schedule)
    {
        foreach (var slot in schedule)
        {
            if (string.IsNullOrEmpty(slot.RoomId)) continue;
            var key = (slot.RoomId, slot.Day);
            if (!_bookings.TryGetValue(key, out var list))
            {
                list = new List<(int, int)>();
                _bookings[key] = list;
            }
            list.Add((slot.StartMinutes, slot.EndMinutes));
        }

        // Sort each list for predictable iteration (not strictly required for correctness)
        foreach (var list in _bookings.Values)
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    /// <summary>
    /// Returns <c>true</c> if the given room is free on the given day for the
    /// specified time range.
    /// </summary>
    public bool IsAvailable(string roomId, int day, int startMinutes, int durationMinutes)
    {
        int end = startMinutes + durationMinutes;
        if (!_bookings.TryGetValue((roomId, day), out var intervals))
            return true;

        foreach (var (bStart, bEnd) in intervals)
        {
            if (startMinutes < bEnd && bStart < end)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Generates <see cref="MeetingTemplate"/>s and <see cref="RoomSolution"/>s for the
/// Room Availability Browser. Stateless — all data is passed in or injected via repos.
/// </summary>
public class RoomAvailabilityService
{
    /// <summary>
    /// Generates default meeting templates from the cross product of block patterns
    /// and legal start times for the given academic year. Each template has uniform
    /// duration across all days; the user can customize per-day durations afterward.
    /// </summary>
    /// <param name="patterns">All configured block patterns.</param>
    /// <param name="legalStartTimes">All legal start times for the current academic year.</param>
    /// <returns>Templates sorted by pattern name then duration.</returns>
    public List<MeetingTemplate> GenerateTemplates(
        IReadOnlyList<BlockPattern> patterns,
        IReadOnlyList<LegalStartTime> legalStartTimes)
    {
        var templates = new List<MeetingTemplate>();

        foreach (var pattern in patterns)
        {
            foreach (var lst in legalStartTimes)
            {
                if (lst.StartTimes.Count == 0) continue;

                int durationMinutes = (int)(lst.BlockLength * 60);
                var daySpecs = pattern.Days
                    .Select(day => new TemplateDaySpec(day, lst.BlockLength, durationMinutes, lst.StartTimes.AsReadOnly()))
                    .ToList();

                templates.Add(new MeetingTemplate(pattern.Id, pattern.Name, daySpecs));
            }
        }

        return templates
            .OrderBy(t => t.PatternName)
            .ThenBy(t => t.DaySpecs.FirstOrDefault()?.DurationMinutes ?? 0)
            .ToList();
    }

    /// <summary>
    /// Builds a new <see cref="OccupancyIndex"/> from all sections and meetings in the
    /// specified semester, excluding the section or meeting being edited.
    /// </summary>
    public OccupancyIndex BuildOccupancyIndex(
        IEnumerable<Section> sections,
        IEnumerable<Meeting> meetings,
        string? excludeSectionId,
        string? excludeMeetingId = null)
    {
        var index = new OccupancyIndex();
        index.Build(sections, meetings, excludeSectionId, excludeMeetingId);
        return index;
    }

    /// <summary>
    /// Generates all feasible room availability solutions for the given template,
    /// room filter, and occupancy state. Existing meetings on the section are treated
    /// as fixed — only "gap days" (days in the template not yet covered) are solved.
    /// </summary>
    /// <param name="template">The meeting template (with per-day specs).</param>
    /// <param name="filteredRooms">Rooms that pass the user's filter criteria.</param>
    /// <param name="index">Occupancy index for the active semester.</param>
    /// <param name="existingMeetings">Meetings already on the section (for augment mode).</param>
    /// <returns>Solutions sorted by tier, then room label, then earliest start time.</returns>
    public List<RoomSolution> GenerateSolutions(
        MeetingTemplate template,
        IReadOnlyList<Room> filteredRooms,
        OccupancyIndex index,
        IReadOnlyList<SectionDaySchedule> existingMeetings)
    {
        // Determine which days still need to be filled.
        var existingDays = new HashSet<int>(existingMeetings.Select(m => m.Day));
        var gapSpecs = template.DaySpecs.Where(d => !existingDays.Contains(d.Day)).ToList();

        if (gapSpecs.Count == 0)
            return new List<RoomSolution>();

        // Convert existing meetings to fixed SolutionSlots for tier classification.
        var fixedSlots = existingMeetings
            .Select(m => new SolutionSlot(m.Day, m.StartMinutes, m.DurationMinutes, m.RoomId ?? "", ""))
            .ToList();

        var solutions = new List<RoomSolution>();
        var seen = new HashSet<string>();

        // --- Tier 1: same room, same start time across all gap days ---
        ScanTier1(gapSpecs, filteredRooms, index, fixedSlots, solutions, seen);

        // --- Tier 2a: same room, different times ---
        ScanTier2a(gapSpecs, filteredRooms, index, fixedSlots, solutions, seen);

        // --- Tier 2b: same start time, different rooms ---
        ScanTier2b(gapSpecs, filteredRooms, index, fixedSlots, solutions, seen);

        // --- Tier 3: bounded greedy with different room orderings ---
        ScanTier3(gapSpecs, filteredRooms, index, fixedSlots, solutions, seen);

        solutions.Sort((a, b) =>
        {
            int cmp = a.Tier.CompareTo(b.Tier);
            if (cmp != 0) return cmp;

            // Sort by first room label, then earliest start.
            string labelA = a.Slots.OrderBy(s => s.Day).FirstOrDefault()?.RoomLabel ?? "";
            string labelB = b.Slots.OrderBy(s => s.Day).FirstOrDefault()?.RoomLabel ?? "";
            cmp = string.Compare(labelA, labelB, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;

            int minA = a.Slots.Min(s => s.StartMinutes);
            int minB = b.Slots.Min(s => s.StartMinutes);
            return minA.CompareTo(minB);
        });

        return solutions;
    }

    /// <summary>
    /// Applies room filters to the full room list.
    /// </summary>
    public static List<Room> ApplyFilter(
        IReadOnlyList<Room> allRooms,
        string? campusId,
        string? building,
        string? roomTypeId,
        int? minCapacity)
    {
        return allRooms
            .Where(r => campusId == null || r.CampusId == campusId)
            .Where(r => building == null || string.Equals(r.Building, building, StringComparison.OrdinalIgnoreCase))
            .Where(r => roomTypeId == null || r.RoomTypeId == roomTypeId)
            .Where(r => minCapacity == null || r.Capacity >= minCapacity)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Building)
            .ThenBy(r => r.RoomNumber)
            .ToList();
    }

    // ── Spec-based solver (partial-spec input from meeting rows) ────────────

    /// <summary>
    /// Generates solutions from partially-specified meeting rows. Each <see cref="MeetingSpec"/>
    /// must have a duration; day and start time are optional. The solver fills in the gaps,
    /// preferring institutional block patterns for unspecified days.
    /// </summary>
    /// <param name="specs">One per meeting row — duration required, day/start optional.</param>
    /// <param name="filteredRooms">Rooms passing the user's global filter criteria.</param>
    /// <param name="index">Occupancy index for the active semester.</param>
    /// <param name="legalStartTimes">Legal start times for the current academic year.</param>
    /// <param name="blockPatterns">Institutional block patterns (MWF, TR, etc.) for day preference.</param>
    public List<RoomSolution> GenerateSolutionsFromSpecs(
        IReadOnlyList<MeetingSpec> specs,
        IReadOnlyList<Room> filteredRooms,
        OccupancyIndex index,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        IReadOnlyList<BlockPattern> blockPatterns)
    {
        if (specs.Count == 0 || filteredRooms.Count == 0)
            return new List<RoomSolution>();

        // Build a room lookup for locked-room specs.
        var roomById = filteredRooms.ToDictionary(r => r.Id);

        // Pre-compute per-spec room lists based on RoomId (locked) or RoomTypeId (type filter).
        var perSpecRooms = new IReadOnlyList<Room>[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            if (!string.IsNullOrEmpty(spec.RoomId))
            {
                // Locked to a specific room — must be in the filtered set.
                perSpecRooms[i] = roomById.TryGetValue(spec.RoomId, out var locked)
                    ? new[] { locked }
                    : Array.Empty<Room>();
            }
            else if (!string.IsNullOrEmpty(spec.RoomTypeId))
            {
                perSpecRooms[i] = filteredRooms
                    .Where(r => r.RoomTypeId == spec.RoomTypeId)
                    .ToList();
            }
            else
            {
                perSpecRooms[i] = filteredRooms;
            }
        }

        // Identify fully-fixed specs (room + day + start all set) and separate them as fixed slots.
        var fixedSlotList = new List<SolutionSlot>();
        var gapSpecIndices = new List<int>();
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            if (!string.IsNullOrEmpty(spec.RoomId) && spec.Day.HasValue && spec.StartMinutes.HasValue)
            {
                string label = roomById.TryGetValue(spec.RoomId, out var r) ? RoomLabel(r) : spec.RoomId;
                fixedSlotList.Add(new SolutionSlot(spec.Day.Value, spec.StartMinutes.Value,
                    spec.DurationMinutes, spec.RoomId, label));
            }
            else
            {
                gapSpecIndices.Add(i);
            }
        }

        // Build sub-lists for the gap specs only.
        var gapSpecs = gapSpecIndices.Select(i => specs[i]).ToList();
        var gapPerSpecRooms = gapSpecIndices.Select(i => perSpecRooms[i]).ToArray();

        // If everything is fully fixed, return the single deterministic solution.
        if (gapSpecs.Count == 0)
        {
            var solutions0 = new List<RoomSolution>();
            var seen0 = new HashSet<string>();
            TryAddSolution(new List<SolutionSlot>(), fixedSlotList, solutions0, seen0);
            return solutions0;
        }

        // Room lists for Tier 1/2a: intersection (same room must appear in all gap specs).
        IReadOnlyList<Room> intersectionRooms;
        if (gapPerSpecRooms.All(r => ReferenceEquals(r, filteredRooms)))
        {
            intersectionRooms = filteredRooms;
        }
        else
        {
            var ids = new HashSet<string>(gapPerSpecRooms[0].Select(r => r.Id));
            for (int i = 1; i < gapPerSpecRooms.Length; i++)
                ids.IntersectWith(gapPerSpecRooms[i].Select(r => r.Id));
            intersectionRooms = filteredRooms.Where(r => ids.Contains(r.Id)).ToList();
        }

        var solutions = new List<RoomSolution>();
        var seen = new HashSet<string>();

        // Separate fixed-day vs open-day specs (within the gap set).
        var fixedDays = new HashSet<int>(gapSpecs.Where(s => s.Day.HasValue).Select(s => s.Day!.Value));
        int openCount = gapSpecs.Count(s => !s.Day.HasValue);

        var dayAssignments = openCount > 0
            ? EnumerateDayAssignments(fixedDays, openCount, blockPatterns)
            : new List<(List<int> Days, bool IsPattern)> { (new List<int>(), true) };

        bool allStartsFixed = gapSpecs.All(s => s.StartMinutes.HasValue);

        foreach (var (assignment, isPattern) in dayAssignments)
        {
            int openIdx = 0;
            var resolvedDays = new int[gapSpecs.Count];
            for (int i = 0; i < gapSpecs.Count; i++)
                resolvedDays[i] = gapSpecs[i].Day ?? assignment[openIdx++];

            var resolved = ResolveToTemplateDaySpecs(gapSpecs, resolvedDays, legalStartTimes);
            if (resolved == null) continue;

            ScanTier1(resolved, intersectionRooms, index, fixedSlotList, solutions, seen, isPattern);
            ScanTier2a(resolved, intersectionRooms, index, fixedSlotList, solutions, seen, isPattern);
            ScanTier2b(resolved, filteredRooms, index, fixedSlotList, solutions, seen, isPattern, gapPerSpecRooms);
            ScanTier3(resolved, filteredRooms, index, fixedSlotList, solutions, seen, isPattern, gapPerSpecRooms);

            if (allStartsFixed)
            {
                var alt = ResolveToTemplateDaySpecs(gapSpecs, resolvedDays, legalStartTimes, openStarts: true);
                if (alt != null)
                {
                    ScanTier1(alt, intersectionRooms, index, fixedSlotList, solutions, seen, isPattern, isAlternative: true);
                    ScanTier2a(alt, intersectionRooms, index, fixedSlotList, solutions, seen, isPattern, isAlternative: true);
                    ScanTier2b(alt, filteredRooms, index, fixedSlotList, solutions, seen, isPattern, gapPerSpecRooms, isAlternative: true);
                    ScanTier3(alt, filteredRooms, index, fixedSlotList, solutions, seen, isPattern, gapPerSpecRooms, isAlternative: true);
                }
            }
        }

        solutions.Sort((a, b) =>
        {
            // Primaries before alternatives.
            if (a.IsAlternative != b.IsAlternative) return a.IsAlternative ? 1 : -1;
            if (a.IsPatternMatch != b.IsPatternMatch) return a.IsPatternMatch ? -1 : 1;
            int cmp = a.Tier.CompareTo(b.Tier);
            if (cmp != 0) return cmp;
            string labelA = a.Slots.OrderBy(s => s.Day).FirstOrDefault()?.RoomLabel ?? "";
            string labelB = b.Slots.OrderBy(s => s.Day).FirstOrDefault()?.RoomLabel ?? "";
            cmp = string.Compare(labelA, labelB, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
            return a.Slots.Min(s => s.StartMinutes).CompareTo(b.Slots.Min(s => s.StartMinutes));
        });

        return solutions;
    }

    /// <summary>
    /// Converts <see cref="MeetingSpec"/>s with resolved days into <see cref="TemplateDaySpec"/>s
    /// for the tier scanners. Returns null if any spec has no legal start times.
    /// </summary>
    /// <param name="openStarts">When true, ignores the spec's fixed start time and gathers all legal starts (for alternative suggestions).</param>
    private static List<TemplateDaySpec>? ResolveToTemplateDaySpecs(
        IReadOnlyList<MeetingSpec> specs,
        int[] resolvedDays,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool openStarts = false)
    {
        var result = new List<TemplateDaySpec>(specs.Count);
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            int day = resolvedDays[i];
            double blockHours = spec.DurationMinutes / 60.0;

            IReadOnlyList<int> legalStarts;
            if (!openStarts && spec.StartMinutes.HasValue)
            {
                legalStarts = new[] { spec.StartMinutes.Value };
            }
            else
            {
                legalStarts = GatherLegalStarts(spec.DurationMinutes, legalStartTimes);
                if (legalStarts.Count == 0) return null;
            }

            result.Add(new TemplateDaySpec(day, blockHours, spec.DurationMinutes, legalStarts));
        }
        return result;
    }

    /// <summary>
    /// Gathers legal start times for a given duration. When the duration exactly matches a
    /// configured block length, returns the start times for that block length. Otherwise
    /// falls back to all start times where the meeting fits before 22:00.
    /// </summary>
    private static IReadOnlyList<int> GatherLegalStarts(
        int durationMinutes,
        IReadOnlyList<LegalStartTime> legalStartTimes)
    {
        double durationHours = durationMinutes / 60.0;
        var matched = legalStartTimes
            .Where(l => Math.Abs(l.BlockLength - durationHours) < 0.001)
            .SelectMany(l => l.StartTimes)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        if (matched.Count > 0) return matched;

        // Custom duration: use all start times where the meeting fits before 22:00.
        const int maxEnd = 22 * 60;
        return legalStartTimes
            .SelectMany(l => l.StartTimes)
            .Distinct()
            .Where(t => t + durationMinutes <= maxEnd)
            .OrderBy(t => t)
            .ToList();
    }

    /// <summary>
    /// Enumerates candidate day assignments for open-day specs, with institutional
    /// pattern matches listed first.
    /// </summary>
    private static List<(List<int> Days, bool IsPattern)> EnumerateDayAssignments(
        HashSet<int> fixedDays,
        int openCount,
        IReadOnlyList<BlockPattern> patterns)
    {
        var results = new List<(List<int>, bool)>();
        var seenSets = new HashSet<string>();

        var availableDays = new[] { 1, 2, 3, 4, 5 }.Where(d => !fixedDays.Contains(d)).ToList();

        // 1. Pattern completion: try to complete an institutional pattern that already includes the fixed days.
        foreach (var pattern in patterns)
        {
            var patternSet = new HashSet<int>(pattern.Days);
            if (!fixedDays.All(d => patternSet.Contains(d))) continue;
            var remaining = patternSet.Except(fixedDays).OrderBy(d => d).ToList();
            if (remaining.Count != openCount) continue;

            string key = string.Join(",", remaining);
            if (seenSets.Add(key))
                results.Add((remaining, true));
        }

        // 2. Full pattern match: when ALL specs are open-day, try each pattern whose day count matches.
        if (fixedDays.Count == 0)
        {
            foreach (var pattern in patterns)
            {
                if (pattern.Days.Count != openCount) continue;
                var combo = pattern.Days.OrderBy(d => d).ToList();
                string key = string.Join(",", combo);
                if (seenSets.Add(key))
                    results.Add((combo, true));
            }
        }

        // 3. Combinatorial fallback: remaining distinct day combos from the available pool.
        int cap = 50 - results.Count;
        if (cap > 0)
        {
            foreach (var combo in Combinations(availableDays, openCount))
            {
                string key = string.Join(",", combo);
                if (seenSets.Add(key))
                {
                    results.Add((combo, false));
                    if (--cap <= 0) break;
                }
            }
        }

        return results;
    }

    /// <summary>Yields all <paramref name="count"/>-element subsets of <paramref name="source"/> in lexicographic order.</summary>
    private static IEnumerable<List<int>> Combinations(List<int> source, int count)
    {
        if (count == 0) { yield return new List<int>(); yield break; }
        if (count > source.Count) yield break;

        for (int i = 0; i <= source.Count - count; i++)
        {
            foreach (var rest in Combinations(source.GetRange(i + 1, source.Count - i - 1), count - 1))
            {
                rest.Insert(0, source[i]);
                yield return rest;
            }
        }
    }

    // ── Tier scanning methods ────────────────────────────────────────────────

    private static void ScanTier1(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen,
        bool isPatternMatch = false,
        bool isAlternative = false)
    {
        // Same start time on the same day = guaranteed overlap.
        if (FindSameDayGroups(gapSpecs).Count > 0) return;

        // Find start times that are legal on ALL gap days.
        var commonStarts = gapSpecs[0].LegalStartTimes.AsEnumerable();
        for (int i = 1; i < gapSpecs.Count; i++)
            commonStarts = commonStarts.Intersect(gapSpecs[i].LegalStartTimes);
        var commonStartList = commonStarts.OrderBy(t => t).ToList();

        foreach (var room in rooms)
        {
            string label = RoomLabel(room);
            foreach (int startTime in commonStartList)
            {
                bool allFree = gapSpecs.All(spec =>
                    index.IsAvailable(room.Id, spec.Day, startTime, spec.DurationMinutes));

                if (!allFree) continue;

                var newSlots = gapSpecs
                    .Select(spec => new SolutionSlot(spec.Day, startTime, spec.DurationMinutes, room.Id, label))
                    .ToList();

                TryAddSolution(newSlots, fixedSlots, solutions, seen, isPatternMatch, isAlternative);
            }
        }
    }

    private static void ScanTier2a(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen,
        bool isPatternMatch = false,
        bool isAlternative = false)
    {
        var sameDayGroups = FindSameDayGroups(gapSpecs);
        bool hasSameDaySpecs = sameDayGroups.Count > 0;
        var orderings = BuildSpecOrderings(gapSpecs, sameDayGroups);

        foreach (var room in rooms)
        {
            string label = RoomLabel(room);

            foreach (var order in orderings)
            {
                var daySlots = new List<SolutionSlot>();
                bool allDaysCovered = true;

                foreach (int idx in order)
                {
                    var spec = gapSpecs[idx];

                    // Find the earliest legal start where the room is free
                    // AND doesn't overlap any same-day slot already assigned.
                    bool found = false;
                    int earliest = 0;
                    foreach (int t in spec.LegalStartTimes.OrderBy(t => t))
                    {
                        if (index.IsAvailable(room.Id, spec.Day, t, spec.DurationMinutes)
                            && (!hasSameDaySpecs || IsNonOverlapping(spec.Day, t, spec.DurationMinutes, daySlots)))
                        {
                            earliest = t;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        allDaysCovered = false;
                        break;
                    }

                    daySlots.Add(new SolutionSlot(spec.Day, earliest, spec.DurationMinutes, room.Id, label));
                }

                if (allDaysCovered && daySlots.Count == gapSpecs.Count)
                    TryAddSolution(daySlots, fixedSlots, solutions, seen, isPatternMatch, isAlternative);
            }
        }
    }

    private static void ScanTier2b(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen,
        bool isPatternMatch = false,
        IReadOnlyList<Room>[]? perSpecRooms = null,
        bool isAlternative = false)
    {
        // Same start time on the same day = guaranteed overlap.
        if (FindSameDayGroups(gapSpecs).Count > 0) return;

        // Find start times legal on ALL gap days.
        var commonStarts = gapSpecs[0].LegalStartTimes.AsEnumerable();
        for (int i = 1; i < gapSpecs.Count; i++)
            commonStarts = commonStarts.Intersect(gapSpecs[i].LegalStartTimes);

        foreach (int startTime in commonStarts.OrderBy(t => t))
        {
            var daySlots = new List<SolutionSlot>();
            bool allDaysCovered = true;

            for (int i = 0; i < gapSpecs.Count; i++)
            {
                var spec = gapSpecs[i];
                var pool = perSpecRooms?[i] ?? rooms;
                var room = pool.FirstOrDefault(r =>
                    index.IsAvailable(r.Id, spec.Day, startTime, spec.DurationMinutes));

                if (room == null)
                {
                    allDaysCovered = false;
                    break;
                }

                daySlots.Add(new SolutionSlot(spec.Day, startTime, spec.DurationMinutes, room.Id, RoomLabel(room)));
            }

            if (allDaysCovered && daySlots.Count == gapSpecs.Count)
                TryAddSolution(daySlots, fixedSlots, solutions, seen, isPatternMatch, isAlternative);
        }
    }

    private static void ScanTier3(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen,
        bool isPatternMatch = false,
        IReadOnlyList<Room>[]? perSpecRooms = null,
        bool isAlternative = false)
    {
        var sameDayGroups = FindSameDayGroups(gapSpecs);
        bool hasSameDaySpecs = sameDayGroups.Count > 0;
        var specOrderings = BuildSpecOrderings(gapSpecs, sameDayGroups);

        // Generate representative Tier 3 solutions using different room orderings
        // and (when same-day specs exist) different spec orderings.
        foreach (var orderedRooms in BuildOrderings(rooms))
        {
            // When per-spec rooms are in play, pre-compute per-spec ordered pools.
            var perSpecOrdered = perSpecRooms?.Select(pool =>
                ReferenceEquals(pool, rooms) ? orderedRooms : ApplyOrdering(pool, orderedRooms))
                .ToArray();

            foreach (var specOrder in specOrderings)
            {
                var daySlots = new List<SolutionSlot>();
                bool allDaysCovered = true;

                foreach (int idx in specOrder)
                {
                    var spec = gapSpecs[idx];
                    var pool = perSpecOrdered?[idx] ?? orderedRooms;

                    SolutionSlot? best = null;
                    foreach (int startTime in spec.LegalStartTimes.OrderBy(t => t))
                    {
                        var room = pool.FirstOrDefault(r =>
                            index.IsAvailable(r.Id, spec.Day, startTime, spec.DurationMinutes)
                            && (!hasSameDaySpecs || IsNonOverlapping(spec.Day, startTime, spec.DurationMinutes, daySlots)));
                        if (room != null)
                        {
                            best = new SolutionSlot(spec.Day, startTime, spec.DurationMinutes, room.Id, RoomLabel(room));
                            break;
                        }
                    }

                    if (best == null)
                    {
                        allDaysCovered = false;
                        break;
                    }
                    daySlots.Add(best);
                }

                if (allDaysCovered && daySlots.Count == gapSpecs.Count)
                    TryAddSolution(daySlots, fixedSlots, solutions, seen, isPatternMatch, isAlternative);
            }
        }
    }

    /// <summary>Returns three ordering strategies for room preference in Tier 3.</summary>
    private static List<IReadOnlyList<Room>> BuildOrderings(IReadOnlyList<Room> rooms) => new()
    {
        rooms.OrderBy(r => r.Building).ThenBy(r => r.RoomNumber).ToList(),
        rooms.OrderByDescending(r => r.Capacity).ThenBy(r => r.Building).ToList(),
        rooms.OrderBy(r => r.Capacity).ThenBy(r => r.Building).ToList()
    };

    /// <summary>
    /// Re-orders a per-spec room pool to match the ordering of <paramref name="reference"/>,
    /// preserving only rooms present in <paramref name="pool"/>.
    /// </summary>
    private static IReadOnlyList<Room> ApplyOrdering(IReadOnlyList<Room> pool, IReadOnlyList<Room> reference)
    {
        var ids = new HashSet<string>(pool.Select(r => r.Id));
        return reference.Where(r => ids.Contains(r.Id)).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a solution from new slots + fixed slots, classifies its tier,
    /// and adds it to the list if not a duplicate.
    /// </summary>
    private static void TryAddSolution(
        List<SolutionSlot> newSlots,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen,
        bool isPatternMatch = false,
        bool isAlternative = false)
    {
        var allSlots = fixedSlots.Concat(newSlots).OrderBy(s => s.Day).ToList();
        string key = SolutionKey(allSlots);
        if (!seen.Add(key)) return;

        var tier = RoomSolution.Classify(allSlots);
        solutions.Add(new RoomSolution(allSlots, tier, isPatternMatch, isAlternative));
    }

    /// <summary>
    /// Builds a de-duplication key from the sorted slot set.
    /// </summary>
    private static string SolutionKey(IEnumerable<SolutionSlot> slots) =>
        string.Join("|", slots.OrderBy(s => s.Day).Select(s => $"{s.Day}:{s.RoomId}:{s.StartMinutes}"));

    /// <summary>
    /// Formats a room's display label as "Building RoomNumber".
    /// </summary>
    internal static string RoomLabel(Room room) =>
        string.IsNullOrEmpty(room.Building)
            ? room.RoomNumber
            : $"{room.Building} {room.RoomNumber}";

    // ── Same-day helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns day → spec-index-list for days that have two or more specs.
    /// An empty dictionary means no day is shared — callers can skip overlap logic.
    /// </summary>
    private static Dictionary<int, List<int>> FindSameDayGroups(List<TemplateDaySpec> specs)
    {
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < specs.Count; i++)
        {
            int day = specs[i].Day;
            if (!groups.TryGetValue(day, out var list))
            {
                list = new List<int>();
                groups[day] = list;
            }
            list.Add(i);
        }

        // Keep only days with 2+ specs.
        var sameDays = new Dictionary<int, List<int>>();
        foreach (var kv in groups)
            if (kv.Value.Count > 1)
                sameDays[kv.Key] = kv.Value;
        return sameDays;
    }

    /// <summary>
    /// Returns true if [startMinutes, startMinutes + durationMinutes) does NOT overlap
    /// any slot on the same <paramref name="day"/> already in <paramref name="assignedSlots"/>.
    /// </summary>
    private static bool IsNonOverlapping(
        int day, int startMinutes, int durationMinutes,
        List<SolutionSlot> assignedSlots)
    {
        int end = startMinutes + durationMinutes;
        foreach (var slot in assignedSlots)
        {
            if (slot.Day != day) continue;
            int slotEnd = slot.StartMinutes + slot.DurationMinutes;
            if (startMinutes < slotEnd && slot.StartMinutes < end)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Produces spec-index orderings for the tier scanners. When no same-day groups
    /// exist, returns the single natural ordering [0, 1, …]. When same-day groups exist,
    /// adds a second ordering where same-day specs are sorted longest-first (they are
    /// harder to place and benefit from priority in greedy search).
    /// </summary>
    private static List<int[]> BuildSpecOrderings(
        List<TemplateDaySpec> specs,
        Dictionary<int, List<int>> sameDayGroups)
    {
        int n = specs.Count;
        var natural = Enumerable.Range(0, n).ToArray();
        if (sameDayGroups.Count == 0)
            return new List<int[]> { natural };

        // Build an alternative ordering: same-day specs sorted by descending duration.
        var alt = (int[])natural.Clone();
        foreach (var kv in sameDayGroups)
        {
            var indices = kv.Value;
            var sorted = indices
                .OrderByDescending(i => specs[i].DurationMinutes)
                .ToList();
            for (int j = 0; j < indices.Count; j++)
                alt[indices[j]] = sorted[j];
        }

        // The alt array has been index-swapped: alt[originalPos] = newSpecIndex.
        // We need a permutation of spec indices, not a remapping.
        // Rebuild: for each position, which spec index goes there?
        var altOrder = new int[n];
        // Positions that aren't in any same-day group keep their natural index.
        var inGroup = new HashSet<int>(sameDayGroups.Values.SelectMany(v => v));
        int pos = 0;
        // Walk natural order, inserting same-day group members in duration-descending order
        // while keeping non-group members in place.
        var groupSorted = new Dictionary<int, Queue<int>>();
        foreach (var kv in sameDayGroups)
            groupSorted[kv.Key] = new Queue<int>(
                kv.Value.OrderByDescending(i => specs[i].DurationMinutes));

        for (int i = 0; i < n; i++)
        {
            int day = specs[i].Day;
            if (groupSorted.TryGetValue(day, out var q) && q.Count > 0)
                altOrder[i] = q.Dequeue();
            else
                altOrder[i] = i;
        }

        // Only add if different from natural.
        if (!natural.SequenceEqual(altOrder))
            return new List<int[]> { natural, altOrder };
        return new List<int[]> { natural };
    }
}

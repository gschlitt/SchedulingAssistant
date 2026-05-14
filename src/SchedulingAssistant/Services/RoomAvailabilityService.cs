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
    /// excluding the section currently being edited.
    /// </summary>
    /// <param name="sections">All sections in the target semester.</param>
    /// <param name="meetings">All meetings in the target semester.</param>
    /// <param name="excludeSectionId">The section being edited (excluded from bookings).</param>
    public void Build(IEnumerable<Section> sections, IEnumerable<Meeting> meetings, string? excludeSectionId)
    {
        _bookings.Clear();

        foreach (var section in sections)
        {
            if (section.Id == excludeSectionId) continue;
            AddSchedule(section.Schedule);
        }

        foreach (var meeting in meetings)
            AddSchedule(meeting.Schedule);
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
    /// specified semester, excluding the section being edited.
    /// </summary>
    public OccupancyIndex BuildOccupancyIndex(
        IEnumerable<Section> sections,
        IEnumerable<Meeting> meetings,
        string? excludeSectionId)
    {
        var index = new OccupancyIndex();
        index.Build(sections, meetings, excludeSectionId);
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

    // ── Tier scanning methods ────────────────────────────────────────────────

    private static void ScanTier1(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen)
    {
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

                TryAddSolution(newSlots, fixedSlots, solutions, seen);
            }
        }
    }

    private static void ScanTier2a(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen)
    {
        foreach (var room in rooms)
        {
            string label = RoomLabel(room);
            var daySlots = new List<SolutionSlot>();
            bool allDaysCovered = true;

            foreach (var spec in gapSpecs)
            {
                int? earliest = spec.LegalStartTimes
                    .OrderBy(t => t)
                    .FirstOrDefault(t => index.IsAvailable(room.Id, spec.Day, t, spec.DurationMinutes));

                // FirstOrDefault returns 0 for int, so check if the value is actually legal.
                if (earliest == default && !spec.LegalStartTimes.Contains(0))
                {
                    allDaysCovered = false;
                    break;
                }

                if (!index.IsAvailable(room.Id, spec.Day, earliest!.Value, spec.DurationMinutes))
                {
                    allDaysCovered = false;
                    break;
                }

                daySlots.Add(new SolutionSlot(spec.Day, earliest.Value, spec.DurationMinutes, room.Id, label));
            }

            if (allDaysCovered && daySlots.Count == gapSpecs.Count)
                TryAddSolution(daySlots, fixedSlots, solutions, seen);
        }
    }

    private static void ScanTier2b(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen)
    {
        // Find start times legal on ALL gap days.
        var commonStarts = gapSpecs[0].LegalStartTimes.AsEnumerable();
        for (int i = 1; i < gapSpecs.Count; i++)
            commonStarts = commonStarts.Intersect(gapSpecs[i].LegalStartTimes);

        foreach (int startTime in commonStarts.OrderBy(t => t))
        {
            var daySlots = new List<SolutionSlot>();
            bool allDaysCovered = true;

            foreach (var spec in gapSpecs)
            {
                var room = rooms.FirstOrDefault(r =>
                    index.IsAvailable(r.Id, spec.Day, startTime, spec.DurationMinutes));

                if (room == null)
                {
                    allDaysCovered = false;
                    break;
                }

                daySlots.Add(new SolutionSlot(spec.Day, startTime, spec.DurationMinutes, room.Id, RoomLabel(room)));
            }

            if (allDaysCovered && daySlots.Count == gapSpecs.Count)
                TryAddSolution(daySlots, fixedSlots, solutions, seen);
        }
    }

    private static void ScanTier3(
        List<TemplateDaySpec> gapSpecs,
        IReadOnlyList<Room> rooms,
        OccupancyIndex index,
        List<SolutionSlot> fixedSlots,
        List<RoomSolution> solutions,
        HashSet<string> seen)
    {
        // Generate a small number of representative Tier 3 solutions using
        // different room orderings. Each ordering produces at most one solution.
        var orderings = new List<IReadOnlyList<Room>>
        {
            rooms.OrderBy(r => r.Building).ThenBy(r => r.RoomNumber).ToList(),
            rooms.OrderByDescending(r => r.Capacity).ThenBy(r => r.Building).ToList(),
            rooms.OrderBy(r => r.Capacity).ThenBy(r => r.Building).ToList()
        };

        foreach (var orderedRooms in orderings)
        {
            var daySlots = new List<SolutionSlot>();
            bool allDaysCovered = true;

            foreach (var spec in gapSpecs)
            {
                SolutionSlot? best = null;
                foreach (int startTime in spec.LegalStartTimes.OrderBy(t => t))
                {
                    var room = orderedRooms.FirstOrDefault(r =>
                        index.IsAvailable(r.Id, spec.Day, startTime, spec.DurationMinutes));
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
                TryAddSolution(daySlots, fixedSlots, solutions, seen);
        }
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
        HashSet<string> seen)
    {
        var allSlots = fixedSlots.Concat(newSlots).OrderBy(s => s.Day).ToList();
        string key = SolutionKey(allSlots);
        if (!seen.Add(key)) return;

        var tier = RoomSolution.Classify(allSlots);
        solutions.Add(new RoomSolution(allSlots, tier));
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
}

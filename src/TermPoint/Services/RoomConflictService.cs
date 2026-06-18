using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Detects room-scheduling conflicts among sections within a semester.
/// A conflict exists when two sections share the same room and day with
/// overlapping time ranges and overlapping weekly frequencies.
/// </summary>
public static class RoomConflictService
{
    /// <summary>
    /// One side of a detected room conflict, identifying the conflicting section
    /// and the day/time details.
    /// </summary>
    private readonly record struct ScheduleSlot(
        string SectionId,
        string CourseLabel,
        int StartMinutes,
        int EndMinutes,
        string? Frequency);

    private static readonly string[] DayNames = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>
    /// Scans all sections for room-time overlaps within each (room, day) bucket.
    /// Returns a dictionary keyed by section ID; each value is the list of
    /// human-readable conflict descriptions for that section.
    /// Sections with no conflicts are omitted from the result.
    /// </summary>
    /// <param name="sections">All sections in a single semester.</param>
    /// <param name="roomNameById">Room display label keyed by room ID (e.g. "A 101").</param>
    /// <param name="courseCodeById">Calendar code keyed by course ID (e.g. "CHEM101").</param>
    /// <returns>Section ID → list of conflict description strings.</returns>
    public static Dictionary<string, List<string>> DetectConflicts(
        IReadOnlyList<Section> sections,
        Dictionary<string, string> roomNameById,
        Dictionary<string, string> courseCodeById)
    {
        // Group every schedule entry by (RoomId, Day).
        // Skip entries with no room (remote or unassigned).
        var buckets = new Dictionary<(string RoomId, int Day), List<ScheduleSlot>>();

        foreach (var section in sections)
        {
            var courseLabel = section.CourseId is not null
                             && courseCodeById.TryGetValue(section.CourseId, out var cc)
                ? $"{cc} {section.SectionCode}"
                : section.SectionCode;

            foreach (var sched in section.Schedule)
            {
                if (sched.RoomId is null) continue;

                var key = (sched.RoomId, sched.Day);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<ScheduleSlot>();
                    buckets[key] = list;
                }
                list.Add(new ScheduleSlot(
                    section.Id,
                    courseLabel,
                    sched.StartMinutes,
                    sched.EndMinutes,
                    sched.Frequency));
            }
        }

        var result = new Dictionary<string, List<string>>();

        foreach (var ((roomId, day), slots) in buckets)
        {
            if (slots.Count < 2) continue;

            var roomLabel = roomNameById.TryGetValue(roomId, out var rn) ? rn : roomId;
            var dayLabel = day >= 1 && day <= 6 ? DayNames[day] : $"Day {day}";

            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    var a = slots[i];
                    var b = slots[j];

                    if (a.SectionId == b.SectionId) continue;

                    // Half-open interval overlap test
                    if (a.StartMinutes >= b.EndMinutes || b.StartMinutes >= a.EndMinutes)
                        continue;

                    // Frequency check — odd-vs-even, etc.
                    if (!SectionDaySchedule.FrequenciesOverlap(a.Frequency, b.Frequency))
                        continue;

                    // Conflict found — record for both sides
                    AddConflict(result, a.SectionId, roomLabel, dayLabel, a, b);
                    AddConflict(result, b.SectionId, roomLabel, dayLabel, b, a);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a formatted conflict description to the result dictionary for one section.
    /// Format: "Room conflict: A101 Mon 0800–0850 with CHEM101 B"
    /// </summary>
    private static void AddConflict(
        Dictionary<string, List<string>> result,
        string sectionId,
        string roomLabel,
        string dayLabel,
        ScheduleSlot self,
        ScheduleSlot other)
    {
        if (!result.TryGetValue(sectionId, out var list))
        {
            list = new List<string>();
            result[sectionId] = list;
        }

        var time = $"{FormatMinutes(self.StartMinutes)}–{FormatMinutes(self.EndMinutes)}";
        list.Add($"Room conflict: {roomLabel} {dayLabel} {time} with {other.CourseLabel}");
    }

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}

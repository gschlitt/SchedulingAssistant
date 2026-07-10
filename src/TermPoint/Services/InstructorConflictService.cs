using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Detects instructor-scheduling conflicts among sections within a semester.
/// A conflict exists when two sections share the same instructor and day with
/// overlapping time ranges and overlapping weekly frequencies.
/// </summary>
public static class InstructorConflictService
{
    /// <summary>
    /// One side of a detected instructor conflict, identifying the conflicting section
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
    /// Scans all sections for instructor-time overlaps within each (instructor, day) bucket.
    /// Returns a dictionary keyed by section ID; each value is the list of
    /// human-readable conflict descriptions for that section.
    /// Sections with no conflicts are omitted from the result.
    /// </summary>
    /// <param name="sections">All sections in a single semester.</param>
    /// <param name="instructorLabelById">Instructor display label keyed by ID (e.g. "J. Smith").</param>
    /// <param name="courseCodeById">Calendar code keyed by course ID (e.g. "CHEM101").</param>
    /// <returns>Section ID → list of conflict description strings.</returns>
    public static Dictionary<string, List<string>> DetectConflicts(
        IReadOnlyList<Section> sections,
        Dictionary<string, string> instructorLabelById,
        Dictionary<string, string> courseCodeById)
    {
        var buckets = new Dictionary<(string InstructorId, int Day), List<ScheduleSlot>>();

        foreach (var section in sections)
        {
            var courseLabel = section.CourseId is not null
                             && courseCodeById.TryGetValue(section.CourseId, out var cc)
                ? $"{cc} {section.SectionCode}"
                : section.SectionCode;

            foreach (var assignment in section.InstructorAssignments)
            {
                foreach (var sched in section.Schedule)
                {
                    var key = (assignment.InstructorId, sched.Day);
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
        }

        var result = new Dictionary<string, List<string>>();

        foreach (var ((instructorId, day), slots) in buckets)
        {
            if (slots.Count < 2) continue;

            var instrLabel = instructorLabelById.TryGetValue(instructorId, out var name)
                ? name : instructorId;
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
                    AddConflict(result, a.SectionId, instrLabel, dayLabel, a, b);
                    AddConflict(result, b.SectionId, instrLabel, dayLabel, b, a);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Scans all sections for instructor-time overlaps and returns results keyed by
    /// instructor ID rather than section ID. Used by the workload panel to flag
    /// instructors who have conflicts.
    /// </summary>
    /// <param name="sections">All sections in a single semester.</param>
    /// <param name="courseCodeById">Calendar code keyed by course ID (e.g. "CHEM101").</param>
    /// <returns>Instructor ID → list of conflict description strings.</returns>
    public static Dictionary<string, List<string>> DetectConflictsByInstructor(
        IReadOnlyList<Section> sections,
        Dictionary<string, string> courseCodeById)
    {
        var buckets = new Dictionary<(string InstructorId, int Day), List<ScheduleSlot>>();

        foreach (var section in sections)
        {
            var courseLabel = section.CourseId is not null
                             && courseCodeById.TryGetValue(section.CourseId, out var cc)
                ? $"{cc} {section.SectionCode}"
                : section.SectionCode;

            foreach (var assignment in section.InstructorAssignments)
            {
                foreach (var sched in section.Schedule)
                {
                    var key = (assignment.InstructorId, sched.Day);
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
        }

        var result = new Dictionary<string, List<string>>();

        foreach (var ((instructorId, day), slots) in buckets)
        {
            if (slots.Count < 2) continue;

            var dayLabel = day >= 1 && day <= 6 ? DayNames[day] : $"Day {day}";

            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    var a = slots[i];
                    var b = slots[j];

                    if (a.SectionId == b.SectionId) continue;

                    if (a.StartMinutes >= b.EndMinutes || b.StartMinutes >= a.EndMinutes)
                        continue;

                    if (!SectionDaySchedule.FrequenciesOverlap(a.Frequency, b.Frequency))
                        continue;

                    AddInstructorConflict(result, instructorId, dayLabel, a, b);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a formatted conflict description to the result dictionary for one section.
    /// Format: "Instructor conflict: J. Smith Mon 0800–0850 with CHEM101 B"
    /// </summary>
    private static void AddConflict(
        Dictionary<string, List<string>> result,
        string sectionId,
        string instrLabel,
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
        list.Add($"Instructor conflict: {instrLabel} {dayLabel} {time} with {other.CourseLabel}");
    }

    /// <summary>
    /// Adds a formatted conflict description to the result dictionary keyed by instructor ID.
    /// Format: "Mon 0800–0850: CHEM101 A vs BIOL200 B"
    /// </summary>
    private static void AddInstructorConflict(
        Dictionary<string, List<string>> result,
        string instructorId,
        string dayLabel,
        ScheduleSlot self,
        ScheduleSlot other)
    {
        if (!result.TryGetValue(instructorId, out var list))
        {
            list = new List<string>();
            result[instructorId] = list;
        }

        var desc = $"{dayLabel} {FormatMinutes(self.StartMinutes)}–{FormatMinutes(self.EndMinutes)}: " +
                   $"{self.CourseLabel} vs {other.CourseLabel}";
        list.Add(desc);
    }

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}

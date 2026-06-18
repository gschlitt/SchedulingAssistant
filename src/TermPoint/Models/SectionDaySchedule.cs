namespace TermPoint.Models;

public class SectionDaySchedule
{
    /// <summary>Well-known sentinel value indicating a remote/online meeting that needs no physical room.</summary>
    public const string RemoteRoomTypeId = "__remote__";

    /// <summary>Day of week: 1=Monday, 2=Tuesday, 3=Wednesday, 4=Thursday, 5=Friday.</summary>
    public int Day { get; set; }

    /// <summary>Start time in minutes from midnight, e.g. 510 = 8:30 AM.</summary>
    public int StartMinutes { get; set; }

    /// <summary>Duration in minutes, e.g. 90 for a 1.5-hour block.</summary>
    public int DurationMinutes { get; set; }

    public int EndMinutes => StartMinutes + DurationMinutes;

    /// <summary>Optional meeting type ID referencing a SchedulingEnvironmentValue of type "meetingType".</summary>
    public string? MeetingTypeId { get; set; }

    /// <summary>Optional room ID for this specific meeting.</summary>
    public string? RoomId { get; set; }

    /// <summary>
    /// Optional room type ID referencing a SchedulingEnvironmentValue of type "roomType",
    /// or <see cref="RemoteRoomTypeId"/> for remote/online meetings.
    /// </summary>
    public string? RoomTypeId { get; set; }

    /// <summary>True when this meeting is remote/online and does not require a physical room.</summary>
    public bool IsRemote => RoomTypeId == RemoteRoomTypeId;

    /// <summary>
    /// Optional meeting frequency within the semester. Null or empty means the meeting
    /// occurs every week (the default). Otherwise one of:
    ///   "odd"  — meets on odd-numbered weeks (1, 3, 5, …)
    ///   "even" — meets on even-numbered weeks (2, 4, 6, …)
    ///   "1,6,7" — meets only on the listed week numbers (sorted, comma-separated integers)
    /// </summary>
    public string? Frequency { get; set; }

    /// <summary>
    /// Returns a parenthesised display annotation for the meeting frequency,
    /// e.g. "(odd)", "(even)", "(1,6,7)". Returns an empty string when the meeting
    /// occurs every week (Frequency is null or empty).
    /// </summary>
    /// <param name="frequency">The raw stored frequency string.</param>
    /// <returns>Formatted annotation, or empty string for weekly meetings.</returns>
    public static string FormatFrequency(string? frequency) =>
        string.IsNullOrEmpty(frequency) ? string.Empty : $"({frequency})";

    /// <summary>
    /// Returns <c>true</c> when two frequency values share at least one week in common.
    /// Null or empty means "every week", which overlaps with everything.
    /// Handles "odd", "even", and comma-separated week-number lists.
    /// </summary>
    public static bool FrequenciesOverlap(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return true;

        if (a == b) return true;

        // "odd" and "even" are disjoint by definition.
        bool aOdd = a == "odd", aEven = a == "even";
        bool bOdd = b == "odd", bEven = b == "even";
        if ((aOdd && bEven) || (aEven && bOdd)) return false;
        if ((aOdd && bOdd) || (aEven && bEven)) return true;

        var setA = ExpandToWeekSet(a);
        var setB = ExpandToWeekSet(b);
        return setA.Overlaps(setB);
    }

    /// <summary>
    /// Expands a frequency string into a concrete set of week numbers.
    /// "odd" → {1,3,5,...,17}, "even" → {2,4,6,...,18}, "1,6,7" → {1,6,7}.
    /// </summary>
    private static HashSet<int> ExpandToWeekSet(string frequency)
    {
        const int maxWeeks = 18;
        if (frequency == "odd")
            return new HashSet<int>(Enumerable.Range(1, maxWeeks).Where(w => w % 2 == 1));
        if (frequency == "even")
            return new HashSet<int>(Enumerable.Range(1, maxWeeks).Where(w => w % 2 == 0));

        var set = new HashSet<int>();
        foreach (var part in frequency.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int week))
                set.Add(week);
        }
        return set;
    }
}

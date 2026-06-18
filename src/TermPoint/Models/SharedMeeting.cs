namespace TermPoint.Models;

/// <summary>
/// One day/time slot from an imported shared schedule CSV.
/// In-memory only — never persisted to the local database.
/// </summary>
public class SharedMeeting
{
    /// <summary>Day of week: 1=Monday … 7=Sunday (matches SectionDaySchedule).</summary>
    public int Day { get; set; }

    /// <summary>Start time in minutes from midnight (e.g. 480 = 8:00 AM).</summary>
    public int StartMinutes { get; set; }

    /// <summary>Duration in minutes.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>End time in minutes from midnight.</summary>
    public int EndMinutes => StartMinutes + DurationMinutes;

    /// <summary>
    /// Meeting frequency within the semester. Null means every week.
    /// Otherwise "odd", "even", or comma-separated week numbers (e.g. "1,6,7").
    /// </summary>
    public string? Frequency { get; set; }
}

namespace SchedulingAssistant.Models;

public class LegalStartTime
{
    /// <summary>Duration in hours, e.g. 1.5, 2.0, 3.0, 4.0. Also the primary key.</summary>
    public double BlockLength { get; set; }

    /// <summary>Valid start times in minutes from midnight for this block length.</summary>
    public List<int> StartTimes { get; set; } = new();

    /// <summary>Human-readable start times for display (e.g. "8:00am, 9:30am, 11:00am").</summary>
    public string StartTimesDisplay =>
        StartTimes.Count == 0
            ? "(none)"
            : string.Join(", ", StartTimes.OrderBy(m => m).Select(FormatMinutes));

    private static string FormatMinutes(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";
}

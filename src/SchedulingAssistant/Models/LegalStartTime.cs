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

    private static string FormatMinutes(int minutes)
    {
        int h = minutes / 60, m = minutes % 60;
        var period = h < 12 ? "am" : "pm";
        var h12 = h % 12 == 0 ? 12 : h % 12;
        return m == 0 ? $"{h12}{period}" : $"{h12}:{m:D2}{period}";
    }
}

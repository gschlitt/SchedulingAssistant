namespace TermPoint.Models;

/// <summary>
/// A recurring weekly commitment (e.g. an office hour) that blocks an instructor's time in a semester.
/// Derives from <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip.
/// </summary>
public class InstructorCommitment : PersistedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InstructorId { get; set; } = string.Empty;
    public string SemesterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Day { get; set; }           // 1=Mon … 6=Sat
    public int StartMinutes { get; set; }  // minutes from midnight
    public int EndMinutes { get; set; }    // minutes from midnight

    public string DayDisplay => Day switch
    {
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        _ => "?"
    };

    public string TimeDisplay
    {
        get
        {
            var startStr = MinutesToTime(StartMinutes);
            var endStr = MinutesToTime(EndMinutes);
            return $"{startStr} – {endStr}";
        }
    }

    private static string MinutesToTime(int minutes)
    {
        var hours = minutes / 60;
        var mins = minutes % 60;
        return $"{hours:D2}:{mins:D2}";
    }
}

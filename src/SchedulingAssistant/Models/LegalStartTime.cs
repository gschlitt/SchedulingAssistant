namespace SchedulingAssistant.Models;

public class LegalStartTime
{
    /// <summary>Duration in hours, e.g. 1.5, 2.0, 3.0, 4.0. Also the primary key.</summary>
    public double BlockLength { get; set; }

    /// <summary>Valid start times in minutes from midnight for this block length.</summary>
    public List<int> StartTimes { get; set; } = new();
}

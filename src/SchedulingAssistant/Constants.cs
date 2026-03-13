namespace SchedulingAssistant;

/// <summary>Application-wide constants for domain rules shared across the codebase.</summary>
public static class Constants
{
    /// <summary>
    /// Number of teaching weeks in a standard semester.
    /// Used to validate custom meeting-frequency week numbers (must be in range 1..NumWeeks).
    /// </summary>
    public const int NumWeeks = 13;
}

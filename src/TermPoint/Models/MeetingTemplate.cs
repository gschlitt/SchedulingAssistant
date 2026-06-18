namespace TermPoint.Models;

/// <summary>
/// One day's specification within a meeting template: the block length, duration,
/// and legal start times for that specific day. Different days in a template may
/// have different durations (e.g. MW=90min, F=50min).
/// </summary>
/// <param name="Day">Day of week (1=Monday … 5=Friday).</param>
/// <param name="BlockLengthHours">Duration in hours matching a <see cref="LegalStartTime.BlockLength"/> entry.</param>
/// <param name="DurationMinutes">Duration in minutes (BlockLengthHours × 60).</param>
/// <param name="LegalStartTimes">Valid start times (minutes from midnight) for this block length.</param>
public record TemplateDaySpec(
    int Day,
    double BlockLengthHours,
    int DurationMinutes,
    IReadOnlyList<int> LegalStartTimes,
    string? Frequency = null);

/// <summary>
/// A browsable meeting pattern combining a day pattern with block lengths per day.
/// Auto-generated from the cross product of <see cref="BlockPattern"/> and
/// <see cref="LegalStartTime"/>, then optionally edited by the user to assign
/// different durations to individual days.
/// </summary>
/// <param name="PatternId">FK to <see cref="BlockPattern.Id"/>.</param>
/// <param name="PatternName">User-facing label, e.g. "MWF".</param>
/// <param name="DaySpecs">One spec per day in the pattern, each with its own duration and legal starts.</param>
public record MeetingTemplate(
    string PatternId,
    string PatternName,
    IReadOnlyList<TemplateDaySpec> DaySpecs)
{
    /// <summary>
    /// Display label for the template dropdown. When all days share the same duration,
    /// shows e.g. "MWF 50min". When durations differ, shows e.g. "MWF (MW 90min, F 50min)".
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            if (DaySpecs.Count == 0) return PatternName;

            var groups = DaySpecs.GroupBy(d => d.DurationMinutes).ToList();
            if (groups.Count == 1)
                return $"{PatternName} {groups[0].Key}min";

            var parts = groups
                .OrderByDescending(g => g.Count())
                .Select(g => $"{string.Join("", g.Select(d => DayAbbrev(d.Day)))}{g.Key}min");
            return $"{PatternName} ({string.Join(", ", parts)})";
        }
    }

    private static string DayAbbrev(int day) => day switch
    {
        1 => "M",
        2 => "T",
        3 => "W",
        4 => "R",
        5 => "F",
        6 => "S",
        _ => "?"
    };
}

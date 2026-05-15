namespace SchedulingAssistant.Models;

/// <summary>
/// Ranking tiers for room availability solutions, from most to least coherent.
/// </summary>
public enum SolutionTier
{
    /// <summary>All days use the same room and the same start time.</summary>
    SameRoomSameTime = 1,

    /// <summary>All days use the same start time but rooms differ across days.</summary>
    SameTimeDiffRooms = 2,

    /// <summary>All days use the same room but start times differ across days.</summary>
    SameRoomDiffTimes = 3,

    /// <summary>Neither rooms nor start times are consistent across days.</summary>
    Mixed = 4
}

/// <summary>
/// One day-slot assignment within a room availability solution.
/// </summary>
/// <param name="Day">Day of week (1=Monday … 5=Friday).</param>
/// <param name="StartMinutes">Start time in minutes from midnight.</param>
/// <param name="DurationMinutes">Meeting duration in minutes.</param>
/// <param name="RoomId">Assigned room ID.</param>
/// <param name="RoomLabel">Display label, e.g. "A 101".</param>
public record SolutionSlot(
    int Day,
    int StartMinutes,
    int DurationMinutes,
    string RoomId,
    string RoomLabel);

/// <summary>
/// A complete room availability solution: one <see cref="SolutionSlot"/> per required day.
/// Solutions are ranked by <see cref="Tier"/> and sorted within each tier by room label
/// then earliest start time.
/// </summary>
/// <param name="Slots">One slot per gap day, plus any existing meetings for tier classification.</param>
/// <param name="Tier">Quality tier (lower = more coherent).</param>
/// <param name="IsPatternMatch">True when the day assignment came from an institutional block pattern.</param>
public record RoomSolution(
    IReadOnlyList<SolutionSlot> Slots,
    SolutionTier Tier,
    bool IsPatternMatch = false,
    bool IsAlternative = false)
{
    /// <summary>Human-readable tier description shown in the browser panel.</summary>
    public string TierLabel => Tier switch
    {
        SolutionTier.SameRoomSameTime => "Same room, same time",
        SolutionTier.SameRoomDiffTimes => "Same room, different times",
        SolutionTier.SameTimeDiffRooms => "Same time, different rooms",
        SolutionTier.Mixed => "Mixed",
        _ => "Unknown"
    };

    /// <summary>
    /// Classifies a set of slots into the appropriate <see cref="SolutionTier"/>.
    /// </summary>
    public static SolutionTier Classify(IReadOnlyList<SolutionSlot> slots)
    {
        if (slots.Count <= 1)
            return SolutionTier.SameRoomSameTime;

        bool sameRoom = slots.All(s => s.RoomId == slots[0].RoomId);
        bool sameTime = slots.All(s => s.StartMinutes == slots[0].StartMinutes);

        return (sameRoom, sameTime) switch
        {
            (true, true) => SolutionTier.SameRoomSameTime,
            (true, false) => SolutionTier.SameRoomDiffTimes,
            (false, true) => SolutionTier.SameTimeDiffRooms,
            _ => SolutionTier.Mixed
        };
    }
}

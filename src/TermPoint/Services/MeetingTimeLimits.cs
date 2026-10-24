namespace TermPoint.Services;

/// <summary>
/// The hard bounds on when a section meeting may be scheduled. Shared by the meeting editor,
/// the legal-start-time editor, the commitment editor and the section importer so that every
/// path into the database applies the same limits.
/// </summary>
public static class MeetingTimeLimits
{
    /// <summary>Absolute earliest a meeting may start: 07:30 (450 min from midnight).</summary>
    public const int MinStartMinutes = 7 * 60 + 30;   // 450

    /// <summary>Absolute latest a meeting may end: 22:00 (1320 min from midnight).</summary>
    public const int MaxEndMinutes   = 22 * 60;        // 1320
}

using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// One meeting of a new section on one day: what the writer (step 24.5) turns into a
/// <see cref="SectionDaySchedule"/>. A file row that lists several days (<c>M,W 1000-1200</c>)
/// gives one <see cref="MeetingDraft"/> per day, all with the same times, frequency, meeting type
/// and room. Only rows whose meeting cells are all valid produce meetings.
/// </summary>
/// <param name="RowNumber">The row the meeting came from, as Excel shows it.</param>
/// <param name="Day">The day number: 1 = Monday ... 5 = Friday, 6 = Saturday, 7 = Sunday (the editor's numbering).</param>
/// <param name="StartMinutes">Begin, in minutes from midnight.</param>
/// <param name="DurationMinutes">End minus Begin, in minutes (always more than zero).</param>
/// <param name="Frequency">The stored form: null (weekly), <c>odd</c>, <c>even</c>, or sorted comma-separated week numbers.</param>
/// <param name="MeetingType">The resolved meeting type, or null when the row has none.</param>
/// <param name="Room">The resolved room, or null when the row has none.</param>
public sealed record MeetingDraft(
    int RowNumber,
    int Day,
    int StartMinutes,
    int DurationMinutes,
    string? Frequency,
    SetupRef<SchedulingEnvironmentValue>? MeetingType,
    SetupRef<Room>? Room)
{
    /// <summary>The end of the meeting, in minutes from midnight.</summary>
    public int EndMinutes => StartMinutes + DurationMinutes;
}

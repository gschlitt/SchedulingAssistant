using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// Identifies which specific meeting slot of a conflict pair overlaps, so the renderer knows exactly which
/// day-column boxes to highlight and connect.
/// </summary>
/// <param name="SectionId">The section the meeting belongs to.</param>
/// <param name="CourseId">The section's course id, or null.</param>
/// <param name="Day">Day number, 1 = Monday ... 7 = Sunday.</param>
/// <param name="StartMinutes">Start, minutes from midnight.</param>
/// <param name="EndMinutes">End, minutes from midnight (exclusive).</param>
/// <param name="Frequency">The meeting's week frequency, or null for every week.</param>
public readonly record struct GridConflictMeeting(
    string SectionId,
    string? CourseId,
    int Day,
    int StartMinutes,
    int EndMinutes,
    string? Frequency)
{
    /// <summary>Describes a meeting of a section for drawing.</summary>
    /// <param name="section">The meeting's section.</param>
    /// <param name="meeting">The meeting.</param>
    /// <returns>The drawing record.</returns>
    public static GridConflictMeeting Of(Section section, SectionDaySchedule meeting) =>
        new(section.Id, section.CourseId, meeting.Day, meeting.StartMinutes, meeting.EndMinutes, meeting.Frequency);
}

/// <summary>
/// One drawing record per overlapping meeting pair of a conflict mark: the renderer uses these to glow the two
/// entry rows and to join them with a line when both are on the grid. The hover wording comes separately
/// (<c>AccessText</c>), keyed by section and day.
/// </summary>
/// <param name="WatchId">The watch's id (the watched tag's id).</param>
/// <param name="WatchName">The watch's name (the tag's name).</param>
/// <param name="MeetingA">A meeting of the mark's first section.</param>
/// <param name="MeetingB">The overlapping meeting of the mark's second section.</param>
public readonly record struct GridConflict(
    string WatchId,
    string WatchName,
    GridConflictMeeting MeetingA,
    GridConflictMeeting MeetingB);

namespace SchedulingAssistant.Models;

public class SectionDaySchedule
{
    /// <summary>Day of week: 1=Monday, 2=Tuesday, 3=Wednesday, 4=Thursday, 5=Friday.</summary>
    public int Day { get; set; }

    /// <summary>Start time in minutes from midnight, e.g. 510 = 8:30 AM.</summary>
    public int StartMinutes { get; set; }

    /// <summary>Duration in minutes, e.g. 90 for a 1.5-hour block.</summary>
    public int DurationMinutes { get; set; }

    public int EndMinutes => StartMinutes + DurationMinutes;

    /// <summary>Optional meeting type ID referencing a SectionPropertyValue of type "meetingType".</summary>
    public string? MeetingTypeId { get; set; }

    /// <summary>Optional room ID for this specific meeting.</summary>
    public string? RoomId { get; set; }
}

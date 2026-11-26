using TermPoint.Data;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Names what a section card's Apply changed, so its undo step reads "Change instructors of HIST 101 A" or
/// "Edit HIST 101 A: instructors, meeting times" rather than a bare "Edit HIST 101 A" (Greg's hand check, 2026-11-26).
/// The names are <see cref="HistoryText"/>'s area words; the label itself is <see cref="HistoryText.EditLabel"/>.
/// </summary>
public static class SectionEditChanges
{
    /// <summary>
    /// Compares a section as stored with the section about to be written and lists the areas that differ, in the
    /// card's order. Meetings are compared index by index (the card rebuilds them in their stored order); a change in
    /// the number of meetings, or in a meeting's type, room type or frequency, counts as "meetings". The level is
    /// copied from the course, so it is not an area of its own. Lists are compared by their JSON, so a change in any
    /// persisted field (a workload, a reserve code) counts.
    /// </summary>
    /// <param name="before">The section as stored before the Apply.</param>
    /// <param name="after">The section the Apply will write.</param>
    /// <returns>The changed areas' names; empty when nothing recognisable changed.</returns>
    public static IReadOnlyList<string> Describe(Section before, Section after)
    {
        var areas = new List<string>();
        void Add(bool changed, string area) { if (changed && !areas.Contains(area)) areas.Add(area); }

        Add(before.CourseId != after.CourseId, HistoryText.AreaCourse);
        Add(before.SectionCode != after.SectionCode, HistoryText.AreaSectionCode);
        Add(before.SectionTypeId != after.SectionTypeId, HistoryText.AreaSectionType);
        Add(before.DeliveryTypeId != after.DeliveryTypeId, HistoryText.AreaDeliveryType);
        Add(before.CampusId != after.CampusId, HistoryText.AreaCampus);
        Add(!SameJson(before.InstructorAssignments, after.InstructorAssignments), HistoryText.AreaInstructors);

        if (before.Schedule.Count != after.Schedule.Count)
            Add(true, HistoryText.AreaMeetings);
        else
        {
            for (var i = 0; i < before.Schedule.Count; i++)
            {
                var (b, a) = (before.Schedule[i], after.Schedule[i]);
                Add(b.Day != a.Day || b.StartMinutes != a.StartMinutes || b.DurationMinutes != a.DurationMinutes,
                    HistoryText.AreaMeetingTimes);
                Add(b.RoomId != a.RoomId, HistoryText.AreaRoom);
                Add(b.MeetingTypeId != a.MeetingTypeId || b.RoomTypeId != a.RoomTypeId || b.Frequency != a.Frequency,
                    HistoryText.AreaMeetings);
            }
        }

        Add(before.Capacity != after.Capacity, HistoryText.AreaCapacity);
        Add(!SameJson(before.TagIds, after.TagIds), HistoryText.AreaTags);
        Add(!SameJson(before.ResourceIds, after.ResourceIds), HistoryText.AreaResources);
        Add(!SameJson(before.Reserves, after.Reserves), HistoryText.AreaReserves);
        Add(before.Flag != after.Flag, HistoryText.AreaFlag);
        Add(before.Notes != after.Notes, HistoryText.AreaNotes);
        return areas;
    }

    /// <summary>True when two values serialize to the same JSON (null and null are the same).</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    private static bool SameJson<T>(T a, T b) => JsonHelpers.Serialize(a) == JsonHelpers.Serialize(b);
}

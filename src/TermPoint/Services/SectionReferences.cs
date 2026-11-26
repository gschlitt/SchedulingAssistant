using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services;

/// <summary>
/// One reference removed from a section because the thing it pointed at was deleted.
/// </summary>
/// <param name="Table">The table the deleted thing lived in ("Rooms", "SchedulingEnvironmentValues" or "Instructors").</param>
/// <param name="Id">The deleted thing's id.</param>
/// <param name="Kind">The user-facing word for it: "room", "meeting type", "section type", "delivery type", "tag", "resource", "reserve" or "instructor".</param>
public sealed record RemovedReference(string Table, string Id, string Kind);

/// <summary>
/// The one place that knows which parts of a <see cref="Section"/> refer to a room, a scheduling-environment value or an
/// instructor, and how to take such a reference out. Shared by the deletes' cleanup (deleting a room or a tag removes it
/// from every section that uses it) and by undo (which leaves out references to anything deleted since the step), so the
/// two can never disagree about what a reference is.
///
/// <para>Campus and room type are deliberately not references this class removes: deleting either leaves sections
/// alone today, and undo keeps it that way.</para>
/// </summary>
public static class SectionReferences
{
    /// <summary>The table holding rooms.</summary>
    public const string RoomsTable = "Rooms";

    /// <summary>The table holding tags, types, resources, reserves and the other scheduling-environment values.</summary>
    public const string EnvironmentValuesTable = "SchedulingEnvironmentValues";

    /// <summary>The table holding instructors.</summary>
    public const string InstructorsTable = "Instructors";

    /// <summary>
    /// Clears a room from every meeting of the section that uses it.
    /// </summary>
    /// <param name="section">The section; changed in place.</param>
    /// <param name="roomId">The id of the room being removed.</param>
    /// <returns>True when at least one meeting changed.</returns>
    public static bool RemoveRoom(Section section, string roomId)
    {
        var changed = false;
        foreach (var meeting in section.Schedule.Where(m => m.RoomId == roomId))
        {
            meeting.RoomId = null;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Removes a deleted scheduling-environment value from the section: a tag, resource or reserve is taken out of its
    /// list; a section type or delivery type is cleared; a meeting type is cleared from every meeting that has it. Room
    /// type and staff type do nothing to sections (as before this class existed).
    /// </summary>
    /// <param name="section">The section; changed in place.</param>
    /// <param name="type">The value's type, one of <see cref="SchedulingEnvironmentTypes"/>.</param>
    /// <param name="id">The id of the value being removed.</param>
    /// <returns>True when the section changed.</returns>
    public static bool RemoveEnvironmentValue(Section section, string type, string id)
    {
        switch (type)
        {
            case SchedulingEnvironmentTypes.Tag:
                return section.TagIds.Remove(id);
            case SchedulingEnvironmentTypes.Resource:
                return section.ResourceIds.Remove(id);
            case SchedulingEnvironmentTypes.SectionType:
                if (section.SectionTypeId != id) return false;
                section.SectionTypeId = null;
                return true;
            case SchedulingEnvironmentTypes.DeliveryType:
                if (section.DeliveryTypeId != id) return false;
                section.DeliveryTypeId = null;
                return true;
            case SchedulingEnvironmentTypes.Reserve:
                return section.Reserves.RemoveAll(r => r.ReserveId == id) > 0;
            case SchedulingEnvironmentTypes.MeetingType:
                var changed = false;
                foreach (var meeting in section.Schedule.Where(m => m.MeetingTypeId == id))
                {
                    meeting.MeetingTypeId = null;
                    changed = true;
                }
                return changed;
            default:
                return false;   // room type, staff type: sections are not touched
        }
    }

    /// <summary>
    /// Removes from the section every reference whose target was deleted this session: meeting rooms, meeting types,
    /// section type, delivery type, tags, resources, reserves and instructor assignments. Campus and room type are left
    /// alone (their deletes leave sections alone today).
    /// </summary>
    /// <param name="section">The section; changed in place.</param>
    /// <param name="wasDeleted">
    /// Asks whether the thing in (table, id) was deleted this session and is still gone
    /// (<c>ISectionChangeLog.WasDeleted</c>).
    /// </param>
    /// <returns>One entry per reference removed (the same room on three meetings gives three), in a fixed order.</returns>
    public static IReadOnlyList<RemovedReference> RemoveDeleted(Section section, Func<string, string, bool> wasDeleted)
    {
        var removed = new List<RemovedReference>();

        foreach (var meeting in section.Schedule)
        {
            if (meeting.RoomId is { } roomId && wasDeleted(RoomsTable, roomId))
            {
                removed.Add(new RemovedReference(RoomsTable, roomId, "room"));
                meeting.RoomId = null;
            }
            if (meeting.MeetingTypeId is { } meetingTypeId && wasDeleted(EnvironmentValuesTable, meetingTypeId))
            {
                removed.Add(new RemovedReference(EnvironmentValuesTable, meetingTypeId, "meeting type"));
                meeting.MeetingTypeId = null;
            }
        }

        if (section.SectionTypeId is { } sectionTypeId && wasDeleted(EnvironmentValuesTable, sectionTypeId))
        {
            removed.Add(new RemovedReference(EnvironmentValuesTable, sectionTypeId, "section type"));
            section.SectionTypeId = null;
        }
        if (section.DeliveryTypeId is { } deliveryTypeId && wasDeleted(EnvironmentValuesTable, deliveryTypeId))
        {
            removed.Add(new RemovedReference(EnvironmentValuesTable, deliveryTypeId, "delivery type"));
            section.DeliveryTypeId = null;
        }

        RemoveWhere(section.TagIds, id => id, EnvironmentValuesTable, "tag", wasDeleted, removed);
        RemoveWhere(section.ResourceIds, id => id, EnvironmentValuesTable, "resource", wasDeleted, removed);
        RemoveWhere(section.Reserves, r => r.ReserveId, EnvironmentValuesTable, "reserve", wasDeleted, removed);
        RemoveWhere(section.InstructorAssignments, a => a.InstructorId, InstructorsTable, "instructor", wasDeleted, removed);
        return removed;
    }

    /// <summary>
    /// Removes the items of a list whose referenced id was deleted, noting each removal.
    /// </summary>
    /// <typeparam name="T">The list's item type.</typeparam>
    /// <param name="list">The list; changed in place.</param>
    /// <param name="idOf">Reads the referenced id from an item.</param>
    /// <param name="table">The table the ids refer to.</param>
    /// <param name="kind">The user-facing word for the kind of thing.</param>
    /// <param name="wasDeleted">See <see cref="RemoveDeleted"/>.</param>
    /// <param name="removed">Receives one entry per item removed.</param>
    private static void RemoveWhere<T>(
        List<T> list, Func<T, string> idOf, string table, string kind,
        Func<string, string, bool> wasDeleted, List<RemovedReference> removed)
    {
        var gone = list.Where(item => wasDeleted(table, idOf(item))).ToList();
        foreach (var item in gone)
            removed.Add(new RemovedReference(table, idOf(item), kind));
        list.RemoveAll(item => gone.Contains(item));
    }
}

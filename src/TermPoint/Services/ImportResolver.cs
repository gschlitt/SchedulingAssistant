using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Resolves imported shared schedule sections against local entities. Takes unresolved
/// <see cref="Section"/> objects (with <c>Imported*</c> staging properties populated by
/// the parser) and fills in entity IDs (<c>CampusId</c>, <c>SectionTypeId</c>, <c>TagIds</c>,
/// <c>InstructorAssignments</c>, <c>RoomId</c>, <c>MeetingTypeId</c>) using an
/// <see cref="ImportResolutionIndex"/> built from the importing department's local data.
/// </summary>
public class ImportResolver
{
    /// <summary>
    /// Resolves all sections in a shared schedule set against the provided index.
    /// Populates entity IDs on each section and its meetings, and returns a summary
    /// of resolution outcomes.
    /// </summary>
    /// <param name="sections">Unresolved sections from the parser (mutated in place).</param>
    /// <param name="index">Pre-built index of the importing department's local entities.</param>
    /// <returns>Summary with resolved/unresolved counts and any warnings.</returns>
    public ImportResolutionSummary Resolve(IReadOnlyList<Section> sections, ImportResolutionIndex index)
    {
        var warnings = new List<string>();

        // Track distinct names to count unique resolved/unresolved rather than occurrences
        var resolvedInstructors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedInstructors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedMeetingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedMeetingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool resolvedCampus = false;
        bool resolvedSectionType = false;

        foreach (var section in sections)
        {
            ResolveInstructors(section, index, warnings, resolvedInstructors, unresolvedInstructors);
            ResolveCampus(section, index, ref resolvedCampus);
            ResolveSectionType(section, index, ref resolvedSectionType);
            ResolveTags(section, index, resolvedTags, unresolvedTags);

            foreach (var meeting in section.Schedule)
            {
                ResolveRoom(meeting, index, warnings, resolvedRooms, unresolvedRooms);
                ResolveMeetingType(meeting, index, resolvedMeetingTypes, unresolvedMeetingTypes);
            }
        }

        return new ImportResolutionSummary(
            resolvedInstructors.Count,
            unresolvedInstructors.Count,
            resolvedRooms.Count,
            unresolvedRooms.Count,
            resolvedTags.Count,
            unresolvedTags.Count,
            resolvedCampus,
            resolvedSectionType,
            resolvedMeetingTypes.Count,
            unresolvedMeetingTypes.Count,
            warnings);
    }

    /// <summary>
    /// Resolves instructor display names to local instructor IDs. Parses each
    /// "LastName, FirstName" entry from <see cref="Section.DisplayInstructors"/>,
    /// resolves against the index, and populates <see cref="SchedulableBase.InstructorAssignments"/>.
    /// </summary>
    private static void ResolveInstructors(
        Section section,
        ImportResolutionIndex index,
        List<string> warnings,
        HashSet<string> resolved,
        HashSet<string> unresolved)
    {
        if (section.DisplayInstructors == null || section.DisplayInstructors.Count == 0)
            return;

        section.InstructorAssignments.Clear();

        foreach (var (name, initials) in section.DisplayInstructors)
        {
            ParseInstructorName(name, out var lastName, out var firstName);
            var nameKey = string.IsNullOrWhiteSpace(lastName) ? name : $"{lastName}, {firstName}";

            var instructor = index.ResolveInstructor(lastName, firstName, initials, out var warning);
            if (warning != null)
                warnings.Add(warning);

            if (instructor != null)
            {
                section.InstructorAssignments.Add(new InstructorAssignment
                {
                    InstructorId = instructor.Id
                });
                resolved.Add(nameKey);
            }
            else
            {
                unresolved.Add(nameKey);
            }
        }
    }

    /// <summary>
    /// Parses an instructor name in "LastName, FirstName" format.
    /// If no comma is present, the entire string is treated as the last name.
    /// </summary>
    private static void ParseInstructorName(string name, out string lastName, out string firstName)
    {
        var commaIndex = name.IndexOf(',');
        if (commaIndex >= 0)
        {
            lastName = name[..commaIndex].Trim();
            firstName = name[(commaIndex + 1)..].Trim();
        }
        else
        {
            lastName = name.Trim();
            firstName = string.Empty;
        }
    }

    /// <summary>
    /// Resolves the section's imported campus name to a local campus ID.
    /// </summary>
    private static void ResolveCampus(
        Section section,
        ImportResolutionIndex index,
        ref bool resolvedCampus)
    {
        if (string.IsNullOrWhiteSpace(section.ImportedCampusName))
            return;

        var campus = index.ResolveCampus(section.ImportedCampusName);
        if (campus != null)
        {
            section.CampusId = campus.Id;
            resolvedCampus = true;
        }
    }

    /// <summary>
    /// Resolves the section's imported section type name to a local section type ID.
    /// </summary>
    private static void ResolveSectionType(
        Section section,
        ImportResolutionIndex index,
        ref bool resolvedSectionType)
    {
        if (string.IsNullOrWhiteSpace(section.ImportedSectionTypeName))
            return;

        var sectionType = index.ResolveSectionType(section.ImportedSectionTypeName);
        if (sectionType != null)
        {
            section.SectionTypeId = sectionType.Id;
            resolvedSectionType = true;
        }
    }

    /// <summary>
    /// Resolves each imported tag name to a local tag ID. Unmatched tags are silently
    /// dropped (they don't exist in the importing department's world).
    /// </summary>
    private static void ResolveTags(
        Section section,
        ImportResolutionIndex index,
        HashSet<string> resolved,
        HashSet<string> unresolved)
    {
        if (section.ImportedTagNames == null || section.ImportedTagNames.Count == 0)
            return;

        section.TagIds.Clear();
        foreach (var tagName in section.ImportedTagNames)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                continue;

            var tag = index.ResolveTag(tagName);
            if (tag != null)
            {
                section.TagIds.Add(tag.Id);
                resolved.Add(tagName);
            }
            else
            {
                unresolved.Add(tagName);
            }
        }
    }

    /// <summary>
    /// Resolves a meeting's imported building + room number to a local room ID.
    /// </summary>
    private static void ResolveRoom(
        SectionDaySchedule meeting,
        ImportResolutionIndex index,
        List<string> warnings,
        HashSet<string> resolved,
        HashSet<string> unresolved)
    {
        var roomNumber = meeting.ImportedRoomNumber;
        if (string.IsNullOrWhiteSpace(roomNumber))
            return;

        var building = meeting.ImportedBuilding;
        var displayKey = string.IsNullOrWhiteSpace(building)
            ? roomNumber
            : $"{building} {roomNumber}";

        var room = index.ResolveRoom(building, roomNumber, out var warning);
        if (warning != null)
            warnings.Add(warning);

        if (room != null)
        {
            meeting.RoomId = room.Id;
            resolved.Add(displayKey);
        }
        else
        {
            unresolved.Add(displayKey);
        }
    }

    /// <summary>
    /// Resolves a meeting's imported meeting type name to a local meeting type ID.
    /// </summary>
    private static void ResolveMeetingType(
        SectionDaySchedule meeting,
        ImportResolutionIndex index,
        HashSet<string> resolved,
        HashSet<string> unresolved)
    {
        var meetingTypeName = meeting.ImportedMeetingTypeName;
        if (string.IsNullOrWhiteSpace(meetingTypeName))
            return;

        var meetingType = index.ResolveMeetingType(meetingTypeName);
        if (meetingType != null)
        {
            meeting.MeetingTypeId = meetingType.Id;
            resolved.Add(meetingTypeName);
        }
        else
        {
            unresolved.Add(meetingTypeName);
        }
    }
}

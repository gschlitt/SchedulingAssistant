using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// Something about a real section that the template cannot say, so the exporter left it out. These are
/// observations, not failures: the importer was never meant to carry them, and each category is counted so
/// the report can say how much of a real department's data a spreadsheet round trip cannot keep.
/// </summary>
/// <param name="Category">A short, constant name for the kind of loss (<c>workload</c>, <c>notes</c>, ...).</param>
/// <param name="Detail">One line saying what was left out and why (no personal data).</param>
internal sealed record ExportNote(string Category, string Detail);

/// <summary>One meeting as the importer should store it, expressed in the source database's own ids.</summary>
/// <param name="Day">1 = Monday ... 7 = Sunday.</param>
/// <param name="Start">Start, in minutes from midnight.</param>
/// <param name="Duration">Duration in minutes.</param>
/// <param name="Frequency">Null (weekly), <c>odd</c>, <c>even</c> or a week list.</param>
/// <param name="MeetingTypeId">The meeting type's id, or null.</param>
/// <param name="RoomId">The room's id, or null.</param>
internal sealed record ExpectedMeeting(int Day, int Start, int Duration, string? Frequency, string? MeetingTypeId, string? RoomId);

/// <summary>
/// A real section turned into template rows, with what the importer is expected to produce from them. When
/// the template cannot express part of the section the part is left out and a note says why; when it cannot
/// express the section at all, <see cref="Spec"/> is null.
/// </summary>
internal sealed class ExportedSection
{
    /// <summary>The real section.</summary>
    public required Section Source { get; init; }

    /// <summary>The section as file rows, or null when it could not be written at all.</summary>
    public ImportSectionSpec? Spec { get; init; }

    /// <summary>What was left out, and why.</summary>
    public IReadOnlyList<ExportNote> Notes { get; init; } = Array.Empty<ExportNote>();

    /// <summary>The instructors that were written, as source ids in file order.</summary>
    public IReadOnlyList<string> ExpectedInstructorIds { get; init; } = Array.Empty<string>();

    /// <summary>The section type that was written (source id), or null.</summary>
    public string? ExpectedSectionTypeId { get; init; }

    /// <summary>The campus that was written (source id), or null.</summary>
    public string? ExpectedCampusId { get; init; }

    /// <summary>The tags that were written (source ids).</summary>
    public IReadOnlyList<string> ExpectedTagIds { get; init; } = Array.Empty<string>();

    /// <summary>The meetings that were written, one per day.</summary>
    public IReadOnlyList<ExpectedMeeting> ExpectedMeetings { get; init; } = Array.Empty<ExpectedMeeting>();
}

/// <summary>
/// Every list the exporter needs to turn ids into the names the template uses, read once from a database.
/// </summary>
internal sealed class ExportLookups
{
    /// <summary>Courses by id.</summary>
    public required Dictionary<string, Course> Courses { get; init; }

    /// <summary>Subjects by id.</summary>
    public required Dictionary<string, Subject> Subjects { get; init; }

    /// <summary>Instructors by id.</summary>
    public required Dictionary<string, Instructor> Instructors { get; init; }

    /// <summary>Rooms by id.</summary>
    public required Dictionary<string, Room> Rooms { get; init; }

    /// <summary>Campuses by id.</summary>
    public required Dictionary<string, Campus> Campuses { get; init; }

    /// <summary>Section types, meeting types and tags by id.</summary>
    public required Dictionary<string, SchedulingEnvironmentValue> Values { get; init; }

    /// <summary>How many rooms there are for each room number (case and spacing ignored), to spot numbers that a blank Building cannot name.</summary>
    public required Dictionary<string, int> RoomsPerNumber { get; init; }

    /// <summary>Reads every list from the rig's database.</summary>
    /// <param name="rig">The rig to read.</param>
    /// <returns>The lookups.</returns>
    public static ExportLookups Build(ImportRig rig)
    {
        var rooms = rig.Rooms.GetAll();
        var values = new Dictionary<string, SchedulingEnvironmentValue>();
        foreach (var type in new[] { SchedulingEnvironmentTypes.SectionType, SchedulingEnvironmentTypes.MeetingType, SchedulingEnvironmentTypes.Tag })
            foreach (var value in rig.Environment.GetAll(type))
                values[value.Id] = value;
        return new ExportLookups
        {
            Courses = rig.Courses.GetAll().ToDictionary(c => c.Id),
            Subjects = rig.Subjects.GetAll().ToDictionary(s => s.Id),
            Instructors = rig.Instructors.GetAll().ToDictionary(i => i.Id),
            Rooms = rooms.ToDictionary(r => r.Id),
            Campuses = rig.Campuses.GetAll().ToDictionary(c => c.Id),
            Values = values,
            RoomsPerNumber = rooms.GroupBy(r => NameMatch.Normalize(r.RoomNumber), NameMatch.Comparer)
                .ToDictionary(g => g.Key, g => g.Count(), NameMatch.Comparer),
        };
    }
}

/// <summary>
/// Turns real sections into template rows (the inverse of the import), so that exporting a semester and
/// importing it into another is a round trip that proves the importer understands what a real department's
/// data looks like. Parts of a section the template cannot say are left out and recorded as
/// <see cref="ExportNote"/>s.
/// </summary>
internal static class StressExport
{
    /// <summary>The earliest Begin and the latest End the importer accepts (07:30 and 22:00); a meeting outside them is refused by design (D3).</summary>
    private const int EarliestBegin = 7 * 60 + 30, LatestEnd = 22 * 60;

    /// <summary>
    /// Exports one section.
    /// </summary>
    /// <param name="section">The real section.</param>
    /// <param name="lookups">The lists that turn ids into names.</param>
    /// <returns>The rows' spec, what the importer should produce, and what was left out.</returns>
    public static ExportedSection Export(Section section, ExportLookups lookups)
    {
        var notes = new List<ExportNote>();

        if (section.CourseId is null || !lookups.Courses.TryGetValue(section.CourseId, out var course))
            return Unwritable(section, notes, "course", "the section has no course record");
        if (!lookups.Subjects.TryGetValue(course.SubjectId, out var subject))
            return Unwritable(section, notes, "course", "the course's subject record is missing");

        // The course number is what follows the subject's abbreviation in the course code (SUBJ101 -> 101).
        var code = NameMatch.NormalizeCode(course.CalendarCode);
        var abbreviation = NameMatch.NormalizeCode(subject.CalendarAbbreviation);
        if (abbreviation.Length == 0 || code.Length <= abbreviation.Length
            || !code.StartsWith(abbreviation, StringComparison.OrdinalIgnoreCase))
            return Unwritable(section, notes, "course", "the course code does not start with its subject's abbreviation, so Subject and Course Number cannot be split");
        var number = code[abbreviation.Length..];
        if (string.IsNullOrWhiteSpace(section.SectionCode))
            return Unwritable(section, notes, "section", "the section code is blank");

        var instructorIds = new List<string>();
        var instructorNames = new List<string>();
        foreach (var assignment in section.InstructorAssignments)
        {
            if (!lookups.Instructors.TryGetValue(assignment.InstructorId, out var instructor))
            {
                notes.Add(new ExportNote("instructor", "an assigned instructor's record is missing"));
                continue;
            }
            if (instructor.LastName.Contains(',') || instructor.LastName.Contains(';') || instructor.FirstName.Contains(';')
                || string.IsNullOrWhiteSpace(instructor.LastName))
            {
                notes.Add(new ExportNote("instructor", "a name holds a comma or semicolon (or has no last name), which the Last, First format cannot carry"));
                continue;
            }
            instructorIds.Add(instructor.Id);
            instructorNames.Add($"{instructor.LastName}, {instructor.FirstName}");
            if (assignment.Workload != 1.0m)
                notes.Add(new ExportNote("workload", "an instructor's workload differs from the default 1.0; the import gives every instructor 1.0"));
        }

        var sectionType = NameOf(section.SectionTypeId, lookups, notes, "section type");
        var campus = section.CampusId is not null && lookups.Campuses.TryGetValue(section.CampusId, out var campusRecord)
            ? campusRecord : null;
        if (section.CampusId is not null && campus is null)
            notes.Add(new ExportNote("campus", "the campus record is missing"));
        var tagIds = new List<string>();
        var tagNames = new List<string>();
        foreach (var tagId in section.TagIds)
        {
            var name = NameOf(tagId, lookups, notes, "tag");
            if (name is null)
                continue;
            tagIds.Add(tagId);
            tagNames.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(section.Notes))
            notes.Add(new ExportNote("notes", "the section's notes have no column"));
        if (section.Reserves.Count > 0)
            notes.Add(new ExportNote("reserves", "reserved seats have no column"));
        if (section.ResourceIds.Count > 0)
            notes.Add(new ExportNote("resources", "resources have no column"));
        if (section.Flag != SectionFlag.None)
            notes.Add(new ExportNote("flag", "the attention flag has no column"));

        var (meetings, expected) = ExportMeetings(section, lookups, notes);

        var spec = new ImportSectionSpec
        {
            Subject = subject.CalendarAbbreviation,
            CourseNumber = number,
            CourseTitle = course.Title,
            SectionCode = section.SectionCode,
            SectionType = sectionType,
            Instructors = instructorNames,
            Tags = tagNames,
            Capacity = section.Capacity,
            Campus = campus?.Name,
            Meetings = meetings,
        };
        return new ExportedSection
        {
            Source = section,
            Spec = spec,
            Notes = notes,
            ExpectedInstructorIds = instructorIds,
            ExpectedSectionTypeId = sectionType is null ? null : section.SectionTypeId,
            ExpectedCampusId = campus?.Id,
            ExpectedTagIds = tagIds,
            ExpectedMeetings = expected,
        };
    }

    /// <summary>The result for a section that cannot be written at all.</summary>
    private static ExportedSection Unwritable(Section section, List<ExportNote> notes, string category, string detail)
    {
        notes.Add(new ExportNote(category, detail));
        return new ExportedSection { Source = section, Spec = null, Notes = notes };
    }

    /// <summary>The name of a section type, meeting type or tag, or null (with a note when the id is set but unknown).</summary>
    private static string? NameOf(string? id, ExportLookups lookups, List<ExportNote> notes, string what)
    {
        if (id is null)
            return null;
        if (lookups.Values.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value.Name))
            return value.Name;
        notes.Add(new ExportNote(what.Replace(' ', '-'), $"a {what} id has no record"));
        return null;
    }

    /// <summary>
    /// Groups a section's meetings into patterns (days that share a start, length, frequency, meeting type and
    /// room become one row) and leaves out any the template or the importer's rules cannot take.
    /// </summary>
    private static (List<StressMeeting> Patterns, List<ExpectedMeeting> Expected) ExportMeetings(
        Section section, ExportLookups lookups, List<ExportNote> notes)
    {
        var groups = new List<(string? Frequency, string? MeetingTypeId, string? RoomId, int Start, int Duration, List<int> Days)>();
        var expected = new List<ExpectedMeeting>();

        foreach (var meeting in section.Schedule)
        {
            if (meeting.Day is < 1 or > 7 || meeting.DurationMinutes <= 0)
            {
                notes.Add(new ExportNote("meeting", "a meeting has no real day or no length"));
                continue;
            }
            if (meeting.StartMinutes < EarliestBegin || meeting.EndMinutes > LatestEnd)
            {
                notes.Add(new ExportNote("meeting-outside-hours", "a meeting is outside 07:30-22:00, which the import refuses by design (D3)"));
                continue;
            }

            string? frequency = null;
            if (!string.IsNullOrEmpty(meeting.Frequency))
            {
                var parsed = ScheduleText.ParseFrequency(meeting.Frequency);
                if (!parsed.IsValid)
                {
                    notes.Add(new ExportNote("meeting", "a meeting's frequency is not one the template accepts"));
                    continue;
                }
                frequency = parsed.Value;
            }

            string? meetingTypeId = meeting.MeetingTypeId;
            if (meetingTypeId is not null && !(lookups.Values.TryGetValue(meetingTypeId, out var type) && !string.IsNullOrWhiteSpace(type.Name)))
            {
                notes.Add(new ExportNote("meeting-type", "a meeting type id has no record"));
                meetingTypeId = null;
            }

            string? roomId = meeting.RoomId;
            if (roomId is not null)
            {
                if (!lookups.Rooms.TryGetValue(roomId, out var room) || string.IsNullOrWhiteSpace(room.RoomNumber))
                {
                    notes.Add(new ExportNote("room", "a meeting's room record is missing or has no number"));
                    roomId = null;
                }
                else if (string.IsNullOrWhiteSpace(room.Building) && lookups.RoomsPerNumber.GetValueOrDefault(room.RoomNumber) > 1)
                {
                    notes.Add(new ExportNote("room", "a room with no building shares its number with another room, so a blank Building cannot name it"));
                    roomId = null;
                }
            }
            if (meeting.RoomTypeId is not null)
                notes.Add(new ExportNote("room-type", "a meeting's needed room type (or remote marker) has no column; the import leaves it empty (D8)"));

            var group = groups.FirstOrDefault(g => g.Frequency == frequency && g.MeetingTypeId == meetingTypeId
                && g.RoomId == roomId && g.Start == meeting.StartMinutes && g.Duration == meeting.DurationMinutes);
            if (group.Days is null)
            {
                group = (frequency, meetingTypeId, roomId, meeting.StartMinutes, meeting.DurationMinutes, new List<int>());
                groups.Add(group);
            }
            if (!group.Days.Contains(meeting.Day))
                group.Days.Add(meeting.Day);
            expected.Add(new ExpectedMeeting(meeting.Day, meeting.StartMinutes, meeting.DurationMinutes, frequency, meetingTypeId, roomId));
        }

        var patterns = groups.Select(g =>
        {
            var room = g.RoomId is null ? null : lookups.Rooms[g.RoomId];
            return new StressMeeting(g.Days.OrderBy(d => d).ToList(), g.Start, g.Start + g.Duration, g.Frequency,
                g.MeetingTypeId is null ? null : lookups.Values[g.MeetingTypeId].Name,
                room is null || string.IsNullOrWhiteSpace(room.Building) ? null : room.Building, room?.RoomNumber);
        }).ToList();
        return (patterns, expected);
    }

    /// <summary>
    /// Exports whole sections into one file naming the given semester.
    /// </summary>
    /// <param name="sections">The real sections.</param>
    /// <param name="lookups">The lists that turn ids into names.</param>
    /// <param name="year">The Academic Year the file names.</param>
    /// <param name="term">The Term the file names.</param>
    /// <param name="exported">The result for every section, written or not, in the order given.</param>
    /// <returns>The file, holding the rows of every section that could be written.</returns>
    public static StressFile ExportAll(IEnumerable<Section> sections, ExportLookups lookups, string year, string term,
        out List<ExportedSection> exported)
    {
        var file = new StressFile(year, term);
        exported = new List<ExportedSection>();
        foreach (var section in sections)
        {
            var result = Export(section, lookups);
            exported.Add(result);
            if (result.Spec is not null)
                file.AddSection(result.Spec);
        }
        return file;
    }

    /// <summary>
    /// Compares a section the importer created with the real section it came from, field by field, and says
    /// where they differ. The comparison is made in ids (the import is into the same database, so a matched
    /// record has the same id), and it expects what the design says: tags are the section's plus its course's
    /// (D2), and a blank capacity becomes the course's or the default (D1).
    /// </summary>
    /// <param name="exported">The export of the source section.</param>
    /// <param name="imported">The section the importer created.</param>
    /// <param name="course">The section's course.</param>
    /// <param name="defaultCapacity">The default section capacity setting.</param>
    /// <param name="designedEffects">Notes on differences from the real section that the design asks for (D1, D2).</param>
    /// <returns>One line per unexpected difference; empty when the sections agree.</returns>
    public static List<string> Compare(ExportedSection exported, Section imported, Course course, int? defaultCapacity,
        List<string> designedEffects)
    {
        var source = exported.Source;
        var label = $"{course.CalendarCode} {source.SectionCode}";
        var differences = new List<string>();

        if (imported.CourseId != source.CourseId)
            differences.Add($"{label}: course differs");
        if (!string.Equals(imported.SectionCode, source.SectionCode, StringComparison.Ordinal))
            differences.Add($"{label}: section code differs (typed [{source.SectionCode}], stored [{imported.SectionCode}])");

        if (!exported.ExpectedInstructorIds.ToHashSet().SetEquals(imported.InstructorIds))
            differences.Add($"{label}: instructor set differs ({exported.ExpectedInstructorIds.Count} expected, {imported.InstructorAssignments.Count} found)");
        if (imported.SectionTypeId != exported.ExpectedSectionTypeId)
            differences.Add($"{label}: section type differs");
        if (imported.CampusId != exported.ExpectedCampusId)
            differences.Add($"{label}: campus differs");

        var expectedTags = exported.ExpectedTagIds.Concat(course.TagIds).ToHashSet();
        if (!expectedTags.SetEquals(imported.TagIds))
            differences.Add($"{label}: tags differ ({expectedTags.Count} expected, {imported.TagIds.Count} found)");
        else if (!source.TagIds.ToHashSet().SetEquals(imported.TagIds))
            designedEffects.Add($"{label}: tags gained the course's tags (D2)");

        int? expectedCapacity = source.Capacity ?? course.Capacity ?? defaultCapacity;
        if (imported.Capacity != expectedCapacity)
            differences.Add($"{label}: capacity differs (expected {expectedCapacity?.ToString() ?? "none"}, found {imported.Capacity?.ToString() ?? "none"})");
        else if (imported.Capacity != source.Capacity)
            designedEffects.Add($"{label}: blank capacity became {imported.Capacity?.ToString() ?? "none"} (D1)");

        var expectedMeetings = exported.ExpectedMeetings.Select(MeetingKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var meetings = imported.Schedule
            .Select(m => MeetingKey(new ExpectedMeeting(m.Day, m.StartMinutes, m.DurationMinutes,
                string.IsNullOrEmpty(m.Frequency) ? null : m.Frequency, m.MeetingTypeId, m.RoomId)))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (!expectedMeetings.SequenceEqual(meetings))
            differences.Add($"{label}: meetings differ ({expectedMeetings.Count} expected, {meetings.Count} found)");
        if (imported.Schedule.Any(m => m.RoomTypeId is not null))
            differences.Add($"{label}: a meeting has a room type, which the import should leave empty");
        return differences;
    }

    /// <summary>A meeting as comparable text.</summary>
    private static string MeetingKey(ExpectedMeeting m) =>
        $"{m.Day}|{m.Start}|{m.Duration}|{m.Frequency}|{m.MeetingTypeId}|{m.RoomId}";
}

/// <summary>
/// A throwaway copy of the private real-data database (<c>BIOL-TT.db</c>), opened as an <see cref="ImportRig"/>
/// on one of its semesters. The original is only ever copied (a plain file copy) and never opened; nothing from
/// it is written anywhere but the temporary folder, which <see cref="ImportRig.Dispose"/> deletes.
/// </summary>
internal static class RealImportDb
{
    /// <summary>Where the private real-data database lives (see <see cref="FactRequiresLocalDbAttribute"/>).</summary>
    public static string SourcePath => RoomAvailabilityIntegrationTests.SourceDbPath;

    /// <summary>
    /// Copies the database into a new temporary folder as <c>test.db</c> and opens the rig on a semester of the copy.
    /// </summary>
    /// <param name="yearName">The academic year's name, e.g. <c>2026-2027</c>.</param>
    /// <param name="termName">The semester's name, e.g. <c>Winter</c>.</param>
    /// <returns>The rig; dispose it to delete the copy.</returns>
    /// <exception cref="InvalidOperationException">The copy has no such semester.</exception>
    public static ImportRig Open(string yearName, string termName)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"import_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "test.db");
        File.Copy(SourcePath, copy);

        AcademicYear year;
        Semester semester;
        using (var db = new DatabaseContext(copy))
        {
            (year, semester) = Find(new AcademicYearRepository(db), new SemesterRepository(db), yearName, termName);
        }
        SqliteConnection.ClearAllPools();
        return new ImportRig(folder, seed: false, year: year, semester: semester);
    }

    /// <summary>Finds a semester of a rig's database by academic year and term name.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="yearName">The academic year's name.</param>
    /// <param name="termName">The semester's name.</param>
    /// <returns>The year and the semester.</returns>
    /// <exception cref="InvalidOperationException">There is no such semester.</exception>
    public static (AcademicYear Year, Semester Semester) FindSemester(ImportRig rig, string yearName, string termName) =>
        Find(new AcademicYearRepository(rig.Db), new SemesterRepository(rig.Db), yearName, termName);

    /// <summary>Finds a semester through the repositories.</summary>
    private static (AcademicYear, Semester) Find(IAcademicYearRepository years, ISemesterRepository semesters, string yearName, string termName)
    {
        var year = years.GetAll().FirstOrDefault(y => y.Name == yearName)
            ?? throw new InvalidOperationException($"The database has no academic year {yearName}.");
        var semester = semesters.GetByAcademicYear(year.Id).FirstOrDefault(s => s.Name == termName)
            ?? throw new InvalidOperationException($"The database has no semester {termName} in {yearName}.");
        return (year, semester);
    }
}

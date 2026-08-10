using System.Text;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Exports sections visible in the current filter state as a shared schedule CSV.
/// Stateless — all data passed in as parameters.
/// </summary>
public class SharedScheduleCsvExporter
{
    private static readonly Dictionary<int, string> DayNames = new()
    {
        [1] = "Monday", [2] = "Tuesday", [3] = "Wednesday",
        [4] = "Thursday", [5] = "Friday", [6] = "Saturday", [7] = "Sunday"
    };

    private const string EnrichedHeader =
        "CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency," +
        "Instructor,Initials,Building,RoomNumber,Campus,SectionType,Tags,MeetingType,Level";

    /// <summary>
    /// Writes an enriched 18-column shared schedule CSV to the given stream.
    /// </summary>
    /// <param name="output">Target stream (caller is responsible for closing).</param>
    /// <param name="sourceLabel">Source label for the header comment (e.g. institution name).</param>
    /// <param name="semesterName">Semester name for the header comment (e.g. "Fall").</param>
    /// <param name="sections">Sections to export (already filtered by caller).</param>
    /// <param name="courseCodeLookup">Resolves CourseId → display course code.</param>
    /// <param name="lookups">ID→entity dictionaries for enriched column resolution.</param>
    /// <param name="academicYearName">
    /// Academic year name (e.g. "2026-2027"). Semester names are bare ("Fall", "Winter"), so the
    /// academic year is what distinguishes Fall 2026 from Fall 2027 — without it the importer
    /// cannot detect a cross-year mismatch. Optional for callers that have no academic year.
    /// </param>
    /// <param name="semesterId">
    /// Database ID of the exported semester. Meaningless to a foreign database, so the importer
    /// ignores it; it exists so a future "open my own share" flow can match exactly and survive
    /// a semester rename.
    /// </param>
    /// <returns>Null on success, or an error message string on failure.</returns>
    public string? Export(Stream output, string sourceLabel, string semesterName,
                          IReadOnlyList<Section> sections,
                          Func<string, string> courseCodeLookup, ExportLookups lookups,
                          string? academicYearName = null, string? semesterId = null)
    {
        try
        {
            ExportCore(output, sourceLabel, semesterName, sections, courseCodeLookup, lookups,
                       academicYearName, semesterId);
            return null;
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[SharedScheduleCsvExporter] Export failed: {ex.Message}");
            return "Export failed — unable to write file.";
        }
    }

    private void ExportCore(Stream output, string sourceLabel, string semesterName,
                            IReadOnlyList<Section> sections,
                            Func<string, string> courseCodeLookup, ExportLookups lookups,
                            string? academicYearName, string? semesterId)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        // Header comment, 6 fields:
        //   #TermPoint Schedule Overlay,<label>,<semester>,<date>,<academicYear>,<semesterId>
        // Every field is CSV-escaped: the source label is free text typed by the user and a
        // single comma in it used to shift every following field, silently corrupting the
        // semester name the importer relies on. Fields 5-6 are appended, so older readers that
        // only look at 1-4 are unaffected, and older 4-field files still parse here.
        writer.WriteLine(
            $"#TermPoint Schedule Overlay,{CsvEscape(sourceLabel)},{CsvEscape(semesterName)}," +
            $"{DateTime.Today:yyyy-MM-dd},{CsvEscape(academicYearName)},{CsvEscape(semesterId)}");

        // Column header
        writer.WriteLine(EnrichedHeader);

        // Build sorted export rows
        var exportRows = BuildExportRows(sections, courseCodeLookup, lookups);
        foreach (var row in exportRows)
            writer.WriteLine(row);
    }

    private List<string> BuildExportRows(IReadOnlyList<Section> sections,
                                          Func<string, string> courseCodeLookup,
                                          ExportLookups lookups)
    {
        var rows = new List<string>();

        var ordered = sections
            .Select(s => (Section: s, CourseCode: courseCodeLookup(s.CourseId ?? "")))
            .OrderBy(x => x.CourseCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Section.SectionCode, StringComparer.OrdinalIgnoreCase);

        foreach (var (section, courseCode) in ordered)
        {
            // Resolve per-section enriched fields once
            var perSection = ResolvePerSectionFields(section, lookups);

            if (section.Schedule.Count == 0)
            {
                // Unscheduled section — one row with blank time fields, enriched per-section columns populated
                rows.Add(FormatRow(courseCode, section.SectionCode, section.Notes,
                    "", "", "", "", "", "",
                    perSection.Instructor, perSection.Initials,
                    "", "", // no meeting → no building/room
                    perSection.Campus, perSection.SectionType, perSection.Tags,
                    "", // no meeting → no meeting type
                    perSection.Level));
            }
            else
            {
                var meetings = section.Schedule
                    .OrderBy(m => m.Day)
                    .ThenBy(m => m.StartMinutes);

                foreach (var mtg in meetings)
                {
                    var dayName = DayNames.GetValueOrDefault(mtg.Day, "");
                    var startTime = FormatTime(mtg.StartMinutes);
                    var endTime = FormatTime(mtg.EndMinutes);

                    // Resolve per-meeting enriched fields
                    var (building, roomNumber) = ResolveRoom(mtg, lookups);
                    var meetingType = ResolveMeetingType(mtg, lookups);

                    rows.Add(FormatRow(
                        courseCode, section.SectionCode, section.Notes,
                        dayName, startTime, endTime,
                        mtg.DurationMinutes.ToString(), mtg.StartMinutes.ToString(),
                        mtg.Frequency ?? "",
                        perSection.Instructor, perSection.Initials,
                        building, roomNumber,
                        perSection.Campus, perSection.SectionType, perSection.Tags,
                        meetingType, perSection.Level));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Resolves per-section enriched fields from the section's IDs using the export lookups.
    /// </summary>
    private static (string Instructor, string Initials, string Campus, string SectionType, string Tags, string Level)
        ResolvePerSectionFields(Section section, ExportLookups lookups)
    {
        // Instructors — pipe-delimited "LastName, FirstName" and initials
        var instructorNames = new List<string>();
        var instructorInitials = new List<string>();
        foreach (var assignment in section.InstructorAssignments)
        {
            if (lookups.InstructorsById.TryGetValue(assignment.InstructorId, out var instructor))
            {
                instructorNames.Add($"{instructor.LastName}, {instructor.FirstName}");
                instructorInitials.Add(instructor.Initials ?? "");
            }
        }
        var instructorField = string.Join("|", instructorNames);
        var initialsField = string.Join("|", instructorInitials);

        // Campus
        var campus = "";
        if (!string.IsNullOrEmpty(section.CampusId) &&
            lookups.CampusesById.TryGetValue(section.CampusId, out var campusEntity))
            campus = campusEntity.Name ?? "";

        // Section type
        var sectionType = "";
        if (!string.IsNullOrEmpty(section.SectionTypeId) &&
            lookups.SectionTypesById.TryGetValue(section.SectionTypeId, out var sectionTypeEntity))
            sectionType = sectionTypeEntity.Name ?? "";

        // Tags — pipe-delimited, skip deleted tags
        var tagNames = new List<string>();
        foreach (var tagId in section.TagIds)
        {
            if (lookups.TagsById.TryGetValue(tagId, out var tag) && !string.IsNullOrEmpty(tag.Name))
                tagNames.Add(tag.Name);
        }
        var tags = string.Join("|", tagNames);

        // Level — direct, no lookup
        var level = section.Level ?? "";

        return (instructorField, initialsField, campus, sectionType, tags, level);
    }

    /// <summary>
    /// Resolves building and room number for a meeting from the room lookup.
    /// </summary>
    private static (string Building, string RoomNumber) ResolveRoom(SectionDaySchedule mtg, ExportLookups lookups)
    {
        if (string.IsNullOrEmpty(mtg.RoomId) ||
            !lookups.RoomsById.TryGetValue(mtg.RoomId, out var room))
            return ("", "");
        return (room.Building ?? "", room.RoomNumber ?? "");
    }

    /// <summary>
    /// Resolves meeting type name for a meeting from the meeting type lookup.
    /// </summary>
    private static string ResolveMeetingType(SectionDaySchedule mtg, ExportLookups lookups)
    {
        if (string.IsNullOrEmpty(mtg.MeetingTypeId) ||
            !lookups.MeetingTypesById.TryGetValue(mtg.MeetingTypeId, out var meetingType))
            return "";
        return meetingType.Name ?? "";
    }

    private static string FormatRow(
        string courseCode, string sectionCode, string notes,
        string day, string startTime, string endTime,
        string durationMin, string startMinutes, string frequency,
        string instructor, string initials,
        string building, string roomNumber,
        string campus, string sectionType, string tags,
        string meetingType, string level)
    {
        return $"{CsvEscape(courseCode)},{CsvEscape(sectionCode)},{CsvEscape(notes)}," +
               $"{CsvEscape(day)},{CsvEscape(startTime)},{CsvEscape(endTime)}," +
               $"{CsvEscape(durationMin)},{CsvEscape(startMinutes)},{CsvEscape(frequency)}," +
               $"{CsvEscape(instructor)},{CsvEscape(initials)}," +
               $"{CsvEscape(building)},{CsvEscape(roomNumber)}," +
               $"{CsvEscape(campus)},{CsvEscape(sectionType)},{CsvEscape(tags)}," +
               $"{CsvEscape(meetingType)},{CsvEscape(level)}";
    }

    /// <summary>
    /// RFC-4180 escaping: wraps the field in quotes if it contains a comma, newline, or quote.
    /// Embedded quotes are doubled.
    /// </summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>Formats minutes-from-midnight as "h:mm tt" (e.g. 480 → "8:00 AM").</summary>
    private static string FormatTime(int minutes)
    {
        int hours = minutes / 60;
        int mins = minutes % 60;
        var period = hours >= 12 ? "PM" : "AM";
        var displayHour = hours % 12;
        if (displayHour == 0) displayHour = 12;
        return $"{displayHour}:{mins:D2} {period}";
    }
}

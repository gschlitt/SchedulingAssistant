using System.Globalization;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Parses the three CSV Import file formats (instructors, courses, sections) into
/// typed rows. Stateless — every result is returned via <see cref="CsvParseResult{T}"/>.
/// Malformed rows are skipped and reported as <see cref="CsvParseError"/> entries; the
/// rest of the file still imports (partial-success model, matching
/// <see cref="SharedScheduleCsvParser"/>).
/// </summary>
public class CsvImportParser
{
    /// <summary>
    /// Maps recognized day tokens (numeric, full, short, or single-letter) to
    /// 1=Monday…7=Sunday. Unlike <see cref="SharedScheduleCsvParser"/>'s day map,
    /// this one also accepts bare numeric values, per the CSV Import format.
    /// </summary>
    private static readonly Dictionary<string, int> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = 1, ["Monday"] = 1, ["Mon"] = 1, ["Mo"] = 1, ["M"] = 1,
        ["2"] = 2, ["Tuesday"] = 2, ["Tue"] = 2, ["Tu"] = 2, ["T"] = 2,
        ["3"] = 3, ["Wednesday"] = 3, ["Wed"] = 3, ["We"] = 3, ["W"] = 3,
        ["4"] = 4, ["Thursday"] = 4, ["Thu"] = 4, ["Th"] = 4, ["R"] = 4,
        ["5"] = 5, ["Friday"] = 5, ["Fri"] = 5, ["Fr"] = 5, ["F"] = 5,
        ["6"] = 6, ["Saturday"] = 6, ["Sat"] = 6, ["Sa"] = 6,
        ["7"] = 7, ["Sunday"] = 7, ["Sun"] = 7, ["Su"] = 7,
    };

    /// <summary>
    /// Maps common alternative CSV header names to the canonical names used by
    /// <see cref="GetField"/>. Alias → Canonical, case-insensitive. The alias is
    /// adopted only when the canonical name is absent from the header row.
    /// </summary>
    private static readonly (string Alias, string Canonical)[] HeaderAliases =
    {
        // Course CSV: CalendarAbbrev is the subject abbreviation (→ SubjectCode);
        // CourseNumber is read directly and composed with CalendarAbbrev into
        // CalendarCode inside ParseCourses — no alias needed for it.
        ("CalendarAbbrev", "SubjectCode"),
        ("CourseName",     "Title"),
        ("CourseTitle",    "Title"),

        // Instructor CSV alternatives
        ("Surname",        "LastName"),
        ("FamilyName",     "LastName"),
        ("GivenName",      "FirstName"),

        // Section CSV alternatives
        ("Course",         "CourseCode"),
        ("Section",        "SectionCode"),
        ("Instructor",     "Instructors"),
        ("Type",           "SectionType"),
        ("Duration",       "DurationMin"),
    };

    /// <summary>Parses an instructors CSV. Rows missing a required LastName are reported as errors.</summary>
    public CsvParseResult<InstructorRow> ParseInstructors(string csvText)
    {
        var (columnIndex, dataLines, headerError) = ReadHeaderAndLines(csvText);
        var rows = new List<InstructorRow>();
        var errors = new List<CsvParseError>();
        if (headerError is not null)
        {
            errors.Add(headerError);
            return new CsvParseResult<InstructorRow>(rows, errors);
        }

        foreach (var (lineNumber, fields) in dataLines)
        {
            var lastName = GetField(columnIndex!, fields, "LastName");
            if (string.IsNullOrEmpty(lastName))
            {
                errors.Add(new CsvParseError(lineNumber, "Missing LastName"));
                continue;
            }

            rows.Add(new InstructorRow
            {
                LastName = lastName,
                FirstName = GetField(columnIndex!, fields, "FirstName"),
                Initials = GetField(columnIndex!, fields, "Initials"),
                Email = GetField(columnIndex!, fields, "Email"),
            });
        }

        return new CsvParseResult<InstructorRow>(rows, errors);
    }

    /// <summary>
    /// Parses a courses CSV. The file may supply a pre-composed <c>CalendarCode</c>
    /// column (e.g. "CHEM201") or separate <c>CalendarAbbrev</c> / <c>CourseNumber</c>
    /// columns — if separate, CalendarCode is composed as "{CalendarAbbrev}{CourseNumber}".
    /// <c>CalendarAbbrev</c> is aliased to <c>SubjectCode</c> for subject matching.
    /// Rows missing a CalendarCode (or the components to build one) are reported as errors.
    /// </summary>
    public CsvParseResult<CourseRow> ParseCourses(string csvText)
    {
        var (columnIndex, dataLines, headerError) = ReadHeaderAndLines(csvText);
        var rows = new List<CourseRow>();
        var errors = new List<CsvParseError>();
        if (headerError is not null)
        {
            errors.Add(headerError);
            return new CsvParseResult<CourseRow>(rows, errors);
        }

        // Detect whether CalendarCode must be composed from separate columns.
        var hasCourseNumber = columnIndex!.ContainsKey("CourseNumber");
        var hasCalendarCode = columnIndex.ContainsKey("CalendarCode");

        foreach (var (lineNumber, fields) in dataLines)
        {
            var subjectCode = GetField(columnIndex, fields, "SubjectCode");

            string calendarCode;
            if (hasCalendarCode)
            {
                calendarCode = GetField(columnIndex, fields, "CalendarCode");
            }
            else if (hasCourseNumber)
            {
                var courseNumber = GetField(columnIndex, fields, "CourseNumber");
                calendarCode = !string.IsNullOrEmpty(subjectCode) && !string.IsNullOrEmpty(courseNumber)
                    ? $"{subjectCode}{courseNumber}"
                    : courseNumber;
            }
            else
            {
                calendarCode = "";
            }

            if (string.IsNullOrEmpty(calendarCode))
            {
                errors.Add(new CsvParseError(lineNumber, "Missing CalendarCode (or CalendarAbbrev + CourseNumber)"));
                continue;
            }

            rows.Add(new CourseRow
            {
                SubjectCode = subjectCode,
                CalendarCode = calendarCode,
                Title = GetField(columnIndex, fields, "Title"),
            });
        }

        return new CsvParseResult<CourseRow>(rows, errors);
    }

    /// <summary>
    /// Parses a sections CSV. A row with blank CourseCode and SectionCode is a
    /// continuation of the previous section — it contributes only a MeetingRow.
    /// Rows missing a required Semester, CourseCode, or SectionCode are reported as
    /// errors and do not start a new section (their continuation rows, if any, are
    /// reported as orphaned).
    /// </summary>
    public CsvParseResult<SectionRow> ParseSections(string csvText)
    {
        var (columnIndex, dataLines, headerError) = ReadHeaderAndLines(csvText);
        var rows = new List<SectionRow>();
        var errors = new List<CsvParseError>();
        if (headerError is not null)
        {
            errors.Add(headerError);
            return new CsvParseResult<SectionRow>(rows, errors);
        }

        SectionRow? current = null;

        foreach (var (lineNumber, fields) in dataLines)
        {
            var courseCode = GetField(columnIndex!, fields, "CourseCode");
            var sectionCode = GetField(columnIndex!, fields, "SectionCode");
            bool isContinuation = string.IsNullOrEmpty(courseCode) && string.IsNullOrEmpty(sectionCode);

            if (!isContinuation)
            {
                var semester = GetField(columnIndex!, fields, "Semester");
                if (string.IsNullOrEmpty(semester))
                {
                    errors.Add(new CsvParseError(lineNumber, "Missing Semester"));
                    current = null;
                    continue;
                }
                if (string.IsNullOrEmpty(courseCode))
                {
                    errors.Add(new CsvParseError(lineNumber, "Missing CourseCode"));
                    current = null;
                    continue;
                }
                if (string.IsNullOrEmpty(sectionCode))
                {
                    errors.Add(new CsvParseError(lineNumber, "Missing SectionCode"));
                    current = null;
                    continue;
                }

                current = new SectionRow
                {
                    AcademicYear = GetField(columnIndex!, fields, "AcademicYear"),
                    Semester = semester,
                    CourseCode = courseCode,
                    CourseTitle = GetField(columnIndex!, fields, "CourseTitle"),
                    SectionCode = sectionCode,
                    Instructors = GetField(columnIndex!, fields, "Instructors"),
                    SectionType = GetField(columnIndex!, fields, "SectionType"),
                    Campus = GetField(columnIndex!, fields, "Campus"),
                    Tags = GetField(columnIndex!, fields, "Tags"),
                    Resources = GetField(columnIndex!, fields, "Resources"),
                    Reserves = GetField(columnIndex!, fields, "Reserves"),
                };
                rows.Add(current);
            }
            else if (current is null)
            {
                errors.Add(new CsvParseError(lineNumber, "Continuation row has no preceding section"));
                continue;
            }

            var (meeting, meetingError) = ParseMeetingFields(fields, columnIndex!);
            if (meetingError is not null)
            {
                errors.Add(new CsvParseError(lineNumber, meetingError));
                continue;
            }
            if (meeting is not null)
                current!.Meetings.Add(meeting);
        }

        return new CsvParseResult<SectionRow>(rows, errors);
    }

    /// <summary>
    /// Parses the meeting-level columns of one section CSV row. Returns null/null when
    /// no time information is present at all (a valid unscheduled row). Derives EndTime
    /// from StartTime + DurationMin, or DurationMin from StartTime + EndTime, when one
    /// of the pair is left blank in the CSV.
    /// </summary>
    private static (MeetingRow? Meeting, string? Error) ParseMeetingFields(string[] fields, Dictionary<string, int> columnIndex)
    {
        var day = GetField(columnIndex, fields, "Day");
        var startTime = GetField(columnIndex, fields, "StartTime");
        var endTime = GetField(columnIndex, fields, "EndTime");
        var durationMin = GetField(columnIndex, fields, "DurationMin");

        if (string.IsNullOrEmpty(day) && string.IsNullOrEmpty(startTime) &&
            string.IsNullOrEmpty(endTime) && string.IsNullOrEmpty(durationMin))
            return (null, null);

        if (string.IsNullOrEmpty(day))
            return (null, "Meeting has time information but no Day");
        if (!DayMap.TryGetValue(day, out _))
            return (null, $"Unrecognized day: '{day}'");

        if (string.IsNullOrEmpty(startTime))
            return (null, "Meeting is missing StartTime");
        if (!TryParseTime(startTime, out var start))
            return (null, $"Unparseable StartTime: '{startTime}'");

        if (string.IsNullOrEmpty(endTime) && string.IsNullOrEmpty(durationMin))
            return (null, "Meeting must include either EndTime or DurationMin");

        if (string.IsNullOrEmpty(endTime))
        {
            if (!int.TryParse(durationMin, out var minutes) || minutes <= 0)
                return (null, $"Invalid DurationMin: '{durationMin}'");
            endTime = FormatTime(start.AddMinutes(minutes));
        }
        else if (string.IsNullOrEmpty(durationMin))
        {
            if (!TryParseTime(endTime, out var end))
                return (null, $"Unparseable EndTime: '{endTime}'");
            var minutes = (int)(end - start).TotalMinutes;
            if (minutes <= 0)
                return (null, $"EndTime '{endTime}' is not after StartTime '{startTime}'");
            durationMin = minutes.ToString(CultureInfo.InvariantCulture);
        }
        else if (!int.TryParse(durationMin, out _))
        {
            return (null, $"Invalid DurationMin: '{durationMin}'");
        }

        var meeting = new MeetingRow
        {
            Day = day,
            StartTime = startTime,
            EndTime = endTime,
            DurationMin = durationMin,
            Room = GetField(columnIndex, fields, "Room"),
            Frequency = GetField(columnIndex, fields, "Frequency"),
            MeetingType = GetField(columnIndex, fields, "MeetingType"),
        };
        return (meeting, null);
    }

    private static bool TryParseTime(string text, out DateTime time) =>
        DateTime.TryParseExact(text, "h:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static string FormatTime(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Reads a value by header name; blank if the column is absent from the header or the row.</summary>
    private static string GetField(Dictionary<string, int> columnIndex, string[] fields, string name) =>
        columnIndex.TryGetValue(name, out int idx) && idx < fields.Length ? fields[idx].Trim() : "";

    /// <summary>
    /// Reads the header row (required) into a case-insensitive column-name → index map,
    /// then reads the remaining logical lines into raw field arrays. Column matching is
    /// by name, not position, so files are tolerant of reordered, extra, or missing columns.
    /// </summary>
    private static (Dictionary<string, int>? ColumnIndex, List<(int LineNumber, string[] Fields)> DataLines, CsvParseError? Error)
        ReadHeaderAndLines(string csvText)
    {
        using var reader = new StringReader(csvText);
        var headerLine = ReadCsvLine(reader);
        if (headerLine is null)
            return (null, new(), new CsvParseError(1, "File is empty."));

        var headers = SharedScheduleCsvParser.ParseCsvRow(headerLine);
        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            if (!string.IsNullOrEmpty(h))
                columnIndex[h] = i;
        }

        // Register aliases so common alternative header names resolve to the
        // canonical names used by GetField. Only adds the alias if the canonical
        // name isn't already present (original header wins).
        foreach (var (alias, canonical) in HeaderAliases)
        {
            if (!columnIndex.ContainsKey(canonical) && columnIndex.TryGetValue(alias, out var idx))
                columnIndex[canonical] = idx;
        }

        var dataLines = new List<(int, string[])>();
        int lineNumber = 2;
        string? line;
        while ((line = ReadCsvLine(reader)) is not null)
        {
            if (line.Length > 0)
                dataLines.Add((lineNumber, SharedScheduleCsvParser.ParseCsvRow(line)));
            lineNumber++;
        }

        return (columnIndex, dataLines, null);
    }

    /// <summary>
    /// Reads a logical CSV line, joining physical lines when a quoted field contains an
    /// embedded newline. Returns null at end of input. Field splitting itself is delegated
    /// to <see cref="SharedScheduleCsvParser.ParseCsvRow"/> to avoid a second RFC 4180 scanner.
    /// </summary>
    private static string? ReadCsvLine(TextReader reader)
    {
        var line = reader.ReadLine();
        if (line is null) return null;

        while (CountUnescapedQuotes(line) % 2 != 0)
        {
            var next = reader.ReadLine();
            if (next is null) break;
            line = line + "\n" + next;
        }

        return line;
    }

    private static int CountUnescapedQuotes(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"')
            {
                count++;
                if (i + 1 < s.Length && s[i + 1] == '"')
                    i++;
            }
        }
        return count;
    }
}

using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;

namespace TermPoint.Tests;

/// <summary>
/// Terse builders for the section-import planner tests: rows, sheets and setup snapshots made by hand,
/// so a test states only what it is about. Rows default to the open semester (Fall, 2026-2027) and
/// leave every other cell blank.
/// </summary>
internal static class SectionImportTestKit
{
    /// <summary>The open semester's name in every test snapshot.</summary>
    public const string TermName = "Fall";

    /// <summary>The open semester's academic year name in every test snapshot.</summary>
    public const string AcademicYearName = "2026-2027";

    /// <summary>The open semester used by <see cref="Snapshot"/>.</summary>
    public static readonly Semester OpenSemester = new() { Id = "sem-fall", AcademicYearId = "ay-1", Name = TermName };

    /// <summary>
    /// Builds a row with every template column present. The academic year and term name the open
    /// semester unless <paramref name="cells"/> says otherwise; everything else is blank.
    /// </summary>
    /// <param name="rowNumber">The row number as Excel shows it.</param>
    /// <param name="cells">(Column, value) pairs that override the defaults.</param>
    public static ImportRow Row(int rowNumber, params (string Column, string Value)[] cells)
    {
        var map = new Dictionary<string, string>(NameMatch.Comparer);
        foreach (var column in SectionImportColumns.All)
            map[column.Name] = string.Empty;
        map[SectionImportColumns.AcademicYear] = AcademicYearName;
        map[SectionImportColumns.Term] = TermName;
        foreach (var (column, value) in cells)
            map[column] = value;
        return new ImportRow(rowNumber, map);
    }

    /// <summary>
    /// Builds a row for a section: the five key cells and a course title ("Intro"), plus extra cells.
    /// </summary>
    /// <param name="rowNumber">The row number.</param>
    /// <param name="subject">Subject cell.</param>
    /// <param name="number">Course Number cell.</param>
    /// <param name="code">Section Code cell.</param>
    /// <param name="cells">Further (column, value) pairs; they override the defaults.</param>
    public static ImportRow Sec(int rowNumber, string subject, string number, string code,
        params (string Column, string Value)[] cells)
    {
        var all = new List<(string Column, string Value)>
        {
            (SectionImportColumns.Subject, subject),
            (SectionImportColumns.CourseNumber, number),
            (SectionImportColumns.SectionCode, code),
            (SectionImportColumns.CourseTitle, "Intro"),
        };
        all.AddRange(cells);
        return Row(rowNumber, all.ToArray());
    }

    /// <summary>Builds a sheet from rows.</summary>
    /// <param name="rows">The rows in file order.</param>
    public static ImportSheet Sheet(params ImportRow[] rows) => new("test.csv", rows);

    /// <summary>
    /// Builds a snapshot of the open semester (Fall, 2026-2027) holding only what the test names.
    /// </summary>
    public static ImportSetupSnapshot Snapshot(
        IEnumerable<Subject>? subjects = null,
        IEnumerable<Course>? courses = null,
        IEnumerable<Instructor>? instructors = null,
        IEnumerable<Section>? sections = null,
        IEnumerable<SchedulingEnvironmentValue>? sectionTypes = null,
        IEnumerable<SchedulingEnvironmentValue>? meetingTypes = null,
        IEnumerable<SchedulingEnvironmentValue>? tags = null,
        IEnumerable<Campus>? campuses = null,
        IEnumerable<Room>? rooms = null,
        IEnumerable<LegalStartTime>? legalStartTimes = null,
        int? defaultSectionCapacity = null,
        bool includeSaturday = false,
        bool includeSunday = false) =>
        new(OpenSemester, new AcademicYear { Id = "ay-1", Name = AcademicYearName },
            subjects, courses, instructors, sections, sectionTypes, meetingTypes, tags, campuses, rooms,
            legalStartTimes, defaultSectionCapacity, includeSaturday, includeSunday);

    /// <summary>
    /// Builds a row for a section that meets once: the key cells plus Meeting Days, Begin and End,
    /// and any further cells.
    /// </summary>
    /// <param name="rowNumber">The row number.</param>
    /// <param name="subject">Subject cell.</param>
    /// <param name="number">Course Number cell.</param>
    /// <param name="code">Section Code cell.</param>
    /// <param name="days">Meeting Days cell.</param>
    /// <param name="begin">Begin cell.</param>
    /// <param name="end">End cell.</param>
    /// <param name="cells">Further (column, value) pairs.</param>
    public static ImportRow Mtg(int rowNumber, string subject, string number, string code,
        string days, string begin, string end, params (string Column, string Value)[] cells)
    {
        var all = new List<(string Column, string Value)>
        {
            (SectionImportColumns.MeetingDays, days),
            (SectionImportColumns.Begin, begin),
            (SectionImportColumns.End, end),
        };
        all.AddRange(cells);
        return Sec(rowNumber, subject, number, code, all.ToArray());
    }

    /// <summary>A legal start time entry for a block length, with start times given as HHMM numbers (1030 = 10:30).</summary>
    /// <param name="hours">The block length in hours.</param>
    /// <param name="hhmm">The start times, as HHMM numbers.</param>
    public static LegalStartTime Legal(double hours, params int[] hhmm) =>
        new() { BlockLength = hours, StartTimes = hhmm.Select(t => t / 100 * 60 + t % 100).ToList() };

    /// <summary>A subject record.</summary>
    public static Subject Subj(string abbreviation, string? name = null) =>
        new() { CalendarAbbreviation = abbreviation, Name = name ?? $"{abbreviation} studies" };

    /// <summary>A course record in the given subject.</summary>
    public static Course Crs(Subject subject, string calendarCode, string title = "Existing title") =>
        new() { SubjectId = subject.Id, CalendarCode = calendarCode, Title = title };

    /// <summary>An instructor record.</summary>
    public static Instructor Inst(string last, string first, string initials) =>
        new() { LastName = last, FirstName = first, Initials = initials };

    /// <summary>A setup value (section type, meeting type or tag).</summary>
    public static SchedulingEnvironmentValue Val(string name, int sortOrder = 0) =>
        new() { Name = name, SortOrder = sortOrder };

    /// <summary>A campus record.</summary>
    public static Campus Camp(string name, int sortOrder = 0) => new() { Name = name, SortOrder = sortOrder };

    /// <summary>A room record.</summary>
    public static Room Rm(string building, string number, int sortOrder = 0) =>
        new() { Building = building, RoomNumber = number, SortOrder = sortOrder };

    /// <summary>A section of the open semester.</summary>
    public static Section Sect(Course course, string code) =>
        new() { SemesterId = OpenSemester.Id, CourseId = course.Id, SectionCode = code };
}

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Whether a template column describes the section as a whole or one meeting pattern of it.
/// A section that meets at different times on different days is written as several rows with the
/// same course and section code; the section-level cells describe the section and the
/// meeting-level cells describe that row's meeting.
/// </summary>
public enum ImportColumnLevel
{
    /// <summary>The cell belongs to the section (course, code, type, instructors, tags, capacity, campus).</summary>
    Section,

    /// <summary>The cell belongs to one meeting of the section (days, times, frequency, meeting type, room).</summary>
    Meeting,
}

/// <summary>
/// One column of the section import template.
/// </summary>
/// <param name="Name">The column's display name exactly as it appears in the template's header row.</param>
/// <param name="Level">Whether the column is section-level or meeting-level.</param>
/// <param name="AlwaysRequired">
/// True when a row's cell in this column may never be blank. Every column must be <em>present</em> in a
/// file (the header check); this flag is only about the cell's content, which the planner checks.
/// </param>
public sealed record ImportColumn(string Name, ImportColumnLevel Level, bool AlwaysRequired);

/// <summary>
/// The single source of truth for the columns of the section import template, in template order.
/// The file reader checks headers against <see cref="All"/>; the planner reads cells by the named
/// constants, so no other code ever spells a column name as a string literal.
/// </summary>
public static class SectionImportColumns
{
    /// <summary>Academic year of the semester being imported into (section level, required).</summary>
    public const string AcademicYear = "Academic Year";

    /// <summary>Term of the semester being imported into, e.g. Fall (section level, required).</summary>
    public const string Term = "Term";

    /// <summary>The subject's calendar abbreviation, e.g. CHEM (section level, required).</summary>
    public const string Subject = "Subject";

    /// <summary>The course number, e.g. 101; with <see cref="Subject"/> it identifies the course (section level, required).</summary>
    public const string CourseNumber = "Course Number";

    /// <summary>The course title, used when the course is created (section level).</summary>
    public const string CourseTitle = "Course Title";

    /// <summary>The section code, e.g. A or 001 (section level, required).</summary>
    public const string SectionCode = "Section Code";

    /// <summary>The section type name (section level).</summary>
    public const string SectionType = "Section Type";

    /// <summary>The instructors, <c>Last, First</c> separated by semicolons (section level).</summary>
    public const string Instructors = "Instructor(s)";

    /// <summary>The tags, separated by semicolons (section level).</summary>
    public const string Tags = "Tags";

    /// <summary>The capacity as a whole number (section level).</summary>
    public const string Capacity = "Capacity";

    /// <summary>The meeting's days, e.g. <c>M,W</c> (meeting level).</summary>
    public const string MeetingDays = "Meeting Days";

    /// <summary>The meeting's begin time (meeting level).</summary>
    public const string Begin = "Begin";

    /// <summary>The meeting's end time (meeting level).</summary>
    public const string End = "End";

    /// <summary>The meeting's frequency: blank (weekly), odd, even or week numbers (meeting level).</summary>
    public const string Frequency = "Frequency";

    /// <summary>The meeting type name (meeting level).</summary>
    public const string MeetingType = "Meeting Type";

    /// <summary>The room's building (meeting level).</summary>
    public const string Building = "Building";

    /// <summary>The room number (meeting level).</summary>
    public const string Room = "Room";

    /// <summary>The campus name (section level).</summary>
    public const string Campus = "Campus";

    /// <summary>Every template column, in template order.</summary>
    public static IReadOnlyList<ImportColumn> All { get; } = new[]
    {
        new ImportColumn(AcademicYear,  ImportColumnLevel.Section, AlwaysRequired: true),
        new ImportColumn(Term,          ImportColumnLevel.Section, AlwaysRequired: true),
        new ImportColumn(Subject,       ImportColumnLevel.Section, AlwaysRequired: true),
        new ImportColumn(CourseNumber,  ImportColumnLevel.Section, AlwaysRequired: true),
        new ImportColumn(CourseTitle,   ImportColumnLevel.Section, AlwaysRequired: false),
        new ImportColumn(SectionCode,   ImportColumnLevel.Section, AlwaysRequired: true),
        new ImportColumn(SectionType,   ImportColumnLevel.Section, AlwaysRequired: false),
        new ImportColumn(Instructors,   ImportColumnLevel.Section, AlwaysRequired: false),
        new ImportColumn(Tags,          ImportColumnLevel.Section, AlwaysRequired: false),
        new ImportColumn(Capacity,      ImportColumnLevel.Section, AlwaysRequired: false),
        new ImportColumn(MeetingDays,   ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(Begin,         ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(End,           ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(Frequency,     ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(MeetingType,   ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(Building,      ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(Room,          ImportColumnLevel.Meeting, AlwaysRequired: false),
        new ImportColumn(Campus,        ImportColumnLevel.Section, AlwaysRequired: false),
    };
}

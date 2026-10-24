using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Everything the section-import planner reads from the database, captured once so that planning is a
/// pure function of (file rows, snapshot). It is immutable: the lists are copied on construction, and
/// it holds no database connection, so it is safe to hand to another thread and to build by hand in
/// tests. Production code builds it with <see cref="ImportSetupSnapshotBuilder"/>.
/// </summary>
public sealed class ImportSetupSnapshot
{
    /// <summary>The open semester the file is being imported into.</summary>
    public Semester Semester { get; }

    /// <summary>The academic year that contains <see cref="Semester"/>.</summary>
    public AcademicYear AcademicYear { get; }

    /// <summary>Every subject in the database.</summary>
    public IReadOnlyList<Subject> Subjects { get; }

    /// <summary>Every course in the database (active and inactive).</summary>
    public IReadOnlyList<Course> Courses { get; }

    /// <summary>Every instructor in the database (active and inactive).</summary>
    public IReadOnlyList<Instructor> Instructors { get; }

    /// <summary>The sections that are already in <see cref="Semester"/> (and only that semester).</summary>
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>Every section type (setup value of type <c>sectionType</c>).</summary>
    public IReadOnlyList<SchedulingEnvironmentValue> SectionTypes { get; }

    /// <summary>Every meeting type (setup value of type <c>meetingType</c>).</summary>
    public IReadOnlyList<SchedulingEnvironmentValue> MeetingTypes { get; }

    /// <summary>Every tag (setup value of type <c>tag</c>).</summary>
    public IReadOnlyList<SchedulingEnvironmentValue> Tags { get; }

    /// <summary>Every campus.</summary>
    public IReadOnlyList<Campus> Campuses { get; }

    /// <summary>Every room.</summary>
    public IReadOnlyList<Room> Rooms { get; }

    /// <summary>The legal start times of <see cref="AcademicYear"/>, one entry per block length.</summary>
    public IReadOnlyList<LegalStartTime> LegalStartTimes { get; }

    /// <summary>The default capacity for a new section, or null when none is configured.</summary>
    public int? DefaultSectionCapacity { get; }

    /// <summary>True when Saturday meetings are turned on in the settings.</summary>
    public bool IncludeSaturday { get; }

    /// <summary>True when Sunday meetings are turned on in the settings.</summary>
    public bool IncludeSunday { get; }

    /// <summary>
    /// Creates a snapshot. Only the semester and its academic year are required; every list defaults
    /// to empty, so a test names only what it needs. Lists are copied, so later changes to the
    /// caller's lists do not affect the snapshot.
    /// </summary>
    /// <param name="semester">The open semester.</param>
    /// <param name="academicYear">The semester's academic year.</param>
    /// <param name="subjects">All subjects.</param>
    /// <param name="courses">All courses.</param>
    /// <param name="instructors">All instructors.</param>
    /// <param name="sections">The sections already in the open semester.</param>
    /// <param name="sectionTypes">All section types.</param>
    /// <param name="meetingTypes">All meeting types.</param>
    /// <param name="tags">All tags.</param>
    /// <param name="campuses">All campuses.</param>
    /// <param name="rooms">All rooms.</param>
    /// <param name="legalStartTimes">The academic year's legal start times.</param>
    /// <param name="defaultSectionCapacity">The default section capacity, or null.</param>
    /// <param name="includeSaturday">Whether Saturday meetings are turned on.</param>
    /// <param name="includeSunday">Whether Sunday meetings are turned on.</param>
    /// <exception cref="ArgumentNullException">The semester or academic year is null.</exception>
    public ImportSetupSnapshot(
        Semester semester,
        AcademicYear academicYear,
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
        bool includeSunday = false)
    {
        Semester = semester ?? throw new ArgumentNullException(nameof(semester));
        AcademicYear = academicYear ?? throw new ArgumentNullException(nameof(academicYear));
        Subjects = Copy(subjects);
        Courses = Copy(courses);
        Instructors = Copy(instructors);
        Sections = Copy(sections);
        SectionTypes = Copy(sectionTypes);
        MeetingTypes = Copy(meetingTypes);
        Tags = Copy(tags);
        Campuses = Copy(campuses);
        Rooms = Copy(rooms);
        LegalStartTimes = Copy(legalStartTimes);
        DefaultSectionCapacity = defaultSectionCapacity;
        IncludeSaturday = includeSaturday;
        IncludeSunday = includeSunday;
    }

    /// <summary>Copies a sequence into a read-only array; null becomes empty.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to copy. May be null.</param>
    /// <returns>A new read-only list holding the same elements.</returns>
    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? source) =>
        source is null ? Array.Empty<T>() : source.ToArray();
}

using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Who has taught which subjects, and in which academic year: the "small table of (instructor, subject, academic
/// year)" that <see cref="InstructorFocusRule"/> decides from (spec item 20, "Who counts as an in-focus
/// instructor"). Immutable once built, and pure: <see cref="Build"/> takes plain in-memory collections, so it can
/// be tested without a database.
/// <para>
/// Academic years are identified by their POSITION in a fixed order (<see cref="AcademicYear.StartYear"/>, then
/// <see cref="AcademicYear.Name"/>), not by their start year. "The year before Y" therefore means the previous year
/// that exists in that order, and a gap (no 2023-2024 in the database) does not break it.
/// </para>
/// </summary>
public sealed class TeachingHistory
{
    /// <summary>An empty history: no instructor has taught anything and no academic year is known.</summary>
    public static TeachingHistory Empty { get; } = new(
        new Dictionary<string, int>(),
        new Dictionary<string, IReadOnlyDictionary<int, IReadOnlySet<string>>>());

    /// <summary>Academic year id to its position in the ordered list (0 = earliest).</summary>
    private readonly IReadOnlyDictionary<string, int> _yearIndexById;

    /// <summary>Instructor id to (year position to the subject ids taught that year).</summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlySet<string>>> _subjectsByInstructor;

    /// <summary>The empty answer returned for an instructor who has taught nothing.</summary>
    private static readonly IReadOnlyDictionary<int, IReadOnlySet<string>> NoYears =
        new Dictionary<int, IReadOnlySet<string>>();

    /// <param name="yearIndexById">Academic year id to ordered position.</param>
    /// <param name="subjectsByInstructor">Instructor id to year position to subject ids.</param>
    private TeachingHistory(
        IReadOnlyDictionary<string, int> yearIndexById,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlySet<string>>> subjectsByInstructor)
    {
        _yearIndexById = yearIndexById;
        _subjectsByInstructor = subjectsByInstructor;
    }

    /// <summary>
    /// Builds the history. A section counts for EVERY instructor in its
    /// <see cref="SchedulableBase.InstructorAssignments"/>; its subject is its course's
    /// <see cref="Course.SubjectId"/> (which may be empty: the section still counts as "taught something"). A
    /// section whose course, semester or academic year is unknown is skipped, as is one with no course id.
    /// </summary>
    /// <param name="sections">Every section in the database (not just the viewed semesters).</param>
    /// <param name="courses">Every course.</param>
    /// <param name="semesters">Every semester; maps a section to its academic year.</param>
    /// <param name="academicYears">Every academic year; ordered by start year then name to give each its position.</param>
    /// <returns>The built history.</returns>
    public static TeachingHistory Build(
        IEnumerable<Section> sections,
        IEnumerable<Course> courses,
        IEnumerable<Semester> semesters,
        IEnumerable<AcademicYear> academicYears)
    {
        // Same order SemesterContext.Reload uses (StartYear), with Name as the tie-break for names whose start
        // year could not be parsed (they all share int.MaxValue). Ordinal so the order never depends on culture.
        var yearIndexById = new Dictionary<string, int>();
        foreach (var year in academicYears
                     .OrderBy(y => y.StartYear)
                     .ThenBy(y => y.Name, StringComparer.Ordinal))
        {
            // A duplicate id would be a data fault; keep the first rather than throw.
            yearIndexById.TryAdd(year.Id, yearIndexById.Count);
        }

        var subjectByCourseId = new Dictionary<string, string>();
        foreach (var course in courses) subjectByCourseId.TryAdd(course.Id, course.SubjectId);

        var yearIdBySemesterId = new Dictionary<string, string>();
        foreach (var semester in semesters) yearIdBySemesterId.TryAdd(semester.Id, semester.AcademicYearId);

        var working = new Dictionary<string, Dictionary<int, HashSet<string>>>();
        foreach (var section in sections)
        {
            if (section.CourseId is null
                || !subjectByCourseId.TryGetValue(section.CourseId, out var subjectId)
                || !yearIdBySemesterId.TryGetValue(section.SemesterId, out var yearId)
                || !yearIndexById.TryGetValue(yearId, out var yearIndex))
                continue;

            foreach (var instructorId in section.InstructorAssignments.Select(a => a.InstructorId).Distinct())
            {
                if (!working.TryGetValue(instructorId, out var byYear))
                    working[instructorId] = byYear = new Dictionary<int, HashSet<string>>();
                if (!byYear.TryGetValue(yearIndex, out var subjects))
                    byYear[yearIndex] = subjects = new HashSet<string>(StringComparer.Ordinal);
                subjects.Add(subjectId);
            }
        }

        // Freeze: the nested sets are exposed read-only.
        var frozen = working.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<int, IReadOnlySet<string>>)kv.Value.ToDictionary(
                y => y.Key, y => (IReadOnlySet<string>)y.Value));

        return new TeachingHistory(yearIndexById, frozen);
    }

    /// <summary>
    /// The position of an academic year in the ordered list of years (0 = earliest).
    /// </summary>
    /// <param name="academicYearId">The academic year id; null is allowed.</param>
    /// <returns>The position, or null when the id is null or not a year this history knows.</returns>
    public int? GetYearIndex(string? academicYearId) =>
        academicYearId is not null && _yearIndexById.TryGetValue(academicYearId, out var index) ? index : null;

    /// <summary>
    /// For one instructor, the subject ids they taught in each academic year they taught anything. A year with no
    /// section for them has no entry.
    /// </summary>
    /// <param name="instructorId">The instructor.</param>
    /// <returns>Year position to the subject ids taught that year; empty when the instructor has taught nothing.</returns>
    public IReadOnlyDictionary<int, IReadOnlySet<string>> GetSubjectsByYear(string instructorId) =>
        _subjectsByInstructor.TryGetValue(instructorId, out var byYear) ? byYear : NoYears;
}

using TermPoint.Data.Repositories;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Reads an <see cref="ImportSetupSnapshot"/> from the database through the existing repositories
/// (no new queries). It reads everything in one go and keeps no connection, so the snapshot it
/// returns is a plain in-memory copy. Registered with dependency injection by the import service.
/// </summary>
public sealed class ImportSetupSnapshotBuilder
{
    private readonly ISemesterRepository _semesters;
    private readonly IAcademicYearRepository _academicYears;
    private readonly ISubjectRepository _subjects;
    private readonly ICourseRepository _courses;
    private readonly IInstructorRepository _instructors;
    private readonly ISectionRepository _sections;
    private readonly ISchedulingEnvironmentRepository _environment;
    private readonly ICampusRepository _campuses;
    private readonly IRoomRepository _rooms;
    private readonly ILegalStartTimeRepository _legalStartTimes;

    /// <summary>Creates a builder over the given repositories.</summary>
    /// <param name="semesters">Looks up the open semester.</param>
    /// <param name="academicYears">Looks up the semester's academic year.</param>
    /// <param name="subjects">Reads all subjects.</param>
    /// <param name="courses">Reads all courses.</param>
    /// <param name="instructors">Reads all instructors.</param>
    /// <param name="sections">Reads the open semester's sections.</param>
    /// <param name="environment">Reads section types, meeting types and tags.</param>
    /// <param name="campuses">Reads all campuses.</param>
    /// <param name="rooms">Reads all rooms.</param>
    /// <param name="legalStartTimes">Reads the academic year's legal start times.</param>
    public ImportSetupSnapshotBuilder(
        ISemesterRepository semesters,
        IAcademicYearRepository academicYears,
        ISubjectRepository subjects,
        ICourseRepository courses,
        IInstructorRepository instructors,
        ISectionRepository sections,
        ISchedulingEnvironmentRepository environment,
        ICampusRepository campuses,
        IRoomRepository rooms,
        ILegalStartTimeRepository legalStartTimes)
    {
        _semesters = semesters;
        _academicYears = academicYears;
        _subjects = subjects;
        _courses = courses;
        _instructors = instructors;
        _sections = sections;
        _environment = environment;
        _campuses = campuses;
        _rooms = rooms;
        _legalStartTimes = legalStartTimes;
    }

    /// <summary>
    /// Reads everything the planner needs for the given semester. Sections are only those of this
    /// semester; legal start times are only those of the semester's academic year; the weekend and
    /// default-capacity settings come from <see cref="AppSettings.Current"/>.
    /// </summary>
    /// <param name="semesterId">The id of the open semester.</param>
    /// <returns>An immutable snapshot, holding no database connection.</returns>
    /// <exception cref="InvalidOperationException">The semester, or its academic year, is not in the database.</exception>
    public ImportSetupSnapshot Build(string semesterId)
    {
        var semester = _semesters.GetById(semesterId)
            ?? throw new InvalidOperationException($"The semester '{semesterId}' was not found in the database.");
        var academicYear = _academicYears.GetById(semester.AcademicYearId)
            ?? throw new InvalidOperationException(
                $"The academic year '{semester.AcademicYearId}' of semester '{semester.Name}' was not found in the database.");

        var settings = AppSettings.Current;
        return new ImportSetupSnapshot(
            semester,
            academicYear,
            subjects: _subjects.GetAll(),
            courses: _courses.GetAll(),
            instructors: _instructors.GetAll(),
            sections: _sections.GetAll(semesterId),
            sectionTypes: _environment.GetAll(SchedulingEnvironmentTypes.SectionType),
            meetingTypes: _environment.GetAll(SchedulingEnvironmentTypes.MeetingType),
            tags: _environment.GetAll(SchedulingEnvironmentTypes.Tag),
            campuses: _campuses.GetAll(),
            rooms: _rooms.GetAll(),
            legalStartTimes: _legalStartTimes.GetAll(academicYear.Id),
            defaultSectionCapacity: settings.DefaultSectionCapacity,
            includeSaturday: settings.IncludeSaturday,
            includeSunday: settings.IncludeSunday);
    }
}

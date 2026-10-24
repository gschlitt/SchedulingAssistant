using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ImportSetupSnapshotBuilder"/> against a real temporary SQLite database and the
/// real repositories: it must read the open semester's sections only, the academic year's legal start
/// times only, every setup list, and refuse a semester or academic year that is not there. Also checks
/// that <see cref="ImportSetupSnapshot"/> copies the lists it is given.
/// </summary>
public sealed class ImportSetupSnapshotBuilderTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly DatabaseContext _db;

    /// <summary>Creates a temporary database folder and opens a writer context on it.</summary>
    public ImportSetupSnapshotBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"import_snapshot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _db = new DatabaseContext(Path.Combine(_tempDir, "test.db"));
    }

    /// <summary>Closes the database, releases pooled connections and deletes the folder.</summary>
    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Builds a snapshot builder over the real repositories of the temporary database.</summary>
    private ImportSetupSnapshotBuilder NewBuilder() => new(
        new SemesterRepository(_db), new AcademicYearRepository(_db), new SubjectRepository(_db),
        new CourseRepository(_db), new InstructorRepository(_db), new SectionRepository(_db),
        new SchedulingEnvironmentRepository(_db), new CampusRepository(_db), new RoomRepository(_db),
        new LegalStartTimeRepository(_db));

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The snapshot holds the open semester and its year, only that semester's sections, only that year's
    /// legal start times, and every setup list; the weekend and capacity settings come from AppSettings.
    /// </summary>
    [Fact]
    public void Build_ReadsTheSemestersSectionsAndTheYearsLegalStartTimesOnly()
    {
        var years = new AcademicYearRepository(_db);
        var semesters = new SemesterRepository(_db);
        var ay1 = new AcademicYear { Name = "2026-2027" };
        var ay2 = new AcademicYear { Name = "2027-2028" };
        years.Insert(ay1);
        years.Insert(ay2);
        var fall = new Semester { AcademicYearId = ay1.Id, Name = "Fall" };
        var winter = new Semester { AcademicYearId = ay1.Id, Name = "Winter" };
        var other = new Semester { AcademicYearId = ay2.Id, Name = "Fall" };
        semesters.Insert(fall);
        semesters.Insert(winter);
        semesters.Insert(other);

        var subject = new Subject { Name = "Chemistry", CalendarAbbreviation = "CHEM" };
        new SubjectRepository(_db).Insert(subject);
        var course = new Course { SubjectId = subject.Id, CalendarCode = "CHEM101", Title = "Intro" };
        new CourseRepository(_db).Insert(course);
        var instructor = new Instructor { LastName = "Smith", FirstName = "John", Initials = "JS" };
        new InstructorRepository(_db).Insert(instructor);

        var sections = new SectionRepository(_db);
        sections.Insert(new Section { SemesterId = fall.Id, CourseId = course.Id, SectionCode = "A" });
        sections.Insert(new Section { SemesterId = fall.Id, CourseId = course.Id, SectionCode = "B" });
        sections.Insert(new Section { SemesterId = winter.Id, CourseId = course.Id, SectionCode = "C" });
        sections.Insert(new Section { SemesterId = other.Id, CourseId = course.Id, SectionCode = "D" });

        var environment = new SchedulingEnvironmentRepository(_db);
        environment.Insert(SchedulingEnvironmentTypes.SectionType, new SchedulingEnvironmentValue { Name = "Lecture" });
        environment.Insert(SchedulingEnvironmentTypes.MeetingType, new SchedulingEnvironmentValue { Name = "Lab" });
        environment.Insert(SchedulingEnvironmentTypes.Tag, new SchedulingEnvironmentValue { Name = "Writing" });
        environment.Insert(SchedulingEnvironmentTypes.RoomType, new SchedulingEnvironmentValue { Name = "NotATag" });
        new CampusRepository(_db).Insert(new Campus { Name = "Import test campus" });
        new RoomRepository(_db).Insert(new Room { Building = "SCI", RoomNumber = "210" });

        var legal = new LegalStartTimeRepository(_db);
        legal.Insert(new LegalStartTime { BlockLength = 1.0, StartTimes = { 480, 540 } }, ay1.Id);
        legal.Insert(new LegalStartTime { BlockLength = 2.5, StartTimes = { 600 } }, ay1.Id);
        legal.Insert(new LegalStartTime { BlockLength = 9.0, StartTimes = { 660 } }, ay2.Id);

        var snapshot = NewBuilder().Build(fall.Id);

        Assert.Equal(fall.Id, snapshot.Semester.Id);
        Assert.Equal(ay1.Id, snapshot.AcademicYear.Id);
        Assert.Equal("2026-2027", snapshot.AcademicYear.Name);
        Assert.Equal(new[] { "A", "B" }, snapshot.Sections.Select(s => s.SectionCode).OrderBy(c => c).ToArray());
        Assert.Equal(new[] { 1.0, 2.5 }, snapshot.LegalStartTimes.Select(l => l.BlockLength).OrderBy(b => b).ToArray());
        Assert.Contains(snapshot.Subjects, s => s.Id == subject.Id);
        Assert.Contains(snapshot.Courses, c => c.Id == course.Id);
        Assert.Contains(snapshot.Instructors, i => i.Id == instructor.Id);
        Assert.Equal(new[] { "Lecture" }, snapshot.SectionTypes.Select(v => v.Name).ToArray());
        Assert.Equal(new[] { "Lab" }, snapshot.MeetingTypes.Select(v => v.Name).ToArray());
        Assert.Equal(new[] { "Writing" }, snapshot.Tags.Select(v => v.Name).ToArray());
        Assert.Contains(snapshot.Campuses, c => c.Name == "Import test campus");
        Assert.Contains(snapshot.Rooms, r => r.Building == "SCI" && r.RoomNumber == "210");
        Assert.Equal(AppSettings.Current.DefaultSectionCapacity, snapshot.DefaultSectionCapacity);
        Assert.Equal(AppSettings.Current.IncludeSaturday, snapshot.IncludeSaturday);
        Assert.Equal(AppSettings.Current.IncludeSunday, snapshot.IncludeSunday);
    }

    /// <summary>An unknown semester id is refused with an InvalidOperationException.</summary>
    [Fact]
    public void Build_ThrowsForAnUnknownSemester()
    {
        Assert.Throws<InvalidOperationException>(() => NewBuilder().Build("no-such-semester"));
    }

    /// <summary>A semester whose academic year is missing is refused with an InvalidOperationException.</summary>
    [Fact]
    public void Build_ThrowsWhenTheAcademicYearIsMissing()
    {
        var orphan = new Semester { AcademicYearId = "missing-year", Name = "Fall" };
        new SemesterRepository(_db).Insert(orphan);

        Assert.Throws<InvalidOperationException>(() => NewBuilder().Build(orphan.Id));
    }

    /// <summary>The snapshot copies the lists it is given, so later changes to them do not leak in.</summary>
    [Fact]
    public void Snapshot_CopiesTheListsItIsGiven()
    {
        var subjects = new List<Subject> { new() { CalendarAbbreviation = "CHEM" } };
        var snapshot = new ImportSetupSnapshot(
            new Semester { Name = "Fall" }, new AcademicYear { Name = "2026-2027" }, subjects: subjects);

        subjects.Clear();

        Assert.Single(snapshot.Subjects);
        Assert.Empty(snapshot.Courses);
    }
}

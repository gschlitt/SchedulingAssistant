using System.Data.Common;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.Access;
using TermPoint.ViewModels.Management;

namespace TermPoint.Tests;

/// <summary>
/// A real temporary database seeded for the access-setup tests (piece E): four section types (Lecture and Lab look
/// like components; Online and Thesis do not), four courses and a Fall semester loaded into a section store. The write
/// lock is a switch (<see cref="CanWrite"/>), so the read-only behaviour is exercised without a lock file.
/// <para>
/// Seed: BIOL101 has a lecture A and a lab L1; CHEM201 has lectures A and B; PHYS100 has lecture A and lab L1 (Fall);
/// HIST101 has no sections. No course has a stored component list and the setup has not been done.
/// </para>
/// </summary>
internal sealed class AccessSetupRig : IDisposable
{
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>The database fixture.</summary>
    public ExtensionDataDbFixture Fx { get; } = new();

    /// <summary>Whether this "computer" may save (the service's write test).</summary>
    public bool CanWrite { get; set; } = true;

    /// <summary>Section type and tag repository.</summary>
    public SchedulingEnvironmentRepository Env { get; }
    /// <summary>Course repository.</summary>
    public CourseRepository Courses { get; }
    /// <summary>Section repository.</summary>
    public SectionRepository Sections { get; }
    /// <summary>Set-up mark repository.</summary>
    public AccessChecksSetupRepository Setup { get; }
    /// <summary>The loaded semesters' sections.</summary>
    public SectionStore Store { get; } = new();
    /// <summary>The grid change notifier (counts notifications).</summary>
    public GridChangeNotifier Notifier { get; } = new();
    /// <summary>The access state.</summary>
    public AccessState State { get; }

    /// <summary>The Fall semester.</summary>
    public Semester Fall { get; }
    /// <summary>The Winter semester (empty).</summary>
    public Semester Winter { get; }
    /// <summary>The academic year both belong to.</summary>
    public AcademicYear Year { get; }

    /// <summary>Section types.</summary>
    public SchedulingEnvironmentValue Lecture { get; } = new() { Id = "st-lec", Name = "Lecture", SortOrder = 0 };
    /// <summary>Section types.</summary>
    public SchedulingEnvironmentValue Lab { get; } = new() { Id = "st-lab", Name = "Lab", SortOrder = 1 };
    /// <summary>Section types.</summary>
    public SchedulingEnvironmentValue Online { get; } = new() { Id = "st-onl", Name = "Online", SortOrder = 2 };
    /// <summary>Section types.</summary>
    public SchedulingEnvironmentValue Thesis { get; } = new() { Id = "st-the", Name = "Thesis", SortOrder = 3 };

    /// <summary>A plain tag.</summary>
    public SchedulingEnvironmentValue PlainTag { get; } = new() { Id = "tag-plain", Name = "Plain", SortOrder = 0 };
    /// <summary>A watched tag.</summary>
    public SchedulingEnvironmentValue WatchedTag { get; } = new() { Id = "tag-watch", Name = "Watched", SortOrder = 1, IsAccessWatch = true };
    /// <summary>A tag that is not watched yet (for the eye tests).</summary>
    public SchedulingEnvironmentValue CohortTag { get; } = new() { Id = "tag-cohort", Name = "Cohort", SortOrder = 2 };

    /// <summary>BIOL101.</summary>
    public Course Biol { get; } = new() { Id = "c-biol", CalendarCode = "BIOL101", Title = "Biology" };
    /// <summary>CHEM201.</summary>
    public Course Chem { get; } = new() { Id = "c-chem", CalendarCode = "CHEM201", Title = "Chemistry" };
    /// <summary>HIST101 (no sections).</summary>
    public Course Hist { get; } = new() { Id = "c-hist", CalendarCode = "HIST101", Title = "History" };
    /// <summary>PHYS100.</summary>
    public Course Phys { get; } = new() { Id = "c-phys", CalendarCode = "PHYS100", Title = "Physics" };

    /// <summary>Builds and seeds the database.</summary>
    public AccessSetupRig()
    {
        var db = Fx.Db;
        Env = new SchedulingEnvironmentRepository(db);
        Courses = new CourseRepository(db);
        Sections = new SectionRepository(db);
        Setup = new AccessChecksSetupRepository(db);

        foreach (var t in new[] { Lecture, Lab, Online, Thesis })
            Env.Insert(SchedulingEnvironmentTypes.SectionType, t);
        foreach (var t in new[] { PlainTag, WatchedTag, CohortTag })
            Env.Insert(SchedulingEnvironmentTypes.Tag, t);
        foreach (var c in new[] { Biol, Chem, Hist, Phys })
            Courses.Insert(c);

        Year = new AcademicYear { Id = "ay-1", Name = "2026-2027" };
        new AcademicYearRepository(db).Insert(Year);
        Fall = new Semester { Id = "sem-fall", AcademicYearId = Year.Id, Name = "Fall", SortOrder = 1 };
        Winter = new Semester { Id = "sem-winter", AcademicYearId = Year.Id, Name = "Winter", SortOrder = 2 };
        new SemesterRepository(db).Insert(Fall);
        new SemesterRepository(db).Insert(Winter);

        Add(Biol, "A", Lecture); Add(Biol, "L1", Lab);
        Add(Chem, "A", Lecture); Add(Chem, "B", Lecture);
        Add(Phys, "A", Lecture); Add(Phys, "L1", Lab);

        State = AccessStateTestSupport.For(db, Store, Notifier);
        Store.Reload(Sections, [Fall.Id]);
    }

    /// <summary>Adds a Fall section.</summary>
    /// <param name="course">The course.</param>
    /// <param name="code">The section code.</param>
    /// <param name="type">The section type, or null.</param>
    /// <param name="tagIds">Tag ids.</param>
    /// <returns>The section as stored.</returns>
    public Section Add(Course course, string code, SchedulingEnvironmentValue? type, params string[] tagIds)
    {
        var s = new Section
        {
            SemesterId = Fall.Id, CourseId = course.Id, SectionCode = code, SectionTypeId = type?.Id, TagIds = tagIds.ToList(),
        };
        Sections.Insert(s);
        return s;
    }

    /// <summary>The service over the real repositories; <paramref name="setupRepo"/> replaces the set-up mark repository (to inject a failure).</summary>
    /// <param name="setupRepo">The repository to use for the mark, or null for the real one.</param>
    /// <returns>A new service whose write test is <see cref="CanWrite"/>.</returns>
    public AccessSetupService Service(IAccessChecksSetupRepository? setupRepo = null) =>
        new(Fx.Db, Env, Courses, Sections, setupRepo ?? Setup, State, Notifier, () => CanWrite);

    /// <summary>Rows changed so far on the connection (SQLite <c>total_changes()</c>); differences count the rows a write touched.</summary>
    /// <returns>The running total.</returns>
    public long TotalChanges()
    {
        using var cmd = Fx.Db.Connection.CreateCommand();
        cmd.CommandText = "SELECT total_changes()";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>A section type as stored now.</summary>
    /// <param name="type">The seed value.</param>
    /// <returns>The stored value.</returns>
    public SchedulingEnvironmentValue Stored(SchedulingEnvironmentValue type) => Env.GetById(type.Id)!;

    /// <summary>A course as stored now.</summary>
    /// <param name="course">The seed course.</param>
    /// <returns>The stored course.</returns>
    public Course Stored(Course course) => Courses.GetById(course.Id)!;

    /// <inheritdoc/>
    public void Dispose()
    {
        _settingsGuard.Dispose();
        Fx.Dispose();
    }
}

/// <summary>A set-up mark repository that reads normally but fails when asked to write the mark (the last write of the wizard's save).</summary>
internal sealed class FailingSetupRepository(IAccessChecksSetupRepository inner) : IAccessChecksSetupRepository
{
    /// <inheritdoc/>
    public bool IsSetUp() => inner.IsSetUp();

    /// <inheritdoc/>
    public void MarkSetUp(DbTransaction? tx = null) => throw new InvalidOperationException("injected failure");
}

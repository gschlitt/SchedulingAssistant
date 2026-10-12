using System.Text.Json.Nodes;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 3, persistence layer: for every table that has a JSON <c>data</c> column, a row that
/// carries fields this version does not know (stamped in with SQL, the way a newer app version
/// would have written them) is loaded through its repository and saved back with <c>Update</c>
/// (or <c>Save</c>), and the raw <c>data</c> column must still hold those fields. A control run on
/// an untouched row checks the converse: a row that never had unknown fields does not acquire any.
/// </summary>
public sealed class ExtensionDataRepositoryTests : IDisposable
{
    private readonly ExtensionDataDbFixture _fx = new();

    /// <summary>Releases the temp database and lock.</summary>
    public void Dispose() => _fx.Dispose();

    // ── Shared round-trip helper ──────────────────────────────────────────────

    /// <summary>
    /// Inserts <paramref name="entity"/>, proves that updating it unchanged leaves the row free of
    /// unknown fields (control), then stamps the standard future fields into the row, loads the entity
    /// back by id, updates it, and asserts the raw JSON still carries them and still carries the
    /// declared fields.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="table">The table that stores it.</param>
    /// <param name="entity">A fully built entity with its id set.</param>
    /// <param name="getId">Reads the entity's id.</param>
    /// <param name="insert">Inserts a new row.</param>
    /// <param name="getById">Loads the entity by id.</param>
    /// <param name="update">Updates the row from an entity.</param>
    private void AssertRoundTrip<T>(
        string table, T entity, Func<T, string> getId,
        Action<T> insert, Func<string, T?> getById, Action<T> update)
        where T : PersistedRecord
    {
        var id = getId(entity);
        insert(entity);

        // Control: an old-format row with no unknown fields must stay that way.
        var before = _fx.ReadDataText(table, id);
        var control = getById(id) ?? throw new InvalidOperationException($"{table}: inserted row not found");
        Assert.Null(control.ExtensionData);
        update(control);
        Assert.Equal(before, _fx.ReadDataText(table, id));

        // A newer version adds fields; this (older) version loads and re-saves the record.
        _fx.Stamp(table, id);
        var loaded = getById(id)!;
        Assert.NotNull(loaded.ExtensionData);
        update(loaded);

        var raw = _fx.ReadData(table, id);
        FutureFields.AssertPresent(raw, table, "root");
        FutureFields.AssertNoDuplicateKeys(_fx.ReadDataText(table, id));
    }

    // ── One test per table ────────────────────────────────────────────────────

    [Fact]
    public void Subjects_KeepUnknownFields()
    {
        var repo = new SubjectRepository(_fx.Db);
        AssertRoundTrip("Subjects", new Subject { Name = "History", CalendarAbbreviation = "HIST" },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void Courses_KeepUnknownFields()
    {
        var repo = new CourseRepository(_fx.Db);
        AssertRoundTrip("Courses",
            new Course { SubjectId = "subj-1", CalendarCode = "HIST101", Title = "Intro", Capacity = 30, Level = "100" },
            x => x.Id, repo.Insert, repo.GetById, c => repo.Update(c));
    }

    [Fact]
    public void Instructors_KeepUnknownFields()
    {
        var repo = new InstructorRepository(_fx.Db);
        AssertRoundTrip("Instructors", new Instructor { LastName = "Smith", FirstName = "Jo", Initials = "JS" },
            x => x.Id, repo.Insert, repo.GetById, i => repo.Update(i));
    }

    [Fact]
    public void Rooms_KeepUnknownFields()
    {
        var repo = new RoomRepository(_fx.Db);
        AssertRoundTrip("Rooms", new Room { Building = "B", RoomNumber = "101", Capacity = 40 },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void Campuses_KeepUnknownFields()
    {
        var repo = new CampusRepository(_fx.Db);
        AssertRoundTrip("Campuses", new Campus { Name = "Main", Abbreviation = "MC" },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void SchedulingEnvironmentValues_KeepUnknownFields()
    {
        var repo = new SchedulingEnvironmentRepository(_fx.Db);
        AssertRoundTrip("SchedulingEnvironmentValues", new SchedulingEnvironmentValue { Name = "Lab" },
            x => x.Id, v => repo.Insert("roomType", v), repo.GetById, repo.Update);
    }

    [Fact]
    public void BlockPatterns_KeepUnknownFields()
    {
        var repo = new BlockPatternRepository(_fx.Db);
        AssertRoundTrip("BlockPatterns", new BlockPattern { Name = "MWF", Days = [1, 3, 5] },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void AcademicUnits_KeepUnknownFields()
    {
        var repo = new AcademicUnitRepository(_fx.Db);
        AssertRoundTrip("AcademicUnits", new AcademicUnit { Name = "Biology", Abbreviation = "BIOL" },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void AcademicYears_KeepUnknownFields()
    {
        var repo = new AcademicYearRepository(_fx.Db);
        AssertRoundTrip("AcademicYears", new AcademicYear { Name = "2025-2026" },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void Releases_KeepUnknownFields()
    {
        var repo = new ReleaseRepository(_fx.Db);
        AssertRoundTrip("Releases",
            new Release { SemesterId = "sem-1", InstructorId = "inst-1", Title = "Admin", WorkloadValue = 0.5m },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void InstructorCommitments_KeepUnknownFields()
    {
        var repo = new InstructorCommitmentRepository(_fx.Db);
        AssertRoundTrip("InstructorCommitments",
            new InstructorCommitment { InstructorId = "inst-1", SemesterId = "sem-1", Name = "Office hours", Day = 2, StartMinutes = 600, EndMinutes = 660 },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void SectionCodePatterns_KeepUnknownFields()
    {
        var repo = new SectionCodePatternRepository(_fx.Db);
        AssertRoundTrip("SectionCodePatterns", new SectionCodePattern { Name = "Lecture", Prefix = "L", Examples = "L1, L2" },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void Meetings_KeepUnknownFields()
    {
        var repo = new MeetingRepository(_fx.Db);
        AssertRoundTrip("Meetings", new Meeting { SemesterId = "sem-1", Title = "Faculty meeting" },
            x => x.Id, repo.Insert, repo.GetById, repo.Update);
    }

    [Fact]
    public void Sections_KeepUnknownFields()
    {
        var repo = new SectionRepository(_fx.Db);
        AssertRoundTrip("Sections", new Section { SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A" },
            x => x.Id, s => repo.Insert(s), repo.GetById, s => repo.Update(s));
    }

    /// <summary>
    /// A section's nested records each keep their own unknown fields: the meeting, the instructor
    /// assignment and the reserve, each stamped with a different origin marker so a mix-up between
    /// levels (or between siblings) would show.
    /// </summary>
    [Fact]
    public void Sections_KeepUnknownFields_OnEveryNestedRecord()
    {
        var repo = new SectionRepository(_fx.Db);
        var section = new Section
        {
            SemesterId = "sem-1", CourseId = "c-1", SectionCode = "A",
            Schedule = [new() { Day = 1, StartMinutes = 510, DurationMinutes = 90 }, new() { Day = 3, StartMinutes = 510, DurationMinutes = 90 }],
            InstructorAssignments = [new() { InstructorId = "i-1", Workload = 1m }],
            Reserves = [new() { ReserveId = "r-1", Code = 4 }],
        };
        repo.Insert(section);

        _fx.Stamp("Sections", section.Id, "$", "section");
        _fx.Stamp("Sections", section.Id, "$.schedule[0]", "meeting-0");
        _fx.Stamp("Sections", section.Id, "$.schedule[1]", "meeting-1");
        _fx.Stamp("Sections", section.Id, "$.instructorAssignments[0]", "assignment");
        _fx.Stamp("Sections", section.Id, "$.reserves[0]", "reserve");

        repo.Update(repo.GetById(section.Id)!);

        var raw = _fx.ReadData("Sections", section.Id);
        FutureFields.AssertPresent(raw, "section", "section");
        FutureFields.AssertPresent(raw["schedule"]![0], "schedule[0]", "meeting-0");
        FutureFields.AssertPresent(raw["schedule"]![1], "schedule[1]", "meeting-1");
        FutureFields.AssertPresent(raw["instructorAssignments"]![0], "instructorAssignments[0]", "assignment");
        FutureFields.AssertPresent(raw["reserves"]![0], "reserves[0]", "reserve");
        FutureFields.AssertNoDuplicateKeys(_fx.ReadDataText("Sections", section.Id));
    }

    /// <summary>Meetings (events) have the same nested lists and need the same protection.</summary>
    [Fact]
    public void Meetings_KeepUnknownFields_OnNestedRecords()
    {
        var repo = new MeetingRepository(_fx.Db);
        var meeting = new Meeting
        {
            SemesterId = "sem-1", Title = "Committee",
            Schedule = [new() { Day = 2, StartMinutes = 600, DurationMinutes = 60 }],
            InstructorAssignments = [new() { InstructorId = "i-1" }],
        };
        repo.Insert(meeting);
        _fx.Stamp("Meetings", meeting.Id, "$.schedule[0]", "slot");
        _fx.Stamp("Meetings", meeting.Id, "$.instructorAssignments[0]", "attendee");

        repo.Update(repo.GetById(meeting.Id)!);

        var raw = _fx.ReadData("Meetings", meeting.Id);
        FutureFields.AssertPresent(raw["schedule"]![0], "schedule[0]", "slot");
        FutureFields.AssertPresent(raw["instructorAssignments"]![0], "instructorAssignments[0]", "attendee");
    }

    [Fact]
    public void ProgramWatches_KeepUnknownFields()
    {
        var repo = new ProgramWatchRepository(_fx.Db);
        var watch = new ProgramWatch { Name = "CS core", Mode = ProgramWatchMode.Course, CourseIds = ["c-1"] };
        repo.Save("sem-1", watch);

        _fx.Stamp("ProgramWatches", watch.Id);
        var loaded = repo.GetAll("sem-1").Single();
        Assert.NotNull(loaded.ExtensionData);
        repo.Save("sem-1", loaded);

        FutureFields.AssertPresent(_fx.ReadData("ProgramWatches", watch.Id), "ProgramWatches", "root");
    }

    /// <summary>
    /// The note repository's Save is an upsert keyed on (semester, instructor). It is the one
    /// repository whose update path takes a freshly built object, so the caller carries the
    /// unknown fields (see <see cref="TermPoint.ViewModels.Management.SchedulingNotesViewModel"/>): this checks the repository
    /// itself preserves whatever the object carries.
    /// </summary>
    [Fact]
    public void SchedulingNotes_KeepUnknownFields()
    {
        var repo = new SchedulingNoteRepository(_fx.Db);
        var note = new SchedulingNote { SemesterId = "sem-1", InstructorId = "inst-1", Text = "hello" };
        repo.Save(note);

        _fx.Stamp("SchedulingNotes", note.Id);
        var loaded = repo.Get("sem-1", "inst-1")!;
        Assert.NotNull(loaded.ExtensionData);
        repo.Save(loaded);

        FutureFields.AssertPresent(_fx.ReadData("SchedulingNotes", note.Id), "SchedulingNotes", "root");
    }

    // ── Semesters: a special case ─────────────────────────────────────────────

    /// <summary>
    /// The semester's <c>data</c> column holds only <c>{"Color": ...}</c>, written with default
    /// serializer options (PascalCase), unlike every other table. Unknown fields must survive an
    /// update, and the historical shape must not change for rows that have none.
    /// </summary>
    [Fact]
    public void Semesters_KeepUnknownFields_AndTheirHistoricalFormat()
    {
        var repo = new SemesterRepository(_fx.Db);
        var sem = new Semester { AcademicYearId = "ay-1", Name = "Fall", SortOrder = 1, Color = "#C65D1E" };
        repo.Insert(sem);

        // The shape existing databases already have, and still get.
        Assert.Equal("{\"Color\":\"#C65D1E\"}", _fx.ReadDataText("Semesters", sem.Id));

        // Control: an update of a row with no unknown fields rewrites exactly the same JSON.
        var control = repo.GetById(sem.Id)!;
        Assert.Null(control.ExtensionData);
        Assert.Equal("#C65D1E", control.Color);
        repo.Update(control);
        Assert.Equal("{\"Color\":\"#C65D1E\"}", _fx.ReadDataText("Semesters", sem.Id));

        // A newer version adds fields; the older one loads and re-saves the semester.
        _fx.Stamp("Semesters", sem.Id);
        var loaded = repo.GetById(sem.Id)!;
        Assert.NotNull(loaded.ExtensionData);
        Assert.Equal("#C65D1E", loaded.Color);

        loaded.Color = "#112233";           // a normal edit alongside
        repo.Update(loaded);

        var raw = _fx.ReadData("Semesters", sem.Id);
        FutureFields.AssertPresent(raw, "Semesters", "root");
        Assert.Equal("#112233", raw["Color"]!.GetValue<string>());
        FutureFields.AssertNoDuplicateKeys(_fx.ReadDataText("Semesters", sem.Id));
    }

    /// <summary>Malformed or unexpected semester payloads still load with defaults, as before.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Color\":5}")]
    [InlineData("{}")]
    public void Semesters_TolerateOddDataColumns(string data)
    {
        var repo = new SemesterRepository(_fx.Db);
        var sem = new Semester { AcademicYearId = "ay-1", Name = "Winter" };
        repo.Insert(sem);
        using (var cmd = _fx.Db.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE Semesters SET data = $d WHERE id = $id";
            var d = cmd.CreateParameter(); d.ParameterName = "$d"; d.Value = data; cmd.Parameters.Add(d);
            var i = cmd.CreateParameter(); i.ParameterName = "$id"; i.Value = sem.Id; cmd.Parameters.Add(i);
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.GetById(sem.Id);

        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded!.Color);
        Assert.Equal("Winter", loaded.Name);
    }
}

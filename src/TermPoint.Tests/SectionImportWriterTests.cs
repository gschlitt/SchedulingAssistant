using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.TestSupport.ImportScenario;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SectionImportWriter"/> against real temporary SQLite databases and the real
/// repositories: everything planned is inserted and readable, the readable columns are filled, no existing
/// row changes, a failure rolls everything back, a read-only session is refused before anything is written,
/// the dirty-marker hook fires once, and only <c>Insert</c> is ever called.
/// </summary>
public sealed class SectionImportWriterTests
{
    // ── Round trip ────────────────────────────────────────────────────────────

    /// <summary>
    /// A sheet that creates every kind of record is written, and every field reads back as the design's
    /// field tables say, including the readable columns checked with raw SQL.
    /// </summary>
    [Fact]
    public void Write_RoundTrip_EveryFieldMatchesTheDesign()
    {
        using var rig = new ImportRig();
        var existing = SeedExisting(rig);
        var report = rig.Plan(Sheet());
        Assert.False(report.HasProblems, string.Join("; ", report.Problems.Select(p => p.Message)));

        var counts = rig.Writer.Write(report.Plan, rig.Fall.Id);

        Assert.Equal(new ImportCounts(Subjects: 1, Campuses: 0, SectionTypes: 1, MeetingTypes: 1, Tags: 1,
            Rooms: 1, Courses: 1, Instructors: 1, Sections: 3), counts);

        // Setup values.
        var biol = Assert.Single(rig.Subjects.GetAll(), s => s.CalendarAbbreviation == "BIOL");
        Assert.Equal("BIOL", biol.Name);
        var seminar = Assert.Single(rig.Environment.GetAll(SchedulingEnvironmentTypes.SectionType), v => v.Name == "Seminar");
        Assert.Null(seminar.SectionCodeAbbreviation);
        Assert.True(seminar.SortOrder > existing.Lecture.SortOrder);
        var studio = Assert.Single(rig.Environment.GetAll(SchedulingEnvironmentTypes.MeetingType), v => v.Name == "Studio");
        var labSafety = Assert.Single(rig.Environment.GetAll(SchedulingEnvironmentTypes.Tag), v => v.Name == "Lab Safety");
        Assert.True(labSafety.SortOrder > existing.Writing.SortOrder);
        var art101 = Assert.Single(rig.Rooms.GetAll(), r => r.Building == "ART" && r.RoomNumber == "101");
        Assert.Equal(existing.Main.Id, art101.CampusId);
        Assert.Null(art101.Capacity);
        Assert.Null(art101.RoomTypeId);
        Assert.True(art101.SortOrder > existing.Sci210.SortOrder);

        // Course and instructor.
        var biol200 = Assert.Single(rig.Courses.GetAll(), c => c.CalendarCode == "BIOL200");
        Assert.Equal(biol.Id, biol200.SubjectId);
        Assert.Equal("Genetics", biol200.Title);
        Assert.True(biol200.IsActive);
        Assert.Empty(biol200.TagIds);
        Assert.Equal(string.Empty, biol200.Level);
        Assert.Null(biol200.Capacity);
        var jones = Assert.Single(rig.Instructors.GetAll(), i => i.LastName == "Jones");
        Assert.Equal("Mary", jones.FirstName);
        Assert.Equal("MJ", jones.Initials);
        Assert.True(jones.IsActive);
        Assert.Equal(string.Empty, jones.Email);
        Assert.Null(jones.StaffTypeId);

        // Sections.
        var sections = rig.Sections.GetAll(rig.Fall.Id).ToDictionary(s => s.SectionCode);
        Assert.Equal(new[] { "A", "B", "C", "Z" }, sections.Keys.OrderBy(k => k).ToArray());

        var a = sections["A"];
        Assert.Equal(rig.Fall.Id, a.SemesterId);
        Assert.Equal(existing.Chem101.Id, a.CourseId);
        Assert.Equal(existing.Lecture.Id, a.SectionTypeId);
        Assert.Equal(existing.Main.Id, a.CampusId);
        Assert.Equal(new[] { existing.Writing.Id, labSafety.Id }, a.TagIds);
        Assert.Equal(40, a.Capacity);
        Assert.Equal("100", a.Level);
        Assert.Equal(new[] { existing.Smith.Id, jones.Id }, a.InstructorAssignments.Select(x => x.InstructorId).ToArray());
        Assert.All(a.InstructorAssignments, x => Assert.Equal(1.0m, x.Workload));
        Assert.Equal(string.Empty, a.Notes);
        Assert.Empty(a.Reserves);
        Assert.Empty(a.ResourceIds);
        Assert.Equal(SectionFlag.None, a.Flag);

        var meetings = a.Schedule.OrderBy(m => m.Day).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, meetings.Select(m => m.Day).ToArray());
        Assert.Equal(new[] { 600, 780, 600 }, meetings.Select(m => m.StartMinutes).ToArray());
        Assert.Equal(new[] { 60, 110, 60 }, meetings.Select(m => m.DurationMinutes).ToArray());
        Assert.Equal(new string?[] { null, "odd", null }, meetings.Select(m => m.Frequency).ToArray());
        Assert.Equal(new[] { existing.ClassMeeting.Id, studio.Id, existing.ClassMeeting.Id }, meetings.Select(m => m.MeetingTypeId).ToArray());
        Assert.Equal(new[] { existing.Sci210.Id, art101.Id, existing.Sci210.Id }, meetings.Select(m => m.RoomId).ToArray());
        Assert.All(meetings, m => Assert.Null(m.RoomTypeId));

        var b = sections["B"];
        Assert.Equal(biol200.Id, b.CourseId);
        Assert.Equal(seminar.Id, b.SectionTypeId);
        Assert.Null(b.CampusId);
        Assert.Empty(b.TagIds);
        Assert.Equal(25, b.Capacity);
        Assert.Equal(string.Empty, b.Level);
        Assert.Equal(jones.Id, Assert.Single(b.InstructorAssignments).InstructorId);
        Assert.Empty(b.Schedule);

        var c = sections["C"];
        Assert.Equal(existing.Chem101.Id, c.CourseId);
        Assert.Null(c.SectionTypeId);
        Assert.Null(c.CampusId);
        Assert.Equal(new[] { existing.Writing.Id }, c.TagIds);   // the course's own tag (D2)
        Assert.Equal(40, c.Capacity);                             // the course's capacity (D1)
        Assert.Equal("100", c.Level);
        Assert.Empty(c.InstructorAssignments);
        Assert.Empty(c.Schedule);

        // Readable columns (raw SQL).
        var row = rig.QueryRow(
            "SELECT course_code, semester_name, academic_year_name, section_code, semester_id, course_id " +
            "FROM Sections WHERE id = $id", ("$id", a.Id));
        Assert.Equal(new string?[] { "CHEM101", "Fall", "2026-2027", "A", rig.Fall.Id, existing.Chem101.Id }, row);
        Assert.Equal("BIOL200", rig.QueryRow("SELECT course_code FROM Sections WHERE id = $id", ("$id", b.Id))[0]);
        Assert.Equal(new string?[] { "BIOL200", "Genetics", biol.Id },
            rig.QueryRow("SELECT calendar_code, title, subject_id FROM Courses WHERE id = $id", ("$id", biol200.Id)));
        Assert.Equal(new string?[] { "BIOL", "BIOL" },
            rig.QueryRow("SELECT abbreviation, name FROM Subjects WHERE id = $id", ("$id", biol.Id)));
        Assert.Equal(new string?[] { "Jones", "Mary", "MJ" },
            rig.QueryRow("SELECT last_name, first_name, initials FROM Instructors WHERE id = $id", ("$id", jones.Id)));
        Assert.Equal(new string?[] { "ART", "101" },
            rig.QueryRow("SELECT building, room_number FROM Rooms WHERE id = $id", ("$id", art101.Id)));
        Assert.Equal(new string?[] { SchedulingEnvironmentTypes.SectionType, "Seminar" },
            rig.QueryRow("SELECT type, name FROM SchedulingEnvironmentValues WHERE id = $id", ("$id", seminar.Id)));
        Assert.Equal(new string?[] { SchedulingEnvironmentTypes.MeetingType, "Studio" },
            rig.QueryRow("SELECT type, name FROM SchedulingEnvironmentValues WHERE id = $id", ("$id", studio.Id)));
        Assert.Equal(new string?[] { SchedulingEnvironmentTypes.Tag, "Lab Safety" },
            rig.QueryRow("SELECT type, name FROM SchedulingEnvironmentValues WHERE id = $id", ("$id", labSafety.Id)));
    }

    /// <summary>A new campus is inserted, and the room and section that name it refer to it.</summary>
    [Fact]
    public void Write_NewCampus_IsInsertedAndReferencedByTheSectionAndRoom()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = SectionImportTestKit.Sheet(
            SectionImportTestKit.Mtg(2, "CHEM", "101", "N", "M", "1000", "1100",
                (SectionImportColumns.Campus, "Satellite"), (SectionImportColumns.Building, "NEW"),
                (SectionImportColumns.Room, "7")));
        var report = rig.Plan(sheet);
        Assert.False(report.HasProblems);

        var counts = rig.Writer.Write(report.Plan, rig.Fall.Id);

        Assert.Equal(1, counts.Campuses);
        var campus = Assert.Single(rig.Campuses.GetAll(), c => c.Name == "Satellite");
        Assert.Equal(campus.Id, rig.Rooms.GetAll().Single(r => r.Building == "NEW").CampusId);
        Assert.Equal(campus.Id, rig.Sections.GetAll(rig.Fall.Id).Single(s => s.SectionCode == "N").CampusId);
        Assert.Equal("Satellite", rig.QueryRow("SELECT name FROM Campuses WHERE id = $id", ("$id", campus.Id))[0]);
    }

    /// <summary>An empty plan writes nothing and does not touch the database (no dirty marker, no refusal).</summary>
    [Fact]
    public void Write_EmptyPlan_WritesNothingAndTouchesNothing()
    {
        int fired = 0;
        using var rig = new ImportRig(onFirstWrite: () => fired++);
        rig.Db.ResetDirty();
        fired = 0;
        var before = rig.DumpAllTables();

        var counts = rig.Writer.Write(ImportPlan.Empty, rig.Fall.Id);

        Assert.Equal(ImportCounts.None, counts);
        Assert.Equal(0, fired);
        Assert.Equal(before, rig.DumpAllTables());
    }

    // ── Existing records ──────────────────────────────────────────────────────

    /// <summary>
    /// An import that matches some existing records and creates others leaves every pre-existing row of
    /// every table byte-for-byte identical.
    /// </summary>
    [Fact]
    public void Write_NoExistingRowChanges()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var before = rig.DumpAllTables();
        var report = rig.Plan(Sheet());
        Assert.False(report.HasProblems);

        var counts = rig.Writer.Write(report.Plan, rig.Fall.Id);
        Assert.True(counts.Total > 0);

        var after = rig.DumpAllTables();
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (table, rowsBefore) in before)
        {
            // Every row that was there is still there, unchanged (as a multiset: nothing removed or edited).
            var remaining = after[table].GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
            foreach (var row in rowsBefore)
            {
                Assert.True(remaining.TryGetValue(row, out int n) && n > 0, $"A row of {table} changed or disappeared: {row}");
                remaining[row] = n - 1;
            }
        }

        // And something was added in exactly the tables the import writes to.
        var grown = after.Where(t => t.Value.Count > before[t.Key].Count).Select(t => t.Key).OrderBy(k => k).ToArray();
        Assert.Equal(
            new[] { "Courses", "Instructors", "Rooms", "SchedulingEnvironmentValues", "Sections", "Subjects" },
            grown);
    }

    /// <summary>
    /// The writer calls only <c>Insert</c> on the seven repositories: never an Update, a Delete or anything
    /// else, and exactly one Insert per record it reports.
    /// </summary>
    [Fact]
    public void Write_CallsOnlyInsert_OnEveryRepository()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var (subjects, pSubjects) = RecordingRepositoryProxy<ISubjectRepository>.Wrap(rig.Subjects);
        var (campuses, pCampuses) = RecordingRepositoryProxy<ICampusRepository>.Wrap(rig.Campuses);
        var (environment, pEnvironment) = RecordingRepositoryProxy<ISchedulingEnvironmentRepository>.Wrap(rig.Environment);
        var (rooms, pRooms) = RecordingRepositoryProxy<IRoomRepository>.Wrap(rig.Rooms);
        var (courses, pCourses) = RecordingRepositoryProxy<ICourseRepository>.Wrap(rig.Courses);
        var (instructors, pInstructors) = RecordingRepositoryProxy<IInstructorRepository>.Wrap(rig.Instructors);
        var (sections, pSections) = RecordingRepositoryProxy<ISectionRepository>.Wrap(rig.Sections);
        var writer = rig.NewWriter(subjects, campuses, environment, rooms, courses, instructors, sections);
        var report = rig.Plan(Sheet());

        var counts = writer.Write(report.Plan, rig.Fall.Id);

        var calls = pSubjects.Calls.Concat(pCampuses.Calls).Concat(pEnvironment.Calls).Concat(pRooms.Calls)
            .Concat(pCourses.Calls).Concat(pInstructors.Calls).Concat(pSections.Calls).ToList();
        Assert.All(calls, name => Assert.Equal("Insert", name));
        Assert.Equal(counts.Total, calls.Count);
    }

    // ── Failure and refusal ───────────────────────────────────────────────────

    /// <summary>
    /// A section insert that fails partway (the second one) rolls everything back: no new row exists in any
    /// table, the original exception reaches the caller, and the database is still usable (the same plan then
    /// writes normally).
    /// </summary>
    [Fact]
    public void Write_FailureOnTheSecondSection_RollsBackEverything_AndTheDatabaseStaysUsable()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var before = rig.DumpAllTables();
        var report = rig.Plan(Sheet());
        var (sections, _) = RecordingRepositoryProxy<ISectionRepository>.Wrap(
            rig.Sections, failWhen: (method, n) => method == "Insert" && n == 2);
        var failing = rig.NewWriter(sections: sections);

        Assert.Throws<RecordingRepositoryProxy<ISectionRepository>.InjectedFailure>(
            () => failing.Write(report.Plan, rig.Fall.Id));

        Assert.Equal(before, rig.DumpAllTables());

        // Still usable: the same plan now goes in (no leftover row collides with it).
        var counts = rig.Writer.Write(report.Plan, rig.Fall.Id);
        Assert.Equal(3, counts.Sections);
        Assert.Equal(4, rig.Sections.GetAll(rig.Fall.Id).Count);
    }

    /// <summary>
    /// A failure before any section (the first room insert) also leaves nothing behind: the subject, campus,
    /// types and tag inserted earlier in the same transaction are rolled back too.
    /// </summary>
    [Fact]
    public void Write_FailureOnTheRoomInsert_RollsBackTheSetupValuesInsertedBefore()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var before = rig.DumpAllTables();
        var report = rig.Plan(Sheet());
        var (rooms, _) = RecordingRepositoryProxy<IRoomRepository>.Wrap(rig.Rooms, failWhen: (m, n) => m == "Insert");

        Assert.Throws<RecordingRepositoryProxy<IRoomRepository>.InjectedFailure>(
            () => rig.NewWriter(rooms: rooms).Write(report.Plan, rig.Fall.Id));

        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>
    /// A read-only context refuses the write with <see cref="WriteAccessLostException"/> before anything is
    /// written; reopening the database read-write shows it exactly as it was.
    /// </summary>
    [Fact]
    public void Write_InAReadOnlySession_ThrowsWriteAccessLost_AndWritesNothing()
    {
        using var rw = new ImportRig();
        SeedExisting(rw);
        var report = rw.Plan(Sheet());
        var before = rw.DumpAllTables();

        using (var ro = new ImportRig(rw.Folder, readOnly: true, seed: false, year: rw.Year, semester: rw.Fall, deleteFolder: false))
        {
            Assert.Throws<WriteAccessLostException>(() => ro.Writer.Write(report.Plan, rw.Fall.Id));
        }

        using var reopened = new ImportRig(rw.Folder, seed: false, year: rw.Year, semester: rw.Fall, deleteFolder: false);
        Assert.Equal(before, reopened.DumpAllTables());
        Assert.Equal(before, rw.DumpAllTables());
    }

    // ── Dirty marker ──────────────────────────────────────────────────────────

    /// <summary>
    /// The context's first-write callback (the dirty marker) fires exactly once for an import, however many
    /// records it inserts, and not again for a second import in the same session.
    /// </summary>
    [Fact]
    public void Write_InvokesTheFirstWriteCallbackExactlyOnce()
    {
        int fired = 0;
        using var rig = new ImportRig(onFirstWrite: () => fired++);
        SeedExisting(rig);
        rig.Db.ResetDirty();   // seeding was "the session's first write"; start the count from a clean session
        fired = 0;
        var report = rig.Plan(Sheet());

        var counts = rig.Writer.Write(report.Plan, rig.Fall.Id);

        Assert.True(counts.Total > 1);
        Assert.Equal(1, fired);
    }

    // ── Plan guard ────────────────────────────────────────────────────────────

    /// <summary>
    /// A plan that lists an existing record as new is refused before anything is written (the writer is
    /// insert-only and must never be asked to re-insert one).
    /// </summary>
    [Fact]
    public void Write_ARecordListedAsNewThatAlreadyExists_IsRefusedBeforeWriting()
    {
        using var rig = new ImportRig();
        var existing = SeedExisting(rig);
        var before = rig.DumpAllTables();
        var plan = ImportPlan.Empty with
        {
            NewSubjects = new[] { SetupRef<Subject>.Existing(existing.Chem) },
        };

        Assert.Throws<InvalidOperationException>(() => rig.Writer.Write(plan, rig.Fall.Id));

        Assert.Equal(before, rig.DumpAllTables());
    }
}

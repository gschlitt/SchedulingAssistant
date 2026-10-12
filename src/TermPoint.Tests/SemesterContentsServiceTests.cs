using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Moq;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Data.Repositories.Demo;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for step 8a of spec item 8 — the rules for emptying and deleting semesters and academic
/// years, owned by <see cref="SemesterContentsService"/>:
/// <list type="number">
///   <item><description>Empty Semester deletes sections, meetings, releases, commitments,
///   scheduling notes and program watches for that semester only, in one transaction, and is refused
///   for any loaded semester (not just the first of several).</description></item>
///   <item><description>A semester can be deleted only once it is empty (all six tables) and not
///   loaded.</description></item>
///   <item><description>An academic year can be deleted only when none of its semesters is loaded and
///   all of them are empty; it then takes its semesters and legal start times with it, in one
///   transaction.</description></item>
/// </list>
/// The SQLite tests use a real temp-file <see cref="DatabaseContext"/> and real repositories. The
/// last group checks the in-memory Demo repositories (WASM build) behave the same way.
/// </summary>
public sealed class SemesterContentsServiceTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>The six kinds of row tied to a semester; used to seed exactly one kind.</summary>
    public enum Kind { Sections, Meetings, Releases, Commitments, SchedulingNotes, ProgramWatches }

    /// <summary>The six semester-scoped tables, for raw-SQL orphan checks that bypass the repositories.</summary>
    private static readonly string[] SemesterTables =
        ["Sections", "Meetings", "Releases", "InstructorCommitments", "SchedulingNotes", "ProgramWatches"];

    // Distinct counts per kind, so a mix-up between two kinds cannot go unnoticed.
    private static readonly SemesterContents FullContents   = new(3, 2, 4, 5, 6, 7);
    private static readonly SemesterContents SingleOfEach   = new(1, 1, 1, 1, 1, 1);
    private static readonly SemesterContents PairOfEach     = new(2, 2, 2, 2, 2, 2);

    private readonly SettingsFileGuard _settingsGuard;
    private readonly string _tempDir;
    private readonly DatabaseContext _db;

    private readonly SectionRepository _sectionRepo;
    private readonly MeetingRepository _meetingRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SchedulingNoteRepository _noteRepo;
    private readonly ProgramWatchRepository _watchRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly LegalStartTimeRepository _legalRepo;
    private readonly SemesterContext _context;
    private readonly SemesterContentsService _service;

    // Ids of the seeded structure: two academic years of two semesters each.
    private readonly string _ay1 = Guid.NewGuid().ToString();   // "2024-2025"
    private readonly string _ay2 = Guid.NewGuid().ToString();   // "2025-2026"
    private readonly string _fall   = Guid.NewGuid().ToString(); // AY1, FullContents
    private readonly string _winter = Guid.NewGuid().ToString(); // AY1, SingleOfEach
    private readonly string _spring = Guid.NewGuid().ToString(); // AY2, PairOfEach
    private readonly string _summer = Guid.NewGuid().ToString(); // AY2, empty

    /// <summary>
    /// Builds a temp-file database holding two academic years (2024-2025: Fall, Winter;
    /// 2025-2026: Spring, Summer), legal start times for both years, and rows in all six
    /// semester-scoped tables for Fall, Winter and Spring. Summer is left empty.
    /// <see cref="SemesterContext.Reload"/> saves <see cref="AppSettings"/>, so the settings file is
    /// guarded for the life of the fixture.
    /// </summary>
    public SemesterContentsServiceTests()
    {
        _settingsGuard = new SettingsFileGuard();

        _tempDir = Path.Combine(Path.GetTempPath(), $"semcontents_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _db = new DatabaseContext(Path.Combine(_tempDir, "test.db"));

        _sectionRepo = new SectionRepository(_db);
        _meetingRepo = new MeetingRepository(_db);
        _releaseRepo = new ReleaseRepository(_db);
        _commitmentRepo = new InstructorCommitmentRepository(_db);
        _noteRepo = new SchedulingNoteRepository(_db);
        _watchRepo = new ProgramWatchRepository(_db);
        _semesterRepo = new SemesterRepository(_db);
        _ayRepo = new AcademicYearRepository(_db);
        _legalRepo = new LegalStartTimeRepository(_db);
        _context = new SemesterContext();

        _service = CreateService();

        SeedStructure();
        Seed(_fall, FullContents);
        Seed(_winter, SingleOfEach);
        Seed(_spring, PairOfEach);
        // _summer stays empty.
    }

    /// <summary>Closes the database, deletes the temp folder and restores the settings file.</summary>
    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        _settingsGuard.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a service over the real repositories, optionally replacing the program-watch or
    /// academic-year repository (used to inject a failure that must trigger a rollback).
    /// </summary>
    /// <param name="watchRepo">Replacement watch repository, or <c>null</c> for the real one.</param>
    /// <param name="ayRepo">Replacement academic-year repository, or <c>null</c> for the real one.</param>
    private SemesterContentsService CreateService(
        IProgramWatchRepository? watchRepo = null, IAcademicYearRepository? ayRepo = null) =>
        new(_db, _sectionRepo, _meetingRepo, _releaseRepo, _commitmentRepo, _noteRepo,
            watchRepo ?? _watchRepo, _semesterRepo, ayRepo ?? _ayRepo, _legalRepo, _context);

    /// <summary>Inserts the two academic years, their semesters and their legal start times.</summary>
    private void SeedStructure()
    {
        _ayRepo.Insert(new AcademicYear { Id = _ay1, Name = "2024-2025" });
        _ayRepo.Insert(new AcademicYear { Id = _ay2, Name = "2025-2026" });

        _semesterRepo.Insert(new Semester { Id = _fall,   AcademicYearId = _ay1, Name = "Fall",   SortOrder = 0 });
        _semesterRepo.Insert(new Semester { Id = _winter, AcademicYearId = _ay1, Name = "Winter", SortOrder = 1 });
        _semesterRepo.Insert(new Semester { Id = _spring, AcademicYearId = _ay2, Name = "Spring", SortOrder = 0 });
        _semesterRepo.Insert(new Semester { Id = _summer, AcademicYearId = _ay2, Name = "Summer", SortOrder = 1 });

        foreach (var ay in new[] { _ay1, _ay2 })
        {
            _legalRepo.Insert(new LegalStartTime { BlockLength = 1.5, StartTimes = [510, 600] }, ay);
            _legalRepo.Insert(new LegalStartTime { BlockLength = 3.0, StartTimes = [510, 690] }, ay);
        }
    }

    /// <summary>
    /// Inserts the number of rows of each kind given by <paramref name="counts"/> into a semester,
    /// using each kind's own repository.
    /// </summary>
    /// <param name="semesterId">The semester to fill.</param>
    /// <param name="counts">How many rows of each kind to insert.</param>
    private void Seed(string semesterId, SemesterContents counts)
    {
        for (var i = 0; i < counts.Sections; i++)
            _sectionRepo.Insert(new Section { SemesterId = semesterId, SectionCode = $"S{i}" });

        for (var i = 0; i < counts.Meetings; i++)
            _meetingRepo.Insert(new Meeting { SemesterId = semesterId, Title = $"Meeting {i}" });

        for (var i = 0; i < counts.Releases; i++)
            _releaseRepo.Insert(new Release { SemesterId = semesterId, InstructorId = "inst-1", Title = $"Release {i}" });

        for (var i = 0; i < counts.Commitments; i++)
            _commitmentRepo.Insert(new InstructorCommitment
            {
                SemesterId = semesterId, InstructorId = "inst-1", Name = $"Commitment {i}",
                Day = 1, StartMinutes = 480, EndMinutes = 540
            });

        // One scheduling note per (instructor, semester), so vary the instructor.
        for (var i = 0; i < counts.SchedulingNotes; i++)
            _noteRepo.Save(new SchedulingNote { SemesterId = semesterId, InstructorId = $"inst-{i}", Text = "note" });

        for (var i = 0; i < counts.ProgramWatches; i++)
            _watchRepo.Save(semesterId, new ProgramWatch { Name = $"Watch {i}" });
    }

    /// <summary>Returns a <see cref="SemesterContents"/> with 1 in the given kind and 0 elsewhere.</summary>
    /// <param name="kind">The one kind that has a row.</param>
    private static SemesterContents OnlyOne(Kind kind) => new(
        kind == Kind.Sections ? 1 : 0,
        kind == Kind.Meetings ? 1 : 0,
        kind == Kind.Releases ? 1 : 0,
        kind == Kind.Commitments ? 1 : 0,
        kind == Kind.SchedulingNotes ? 1 : 0,
        kind == Kind.ProgramWatches ? 1 : 0);

    /// <summary>
    /// Counts rows referring to a semester by querying the six tables directly, bypassing the
    /// repositories, so orphan checks do not depend on the code under test.
    /// </summary>
    /// <param name="semesterId">The semester id to look for.</param>
    private int RawRowsForSemester(string semesterId)
    {
        var total = 0;
        foreach (var table in SemesterTables)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE semester_id = $sid";
            var p = cmd.CreateParameter();
            p.ParameterName = "$sid";
            p.Value = semesterId;
            cmd.Parameters.Add(p);
            total += Convert.ToInt32(cmd.ExecuteScalar());
        }
        return total;
    }

    /// <summary>Loads the given semesters of an academic year into the context (the "loaded" set).</summary>
    /// <param name="yearId">The academic year the semesters belong to.</param>
    /// <param name="semesterIds">The semesters to load, in any order.</param>
    private void Load(string yearId, params string[] semesterIds) =>
        _context.Reload(_ayRepo, _semesterRepo, yearId, semesterIds.ToHashSet());

    // ── CountContents / IsEmpty ───────────────────────────────────────────────

    /// <summary>Each of the six counts comes from its own table.</summary>
    [Fact]
    public void CountContents_ReturnsTheCountForEachKind()
    {
        Assert.Equal(FullContents, _service.CountContents(_fall));
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));
        Assert.Equal(PairOfEach, _service.CountContents(_spring));
        Assert.Equal(27, _service.CountContents(_fall).Total);
    }

    /// <summary>An empty or unknown semester counts as all zeros and reports empty.</summary>
    [Fact]
    public void CountContents_EmptyAndUnknownSemesters_AreAllZero()
    {
        Assert.True(_service.IsEmpty(_summer));
        Assert.True(_service.IsEmpty("no-such-semester"));
        Assert.Equal(0, _service.CountContents(_summer).Total);
        Assert.False(_service.IsEmpty(_fall));
    }

    /// <summary>A semester is not empty when only one of the six tables has a row.</summary>
    [Theory]
    [InlineData(Kind.Sections)]
    [InlineData(Kind.Meetings)]
    [InlineData(Kind.Releases)]
    [InlineData(Kind.Commitments)]
    [InlineData(Kind.SchedulingNotes)]
    [InlineData(Kind.ProgramWatches)]
    public void IsEmpty_IsFalseWhenAnySingleTableHasARow(Kind kind)
    {
        Seed(_summer, OnlyOne(kind));

        Assert.False(_service.IsEmpty(_summer));
        Assert.Equal(OnlyOne(kind), _service.CountContents(_summer));
    }

    // ── Describe ──────────────────────────────────────────────────────────────

    /// <summary>With nothing attached, the description is the word "nothing".</summary>
    [Fact]
    public void Describe_Empty_IsNothing()
    {
        var empty = new SemesterContents(0, 0, 0, 0, 0, 0);

        Assert.True(empty.IsEmpty);
        Assert.Equal("nothing", empty.Describe());
    }

    /// <summary>Only the non-zero kinds appear, in a fixed order, with correct singular/plural.</summary>
    [Theory]
    [InlineData(1, 0, 0, 0, 0, 0, "1 section")]
    [InlineData(12, 3, 1, 4, 0, 0, "12 sections, 3 meetings, 1 release, 4 scheduled commitments")]
    [InlineData(0, 1, 2, 0, 0, 0, "1 meeting, 2 releases")]
    [InlineData(0, 0, 0, 1, 2, 1, "1 scheduled commitment, 2 scheduling notes, 1 program watch")]
    [InlineData(0, 0, 0, 0, 1, 3, "1 scheduling note, 3 program watches")]
    [InlineData(1, 1, 1, 1, 1, 1, "1 section, 1 meeting, 1 release, 1 scheduled commitment, 1 scheduling note, 1 program watch")]
    [InlineData(2, 2, 2, 2, 2, 2, "2 sections, 2 meetings, 2 releases, 2 scheduled commitments, 2 scheduling notes, 2 program watches")]
    public void Describe_NamesOnlyNonZeroKinds_WithSingularAndPlural(
        int sections, int meetings, int releases, int commitments, int notes, int watches, string expected)
    {
        var contents = new SemesterContents(sections, meetings, releases, commitments, notes, watches);

        Assert.Equal(expected, contents.Describe());
        Assert.False(contents.IsEmpty);
    }

    // ── EmptySemester ─────────────────────────────────────────────────────────

    /// <summary>
    /// Emptying one semester removes all six kinds of row for it, leaves every other semester
    /// (same year and other year) untouched, and keeps the semester row and the legal start times.
    /// </summary>
    [Fact]
    public void EmptySemester_RemovesAllSixKinds_ForThatSemesterOnly()
    {
        _service.EmptySemester(_fall);

        Assert.True(_service.IsEmpty(_fall));
        Assert.Equal(0, RawRowsForSemester(_fall));

        // Other semesters, in the same year and in the other year, are untouched.
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));
        Assert.Equal(PairOfEach, _service.CountContents(_spring));

        // The semester row itself is not deleted by emptying it.
        Assert.NotNull(_semesterRepo.GetById(_fall));
        Assert.Equal(2, _legalRepo.GetAll(_ay1).Count);
    }

    /// <summary>Emptying an already-empty semester is harmless.</summary>
    [Fact]
    public void EmptySemester_OnEmptySemester_Succeeds()
    {
        _service.EmptySemester(_summer);

        Assert.True(_service.IsEmpty(_summer));
        Assert.NotNull(_semesterRepo.GetById(_summer));
    }

    /// <summary>
    /// A failure part-way through (the program-watch delete, which runs last) rolls back the
    /// deletes already made for sections, meetings, releases, commitments and notes, so the
    /// semester is exactly as it was, and the original exception reaches the caller.
    /// </summary>
    [Fact]
    public void EmptySemester_FailureMidway_RollsBackEverything()
    {
        var failingWatches = new Mock<IProgramWatchRepository>();
        failingWatches
            .Setup(w => w.DeleteBySemesterId(It.IsAny<string>(), It.IsAny<DbTransaction?>()))
            .Throws(new IOException("simulated watch failure"));
        var service = CreateService(watchRepo: failingWatches.Object);

        var ex = Assert.Throws<IOException>(() => service.EmptySemester(_fall));
        Assert.Equal("simulated watch failure", ex.Message);

        // Everything deleted earlier in the same transaction is back.
        Assert.Equal(FullContents, _service.CountContents(_fall));
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));

        // The connection is not left inside a pending transaction: a normal empty now works.
        _service.EmptySemester(_fall);
        Assert.True(_service.IsEmpty(_fall));
    }

    /// <summary>Emptying the loaded semester is refused and deletes nothing.</summary>
    [Fact]
    public void EmptySemester_RefusesTheLoadedSemester()
    {
        Load(_ay1, _fall);

        var ex = Assert.Throws<InvalidOperationException>(() => _service.EmptySemester(_fall));

        Assert.Equal("\"Fall\" is currently loaded. Switch to a different semester in the main view before emptying it.",
            ex.Message);
        Assert.Equal(FullContents, _service.CountContents(_fall));
    }

    /// <summary>
    /// In multi-semester mode every selected semester is loaded, not just the first. Winter is the
    /// second of two loaded semesters and must still be protected, while an unloaded semester in
    /// another year can be emptied.
    /// </summary>
    [Fact]
    public void EmptySemester_RefusesALoadedSemesterThatIsNotTheFirstSelected()
    {
        Load(_ay1, _fall, _winter);
        Assert.True(_context.IsMultiSemesterMode);
        Assert.Equal(_fall, _context.SelectedSemesterDisplay!.Semester.Id);   // Winter is not the first

        Assert.Throws<InvalidOperationException>(() => _service.EmptySemester(_winter));
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));

        _service.EmptySemester(_spring);                                      // not loaded: allowed
        Assert.True(_service.IsEmpty(_spring));
    }

    // ── IsLoaded / AcademicYearHasLoadedSemester ──────────────────────────────

    /// <summary>With nothing selected, nothing is loaded.</summary>
    [Fact]
    public void IsLoaded_NothingSelected_IsFalse()
    {
        Assert.False(_service.IsLoaded(_fall));
        Assert.False(_service.AcademicYearHasLoadedSemester(_ay1));
    }

    /// <summary>Loaded means "any of the selected semesters", and the year check follows the semesters.</summary>
    [Fact]
    public void IsLoaded_MatchesAnySelectedSemester()
    {
        Load(_ay1, _fall, _winter);

        Assert.True(_service.IsLoaded(_fall));
        Assert.True(_service.IsLoaded(_winter));
        Assert.False(_service.IsLoaded(_spring));
        Assert.True(_service.AcademicYearHasLoadedSemester(_ay1));
        Assert.False(_service.AcademicYearHasLoadedSemester(_ay2));
    }

    // ── WhyCannotDeleteSemester / DeleteSemester ──────────────────────────────

    /// <summary>
    /// A semester with a row in just one of the six tables cannot be deleted. The refusal says
    /// what remains and points to Empty Semester, and <see cref="SemesterContentsService.DeleteSemester"/>
    /// throws the same text and leaves the semester in place.
    /// </summary>
    [Theory]
    [InlineData(Kind.Sections, "1 section")]
    [InlineData(Kind.Meetings, "1 meeting")]
    [InlineData(Kind.Releases, "1 release")]
    [InlineData(Kind.Commitments, "1 scheduled commitment")]
    [InlineData(Kind.SchedulingNotes, "1 scheduling note")]
    [InlineData(Kind.ProgramWatches, "1 program watch")]
    public void DeleteSemester_IsRefusedWhileAnySingleTableHasARow(Kind kind, string remaining)
    {
        Seed(_summer, OnlyOne(kind));
        var expected = $"\"Summer\" still contains {remaining}. " +
                       "Use Empty Semester to remove everything before deleting it.";

        Assert.Equal(expected, _service.WhyCannotDeleteSemester(_summer));
        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteSemester(_summer));
        Assert.Equal(expected, ex.Message);
        Assert.NotNull(_semesterRepo.GetById(_summer));
    }

    /// <summary>A multi-kind refusal lists every kind that remains.</summary>
    [Fact]
    public void WhyCannotDeleteSemester_ListsEverythingThatRemains()
    {
        Assert.Equal(
            "\"Fall\" still contains 3 sections, 2 meetings, 4 releases, 5 scheduled commitments, " +
            "6 scheduling notes, 7 program watches. Use Empty Semester to remove everything before deleting it.",
            _service.WhyCannotDeleteSemester(_fall));
    }

    /// <summary>A loaded semester cannot be deleted even when it is empty.</summary>
    [Fact]
    public void DeleteSemester_IsRefusedWhileLoaded_EvenWhenEmpty()
    {
        Load(_ay2, _summer);
        var expected = "\"Summer\" is currently loaded. " +
                       "Switch to a different semester in the main view before deleting it.";

        Assert.True(_service.IsEmpty(_summer));
        Assert.Equal(expected, _service.WhyCannotDeleteSemester(_summer));
        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteSemester(_summer));
        Assert.Equal(expected, ex.Message);
        Assert.NotNull(_semesterRepo.GetById(_summer));
    }

    /// <summary>A semester that is empty and not loaded can be deleted; nothing else is touched.</summary>
    [Fact]
    public void DeleteSemester_EmptyAndNotLoaded_IsAllowedAndDeletesOnlyThatSemester()
    {
        Assert.Null(_service.WhyCannotDeleteSemester(_summer));

        _service.DeleteSemester(_summer);

        Assert.Null(_semesterRepo.GetById(_summer));
        Assert.NotNull(_semesterRepo.GetById(_spring));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay1).Count);
        Assert.NotNull(_ayRepo.GetById(_ay2));
    }

    /// <summary>
    /// The intended workflow: Empty Semester, then Delete. Afterwards no row in any of the six
    /// tables refers to the deleted semester (no orphans).
    /// </summary>
    [Fact]
    public void EmptyThenDeleteSemester_LeavesNoOrphans()
    {
        _service.EmptySemester(_fall);
        _service.DeleteSemester(_fall);

        Assert.Null(_semesterRepo.GetById(_fall));
        Assert.Equal(0, RawRowsForSemester(_fall));
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));
    }

    // ── WhyCannotDeleteAcademicYear / DeleteAcademicYear ──────────────────────

    /// <summary>
    /// While any semester of the year is non-empty the year cannot be deleted. The refusal names
    /// each non-empty semester with what it contains, and points to Empty Semester.
    /// </summary>
    [Fact]
    public void DeleteAcademicYear_IsRefusedWhileAnySemesterIsNotEmpty()
    {
        var expected =
            $"\"2024-2025\" can't be deleted because it still contains: Fall ({FullContents.Describe()}); " +
            $"Winter ({SingleOfEach.Describe()}). " +
            "Use Empty Semester to remove everything from each of these semesters before deleting the year.";

        Assert.Equal(expected, _service.WhyCannotDeleteAcademicYear(_ay1));
        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteAcademicYear(_ay1));
        Assert.Equal(expected, ex.Message);

        // Nothing was deleted.
        Assert.NotNull(_ayRepo.GetById(_ay1));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay1).Count);
        Assert.Equal(2, _legalRepo.GetAll(_ay1).Count);
        Assert.Equal(FullContents, _service.CountContents(_fall));
    }

    /// <summary>Semesters that are already empty are not listed in the refusal.</summary>
    [Fact]
    public void WhyCannotDeleteAcademicYear_ListsOnlyTheNonEmptySemesters()
    {
        _service.EmptySemester(_winter);

        var message = _service.WhyCannotDeleteAcademicYear(_ay1);

        Assert.NotNull(message);
        Assert.Contains("Fall (", message);
        Assert.DoesNotContain("Winter", message);
    }

    /// <summary>
    /// A single non-empty table in a single semester is enough to refuse the year
    /// (the year's other semester is empty).
    /// </summary>
    [Fact]
    public void WhyCannotDeleteAcademicYear_RefusesOnOneRowInOneSemester()
    {
        _service.EmptySemester(_spring);
        Assert.Null(_service.WhyCannotDeleteAcademicYear(_ay2));

        Seed(_summer, OnlyOne(Kind.ProgramWatches));

        Assert.Equal(
            "\"2025-2026\" can't be deleted because it still contains: Summer (1 program watch). " +
            "Use Empty Semester to remove everything from each of these semesters before deleting the year.",
            _service.WhyCannotDeleteAcademicYear(_ay2));
    }

    /// <summary>
    /// A year that holds a loaded semester cannot be deleted. The refusal names the semester and
    /// says to switch, and takes precedence over the non-empty list.
    /// </summary>
    [Fact]
    public void DeleteAcademicYear_IsRefusedWhileOneOfItsSemestersIsLoaded()
    {
        Load(_ay1, _winter);   // Winter is also non-empty; the loaded message wins
        var expected = "\"2024-2025\" is currently loaded in the main view. " +
                       "Switch to a different academic year there before deleting it.";

        Assert.Equal(expected, _service.WhyCannotDeleteAcademicYear(_ay1));
        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteAcademicYear(_ay1));
        Assert.Equal(expected, ex.Message);
        Assert.NotNull(_ayRepo.GetById(_ay1));
    }

    /// <summary>
    /// Loaded EMPTY semesters still block their year. The message is about the year being loaded,
    /// not about any one semester, because the year is what the user is trying to delete.
    /// </summary>
    [Fact]
    public void WhyCannotDeleteAcademicYear_LoadedEmptySemesters_StillBlockTheYear()
    {
        _service.EmptySemester(_spring);
        Load(_ay2, _spring, _summer);   // both empty, both loaded

        Assert.Equal(
            "\"2025-2026\" is currently loaded in the main view. " +
            "Switch to a different academic year there before deleting it.",
            _service.WhyCannotDeleteAcademicYear(_ay2));
    }

    /// <summary>
    /// A loaded semester in a DIFFERENT year does not block this year: with all of its semesters
    /// empty, the year is deleted along with its semesters and legal start times, and the other
    /// year (semesters, contents, legal start times) is left intact.
    /// </summary>
    [Fact]
    public void DeleteAcademicYear_AllSemestersEmpty_DeletesYearSemestersAndLegalStartTimes()
    {
        _service.EmptySemester(_spring);
        Load(_ay1, _fall);   // a semester of the OTHER year is loaded
        Assert.Null(_service.WhyCannotDeleteAcademicYear(_ay2));

        _service.DeleteAcademicYear(_ay2);

        // Year 2 is gone, with its semesters and legal start times.
        Assert.Null(_ayRepo.GetById(_ay2));
        Assert.Empty(_semesterRepo.GetByAcademicYear(_ay2));
        Assert.Empty(_legalRepo.GetAll(_ay2));
        Assert.Equal(0, RawRowsForSemester(_spring));
        Assert.Equal(0, RawRowsForSemester(_summer));

        // Year 1 is intact.
        Assert.NotNull(_ayRepo.GetById(_ay1));
        Assert.Equal(new[] { _fall, _winter }, _semesterRepo.GetByAcademicYear(_ay1).Select(s => s.Id).ToArray());
        Assert.Equal(2, _legalRepo.GetAll(_ay1).Count);
        Assert.Equal(FullContents, _service.CountContents(_fall));
        Assert.Equal(SingleOfEach, _service.CountContents(_winter));
    }

    /// <summary>A year with no semesters at all can be deleted, taking its legal start times with it.</summary>
    [Fact]
    public void DeleteAcademicYear_WithNoSemesters_IsAllowed()
    {
        var lonely = Guid.NewGuid().ToString();
        _ayRepo.Insert(new AcademicYear { Id = lonely, Name = "2030-2031" });
        _legalRepo.Insert(new LegalStartTime { BlockLength = 1.5, StartTimes = [510] }, lonely);

        Assert.Null(_service.WhyCannotDeleteAcademicYear(lonely));
        _service.DeleteAcademicYear(lonely);

        Assert.Null(_ayRepo.GetById(lonely));
        Assert.Empty(_legalRepo.GetAll(lonely));
    }

    /// <summary>
    /// If the last step of deleting a year fails (the academic-year row delete), the legal start
    /// times and semesters already deleted in the same transaction come back.
    /// </summary>
    [Fact]
    public void DeleteAcademicYear_FailureMidway_RollsBackEverything()
    {
        _service.EmptySemester(_spring);

        var failingYears = new Mock<IAcademicYearRepository>();
        failingYears
            .Setup(y => y.Delete(It.IsAny<string>(), It.IsAny<DbTransaction?>()))
            .Throws(new IOException("simulated year failure"));
        var service = CreateService(ayRepo: failingYears.Object);

        var ex = Assert.Throws<IOException>(() => service.DeleteAcademicYear(_ay2));
        Assert.Equal("simulated year failure", ex.Message);

        Assert.NotNull(_ayRepo.GetById(_ay2));
        Assert.Equal(2, _semesterRepo.GetByAcademicYear(_ay2).Count);
        Assert.Equal(2, _legalRepo.GetAll(_ay2).Count);

        // No pending transaction is left behind: the real service can now do it.
        _service.DeleteAcademicYear(_ay2);
        Assert.Null(_ayRepo.GetById(_ay2));
    }

    // ── Demo repositories (WASM build) ────────────────────────────────────────

    /// <summary>Demo releases count and delete by semester, leaving other semesters alone.</summary>
    [Fact]
    public void Demo_Releases_CountAndDeleteBySemesterId()
    {
        var repo = new DemoReleaseRepository();
        Assert.Equal(1, repo.CountBySemesterId("demo-sem-1"));     // seeded from DemoData
        repo.Insert(new Release { SemesterId = "other-sem", InstructorId = "i", Title = "R" });

        repo.DeleteBySemesterId("demo-sem-1");

        Assert.Equal(0, repo.CountBySemesterId("demo-sem-1"));
        Assert.Equal(1, repo.CountBySemesterId("other-sem"));
    }

    /// <summary>Demo commitments count and delete by semester, across all instructors.</summary>
    [Fact]
    public void Demo_Commitments_CountAndDeleteBySemesterId()
    {
        var repo = new DemoInstructorCommitmentRepository();
        Assert.Equal(1, repo.CountBySemesterId("demo-sem-1"));     // seeded from DemoData
        repo.Insert(new InstructorCommitment { SemesterId = "demo-sem-1", InstructorId = "someone-else" });
        repo.Insert(new InstructorCommitment { SemesterId = "other-sem", InstructorId = "i" });
        Assert.Equal(2, repo.CountBySemesterId("demo-sem-1"));

        repo.DeleteBySemesterId("demo-sem-1");

        Assert.Equal(0, repo.CountBySemesterId("demo-sem-1"));
        Assert.Equal(1, repo.CountBySemesterId("other-sem"));
    }

    /// <summary>Demo scheduling notes count and delete by semester, across all instructors.</summary>
    [Fact]
    public void Demo_SchedulingNotes_CountAndDeleteBySemesterId()
    {
        var repo = new DemoSchedulingNoteRepository();
        repo.Save(new SchedulingNote { SemesterId = "sem-a", InstructorId = "i1", Text = "x" });
        repo.Save(new SchedulingNote { SemesterId = "sem-a", InstructorId = "i2", Text = "y" });
        repo.Save(new SchedulingNote { SemesterId = "sem-b", InstructorId = "i1", Text = "z" });
        Assert.Equal(2, repo.CountBySemesterId("sem-a"));

        repo.DeleteBySemesterId("sem-a");

        Assert.Equal(0, repo.CountBySemesterId("sem-a"));
        Assert.Equal(1, repo.CountBySemesterId("sem-b"));
    }

    /// <summary>Demo program watches count and delete by semester.</summary>
    [Fact]
    public void Demo_ProgramWatches_CountAndDeleteBySemesterId()
    {
        var repo = new DemoProgramWatchRepository();
        repo.Save("sem-a", new ProgramWatch { Name = "W1" });
        repo.Save("sem-a", new ProgramWatch { Name = "W2" });
        repo.Save("sem-b", new ProgramWatch { Name = "W3" });
        Assert.Equal(2, repo.CountBySemesterId("sem-a"));

        repo.DeleteBySemesterId("sem-a");

        Assert.Equal(0, repo.CountBySemesterId("sem-a"));
        Assert.Equal(1, repo.CountBySemesterId("sem-b"));
    }

    /// <summary>Demo sections and meetings accept the (ignored) transaction argument and delete by semester.</summary>
    [Fact]
    public void Demo_SectionsAndMeetings_DeleteBySemesterId_AcceptsNullTransaction()
    {
        var sections = new DemoSectionRepository();
        var meetings = new DemoMeetingRepository();
        Assert.True(sections.CountBySemesterId("demo-sem-1") > 0);
        var otherSemesterSections = sections.CountBySemesterId("demo-sem-3");
        Assert.True(otherSemesterSections > 0);
        Assert.Equal(1, meetings.CountBySemesterId("demo-sem-1"));

        sections.DeleteBySemesterId("demo-sem-1", null);
        meetings.DeleteBySemesterId("demo-sem-1", null);

        Assert.Equal(0, sections.CountBySemesterId("demo-sem-1"));
        Assert.Equal(otherSemesterSections, sections.CountBySemesterId("demo-sem-3"));
        Assert.Equal(0, meetings.CountBySemesterId("demo-sem-1"));
    }

    /// <summary>Demo legal start times delete by academic year, leaving other years alone.</summary>
    [Fact]
    public void Demo_LegalStartTimes_DeleteByAcademicYear()
    {
        var repo = new DemoLegalStartTimeRepository();
        Assert.NotEmpty(repo.GetAll("demo-ay-1"));                 // seeded from DemoData
        repo.Insert(new LegalStartTime { BlockLength = 2.0, StartTimes = [600] }, "other-ay");

        repo.DeleteByAcademicYear("demo-ay-1");

        Assert.Empty(repo.GetAll("demo-ay-1"));
        Assert.Single(repo.GetAll("other-ay"));
    }

    /// <summary>Demo semester and academic-year deletes accept the (ignored) transaction argument.</summary>
    [Fact]
    public void Demo_SemesterAndAcademicYearDeletes_AcceptNullTransaction()
    {
        var semesters = new DemoSemesterRepository();
        var years = new DemoAcademicYearRepository();

        semesters.Delete("demo-sem-5", null);
        Assert.Null(semesters.GetById("demo-sem-5"));
        Assert.Equal(4, semesters.GetByAcademicYear("demo-ay-1").Count);

        semesters.DeleteByAcademicYear("demo-ay-1", null);
        Assert.Empty(semesters.GetByAcademicYear("demo-ay-1"));

        years.Delete("demo-ay-1", null);
        Assert.Null(years.GetById("demo-ay-1"));
    }

    /// <summary>
    /// The service runs end to end over the Demo repositories and the no-transaction
    /// <see cref="DemoDatabaseContext"/> (nullable-tx path, no <see cref="NotSupportedException"/>
    /// from the connection), enforcing the same rules as the SQLite path.
    /// </summary>
    [Fact]
    public void Demo_Service_EnforcesTheSameRules()
    {
        var context = new SemesterContext();   // nothing loaded
        var semesters = new DemoSemesterRepository();
        var years = new DemoAcademicYearRepository();
        var legalStartTimes = new DemoLegalStartTimeRepository();
        var service = new SemesterContentsService(
            new DemoDatabaseContext(), new DemoSectionRepository(), new DemoMeetingRepository(),
            new DemoReleaseRepository(), new DemoInstructorCommitmentRepository(),
            new DemoSchedulingNoteRepository(), new DemoProgramWatchRepository(),
            semesters, years, legalStartTimes, context);

        // Fall (demo-sem-1) holds sections, a meeting, a release and a commitment.
        var before = service.CountContents("demo-sem-1");
        Assert.True(before.Sections > 0);
        Assert.Equal(1, before.Meetings);
        Assert.Equal(1, before.Releases);
        Assert.Equal(1, before.Commitments);
        Assert.NotNull(service.WhyCannotDeleteSemester("demo-sem-1"));
        Assert.NotNull(service.WhyCannotDeleteAcademicYear("demo-ay-1"));

        service.EmptySemester("demo-sem-1");
        Assert.True(service.IsEmpty("demo-sem-1"));
        Assert.False(service.IsEmpty("demo-sem-3"));                // Early Summer still has sections
        Assert.Null(service.WhyCannotDeleteSemester("demo-sem-1"));

        service.DeleteSemester("demo-sem-1");
        Assert.Null(semesters.GetById("demo-sem-1"));
        Assert.Throws<InvalidOperationException>(() => service.DeleteAcademicYear("demo-ay-1"));

        // The year can only go once every one of its semesters has been emptied.
        foreach (var semester in semesters.GetByAcademicYear("demo-ay-1"))
            service.EmptySemester(semester.Id);
        service.DeleteAcademicYear("demo-ay-1");
        Assert.Null(years.GetById("demo-ay-1"));
        Assert.Empty(semesters.GetByAcademicYear("demo-ay-1"));
        Assert.Empty(legalStartTimes.GetAll("demo-ay-1"));
    }
}

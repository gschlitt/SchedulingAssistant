using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Verifies that the per-card room-conflict warning shown in the Section List is
/// suppressed when more than one semester is selected (multi-semester mode), mirroring
/// the disabled room availability browser. Reasoning about room collisions across
/// multiple — possibly non-overlapping — semesters is not well-defined, so the app shows
/// no warnings rather than misleading ones.
///
/// <para>
/// The guard lives at the top of <c>SectionListViewModel.ApplyRoomConflicts</c>: in
/// multi-semester mode it clears <see cref="SectionListItemViewModel.RoomConflictWarning"/>
/// on every card and returns before running <see cref="RoomConflictService"/>.
/// </para>
///
/// <para>
/// Each test wires a real <see cref="SectionListViewModel"/> to a temp-file SQLite
/// database, inserts two sections that share a room+day+time within a semester (a genuine
/// conflict), and drives a load by toggling the <see cref="SemesterContext"/> selection.
/// </para>
/// </summary>
public sealed class RoomConflictMultiSemesterTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly DatabaseContext _db;
    private readonly WriteLockService _lock;

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly LegalStartTimeRepository _legalStartTimeRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly BlockPatternRepository _blockPatternRepo;
    private readonly SectionCodePatternRepository _codePatternRepo;
    private readonly SchedulingEnvironmentRepository _propertyRepo;
    private readonly CampusRepository _campusRepo;
    private readonly MeetingRepository _meetingRepo;
    private readonly AcademicYearRepository _ayRepo;

    private readonly SemesterContext _semesterContext;
    private readonly SectionStore _sectionStore;
    private readonly IDialogService _dialog;

    public RoomConflictMultiSemesterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"conflict_multisem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _db = new DatabaseContext(Path.Combine(_tempDir, "test.db"));

        _lock = new WriteLockService(); // reader mode is sufficient for loading + conflict detection

        _sectionRepo = new SectionRepository(_db);
        _courseRepo = new CourseRepository(_db);
        _subjectRepo = new SubjectRepository(_db);
        _instructorRepo = new InstructorRepository(_db);
        _roomRepo = new RoomRepository(_db);
        _legalStartTimeRepo = new LegalStartTimeRepository(_db);
        _semesterRepo = new SemesterRepository(_db);
        _blockPatternRepo = new BlockPatternRepository(_db);
        _codePatternRepo = new SectionCodePatternRepository(_db);
        _propertyRepo = new SchedulingEnvironmentRepository(_db);
        _campusRepo = new CampusRepository(_db);
        _meetingRepo = new MeetingRepository(_db);
        _ayRepo = new AcademicYearRepository(_db);

        _semesterContext = new SemesterContext();
        _sectionStore = new SectionStore();
        _dialog = new NullDialogService();
    }

    public void Dispose()
    {
        _lock.Dispose();
        _db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class NullDialogService : IDialogService
    {
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") => Task.FromResult(false);
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>Creates a fully-wired <see cref="SectionListViewModel"/> in reader mode.</summary>
    private SectionListViewModel CreateVm() =>
        new(_sectionRepo, _courseRepo, _subjectRepo, _instructorRepo, _roomRepo,
            _legalStartTimeRepo, _semesterRepo, _blockPatternRepo, _codePatternRepo,
            _semesterContext, _sectionStore, _propertyRepo, _campusRepo, _meetingRepo,
            _dialog, _lock);

    /// <summary>
    /// Inserts one academic year plus two semesters under it, returning their IDs.
    /// </summary>
    private (string YearId, string Sem1, string Sem2) SeedYearAndSemesters()
    {
        var year = new AcademicYear { Id = Guid.NewGuid().ToString(), Name = "2025-2026" };
        _ayRepo.Insert(year);

        var sem1 = new Semester { Id = Guid.NewGuid().ToString(), AcademicYearId = year.Id, Name = "Fall", SortOrder = 0 };
        var sem2 = new Semester { Id = Guid.NewGuid().ToString(), AcademicYearId = year.Id, Name = "Summer", SortOrder = 1 };
        _semesterRepo.Insert(sem1);
        _semesterRepo.Insert(sem2);

        return (year.Id, sem1.Id, sem2.Id);
    }

    /// <summary>
    /// Inserts two sections into <paramref name="semesterId"/> that share the same room,
    /// day, and time — i.e. a genuine room conflict within that semester.
    /// </summary>
    private void SeedConflictingPair(string semesterId, string codePrefix)
    {
        foreach (var code in new[] { codePrefix + "A", codePrefix + "B" })
        {
            _sectionRepo.Insert(new Section
            {
                Id = Guid.NewGuid().ToString(),
                SemesterId = semesterId,
                SectionCode = code,
                Schedule =
                [
                    new SectionDaySchedule
                    {
                        Day = 1,             // Mon
                        StartMinutes = 480,  // 08:00
                        DurationMinutes = 60,
                        RoomId = "r1"
                    }
                ]
            });
        }
    }

    /// <summary>
    /// Loads academic-year / semester data into the context and selects the given semesters.
    /// Setting the selection cascades through the VM: it refreshes the shared
    /// <see cref="SectionStore"/> for the new set, which fires SectionsChanged → the VM
    /// reloads and runs conflict detection.
    /// </summary>
    private void SelectSemesters(string yearId, params string[] semesterIds) =>
        _semesterContext.Reload(_ayRepo, _semesterRepo, yearId, semesterIds.ToHashSet());

    private static IReadOnlyList<SectionListItemViewModel> Cards(SectionListViewModel vm) =>
        vm.SectionItems.OfType<SectionListItemViewModel>().ToList();

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Control test: in single-semester mode the two co-located sections are flagged,
    /// confirming the detection path actually runs and produces warnings.
    /// </summary>
    [Fact]
    public void SingleSemester_ConflictsAreFlagged()
    {
        var (yearId, sem1, _) = SeedYearAndSemesters();
        SeedConflictingPair(sem1, "S1");

        var vm = CreateVm();
        SelectSemesters(yearId, sem1);

        var cards = Cards(vm);
        Assert.Equal(2, cards.Count);
        Assert.False(_semesterContext.IsMultiSemesterMode);
        Assert.All(cards, c => Assert.False(string.IsNullOrEmpty(c.RoomConflictWarning),
            "Each conflicting section should carry a room-conflict warning in single-semester mode."));
    }

    /// <summary>
    /// In multi-semester mode the warnings are suppressed even though each semester
    /// contains a genuine within-semester conflict.
    /// </summary>
    [Fact]
    public void MultiSemester_ConflictsAreSuppressed()
    {
        var (yearId, sem1, sem2) = SeedYearAndSemesters();
        SeedConflictingPair(sem1, "S1");
        SeedConflictingPair(sem2, "S2");

        var vm = CreateVm();
        SelectSemesters(yearId, sem1, sem2);

        var cards = Cards(vm);
        Assert.Equal(4, cards.Count); // two conflicting pairs, both semesters loaded
        Assert.True(_semesterContext.IsMultiSemesterMode);
        Assert.All(cards, c => Assert.True(string.IsNullOrEmpty(c.RoomConflictWarning),
            "No room-conflict warnings should appear while multiple semesters are selected."));
    }

    /// <summary>
    /// Adding a second semester after warnings were already shown must wipe the stale
    /// warnings — this is why the guard clears rather than merely returning early.
    /// </summary>
    [Fact]
    public void AddingSecondSemester_ClearsExistingWarnings()
    {
        var (yearId, sem1, sem2) = SeedYearAndSemesters();
        SeedConflictingPair(sem1, "S1");
        SeedConflictingPair(sem2, "S2");

        var vm = CreateVm();

        // Start in single-semester mode → warnings present.
        SelectSemesters(yearId, sem1);
        Assert.All(Cards(vm), c => Assert.False(string.IsNullOrEmpty(c.RoomConflictWarning)));

        // Now select both → warnings must be cleared on the rebuilt list.
        SelectSemesters(yearId, sem1, sem2);
        Assert.True(_semesterContext.IsMultiSemesterMode);
        Assert.All(Cards(vm), c => Assert.True(string.IsNullOrEmpty(c.RoomConflictWarning)));
    }
}

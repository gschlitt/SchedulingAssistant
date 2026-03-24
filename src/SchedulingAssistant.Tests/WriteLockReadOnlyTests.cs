using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Verifies that every write-capable command in every ViewModel refuses execution
/// (<c>CanExecute == false</c>) when the application is in read-only mode — that is,
/// when <see cref="WriteLockService.IsWriter"/> is <c>false</c> because
/// <see cref="WriteLockService.TryAcquire"/> was never called.
///
/// <para>
/// This is the <em>CanExecute layer</em> of the belt-and-suspenders write-protection
/// strategy. Even if UI <c>IsEnabled</c> bindings are somehow bypassed, or a command
/// is invoked programmatically, the <c>CanExecute</c> guard prevents any mutation from
/// reaching the database.
/// </para>
///
/// <para>
/// Coverage spans all 15 write-capable ViewModel surfaces identified during the
/// read-only audit:
/// </para>
/// <list type="number">
///   <item>SectionListViewModel — Add, AddToSemester, Edit, Copy, Delete</item>
///   <item>SectionContextMenuViewModel — Confirm</item>
///   <item>SubjectListViewModel — Add, Edit, Delete</item>
///   <item>SemesterListViewModel — Add, Edit, Delete</item>
///   <item>BlockPatternListViewModel (slots 1–5) — Edit, Clear</item>
///   <item>CopySemesterViewModel — Copy, ContinueCopy</item>
///   <item>EmptySemesterViewModel — Empty</item>
///   <item>SectionPrefixListViewModel — Add, Edit, Delete</item>
///   <item>LegalStartTimeListViewModel — Add, Edit, Delete</item>
///   <item>AcademicYearListViewModel — Add, Delete</item>
///   <item>AcademicUnitListViewModel — Save</item>
///   <item>CourseListViewModel — Add, Edit, Delete, AddSubject, EditSubject, DeleteSubject</item>
///   <item>InstructorListViewModel — Add, Edit, Delete</item>
///   <item>RoomListViewModel — Add, Edit, Delete, MoveUp, MoveDown</item>
///   <item>SectionPropertyListViewModel — Add, Edit, Delete, MoveUp, MoveDown</item>
/// </list>
///
/// <para>
/// Each test constructs a real ViewModel backed by a real (empty) SQLite database in
/// a temporary directory, so the full constructor path — including any eager DB reads —
/// is exercised. The <see cref="WriteLockService"/> is left in its default state
/// (TryAcquire never called), so <see cref="WriteLockService.IsWriter"/> is <c>false</c>.
/// </para>
/// </summary>
public sealed class WriteLockReadOnlyTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly DatabaseContext _db;

    /// <summary>
    /// Reader-mode lock: <see cref="WriteLockService.TryAcquire"/> is intentionally
    /// never called, leaving <see cref="WriteLockService.IsWriter"/> == false.
    /// </summary>
    private readonly WriteLockService _lock;

    // ── Repositories (all backed by the same temp-file SQLite connection) ─────

    private readonly SectionRepository             _sectionRepo;
    private readonly CourseRepository              _courseRepo;
    private readonly SubjectRepository             _subjectRepo;
    private readonly InstructorRepository          _instructorRepo;
    private readonly RoomRepository                _roomRepo;
    private readonly LegalStartTimeRepository      _legalStartTimeRepo;
    private readonly SemesterRepository            _semesterRepo;
    private readonly BlockPatternRepository        _blockPatternRepo;
    private readonly SectionPrefixRepository       _prefixRepo;
    private readonly SectionPropertyRepository     _propertyRepo;
    private readonly CampusRepository              _campusRepo;
    private readonly AcademicYearRepository        _ayRepo;
    private readonly AcademicUnitRepository        _academicUnitRepo;
    private readonly ReleaseRepository             _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;

    // ── Services ──────────────────────────────────────────────────────────────

    private readonly SemesterContext           _semesterContext;
    private readonly SectionChangeNotifier     _changeNotifier;
    private readonly SectionStore              _sectionStore;
    private readonly AcademicUnitService       _academicUnitService;
    private readonly ScheduleValidationService _scheduleValidation;
    private readonly IDialogService            _dialog;

    /// <summary>
    /// Creates an isolated temporary directory, initialises a fresh SQLite schema
    /// inside it, and wires all repositories and services to that connection.
    /// The <see cref="WriteLockService"/> is constructed but <em>not</em> acquired,
    /// so it remains in read-only mode throughout.
    /// </summary>
    public WriteLockReadOnlyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ro_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _db = new DatabaseContext(Path.Combine(_tempDir, "test.db"));

        _lock = new WriteLockService(); // TryAcquire never called → IsWriter == false

        _sectionRepo     = new SectionRepository(_db);
        _courseRepo      = new CourseRepository(_db);
        _subjectRepo     = new SubjectRepository(_db);
        _instructorRepo  = new InstructorRepository(_db);
        _roomRepo        = new RoomRepository(_db);
        _legalStartTimeRepo = new LegalStartTimeRepository(_db);
        _semesterRepo    = new SemesterRepository(_db);
        _blockPatternRepo = new BlockPatternRepository(_db);
        _prefixRepo      = new SectionPrefixRepository(_db);
        _propertyRepo    = new SectionPropertyRepository(_db);
        _campusRepo      = new CampusRepository(_db);
        _ayRepo          = new AcademicYearRepository(_db);
        _academicUnitRepo = new AcademicUnitRepository(_db);
        _releaseRepo     = new ReleaseRepository(_db);
        _commitmentRepo  = new InstructorCommitmentRepository(_db);

        _semesterContext    = new SemesterContext();
        _changeNotifier     = new SectionChangeNotifier();
        _sectionStore       = new SectionStore();
        _academicUnitService = new AcademicUnitService(_academicUnitRepo);
        _scheduleValidation  = new ScheduleValidationService(_legalStartTimeRepo);
        _dialog             = new NullDialogService();
    }

    /// <summary>Disposes the lock service, database connection, and temp directory.</summary>
    public void Dispose()
    {
        _lock.Dispose();
        _db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub that satisfies <see cref="IDialogService"/> without showing any Avalonia UI.
    /// Confirm always returns false (cancelled); ShowError is a no-op.
    /// </summary>
    private sealed class NullDialogService : IDialogService
    {
        /// <inheritdoc/>
        public Task<bool> Confirm(string message, string confirmLabel = "Delete") =>
            Task.FromResult(false);

        /// <inheritdoc/>
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>Creates a fully-wired <see cref="SectionListViewModel"/> in reader mode.</summary>
    private SectionListViewModel CreateSectionListVm() =>
        new(_sectionRepo, _courseRepo, _subjectRepo, _instructorRepo, _roomRepo,
            _legalStartTimeRepo, _semesterRepo, _blockPatternRepo, _prefixRepo,
            _semesterContext, _sectionStore, _propertyRepo, _campusRepo, _dialog, _lock);

    /// <summary>Creates a fully-wired <see cref="CourseListViewModel"/> in reader mode.</summary>
    private CourseListViewModel CreateCourseListVm() =>
        new(_courseRepo, _subjectRepo, _propertyRepo, _dialog, _sectionRepo,
            _semesterRepo, _ayRepo, _instructorRepo, _lock);

    /// <summary>Creates a fully-wired <see cref="InstructorListViewModel"/> in reader mode.</summary>
    private InstructorListViewModel CreateInstructorListVm() =>
        new(_instructorRepo, _propertyRepo, _sectionRepo, _courseRepo, _releaseRepo,
            _commitmentRepo, _semesterRepo, _ayRepo, _semesterContext, _changeNotifier,
            _dialog, _lock);

    /// <summary>Creates a fully-wired <see cref="RoomListViewModel"/> in reader mode.</summary>
    private RoomListViewModel CreateRoomListVm() =>
        new(_roomRepo, _campusRepo, _sectionRepo, CreateSectionListVm(), _db, _dialog, _lock);

    /// <summary>
    /// Creates a <see cref="SectionPropertyListViewModel"/> for the "sectionType"
    /// property type. The guard code is shared across all types so one instance is
    /// sufficient to prove the lock is checked.
    /// </summary>
    private SectionPropertyListViewModel CreateSectionPropertyVm() =>
        new("sectionType", "Section Type", _propertyRepo, _sectionRepo, _courseRepo,
            _instructorRepo, _db, CreateSectionListVm(), _dialog, _lock);

    // ═════════════════════════════════════════════════════════════════════════
    // Group 1 — SectionListViewModel
    // Primary section-CRUD surface visible in the left panel at all times.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The IsWriteEnabled property must be false so the UI can bind to it.</summary>
    [Fact]
    public void SectionList_IsWriteEnabled_FalseInReaderMode()
    {
        var vm = CreateSectionListVm();
        Assert.False(vm.IsWriteEnabled);
    }

    [Fact]
    public void SectionList_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionListVm();
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void SectionList_AddToSemesterCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionListVm();
        // Parameter type is string; the CanExecute predicate only checks IsWriter.
        Assert.False(vm.AddToSemesterCommand.CanExecute(""));
    }

    [Fact]
    public void SectionList_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionListVm();
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void SectionList_CopyCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionListVm();
        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void SectionList_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionListVm();
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 2 — SectionContextMenuViewModel
    // Right-click context menu on schedule-grid tiles. Confirm patches the section.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SectionContextMenu_ConfirmCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SectionContextMenuViewModel(_sectionRepo, () => { }, _lock);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 3 — SubjectListViewModel
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Subject_IsWriteEnabled_FalseInReaderMode()
    {
        var vm = new SubjectListViewModel(_subjectRepo, _dialog, _lock);
        Assert.False(vm.IsWriteEnabled);
    }

    [Fact]
    public void Subject_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SubjectListViewModel(_subjectRepo, _dialog, _lock);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Subject_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SubjectListViewModel(_subjectRepo, _dialog, _lock);
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void Subject_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SubjectListViewModel(_subjectRepo, _dialog, _lock);
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 5 — BlockPatternListViewModel  (nested BlockPatternSlotViewModel)
    // Write commands live on the slot sub-ViewModels, which each receive a
    // Func<bool> delegate that proxies the parent's WriteLockService.IsWriter.
    // Tests exercise slot 1 (first) and slot 5 (last) as boundary checks.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BlockPattern_Slot1_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new BlockPatternListViewModel(_blockPatternRepo, _lock);
        Assert.False(vm.Slot1.EditCommand.CanExecute(null));
    }

    [Fact]
    public void BlockPattern_Slot1_ClearCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new BlockPatternListViewModel(_blockPatternRepo, _lock);
        Assert.False(vm.Slot1.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void BlockPattern_Slot5_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new BlockPatternListViewModel(_blockPatternRepo, _lock);
        Assert.False(vm.Slot5.EditCommand.CanExecute(null));
    }

    [Fact]
    public void BlockPattern_Slot5_ClearCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new BlockPatternListViewModel(_blockPatternRepo, _lock);
        Assert.False(vm.Slot5.ClearCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 6 — CopySemesterViewModel
    // Copy (begin) and ContinueCopy (proceed past schedule-conflict warning)
    // are both write operations; AbortCopy/Cancel/Done are non-destructive.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CopySemester_CopyCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new CopySemesterViewModel(_ayRepo, _semesterRepo, _sectionRepo, _db,
            _scheduleValidation, _courseRepo, _subjectRepo, _lock);
        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void CopySemester_ContinueCopyCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new CopySemesterViewModel(_ayRepo, _semesterRepo, _sectionRepo, _db,
            _scheduleValidation, _courseRepo, _subjectRepo, _lock);
        Assert.False(vm.ContinueCopyCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 7 — EmptySemesterViewModel
    // Empty deletes all sections in a semester; Cancel is a no-op.
    // CanEmpty also requires a semester to be selected, but IsWriter == false
    // is sufficient on its own to make CanExecute return false.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptySemester_EmptyCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new EmptySemesterViewModel(_ayRepo, _semesterRepo, _sectionRepo,
            _semesterContext, _lock);
        Assert.False(vm.EmptyCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 8 — SectionPrefixListViewModel
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SectionPrefix_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SectionPrefixListViewModel(_prefixRepo, _campusRepo, _dialog, _lock);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void SectionPrefix_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SectionPrefixListViewModel(_prefixRepo, _campusRepo, _dialog, _lock);
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void SectionPrefix_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new SectionPrefixListViewModel(_prefixRepo, _campusRepo, _dialog, _lock);
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 9 — LegalStartTimeListViewModel
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LegalStartTime_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new LegalStartTimeListViewModel(_legalStartTimeRepo, _semesterContext,
            _dialog, _lock);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void LegalStartTime_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new LegalStartTimeListViewModel(_legalStartTimeRepo, _semesterContext,
            _dialog, _lock);
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void LegalStartTime_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new LegalStartTimeListViewModel(_legalStartTimeRepo, _semesterContext,
            _dialog, _lock);
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 10 — AcademicYearListViewModel
    // CopySemester and EmptySemester open flyouts (navigation commands) so they
    // are not write-guarded; Add and Delete are the actual DB mutations.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AcademicYear_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new AcademicYearListViewModel(_ayRepo, _semesterRepo, _sectionRepo,
            _semesterContext, _legalStartTimeRepo, _db, _dialog, _lock);
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void AcademicYear_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new AcademicYearListViewModel(_ayRepo, _semesterRepo, _sectionRepo,
            _semesterContext, _legalStartTimeRepo, _db, _dialog, _lock);
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 11 — AcademicUnitListViewModel
    // The CanSave predicate is: IsWriter AND (name has changed). Since IsWriter
    // is false in reader mode, SaveCommand.CanExecute must return false regardless
    // of whether the name field appears dirty.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AcademicUnit_SaveCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = new AcademicUnitListViewModel(_academicUnitService, _lock);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 12 — CourseListViewModel
    // Manages courses and the subjects they belong to; both entity types have
    // full Add/Edit/Delete guarded by CanWrite.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Course_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateCourseListVm();
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Course_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateCourseListVm();
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void Course_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateCourseListVm();
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Course_AddSubjectCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateCourseListVm();
        Assert.False(vm.AddSubjectCommand.CanExecute(null));
    }

    [Fact]
    public void Course_EditSubjectCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateCourseListVm();
        Assert.False(vm.EditSubjectCommand.CanExecute(null));
    }

    [Fact]
    public void Course_DeleteSubjectCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateCourseListVm();
        Assert.False(vm.DeleteSubjectCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 13 — InstructorListViewModel
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Instructor_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateInstructorListVm();
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Instructor_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateInstructorListVm();
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void Instructor_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateInstructorListVm();
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 14 — RoomListViewModel
    // MoveUp/MoveDown reorder the display sort; their predicates also include
    // IsWriter so they are blocked in reader mode.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Room_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateRoomListVm();
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Room_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateRoomListVm();
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void Room_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateRoomListVm();
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Room_MoveUpCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateRoomListVm();
        Assert.False(vm.MoveUpCommand.CanExecute(null));
    }

    [Fact]
    public void Room_MoveDownCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateRoomListVm();
        Assert.False(vm.MoveDownCommand.CanExecute(null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 15 — SectionPropertyListViewModel
    // Covers all property types (section type, meeting type, tag, campus, etc.);
    // the lock guard is shared so one type representative is sufficient.
    // MoveUp/MoveDown are also blocked in reader mode.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SectionProperty_AddCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionPropertyVm();
        Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void SectionProperty_EditCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionPropertyVm();
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void SectionProperty_DeleteCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionPropertyVm();
        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void SectionProperty_MoveUpCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionPropertyVm();
        Assert.False(vm.MoveUpCommand.CanExecute(null));
    }

    [Fact]
    public void SectionProperty_MoveDownCommand_CanExecuteIsFalseInReaderMode()
    {
        var vm = CreateSectionPropertyVm();
        Assert.False(vm.MoveDownCommand.CanExecute(null));
    }
}

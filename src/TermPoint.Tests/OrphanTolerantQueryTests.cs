using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for step 8c of spec item 8: queries that span ALL semesters ignore "orphan" sections, the
/// rows whose semester no longer exists in <c>Semesters</c> (left behind by older app versions when an
/// academic year was deleted). The four repository methods that carry the filter are
/// <see cref="SectionRepository.GetAll()"/>, <see cref="SectionRepository.GetByCourseId"/>,
/// <see cref="CourseRepository.HasSections"/> and <see cref="InstructorRepository.HasSections"/>.
/// <para>
/// Decided 2026-10-08: orphans are never deleted, detected or reported. So besides checking that each
/// method excludes them, these tests check that nothing is deleted, and that the screens built on the
/// methods no longer trip over them: a course or instructor whose only sections are orphans can be
/// deleted, and the room and scheduling-environment deletes (which rewrite every section that mentions
/// the deleted value) leave the orphans exactly as they were.
/// </para>
/// <para>
/// Real temp-file database, real repositories, real view models and a write lock held as writer
/// (<see cref="ExtensionDataDbFixture"/>). Orphans are made the way production made them: a semester
/// is created, sections are stored against it, then the semester and its academic year are deleted
/// without touching the sections. One more orphan refers to a semester id that never existed.
/// </para>
/// </summary>
public sealed class OrphanTolerantQueryTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Protects the developer's real settings.json: SemesterContext and the list view models may save it.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Temp database plus a write lock held as writer (so write commands are enabled).</summary>
    private readonly ExtensionDataDbFixture _fx = new();

    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly AcademicYearRepository _ayRepo;
    private readonly SchedulingEnvironmentRepository _envRepo;
    private readonly CampusRepository _campusRepo;
    private readonly BlockPatternRepository _blockPatternRepo;
    private readonly SectionCodePatternRepository _codePatternRepo;
    private readonly LegalStartTimeRepository _legalRepo;
    private readonly MeetingRepository _meetingRepo;
    private readonly ReleaseRepository _releaseRepo;
    private readonly InstructorCommitmentRepository _commitmentRepo;
    private readonly SchedulingNoteRepository _noteRepo;

    // Services the list view models need.
    private readonly SemesterContext _semesterContext = new();
    private readonly SectionStore _sectionStore = new();
    private readonly GridChangeNotifier _changeNotifier = new();

    // The semester that exists, and the one that existed but was deleted (with its academic year).
    private readonly string _realAy = Guid.NewGuid().ToString();
    private readonly string _realSemester = Guid.NewGuid().ToString();
    private readonly string _deadAy = Guid.NewGuid().ToString();
    private readonly string _deadSemester = Guid.NewGuid().ToString();

    /// <summary>A semester id that never existed at all (a second kind of orphan).</summary>
    private readonly string _neverExistedSemester = Guid.NewGuid().ToString();

    // Reference data. The "orphan-only" course and instructor are used by orphan sections and by
    // nothing else; the "real" ones are also used by sections in the semester that exists.
    private const string SubjectId = "subj-1";
    private const string CourseReal = "course-real";
    private const string CourseOrphanOnly = "course-orphan-only";
    private const string CourseUnused = "course-unused";
    private const string InstructorReal = "inst-real";
    private const string InstructorSecond = "inst-second";
    private const string InstructorOrphanOnly = "inst-orphan-only";
    private const string InstructorUnused = "inst-unused";
    private const string RoomId = "room-1";
    private const string TagId = "tag-1";

    // Section ids, fixed so assertions can name them.
    private const string RealA = "sec-real-a";       // CourseReal, InstructorReal, RoomId, TagId
    private const string RealB = "sec-real-b";       // CourseReal, InstructorReal + InstructorSecond
    private const string OrphanOfReal = "sec-orphan-of-real";            // CourseReal, in the dead semester
    private const string OrphanOnlyA = "sec-orphan-only-a";              // CourseOrphanOnly, in the dead semester
    private const string OrphanOnlyB = "sec-orphan-only-b";              // CourseOrphanOnly, in the dead semester
    private const string OrphanNeverExisted = "sec-orphan-never-existed"; // CourseOrphanOnly, never-existing semester

    /// <summary>All four orphan section ids.</summary>
    private static readonly string[] OrphanIds = [OrphanOfReal, OrphanOnlyA, OrphanOnlyB, OrphanNeverExisted];

    /// <summary>The two sections in the semester that exists.</summary>
    private static readonly string[] RealIds = [RealA, RealB];

    /// <summary>
    /// Builds the reference data, two real sections, and four orphan sections. The orphans refer to
    /// the room and the tag, so the room and tag deletes have something to (not) rewrite.
    /// </summary>
    public OrphanTolerantQueryTests()
    {
        var db = _fx.Db;
        _sectionRepo = new SectionRepository(db);
        _courseRepo = new CourseRepository(db);
        _subjectRepo = new SubjectRepository(db);
        _instructorRepo = new InstructorRepository(db);
        _roomRepo = new RoomRepository(db);
        _semesterRepo = new SemesterRepository(db);
        _ayRepo = new AcademicYearRepository(db);
        _envRepo = new SchedulingEnvironmentRepository(db);
        _campusRepo = new CampusRepository(db);
        _blockPatternRepo = new BlockPatternRepository(db);
        _codePatternRepo = new SectionCodePatternRepository(db);
        _legalRepo = new LegalStartTimeRepository(db);
        _meetingRepo = new MeetingRepository(db);
        _releaseRepo = new ReleaseRepository(db);
        _commitmentRepo = new InstructorCommitmentRepository(db);
        _noteRepo = new SchedulingNoteRepository(db);

        SeedReferenceData();
        SeedSections();
    }

    /// <summary>Releases the temp database and lock, and restores the settings file.</summary>
    public void Dispose()
    {
        _fx.Dispose();
        _settingsGuard.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A dialog service that records every confirmation text it is shown and answers confirmations
    /// with a fixed answer (yes, by default). Errors are ignored.
    /// </summary>
    private sealed class FakeDialog(bool answer = true) : IDialogService
    {
        /// <summary>Every confirmation message the user was asked, in order.</summary>
        public List<string> Confirmations { get; } = new();

        /// <inheritdoc/>
        public Task<bool> Confirm(string message, string confirmLabel = "Delete")
        {
            Confirmations.Add(message);
            return Task.FromResult(answer);
        }

        /// <inheritdoc/>
        public Task ShowError(string message) => Task.CompletedTask;
    }

    /// <summary>Inserts the semester that exists, the subject, courses, instructors, room and tag.</summary>
    private void SeedReferenceData()
    {
        _ayRepo.Insert(new AcademicYear { Id = _realAy, Name = "2025-2026" });
        _semesterRepo.Insert(new Semester { Id = _realSemester, AcademicYearId = _realAy, Name = "Fall", SortOrder = 0 });

        _subjectRepo.Insert(new Subject { Id = SubjectId, Name = "History", CalendarAbbreviation = "HIST" });
        _courseRepo.Insert(new Course { Id = CourseReal,       SubjectId = SubjectId, CalendarCode = "HIST101", Title = "Intro" });
        _courseRepo.Insert(new Course { Id = CourseOrphanOnly, SubjectId = SubjectId, CalendarCode = "HIST201", Title = "Second year" });
        _courseRepo.Insert(new Course { Id = CourseUnused,     SubjectId = SubjectId, CalendarCode = "HIST301", Title = "Unused" });

        _instructorRepo.Insert(new Instructor { Id = InstructorReal,       LastName = "Real",   FirstName = "Ria", Initials = "RR" });
        _instructorRepo.Insert(new Instructor { Id = InstructorSecond,     LastName = "Second", FirstName = "Sam", Initials = "SS" });
        _instructorRepo.Insert(new Instructor { Id = InstructorOrphanOnly, LastName = "Orphan", FirstName = "Ola", Initials = "OO" });
        _instructorRepo.Insert(new Instructor { Id = InstructorUnused,     LastName = "Unused", FirstName = "Una", Initials = "UU" });

        _roomRepo.Insert(new Room { Id = RoomId, Building = "B", RoomNumber = "101" });
        _envRepo.Insert(SchedulingEnvironmentTypes.Tag, new SchedulingEnvironmentValue { Id = TagId, Name = "Core" });
    }

    /// <summary>
    /// Inserts two real sections, then four orphans: three in a semester that is created and then
    /// deleted along with its academic year (what older versions did), and one in a semester id that
    /// never existed.
    /// </summary>
    private void SeedSections()
    {
        // Real sections, in the semester that exists.
        _sectionRepo.Insert(NewSection(RealA, _realSemester, CourseReal, "A", InstructorReal));
        _sectionRepo.Insert(NewSection(RealB, _realSemester, CourseReal, "B", InstructorReal, InstructorSecond));

        // A semester that will be deleted behind its sections' backs. While it exists, the section
        // insert fills in the readable semester_name / academic_year_name columns, as in production.
        _ayRepo.Insert(new AcademicYear { Id = _deadAy, Name = "2020-2021" });
        _semesterRepo.Insert(new Semester { Id = _deadSemester, AcademicYearId = _deadAy, Name = "Old Fall", SortOrder = 0 });
        _sectionRepo.Insert(NewSection(OrphanOfReal, _deadSemester, CourseReal,       "X", InstructorOrphanOnly));
        _sectionRepo.Insert(NewSection(OrphanOnlyA,  _deadSemester, CourseOrphanOnly, "A", InstructorOrphanOnly));
        _sectionRepo.Insert(NewSection(OrphanOnlyB,  _deadSemester, CourseOrphanOnly, "B"));

        // The old academic-year delete: semester and year go, the sections stay.
        _semesterRepo.Delete(_deadSemester);
        _ayRepo.Delete(_deadAy);

        // A second kind of orphan: its semester id was never in Semesters.
        _sectionRepo.Insert(NewSection(OrphanNeverExisted, _neverExistedSemester, CourseOrphanOnly, "Z"));
    }

    /// <summary>
    /// Builds a section meeting once a week in <see cref="RoomId"/> and tagged <see cref="TagId"/>
    /// (so room and tag deletes have something to rewrite), taught by the given instructors.
    /// </summary>
    /// <param name="id">The section id.</param>
    /// <param name="semesterId">The semester the section is stored against.</param>
    /// <param name="courseId">The section's course.</param>
    /// <param name="code">The section code.</param>
    /// <param name="instructorIds">Instructors to assign, in order.</param>
    private static Section NewSection(string id, string semesterId, string courseId, string code,
                                      params string[] instructorIds) => new()
    {
        Id = id,
        SemesterId = semesterId,
        CourseId = courseId,
        SectionCode = code,
        TagIds = [TagId],
        Schedule = [new SectionDaySchedule { Day = 1, StartMinutes = 480, DurationMinutes = 90, RoomId = RoomId }],
        InstructorAssignments = instructorIds
            .Select(i => new InstructorAssignment { InstructorId = i, Workload = 1m })
            .ToList(),
    };

    /// <summary>The ids of the given sections, sorted, for order-independent comparison.</summary>
    /// <param name="sections">The sections.</param>
    private static string[] SortedIds(IEnumerable<Section> sections) =>
        sections.Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();

    /// <summary>Returns <paramref name="ids"/> sorted, to compare against <see cref="SortedIds"/>.</summary>
    /// <param name="ids">The expected ids.</param>
    private static string[] Sorted(params string[] ids) =>
        ids.OrderBy(i => i, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Counts sections whose semester is missing from <c>Semesters</c>, by querying the table
    /// directly (bypassing the repositories, so the check does not depend on the code under test).
    /// </summary>
    private int RawOrphanCount()
    {
        using var cmd = _fx.Db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Sections WHERE semester_id NOT IN (SELECT id FROM Semesters)";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Counts every row in <c>Sections</c>, orphan or not, bypassing the repositories.</summary>
    private int RawSectionCount()
    {
        using var cmd = _fx.Db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Sections";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Reads one section's readable <c>semester_name</c> column directly (null when the column is NULL).</summary>
    /// <param name="sectionId">The section id.</param>
    private string? RawSemesterName(string sectionId)
    {
        using var cmd = _fx.Db.Connection.CreateCommand();
        cmd.CommandText = "SELECT semester_name FROM Sections WHERE id = $id";
        var p = cmd.CreateParameter(); p.ParameterName = "$id"; p.Value = sectionId; cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>A real section list view model (needed by the room and environment lists).</summary>
    private SectionListViewModel NewSectionList(IDialogService dialog) =>
        new(_sectionRepo, _courseRepo, _subjectRepo, _instructorRepo, _roomRepo,
            _legalRepo, _semesterRepo, _blockPatternRepo, _codePatternRepo,
            _semesterContext, _sectionStore, _envRepo, _campusRepo, _meetingRepo,
            dialog, _fx.Lock);

    // ═════════════════════════════════════════════════════════════════════════
    // Fixture sanity
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The fixture really contains orphans (4 of 6 section rows), made the production way. If this
    /// fails, the other tests prove nothing.
    /// </summary>
    [Fact]
    public void Fixture_HasFourOrphansAndTwoRealSections()
    {
        Assert.Equal(6, RawSectionCount());
        Assert.Equal(4, RawOrphanCount());
        Assert.Null(_semesterRepo.GetById(_deadSemester));
        Assert.NotNull(_semesterRepo.GetById(_realSemester));
        // The dead semester's sections still carry its readable name: the rows are untouched leftovers.
        Assert.Equal("Old Fall", RawSemesterName(OrphanOnlyA));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SectionRepository
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The no-argument GetAll drops every orphan and keeps every real section.</summary>
    [Fact]
    public void SectionGetAll_ExcludesOrphans_AndKeepsRealSections()
    {
        var all = _sectionRepo.GetAll();

        Assert.Equal(Sorted(RealA, RealB), SortedIds(all));
        Assert.DoesNotContain(all, s => OrphanIds.Contains(s.Id));
    }

    /// <summary>The semester-scoped GetAll is unchanged: the semester that exists gives its sections.</summary>
    [Fact]
    public void SectionGetAll_ForTheSemesterThatExists_ReturnsItsSections()
    {
        Assert.Equal(Sorted(RealA, RealB), SortedIds(_sectionRepo.GetAll(_realSemester)));
    }

    /// <summary>
    /// GetByCourseId drops the orphans of a course that also has real sections, and returns nothing
    /// for a course used only by orphans.
    /// </summary>
    [Fact]
    public void SectionGetByCourseId_ExcludesOrphans()
    {
        Assert.Equal(Sorted(RealA, RealB), SortedIds(_sectionRepo.GetByCourseId(CourseReal)));
        Assert.Empty(_sectionRepo.GetByCourseId(CourseOrphanOnly));
        Assert.Empty(_sectionRepo.GetByCourseId(CourseUnused));
    }

    /// <summary>GetByCourseId keeps its ordering by section code once the filter is in.</summary>
    [Fact]
    public void SectionGetByCourseId_StillOrdersBySectionCode()
    {
        var codes = _sectionRepo.GetByCourseId(CourseReal).Select(s => s.SectionCode).ToList();

        Assert.Equal(["A", "B"], codes);
    }

    /// <summary>
    /// With no semesters at all, every section is an orphan, so nothing is returned. This is the
    /// definition of an orphan, not a special case; it is pinned so the behaviour is deliberate.
    /// </summary>
    [Fact]
    public void WhenNoSemesterExists_EverySectionCountsAsAnOrphan()
    {
        _semesterRepo.Delete(_realSemester);

        Assert.Empty(_sectionRepo.GetAll());
        Assert.Empty(_sectionRepo.GetByCourseId(CourseReal));
        Assert.False(_courseRepo.HasSections(CourseReal));
        Assert.False(_instructorRepo.HasSections(InstructorReal));
        // ...and nothing was deleted.
        Assert.Equal(6, RawSectionCount());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CourseRepository / InstructorRepository HasSections
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A course used by a real section has sections; a course used only by orphans, or by nothing,
    /// does not.
    /// </summary>
    [Fact]
    public void CourseHasSections_IgnoresOrphans()
    {
        Assert.True(_courseRepo.HasSections(CourseReal));
        Assert.False(_courseRepo.HasSections(CourseOrphanOnly));
        Assert.False(_courseRepo.HasSections(CourseUnused));
    }

    /// <summary>
    /// An instructor assigned to a real section (first or later in the assignment list) has sections;
    /// one assigned only to orphans, or to nothing, does not. The existing JSON matching still works.
    /// </summary>
    [Fact]
    public void InstructorHasSections_IgnoresOrphans()
    {
        Assert.True(_instructorRepo.HasSections(InstructorReal));
        Assert.True(_instructorRepo.HasSections(InstructorSecond));   // second assignment of RealB
        Assert.False(_instructorRepo.HasSections(InstructorOrphanOnly));
        Assert.False(_instructorRepo.HasSections(InstructorUnused));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Nothing is deleted
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Control: running all four queries leaves every orphan in the database, byte for byte. The
    /// mitigation is silent; it must never clean up.
    /// </summary>
    [Fact]
    public void TheQueries_DeleteAndChangeNothing()
    {
        var before = OrphanIds.ToDictionary(i => i, i => _fx.ReadDataText("Sections", i));

        _ = _sectionRepo.GetAll();
        _ = _sectionRepo.GetByCourseId(CourseReal);
        _ = _sectionRepo.GetByCourseId(CourseOrphanOnly);
        _ = _courseRepo.HasSections(CourseOrphanOnly);
        _ = _instructorRepo.HasSections(InstructorOrphanOnly);

        Assert.Equal(6, RawSectionCount());
        Assert.Equal(4, RawOrphanCount());
        foreach (var id in OrphanIds)
            Assert.Equal(before[id], _fx.ReadDataText("Sections", id));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Screens: the delete guards and sweeps built on the queries
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A course whose only sections are orphans can now be deleted: the "has sections" guard no
    /// longer fires on rows nobody can see. The orphans are left in the database.
    /// </summary>
    [Fact]
    public void CourseList_Delete_IsNotBlockedByOrphanSections()
    {
        var vm = NewCourseList();
        vm.SelectedCourse = vm.Courses.Single(c => c.Id == CourseOrphanOnly);

        vm.DeleteCommand.Execute(null);

        Assert.Null(vm.LastErrorMessage);
        Assert.Null(_courseRepo.GetById(CourseOrphanOnly));
        Assert.Equal(6, RawSectionCount());
    }

    /// <summary>Control: a course with a real section is still refused, naming the course.</summary>
    [Fact]
    public void CourseList_Delete_IsStillBlockedByRealSections()
    {
        var vm = NewCourseList();
        vm.SelectedCourse = vm.Courses.Single(c => c.Id == CourseReal);

        vm.DeleteCommand.Execute(null);

        Assert.NotNull(vm.LastErrorMessage);
        Assert.Contains("HIST101", vm.LastErrorMessage);
        Assert.NotNull(_courseRepo.GetById(CourseReal));
    }

    /// <summary>
    /// An instructor assigned only to orphan sections can now be deleted (after the usual
    /// confirmation). The orphans are left in the database.
    /// </summary>
    [Fact]
    public async Task InstructorList_Delete_IsNotBlockedByOrphanSections()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = NewInstructorList(dialog);
        vm.SelectedInstructor = vm.Instructors.Single(i => i.Id == InstructorOrphanOnly);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
        Assert.Single(dialog.Confirmations);                  // got as far as asking the user
        Assert.Null(_instructorRepo.GetById(InstructorOrphanOnly));
        Assert.Equal(6, RawSectionCount());
    }

    /// <summary>Control: an instructor with a real assignment is still refused, without a confirmation prompt.</summary>
    [Fact]
    public async Task InstructorList_Delete_IsStillBlockedByRealSections()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = NewInstructorList(dialog);
        vm.SelectedInstructor = vm.Instructors.Single(i => i.Id == InstructorReal);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.NotNull(vm.LastErrorMessage);
        Assert.Contains("assigned workload", vm.LastErrorMessage);
        Assert.Empty(dialog.Confirmations);
        Assert.NotNull(_instructorRepo.GetById(InstructorReal));
    }

    /// <summary>
    /// Deleting a room clears it from the real sections' meetings and does not touch the orphans:
    /// they keep their meeting data and their readable semester name (the section update would have
    /// rewritten the JSON and blanked <c>semester_name</c>, since no such semester exists).
    /// </summary>
    [Fact]
    public async Task RoomList_Delete_ClearsRealSections_AndLeavesOrphansUntouched()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = new RoomListViewModel(_roomRepo, _campusRepo, _envRepo, _sectionRepo, NewSectionList(dialog),
            _fx.Db, dialog, _fx.Lock);
        vm.SelectedRow = vm.Rooms.Single(r => r.Room.Id == RoomId);
        var orphanJsonBefore = OrphanIds.ToDictionary(i => i, i => _fx.ReadDataText("Sections", i));

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
        Assert.Null(_roomRepo.GetById(RoomId));
        // The real sections no longer mention the room...
        Assert.All(_sectionRepo.GetAll(), s => Assert.All(s.Schedule, m => Assert.Null(m.RoomId)));
        // ...and the orphans are exactly as they were.
        foreach (var id in OrphanIds)
            Assert.Equal(orphanJsonBefore[id], _fx.ReadDataText("Sections", id));
        Assert.Equal("Old Fall", RawSemesterName(OrphanOnlyA));
    }

    /// <summary>
    /// Deleting a tag removes it from the real sections and does not touch the orphans (same
    /// guarantee as the room delete; this is the scheduling-environment list's sweep).
    /// </summary>
    [Fact]
    public async Task SchedulingEnvironmentList_Delete_ClearsRealSections_AndLeavesOrphansUntouched()
    {
        var dialog = new FakeDialog(answer: true);
        var vm = new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Tag, "Tag",
            _envRepo, _sectionRepo, _courseRepo, _instructorRepo, _fx.Db, NewSectionList(dialog), dialog, _fx.Lock);
        vm.SelectedItem = vm.Items.Single(v => v.Id == TagId);
        var orphanJsonBefore = OrphanIds.ToDictionary(i => i, i => _fx.ReadDataText("Sections", i));

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
        Assert.Null(_envRepo.GetById(TagId));
        Assert.All(_sectionRepo.GetAll(), s => Assert.DoesNotContain(TagId, s.TagIds));
        foreach (var id in OrphanIds)
            Assert.Equal(orphanJsonBefore[id], _fx.ReadDataText("Sections", id));
        Assert.Equal("Old Fall", RawSemesterName(OrphanOnlyA));
    }

    // ── View-model builders ───────────────────────────────────────────────────

    /// <summary>Builds the Courses list over the fixture database.</summary>
    private CourseListViewModel NewCourseList() =>
        new(_courseRepo, _subjectRepo, _envRepo, new FakeDialog(), _sectionRepo,
            _semesterRepo, _ayRepo, _instructorRepo, _fx.Lock, _sectionStore);

    /// <summary>Builds the Instructors list over the fixture database.</summary>
    /// <param name="dialog">The dialog service answering the delete confirmation.</param>
    private InstructorListViewModel NewInstructorList(IDialogService dialog) =>
        new(_instructorRepo, _envRepo, _sectionRepo, _courseRepo, _releaseRepo,
            _commitmentRepo, _noteRepo, _semesterRepo, _ayRepo, _semesterContext, _changeNotifier,
            dialog, _fx.Lock,
            new WorkloadMailerViewModel(_ayRepo, _semesterRepo, _instructorRepo, _sectionRepo, _releaseRepo, _courseRepo));
}

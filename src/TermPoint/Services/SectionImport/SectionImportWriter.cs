using System.Data.Common;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Writes a planned section import (an <see cref="ImportPlan"/>) into the open local database, in one
/// transaction, through the repositories' <c>Insert</c> methods and nothing else.
///
/// <para><b>Insert only.</b> This class never calls an <c>Update</c> or <c>Delete</c> method and never builds
/// a replacement for an existing record, so no existing row can change (spec Phase 2, invariant 2). Every
/// record it inserts is brand new, so there is no extension data to carry across
/// (<see cref="PersistedRecord"/>).</para>
///
/// <para><b>The four rules for the shared database.</b> It writes only to the open local copy, through
/// repositories that each call <see cref="IDatabaseContext.MarkDirty"/> first. It never touches a file, a
/// lock or a save, so (1) the shared database still changes only by the normal save's atomic rename, (2) a
/// session that does not hold the lock is refused by the first insert, (3) nothing is deleted, and an import
/// that fails leaves no half-import to lose or to mistake for real work, and (4) it shows the user nothing:
/// the exception propagates and the caller decides the message.</para>
///
/// <para><b>Order.</b> Subjects, campuses, section types, meeting types, tags, rooms, courses, instructors,
/// then sections, so nothing is inserted before a record it refers to. <c>SectionRepository.Insert</c> fills
/// the readable <c>course_code</c> column by subquery on <c>Courses</c>, so courses must already be in.</para>
///
/// <para>Compiles for both targets; it is DI-registered by a later step.</para>
/// </summary>
public sealed class SectionImportWriter
{
    /// <summary>The workload the section editor gives an instructor it has just ticked (a full share).</summary>
    private const decimal DefaultWorkload = 1.0m;

    private readonly IDatabaseContext _db;
    private readonly ISubjectRepository _subjects;
    private readonly ICampusRepository _campuses;
    private readonly ISchedulingEnvironmentRepository _environment;
    private readonly IRoomRepository _rooms;
    private readonly ICourseRepository _courses;
    private readonly IInstructorRepository _instructors;
    private readonly ISectionRepository _sections;

    /// <summary>Creates a writer over the open database and its repositories.</summary>
    /// <param name="db">The open database; supplies the transaction and the write guard.</param>
    /// <param name="subjects">Inserts subjects.</param>
    /// <param name="campuses">Inserts campuses.</param>
    /// <param name="environment">Inserts section types, meeting types and tags.</param>
    /// <param name="rooms">Inserts rooms.</param>
    /// <param name="courses">Inserts courses.</param>
    /// <param name="instructors">Inserts instructors.</param>
    /// <param name="sections">Inserts sections.</param>
    public SectionImportWriter(
        IDatabaseContext db,
        ISubjectRepository subjects,
        ICampusRepository campuses,
        ISchedulingEnvironmentRepository environment,
        IRoomRepository rooms,
        ICourseRepository courses,
        IInstructorRepository instructors,
        ISectionRepository sections)
    {
        _db = db;
        _subjects = subjects;
        _campuses = campuses;
        _environment = environment;
        _rooms = rooms;
        _courses = courses;
        _instructors = instructors;
        _sections = sections;
    }

    /// <summary>
    /// Inserts everything in <paramref name="plan"/> into the open semester, all or nothing. The sections
    /// are built before the transaction begins, so a fault in the plan is found before anything is written.
    /// On any exception the transaction is rolled back (a failed rollback never hides the original
    /// exception) and the exception is rethrown: nothing from a failed write remains.
    /// </summary>
    /// <param name="plan">What to create. Its new records are already built, with fresh ids and sort orders.</param>
    /// <param name="semesterId">The id of the open semester the sections are added to.</param>
    /// <returns>How many records of each kind were inserted; <see cref="ImportCounts.None"/> for an empty plan.</returns>
    /// <exception cref="WriteAccessLostException">
    /// The session is read-only (or lost the write lock): the first insert is refused before anything is written.
    /// </exception>
    /// <exception cref="InvalidOperationException">The plan lists a record that already exists (a planner bug); nothing is written.</exception>
    /// <exception cref="Exception">Any database failure, after the rollback.</exception>
    public ImportCounts Write(ImportPlan plan, string semesterId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(semesterId);

        RequireOnlyNewRecords(plan);
        var sections = plan.NewSections.Select(draft => BuildSection(draft, semesterId)).ToList();

        // Nothing to insert: do not touch the database at all (no dirty marker, no read-only refusal).
        if (HasNothingToWrite(plan))
            return ImportCounts.None;

        // Nullable-tx pattern, as SemesterContentsService.RunInTransaction: a context without transactions
        // (the WASM demo) runs the same inserts with a null transaction.
        using var tx = _db.SupportsTransactions ? _db.Connection.BeginTransaction() : null;
        try
        {
            var counts = InsertAll(plan, sections, tx);
            tx?.Commit();
            return counts;
        }
        catch
        {
            // A failed rollback must not mask the original exception, which is the useful one.
            try { tx?.Rollback(); } catch { /* the original exception is rethrown below */ }
            throw;
        }
    }

    // ── Inserting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs every insert, in dependency order. Commands the other repositories create after the
    /// transaction has begun enlist in it automatically; the section repository assigns its transaction
    /// explicitly, so it must be handed <paramref name="tx"/> (passing none would clear it and fail).
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="sections">The sections, already built from the plan's drafts.</param>
    /// <param name="tx">The open transaction, or null when the context has none.</param>
    /// <returns>The counts of what was inserted.</returns>
    private ImportCounts InsertAll(ImportPlan plan, IReadOnlyList<Section> sections, DbTransaction? tx)
    {
        foreach (var subject in plan.NewSubjects)
            _subjects.Insert(subject.Record);
        foreach (var campus in plan.NewCampuses)
            _campuses.Insert(campus.Record);
        foreach (var sectionType in plan.NewSectionTypes)
            _environment.Insert(SchedulingEnvironmentTypes.SectionType, sectionType.Record);
        foreach (var meetingType in plan.NewMeetingTypes)
            _environment.Insert(SchedulingEnvironmentTypes.MeetingType, meetingType.Record);
        foreach (var tag in plan.NewTags)
            _environment.Insert(SchedulingEnvironmentTypes.Tag, tag.Record);
        foreach (var room in plan.NewRooms)
            _rooms.Insert(room.Record);
        foreach (var course in plan.NewCourses)
            _courses.Insert(course.Record);
        foreach (var instructor in plan.NewInstructors)
            _instructors.Insert(instructor.Record);
        foreach (var section in sections)
            _sections.Insert(section, tx);

        return new ImportCounts(
            plan.NewSubjects.Count, plan.NewCampuses.Count, plan.NewSectionTypes.Count,
            plan.NewMeetingTypes.Count, plan.NewTags.Count, plan.NewRooms.Count,
            plan.NewCourses.Count, plan.NewInstructors.Count, sections.Count);
    }

    /// <summary>True when the plan creates no record of any kind.</summary>
    /// <param name="plan">The plan.</param>
    private static bool HasNothingToWrite(ImportPlan plan) =>
        plan.NewSubjects.Count == 0 && plan.NewCampuses.Count == 0 && plan.NewSectionTypes.Count == 0
        && plan.NewMeetingTypes.Count == 0 && plan.NewTags.Count == 0 && plan.NewRooms.Count == 0
        && plan.NewCourses.Count == 0 && plan.NewInstructors.Count == 0 && plan.NewSections.Count == 0;

    /// <summary>
    /// Refuses a plan that lists an existing record as new, or a section that is already in the semester.
    /// The planner never does this; the check keeps an insert-only writer from ever being asked to re-insert
    /// (or, by implication, replace) a record that is already there.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <exception cref="InvalidOperationException">An entry of the plan is not new.</exception>
    private static void RequireOnlyNewRecords(ImportPlan plan)
    {
        bool allNew =
            plan.NewSubjects.All(r => r.IsNew) && plan.NewCampuses.All(r => r.IsNew)
            && plan.NewSectionTypes.All(r => r.IsNew) && plan.NewMeetingTypes.All(r => r.IsNew)
            && plan.NewTags.All(r => r.IsNew) && plan.NewRooms.All(r => r.IsNew)
            && plan.NewCourses.All(r => r.IsNew) && plan.NewInstructors.All(r => r.IsNew)
            && plan.NewSections.All(d => !d.AlreadyExists);
        if (!allNew)
            throw new InvalidOperationException("The import plan lists a record that already exists; nothing was written.");
    }

    // ── Building a section ────────────────────────────────────────────────────

    /// <summary>
    /// Builds one new <see cref="Section"/> from its draft, field by field as the design's Section and
    /// SectionDaySchedule tables say. The values are the ones the section editor gives a section it has
    /// just created: no notes, no reserves, no resources, no flag, and a workload of 1.0 per instructor.
    /// </summary>
    /// <param name="draft">The section's draft (final capacity, tag ids, level and meetings already worked out).</param>
    /// <param name="semesterId">The open semester.</param>
    /// <returns>A new section with a fresh id, not yet inserted.</returns>
    internal static Section BuildSection(SectionDraft draft, string semesterId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SemesterId = semesterId,
        CourseId = draft.Course.Record.Id,
        SectionCode = draft.Key.SectionCode.Trim(),
        SectionTypeId = draft.SectionType?.Record.Id,
        CampusId = draft.Campus?.Record.Id,
        TagIds = draft.TagIds.ToList(),
        Capacity = draft.Capacity,
        Level = draft.Level,
        InstructorAssignments = draft.Instructors
            .Select(i => new InstructorAssignment { InstructorId = i.Record.Id, Workload = DefaultWorkload })
            .ToList(),
        Schedule = draft.Meetings.Select(BuildMeeting).ToList(),
        Notes = string.Empty,
        Reserves = new List<SectionReserve>(),
        ResourceIds = new List<string>(),
        Flag = SectionFlag.None,
    };

    /// <summary>
    /// Builds the <see cref="SectionDaySchedule"/> for one meeting. <c>RoomTypeId</c> is deliberately null:
    /// on a meeting it is the room type the meeting <i>needs</i>, not a copy of the room's type, and the file
    /// has no column for it.
    /// </summary>
    /// <param name="meeting">The meeting draft.</param>
    /// <returns>The schedule entry.</returns>
    private static SectionDaySchedule BuildMeeting(MeetingDraft meeting) => new()
    {
        Day = meeting.Day,
        StartMinutes = meeting.StartMinutes,
        DurationMinutes = meeting.DurationMinutes,
        Frequency = meeting.Frequency,
        MeetingTypeId = meeting.MeetingType?.Record.Id,
        RoomId = meeting.Room?.Record.Id,
        RoomTypeId = null,
    };
}

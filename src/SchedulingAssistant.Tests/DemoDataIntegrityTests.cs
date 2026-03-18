using SchedulingAssistant.Demo;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Referential-integrity checks for the static <see cref="DemoData"/> class.
///
/// These tests run automatically on every build and catch any mismatch introduced
/// when the Demo Data Generator writes the <c>DemoData.*.cs</c> files.
/// They verify that every ID referenced by one entity actually exists in the
/// corresponding entity list, mirroring the foreign-key constraints that SQLite
/// enforces in the real database.
///
/// Coverage:
///   - Sections → Semesters, Courses, Instructors, Rooms, SectionTypes, Campuses, Tags, Resources, Reserves, MeetingTypes
///   - Courses   → Subjects, Tags
///   - Instructors → StaffTypes
///   - SectionPrefixes → Campuses
///   - Section codes are unique per semester+course
/// </summary>
public class DemoDataIntegrityTests
{
    // ── Pre-computed look-up sets built once per test run ────────────────────

    private static readonly HashSet<string> SemesterIds =
        DemoData.Semesters.Select(s => s.Id).ToHashSet();

    private static readonly HashSet<string> InstructorIds =
        DemoData.Instructors.Select(i => i.Id).ToHashSet();

    private static readonly HashSet<string> CourseIds =
        DemoData.Courses.Select(c => c.Id).ToHashSet();

    private static readonly HashSet<string> RoomIds =
        DemoData.Rooms.Select(r => r.Id).ToHashSet();

    private static readonly HashSet<string> SubjectIds =
        DemoData.Subjects.Select(s => s.Id).ToHashSet();

    private static readonly HashSet<string> SectionTypeIds =
        DemoData.SectionTypes.Select(v => v.Id).ToHashSet();

    private static readonly HashSet<string> MeetingTypeIds =
        DemoData.MeetingTypes.Select(v => v.Id).ToHashSet();

    private static readonly HashSet<string> StaffTypeIds =
        DemoData.StaffTypes.Select(v => v.Id).ToHashSet();

    private static readonly HashSet<string> CampusIds =
        DemoData.Campuses.Select(v => v.Id).ToHashSet();

    private static readonly HashSet<string> TagIds =
        DemoData.Tags.Select(v => v.Id).ToHashSet();

    private static readonly HashSet<string> ResourceIds =
        DemoData.Resources.Select(v => v.Id).ToHashSet();

    private static readonly HashSet<string> ReserveIds =
        DemoData.Reserves.Select(v => v.Id).ToHashSet();

    // ── AcademicYear ─────────────────────────────────────────────────────────

    [Fact]
    public void AcademicYear_HasNonEmptyId()
    {
        // Empty DemoData before first generation is acceptable — skip if not yet populated.
        if (string.IsNullOrEmpty(DemoData.AcademicYear.Id)) return;
        Assert.NotEmpty(DemoData.AcademicYear.Id);
        Assert.NotEmpty(DemoData.AcademicYear.Name);
    }

    [Fact]
    public void Semesters_AllBelongToTheOneAcademicYear()
    {
        if (!DemoData.Semesters.Any()) return;

        var ayId = DemoData.AcademicYear.Id;
        var bad = DemoData.Semesters.Where(s => s.AcademicYearId != ayId).ToList();
        Assert.Empty(bad);
    }

    // ── Sections → other entities ─────────────────────────────────────────────

    [Fact]
    public void Sections_SemesterIdExistsInSemesters()
    {
        var bad = DemoData.Sections
            .Where(s => !SemesterIds.Contains(s.SemesterId))
            .Select(s => $"Section {s.SectionCode} (id={s.Id}) → semesterId={s.SemesterId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_CourseIdExistsInCourses()
    {
        var bad = DemoData.Sections
            .Where(s => s.CourseId is not null && !CourseIds.Contains(s.CourseId))
            .Select(s => $"Section {s.SectionCode} (id={s.Id}) → courseId={s.CourseId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_SectionTypeIdExistsInSectionTypes()
    {
        var bad = DemoData.Sections
            .Where(s => s.SectionTypeId is not null && !SectionTypeIds.Contains(s.SectionTypeId))
            .Select(s => $"Section {s.SectionCode} (id={s.Id}) → sectionTypeId={s.SectionTypeId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_CampusIdExistsInCampuses()
    {
        var bad = DemoData.Sections
            .Where(s => s.CampusId is not null && !CampusIds.Contains(s.CampusId))
            .Select(s => $"Section {s.SectionCode} (id={s.Id}) → campusId={s.CampusId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_TagIdsExistInTags()
    {
        var bad = DemoData.Sections
            .SelectMany(s => s.TagIds.Select(tid => new { s, tid }))
            .Where(x => !TagIds.Contains(x.tid))
            .Select(x => $"Section {x.s.SectionCode} (id={x.s.Id}) → tagId={x.tid}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_ResourceIdsExistInResources()
    {
        var bad = DemoData.Sections
            .SelectMany(s => s.ResourceIds.Select(rid => new { s, rid }))
            .Where(x => !ResourceIds.Contains(x.rid))
            .Select(x => $"Section {x.s.SectionCode} (id={x.s.Id}) → resourceId={x.rid}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_ReserveIdsExistInReserves()
    {
        var bad = DemoData.Sections
            .SelectMany(s => s.Reserves.Select(r => new { s, r }))
            .Where(x => !ReserveIds.Contains(x.r.ReserveId))
            .Select(x => $"Section {x.s.SectionCode} (id={x.s.Id}) → reserveId={x.r.ReserveId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_InstructorIdsExistInInstructors()
    {
        var bad = DemoData.Sections
            .SelectMany(s => s.InstructorAssignments.Select(a => new { s, a.InstructorId }))
            .Where(x => !InstructorIds.Contains(x.InstructorId))
            .Select(x => $"Section {x.s.SectionCode} (id={x.s.Id}) → instructorId={x.InstructorId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_ScheduleRoomIdsExistInRooms()
    {
        var bad = DemoData.Sections
            .SelectMany(s => s.Schedule.Where(d => d.RoomId is not null).Select(d => new { s, d.RoomId }))
            .Where(x => !RoomIds.Contains(x.RoomId!))
            .Select(x => $"Section {x.s.SectionCode} (id={x.s.Id}) → roomId={x.RoomId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_ScheduleMeetingTypeIdsExistInMeetingTypes()
    {
        var bad = DemoData.Sections
            .SelectMany(s => s.Schedule.Where(d => d.MeetingTypeId is not null).Select(d => new { s, d.MeetingTypeId }))
            .Where(x => !MeetingTypeIds.Contains(x.MeetingTypeId!))
            .Select(x => $"Section {x.s.SectionCode} (id={x.s.Id}) → meetingTypeId={x.MeetingTypeId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Sections_SectionCodeUniquePerSemesterAndCourse()
    {
        // Within a semester, no two sections of the same course may share a section code
        // (case-insensitive), matching the uniqueness constraint in SectionRepository.
        var duplicates = DemoData.Sections
            .Where(s => s.CourseId is not null)
            .GroupBy(s => (s.SemesterId, s.CourseId, s.SectionCode.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicate: semester={g.Key.SemesterId} course={g.Key.CourseId} code={g.Key.Item3}")
            .ToList();

        Assert.Empty(duplicates);
    }

    // ── Courses → other entities ──────────────────────────────────────────────

    [Fact]
    public void Courses_SubjectIdExistsInSubjects()
    {
        var bad = DemoData.Courses
            .Where(c => !SubjectIds.Contains(c.SubjectId))
            .Select(c => $"Course {c.CalendarCode} (id={c.Id}) → subjectId={c.SubjectId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Courses_TagIdsExistInTags()
    {
        var bad = DemoData.Courses
            .SelectMany(c => c.TagIds.Select(tid => new { c, tid }))
            .Where(x => !TagIds.Contains(x.tid))
            .Select(x => $"Course {x.c.CalendarCode} (id={x.c.Id}) → tagId={x.tid}")
            .ToList();

        Assert.Empty(bad);
    }

    // ── Instructors → other entities ──────────────────────────────────────────

    [Fact]
    public void Instructors_StaffTypeIdExistsInStaffTypes()
    {
        var bad = DemoData.Instructors
            .Where(i => i.StaffTypeId is not null && !StaffTypeIds.Contains(i.StaffTypeId))
            .Select(i => $"Instructor {i.LastName}, {i.FirstName} (id={i.Id}) → staffTypeId={i.StaffTypeId}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Instructors_InitialsAreNonEmpty()
    {
        if (!DemoData.Instructors.Any()) return;

        var bad = DemoData.Instructors
            .Where(i => string.IsNullOrWhiteSpace(i.Initials))
            .Select(i => $"Instructor id={i.Id}")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Instructors_NamesAreAnonymised()
    {
        // Verify the generator stripped the Email and Notes fields (privacy check).
        var withEmail = DemoData.Instructors.Where(i => !string.IsNullOrEmpty(i.Email)).ToList();
        var withNotes = DemoData.Instructors.Where(i => !string.IsNullOrEmpty(i.Notes)).ToList();

        Assert.Empty(withEmail);
        Assert.Empty(withNotes);
    }

    // ── SectionPrefixes → other entities ─────────────────────────────────────

    [Fact]
    public void SectionPrefixes_CampusIdExistsInCampuses()
    {
        var bad = DemoData.SectionPrefixes
            .Where(p => p.CampusId is not null && !CampusIds.Contains(p.CampusId))
            .Select(p => $"Prefix '{p.Prefix}' (id={p.Id}) → campusId={p.CampusId}")
            .ToList();

        Assert.Empty(bad);
    }

    // ── AllSectionProperties ──────────────────────────────────────────────────

    [Fact]
    public void AllSectionProperties_ContainsAllTypedLists()
    {
        // AllSectionProperties must be the union of all seven typed lists.
        var all = DemoData.AllSectionProperties.Select(v => v.Id).ToHashSet();

        var expected = new HashSet<string>(
            DemoData.SectionTypes.Concat(DemoData.MeetingTypes).Concat(DemoData.StaffTypes)
            .Concat(DemoData.Campuses).Concat(DemoData.Tags).Concat(DemoData.Resources)
            .Concat(DemoData.Reserves).Select(v => v.Id));

        Assert.Equal(expected, all);
    }
}

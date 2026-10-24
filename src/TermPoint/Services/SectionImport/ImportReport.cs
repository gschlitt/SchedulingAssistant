using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Everything the import will create, each list in order of first appearance in the file. A new
/// setup value, course or instructor appears once however many rows name it, and every
/// <see cref="SectionDraft"/> points at the same instance.
/// </summary>
/// <param name="NewSubjects">Subjects to create.</param>
/// <param name="NewCampuses">Campuses to create.</param>
/// <param name="NewSectionTypes">Section types to create.</param>
/// <param name="NewMeetingTypes">Meeting types to create.</param>
/// <param name="NewTags">Tags to create.</param>
/// <param name="NewRooms">Rooms to create (their <c>CampusId</c> is already set where the sections agree).</param>
/// <param name="NewCourses">Courses to create.</param>
/// <param name="NewInstructors">Instructors to create.</param>
/// <param name="NewSections">The sections to add: those not already in the open semester.</param>
public sealed record ImportPlan(
    IReadOnlyList<SetupRef<Subject>> NewSubjects,
    IReadOnlyList<SetupRef<Campus>> NewCampuses,
    IReadOnlyList<SetupRef<SchedulingEnvironmentValue>> NewSectionTypes,
    IReadOnlyList<SetupRef<SchedulingEnvironmentValue>> NewMeetingTypes,
    IReadOnlyList<SetupRef<SchedulingEnvironmentValue>> NewTags,
    IReadOnlyList<SetupRef<Room>> NewRooms,
    IReadOnlyList<CourseRef> NewCourses,
    IReadOnlyList<InstructorRef> NewInstructors,
    IReadOnlyList<SectionDraft> NewSections)
{
    /// <summary>A plan that creates nothing.</summary>
    public static ImportPlan Empty { get; } = new(
        Array.Empty<SetupRef<Subject>>(),
        Array.Empty<SetupRef<Campus>>(),
        Array.Empty<SetupRef<SchedulingEnvironmentValue>>(),
        Array.Empty<SetupRef<SchedulingEnvironmentValue>>(),
        Array.Empty<SetupRef<SchedulingEnvironmentValue>>(),
        Array.Empty<SetupRef<Room>>(),
        Array.Empty<CourseRef>(),
        Array.Empty<InstructorRef>(),
        Array.Empty<SectionDraft>());
}

/// <summary>
/// The result of checking an import file against the open semester: what is wrong, what is worth a
/// look, what will be skipped, and what will be created.
/// </summary>
/// <param name="Problems">Faults in the file; any problem means nothing is imported. Ordered by row (file-wide first).</param>
/// <param name="Warnings">Grouped warnings that do not block the import, ordered file-wide first, then by first row.</param>
/// <param name="SkippedSections">The sections already in the open semester, skipped whole.</param>
/// <param name="Plan">What the import will create.</param>
/// <param name="SemesterWasEmpty">True when the open semester had no sections before the import (for the "done" message).</param>
public sealed record ImportReport(
    IReadOnlyList<ImportProblem> Problems,
    IReadOnlyList<ImportWarning> Warnings,
    IReadOnlyList<SectionDraft> SkippedSections,
    ImportPlan Plan,
    bool SemesterWasEmpty)
{
    /// <summary>True when the file has at least one problem, so the import must not run.</summary>
    public bool HasProblems => Problems.Count > 0;
}

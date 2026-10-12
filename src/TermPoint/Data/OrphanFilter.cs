namespace TermPoint.Data;

/// <summary>
/// The one SQL fragment that makes a query spanning ALL semesters ignore "orphan" rows: rows in a
/// semester-scoped table (Sections, Meetings, Releases, InstructorCommitments, SchedulingNotes,
/// ProgramWatches) whose <c>semester_id</c> no longer exists in <c>Semesters</c>.
/// <para>
/// Orphans were left behind by older app versions (academic-year delete removed the semesters but
/// not what hung off them). Decided 2026-10-08, spec item 8: they are never deleted, detected or
/// reported; instead every query that spans all semesters excludes them, so they cannot block a
/// course or instructor delete ("it has sections" with nothing visible), or be rewritten by the room
/// and scheduling-environment delete sweeps or a course's apply-tags-to-all-sections. (Course History
/// and Workload History already hid them, by joining against existing semesters.) Queries scoped to
/// one semester need no filter, because an orphan can never match a semester that exists.
/// </para>
/// </summary>
internal static class OrphanFilter
{
    /// <summary>
    /// A predicate for a <c>WHERE</c> clause on a semester-scoped table: true only when the row's
    /// semester still exists. <c>semester_id IN (SELECT id FROM Semesters)</c> is satisfied through
    /// the unique index on <c>Semesters.id</c> (its primary key), so it adds one index probe per row
    /// examined. A row whose <c>semester_id</c> is <c>NULL</c> is excluded as well.
    /// </summary>
    internal const string SemesterStillExists = "semester_id IN (SELECT id FROM Semesters)";
}

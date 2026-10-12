using System.Data.Common;
using TermPoint.Data;
using TermPoint.Data.Repositories;

namespace TermPoint.Services;

/// <summary>
/// The single owner of the rules for emptying and deleting semesters and academic years
/// (spec item 8). Every screen that empties or deletes one (Empty Semester, the Semester Manager,
/// the Academic Year list) must go through this service, so the rules cannot drift apart again.
/// <para>
/// Six tables are tied to a semester by <c>semester_id</c>: Sections, Meetings, Releases,
/// InstructorCommitments, SchedulingNotes and ProgramWatches. The decided rules are:
/// </para>
/// <list type="number">
///   <item><description><b>Empty Semester</b> deletes everything attached to the semester, in all
///   six tables, in one transaction. It is refused for a semester that is currently loaded (any of
///   the selected semesters, not just the first). It does not delete the semester row.</description></item>
///   <item><description><b>Delete semester</b> is allowed only when the semester has no rows in any
///   of the six tables and is not currently loaded. Otherwise the refusal says what remains and
///   points to Empty Semester.</description></item>
///   <item><description><b>Delete academic year</b> is allowed only when none of its semesters is
///   currently loaded and every one of its semesters is empty. It then deletes the year's legal
///   start times, its (empty) semesters and the year itself, in one transaction. Otherwise the
///   refusal names the loaded semester or lists the non-empty semesters.</description></item>
/// </list>
/// <para>
/// The <c>WhyCannot…</c> methods return the user-facing refusal text (or <c>null</c> when the
/// action is allowed) so a ViewModel can show it. The action methods re-check the same rules and
/// throw <see cref="InvalidOperationException"/> with that text as defence in depth, so a caller
/// that skips the check still cannot orphan rows.
/// </para>
/// <para>
/// Transactions use the nullable-tx pattern: when <see cref="IDatabaseContext.SupportsTransactions"/>
/// is false (the WASM demo) the repositories are called with a <c>null</c> transaction.
/// </para>
/// </summary>
/// <param name="db">The database context; supplies the connection for transactions.</param>
/// <param name="sectionRepo">Sections (semester-scoped).</param>
/// <param name="meetingRepo">Meetings (semester-scoped).</param>
/// <param name="releaseRepo">Releases (semester-scoped).</param>
/// <param name="commitmentRepo">Instructor commitments (semester-scoped).</param>
/// <param name="noteRepo">Scheduling notes (semester-scoped).</param>
/// <param name="watchRepo">Program watches (semester-scoped).</param>
/// <param name="semesterRepo">Semesters, used for names and for deleting semester rows.</param>
/// <param name="academicYearRepo">Academic years, used for names and for deleting the year row.</param>
/// <param name="legalStartTimeRepo">Legal start times (academic-year-scoped).</param>
/// <param name="semesterContext">Knows which semesters are currently loaded.</param>
public sealed class SemesterContentsService(
    IDatabaseContext db,
    ISectionRepository sectionRepo,
    IMeetingRepository meetingRepo,
    IReleaseRepository releaseRepo,
    IInstructorCommitmentRepository commitmentRepo,
    ISchedulingNoteRepository noteRepo,
    IProgramWatchRepository watchRepo,
    ISemesterRepository semesterRepo,
    IAcademicYearRepository academicYearRepo,
    ILegalStartTimeRepository legalStartTimeRepo,
    SemesterContext semesterContext)
{
    // ── Counting ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Counts everything attached to a semester, per kind. Runs outside any transaction.
    /// </summary>
    /// <param name="semesterId">The semester to count.</param>
    /// <returns>The per-kind counts (all zero for an unknown semester).</returns>
    public SemesterContents CountContents(string semesterId) => new(
        Sections:        sectionRepo.CountBySemesterId(semesterId),
        Meetings:        meetingRepo.CountBySemesterId(semesterId),
        Releases:        releaseRepo.CountBySemesterId(semesterId),
        Commitments:     commitmentRepo.CountBySemesterId(semesterId),
        SchedulingNotes: noteRepo.CountBySemesterId(semesterId),
        ProgramWatches:  watchRepo.CountBySemesterId(semesterId));

    /// <summary>
    /// True when the semester has no rows in any of the six semester-scoped tables.
    /// This is the one emptiness rule used by all three screens.
    /// </summary>
    /// <param name="semesterId">The semester to test.</param>
    public bool IsEmpty(string semesterId) => CountContents(semesterId).IsEmpty;

    // ── Loaded-semester guards ────────────────────────────────────────────────

    /// <summary>
    /// True when the semester is currently loaded, i.e. it is ANY of the semesters selected in the
    /// main view. In multi-semester mode several are loaded at once; checking only the first
    /// (<see cref="SemesterContext.SelectedSemesterDisplay"/>) would miss the others.
    /// </summary>
    /// <param name="semesterId">The semester to test.</param>
    public bool IsLoaded(string semesterId) =>
        semesterContext.SelectedSemesters.Any(s => s.Semester.Id == semesterId);

    /// <summary>
    /// True when at least one semester of the academic year is currently loaded.
    /// </summary>
    /// <param name="academicYearId">The academic year to test.</param>
    public bool AcademicYearHasLoadedSemester(string academicYearId) =>
        semesterContext.SelectedSemesters.Any(s => s.Semester.AcademicYearId == academicYearId);

    // ── Refusal messages ──────────────────────────────────────────────────────

    /// <summary>
    /// Explains why a semester cannot be deleted right now.
    /// </summary>
    /// <param name="semesterId">The semester the user wants to delete.</param>
    /// <returns>
    /// <c>null</c> when deleting is allowed; otherwise a user-facing sentence saying that the
    /// semester is loaded (switch first) or what it still contains (use Empty Semester first).
    /// </returns>
    public string? WhyCannotDeleteSemester(string semesterId)
    {
        var name = SemesterName(semesterId);

        if (IsLoaded(semesterId))
            return LoadedSemesterMessage(name, "deleting");

        var contents = CountContents(semesterId);
        if (!contents.IsEmpty)
            return $"\"{name}\" still contains {contents.Describe()}. " +
                   "Use Empty Semester to remove everything before deleting it.";

        return null;
    }

    /// <summary>
    /// Explains why an academic year cannot be deleted right now.
    /// </summary>
    /// <param name="academicYearId">The academic year the user wants to delete.</param>
    /// <returns>
    /// <c>null</c> when deleting is allowed; otherwise a user-facing sentence that either says the
    /// year is loaded in the main view (switch to a different year first) or lists each non-empty
    /// semester with what it contains (use Empty Semester on each first).
    /// </returns>
    public string? WhyCannotDeleteAcademicYear(string academicYearId)
    {
        var yearName = academicYearRepo.GetById(academicYearId)?.Name ?? academicYearId;

        // A loaded year takes precedence: the user must switch away before anything else matters.
        // Worded about the YEAR (not the loaded semester), because the year is what's being deleted.
        if (AcademicYearHasLoadedSemester(academicYearId))
        {
            return $"\"{yearName}\" is currently loaded in the main view. " +
                   "Switch to a different academic year there before deleting it.";
        }

        // Every semester must be empty. Name each one that is not, with what remains.
        var nonEmpty = new List<string>();
        foreach (var semester in semesterRepo.GetByAcademicYear(academicYearId))
        {
            var contents = CountContents(semester.Id);
            if (!contents.IsEmpty)
                nonEmpty.Add($"{semester.Name} ({contents.Describe()})");
        }
        if (nonEmpty.Count > 0)
        {
            return $"\"{yearName}\" can't be deleted because it still contains: {string.Join("; ", nonEmpty)}. " +
                   "Use Empty Semester to remove everything from each of these semesters before deleting the year.";
        }

        return null;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes everything attached to the semester — sections, meetings, releases, instructor
    /// commitments, scheduling notes and program watches — in ONE transaction: either all six are
    /// emptied or (on any failure) none are. The semester row itself is kept.
    /// </summary>
    /// <param name="semesterId">The semester to empty.</param>
    /// <exception cref="InvalidOperationException">
    /// The semester is currently loaded (any of the selected semesters). Nothing is deleted.
    /// </exception>
    /// <exception cref="Exception">
    /// Any database failure is rethrown after the transaction is rolled back.
    /// </exception>
    public void EmptySemester(string semesterId)
    {
        if (IsLoaded(semesterId))
            throw new InvalidOperationException(LoadedSemesterMessage(SemesterName(semesterId), "emptying"));

        RunInTransaction(tx =>
        {
            sectionRepo.DeleteBySemesterId(semesterId, tx);
            meetingRepo.DeleteBySemesterId(semesterId, tx);
            releaseRepo.DeleteBySemesterId(semesterId, tx);
            commitmentRepo.DeleteBySemesterId(semesterId, tx);
            noteRepo.DeleteBySemesterId(semesterId, tx);
            watchRepo.DeleteBySemesterId(semesterId, tx);
        });
    }

    /// <summary>
    /// Deletes a semester. Allowed only when <see cref="WhyCannotDeleteSemester"/> returns
    /// <c>null</c>, which guarantees no rows are left behind referring to it.
    /// </summary>
    /// <param name="semesterId">The semester to delete.</param>
    /// <exception cref="InvalidOperationException">
    /// The semester is loaded or not empty; the message is the text from
    /// <see cref="WhyCannotDeleteSemester"/>. Nothing is deleted.
    /// </exception>
    public void DeleteSemester(string semesterId)
    {
        var refusal = WhyCannotDeleteSemester(semesterId);
        if (refusal is not null)
            throw new InvalidOperationException(refusal);

        // A single DELETE is atomic by itself, so no explicit transaction is needed.
        semesterRepo.Delete(semesterId);
    }

    /// <summary>
    /// Deletes an academic year together with its semesters and legal start times, in ONE
    /// transaction. Allowed only when <see cref="WhyCannotDeleteAcademicYear"/> returns
    /// <c>null</c>, i.e. none of its semesters is loaded and all of them are empty.
    /// </summary>
    /// <param name="academicYearId">The academic year to delete.</param>
    /// <exception cref="InvalidOperationException">
    /// A semester of the year is loaded or not empty; the message is the text from
    /// <see cref="WhyCannotDeleteAcademicYear"/>. Nothing is deleted.
    /// </exception>
    /// <exception cref="Exception">
    /// Any database failure is rethrown after the transaction is rolled back.
    /// </exception>
    public void DeleteAcademicYear(string academicYearId)
    {
        var refusal = WhyCannotDeleteAcademicYear(academicYearId);
        if (refusal is not null)
            throw new InvalidOperationException(refusal);

        RunInTransaction(tx =>
        {
            legalStartTimeRepo.DeleteByAcademicYear(academicYearId, tx);
            semesterRepo.DeleteByAcademicYear(academicYearId, tx);
            academicYearRepo.Delete(academicYearId, tx);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction using the nullable-tx pattern:
    /// when the context does not support transactions (WASM demo) the work runs with a
    /// <c>null</c> transaction. Commits on success; on any exception rolls back and rethrows.
    /// <para>
    /// Every command executed by <paramref name="work"/> must enlist the transaction it is given
    /// (SQLite refuses commands that do not while a transaction is pending), so do counts and
    /// guard checks BEFORE calling this.
    /// </para>
    /// </summary>
    /// <param name="work">The writes to perform; receives the transaction (or <c>null</c>).</param>
    private void RunInTransaction(Action<DbTransaction?> work)
    {
        using var tx = db.SupportsTransactions ? db.Connection.BeginTransaction() : null;
        try
        {
            work(tx);
            tx?.Commit();
        }
        catch
        {
            // A failed rollback must not mask the original exception, which is the useful one.
            try { tx?.Rollback(); } catch { /* original exception is rethrown below */ }
            throw;
        }
    }

    /// <summary>
    /// Returns the display name of a semester, falling back to its id when it cannot be found.
    /// </summary>
    /// <param name="semesterId">The semester to name.</param>
    private string SemesterName(string semesterId) =>
        semesterRepo.GetById(semesterId)?.Name ?? semesterId;

    /// <summary>
    /// Builds the refusal text for an operation on a semester that is currently loaded.
    /// </summary>
    /// <param name="semesterName">The semester's display name.</param>
    /// <param name="gerund">What the user was trying to do, as a gerund: "deleting" or "emptying".</param>
    private static string LoadedSemesterMessage(string semesterName, string gerund) =>
        $"\"{semesterName}\" is currently loaded. " +
        $"Switch to a different semester in the main view before {gerund} it.";
}

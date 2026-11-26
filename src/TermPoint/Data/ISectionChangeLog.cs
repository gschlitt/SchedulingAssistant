namespace TermPoint.Data;

/// <summary>
/// One row of the <c>Sections</c> table, every column, exactly as stored. The undo history keeps whole rows (not
/// <c>Section</c> objects) so that writing one back restores the readable columns and every JSON field, including
/// fields written by a newer TermPoint that this version does not know.
/// </summary>
/// <param name="Id">The section's id (primary key).</param>
/// <param name="SemesterId">The owning semester's id.</param>
/// <param name="SemesterName">The readable semester name column.</param>
/// <param name="AcademicYearName">The readable academic year name column.</param>
/// <param name="CourseId">The course's id, or null.</param>
/// <param name="RoomId">The readable room id column, or null.</param>
/// <param name="SectionCode">The readable section code column, or null.</param>
/// <param name="CourseCode">The readable course code column, or null.</param>
/// <param name="Data">The <c>data</c> JSON column, verbatim.</param>
public sealed record SectionRow(
    string Id,
    string SemesterId,
    string? SemesterName,
    string? AcademicYearName,
    string? CourseId,
    string? RoomId,
    string? SectionCode,
    string? CourseCode,
    string Data);

/// <summary>The kind of row change a <see cref="SectionLogEntry"/> records.</summary>
public enum SectionLogOp
{
    /// <summary>A row was added (<see cref="SectionLogEntry.Old"/> is null).</summary>
    Insert,

    /// <summary>A row was changed.</summary>
    Update,

    /// <summary>A row was removed (<see cref="SectionLogEntry.New"/> is null).</summary>
    Delete,
}

/// <summary>One recorded change to one <c>Sections</c> row, inside one undo step.</summary>
/// <param name="Seq">Order of recording, ascending (undo walks it newest first).</param>
/// <param name="StepId">The <see cref="UndoStepInfo.Id"/> of the step it belongs to.</param>
/// <param name="Op">Insert, update or delete.</param>
/// <param name="SectionId">The id of the section row that changed.</param>
/// <param name="Old">The row before the change; null for an insert.</param>
/// <param name="New">The row after the change; null for a delete.</param>
public sealed record SectionLogEntry(
    long Seq,
    long StepId,
    SectionLogOp Op,
    string SectionId,
    SectionRow? Old,
    SectionRow? New);

/// <summary>One step in the undo history.</summary>
/// <param name="Id">The step's id; ascending in the order the steps were made.</param>
/// <param name="Label">The approved label shown to the user ("Move CHEM 101 B ...").</param>
/// <param name="At">When the step was made.</param>
/// <param name="EditCount">
/// The edit count (<c>CheckoutService.NoteEdit</c>) after the step; a save that snapshotted at or above it
/// contains the step, so <see cref="ISectionChangeLog.TrimAfterSave"/> drops it.
/// </param>
/// <param name="IsUndone">True when the step has been undone and sits on the redo list.</param>
public sealed record UndoStepInfo(long Id, string Label, DateTime At, long EditCount, bool IsUndone);

/// <summary>
/// How the log treats section writes while a pause is active (<see cref="ISectionChangeLog.BeginPause"/>).
/// </summary>
public enum ChangeLogPause
{
    /// <summary>No pause: section writes belong to the current step, or (with no step) cut the history.</summary>
    None = 0,

    /// <summary>
    /// Section writes that are not user steps and are never undone (a room delete's cleanup, Copy Semester, imports,
    /// semester deletes): nothing is recorded, but "other changes" is set because the file now differs from the save.
    /// </summary>
    NotAStep = 1,

    /// <summary>The log itself is writing rows back (undo or redo): nothing is recorded and nothing is flagged.</summary>
    WritingHistory = 2,
}

/// <summary>
/// The undo history for section changes, kept for one writer session of the working copy (see
/// <c>designs/1.2.5/undo.md</c>, "Technical detail"). It lives in TEMP tables and TEMP triggers on the context's one
/// connection, so it never reaches a file that is copied (the save's snapshot, the shared database) and is gone when the
/// connection is replaced (a lost lock, a database switch).
///
/// <para>The triggers do the recording, and fail safe. Section writes inside a step are logged; section writes inside a
/// <see cref="BeginPause"/> are not; a section write that is neither (a path the design missed) empties the history and
/// sets <see cref="HistoryIncomplete"/>, so a forgotten path can only cut the history short, never undo the wrong thing.
/// A write to any other table sets <see cref="OtherChanges"/> and never touches the history.</para>
///
/// <para>Every member other than <see cref="IsAvailable"/>, <see cref="InstallError"/> and <see cref="Reset"/> may assume
/// <see cref="IsAvailable"/>: when it is false, reads return empty or false and writes throw
/// <see cref="InvalidOperationException"/> (except <see cref="EndStep"/> and <see cref="EndPause"/>, which are no-ops
/// then, so a <c>finally</c> block can always call them).</para>
/// </summary>
public interface ISectionChangeLog
{
    /// <summary>
    /// True when the log exists on the context's current connection: a writer session whose log was created. False for
    /// a read-only session, after the connection was closed, or when creating the log failed.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Why the last attempt to create the log failed, or null. The data layer has no logger, so the service layer
    /// reads this and logs it.
    /// </summary>
    Exception? InstallError { get; }

    /// <summary>
    /// Raised after the connection was reopened (<c>ReinitializeConnection</c>): the history is now empty, or the log
    /// unavailable when the session became read-only. Raised whether the reopen succeeded or failed.
    /// </summary>
    event Action? Reset;

    /// <summary>
    /// Starts an undo step: deletes the redo list (steps that are undone, with their entries), inserts the step and
    /// makes it current, so every section write until <see cref="EndStep"/> is logged under it.
    /// </summary>
    /// <param name="label">The step's approved label.</param>
    /// <param name="at">When the step was made.</param>
    /// <returns>The new step's id.</returns>
    /// <exception cref="InvalidOperationException">The log is unavailable, or a step or pause is already active (no nesting).</exception>
    long BeginStep(string label, DateTime at);

    /// <summary>
    /// Ends the step: clears the current step (always, so callers put this in a <c>finally</c>); deletes entries of the
    /// step whose old and new rows are identical in every column (an update that changed nothing); deletes the step if
    /// no entries are left, otherwise stores <paramref name="editCount"/> on it. A no-op when the log is unavailable.
    /// </summary>
    /// <param name="stepId">The id returned by <see cref="BeginStep"/>.</param>
    /// <param name="editCount">The edit count after the step's writes.</param>
    void EndStep(long stepId, long editCount);

    /// <summary>Starts a pause: section writes are not recorded (see <see cref="ChangeLogPause"/>).</summary>
    /// <param name="mode"><see cref="ChangeLogPause.NotAStep"/> or <see cref="ChangeLogPause.WritingHistory"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="mode"/> is <see cref="ChangeLogPause.None"/>.</exception>
    /// <exception cref="InvalidOperationException">The log is unavailable, or a step or pause is already active (no nesting).</exception>
    void BeginPause(ChangeLogPause mode);

    /// <summary>Ends the pause. A no-op when the log is unavailable.</summary>
    void EndPause();

    /// <summary>The steps, oldest first, both done and undone.</summary>
    /// <returns>The steps, or an empty list when the log is unavailable.</returns>
    IReadOnlyList<UndoStepInfo> GetSteps();

    /// <summary>The entries of one step, in <see cref="SectionLogEntry.Seq"/> order.</summary>
    /// <param name="stepId">The step's id.</param>
    /// <returns>The entries, or an empty list when the log is unavailable or the step is unknown.</returns>
    IReadOnlyList<SectionLogEntry> GetEntries(long stepId);

    /// <summary>Reads a section row as it is now in <c>main.Sections</c>.</summary>
    /// <param name="sectionId">The section's id.</param>
    /// <returns>The row, or null when there is none (or the log is unavailable).</returns>
    SectionRow? ReadCurrent(string sectionId);

    /// <summary>
    /// Writes rows back (undo or redo) in ONE transaction with the <see cref="ChangeLogPause.WritingHistory"/> pause
    /// for its duration: a null row deletes that id; otherwise every column is updated if the id exists, else inserted.
    /// Then marks the step undone or not, and commits. On failure everything is rolled back and the exception rethrown;
    /// the pause is always cleared. The caller calls <c>IDatabaseContext.MarkDirty()</c> first; this does not.
    /// </summary>
    /// <param name="stepId">The step being undone or redone.</param>
    /// <param name="writes">
    /// The rows to write, in the order to write them. Every column is written as given EXCEPT the three readable
    /// columns (course code, semester name, academic year name), which are recomputed from the course, semester and
    /// academic year as they are now, exactly as <c>SectionRepository</c> computes them.
    /// </param>
    /// <param name="markUndone">True to move the step to the redo list (undo); false to put it back (redo).</param>
    /// <exception cref="InvalidOperationException">The log is unavailable, or a step or pause is active.</exception>
    /// <exception cref="Exception">A write failed (a <c>SqliteException</c> in the desktop build); nothing was changed.</exception>
    void WriteRows(long stepId, IReadOnlyList<(string SectionId, SectionRow? Row)> writes, bool markUndone);

    /// <summary>
    /// True when something other than a section edit changed the working copy since the log was created or last reset
    /// (any write to another table, or a <see cref="ChangeLogPause.NotAStep"/> section write). Such a change is not undoable
    /// but is still unsaved.
    /// </summary>
    bool OtherChanges { get; }

    /// <summary>
    /// True when a section write the design missed emptied the history (or <see cref="MarkHistoryIncomplete"/> was called,
    /// e.g. for a recovered open): earlier changes can't be undone one by one, and are still unsaved.
    /// </summary>
    bool HistoryIncomplete { get; }

    /// <summary>Sets <see cref="HistoryIncomplete"/>.</summary>
    /// <exception cref="InvalidOperationException">The log is unavailable.</exception>
    void MarkHistoryIncomplete();

    /// <summary>
    /// True when the row of <paramref name="table"/> with <paramref name="id"/> was deleted since the log was created and
    /// is still gone. Covers every table with an <c>id</c> and a <c>data</c> column, and is never trimmed during a session
    /// (it goes with the connection).
    /// </summary>
    /// <param name="table">The table's name, e.g. "Rooms".</param>
    /// <param name="id">The row's id.</param>
    /// <returns>False when not deleted, the table is not tracked, or the log is unavailable.</returns>
    bool WasDeleted(string table, string id);

    /// <summary>The <c>data</c> column of a row deleted this session (see <see cref="WasDeleted"/>).</summary>
    /// <param name="table">The table's name.</param>
    /// <param name="id">The row's id.</param>
    /// <returns>The JSON as it was when the row was deleted; null when <see cref="WasDeleted"/> is false.</returns>
    string? DeletedData(string table, string id);

    /// <summary>
    /// Empties both lists (done and undone steps, with their entries) and sets <see cref="HistoryIncomplete"/>: the
    /// history can no longer be trusted (an undo found a section changed another way).
    /// </summary>
    /// <exception cref="InvalidOperationException">The log is unavailable.</exception>
    void ClearHistory();

    /// <summary>
    /// Trims the history after a save: deletes every undone step, and every step that is not undone and whose edit count
    /// is at or below <paramref name="editCountAtSnapshot"/>, with their entries. Emptying the TEMP tables is not an edit
    /// to the save's snapshot (probe: <c>PRAGMA data_version</c> does not change).
    /// </summary>
    /// <param name="editCountAtSnapshot">The edit count the save's snapshot was taken at.</param>
    /// <param name="resetFlags">True when nothing was edited since: also clears <see cref="OtherChanges"/> and <see cref="HistoryIncomplete"/>.</param>
    /// <exception cref="InvalidOperationException">The log is unavailable.</exception>
    void TrimAfterSave(long editCountAtSnapshot, bool resetFlags);
}

// The data types of a restore (outcome, lock check, result) live apart from DatabaseRestore.cs, which is
// desktop-only (it is excluded from the browser build in TermPoint.csproj). They are pure data, so keeping them
// here lets RestorePrompt (the restore wording) compile and be unit-tested on both targets.

namespace TermPoint.Services;

/// <summary>
/// Outcome of <see cref="DatabaseRestore.ReplaceAsync"/> (and of <see cref="CheckoutService.RestoreFromBackupAsync"/>,
/// which wraps it). For EVERY value except <see cref="Restored"/> and <see cref="RenameUnconfirmed"/> the shared
/// database D is exactly as it was before the call, and nothing the call created is left behind (no D.tmp, no
/// "before restore" file) — see <see cref="RestoreResult.TargetUnchanged"/>.
/// </summary>
public enum RestoreOutcome
{
    /// <summary>
    /// D was replaced by the backup and the replacement is confirmed. The replaced D is kept beside it as the
    /// pre-restore copy (<see cref="RestoreResult.PreRestoreCopyPath"/>). The caller's session on the OLD D is
    /// now stale and must be ended without saving.
    /// </summary>
    Restored,

    /// <summary>
    /// The final rename (D.tmp over D) timed out or failed in a way that could not be confirmed either way, so D
    /// MAY have been replaced — or may still be replaced a moment later by the abandoned move (a "ghost
    /// rename"). The pre-restore copy is kept and, after a timed-out move, D.tmp is left in place. Treat it like
    /// <see cref="Restored"/>: the session is stale and must be ended without saving.
    /// </summary>
    RenameUnconfirmed,

    /// <summary>
    /// Produced only by <see cref="CheckoutService.RestoreFromBackupAsync"/>: this session does not hold write
    /// access (a reader, a read-only licence, or write access already lost), so nothing was touched. D unchanged.
    /// </summary>
    NotWriter,

    /// <summary>
    /// Produced only by <see cref="CheckoutService.RestoreFromBackupAsync"/>: a save (or another restore) is in
    /// flight on this session, and a restore must never interleave with it. Nothing was touched. D unchanged.
    /// </summary>
    Busy,

    /// <summary>
    /// The backup is positively damaged: it is not a SQLite database (wrong signature, or empty), or SQLite's
    /// integrity check on the local staged copy failed. D unchanged.
    /// </summary>
    BackupDamaged,

    /// <summary>
    /// The backup is a valid SQLite database but not a TermPoint one: it lacks the core tables every TermPoint
    /// database has had since the first version (<c>Sections</c>, <c>Semesters</c>, <c>AcademicYears</c>) — for
    /// example a database from some other program, picked by mistake. Restoring it would leave everyone with a
    /// database TermPoint cannot use. D unchanged.
    /// </summary>
    NotTermPointDatabase,

    /// <summary>
    /// The backup could not be read or examined: it is missing, another program holds it, access was denied, or
    /// the integrity check could not run. Says nothing about its health. D unchanged.
    /// </summary>
    BackupUnreadable,

    /// <summary>
    /// D no longer has the hash the caller last knew (someone else wrote it, or a late rename landed), so it
    /// could not safely be replaced. D is left exactly as found.
    /// </summary>
    TargetChanged,

    /// <summary>
    /// D, D.tmp or the pre-restore file is held by another program (antivirus, backup or sync software) or access
    /// to it was denied. Usually transient. D unchanged.
    /// </summary>
    FileInUse,

    /// <summary>The write lock on D is no longer ours, so no restore was done. D unchanged.</summary>
    LockLost,

    /// <summary>The location did not answer, or a transfer stalled past the network deadline. D unchanged.</summary>
    Unreachable,

    /// <summary>Any other failure; see <see cref="RestoreResult.Error"/>. D unchanged.</summary>
    Failed
}

/// <summary>
/// The answer to "is the caller's write lock on D still ours?", asked twice during a restore: at the start and
/// immediately before the swap. Mirrors the checkout service's three-way lock verification.
/// </summary>
public enum RestoreLockCheck
{
    /// <summary>The lock file exists and belongs to this session.</summary>
    Ours,

    /// <summary>The lock file is gone or belongs to someone else (the location is reachable, so this is real).</summary>
    NotOurs,

    /// <summary>The location could not be reached, so the lock could not be checked. Transient, not a lock loss.</summary>
    Unreachable
}

/// <summary>
/// Result of <see cref="DatabaseRestore.ReplaceAsync"/>.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="PreRestoreCopyPath">
/// Full path of the kept copy of the replaced D, beside D. Set only for <see cref="RestoreOutcome.Restored"/> and
/// <see cref="RestoreOutcome.RenameUnconfirmed"/>; for every other outcome the copy (if one was started) has been
/// removed and this is null.
/// </param>
/// <param name="RestoredHash">
/// SHA-256 (upper-case hex) of the file that was installed as D — the hash D has once the restore is complete.
/// Set under the same conditions as <paramref name="PreRestoreCopyPath"/>; otherwise null.
/// </param>
/// <param name="Error">
/// The exception behind a <see cref="RestoreOutcome.Failed"/>, <see cref="RestoreOutcome.FileInUse"/>,
/// <see cref="RestoreOutcome.BackupUnreadable"/> or an exception-driven <see cref="RestoreOutcome.RenameUnconfirmed"/>
/// outcome (null otherwise). Callers feed it to their existing error-message mapping.
/// </param>
public sealed record RestoreResult(
    RestoreOutcome Outcome,
    string? PreRestoreCopyPath = null,
    string? RestoredHash = null,
    Exception? Error = null)
{
    /// <summary>True only when D was replaced and the replacement is confirmed.</summary>
    public bool Succeeded => Outcome == RestoreOutcome.Restored;

    /// <summary>
    /// True for every outcome except <see cref="RestoreOutcome.Restored"/> and
    /// <see cref="RestoreOutcome.RenameUnconfirmed"/>: D is exactly as it was, and the calling session may
    /// carry on as if nothing had been attempted.
    /// </summary>
    public bool TargetUnchanged => Outcome is not (RestoreOutcome.Restored or RestoreOutcome.RenameUnconfirmed);
}

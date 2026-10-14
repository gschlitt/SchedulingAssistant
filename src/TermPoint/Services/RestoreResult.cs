// The data types of a restore (outcome, lock check, result) live apart from DatabaseRestore.cs, which is
// desktop-only (it is excluded from the browser build in TermPoint.csproj). They are pure data, so keeping them
// here lets RestorePrompt (the restore wording) compile and be unit-tested on both targets.

using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Outcome of <see cref="DatabaseRestore.ReplaceAsync"/>, <see cref="DatabaseRestore.CreateFromBackupAsync"/> and of
/// the checkout service's wrappers around them (<see cref="CheckoutService.RestoreFromBackupAsync"/>, which restores
/// into the session's own database, and <see cref="CheckoutService.RestoreIntoAsync"/>, which restores into a
/// database nobody has open). For EVERY value except <see cref="Restored"/>, <see cref="RenameUnconfirmed"/> and
/// <see cref="Created"/> the destination is exactly as it was before the call (an existing database is
/// byte-identical, a missing one is still missing), and nothing the call created is left behind (no D.tmp, no
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

    /// <summary>
    /// Any other failure; see <see cref="RestoreResult.Error"/>. D unchanged. When the cause is something the user
    /// can act on and TermPoint can word precisely (the editing lock file could not be created), the sentence is in
    /// <see cref="RestoreResult.Detail"/>.
    /// </summary>
    Failed,

    /// <summary>
    /// Produced by <see cref="DatabaseRestore.CreateFromBackupAsync"/> (and so by
    /// <see cref="CheckoutService.RestoreIntoAsync"/> when the destination did not exist): the destination did not
    /// exist and a new database now exists there, complete and in rollback-journal mode, holding the backup's
    /// content. This is the only outcome besides <see cref="Restored"/> that is a success; it is NOT "unchanged"
    /// (<see cref="RestoreResult.TargetUnchanged"/> is false) because a file now exists that did not before.
    /// </summary>
    Created,

    /// <summary>
    /// Produced on the create path: a file exists at the destination — it was there when the call started, or it
    /// appeared while the backup was being prepared. That file was NOT touched and nothing was created. Also the
    /// answer of <see cref="CheckoutService.RestoreIntoAsync"/> when the caller did not allow replacing and the
    /// destination turned out to exist. Destination unchanged.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// Produced only by <see cref="CheckoutService.RestoreIntoAsync"/>: somebody else holds the editing lock on the
    /// database (a live editor, or an abandoned lock that TermPoint's normal open would offer to take over), so
    /// the restore was refused without touching anything. The holder is in <see cref="RestoreResult.Holder"/>. The
    /// lock is never taken over or touched by a restore. Database unchanged.
    /// </summary>
    LockHeld
}

/// <summary>
/// Which screen a restore message is for, so one <see cref="RestoreResult"/> can be worded for the place the user is
/// looking at: the Save &amp; Backup panel (the app restarts afterwards) or the database chooser (the database opens
/// afterwards). See <see cref="RestorePrompt.Describe(RestoreResult, RestoreContext)"/>.
/// </summary>
public enum RestoreContext
{
    /// <summary>The Save &amp; Backup panel: a restore into the database this session has open.</summary>
    SaveAndBackup,

    /// <summary>The database chooser: a restore into a database nobody has open.</summary>
    Chooser
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
/// Result of <see cref="DatabaseRestore.ReplaceAsync"/>, <see cref="DatabaseRestore.CreateFromBackupAsync"/> and the
/// checkout service's restore wrappers.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="PreRestoreCopyPath">
/// Full path of the kept copy of the replaced D, beside D. Set only for <see cref="RestoreOutcome.Restored"/> and
/// <see cref="RestoreOutcome.RenameUnconfirmed"/>; for every other outcome the copy (if one was started) has been
/// removed and this is null (a created database replaced nothing, so it has no such copy either).
/// </param>
/// <param name="RestoredHash">
/// SHA-256 (upper-case hex) of the file that was installed as D — the hash D has once the restore is complete.
/// Set for <see cref="RestoreOutcome.Restored"/>, <see cref="RestoreOutcome.RenameUnconfirmed"/> and
/// <see cref="RestoreOutcome.Created"/>; otherwise null.
/// </param>
/// <param name="Error">
/// The exception behind a <see cref="RestoreOutcome.Failed"/>, <see cref="RestoreOutcome.FileInUse"/>,
/// <see cref="RestoreOutcome.BackupUnreadable"/> or an exception-driven <see cref="RestoreOutcome.RenameUnconfirmed"/>
/// outcome (null otherwise). Callers feed it to their existing error-message mapping.
/// </param>
/// <param name="Holder">
/// For <see cref="RestoreOutcome.LockHeld"/> only: who holds the editing lock (as read from the lock file; fields
/// may be blank or the unreadable-lock placeholder). Null for every other outcome.
/// </param>
/// <param name="CreatePath">
/// True when the destination did not exist and the create path ran (<see cref="DatabaseRestore.CreateFromBackupAsync"/>),
/// whatever the outcome — so a message can say "Nothing was created." instead of "Your database is unchanged."
/// False for the replace path and for refusals that happen before either path is chosen.
/// </param>
/// <param name="Detail">
/// An optional, user-presentable sentence that explains a <see cref="RestoreOutcome.Failed"/> precisely — today
/// only why the editing lock file could not be created (Controlled Folder Access, a permission denial, a full disk).
/// Null otherwise; <see cref="RestorePrompt"/> shows it in place of the generic failure sentence.
/// </param>
public sealed record RestoreResult(
    RestoreOutcome Outcome,
    string? PreRestoreCopyPath = null,
    string? RestoredHash = null,
    Exception? Error = null,
    LockFileData? Holder = null,
    bool CreatePath = false,
    string? Detail = null)
{
    /// <summary>
    /// True when the restore finished and the destination holds the backup: <see cref="RestoreOutcome.Restored"/>
    /// (an existing database was replaced and the replacement is confirmed) or <see cref="RestoreOutcome.Created"/>
    /// (a new database was created).
    /// </summary>
    public bool Succeeded => Outcome is RestoreOutcome.Restored or RestoreOutcome.Created;

    /// <summary>
    /// True for every outcome except <see cref="RestoreOutcome.Restored"/>, <see cref="RestoreOutcome.RenameUnconfirmed"/>
    /// and <see cref="RestoreOutcome.Created"/>: the destination is exactly as it was (an existing database
    /// byte-identical, a missing one still missing), and the caller may carry on as if nothing had been attempted.
    /// </summary>
    public bool TargetUnchanged =>
        Outcome is not (RestoreOutcome.Restored or RestoreOutcome.RenameUnconfirmed or RestoreOutcome.Created);
}

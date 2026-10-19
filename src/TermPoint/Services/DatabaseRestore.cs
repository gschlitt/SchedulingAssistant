using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace TermPoint.Services;

/// <summary>
/// Replaces the shared database D with a backup, safely — the one restore routine shared by every restore site
/// (spec item 5). It never opens D or the backup on the share with SQLite, never leaves a half-written D, and
/// keeps what it replaced.
///
/// <para><b>Why:</b> the old restores did a raw synchronous <c>File.Copy</c> straight onto D, on the UI thread,
/// with no copy of what was overwritten. A network blip mid-copy left a truncated database for everyone, and a
/// restore by a reader wiped out the editor's session. This routine reuses the building blocks of the save and of
/// <see cref="NewDatabaseFile"/>: everything on the share goes through <see cref="NetworkFileOps"/> (off the UI
/// thread, deadline-bounded, stall-aware), the file is built and checked LOCALLY, and it reaches D through
/// <c>D.tmp</c> and the save's atomic rename.</para>
///
/// <para><b>Stages</b> (each failure returns at once; D is untouched until stage 9):</para>
/// <list type="number">
/// <item><description>Check the caller's write lock is still ours.</description></item>
/// <item><description>Stage the backup into a LOCAL temporary file.</description></item>
/// <item><description>Integrity-check the staged copy (a damaged backup is refused), normalise it to
/// rollback-journal mode (WAL must never be active on a shared file), check that it is a TermPoint database
/// at all (a valid SQLite file without TermPoint's core tables is refused) and hash it.</description></item>
/// <item><description>Confirm D still has the hash the caller last knew.</description></item>
/// <item><description>Keep the replaced D as a verified pre-restore copy beside D.</description></item>
/// <item><description>Stream the staged backup to <c>D.tmp</c>.</description></item>
/// <item><description>Verify <c>D.tmp</c> by hash.</description></item>
/// <item><description>Immediately before the swap: lock still ours, D still unchanged.</description></item>
/// <item><description>Rename <c>D.tmp</c> over D (the save's atomic rename), including its check for a rename that
/// lands after its deadline.</description></item>
/// </list>
///
/// <para><b>Cleanup:</b> the local staged file is always deleted. When D is unchanged, the D.tmp and the
/// pre-restore copy this call wrote are deleted too — a "before restore" file never appears when no restore
/// happened. After <see cref="RestoreOutcome.Restored"/> or <see cref="RestoreOutcome.RenameUnconfirmed"/> the
/// share is left exactly as the restore left it.</para>
///
/// <para><b>Creating instead of replacing</b> (<see cref="CreateFromBackupAsync"/>, spec item 5c): when the
/// destination does not exist there is no D to protect, so the routine stages, checks, normalises and hashes the
/// backup exactly as above (stages 2 and 3 — the same code, not a copy) and then places the staged file the way a
/// brand-new database is placed (<see cref="NewFilePlacement"/>: folder, early and late probe, <c>D.tmp</c>, hash
/// verify, a move that can never overwrite). There is no lock, no hash comparison, no pre-restore copy and no
/// rename over anything; a file that appears at the destination meanwhile is never touched.</para>
/// </summary>
public static class DatabaseRestore
{
    /// <summary>
    /// How many candidate names are tried for the pre-restore copy (the first is the plain timestamped name; the
    /// rest add " (2)", " (3)", …) before giving up. Two restores in the same minute need only a second name.
    /// </summary>
    internal const int MaxPreRestoreNameAttempts = 20;

    /// <summary>
    /// The tables a backup must contain to count as a TermPoint database (stage 3). These three have existed
    /// since the very first TermPoint version, so every backup ever made has them; a SQLite file without them
    /// is some other program's database. Deliberately short: a newer table (added by a later release) must
    /// never make an older, perfectly good backup look foreign.
    /// </summary>
    internal static readonly string[] RequiredTables = ["Sections", "Semesters", "AcademicYears"];

    /// <summary>
    /// Replaces <paramref name="targetPath"/> (D) with the backup at <paramref name="backupPath"/>, keeping the
    /// replaced D beside it. See the class remarks for the stages. Every operation on the share is
    /// deadline-bounded; local work runs on thread-pool threads. <b>Never throws</b>: every failure, including
    /// an exception nobody anticipated, comes back as a <see cref="RestoreResult"/>.
    /// </summary>
    /// <param name="backupPath">The backup file to restore (may be on a share). It is only ever copied — never opened with SQLite.</param>
    /// <param name="targetPath">D, the shared database being replaced. It is only ever copied and renamed — never opened with SQLite.</param>
    /// <param name="expectedTargetHash">
    /// D's SHA-256 (upper-case hex) as the caller last knew it. D must still have this hash, both when the restore
    /// starts and immediately before the swap, or the restore is refused with <see cref="RestoreOutcome.TargetChanged"/>.
    /// </param>
    /// <param name="checkLock">
    /// Asks whether the caller's write lock on D is still ours. Called at the start and again right before the
    /// swap. <see cref="RestoreLockCheck.NotOurs"/> gives <see cref="RestoreOutcome.LockLost"/>;
    /// <see cref="RestoreLockCheck.Unreachable"/> gives <see cref="RestoreOutcome.Unreachable"/>.
    /// </param>
    /// <param name="now">The current LOCAL time, used for the pre-restore file name.</param>
    /// <param name="localDirectory">
    /// Folder for the local staged copy. Defaults to the system temp folder; tests supply their own so they can
    /// assert nothing is left behind. The staged file is always deleted.
    /// </param>
    /// <param name="faultPlan">DEBUG-only fault injection (null in a shipped build); see <see cref="RestoreFaultPlan"/>.</param>
    /// <param name="logger">Logger for the outcome lines; defaults to <see cref="App.Logger"/>.</param>
    /// <returns>The outcome. Only <see cref="RestoreOutcome.Restored"/> means D now holds the backup.</returns>
    public static async Task<RestoreResult> ReplaceAsync(
        string backupPath,
        string targetPath,
        string expectedTargetHash,
        Func<Task<RestoreLockCheck>> checkLock,
        DateTime now,
        string? localDirectory = null,
        RestoreFaultPlan? faultPlan = null,
        IAppLogger? logger = null)
    {
        var run = new RestoreRun(
            backupPath, targetPath, expectedTargetHash, checkLock, now,
            localDirectory ?? Path.GetTempPath(), faultPlan, logger ?? App.Logger);
        return await run.ExecuteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a NEW database at <paramref name="targetPath"/> from the backup at <paramref name="backupPath"/>.
    /// The target must not exist; nothing that exists there is ever replaced or touched. The backup is staged
    /// locally, integrity-checked, converted to rollback-journal mode, checked for TermPoint's core tables and
    /// hashed exactly as in <see cref="ReplaceAsync"/>, then placed at the target the way
    /// <see cref="NewDatabaseFile"/> places a brand-new database (<see cref="NewFilePlacement"/>): target folder
    /// created, an early and a late probe, <c>target.tmp</c>, a hash check, and a move that refuses to overwrite.
    /// A damaged, foreign or unreadable backup creates nothing — not even the folder.
    /// Every operation on the share is deadline-bounded; local work runs on thread-pool threads. <b>Never
    /// throws</b>: every failure, including an exception nobody anticipated, comes back as a
    /// <see cref="RestoreResult"/>, whose <see cref="RestoreResult.CreatePath"/> is true.
    ///
    /// <para>Of the DEBUG faults (<see cref="RestoreFaultPlan"/>) only <c>backup</c>, <c>damaged</c> and <c>copy</c>
    /// have a stage on this path.</para>
    /// </summary>
    /// <param name="backupPath">The backup file to restore (may be on a share). It is only ever copied — never opened with SQLite.</param>
    /// <param name="targetPath">Full path of the database to create. It is only ever copied to and moved — never opened with SQLite.</param>
    /// <param name="localDirectory">
    /// Folder for the local staged copy. Defaults to the system temp folder; tests supply their own so they can
    /// assert nothing is left behind. The staged file is always deleted.
    /// </param>
    /// <param name="faultPlan">DEBUG-only fault injection (null in a shipped build); see <see cref="RestoreFaultPlan"/>.</param>
    /// <param name="logger">Logger for the outcome lines; defaults to <see cref="App.Logger"/>.</param>
    /// <returns>
    /// <see cref="RestoreOutcome.Created"/> when the new database exists and is complete;
    /// <see cref="RestoreOutcome.AlreadyExists"/> when a file is (or appeared) at the target — untouched; otherwise
    /// the first failure (damaged, foreign or unreadable backup, <see cref="RestoreOutcome.Unreachable"/>, …), with
    /// nothing created.
    /// </returns>
    public static async Task<RestoreResult> CreateFromBackupAsync(
        string backupPath,
        string targetPath,
        string? localDirectory = null,
        RestoreFaultPlan? faultPlan = null,
        IAppLogger? logger = null)
    {
        var run = new RestoreRun(
            backupPath, targetPath, expectedTargetHash: null, checkLock: null, now: default,
            localDirectory ?? Path.GetTempPath(), faultPlan, logger ?? App.Logger, creating: true);
        return await run.ExecuteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the full path of the pre-restore copy: <c>&lt;name&gt; (before restore yyyy-MM-dd HH-mm)&lt;ext&gt;</c>
    /// in D's own folder, where <c>name</c> and <c>ext</c> are D's. A second and later <paramref name="attempt"/>
    /// inserts " (2)", " (3)", … before the extension, for a name that is already taken. The name never starts
    /// with <c>&lt;name&gt;_</c>, so backup rotation (<c>&lt;name&gt;_*.db</c>) can never match it.
    /// </summary>
    /// <param name="targetPath">Full path of D.</param>
    /// <param name="now">Local time stamped into the name (minute resolution).</param>
    /// <param name="attempt">1 for the plain name; 2 or more for the numbered alternatives.</param>
    /// <returns>The full path of the candidate file.</returns>
    internal static string PreRestoreCopyName(string targetPath, DateTime now, int attempt)
    {
        var name   = Path.GetFileNameWithoutExtension(targetPath);
        var ext    = Path.GetExtension(targetPath);
        var stamp  = now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture);
        var number = attempt >= 2 ? $" ({attempt})" : string.Empty;
        var file   = $"{name} (before restore {stamp}){number}{ext}";

        var folder = Path.GetDirectoryName(targetPath);
        return string.IsNullOrEmpty(folder) ? file : Path.Combine(folder, file);
    }

    /// <summary>
    /// Recognises a pre-restore copy's file name (the format <see cref="PreRestoreCopyName"/> builds, including the
    /// " (2)", " (3)", … of a second copy in the same minute) and recovers the name of the database it is a copy
    /// of. Restoring that file is how a user undoes a restore, so the chooser uses this to offer the original
    /// database's name as the destination. Kept beside <see cref="PreRestoreCopyName"/> so the two formats cannot
    /// drift apart.
    /// </summary>
    /// <param name="path">A file name or full path.</param>
    /// <returns>
    /// The original database's file name with its extension (<c>dept.db</c> for
    /// <c>dept (before restore 2026-10-13 14-05) (2).db</c>), or null when <paramref name="path"/> is not named like a pre-restore copy.
    /// </returns>
    internal static string? TryGetOriginalNameOfPreRestoreCopy(string path)
    {
        var match = PreRestoreNamePattern.Match(Path.GetFileName(path));
        return match.Success ? match.Groups["name"].Value + match.Groups["ext"].Value : null;
    }

    /// <summary>
    /// The shape of a pre-restore copy's file name: <c>&lt;name&gt; (before restore yyyy-MM-dd HH-mm)[ (n)]&lt;ext&gt;</c>.
    /// </summary>
    private static readonly Regex PreRestoreNamePattern = new(
        @"^(?<name>.+) \(before restore \d{4}-\d{2}-\d{2} \d{2}-\d{2}\)(?: \(\d+\))?(?<ext>\.[^.]*)?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// One execution of the restore: holds the inputs, the bookkeeping the cleanup needs (which files this run
    /// created), and one method per stage. A fresh instance per call keeps <see cref="ReplaceAsync"/> reentrant.
    /// </summary>
    private sealed class RestoreRun
    {
        // ── Inputs ────────────────────────────────────────────────────────────

        private readonly string _backupPath;
        private readonly string _targetPath;

        /// <summary>D's hash as the caller last knew it. Null on the create path, which has no D to compare.</summary>
        private readonly string? _expectedTargetHash;

        /// <summary>The caller's "is the write lock still ours?" question. Null on the create path, which takes no lock.</summary>
        private readonly Func<Task<RestoreLockCheck>>? _checkLock;

        /// <summary>True for <see cref="CreateFromBackupAsync"/> (no D exists), false for <see cref="ReplaceAsync"/>.</summary>
        private readonly bool _creating;

        private readonly DateTime _now;
        private readonly string _localDirectory;
        private readonly RestoreFaultPlan? _faults;
        private readonly IAppLogger _log;

        // ── Derived paths ─────────────────────────────────────────────────────

        /// <summary>The local staged copy of the backup. Always deleted at the end.</summary>
        private readonly string _stagedPath;

        /// <summary>D.tmp — where the backup is streamed before the swap.</summary>
        private readonly string _targetTmpPath;

        // ── Bookkeeping for cleanup ───────────────────────────────────────────

        /// <summary>True once this run may have written D.tmp (so a failure must remove it).</summary>
        private bool _targetTmpWritten;

        /// <summary>The chosen pre-restore file name (null until stage 5 picks one).</summary>
        private string? _preRestorePath;

        /// <summary>The pre-restore file's .tmp name (null until stage 5 picks one).</summary>
        private string? _preRestoreTmpPath;

        /// <summary>True while a pre-restore .tmp written by this run may exist.</summary>
        private bool _preRestoreTmpWritten;

        /// <summary>
        /// True when the final pre-restore file may exist because THIS run moved (or tried to move) it into place.
        /// Never set for a file that was already there, so a name collision can never delete someone else's file.
        /// </summary>
        private bool _preRestoreFinalMayExist;

        /// <summary>SHA-256 of the staged (normalised) backup — the hash D has once the restore completes.</summary>
        private string? _restoredHash;

        /// <summary>True once the swap (D.tmp over D) has been started, after which D's state is no longer known to be unchanged.</summary>
        private bool _swapAttempted;

        /// <summary>
        /// Creates the run. See <see cref="DatabaseRestore.ReplaceAsync"/> and
        /// <see cref="DatabaseRestore.CreateFromBackupAsync"/> for the parameters.
        /// </summary>
        /// <param name="creating">True for the create path; then <paramref name="expectedTargetHash"/>, <paramref name="checkLock"/> and <paramref name="now"/> are unused and may be null/default.</param>
        public RestoreRun(
            string backupPath, string targetPath, string? expectedTargetHash,
            Func<Task<RestoreLockCheck>>? checkLock, DateTime now,
            string localDirectory, RestoreFaultPlan? faultPlan, IAppLogger logger,
            bool creating = false)
        {
            _backupPath         = backupPath;
            _targetPath         = targetPath;
            _expectedTargetHash = expectedTargetHash;
            _checkLock          = checkLock;
            _now                = now;
            _localDirectory     = localDirectory;
            _faults             = faultPlan;
            _log                = logger;
            _creating           = creating;

            _stagedPath    = Path.Combine(localDirectory, $"termpoint-restore-{Guid.NewGuid():N}.db");
            _targetTmpPath = targetPath + ".tmp";
        }

        // ── Driver ────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs every stage, then cleans up and logs. Never throws.
        /// </summary>
        /// <returns>The outcome of the restore.</returns>
        public async Task<RestoreResult> ExecuteAsync()
        {
            RestoreResult? result = null;
            try
            {
                try
                {
                    result = _creating
                        ? await RunCreateStagesAsync().ConfigureAwait(false)
                        : await RunStagesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = FromException(ex);
                }
                return result;
            }
            finally
            {
                // The cleanup rules key off the outcome: see CleanupAsync. result is null only if the
                // handler above itself failed, in which case nothing on the share is touched.
                await CleanupAsync(result).ConfigureAwait(false);
                if (result is not null)
                    _log.LogInfo($"DatabaseRestore: {result.Outcome} " +
                                 (result.TargetUnchanged ? (_creating ? "(nothing created)" : "(database unchanged)")
                                  : _creating ? "(database created)" : "(database replaced or replacement unconfirmed)"));
            }
        }

        /// <summary>
        /// Runs the nine stages in order, returning at the first one that reports a result.
        /// </summary>
        /// <returns>The outcome — <see cref="RestoreOutcome.Restored"/> or <see cref="RestoreOutcome.RenameUnconfirmed"/> from the swap, otherwise the first stage failure.</returns>
        private async Task<RestoreResult> RunStagesAsync()
        {
            // 1. Lock still ours (early).
            if (await CheckLockAsync(atTheSwap: false).ConfigureAwait(false) is { } lockFailure) return lockFailure;

            // 2. Stage the backup locally.
            if (await StageBackupAsync().ConfigureAwait(false) is { } stageFailure) return stageFailure;
            CrashPoints.Hit("Restore.AfterStaged");

            // 3. Integrity-check, normalise and hash the staged copy.
            if (await CheckAndNormaliseStagedAsync().ConfigureAwait(false) is { } checkFailure) return checkFailure;

            // 4. D unchanged (early).
            if (await ConfirmTargetUnchangedAsync().ConfigureAwait(false) is { } changedEarly) return changedEarly;

            // 5. Keep a verified copy of the D being replaced.
            if (await KeepPreRestoreCopyAsync().ConfigureAwait(false) is { } preCopyFailure) return preCopyFailure;
            CrashPoints.Hit("Restore.AfterPreRestoreCopy");

            // 6. Stream to D.tmp.
            if (await StreamToTargetTmpAsync().ConfigureAwait(false) is { } copyFailure) return copyFailure;
            CrashPoints.Hit("Restore.AfterTmpWritten");

            // 7. Verify D.tmp.
            if (await VerifyTargetTmpAsync().ConfigureAwait(false) is { } verifyFailure) return verifyFailure;
            CrashPoints.Hit("Restore.AfterTmpVerified");

            // 8. Final checks immediately before the swap.
            if (await CheckLockAsync(atTheSwap: true).ConfigureAwait(false) is { } lockLate) return lockLate;
            if (await ConfirmTargetUnchangedAsync().ConfigureAwait(false) is { } changedLate) return changedLate;

            // 9. Swap.
            var swapped = await SwapAsync().ConfigureAwait(false);
            CrashPoints.Hit("Restore.AfterRename");
            return swapped;
        }

        /// <summary>
        /// The create path: refuse early if something is already at the target, stage and check the backup (the
        /// very same stages 2 and 3 as the replace path), then place the staged file the way a new database is
        /// placed. Returns at the first stage that reports a result.
        /// </summary>
        /// <returns><see cref="RestoreOutcome.Created"/>, or the first failure — with nothing created.</returns>
        private async Task<RestoreResult> RunCreateStagesAsync()
        {
            // C1. Cheap refusal before any work: a file already there (or a dead location) ends it here.
            if (await NewFilePlacement.RefuseIfPresentAsync(_targetPath).ConfigureAwait(false) is { } present)
                return FromPlacement(present);

            // C2. Stage the backup locally (shared with the replace path).
            if (await StageBackupAsync().ConfigureAwait(false) is { } stageFailure) return stageFailure;
            CrashPoints.Hit("Restore.AfterStaged");

            // C3. Integrity-check, normalise, TermPoint-tables check and hash (shared with the replace path).
            if (await CheckAndNormaliseStagedAsync().ConfigureAwait(false) is { } checkFailure) return checkFailure;

            // C4. Folder, late probe, D.tmp, hash verify, no-overwrite move. Only now is anything created.
            return await PlaceNewFileAsync().ConfigureAwait(false);
        }

        // ── Stages ────────────────────────────────────────────────────────────

        /// <summary>
        /// Stages 1 and 8: asks the caller whether its write lock on D is still ours.
        /// </summary>
        /// <param name="atTheSwap">True for the last check before the swap (stage 8), which is where the <c>lock</c> debug fault fires.</param>
        /// <returns>Null to carry on; otherwise LockLost or Unreachable.</returns>
        private async Task<RestoreResult?> CheckLockAsync(bool atTheSwap)
        {
            if (atTheSwap && Inject(InjectedRestoreFault.Lock, "the lock is reported as no longer ours"))
            {
                _log.LogInfo("DatabaseRestore: the write lock is no longer ours");
                return Result(RestoreOutcome.LockLost);
            }

            switch (await _checkLock!().ConfigureAwait(false))
            {
                case RestoreLockCheck.Ours:
                    return null;
                case RestoreLockCheck.NotOurs:
                    _log.LogInfo("DatabaseRestore: the write lock is no longer ours");
                    return Result(RestoreOutcome.LockLost);
                default:
                    return Stalled("checking the write lock");
            }
        }

        /// <summary>
        /// Stage 2: copies the backup from wherever it is (possibly a share) into a local temporary file, so the
        /// backup on the share is never opened with SQLite and the integrity check runs on a quiescent local file.
        /// </summary>
        /// <returns>Null to carry on; Unreachable on a stall; BackupUnreadable when the backup is missing, locked or access is denied.</returns>
        private async Task<RestoreResult?> StageBackupAsync()
        {
            if (Inject(InjectedRestoreFault.Backup, "staging the backup stalls"))
                return Stalled("staging the backup");

            try
            {
                Directory.CreateDirectory(_localDirectory);
                if (!await NetworkFileOps.CopyAsync(_backupPath, _stagedPath).ConfigureAwait(false))
                    return Stalled("staging the backup");
            }
            catch (Exception ex) when (
                ex is FileNotFoundException or DirectoryNotFoundException
                || DatabaseValidator.IsLockedOrAccessDenied(ex))
            {
                _log.LogInfo($"DatabaseRestore: the backup could not be read ({ex.GetType().Name}: {ex.Message})");
                return Result(RestoreOutcome.BackupUnreadable, error: ex);
            }

            return null;
        }

        /// <summary>
        /// Stage 3: refuses a damaged backup, converts the staged copy to rollback-journal mode, refuses a file
        /// that is not a TermPoint database, and hashes it.
        /// </summary>
        /// <returns>Null to carry on; BackupDamaged on positive evidence of damage; BackupUnreadable if the check could not run; NotTermPointDatabase for a valid SQLite file without TermPoint's core tables.</returns>
        private async Task<RestoreResult?> CheckAndNormaliseStagedAsync()
        {
            var injected = Inject(InjectedRestoreFault.Damaged, "the staged backup is reported as damaged");
            var check = injected
                ? new IntegrityCheckOutcome(IntegrityCheckResult.Damaged, "DEBUG fault injection: damaged", DamageEvidence.IntegrityProblems)
                : await Task.Run(CheckStagedBackup).ConfigureAwait(false);

            // Spec item 5, step 5e: positive evidence of damage inside the backup goes to the developers (once per run).
            // A backup that is not a SQLite database at all (the usual wrong-file pick), one that could not be read, and a
            // fault injected by the DEBUG plan are never reported. Never throws, and no path is sent.
            DatabaseDamageReporter.ReportIfDamaged(
                DamagedFileKind.BackupBeingRestored, check.Evidence, check.Summary, _log, injected);

            switch (check.Result)
            {
                case IntegrityCheckResult.Damaged:
                    _log.LogInfo("DatabaseRestore: the backup failed its integrity check — refusing to restore it");
                    return Result(RestoreOutcome.BackupDamaged);
                case IntegrityCheckResult.Unreadable:
                    _log.LogInfo("DatabaseRestore: the backup could not be checked");
                    return Result(RestoreOutcome.BackupUnreadable);
            }

            // Local work (the staged file is not on the share): no deadline needed.
            var isTermPoint = await Task.Run(() =>
            {
                NormaliseStagedBackup();

                // After normalising (so the file is a plain rollback-journal database) and BEFORE hashing: a
                // foreign database is refused without ever computing the hash D would get.
                if (!StagedBackupHasTermPointTables())
                    return false;

                _restoredHash = NetworkFileOps.ComputeHash(_stagedPath);
                return true;
            }).ConfigureAwait(false);

            if (!isTermPoint)
            {
                _log.LogInfo("DatabaseRestore: the backup is a SQLite database but not a TermPoint one — refusing to restore it");
                return Result(RestoreOutcome.NotTermPointDatabase);
            }

            return null;
        }

        /// <summary>
        /// Stages 4 and 8 (second half): confirms D still has the hash the caller last knew, by hashing it
        /// (never by opening it with SQLite).
        /// </summary>
        /// <returns>Null when D is unchanged; Unreachable on a stall; FileInUse when D cannot be read; Failed when D is gone; TargetChanged when its hash differs.</returns>
        private async Task<RestoreResult?> ConfirmTargetUnchangedAsync()
        {
            string? hash;
            try
            {
                bool completed;
                (completed, hash) = await NetworkFileOps.ComputeHashAsync(_targetPath).ConfigureAwait(false);
                if (!completed)
                    return Stalled("hashing the database");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _log.LogInfo($"DatabaseRestore: the database is missing ({ex.GetType().Name})");
                return Result(RestoreOutcome.Failed, error: ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogInfo($"DatabaseRestore: cannot read the database ({ex.GetType().Name}: {ex.Message})");
                return Result(RestoreOutcome.FileInUse, error: ex);
            }

            if (!HashesEqual(hash, _expectedTargetHash))
            {
                _log.LogInfo("DatabaseRestore: the database changed since it was last read — refusing to restore over it");
                return Result(RestoreOutcome.TargetChanged);
            }

            return null;
        }

        /// <summary>
        /// Stage 5: copies the current D to a pre-restore file beside it (first to <c>.tmp</c>, verified by hash,
        /// then moved into place WITHOUT overwrite, so an existing file is never touched).
        /// </summary>
        /// <returns>Null to carry on; Unreachable on a stall; Failed if no free name was found or the copy does not match D; IO errors are mapped by <see cref="FromException"/>.</returns>
        private async Task<RestoreResult?> KeepPreRestoreCopyAsync()
        {
            // Pick the first name that is confirmed free.
            string? chosen = null;
            for (var attempt = 1; attempt <= MaxPreRestoreNameAttempts && chosen is null; attempt++)
            {
                var candidate = PreRestoreCopyName(_targetPath, _now, attempt);
                switch (await NetworkFileOps.ProbeFileAsync(candidate).ConfigureAwait(false))
                {
                    case FileProbeResult.Missing:
                        chosen = candidate;
                        break;
                    case FileProbeResult.Unreachable:
                        return Stalled("choosing the pre-restore file name");
                }
            }

            if (chosen is null)
                return Result(RestoreOutcome.Failed,
                    error: new IOException("No free name could be found for the pre-restore copy."));

            _preRestorePath    = chosen;
            _preRestoreTmpPath = chosen + ".tmp";

            if (Inject(InjectedRestoreFault.PreCopy, "the pre-restore copy stalls"))
                return Stalled("copying the database to the pre-restore file");

            _preRestoreTmpWritten = true;
            if (!await NetworkFileOps.CopyAsync(_targetPath, _preRestoreTmpPath).ConfigureAwait(false))
                return Stalled("copying the database to the pre-restore file");

            var (verified, copyHash) = await NetworkFileOps.ComputeHashAsync(_preRestoreTmpPath).ConfigureAwait(false);
            if (!verified)
                return Stalled("verifying the pre-restore copy");
            if (!HashesEqual(copyHash, _expectedTargetHash))
            {
                _log.LogInfo("DatabaseRestore: the pre-restore copy does not match the database — aborting");
                return Result(RestoreOutcome.Failed,
                    error: new IOException("The pre-restore copy did not copy correctly."));
            }

            // Move into place without overwrite. From here the final file may exist because of us; a move that
            // THROWS (typically: the name appeared meanwhile) means we created nothing.
            _preRestoreFinalMayExist = true;
            bool moved;
            try
            {
                moved = await NetworkFileOps.MoveAsync(_preRestoreTmpPath, chosen, overwrite: false).ConfigureAwait(false);
            }
            catch
            {
                _preRestoreFinalMayExist = false;
                throw;
            }

            if (!moved)
                return Stalled("moving the pre-restore copy into place");

            _preRestoreTmpWritten = false;   // the .tmp became the pre-restore file
            return null;
        }

        /// <summary>
        /// Stage 6: streams the staged backup to <c>D.tmp</c> with the stall-aware copy.
        /// </summary>
        /// <returns>Null to carry on; Unreachable on a stall; IO errors (a D.tmp held by another program, …) are mapped by <see cref="FromException"/>.</returns>
        private async Task<RestoreResult?> StreamToTargetTmpAsync()
        {
            _targetTmpWritten = true;

            if (Inject(InjectedRestoreFault.Copy, "the transfer to D.tmp is cut part-way"))
            {
                await WritePartialTargetTmpAsync().ConfigureAwait(false);
                return Stalled("streaming the backup to D.tmp");
            }

            if (!await NetworkFileOps.CopyAsync(_stagedPath, _targetTmpPath).ConfigureAwait(false))
                return Stalled("streaming the backup to D.tmp");

            return null;
        }

        /// <summary>
        /// Stage 7: D.tmp must hash exactly like the staged backup, or the transfer was torn and it must never
        /// become D.
        /// </summary>
        /// <returns>Null when it matches; Unreachable on a stall; Failed on a mismatch; IO errors are mapped by <see cref="FromException"/>.</returns>
        private async Task<RestoreResult?> VerifyTargetTmpAsync()
        {
            if (Inject(InjectedRestoreFault.Verify, "D.tmp is reported as not matching the backup"))
                return TornTransfer();

            var (completed, hash) = await NetworkFileOps.ComputeHashAsync(_targetTmpPath).ConfigureAwait(false);
            if (!completed)
                return Stalled("verifying D.tmp");

            return HashesEqual(hash, _restoredHash) ? null : TornTransfer();
        }

        /// <summary>
        /// Create path, last step: makes the target's folder and hands the staged file to the shared placement
        /// pipeline, which probes again, streams to <c>D.tmp</c> (through <see cref="CopyForCreateAsync"/> so the
        /// DEBUG <c>copy</c> fault can cut it), verifies the hash and moves it into place without overwrite. A
        /// leftover <c>D.tmp</c> is removed and awaited before this returns, so "nothing was created" is literally true.
        /// </summary>
        /// <returns>Created, AlreadyExists, Unreachable or Failed; an IO exception propagates to <see cref="FromException"/>.</returns>
        private async Task<RestoreResult> PlaceNewFileAsync()
        {
            _log.LogInfo("DatabaseRestore: about to place the new database at the destination");

            if (await NewFilePlacement.EnsureFolderAsync(_targetPath, _log).ConfigureAwait(false) is { } folder)
                return FromPlacement(folder);

            var placed = await NewFilePlacement.PlaceAsync(
                _stagedPath, _targetPath, awaitTmpCleanup: true,
                localHash: _restoredHash, copyAsync: CopyForCreateAsync, logger: _log).ConfigureAwait(false);
            return FromPlacement(placed);
        }

        /// <summary>
        /// The transfer to <c>D.tmp</c> on the create path: the real stall-aware copy, unless the DEBUG
        /// <c>copy</c> fault is armed, in which case a truncated <c>D.tmp</c> is left behind (the debris a severed
        /// transfer really leaves) and the copy reports a stall.
        /// </summary>
        /// <param name="source">The staged local file.</param>
        /// <param name="destination">D.tmp.</param>
        /// <returns>True when the copy completed; false when it stalled.</returns>
        private async Task<bool> CopyForCreateAsync(string source, string destination)
        {
            if (Inject(InjectedRestoreFault.Copy, "the transfer to D.tmp is cut part-way"))
            {
                await WritePartialTargetTmpAsync().ConfigureAwait(false);
                return false;
            }

            return await NetworkFileOps.CopyAsync(source, destination).ConfigureAwait(false);
        }

        /// <summary>
        /// Stage 9: renames D.tmp over D with the save's atomic rename, resolving a rename that timed out or
        /// threw by looking at what D now is (the "ghost rename" check).
        /// </summary>
        /// <returns>Restored, or RenameUnconfirmed; or — only when D is proven unchanged after a failed move — FileInUse/Failed.</returns>
        private async Task<RestoreResult> SwapAsync()
        {
            _swapAttempted = true;

            bool moved;
            try
            {
                // The rename debug fault does NOT move anything: it behaves as a move that timed out.
                moved = !Inject(InjectedRestoreFault.Rename, "the rename is reported as timed out (nothing is moved)")
                    && await NetworkFileOps.MoveAsync(_targetTmpPath, _targetPath, overwrite: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await ResolveFailedMoveAsync(ex).ConfigureAwait(false);
            }

            if (moved)
            {
                _targetTmpWritten = false;   // D.tmp became D
                return Result(RestoreOutcome.Restored);
            }

            // The deadline elapsed. The abandoned File.Move may still complete ("ghost rename"): probe D once.
            // If it already is the restored file the move landed just past the deadline and we succeeded.
            if (HashesEqual(await ProbeTargetHashAsync().ConfigureAwait(false), _restoredHash))
            {
                _log.LogInfo("DatabaseRestore: the rename timed out but the database already matches the backup — treating it as restored");
                _targetTmpWritten = false;
                return Result(RestoreOutcome.Restored);
            }

            // Genuinely unconfirmed. D.tmp is deliberately NOT deleted: the abandoned move may still land, and
            // deleting its source would make it fail halfway. The pre-restore copy is kept too.
            _log.LogInfo("DatabaseRestore: the rename timed out and could not be confirmed");
            return Result(RestoreOutcome.RenameUnconfirmed);
        }

        /// <summary>
        /// Decides what a THROWN swap means by probing D once: it already is the restored file (the move did
        /// land); it is still the original (the move definitely did not happen); or anything else (unknown).
        /// </summary>
        /// <param name="ex">The exception the move threw.</param>
        /// <returns>
        /// Restored; FileInUse/Failed with D proven unchanged (cleanup then removes D.tmp and the pre-restore
        /// copy); or RenameUnconfirmed (D.tmp is removed here — a thrown move leaves no abandoned thread — but the
        /// pre-restore copy is kept).
        /// </returns>
        private async Task<RestoreResult> ResolveFailedMoveAsync(Exception ex)
        {
            var current = await ProbeTargetHashAsync().ConfigureAwait(false);

            if (HashesEqual(current, _restoredHash))
            {
                _log.LogInfo("DatabaseRestore: the rename reported an error but the database already matches the backup — treating it as restored");
                _targetTmpWritten = false;
                return Result(RestoreOutcome.Restored);
            }

            if (HashesEqual(current, _expectedTargetHash))
            {
                // The move definitely did not happen: D is as it was. Cleanup removes D.tmp and the pre-restore copy.
                _swapAttempted = false;
                _log.LogInfo($"DatabaseRestore: the rename failed and the database is unchanged ({ex.GetType().Name}: {ex.Message})");
                return Result(
                    DatabaseValidator.IsLockedOrAccessDenied(ex) ? RestoreOutcome.FileInUse : RestoreOutcome.Failed,
                    error: ex);
            }

            _log.LogInfo($"DatabaseRestore: the rename failed and the database could not be confirmed ({ex.GetType().Name}: {ex.Message})");
            await NetworkFileOps.DeleteAsync(_targetTmpPath).ConfigureAwait(false);
            _targetTmpWritten = false;
            return Result(RestoreOutcome.RenameUnconfirmed, error: ex);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        /// <summary>
        /// Removes what this run created that must not outlive it. The local staged file always goes. When D is
        /// unchanged (<see cref="RestoreResult.TargetUnchanged"/>) so do the D.tmp and the pre-restore copy
        /// (and its .tmp) this run wrote. After Restored / RenameUnconfirmed the share is left alone. Share
        /// deletes are deadline-bounded and non-throwing, run in parallel and awaited, so a caller (and a test)
        /// can rely on them being finished. Never throws.
        /// </summary>
        /// <param name="result">The outcome, or null if none could be produced (then the share is left alone).</param>
        private async Task CleanupAsync(RestoreResult? result)
        {
            try
            {
                DeleteLocalFiles(_stagedPath);

                if (result is null || !result.TargetUnchanged)
                    return;

                var deletes = new List<Task>();
                if (_targetTmpWritten)
                    deletes.Add(NetworkFileOps.DeleteAsync(_targetTmpPath));
                if (_preRestoreTmpWritten && _preRestoreTmpPath is not null)
                    deletes.Add(NetworkFileOps.DeleteAsync(_preRestoreTmpPath));
                if (_preRestoreFinalMayExist && _preRestorePath is not null)
                    deletes.Add(NetworkFileOps.DeleteAsync(_preRestorePath));

                if (deletes.Count > 0)
                    await Task.WhenAll(deletes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogInfo($"DatabaseRestore: cleanup was incomplete ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // ── Staged-backup helpers (local files) ───────────────────────────────

        /// <summary>
        /// Examines the LOCAL staged copy. A file that is not a SQLite database at all — including an empty one,
        /// which SQLite itself would happily treat as a valid empty database and so pass the integrity check —
        /// is Damaged. Otherwise SQLite's own integrity check decides. It runs with ReadWrite access because a
        /// backup may carry a WAL-mode header (the open then needs to create temporary sidecars); the file is a
        /// private temporary copy, so that is harmless.
        /// </summary>
        /// <returns>
        /// The verdict — Ok, Damaged (positive evidence only) or Unreadable — with SQLite's summary and the evidence
        /// behind a Damaged one. A file without the SQLite header is Damaged with
        /// <see cref="DamageEvidence.NotADatabase"/>: refused, but not worth a damage report.
        /// </returns>
        private IntegrityCheckOutcome CheckStagedBackup()
        {
            if (!DatabaseValidator.HasSqliteSignature(_stagedPath))
                return new IntegrityCheckOutcome(
                    IntegrityCheckResult.Damaged, "the file does not start with the SQLite header", DamageEvidence.NotADatabase);

            return BackupService.CheckIntegrityWithSummary(_stagedPath, SqliteOpenMode.ReadWrite);
        }

        /// <summary>
        /// Converts the staged copy to rollback-journal mode and removes WAL sidecars, mirroring
        /// <c>NewDatabaseFile.BuildLocalFile</c>: WAL must never be active on the shared file. Then checks the
        /// result at the byte level (header bytes 18 and 19 are the file-format versions; 2 means WAL).
        /// <c>Pooling=False</c> is essential: a pooled connection would keep the file open after Dispose.
        /// </summary>
        /// <exception cref="InvalidOperationException">The file still claims to be in WAL mode afterwards.</exception>
        private void NormaliseStagedBackup()
        {
            using (var conn = new SqliteConnection($"Data Source={_stagedPath};Mode=ReadWrite;Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=DELETE";
                cmd.ExecuteScalar();
            }

            DeleteWalSidecars(_stagedPath);

            var header = new byte[20];
            using (var stream = new FileStream(_stagedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                stream.ReadExactly(header);
            if (header[18] == 2 || header[19] == 2)
                throw new InvalidOperationException("The staged backup could not be converted out of WAL mode.");
        }

        /// <summary>
        /// Checks that the LOCAL staged copy contains every table in <see cref="RequiredTables"/>. Table names
        /// are compared case-insensitively, as SQLite itself does. Read-only and unpooled (<c>Pooling=False</c>),
        /// so the connection holds nothing open once it is disposed and the staged file can be deleted at once.
        /// </summary>
        /// <returns>True when all of the required tables exist.</returns>
        /// <exception cref="SqliteException">The staged file could not be queried (the integrity check has already passed, so this is unexpected; it surfaces as a Failed result).</exception>
        private bool StagedBackupHasTermPointTables()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqliteConnection($"Data Source={_stagedPath};Mode=ReadOnly;Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    found.Add(reader.GetString(0));
            }

            return RequiredTables.All(found.Contains);
        }

        /// <summary>
        /// DEBUG-fault helper: leaves a truncated D.tmp, the debris a severed transfer really leaves, so the
        /// cleanup of a half-written D.tmp is exercised too. Best effort.
        /// </summary>
        private async Task WritePartialTargetTmpAsync()
        {
            try
            {
                await NetworkFileOps.RunAsync(() =>
                {
                    var bytes = File.ReadAllBytes(_stagedPath);
                    File.WriteAllBytes(_targetTmpPath, bytes.AsSpan(0, bytes.Length / 2).ToArray());
                }, "write partial D.tmp (debug fault)").ConfigureAwait(false);
            }
            catch
            {
                // Best effort: the fault still reports a stall without the debris.
            }
        }

        // ── Small helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Hashes D once for the ghost-rename probe. Any failure (stall, missing, locked) is "unknown", never an
        /// exception.
        /// </summary>
        /// <returns>D's hash, or null when it could not be read.</returns>
        private async Task<string?> ProbeTargetHashAsync()
        {
            try
            {
                var (completed, hash) = await NetworkFileOps.ComputeHashAsync(_targetPath).ConfigureAwait(false);
                return completed ? hash : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds the result. The pre-restore path and the restored hash are reported only for outcomes where D
        /// was (or may have been) replaced; for every other outcome the pre-restore copy has been (or is about to
        /// be) deleted.
        /// </summary>
        /// <param name="outcome">The outcome to report.</param>
        /// <param name="error">The exception behind it, if any.</param>
        private RestoreResult Result(RestoreOutcome outcome, Exception? error = null)
        {
            var changed = outcome is RestoreOutcome.Restored or RestoreOutcome.RenameUnconfirmed or RestoreOutcome.Created;
            return new RestoreResult(
                outcome, changed ? _preRestorePath : null, changed ? _restoredHash : null, error,
                CreatePath: _creating);
        }

        /// <summary>
        /// Translates the shared placement pipeline's result (<see cref="NewDatabaseResult"/>) to a restore result,
        /// logging the two refusals. Used by the create path only.
        /// </summary>
        /// <param name="placement">The placement step's result.</param>
        private RestoreResult FromPlacement(NewDatabaseResult placement)
        {
            switch (placement.Outcome)
            {
                case NewDatabaseOutcome.Created:
                    return Result(RestoreOutcome.Created);
                case NewDatabaseOutcome.AlreadyExists:
                    _log.LogInfo("DatabaseRestore: a file is already at the destination — nothing was created");
                    return Result(RestoreOutcome.AlreadyExists);
                case NewDatabaseOutcome.Unreachable:
                    return Stalled("placing the new database");
                default:
                    return Result(RestoreOutcome.Failed, error: placement.Error);
            }
        }

        /// <summary>Logs a stall and returns the matching Unreachable result.</summary>
        /// <param name="what">Which step stalled (log text only).</param>
        private RestoreResult Stalled(string what)
        {
            _log.LogInfo($"DatabaseRestore: {what} — location unreachable");
            return Result(RestoreOutcome.Unreachable);
        }

        /// <summary>Logs a torn transfer (D.tmp does not match the staged backup) and returns the Failed result.</summary>
        private RestoreResult TornTransfer()
        {
            _log.LogInfo("DatabaseRestore: D.tmp does not match the backup — torn transfer; aborting");
            return Result(RestoreOutcome.Failed,
                error: new IOException("The restored copy did not transfer to the destination correctly."));
        }

        /// <summary>
        /// Maps an exception that escaped a stage to a result. After the swap has started D's state is unknown,
        /// so anything is RenameUnconfirmed. Before it: a file another program holds, or access denied, is
        /// FileInUse; any other file error is Failed (both logged quietly, like <see cref="NewDatabaseFile"/>);
        /// and an exception nobody anticipated is Failed and reported with LogError so it reaches the error
        /// reporter. Never throws.
        /// </summary>
        /// <param name="ex">The exception.</param>
        private RestoreResult FromException(Exception ex)
        {
            var fileError = ex is IOException or UnauthorizedAccessException;
            if (fileError)
                _log.LogInfo($"DatabaseRestore: file error ({ex.GetType().Name}: {ex.Message})");
            else
                SafeLogError(ex, "DatabaseRestore: unexpected exception while restoring");

            if (_swapAttempted)
                return Result(RestoreOutcome.RenameUnconfirmed, ex);

            return Result(
                fileError && DatabaseValidator.IsLockedOrAccessDenied(ex) ? RestoreOutcome.FileInUse : RestoreOutcome.Failed,
                ex);
        }

        /// <summary>LogError, but never lets the logger's development-time re-throw (<see cref="IAppLogger.ThrowOnError"/>) escape this never-throws routine.</summary>
        /// <param name="ex">The exception to report.</param>
        /// <param name="context">What was happening.</param>
        private void SafeLogError(Exception ex, string context)
        {
            try { _log.LogError(ex, context); }
            catch { /* ThrowOnError re-throws by design in development; the result still carries the exception */ }
        }

        /// <summary>
        /// Asks the DEBUG fault plan whether the stage just reached should fail, and if so logs the injection.
        /// </summary>
        /// <param name="stage">The fault that targets the stage just reached.</param>
        /// <param name="what">What the injected fault pretends happened (log text).</param>
        /// <returns>True when the stage must fail on purpose. Always false when there is no plan (every shipped build).</returns>
        private bool Inject(InjectedRestoreFault stage, string what)
        {
            if (_faults is null || !_faults.TryConsume(stage))
                return false;

            _log.LogInfo($"[DEBUG fault injection] {RestoreFaultPlan.EnvironmentVariableName}: {what}; {_faults.Description}.");
            return true;
        }

        /// <summary>Case-insensitive hash comparison that treats null as "unknown, so not equal".</summary>
        /// <param name="a">One hash (hex), or null.</param>
        /// <param name="b">The other hash (hex), or null.</param>
        private static bool HashesEqual(string? a, string? b)
            => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Deletes the staged local database and every sidecar SQLite may have made. Non-throwing.</summary>
        /// <param name="localPath">The staged local database path.</param>
        private static void DeleteLocalFiles(string localPath)
        {
            TryDelete(localPath);
            DeleteWalSidecars(localPath);
            TryDelete(localPath + "-journal");
        }

        /// <summary>Deletes the <c>-wal</c> and <c>-shm</c> sidecars of <paramref name="dbPath"/>. Non-throwing.</summary>
        /// <param name="dbPath">Database whose sidecars to remove.</param>
        private static void DeleteWalSidecars(string dbPath)
        {
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
        }

        /// <summary>Best-effort local file deletion; failures are ignored.</summary>
        /// <param name="path">File to delete.</param>
        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { /* best effort — temporary file in a temp folder */ }
        }
    }
}

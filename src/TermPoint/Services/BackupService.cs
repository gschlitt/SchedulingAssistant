using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Exceptions;
using TermPoint.Models;
using TermPoint.ViewModels.Management;

namespace TermPoint.Services;

/// <summary>
/// Tri-state outcome of <see cref="BackupService.CheckIntegrityDetailed"/>. The old boolean
/// check returned false for ANY exception, so a healthy file that another program held open
/// (a sharing violation) was reported as damaged — and a user told their database is damaged
/// may restore a backup over a good, current file. This keeps "damaged" for positive evidence
/// only (spec item 4e).
/// </summary>
public enum IntegrityCheckResult
{
    /// <summary>The file opened and <c>PRAGMA integrity_check</c> reported "ok".</summary>
    Ok,

    /// <summary>
    /// Positive evidence of damage: <c>PRAGMA integrity_check</c> reported problems, or SQLite
    /// said the file is corrupt, not a database, or in an unrecognised format.
    /// </summary>
    Damaged,

    /// <summary>
    /// The file could not be examined right now (another program holds it open, access is
    /// denied, an I/O error, or any exception that is not clear evidence of damage). Says nothing
    /// about the file's health — it must never be presented as damage.
    /// </summary>
    Unreadable
}

/// <summary>
/// WHAT kind of evidence lay behind a <see cref="IntegrityCheckResult.Damaged"/> verdict (spec item 5, step 5e).
/// <see cref="IntegrityCheckResult.Damaged"/> deliberately lumps together "SQLite found problems INSIDE a database"
/// and "this does not look like a SQLite database at all"; the damage report must tell them apart, because only the
/// first is worth a developer's attention — the second is far more often a wrong file the user picked than damage.
/// </summary>
public enum DamageEvidence
{
    /// <summary>No evidence of damage: the file passed, could not be examined, or the verdict carries no evidence.</summary>
    None,

    /// <summary><c>PRAGMA integrity_check</c> ran and reported problems inside the database. Reportable.</summary>
    IntegrityProblems,

    /// <summary>SQLite refused the file as corrupt (SQLITE_CORRUPT, 11: "database disk image is malformed"). Reportable.</summary>
    Corrupt,

    /// <summary>
    /// The file is not recognisable as a SQLite database: SQLITE_NOTADB (26), SQLITE_FORMAT (24), or a missing or
    /// short SQLite header. Still a Damaged verdict (the file is unusable), but NOT reportable — it is usually a
    /// wrong file, not damage.
    /// </summary>
    NotADatabase
}

/// <summary>
/// The full account of one integrity check (<see cref="BackupService.CheckIntegrityWithSummary"/>): the verdict, SQLite's
/// short summary of why the file did not pass, and the kind of evidence behind a Damaged verdict.
/// </summary>
/// <param name="Result">The verdict; exactly what <see cref="BackupService.CheckIntegrityDetailed"/> returns.</param>
/// <param name="Summary">SQLite's short, path-free account of why the file did not pass; null when it passed.</param>
/// <param name="Evidence">The evidence behind a Damaged verdict; <see cref="DamageEvidence.None"/> for every other verdict.</param>
public readonly record struct IntegrityCheckOutcome(IntegrityCheckResult Result, string? Summary, DamageEvidence Evidence);

/// <summary>
/// Manages automated SQLite backups and companion section CSV exports.
///
/// <para><b>Backup file naming:</b> <c>{dbName}_{yyyy-MM-dd_HH-mm-ss}.db</c> and
/// <c>{dbName}_{yyyy-MM-dd_HH-mm-ss}_sections.csv</c>, all written to
/// <see cref="AppSettings.BackupFolderPath"/>. Using the database filename as a prefix
/// keeps backups from multiple databases cleanly separated within the same folder.
/// The entire database is always backed up regardless of which academic year is selected,
/// so no academic year slug appears in the filename.</para>
///
/// <para><b>Session lifecycle:</b> Call <see cref="StartSessionAsync"/> when the user
/// acquires the write lock. The service performs an immediate backup and then starts a
/// periodic timer (interval: <see cref="AppSettings.BackupIntervalMinutes"/>). Call
/// <see cref="StopSession"/> (or <see cref="Dispose"/>) to stop the timer on shutdown
/// or database switch.</para>
///
/// <para><b>Rotation:</b> After every backup, the oldest backup pairs are deleted until
/// only <see cref="AppSettings.MaxBackupCount"/> remain.</para>
///
/// <para><b>Integrity check:</b> <see cref="CheckIntegrity"/> and
/// <see cref="CheckIntegrityDetailed"/> are static methods that
/// open a raw connection (without touching the app's live connection) and run
/// <c>PRAGMA integrity_check</c>. They can be called before <see cref="StartSessionAsync"/>
/// to verify the main database before any writes occur.</para>
/// </summary>
public class BackupService : IDisposable
{
    private readonly IDatabaseContext _db;
    private readonly SemesterContext _semesterContext;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly ISchedulingEnvironmentRepository _propertyRepo;
    private readonly ICampusRepository _campusRepo;
    private readonly IAppLogger _logger;

    private Timer? _periodicTimer;
    private readonly SemaphoreSlim _backupGuard = new(1, 1);
    private string? _dbName;   // filename-without-extension prefix for backup filenames
    private bool _disposed;
    private DateTime _lastTimerFire = DateTime.MinValue;

    /// <summary>
    /// Result of the most recent backup attempt, or null if no backup has been attempted
    /// in this session. Updated after every call to <see cref="PerformBackupAsync"/>.
    /// </summary>
    public BackupResult? LastBackupResult { get; private set; }

    /// <summary>
    /// Fired on the UI thread after every backup attempt (success or failure), so that
    /// the Settings view can refresh its status line and backup list without polling.
    /// </summary>
    public event EventHandler? BackupCompleted;

    /// <param name="db">Live database context — used for VACUUM INTO.</param>
    /// <param name="semesterContext">Provides the currently selected academic year for CSV scope.</param>
    /// <param name="sectionRepo">Section data for CSV export.</param>
    /// <param name="courseRepo">Course names for CSV export.</param>
    /// <param name="instructorRepo">Instructor names for CSV export.</param>
    /// <param name="roomRepo">Room names for CSV export.</param>
    /// <param name="semesterRepo">Semester names for CSV export.</param>
    /// <param name="propertyRepo">Section property values (section type, tags, etc.) for CSV export.</param>
    /// <param name="campusRepo">Campus names for CSV export.</param>
    /// <param name="logger">App-wide logger.</param>
    public BackupService(
        IDatabaseContext db,
        SemesterContext semesterContext,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IRoomRepository roomRepo,
        ISemesterRepository semesterRepo,
        ISchedulingEnvironmentRepository propertyRepo,
        ICampusRepository campusRepo,
        IAppLogger logger)
    {
        _db             = db;
        _semesterContext = semesterContext;
        _sectionRepo    = sectionRepo;
        _courseRepo     = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo       = roomRepo;
        _semesterRepo   = semesterRepo;
        _propertyRepo   = propertyRepo;
        _campusRepo     = campusRepo;
        _logger         = logger;
    }

    // ── Session lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// Called when the user acquires the write lock. Performs an immediate backup and
    /// starts the periodic backup timer.
    /// </summary>
    /// <param name="dbPath">Full path to the active database file. Used to derive the backup filename prefix.</param>
    public async Task StartSessionAsync(string dbPath)
    {
        _dbName = Path.GetFileNameWithoutExtension(dbPath);
        await PerformBackupAsync();
        _lastTimerFire = DateTime.UtcNow;
        StartPeriodicTimer();
    }

    /// <summary>
    /// Stops the periodic backup timer without performing a final backup.
    /// Safe to call multiple times. Also called by <see cref="Dispose"/>.
    /// </summary>
    public void StopSession()
    {
        _periodicTimer?.Dispose();
        _periodicTimer = null;
    }

    // ── Core backup logic ────────────────────────────────────────────────────

    /// <summary>
    /// Converts an academic year name (e.g. "2024-2025" or "Fall 2024") into a string
    /// safe to embed in a filename. Spaces become underscores; any character that is not
    /// alphanumeric, a hyphen, or an underscore is removed.
    /// </summary>
    /// <param name="name">Raw academic year name from the data model.</param>
    /// <returns>Filesystem-safe slug, e.g. "2024-2025" or "Fall_2024".</returns>
    private static string ToFileSlug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-') sb.Append(ch);
            else if (ch == ' ')                         sb.Append('_');
            // all other chars silently dropped
        }
        return sb.Length > 0 ? sb.ToString() : "Unknown";
    }


    /// <summary>
    /// Performs one complete backup cycle:
    /// <list type="number">
    ///   <item>VACUUM INTO — creates a compacted snapshot of the live database.</item>
    ///   <item>Integrity check — verifies the backup file is structurally sound.</item>
    ///   <item>CSV export — writes a human-readable section dump alongside the .db file.</item>
    ///   <item>Rotation — deletes the oldest backup pair(s) to stay within <see cref="AppSettings.MaxBackupCount"/>.</item>
    /// </list>
    /// Failures are logged and surfaced via <see cref="LastBackupResult"/>; they do not throw.
    /// </summary>
    /// <returns>A <see cref="BackupResult"/> describing the outcome.</returns>
    public async Task<BackupResult> PerformBackupAsync()
    {
        var settings = AppSettings.Current;
        var folder   = settings.BackupFolderPath;

        if (string.IsNullOrWhiteSpace(folder))
        {
            var r = BackupResult.NoFolderConfigured();
            LastBackupResult = r;
            FireBackupCompleted();
            return r;
        }

        // Deadline-bounded probe — raw Directory.Exists on a dead network share
        // blocks the calling thread for the full SMB redirector timeout (~60s).
        var (dirCheckCompleted, dirExists) = await NetworkFileOps.DirectoryExistsAsync(folder);
        if (!dirCheckCompleted || !dirExists)
        {
            var r = BackupResult.FolderUnavailable(folder);
            // Spec item 7: a local line with the path, a named report without it, and one plain banner per run
            // (this used to be LogError(null, …), which sent BugSnag a report with no exception and put the path in
            // the banner).
            _logger.LogInfo($"Backup folder unavailable ({(dirCheckCompleted ? "missing" : "not responding")}): {folder}");
            HandledEventReporter.Report(HandledEvent.BackupFolderUnavailable, _logger,
                new Dictionary<string, string> { ["reason"] = dirCheckCompleted ? "missing" : "not responding" });
            ShowBackupFailureNoticeOnce(dirCheckCompleted ? BackupFolderMissingNotice : BackupFolderNotRespondingNotice);
            LastBackupResult = r;
            FireBackupCompleted();
            return r;
        }

        var prefix    = _dbName ?? "backup";
        var aySlug    = ToFileSlug(_semesterContext.SelectedAcademicYear?.Name ?? "Unknown");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var dbDest    = Path.Combine(folder, $"{prefix}_{timestamp}.db");
        // CSV name includes the academic year slug because the CSV contains only that
        // year's sections. The .db file contains the whole database, so it gets no slug.
        var csvDest   = Path.Combine(folder, $"{prefix}_{timestamp}_{aySlug}_sections.csv");

        // Step 1 — VACUUM INTO a LOCAL temp file, then stream it to the backup folder.
        // Open a FRESH unpooled connection rather than reusing _db.Connection.
        // Backups can fire from a thread-pool thread (periodic timer) or from a
        // CheckoutService.SaveAsync caller. SqliteConnection is not thread-safe;
        // sharing the live repository connection from a background thread risks
        // SQLITE_MISUSE if a UI write transaction is in flight at the same moment.
        // SQLite serialises across separate connections to the same file, so a
        // fresh handle is the safe path. (F1, data-integrity-agenda 2026-05-04.)
        //
        // The vacuum holds a read lock on the live database for its whole duration,
        // so the destination MUST be local: vacuuming straight to a network backup
        // folder held that lock for the entire SMB transfer, during which any
        // UI-thread write sat frozen in SQLite's busy handler. Vacuum locally
        // (fast, bounded by local disk), verify, then stream the finished file with
        // the stall-aware NetworkFileOps.CopyAsync — the same pattern the save
        // pipeline uses (finding #7). Task.Run keeps the vacuum and the integrity
        // read off the caller's thread: StartSessionAsync awaits this method from
        // the UI thread at write-lock acquisition.
        var localTmp = _db.DatabasePath + ".backuptmp";
        bool integrityOk;
        try
        {
            TryDelete(localTmp); // VACUUM INTO requires that the destination not exist
            await Task.Run(() => VacuumIntoFreshConnection(localTmp));

            // Step 2 — integrity check on the local snapshot (non-fatal if it fails)
            var snapshotCheck = await Task.Run(() => CheckIntegrityWithSummary(localTmp));
            integrityOk = snapshotCheck.Result == IntegrityCheckResult.Ok;
            if (!integrityOk)
                ReportSnapshotFailedIntegrity(snapshotCheck);

            var copyCompleted = await NetworkFileOps.CopyAsync(localTmp, dbDest);
            if (!copyCompleted)
            {
                // Spec item 7: as for an unavailable folder above (local line with the path, named report, one banner).
                _logger.LogInfo($"Backup copy to {dbDest} stalled — backup folder unreachable.");
                HandledEventReporter.Report(HandledEvent.BackupCopyStalled, _logger);
                ShowBackupFailureNoticeOnce(BackupFolderNotRespondingNotice);

                // Remove the partial file under the same network deadline as the copy (spec item 30). The backup
                // folder has just stopped responding, so a plain File.Exists / File.Delete here could hold this
                // thread for the full SMB timeout — and this method runs on the UI thread when the backup starts
                // at lock acquisition or from the Save and Backup panel. Best effort: if the folder is still dead
                // the partial file stays; it has a normal backup name, so rotation deletes it in time, and a
                // restore refuses it because it fails the integrity check.
                if (!await NetworkFileOps.DeleteAsync(dbDest))
                    _logger.LogInfo("Backup: the partial backup file could not be removed in time; rotation will remove it later.");
                var stalled = BackupResult.Failed("Database backup failed: the backup folder did not respond.");
                LastBackupResult = stalled;
                FireBackupCompleted();
                return stalled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VACUUM INTO failed during backup");
            var r = BackupResult.Failed($"Database backup failed: {ex.Message}");
            LastBackupResult = r;
            FireBackupCompleted();
            return r;
        }
        finally
        {
            TryDelete(localTmp);
        }

        // Step 3 — CSV export (non-fatal; DB backup already succeeded)
        bool csvOk = false;
        try
        {
            await WriteSectionsCsvAsync(csvDest);
            csvOk = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Section CSV export failed during backup");
        }

        // Step 4 — rotate old backups
        try
        {
            RotateBackups(folder, prefix, settings.MaxBackupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup rotation failed");
        }

        var result = new BackupResult
        {
            Success         = true,
            Timestamp       = DateTime.Now,
            DbPath          = dbDest,
            CsvPath         = csvOk ? csvDest : null,
            IntegrityPassed = integrityOk
        };

        LastBackupResult = result;
        FireBackupCompleted();
        return result;
    }

    /// <summary>
    /// What the user is told, once per run, when a backup can't be made because the backup folder doesn't respond
    /// (spec item 7; wording approved by Greg 2026-10-26). No path: the Save and Backup panel shows the folder.
    /// </summary>
    public const string BackupFolderNotRespondingNotice =
        "TermPoint couldn't make a backup because the backup folder isn't responding. Your schedule itself isn't " +
        "affected. You can check the backup folder in Save and Backup.";

    /// <summary>
    /// The same notice when the backup folder answered but no longer exists (deleted, renamed, or a drive that is no
    /// longer mapped): "isn't responding" would be untrue there (spec item 7; wording approved by Greg 2026-10-27).
    /// </summary>
    public const string BackupFolderMissingNotice =
        "TermPoint couldn't make a backup because the backup folder can't be found. Your schedule itself isn't " +
        "affected. You can check the backup folder in Save and Backup.";

    /// <summary>1 once a backup-failure notice has been shown in this run; the banner is shown once per run.</summary>
    private static int _backupFailureNoticeShown;

    /// <summary>
    /// Shows <paramref name="notice"/> as a banner, unless a backup-failure banner was already shown in this run: backups
    /// run every 30 minutes, and a dead folder would otherwise raise the same banner each time. The Save and Backup
    /// panel's status line still shows every failure. Thread-safe.
    /// </summary>
    /// <param name="notice">The notice text (one of the backup-folder notices).</param>
    private void ShowBackupFailureNoticeOnce(string notice)
    {
        if (Interlocked.Exchange(ref _backupFailureNoticeShown, 1) == 0)
            _logger.NotifyUser(notice);
    }

    /// <summary>Lets the next backup failure show its banner again. For tests.</summary>
    internal static void ResetBackupFailureNoticeForTests() => Interlocked.Exchange(ref _backupFailureNoticeShown, 0);

    /// <summary>What the user is told when the backup snapshot fails its integrity check (no path, no technical detail).</summary>
    public const string SnapshotFailedIntegrityNotice =
        "The latest backup failed its integrity check, so it may not be usable for a restore.";

    /// <summary>
    /// Handles a backup snapshot that did not pass its integrity check (spec item 5, step 5e), replacing the old
    /// <c>LogError(null, "…failed: {path}")</c>, which sent BugSnag a report with no exception and a local path.
    /// <list type="bullet">
    /// <item><description>Local log: one line with the verdict and SQLite's summary, and no path.</description></item>
    /// <item><description>BugSnag: via <see cref="DatabaseDamageReporter"/> (kind
    /// <see cref="DamagedFileKind.BackupSnapshot"/>), only on positive evidence of damage inside the database, and at
    /// most once per run — a snapshot that merely could not be read is never reported.</description></item>
    /// <item><description>The user: <see cref="SnapshotFailedIntegrityNotice"/> in the notification banner, as before
    /// (<see cref="IAppLogger.NotifyUser"/>: a banner without a report).</description></item>
    /// </list>
    /// The backup itself carries on, as before (the snapshot is still copied; <see cref="BackupResult.IntegrityPassed"/>
    /// records the failure). Never throws.
    /// </summary>
    /// <param name="check">The failed check of the snapshot.</param>
    internal void ReportSnapshotFailedIntegrity(IntegrityCheckOutcome check)
    {
        try
        {
            _logger.LogWarning(
                $"BackupService: the backup snapshot did not pass its integrity check ({check.Result}: {check.Summary}).");
            DatabaseDamageReporter.ReportIfDamaged(DamagedFileKind.BackupSnapshot, check.Evidence, check.Summary, _logger);
            _logger.NotifyUser(SnapshotFailedIntegrityNotice);
        }
        catch (Exception ex)
        {
            // A reporting problem must not turn a successful backup into a failed one.
            try { _logger.LogInfo($"BackupService: could not report the failed snapshot check ({ex.GetType().Name})."); }
            catch { /* the logger itself is failing */ }
        }
    }

    /// <summary>
    /// Runs <c>VACUUM INTO</c> against <see cref="IDatabaseContext.DatabasePath"/> using
    /// a freshly-opened, unpooled <see cref="SqliteConnection"/>. SQLite serialises
    /// concurrent connections to the same file, so this is safe to call from a
    /// background thread while the live repository connection is mid-write.
    /// <para><c>Pooling=False</c> ensures the connection (and its OS file handle) is
    /// fully released on <see cref="IDisposable.Dispose"/> rather than being returned
    /// to the pool — important on Windows so subsequent file operations in
    /// <see cref="CheckoutService"/> are not blocked by a lingering handle.</para>
    /// <para>The vacuum holds a read lock on the source for its entire duration, so
    /// <paramref name="destPath"/> must be a LOCAL path — the caller streams the
    /// finished snapshot to the backup folder afterwards.</para>
    /// </summary>
    /// <param name="destPath">Destination file for the VACUUM INTO output (must not exist; must be local).</param>
    /// <exception cref="InvalidOperationException">The context has no database path.</exception>
    /// <exception cref="SqliteException">
    /// The source cannot be opened — including when it does not exist (it is never created). Internal so
    /// tests can pin that.
    /// </exception>
    internal void VacuumIntoFreshConnection(string destPath)
    {
        var sourcePath = _db.DatabasePath;
        if (string.IsNullOrEmpty(sourcePath))
            throw new InvalidOperationException(
                "BackupService: IDatabaseContext.DatabasePath is empty — cannot run VACUUM INTO.");

        // Mode=ReadWrite — never create. The default (ReadWriteCreate) would re-create an EMPTY
        // database at D' if the working copy had been deleted while the app ran (on macOS unlinking an
        // open file succeeds), and this backup would then ship an empty "backup" to the backup folder
        // and rotate real ones out. With ReadWrite a missing D' is a failed open, reported by the
        // caller's catch as a failed backup. (Same rule as CheckoutService.BackupSqliteDatabase.)
        using (var conn = new SqliteConnection($"Data Source={sourcePath};Mode=ReadWrite;Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path";
            var p = cmd.CreateParameter();
            p.ParameterName = "$path";
            p.Value = destPath;
            cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }

        // Normalize the snapshot to rollback journal mode. The live source runs in WAL
        // (see DatabaseContext.ConfigureConnection); backup archives are plain files at
        // rest and should be self-contained single .db files with no WAL dependency.
        using var destConn = new SqliteConnection($"Data Source={destPath};Pooling=False");
        destConn.Open();
        using var journalMode = destConn.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode=DELETE";
        journalMode.ExecuteScalar();
    }

    // ── Backup list ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all backup entries for the current database, newest first.
    /// Scans <see cref="AppSettings.BackupFolderPath"/> for files matching
    /// <c>{dbName}_*.db</c>. Returns an empty list when no folder is configured
    /// or the folder is unavailable.
    /// </summary>
    /// <summary>
    /// Synchronous overload for backward compatibility. Prefer <see cref="GetBackupsAsync"/>
    /// when calling from the UI thread — this overload blocks on network paths.
    /// </summary>
    public List<BackupEntry> GetBackups() => GetBackupsAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Async version that deadline-bounds the <see cref="Directory.Exists"/> and
    /// <see cref="Directory.GetFiles"/> calls so a dead backup share does not
    /// freeze the UI thread for the full SMB redirector timeout.
    /// </summary>
    public async Task<List<BackupEntry>> GetBackupsAsync()
    {
        var folder = AppSettings.Current.BackupFolderPath;
        var prefix = _dbName
            ?? Path.GetFileNameWithoutExtension(AppSettings.Current.DatabasePath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(folder))
            return new List<BackupEntry>();

        var (completed, exists) = await NetworkFileOps.DirectoryExistsAsync(folder);
        if (!completed || !exists)
            return new List<BackupEntry>();

        var (filesCompleted, files) = await NetworkFileOps.RunAsync(
            () => Directory.GetFiles(folder, $"{prefix}_*.db"), "GetBackups.GetFiles");
        if (!filesCompleted || files is null)
            return new List<BackupEntry>();

        return files
            .Select(f =>
            {
                return new BackupEntry
                {
                    DbPath    = f,
                    CsvPath   = FindCompanionCsv(folder, f),
                    Timestamp = ParseTimestampFromFilename(f) ?? File.GetCreationTime(f)
                };
            })
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    // ── Static integrity check ───────────────────────────────────────────────

    /// <summary>
    /// Opens a temporary connection (read-only by default) to <paramref name="dbPath"/> and runs
    /// <c>PRAGMA integrity_check</c>. Returns true only when SQLite reports "ok"; false on any
    /// structural problem AND on any failure to examine the file at all (a locked or
    /// unreadable file is also false). Callers that must tell "damaged" from "couldn't read it"
    /// apart should use <see cref="CheckIntegrityDetailed"/> instead. This is a static method so
    /// it can be called during startup before the BackupService session has started.
    /// </summary>
    /// <param name="dbPath">Full path to the SQLite file to check.</param>
    /// <param name="mode">
    /// Connection open mode. Defaults to <see cref="SqliteOpenMode.ReadOnly"/>, which never
    /// modifies the file. Pass <see cref="SqliteOpenMode.ReadWrite"/> when checking a working
    /// copy left behind by a crashed session: such a file usually has a hot rollback journal,
    /// which SQLite cannot replay on a read-only connection (the open fails and the file would
    /// be misreported as corrupt). A read-write open replays the journal — completing SQLite's
    /// own crash recovery — and then verifies the result.
    /// </param>
    /// <returns>True if the database passes integrity check; false otherwise.</returns>
    public static bool CheckIntegrity(string dbPath, SqliteOpenMode mode = SqliteOpenMode.ReadOnly)
        => CheckIntegrityDetailed(dbPath, mode) == IntegrityCheckResult.Ok;

    /// <summary>
    /// Like <see cref="CheckIntegrity"/>, but reports WHY a check did not pass: a file that is
    /// genuinely damaged (<see cref="IntegrityCheckResult.Damaged"/>) is distinguished from one
    /// that simply could not be read right now (<see cref="IntegrityCheckResult.Unreadable"/> —
    /// held open exclusively by another program, access denied, I/O error). Never throws.
    ///
    /// <para>Classification is by SQLite's <i>primary</i> result code (the low byte of the
    /// code, so extended codes such as SQLITE_CORRUPT_VTAB fold into their primary code); see
    /// <see cref="ClassifyIntegrityException"/>. A <c>PRAGMA integrity_check</c> answer other than
    /// "ok" is Damaged. Any exception that is not clear evidence of damage is Unreadable and is
    /// logged, so a file is never called damaged without positive evidence.</para>
    ///
    /// <para>Pooled connections are always cleared afterwards (see the <c>finally</c> block), so
    /// the OS file handle is released before the caller copies or replaces the file.</para>
    /// </summary>
    /// <param name="dbPath">Full path to the SQLite file to check.</param>
    /// <param name="mode">Connection open mode — see <see cref="CheckIntegrity"/>.</param>
    /// <returns>
    /// <see cref="IntegrityCheckResult.Ok"/> when SQLite reports "ok";
    /// <see cref="IntegrityCheckResult.Damaged"/> on positive evidence of damage;
    /// <see cref="IntegrityCheckResult.Unreadable"/> when the file could not be examined.
    /// </returns>
    public static IntegrityCheckResult CheckIntegrityDetailed(
        string dbPath, SqliteOpenMode mode = SqliteOpenMode.ReadOnly)
        => CheckIntegrityWithSummary(dbPath, mode).Result;

    /// <summary>
    /// How many lines of <c>PRAGMA integrity_check</c> output <see cref="CheckIntegrityWithSummary"/> keeps in its
    /// summary. SQLite can report up to 100 problems; the first few name the damaged pages and tables, and that is
    /// all a log line or a damage report needs.
    /// </summary>
    internal const int IntegritySummaryMaxLines = 5;

    /// <summary>
    /// <see cref="CheckIntegrityDetailed"/> plus a short SQLite summary of why the file did not pass (spec item 5,
    /// step 5d; the damage report of step 5e uses it). Never throws; the verdict is exactly the one
    /// <see cref="CheckIntegrityDetailed"/> gives.
    ///
    /// <para><b>The summary</b> is null when the verdict is <see cref="IntegrityCheckResult.Ok"/>. When
    /// <c>PRAGMA integrity_check</c> answered with problems (Damaged) it is the first
    /// <see cref="IntegritySummaryMaxLines"/> lines of that output, one per line — these name pages and tables,
    /// never row data. When the verdict came from an exception it is <c>SQLite error {code}: {message}</c> for a
    /// <see cref="SqliteException"/>, or just the exception's type name for anything else (an I/O exception's
    /// own message names the file path, which must never travel). Either way it carries no path and no data.</para>
    ///
    /// <para><b>The evidence</b> (<see cref="DamageEvidence"/>) says what lay behind a Damaged verdict, so the damage
    /// report (step 5e) need not parse the summary text: <c>integrity_check</c> problems →
    /// <see cref="DamageEvidence.IntegrityProblems"/>; SQLITE_CORRUPT → <see cref="DamageEvidence.Corrupt"/>;
    /// SQLITE_NOTADB / SQLITE_FORMAT → <see cref="DamageEvidence.NotADatabase"/>. Every other verdict carries
    /// <see cref="DamageEvidence.None"/>.</para>
    /// </summary>
    /// <param name="dbPath">Full path to the SQLite file to check.</param>
    /// <param name="mode">Connection open mode — see <see cref="CheckIntegrity"/>.</param>
    /// <returns>The verdict, the summary (null when the verdict is Ok) and the evidence behind a Damaged verdict.</returns>
    public static IntegrityCheckOutcome CheckIntegrityWithSummary(
        string dbPath, SqliteOpenMode mode = SqliteOpenMode.ReadOnly)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode       = mode
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";

            // A healthy file answers with ONE row, "ok"; a damaged one with up to 100 rows of problems.
            // Only the first few are kept (reading stops there), and the first row alone decides the verdict,
            // exactly as when only the first row was read.
            using var reader = cmd.ExecuteReader();
            var lines = new List<string>(IntegritySummaryMaxLines);
            while (lines.Count < IntegritySummaryMaxLines && reader.Read())
                lines.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));

            if (lines.Count > 0 && string.Equals(lines[0], "ok", StringComparison.OrdinalIgnoreCase))
                return new IntegrityCheckOutcome(IntegrityCheckResult.Ok, null, DamageEvidence.None);

            // Problems named by integrity_check are positive evidence of damage inside the database. An answer
            // with no rows at all (not something SQLite does) is still Damaged, but names nothing to report.
            return lines.Count > 0
                ? new IntegrityCheckOutcome(IntegrityCheckResult.Damaged, string.Join("\n", lines), DamageEvidence.IntegrityProblems)
                : new IntegrityCheckOutcome(IntegrityCheckResult.Damaged, "integrity_check returned no result", DamageEvidence.None);
        }
        catch (Exception ex)
        {
            var verdict = ClassifyIntegrityException(ex);
            // Logged with the exception type and message so an unanticipated failure is still
            // diagnosable. Deliberately not LogError: that raises a Bugsnag report and a
            // user-facing banner, and a locked file is an ordinary condition.
            App.Logger.LogInfo(
                $"BackupService: integrity check of '{dbPath}' classified as {verdict} ({ex.GetType().Name}: {ex.Message})");
            return new IntegrityCheckOutcome(verdict, SummarizeIntegrityException(ex), ClassifyDamageEvidence(ex));
        }
        finally
        {
            // Microsoft.Data.Sqlite pools connections by default: conn.Dispose() returns the
            // connection to the pool rather than closing it, leaving the OS file handle on
            // dbPath open. ClearAllPools() destroys every pooled entry so the handle is
            // released before CheckoutService copies or replaces dbPath. Without this,
            // File.Move(D.tmp → D) in CheckoutService.SaveAsync fails on Windows with
            // "Access to the path is denied" because the pooled handle lacks FILE_SHARE_DELETE.
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>SQLite primary result code: the database disk image is malformed.</summary>
    private const int SqliteCorrupt = 11;

    /// <summary>SQLite primary result code: the file is not a database ("file is not a database").</summary>
    private const int SqliteNotADb = 26;

    /// <summary>SQLite primary result code: the database format is not recognised.</summary>
    private const int SqliteFormat = 24;

    /// <summary>
    /// Maps an exception thrown while opening or checking a database file to a verdict.
    /// Only a SQLite error that positively says the file's contents are bad is
    /// <see cref="IntegrityCheckResult.Damaged"/>:
    /// <list type="bullet">
    /// <item><description>SQLITE_CORRUPT (11), SQLITE_NOTADB (26), SQLITE_FORMAT (24) → Damaged.</description></item>
    /// <item><description>SQLITE_PERM (3), SQLITE_BUSY (5), SQLITE_LOCKED (6), SQLITE_IOERR (10),
    /// SQLITE_CANTOPEN (14), SQLITE_AUTH (23) → Unreadable. (A sharing violation from another
    /// program holding the file exclusively surfaces as SQLITE_CANTOPEN.)</description></item>
    /// <item><description><see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> →
    /// Unreadable.</description></item>
    /// <item><description>Anything else → Unreadable: with no positive evidence of damage a file
    /// is never called damaged.</description></item>
    /// </list>
    /// The primary code is the low byte of <see cref="SqliteException.SqliteErrorCode"/>, so an
    /// extended code (for example SQLITE_CORRUPT_VTAB, 267) folds into its primary code (11).
    /// </summary>
    /// <param name="ex">The exception caught around the open / <c>PRAGMA integrity_check</c>.</param>
    /// <returns>
    /// <see cref="IntegrityCheckResult.Damaged"/> or <see cref="IntegrityCheckResult.Unreadable"/>
    /// (never <see cref="IntegrityCheckResult.Ok"/>).
    /// </returns>
    internal static IntegrityCheckResult ClassifyIntegrityException(Exception ex)
    {
        if (ex is SqliteException sqlite)
        {
            var primary = sqlite.SqliteErrorCode & 0xFF;
            return primary is SqliteCorrupt or SqliteNotADb or SqliteFormat
                ? IntegrityCheckResult.Damaged
                : IntegrityCheckResult.Unreadable;
        }

        // IOException / UnauthorizedAccessException — and, deliberately, every other exception
        // type — mean "could not examine the file", never "the file is bad".
        return IntegrityCheckResult.Unreadable;
    }

    /// <summary>
    /// Says what kind of evidence of damage an exception thrown while opening or checking a database file is, for the
    /// damage report (spec item 5, step 5e). Agrees with <see cref="ClassifyIntegrityException"/>: exactly the
    /// exceptions that classify as Damaged carry evidence, and nothing else does.
    /// <list type="bullet">
    /// <item><description>SQLITE_CORRUPT (11) → <see cref="DamageEvidence.Corrupt"/>: SQLite recognised the file as one of
    /// its databases and found it malformed.</description></item>
    /// <item><description>SQLITE_NOTADB (26), SQLITE_FORMAT (24) → <see cref="DamageEvidence.NotADatabase"/>: far more often a
    /// wrong file than damage, so it is not reported.</description></item>
    /// <item><description>Anything else → <see cref="DamageEvidence.None"/>.</description></item>
    /// </list>
    /// Like <see cref="ClassifyIntegrityException"/> it looks at the primary code (the low byte).
    /// </summary>
    /// <param name="ex">The exception caught around the open / <c>PRAGMA integrity_check</c>.</param>
    /// <returns>The evidence; <see cref="DamageEvidence.None"/> for anything that is not clear evidence of damage.</returns>
    internal static DamageEvidence ClassifyDamageEvidence(Exception ex)
    {
        if (ex is not SqliteException sqlite)
            return DamageEvidence.None;

        return (sqlite.SqliteErrorCode & 0xFF) switch
        {
            SqliteCorrupt              => DamageEvidence.Corrupt,
            SqliteNotADb or SqliteFormat => DamageEvidence.NotADatabase,
            _                          => DamageEvidence.None
        };
    }

    /// <summary>
    /// The summary for a verdict that came from an exception (see <see cref="CheckIntegrityWithSummary"/>): SQLite's
    /// primary error code and its own message for a <see cref="SqliteException"/>
    /// (<c>SQLite error 26: 'file is not a database'.</c>); only the exception's type name for anything else,
    /// because an <see cref="IOException"/> message names the file path.
    /// </summary>
    /// <param name="ex">The exception caught around the open / <c>PRAGMA integrity_check</c>.</param>
    /// <returns>A short, path-free description.</returns>
    internal static string SummarizeIntegrityException(Exception ex)
    {
        if (ex is not SqliteException sqlite)
            return $"{ex.GetType().Name} (not a SQLite error)";

        // Microsoft.Data.Sqlite words its message "SQLite Error {code}: '{text}'." — the code is stated once, here.
        var detail = sqlite.Message;
        const string prefix = "SQLite Error ";
        if (detail.StartsWith(prefix, StringComparison.Ordinal) && detail.IndexOf(": ", StringComparison.Ordinal) is var colon and > 0)
            detail = detail[(colon + 2)..];

        return $"SQLite error {sqlite.SqliteErrorCode & 0xFF}: {detail}";
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Writes a human-readable CSV of all sections in the currently selected academic year.
    /// Columns are grouped: section-level fields first (AcademicYear … Reserves), then
    /// meeting-level fields (Day … MeetingType). For multi-meeting sections the first row
    /// carries all section-level data; subsequent rows for the same section leave those
    /// columns blank to avoid repetition. Sections with no scheduled meetings still emit
    /// one row (with empty meeting columns) so no section is silently omitted.
    /// All foreign-key IDs are resolved to display names.
    /// </summary>
    /// <param name="destPath">Full path of the CSV file to create (overwrite if exists).</param>
    private async Task WriteSectionsCsvAsync(string destPath)
    {
        // This method runs on a thread-pool thread (backup timer / save caller), but the
        // injected repositories all execute on the shared DatabaseContext connection,
        // which the UI thread may be using at the same moment — and SqliteConnection is
        // not thread-safe (same reasoning as the fresh VACUUM INTO connection above, F1).
        // Read through a fresh, isolated connection whenever a real database file backs
        // the context; fall back to the injected repositories in demo/in-memory contexts,
        // which have no file (and no cross-thread contention).
        using var isolated = IsolatedReadContext.TryCreate(_db.DatabasePath);
        var sectionRepo  = isolated is null ? _sectionRepo    : new SectionRepository(isolated);
        var courseRepo   = isolated is null ? _courseRepo     : new CourseRepository(isolated);
        var instrRepo    = isolated is null ? _instructorRepo : new InstructorRepository(isolated);
        var roomRepo     = isolated is null ? _roomRepo       : new RoomRepository(isolated);
        var semesterRepo = isolated is null ? _semesterRepo   : new SemesterRepository(isolated);
        var propertyRepo = isolated is null ? _propertyRepo   : new SchedulingEnvironmentRepository(isolated);
        var campusRepo   = isolated is null ? _campusRepo     : new CampusRepository(isolated);

        // Build lookup dictionaries from the repositories.
        var courses     = courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructors = instrRepo.GetAll().ToDictionary(i => i.Id);
        var rooms       = roomRepo.GetAll().ToDictionary(r => r.Id);
        var sectionTypes = propertyRepo.GetAll(SchedulingEnvironmentTypes.SectionType)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var campuses     = campusRepo.GetAll()
                                        .ToDictionary(c => c.Id, c => c.Name);
        var tags         = propertyRepo.GetAll(SchedulingEnvironmentTypes.Tag)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var meetingTypes = propertyRepo.GetAll(SchedulingEnvironmentTypes.MeetingType)
                                        .ToDictionary(p => p.Id, p => p.Name);
        var resourceProps = propertyRepo.GetAll(SchedulingEnvironmentTypes.Resource)
                                         .ToDictionary(p => p.Id, p => p.Name);
        var reserveProps  = propertyRepo.GetAll(SchedulingEnvironmentTypes.Reserve)
                                         .ToDictionary(p => p.Id, p => p.Name);

        // Resolve the current academic year so we can label each row.
        var ay = _semesterContext.SelectedAcademicYear;
        var ayName = ay?.Name ?? string.Empty;

        // Load all semesters and sections for the current academic year.
        var allSemesters = ay is not null
            ? semesterRepo.GetAll().Where(s => s.AcademicYearId == ay.Id).ToList()
            : new List<Semester>();

        var sb = new StringBuilder();

        // Header row — section-level columns first, then meeting-level columns.
        AppendCsvRow(sb,
            "AcademicYear", "Semester",
            "CourseCode", "CourseTitle",
            "SectionCode", "Instructors",
            "SectionType", "Campus", "Tags", "Resources", "Reserves",
            "Day", "StartTime", "EndTime", "DurationMin",
            "Room", "Frequency", "MeetingType");

        // Semesters in calendar order (Fall → Winter → Early Summer → Summer → Late Summer).
        // Within each semester, sections sorted by "{CourseCode} {SectionCode}" —
        // the same heading-based alphabetic order used by SectionListViewModel.
        // Materialise to List<Section> immediately so that section.Schedule is
        // stable during the meeting loop (avoids any deferred-execution surprises).
        foreach (var semester in allSemesters.OrderBy(s => s.SortOrder))
        {
            var sections = sectionRepo.GetAll(semester.Id)
                .OrderBy(s =>
                {
                    var code = s.CourseId is not null && courses.TryGetValue(s.CourseId, out var cx)
                               ? cx.CalendarCode : string.Empty;
                    return $"{code} {s.SectionCode}".Trim();
                }, StringComparer.Ordinal)
                .ToList();

            foreach (var section in sections)
            {
                // Resolve names
                var courseCode  = section.CourseId is not null && courses.TryGetValue(section.CourseId, out var c)
                                  ? c.CalendarCode : string.Empty;
                var courseTitle = section.CourseId is not null && courses.TryGetValue(section.CourseId, out var ct)
                                  ? ct.Title : string.Empty;

                var instructorNames = section.InstructorAssignments
                    .Select(a => instructors.TryGetValue(a.InstructorId, out var ins)
                                 ? $"{ins.FirstName} {ins.LastName}".Trim()
                                 : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();
                var instructorsCsv = string.Join("; ", instructorNames);

                var sectionType = section.SectionTypeId is not null
                                  && sectionTypes.TryGetValue(section.SectionTypeId, out var st)
                                  ? st : string.Empty;
                var campus      = section.CampusId is not null
                                  && campuses.TryGetValue(section.CampusId, out var cp)
                                  ? cp : string.Empty;
                var tagNames    = section.TagIds
                    .Select(id => tags.TryGetValue(id, out var tn) ? tn : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();
                var tagsCsv = string.Join("; ", tagNames);

                var resourceNames = section.ResourceIds
                    .Select(id => resourceProps.TryGetValue(id, out var rn) ? rn : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList();
                var resourcesCsv = string.Join("; ", resourceNames);

                // Reserves are displayed as "Name (Code)" pairs, e.g. "EDUC 101 (50); NURS 200 (25)".
                var reserveNames = section.Reserves
                    .Select(r =>
                    {
                        var name = reserveProps.TryGetValue(r.ReserveId, out var rn) ? rn : string.Empty;
                        return name.Length > 0 ? $"{name} ({r.Code})" : string.Empty;
                    })
                    .Where(n => n.Length > 0)
                    .ToList();
                var reservesCsv = string.Join("; ", reserveNames);

                if (section.Schedule.Count == 0)
                {
                    // No scheduled meetings — still emit one row so the section is visible.
                    AppendCsvRow(sb,
                        ayName, semester.Name,
                        courseCode, courseTitle,
                        section.SectionCode, instructorsCsv,
                        sectionType, campus, tagsCsv, resourcesCsv, reservesCsv,
                        "", "", "", "",
                        "", "", "");
                }
                else
                {
                    // First meeting row carries all section-level info.
                    // Subsequent meeting rows for the same section leave section-level columns
                    // blank so the CSV is easier to read — the data isn't repeated unnecessarily.
                    bool firstMeeting = true;
                    foreach (var meeting in section.Schedule)
                    {
                        var dayName     = DayName(meeting.Day);
                        var startStr    = MinutesToTimeString(meeting.StartMinutes);
                        var endStr      = MinutesToTimeString(meeting.EndMinutes);
                        var durStr      = meeting.DurationMinutes.ToString();
                        var roomName    = meeting.RoomId is not null
                                          && rooms.TryGetValue(meeting.RoomId, out var rm)
                                          ? $"{rm.Building} {rm.RoomNumber}".Trim()
                                          : string.Empty;
                        var freq        = SectionDaySchedule.FormatFrequency(meeting.Frequency);
                        var meetingType = meeting.MeetingTypeId is not null
                                          && meetingTypes.TryGetValue(meeting.MeetingTypeId, out var mt)
                                          ? mt : string.Empty;

                        if (firstMeeting)
                        {
                            AppendCsvRow(sb,
                                ayName, semester.Name,
                                courseCode, courseTitle,
                                section.SectionCode, instructorsCsv,
                                sectionType, campus, tagsCsv, resourcesCsv, reservesCsv,
                                dayName, startStr, endStr, durStr,
                                roomName, freq, meetingType);
                            firstMeeting = false;
                        }
                        else
                        {
                            // Continuation row — section-level columns left blank.
                            AppendCsvRow(sb,
                                "", "",
                                "", "",
                                "", "",
                                "", "", "", "", "",
                                dayName, startStr, endStr, durStr,
                                roomName, freq, meetingType);
                        }
                    }
                }
            }
        }

        await File.WriteAllTextAsync(destPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Enforces the backup retention limit. Oldest backup pairs (db + csv) are deleted
    /// until the count is at or below <paramref name="maxCount"/>.
    /// </summary>
    /// <param name="folder">Backup folder path.</param>
    /// <param name="prefix">Database name prefix, e.g. "ComputerScience".</param>
    /// <param name="maxCount">Maximum number of periodic backup pairs to retain.</param>
    internal static void RotateBackups(string folder, string prefix, int maxCount)
    {
        // Clamp to a sensible minimum to prevent accidental deletion of everything.
        maxCount = Math.Max(maxCount, 1);

        var files = Directory.GetFiles(folder, $"{prefix}_*.db")
                             .OrderBy(f => f)   // ascending = oldest first
                             .ToList();

        while (files.Count > maxCount)
        {
            var oldest = files[0];
            files.RemoveAt(0);

            TryDelete(oldest);

            // Delete companion CSV — the CSV name includes an academic year slug between
            // the timestamp and "_sections.csv", so derive it via glob rather than string
            // substitution. e.g. DB: MATH-TT_2026-04-05_21-15-25.db →
            // CSV: MATH-TT_2026-04-05_21-15-25_2025-2026_sections.csv
            var companionCsv = FindCompanionCsv(folder, oldest);
            if (companionCsv != null) TryDelete(companionCsv);
        }
    }

    /// <summary>Starts or restarts the periodic backup timer using the current interval setting.</summary>
    private void StartPeriodicTimer()
    {
        _periodicTimer?.Dispose();

        var intervalMinutes = Math.Max(AppSettings.Current.BackupIntervalMinutes, 1);
        var interval        = TimeSpan.FromMinutes(intervalMinutes);

        _periodicTimer = new Timer(
            _ => _ = RunGuardedBackupAsync(),
            null,
            interval,   // first fire after one interval (we already backed up at session start)
            interval);
    }

    /// <summary>
    /// Runs <see cref="PerformBackupAsync"/> inside a semaphore guard so that overlapping
    /// timer ticks are skipped rather than executing concurrently. Exceptions are caught
    /// and logged to prevent unobserved task exceptions on the thread pool.
    ///
    /// <para>Wake-from-sleep guard: if the timer fires more than one minute later than
    /// expected (indicating the machine was suspended), the backup is deferred by 60 seconds
    /// to give network shares time to reconnect before VACUUM INTO is attempted.</para>
    /// </summary>
    private async Task RunGuardedBackupAsync()
    {
        if (!await _backupGuard.WaitAsync(0)) return; // previous backup still running — skip this tick
        try
        {
            // Detect wake-from-sleep: the timer interval is BackupIntervalMinutes, but
            // System.Threading.Timer fires immediately after resume if the deadline passed
            // while the machine was asleep.  A gap > interval + 1 min is a reliable signal.
            var intervalMinutes = Math.Max(AppSettings.Current.BackupIntervalMinutes, 1);
            var expectedInterval = TimeSpan.FromMinutes(intervalMinutes);
            var now = DateTime.UtcNow;

            if (_lastTimerFire != DateTime.MinValue &&
                (now - _lastTimerFire) > expectedInterval + TimeSpan.FromMinutes(1))
            {
                _logger.LogInfo("BackupService: wake-from-sleep detected, deferring backup 60 s");
                _backupGuard.Release();
                await Task.Delay(TimeSpan.FromSeconds(60));
                if (!await _backupGuard.WaitAsync(0)) return;
            }

            _lastTimerFire = now;
            await PerformBackupAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Periodic backup failed");
        }
        finally
        {
            _backupGuard.Release();
        }
    }

    /// <summary>
    /// Fires <see cref="BackupCompleted"/> on the UI thread so subscribers can safely update
    /// observable properties.
    /// </summary>
    private void FireBackupCompleted()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => BackupCompleted?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Tries to parse the timestamp embedded in a backup filename, e.g.
    /// <c>ComputerScience_2026-03-20_14-30-00.db</c> → 2026-03-20 14:30:00.
    /// Returns null when the filename does not match the expected pattern.
    /// </summary>
    internal static DateTime? ParseTimestampFromFilename(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        // Expected suffix: _yyyy-MM-dd_HH-mm-ss  (19 chars + 1 underscore = 20)
        if (name.Length < 20) return null;
        var suffix = name[^19..];   // last 19 chars: "yyyy-MM-dd_HH-mm-ss"
        if (DateTime.TryParseExact(suffix, "yyyy-MM-dd_HH-mm-ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    /// <summary>
    /// The inverse of the backup naming (<c>{dbName}_{yyyy-MM-dd_HH-mm-ss}.db</c>): recovers the name of the
    /// database a backup file is a backup of, e.g. <c>ComputerScience_2026-03-20_14-30-00.db</c> →
    /// <c>ComputerScience</c>. Uses <see cref="ParseTimestampFromFilename"/> so the two can never disagree about
    /// what a backup name looks like. The database chooser uses it to offer the original database's name as the
    /// restore destination.
    /// </summary>
    /// <param name="filePath">A backup file name or full path.</param>
    /// <returns>The database name without extension, or null when the name is not a backup name (no timestamp suffix, or nothing before it).</returns>
    internal static string? TryGetDatabaseNameFromBackupFilename(string filePath)
    {
        if (ParseTimestampFromFilename(filePath) is null) return null;

        var name = Path.GetFileNameWithoutExtension(filePath);
        // The timestamp is the last 19 characters; the character before it must be the separating underscore,
        // and something must come before that (an empty database name is not a backup name).
        return name.Length > 20 && name[^20] == '_' ? name[..^20] : null;
    }

    /// <summary>
    /// Locates the companion CSV file for a given database backup file.
    /// The DB is named <c>{prefix}_{timestamp}.db</c>; the CSV is named
    /// <c>{prefix}_{timestamp}_{aySlug}_sections.csv</c>. Because the slug varies,
    /// the companion is found by scanning for files matching
    /// <c>{prefix}_{timestamp}_*_sections.csv</c>.
    /// Returns null when no companion exists.
    /// </summary>
    /// <param name="folder">Backup folder to search.</param>
    /// <param name="dbPath">Full path to the .db backup file.</param>
    /// <returns>Full path to the companion CSV, or null if none found.</returns>
    private static string? FindCompanionCsv(string folder, string dbPath)
    {
        var stem = Path.GetFileNameWithoutExtension(dbPath);  // e.g. "MATH-TT_2026-04-05_21-15-25"
        return Directory
            .GetFiles(folder, $"{stem}_*_sections.csv")
            .FirstOrDefault();
    }

    /// <summary>Converts minutes-from-midnight to a display string, e.g. 510 → "8:30 AM".</summary>
    private static string MinutesToTimeString(int totalMinutes)
    {
        var hours   = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var dt      = new DateTime(2000, 1, 1, hours, minutes, 0);
        return dt.ToString("h:mm tt");
    }

    /// <summary>Returns the full English day name for day codes 1–7 (1=Monday … 7=Sunday).</summary>
    private static string DayName(int day) => day switch
    {
        1 => "Monday",
        2 => "Tuesday",
        3 => "Wednesday",
        4 => "Thursday",
        5 => "Friday",
        6 => "Saturday",
        7 => "Sunday",
        _ => day.ToString()
    };

    /// <summary>
    /// Appends one RFC-4180-compliant CSV row to <paramref name="sb"/>.
    /// Values containing commas, double quotes, or newlines are quoted and internal
    /// double quotes are doubled.
    /// </summary>
    private static void AppendCsvRow(StringBuilder sb, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var v = values[i] ?? string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r'))
            {
                sb.Append('"');
                sb.Append(v.Replace("\"", "\"\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(v);
            }
        }
        sb.AppendLine();
    }

    /// <summary>Deletes a file, swallowing any exceptions (e.g. file already gone).</summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Minimal <see cref="IDatabaseContext"/> over a fresh, unpooled connection to the live
    /// database file. Lets <see cref="WriteSectionsCsvAsync"/> run repository queries from
    /// the backup thread without touching the shared (non-thread-safe) DatabaseContext
    /// connection. Opened read-write (never writes) rather than read-only because a
    /// read-only connection cannot always attach to a WAL database on its own.
    /// </summary>
    private sealed class IsolatedReadContext : IDatabaseContext
    {
        private readonly SqliteConnection _conn;

        /// <inheritdoc/>
        public DbConnection Connection => _conn;

        /// <inheritdoc/>
        public string DatabasePath { get; }

        /// <inheritdoc/>
        public bool SupportsTransactions => true;

        /// <inheritdoc/>
        public void MarkDirty() { }  // read-only usage — no user writes ever flow through here

        /// <inheritdoc/>
        public void ResetDirty() { }

        private IsolatedReadContext(string dbPath)
        {
            DatabasePath = dbPath;
            _conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode       = SqliteOpenMode.ReadWrite,
                Pooling    = false,
            }.ToString());
            _conn.Open();
        }

        /// <summary>
        /// Opens an isolated connection to <paramref name="dbPath"/>, or returns null when
        /// the context has no backing file (demo/in-memory contexts).
        /// </summary>
        public static IsolatedReadContext? TryCreate(string dbPath)
            => string.IsNullOrEmpty(dbPath) ? null : new IsolatedReadContext(dbPath);

        /// <inheritdoc/>
        public void Dispose() => _conn.Dispose();
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSession();
        _backupGuard.Dispose();
    }
}

// ── Supporting types ─────────────────────────────────────────────────────────

/// <summary>
/// Outcome of a single <see cref="BackupService.PerformBackupAsync"/> call.
/// </summary>
public class BackupResult
{
    /// <summary>True when the .db backup file was written successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When the backup completed. Null when <see cref="Success"/> is false.</summary>
    public DateTime? Timestamp { get; init; }

    /// <summary>Full path of the written .db backup file. Null when <see cref="Success"/> is false.</summary>
    public string? DbPath { get; init; }

    /// <summary>Full path of the written sections CSV. Null when the CSV step failed or was skipped.</summary>
    public string? CsvPath { get; init; }

    /// <summary>
    /// True when <c>PRAGMA integrity_check</c> passed on the backup file.
    /// Only meaningful when <see cref="Success"/> is true.
    /// </summary>
    public bool IntegrityPassed { get; init; }

    /// <summary>
    /// A short one-line summary suitable for display in the Settings view status line.
    /// </summary>
    public string StatusSummary =>
        Success
            ? $"Last backup: {Timestamp:yyyy-MM-dd  HH:mm:ss}" +
              (IntegrityPassed ? "  ✓" : "  ⚠ integrity check failed")
            : $"Backup failed: {ErrorMessage}";

    /// <inheritdoc cref="BackupResult"/>
    public static BackupResult NoFolderConfigured() => new()
        { Success = false, ErrorMessage = "No backup folder configured." };

    /// <inheritdoc cref="BackupResult"/>
    public static BackupResult FolderUnavailable(string folder) => new()
        { Success = false, ErrorMessage = $"Backup folder not accessible: {folder}" };

    /// <inheritdoc cref="BackupResult"/>
    public static BackupResult Failed(string message) => new()
        { Success = false, ErrorMessage = message };
}

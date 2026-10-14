using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="DatabaseRestore"/> — the shared, safe "replace D with a backup" routine (spec item 5,
/// step 5a). Three temp folders stand in for the real world: a "share" holding D, a "backup" folder holding the
/// backup files, and a separate LOCAL folder the routine stages into (so a test can prove nothing is left
/// behind there). The recurring promise being proved: every outcome except Restored / RenameUnconfirmed leaves D
/// byte-identical and leaves NOTHING behind next to it (no D.tmp, no "before restore" file), and the routine
/// never throws.
/// </summary>
public sealed class DatabaseRestoreTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Stands in for the shared folder holding D.</summary>
    private readonly string _shareDir;

    /// <summary>Holds the backup files.</summary>
    private readonly string _backupDir;

    /// <summary>The local staging folder handed to the routine; must be empty afterwards.</summary>
    private readonly string _localDir;

    /// <summary>The "current time" handed to every restore, so the pre-restore file name is predictable.</summary>
    private static readonly DateTime Now = new(2026, 10, 13, 14, 5, 0);

    /// <summary>Records what the routine logs, so tests can assert fault injections and unexpected errors.</summary>
    private readonly RecordingLogger _log = new();

    /// <summary>Creates isolated folders for this test instance.</summary>
    public DatabaseRestoreTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir  = Path.Combine(Path.GetTempPath(), $"dr_share_{id}");
        _backupDir = Path.Combine(Path.GetTempPath(), $"dr_backup_{id}");
        _localDir  = Path.Combine(Path.GetTempPath(), $"dr_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_backupDir);
        Directory.CreateDirectory(_localDir);
    }

    /// <summary>Removes all three folders (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var dir in new[] { _shareDir, _backupDir, _localDir })
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Path of D inside the "share" folder.</summary>
    private string TargetPath(string name = "dept.db") => Path.Combine(_shareDir, name);

    /// <summary>Path of a backup file inside the backup folder.</summary>
    private string BackupPath(string name = "dept_2026-10-13_13-00-00.db") => Path.Combine(_backupDir, name);

    /// <summary>The pre-restore file name the routine will pick first for <see cref="TargetPath"/> at <see cref="Now"/>.</summary>
    private string ExpectedPreRestorePath(int attempt = 1)
        => DatabaseRestore.PreRestoreCopyName(TargetPath(), Now, attempt);

    /// <summary>
    /// Creates a real SQLite database at <paramref name="path"/> holding one row, <paramref name="marker"/>, in a
    /// <c>marker</c> table — so a test can tell WHICH database a file is by content. Pooling is off so no handle
    /// outlives the call. With <paramref name="wal"/> the file is left with a WAL-mode header (bytes 18/19 = 2),
    /// as an older or hand-picked backup might be. With <paramref name="termPointTables"/> (the default) the file
    /// also carries empty <c>Sections</c>, <c>Semesters</c> and <c>AcademicYears</c> tables — the minimum the
    /// routine requires of a backup before it accepts it as a TermPoint database; pass false for a "foreign"
    /// SQLite file that has only the marker table.
    /// </summary>
    private static void CreateMarkerDb(string path, string marker, bool wal = false, bool termPointTables = true)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();

        if (wal)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL";
            pragma.ExecuteScalar();
        }

        using var cmd = conn.CreateCommand();
        var tables = termPointTables
            ? string.Concat(DatabaseRestore.RequiredTables.Select(t => $"CREATE TABLE {t} (key TEXT, data TEXT); "))
            : string.Empty;
        cmd.CommandText = tables + "CREATE TABLE marker (val TEXT); INSERT INTO marker VALUES (@v);";
        cmd.Parameters.AddWithValue("@v", marker);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the marker row of the database at <paramref name="path"/> from a scratch COPY (so the file under
    /// test is never opened by SQLite).
    /// </summary>
    private string ReadMarker(string path)
    {
        var copy = Path.Combine(_localDir, $"read_{Guid.NewGuid():N}.db");
        File.Copy(path, copy);
        try
        {
            using var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT val FROM marker";
            return (string)cmd.ExecuteScalar()!;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }
    }

    /// <summary>Returns the value of <c>PRAGMA journal_mode</c> for a scratch COPY of the database at <paramref name="path"/>.</summary>
    private string JournalModeOfCopy(string path)
    {
        var copy = Path.Combine(_localDir, $"jm_{Guid.NewGuid():N}.db");
        File.Copy(path, copy);
        try
        {
            using var conn = new SqliteConnection($"Data Source={copy};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode";
            return (string)cmd.ExecuteScalar()!;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }
    }

    /// <summary>The file-format write/read version bytes (header offsets 18 and 19): 1/1 = rollback journal, 2 = WAL.</summary>
    private static (byte Write, byte Read) FormatVersions(string path)
    {
        var header = new byte[20];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.ReadExactly(header);
        return (header[18], header[19]);
    }

    /// <summary>SHA-256 of a file (shared open).</summary>
    private static string Hash(string path) => DbFileTestHelpers.HashOf(path);

    /// <summary>The lock check of a caller that always still holds the lock.</summary>
    private static Task<RestoreLockCheck> AlwaysOurs() => Task.FromResult(RestoreLockCheck.Ours);

    /// <summary>
    /// Runs the routine under test against the fixture folders.
    /// </summary>
    /// <param name="backup">Backup path.</param>
    /// <param name="target">D.</param>
    /// <param name="expectedHash">D's hash as the "caller" knows it.</param>
    /// <param name="checkLock">Lock check; defaults to "always ours".</param>
    /// <param name="plan">Optional fault plan.</param>
    private Task<RestoreResult> ReplaceAsync(
        string backup, string target, string expectedHash,
        Func<Task<RestoreLockCheck>>? checkLock = null, RestoreFaultPlan? plan = null)
        => DatabaseRestore.ReplaceAsync(backup, target, expectedHash, checkLock ?? AlwaysOurs, Now, _localDir, plan, _log);

    /// <summary>Creates D (marker "old-d") and returns its bytes and hash.</summary>
    private (byte[] Bytes, string Hash) CreateTarget(string? path = null)
    {
        var target = path ?? TargetPath();
        CreateMarkerDb(target, "old-d");
        return (File.ReadAllBytes(target), Hash(target));
    }

    /// <summary>Entries in the share folder, sorted, for exact "nothing else is there" assertions.</summary>
    private string[] ShareEntries() => Directory.GetFileSystemEntries(_shareDir).OrderBy(e => e, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Asserts the "refused cleanly" invariant: D byte-identical and the only thing in the share folder, and the
    /// local staging folder empty.
    /// </summary>
    private void AssertDatabaseUntouched(string target, byte[] bytesBefore)
    {
        Assert.Equal(bytesBefore, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }

    /// <summary>
    /// A lock check that answers from a script (the last answer repeats) and counts its calls; an optional hook
    /// runs on each call so a test can observe the share at that exact moment.
    /// </summary>
    private sealed class LockScript
    {
        private readonly RestoreLockCheck[] _answers;
        private readonly Action<int>? _onCall;

        /// <summary>How many times the routine has asked.</summary>
        public int Calls { get; private set; }

        /// <summary>Creates a script.</summary>
        /// <param name="onCall">Runs on each call with the 1-based call number, before the answer is returned; may be null.</param>
        /// <param name="answers">The answers, one per call; the last repeats.</param>
        public LockScript(Action<int>? onCall, params RestoreLockCheck[] answers)
        {
            _onCall  = onCall;
            _answers = answers;
        }

        /// <summary>The <c>checkLock</c> delegate.</summary>
        public Task<RestoreLockCheck> CheckAsync()
        {
            Calls++;
            _onCall?.Invoke(Calls);
            return Task.FromResult(_answers[Math.Min(Calls, _answers.Length) - 1]);
        }
    }

    /// <summary>Test logger that remembers info lines and errors.</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        /// <summary>Every <see cref="LogInfo"/> message.</summary>
        public List<string> Infos { get; } = new();

        /// <summary>Every <see cref="LogError"/> call.</summary>
        public List<(Exception? Ex, string? Context)> Errors { get; } = new();

        /// <summary>Every <see cref="ReportHandled"/> call: the damage reports of spec item 5, step 5e.</summary>
        public List<HandledReport> Handled { get; } = new();

        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) => Errors.Add((ex, context));
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) => Infos.Add(message);
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
        /// <inheritdoc/>
        public void ReportHandled(
            Exception ex, string context, IReadOnlyDictionary<string, string>? metadata = null,
            string metadataTab = "details", string? groupingKey = null)
            => Handled.Add(new HandledReport(ex, context, metadata, metadataTab, groupingKey));
    }

    // ── Damage reporting (spec item 5, step 5e) ───────────────────────────────

    /// <summary>
    /// A backup with positive evidence of damage inside it (a trashed page) is refused as before AND reported once as a
    /// damaged backup being restored — with SQLite's summary and none of the fixture's paths.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_BackupHasACorruptedPage_IsReportedAsBackupBeingRestored()
    {
        var backup = BackupPath();
        DbFileTestHelpers.CreateHealthyDatabase(backup);
        DbFileTestHelpers.TrashPage(backup, 2);

        await AssertBackupRefusedAsync(backup, RestoreOutcome.BackupDamaged);

        var report = Assert.Single(_log.Handled);
        Assert.Equal("BackupBeingRestored", report.Metadata!["kind"]);
        Assert.Equal("Database damage: BackupBeingRestored", report.Context);
        Assert.False(string.IsNullOrWhiteSpace(report.Metadata["summary"]));
        foreach (var text in report.Metadata.Values.Append(report.Exception.Message).Append(report.Context))
        {
            Assert.DoesNotContain(_backupDir, text);
            Assert.DoesNotContain(_localDir, text);
            Assert.DoesNotContain(Path.GetFileName(backup), text);
        }
    }

    /// <summary>
    /// A backup that is not a SQLite database at all (random bytes, a text file, an empty file — the usual wrong-file
    /// picks) is still refused as damaged but is NOT reported.
    /// </summary>
    [Theory]
    [InlineData("random")]
    [InlineData("text")]
    [InlineData("empty")]
    public async Task ReplaceAsync_BackupIsNotADatabase_IsRefusedButNotReported(string kind)
    {
        switch (kind)
        {
            case "random": File.WriteAllBytes(BackupPath(), Enumerable.Range(0, 8192).Select(i => (byte)(i * 31 + 7)).ToArray()); break;
            case "text":   File.WriteAllText(BackupPath(), "this is a notes file somebody picked by mistake"); break;
            case "empty":  File.WriteAllBytes(BackupPath(), Array.Empty<byte>()); break;
        }

        await AssertBackupRefusedAsync(BackupPath(), RestoreOutcome.BackupDamaged);

        Assert.Empty(_log.Handled);
    }

    /// <summary>
    /// The routine's own DEBUG <c>damaged</c> fault is refused as damaged but never reported — fake damage must not
    /// reach BugSnag — and the log says so.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_InjectedDamagedFault_IsNotReported_AndTheLogSaysSo()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, oldHash, plan: RestoreFaultPlan.Parse("damaged"));

        Assert.Equal(RestoreOutcome.BackupDamaged, result.Outcome);
        Assert.Empty(_log.Handled);
        Assert.Contains(_log.Infos, m => m.Contains("injected by a DEBUG fault plan") && m.Contains("no report is sent"));
    }

    /// <summary>A healthy backup, and a healthy one that is not a TermPoint database, are not reported either.</summary>
    [Fact]
    public async Task ReplaceAsync_HealthyBackups_AreNotReported()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var good = BackupPath();
        CreateMarkerDb(good, "from-backup");
        var foreign = BackupPath("foreign.db");
        CreateMarkerDb(foreign, "other program", termPointTables: false);

        await ReplaceAsync(foreign, target, oldHash);          // refused: not a TermPoint database
        var restored = await ReplaceAsync(good, target, oldHash);

        Assert.Equal(RestoreOutcome.Restored, restored.Outcome);
        Assert.Empty(_log.Handled);
    }

    /// <summary>Creating a database from a damaged backup (the chooser's new-destination path) reports it the same way.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_BackupWithACorruptedPage_IsReportedAsBackupBeingRestored()
    {
        var backup = BackupPath();
        DbFileTestHelpers.CreateHealthyDatabase(backup);
        DbFileTestHelpers.TrashPage(backup, pageNumber: 2);

        var result = await CreateAsync(backup, TargetPath());

        Assert.Equal(RestoreOutcome.BackupDamaged, result.Outcome);
        Assert.Equal("BackupBeingRestored", Assert.Single(_log.Handled).Metadata!["kind"]);
    }

    // ── Success ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The happy path, end to end: D afterwards holds the backup's content (proved by a marker row only the
    /// backup has), the result carries D's new hash, the OLD D sits beside it under the "before restore" name
    /// byte-for-byte (and still opens), no D.tmp or stray file remains, the local staging folder is empty, the
    /// backup file itself is untouched — and D is in rollback-journal mode even though the backup was WAL-mode.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_Success_ReplacesDKeepsPreRestoreCopyAndLeavesNoDebris()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup", wal: true);
        var backupBytes = File.ReadAllBytes(backup);
        Assert.Equal((byte)2, FormatVersions(backup).Write);   // sanity: the test backup really is WAL-flagged

        var result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.False(result.TargetUnchanged);
        Assert.Null(result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));

        // D holds the backup's content, and the result reports exactly D's hash.
        Assert.Equal(result.RestoredHash, Hash(target));
        Assert.Equal("from-backup", ReadMarker(target));

        // The old D is kept beside it, named as agreed, byte-identical, and still a working database.
        var expectedPre = ExpectedPreRestorePath();
        Assert.Equal(expectedPre, result.PreRestoreCopyPath);
        Assert.Equal("dept (before restore 2026-10-13 14-05).db", Path.GetFileName(expectedPre));
        Assert.Equal(oldBytes, File.ReadAllBytes(expectedPre));
        Assert.Equal("old-d", ReadMarker(expectedPre));

        // Nothing else was left on the share, and the backup was only copied.
        Assert.False(File.Exists(target + ".tmp"));
        Assert.False(File.Exists(expectedPre + ".tmp"));
        Assert.Equal(new[] { target, expectedPre }.OrderBy(e => e, StringComparer.Ordinal), ShareEntries());
        Assert.Equal(backupBytes, File.ReadAllBytes(backup));

        // WAL must never be active on the shared file.
        Assert.Equal(((byte)1, (byte)1), FormatVersions(target));
        Assert.Equal("delete", JournalModeOfCopy(target), ignoreCase: true);
    }

    /// <summary>D is never opened by SQLite: a D that is not even a database is still replaced and preserved intact.</summary>
    [Fact]
    public async Task ReplaceAsync_DNeedNotBeADatabase_BecauseItIsNeverOpenedBySqlite()
    {
        var target = TargetPath();
        File.WriteAllText(target, "not a sqlite file, just bytes");
        var oldBytes = File.ReadAllBytes(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, Hash(target));

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.Equal("from-backup", ReadMarker(target));
        Assert.Equal(oldBytes, File.ReadAllBytes(result.PreRestoreCopyPath!));
    }

    /// <summary>A local staging folder that does not exist yet is created, and still ends up empty.</summary>
    [Fact]
    public async Task ReplaceAsync_MissingLocalFolder_IsCreated()
    {
        var (_, oldHash) = CreateTarget();
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var staging = Path.Combine(_localDir, "not", "yet");

        var result = await DatabaseRestore.ReplaceAsync(backup, TargetPath(), oldHash, AlwaysOurs, Now, staging, null, _log);

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(staging));
    }

    // ── A bad backup is refused, and D is left alone ──────────────────────────

    /// <summary>
    /// Runs a restore of <paramref name="backup"/> against a healthy D and asserts the given refusal with D
    /// byte-identical, no pre-restore file, no D.tmp, nothing in the local folder.
    /// </summary>
    private async Task<RestoreResult> AssertBackupRefusedAsync(string backup, RestoreOutcome expected)
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);

        var result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(expected, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.True(result.TargetUnchanged);
        Assert.Null(result.PreRestoreCopyPath);
        Assert.Null(result.RestoredHash);
        AssertDatabaseUntouched(target, oldBytes);
        return result;
    }

    /// <summary>A backup that is random bytes is damaged.</summary>
    [Fact]
    public async Task ReplaceAsync_BackupIsRandomBytes_IsBackupDamaged()
    {
        var bytes = new byte[8192];
        new Random(12345).NextBytes(bytes);
        File.WriteAllBytes(BackupPath(), bytes);

        await AssertBackupRefusedAsync(BackupPath(), RestoreOutcome.BackupDamaged);
    }

    /// <summary>A backup that is a text file is damaged.</summary>
    [Fact]
    public async Task ReplaceAsync_BackupIsTextFile_IsBackupDamaged()
    {
        File.WriteAllText(BackupPath(), "this is a notes file somebody picked by mistake");

        await AssertBackupRefusedAsync(BackupPath(), RestoreOutcome.BackupDamaged);
    }

    /// <summary>
    /// A zero-byte backup is damaged. SQLite itself treats an empty file as a valid empty database and passes its
    /// integrity check, so without the signature check a 0-byte "backup" would replace D with nothing.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_BackupIsEmptyFile_IsBackupDamaged()
    {
        File.WriteAllBytes(BackupPath(), Array.Empty<byte>());

        await AssertBackupRefusedAsync(BackupPath(), RestoreOutcome.BackupDamaged);
    }

    /// <summary>A genuine SQLite file with a trashed page fails SQLite's integrity check and is damaged.</summary>
    [Fact]
    public async Task ReplaceAsync_BackupHasACorruptedPage_IsBackupDamaged()
    {
        var backup = BackupPath();
        DbFileTestHelpers.CreateHealthyDatabase(backup);
        DbFileTestHelpers.TrashPage(backup, 2);

        await AssertBackupRefusedAsync(backup, RestoreOutcome.BackupDamaged);
    }

    /// <summary>
    /// A perfectly healthy SQLite file that is not a TermPoint database (no Sections / Semesters / AcademicYears
    /// tables — some other program's database picked by mistake) is refused: NotTermPointDatabase, D
    /// byte-identical, no D.tmp, no pre-restore file, and nothing left in the local staging folder.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_BackupIsValidSqliteButNotTermPoint_IsNotTermPointDatabase()
    {
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup", termPointTables: false);

        var result = await AssertBackupRefusedAsync(backup, RestoreOutcome.NotTermPointDatabase);

        Assert.Null(result.Error);
        Assert.False(File.Exists(TargetPath() + ".tmp"));
        Assert.False(File.Exists(ExpectedPreRestorePath()));
    }

    /// <summary>
    /// All three core tables are required, not just one of them: a database that has Sections and Semesters but
    /// not AcademicYears is still not accepted.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_BackupMissingOneCoreTable_IsNotTermPointDatabase()
    {
        var backup = BackupPath();
        using (var conn = new SqliteConnection($"Data Source={backup};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Sections (key TEXT, data TEXT); CREATE TABLE Semesters (key TEXT, data TEXT);";
            cmd.ExecuteNonQuery();
        }

        await AssertBackupRefusedAsync(backup, RestoreOutcome.NotTermPointDatabase);
    }

    /// <summary>
    /// A backup carrying the three core tables plus any number of others (here, an unknown future table) is
    /// accepted: only the missing core tables matter, never extra ones.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_BackupWithCoreTablesAndExtras_IsRestored()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        using (var conn = new SqliteConnection($"Data Source={backup};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE SomeFutureTable (key TEXT, data TEXT);";
            cmd.ExecuteNonQuery();
        }

        var result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.Equal("from-backup", ReadMarker(target));
    }

    /// <summary>A backup that is not there is unreadable (it says nothing about damage) and D is untouched.</summary>
    [Fact]
    public async Task ReplaceAsync_BackupMissing_IsBackupUnreadable()
    {
        var result = await AssertBackupRefusedAsync(BackupPath("nope.db"), RestoreOutcome.BackupUnreadable);

        Assert.IsAssignableFrom<IOException>(result.Error);
    }

    /// <summary>A backup another program holds open exclusively is unreadable, never damaged. Windows only (enforced FileShare.None).</summary>
    [FactWindowsOnly]
    public async Task ReplaceAsync_BackupHeldExclusively_IsBackupUnreadable()
    {
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        RestoreResult result;
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        using (DbFileTestHelpers.LockExclusively(backup))
            result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(RestoreOutcome.BackupUnreadable, result.Outcome);
        AssertDatabaseUntouched(target, oldBytes);
    }

    // ── D must be unchanged, and readable ─────────────────────────────────────

    /// <summary>D no longer has the hash the caller last knew: TargetChanged, nothing written anywhere.</summary>
    [Fact]
    public async Task ReplaceAsync_WrongExpectedHash_IsTargetChanged_AndWritesNothing()
    {
        var target = TargetPath();
        var (oldBytes, _) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, new string('0', 64));

        Assert.Equal(RestoreOutcome.TargetChanged, result.Outcome);
        Assert.True(result.TargetUnchanged);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>The expected hash matches case-insensitively (callers may hold lower-case hex).</summary>
    [Fact]
    public async Task ReplaceAsync_ExpectedHashCaseIsIgnored()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, oldHash.ToLowerInvariant());

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
    }

    /// <summary>D has vanished: Failed with the FileNotFoundException, never thrown, nothing created.</summary>
    [Fact]
    public async Task ReplaceAsync_TargetMissing_IsFailed_AndCreatesNothing()
    {
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, TargetPath(), "ANYTHING");

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.IsAssignableFrom<IOException>(result.Error);
        Assert.Empty(ShareEntries());
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }

    /// <summary>D held exclusively by another program: FileInUse (not Failed, not damaged), D untouched. Windows only.</summary>
    [FactWindowsOnly]
    public async Task ReplaceAsync_TargetHeldExclusively_IsFileInUse()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        RestoreResult result;
        using (DbFileTestHelpers.LockExclusively(target))
            result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(RestoreOutcome.FileInUse, result.Outcome);
        Assert.NotNull(result.Error);
        AssertDatabaseUntouched(target, oldBytes);
    }

    // ── The lock ──────────────────────────────────────────────────────────────

    /// <summary>The lock is already gone when the restore starts: LockLost, asked exactly once, nothing touched.</summary>
    [Fact]
    public async Task ReplaceAsync_LockNotOursAtStart_IsLockLost()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var script = new LockScript(null, RestoreLockCheck.NotOurs);

        var result = await ReplaceAsync(backup, target, oldHash, script.CheckAsync);

        Assert.Equal(RestoreOutcome.LockLost, result.Outcome);
        Assert.Equal(1, script.Calls);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>The lock cannot be checked because the share does not answer: Unreachable, nothing touched.</summary>
    [Fact]
    public async Task ReplaceAsync_LockUnreachableAtStart_IsUnreachable()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var script = new LockScript(null, RestoreLockCheck.Unreachable);

        var result = await ReplaceAsync(backup, target, oldHash, script.CheckAsync);

        Assert.Equal(RestoreOutcome.Unreachable, result.Outcome);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>
    /// The lock is lost only after the pre-restore copy and D.tmp have been written (the second, final check):
    /// LockLost, D byte-identical, and both the D.tmp and the pre-restore copy are REMOVED — a hook proves they
    /// existed at the moment of the second check, so they were cleaned up rather than never made.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_LockLostAtTheFinalCheck_IsLockLost_AndCleansUpWhatItWrote()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var pre = ExpectedPreRestorePath();
        var debrisPresentAtFinalCheck = false;
        var script = new LockScript(
            call => { if (call == 2) debrisPresentAtFinalCheck = File.Exists(pre) && File.Exists(target + ".tmp"); },
            RestoreLockCheck.Ours, RestoreLockCheck.NotOurs);

        var result = await ReplaceAsync(backup, target, oldHash, script.CheckAsync);

        Assert.True(debrisPresentAtFinalCheck, "the pre-restore copy and D.tmp should exist when the final lock check runs");
        Assert.Equal(RestoreOutcome.LockLost, result.Outcome);
        Assert.Equal(2, script.Calls);
        Assert.Null(result.PreRestoreCopyPath);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>The share stops answering at the final lock check: Unreachable, D untouched, debris removed.</summary>
    [Fact]
    public async Task ReplaceAsync_LockUnreachableAtTheFinalCheck_IsUnreachable_AndCleansUp()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var script = new LockScript(null, RestoreLockCheck.Ours, RestoreLockCheck.Unreachable);

        var result = await ReplaceAsync(backup, target, oldHash, script.CheckAsync);

        Assert.Equal(RestoreOutcome.Unreachable, result.Outcome);
        AssertDatabaseUntouched(target, oldBytes);
    }

    // ── Never throws ──────────────────────────────────────────────────────────

    /// <summary>
    /// A lock check that throws (a bug, in the caller) is reported as Failed with the exception and an error
    /// log — the routine never throws — and D is untouched.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_LockCheckThrows_IsFailed_AndNeverThrows()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, oldHash,
            () => throw new InvalidOperationException("boom"));

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Single(_log.Errors);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>A malformed argument (an empty backup path) is a Failed result, not an exception.</summary>
    [Fact]
    public async Task ReplaceAsync_EmptyBackupPath_IsFailed_AndNeverThrows()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);

        var result = await ReplaceAsync("", target, oldHash);

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>An exception from the logger's development-time re-throw (ThrowOnError) cannot escape the never-throws routine.</summary>
    [Fact]
    public async Task ReplaceAsync_LoggerThatRethrowsErrors_DoesNotMakeItThrow()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var rethrowing = new ThrowingErrorLogger();

        var result = await DatabaseRestore.ReplaceAsync(
            backup, target, oldHash, () => throw new InvalidOperationException("boom"), Now, _localDir, null, rethrowing);

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
    }

    /// <summary>A logger whose LogError throws, like a development logger with ThrowOnError set.</summary>
    private sealed class ThrowingErrorLogger : IAppLogger
    {
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; } = true;
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) => throw ex ?? new InvalidOperationException(context);
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }

    // ── The swap itself ───────────────────────────────────────────────────────

    /// <summary>
    /// The final rename throws (D is held open without delete-sharing, so the rename over it is refused) while D
    /// is provably unchanged: FileInUse, D byte-identical, and the D.tmp and the pre-restore copy are REMOVED.
    /// The hold is taken at the final lock check, after D was last hashed. Windows only.
    /// </summary>
    [FactWindowsOnly]
    public async Task ReplaceAsync_RenameRefusedWhileDUnchanged_IsFileInUse_AndCleansUp()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        FileStream? holder = null;
        var script = new LockScript(
            call => { if (call == 2) holder = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); },
            RestoreLockCheck.Ours);

        RestoreResult result;
        try
        {
            result = await ReplaceAsync(backup, target, oldHash, script.CheckAsync);
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.Equal(RestoreOutcome.FileInUse, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Null(result.PreRestoreCopyPath);
        AssertDatabaseUntouched(target, oldBytes);
    }

    // ── DEBUG fault plans ─────────────────────────────────────────────────────

    /// <summary>
    /// Every stage fault except <c>rename</c> fails its own stage with the documented outcome, leaves D
    /// byte-identical, removes everything the restore wrote (D.tmp, pre-restore copy), empties the local folder,
    /// and logs the injection.
    /// </summary>
    [Theory]
    [InlineData("backup",  RestoreOutcome.Unreachable)]
    [InlineData("damaged", RestoreOutcome.BackupDamaged)]
    [InlineData("precopy", RestoreOutcome.Unreachable)]
    [InlineData("copy",    RestoreOutcome.Unreachable)]
    [InlineData("verify",  RestoreOutcome.Failed)]
    [InlineData("lock",    RestoreOutcome.LockLost)]
    public async Task ReplaceAsync_InjectedFault_FailsItsStage_AndLeavesDUnchangedAndNoDebris(string spec, RestoreOutcome expected)
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, oldHash, plan: RestoreFaultPlan.Parse(spec));

        Assert.Equal(expected, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Null(result.PreRestoreCopyPath);
        AssertDatabaseUntouched(target, oldBytes);
        Assert.Contains(_log.Infos, m => m.Contains("[DEBUG fault injection] TERMPOINT_DEBUG_FAIL_RESTORE"));
    }

    /// <summary>
    /// <c>rename</c>: nothing is moved and the move is treated as timed out; the probe sees the old D, so the
    /// outcome is RenameUnconfirmed — D byte-identical in this simulation, D.tmp left in place (the abandoned
    /// move may still land), and the pre-restore copy KEPT and reported.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_InjectedRenameFault_IsRenameUnconfirmed_KeepsPreRestoreCopyAndTmp()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await ReplaceAsync(backup, target, oldHash, plan: RestoreFaultPlan.Parse("rename"));

        Assert.Equal(RestoreOutcome.RenameUnconfirmed, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.False(result.TargetUnchanged);
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));

        Assert.Equal(oldBytes, File.ReadAllBytes(target));   // the simulated move never happened

        var pre = ExpectedPreRestorePath();
        Assert.Equal(pre, result.PreRestoreCopyPath);
        Assert.Equal(oldBytes, File.ReadAllBytes(pre));      // kept, and byte-identical to the old D

        Assert.True(File.Exists(target + ".tmp"));           // kept: the abandoned move may still land
        Assert.Equal(result.RestoredHash, Hash(target + ".tmp"));
        Assert.Equal("from-backup", ReadMarker(target + ".tmp"));
    }

    /// <summary>
    /// A counted fault ("fail the copy once") lets the retry succeed, and the failed first attempt left no
    /// debris that could disturb it (the second restore still gets the plain, un-numbered pre-restore name).
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_CountedCopyFault_FailsOnce_ThenTheRetrySucceeds()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var plan = RestoreFaultPlan.Parse("copy:1");

        var first = await ReplaceAsync(backup, target, oldHash, plan: plan);
        Assert.Equal(RestoreOutcome.Unreachable, first.Outcome);
        AssertDatabaseUntouched(target, oldBytes);

        var second = await ReplaceAsync(backup, target, oldHash, plan: plan);
        Assert.Equal(RestoreOutcome.Restored, second.Outcome);
        Assert.Equal(ExpectedPreRestorePath(), second.PreRestoreCopyPath);
        Assert.Equal("from-backup", ReadMarker(target));
    }

    /// <summary>
    /// A fault only counts restores that REACH its stage: a restore refused earlier (here: at the first lock
    /// check) does not spend a <c>precopy:1</c> budget, so the next restore still hits it.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_FaultBudget_IsNotSpentByARestoreRefusedBeforeItsStage()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var plan = RestoreFaultPlan.Parse("precopy:1")!;

        var refused = await ReplaceAsync(backup, target, oldHash,
            new LockScript(null, RestoreLockCheck.NotOurs).CheckAsync, plan);
        Assert.Equal(RestoreOutcome.LockLost, refused.Outcome);
        Assert.Equal(1, plan.Remaining);

        var hit = await ReplaceAsync(backup, target, oldHash, plan: plan);
        Assert.Equal(RestoreOutcome.Unreachable, hit.Outcome);
        Assert.Equal(0, plan.Remaining);
        AssertDatabaseUntouched(target, oldBytes);
    }

    // ── Pre-restore file naming ───────────────────────────────────────────────

    /// <summary>
    /// A file already carrying the plain pre-restore name is never overwritten or removed: the new copy takes
    /// " (2)" and the existing file is byte-identical afterwards.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_PreRestoreNameTaken_UsesNumberedName_AndLeavesTheExistingFileAlone()
    {
        var target = TargetPath();
        var (oldBytes, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var taken = ExpectedPreRestorePath();
        var takenBytes = "an earlier restore left this here"u8.ToArray();
        File.WriteAllBytes(taken, takenBytes);

        var result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.Equal(ExpectedPreRestorePath(2), result.PreRestoreCopyPath);
        Assert.EndsWith(" (2).db", result.PreRestoreCopyPath);
        Assert.Equal(oldBytes, File.ReadAllBytes(result.PreRestoreCopyPath!));
        Assert.Equal(takenBytes, File.ReadAllBytes(taken));
    }

    /// <summary>Two names taken: the third is used.</summary>
    [Fact]
    public async Task ReplaceAsync_TwoPreRestoreNamesTaken_UsesTheThird()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        File.WriteAllText(ExpectedPreRestorePath(1), "one");
        File.WriteAllText(ExpectedPreRestorePath(2), "two");

        var result = await ReplaceAsync(backup, target, oldHash);

        Assert.Equal(ExpectedPreRestorePath(3), result.PreRestoreCopyPath);
        Assert.Equal("one", File.ReadAllText(ExpectedPreRestorePath(1)));
        Assert.Equal("two", File.ReadAllText(ExpectedPreRestorePath(2)));
    }

    /// <summary>
    /// A FAILED restore whose name collided with an existing file removes only what it created: the existing
    /// file survives, and the " (2)" copy it made is gone.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_NameCollisionThenFailure_NeverDeletesTheExistingFile()
    {
        var target = TargetPath();
        var (_, oldHash) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var taken = ExpectedPreRestorePath();
        File.WriteAllText(taken, "precious earlier copy");
        var script = new LockScript(null, RestoreLockCheck.Ours, RestoreLockCheck.NotOurs);

        var result = await ReplaceAsync(backup, target, oldHash, script.CheckAsync);

        Assert.Equal(RestoreOutcome.LockLost, result.Outcome);
        Assert.Equal("precious earlier copy", File.ReadAllText(taken));
        Assert.False(File.Exists(ExpectedPreRestorePath(2)));
        Assert.Equal(new[] { target, taken }.OrderBy(e => e, StringComparer.Ordinal), ShareEntries());
    }

    /// <summary>The name is "&lt;name&gt; (before restore yyyy-MM-dd HH-mm)&lt;ext&gt;" in D's folder, with D's own extension.</summary>
    [Fact]
    public void PreRestoreCopyName_UsesTimestampedNameInDsFolder()
    {
        var name = DatabaseRestore.PreRestoreCopyName(Path.Combine("share", "UFV", "BIOL-TT.db"), new DateTime(2026, 1, 2, 3, 4, 59), 1);

        Assert.Equal(Path.Combine("share", "UFV", "BIOL-TT (before restore 2026-01-02 03-04).db"), name);
    }

    /// <summary>Whatever D's extension is, the copy keeps it; a file with no extension gets none.</summary>
    [Theory]
    [InlineData("a.sqlite", "a (before restore 2026-10-13 14-05).sqlite")]
    [InlineData("a.db",     "a (before restore 2026-10-13 14-05).db")]
    [InlineData("a",        "a (before restore 2026-10-13 14-05)")]
    [InlineData("my.dept.db", "my.dept (before restore 2026-10-13 14-05).db")]
    public void PreRestoreCopyName_KeepsTheExtension(string fileName, string expected)
    {
        Assert.Equal(expected, Path.GetFileName(DatabaseRestore.PreRestoreCopyName(Path.Combine("share", fileName), Now, 1)));
    }

    /// <summary>Attempt 1 is the plain name; attempts 2, 3, … insert " (2)", " (3)", … before the extension.</summary>
    [Theory]
    [InlineData(1, "a (before restore 2026-10-13 14-05).db")]
    [InlineData(2, "a (before restore 2026-10-13 14-05) (2).db")]
    [InlineData(3, "a (before restore 2026-10-13 14-05) (3).db")]
    [InlineData(20, "a (before restore 2026-10-13 14-05) (20).db")]
    public void PreRestoreCopyName_AttemptSuffix(int attempt, string expected)
    {
        Assert.Equal(expected, Path.GetFileName(DatabaseRestore.PreRestoreCopyName(Path.Combine("share", "a.db"), Now, attempt)));
    }

    /// <summary>
    /// Backup rotation deletes files matching <c>&lt;name&gt;_*.db</c>; the pre-restore copy must never match, or
    /// the next rotation would delete it.
    /// </summary>
    [Fact]
    public void PreRestoreCopyName_NeverMatchesTheBackupRotationPattern()
    {
        var file = Path.GetFileName(DatabaseRestore.PreRestoreCopyName(Path.Combine("share", "BIOL-TT.db"), Now, 2));

        Assert.False(file.StartsWith("BIOL-TT_", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CreateFromBackupAsync (spec item 5, step 5c) — a restore into a destination that does not exist yet
    //
    // The recurring promise: only Created leaves anything behind; every other outcome creates NOTHING — no target,
    // no target.tmp, nothing in the local staging folder (and, for a bad backup, not even the target's folder) —
    // and a file that is already at the destination is never touched.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Runs the create path against the fixture folders.</summary>
    /// <param name="backup">Backup path.</param>
    /// <param name="target">The database to create.</param>
    /// <param name="plan">Optional fault plan.</param>
    private Task<RestoreResult> CreateAsync(string backup, string target, RestoreFaultPlan? plan = null)
        => DatabaseRestore.CreateFromBackupAsync(backup, target, _localDir, plan, _log);

    /// <summary>
    /// The bytes the create path should produce for <paramref name="backup"/>: the same file after being converted
    /// to rollback-journal mode (what the routine does to its staged copy), worked out on a scratch copy so the
    /// backup itself is left alone.
    /// </summary>
    /// <param name="backup">The backup file.</param>
    private byte[] NormalisedBytesOf(string backup)
    {
        var scratch = Path.Combine(_backupDir, $"normalise_{Guid.NewGuid():N}.db");
        File.Copy(backup, scratch);
        try
        {
            using (var conn = new SqliteConnection($"Data Source={scratch};Mode=ReadWrite;Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=DELETE";
                cmd.ExecuteScalar();
            }
            SqliteConnection.ClearAllPools();
            File.Delete(scratch + "-wal");
            File.Delete(scratch + "-shm");
            return File.ReadAllBytes(scratch);
        }
        finally
        {
            File.Delete(scratch);
        }
    }

    /// <summary>
    /// Asserts the create path left nothing anywhere: nothing in the share folder, nothing in the local staging
    /// folder, and the result says the destination is unchanged on the create path.
    /// </summary>
    /// <param name="result">The result to check.</param>
    private void AssertNothingCreated(RestoreResult result)
    {
        Assert.True(result.TargetUnchanged);
        Assert.True(result.CreatePath);
        Assert.False(result.Succeeded);
        Assert.Null(result.PreRestoreCopyPath);
        Assert.Null(result.RestoredHash);
        Assert.Empty(ShareEntries());
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }

    /// <summary>
    /// The happy path: the new database holds exactly the backup as normalised to rollback-journal mode (a WAL-flagged
    /// backup included), the result carries its hash, nothing else is on the share (no .tmp, no pre-restore file),
    /// the local staging folder is empty, and the backup was only copied.
    /// </summary>
    [Fact]
    public async Task CreateFromBackupAsync_Success_CreatesTheNormalisedBackup_AndLeavesNoDebris()
    {
        var target = TargetPath();
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup", wal: true);
        var backupBytes = File.ReadAllBytes(backup);
        var expected = NormalisedBytesOf(backup);
        Assert.Equal((byte)2, FormatVersions(backup).Write);   // sanity: the test backup really is WAL-flagged

        var result = await CreateAsync(backup, target);

        Assert.Equal(RestoreOutcome.Created, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.False(result.TargetUnchanged);
        Assert.True(result.CreatePath);
        Assert.Null(result.Error);
        Assert.Null(result.PreRestoreCopyPath);
        Assert.Equal(expected, File.ReadAllBytes(target));
        Assert.Equal(result.RestoredHash, Hash(target));
        Assert.Equal("from-backup", ReadMarker(target));

        Assert.Equal(((byte)1, (byte)1), FormatVersions(target));
        Assert.Equal("delete", JournalModeOfCopy(target), ignoreCase: true);

        Assert.False(File.Exists(target + ".tmp"));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
        Assert.Equal(backupBytes, File.ReadAllBytes(backup));
    }

    /// <summary>A destination folder that does not exist yet is created, as it is for a new database.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_MissingFolder_IsCreatedWithTheDatabase()
    {
        var target = Path.Combine(_shareDir, "new", "folder", "dept.db");
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await CreateAsync(backup, target);

        Assert.Equal(RestoreOutcome.Created, result.Outcome);
        Assert.Equal("from-backup", ReadMarker(target));
        Assert.Equal(new[] { target }, Directory.GetFileSystemEntries(Path.GetDirectoryName(target)!));
    }

    /// <summary>
    /// A damaged, foreign, empty or missing backup creates nothing — and not even the destination's folder, which
    /// the routine only makes once the backup has passed every check.
    /// </summary>
    [Theory]
    [InlineData("random", RestoreOutcome.BackupDamaged)]
    [InlineData("empty", RestoreOutcome.BackupDamaged)]
    [InlineData("foreign", RestoreOutcome.NotTermPointDatabase)]
    [InlineData("missing", RestoreOutcome.BackupUnreadable)]
    public async Task CreateFromBackupAsync_BadBackup_CreatesNothing_NotEvenTheFolder(string kind, RestoreOutcome expected)
    {
        var backup = BackupPath();
        switch (kind)
        {
            case "random":  File.WriteAllBytes(backup, Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 + 7)).ToArray()); break;
            case "empty":   File.WriteAllBytes(backup, []); break;
            case "foreign": CreateMarkerDb(backup, "other program", termPointTables: false); break;
            case "missing": break;   // the file is never created
        }
        var folder = Path.Combine(_shareDir, "not", "yet");
        var target = Path.Combine(folder, "dept.db");

        var result = await CreateAsync(backup, target);

        Assert.Equal(expected, result.Outcome);
        AssertNothingCreated(result);
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>A corrupted page in the backup is refused, creating nothing.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_BackupWithACorruptedPage_IsBackupDamaged_AndCreatesNothing()
    {
        var backup = BackupPath();
        DbFileTestHelpers.CreateHealthyDatabase(backup);
        DbFileTestHelpers.TrashPage(backup, pageNumber: 2);

        var result = await CreateAsync(backup, TargetPath());

        Assert.Equal(RestoreOutcome.BackupDamaged, result.Outcome);
        AssertNothingCreated(result);
    }

    /// <summary>A file already at the destination is never touched: AlreadyExists, byte-identical, nothing staged, nothing else created.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_TargetExists_IsAlreadyExists_AndTheFileIsUntouched()
    {
        var target = TargetPath();
        var (oldBytes, _) = CreateTarget(target);
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await CreateAsync(backup, target);

        Assert.Equal(RestoreOutcome.AlreadyExists, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.True(result.CreatePath);
        AssertDatabaseUntouched(target, oldBytes);
    }

    /// <summary>
    /// A file that appears at the destination WHILE the backup is being prepared is not overwritten either. The
    /// logger plays the other user: it creates the file at the moment the routine announces that it is about to
    /// place the new database — after the early probe, before the late one.
    /// </summary>
    [Fact]
    public async Task CreateFromBackupAsync_TargetAppearsBetweenTheProbes_IsAlreadyExists_AndTheFileIsUntouched()
    {
        var target = TargetPath();
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var someoneElsesBytes = new byte[] { 1, 2, 3, 4, 5 };
        var logger = new CallbackLogger(message =>
        {
            if (message.Contains("about to place the new database", StringComparison.Ordinal))
                File.WriteAllBytes(target, someoneElsesBytes);
        });

        var result = await DatabaseRestore.CreateFromBackupAsync(backup, target, _localDir, null, logger);

        Assert.Equal(RestoreOutcome.AlreadyExists, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal(someoneElsesBytes, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }

    /// <summary>
    /// The DEBUG <c>copy</c> fault cuts the transfer to target.tmp: Unreachable, the truncated .tmp it left is
    /// removed (and the removal awaited, so it is gone on return), no target exists, nothing is staged.
    /// </summary>
    [Fact]
    public async Task CreateFromBackupAsync_InjectedCopyFault_IsUnreachable_AndLeavesNothing()
    {
        var target = TargetPath();
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await CreateAsync(backup, target, RestoreFaultPlan.Parse("copy"));

        Assert.Equal(RestoreOutcome.Unreachable, result.Outcome);
        AssertNothingCreated(result);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".tmp"));
        Assert.Contains(_log.Infos, m => m.Contains("fault injection", StringComparison.OrdinalIgnoreCase) && m.Contains("cut part-way", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A counted copy fault cuts the first create; the retry on the same destination then succeeds.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_CountedCopyFault_FailsOnce_ThenTheRetrySucceeds()
    {
        var target = TargetPath();
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var plan = RestoreFaultPlan.Parse("copy:1");

        var first = await CreateAsync(backup, target, plan);
        var retry = await CreateAsync(backup, target, plan);

        Assert.Equal(RestoreOutcome.Unreachable, first.Outcome);
        Assert.Equal(RestoreOutcome.Created, retry.Outcome);
        Assert.Equal("from-backup", ReadMarker(target));
        Assert.Equal(new[] { target }, ShareEntries());
    }

    /// <summary>The <c>backup</c> and <c>damaged</c> faults have a stage on the create path too.</summary>
    [Theory]
    [InlineData("backup", RestoreOutcome.Unreachable)]
    [InlineData("damaged", RestoreOutcome.BackupDamaged)]
    public async Task CreateFromBackupAsync_InjectedEarlyFaults_FailTheirStage_AndLeaveNothing(string spec, RestoreOutcome expected)
    {
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");

        var result = await CreateAsync(backup, TargetPath(), RestoreFaultPlan.Parse(spec));

        Assert.Equal(expected, result.Outcome);
        AssertNothingCreated(result);
    }

    /// <summary>The faults that belong to the replace path (lock, rename, …) do not fire on the create path and do not spend their budget.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_ReplaceOnlyFaults_DoNotFire()
    {
        var target = TargetPath();
        var backup = BackupPath();
        CreateMarkerDb(backup, "from-backup");
        var plan = RestoreFaultPlan.Parse("rename:1")!;

        var result = await CreateAsync(backup, target, plan);

        Assert.Equal(RestoreOutcome.Created, result.Outcome);
        Assert.Equal(1, plan.Remaining);
    }

    /// <summary>The create path never throws, whatever it is given.</summary>
    [Fact]
    public async Task CreateFromBackupAsync_EmptyBackupPath_IsFailed_AndNeverThrows()
    {
        var result = await CreateAsync("", TargetPath());

        Assert.False(result.Succeeded);
        Assert.True(result.CreatePath);
        Assert.True(result.TargetUnchanged);
        Assert.Empty(ShareEntries());
    }

    /// <summary>
    /// A logger that calls back with every info message, so a test can act at an exact moment of the routine (it
    /// plays "another user" in the race tests). Errors, warnings and breadcrumbs are ignored.
    /// </summary>
    private sealed class CallbackLogger : IAppLogger
    {
        private readonly Action<string> _onInfo;

        /// <summary>Creates the logger.</summary>
        /// <param name="onInfo">Called, synchronously on the logging thread, with each <see cref="LogInfo"/> message.</param>
        public CallbackLogger(Action<string> onInfo) => _onInfo = onInfo;

        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) => _onInfo(message);
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }
}

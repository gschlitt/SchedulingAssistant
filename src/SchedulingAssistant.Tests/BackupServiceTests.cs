using Microsoft.Data.Sqlite;
using Moq;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for <see cref="BackupService"/>.
///
/// <para>Tests in this file focus on the F1 fix from the data-integrity audit (2026-05-04):
/// <see cref="BackupService.TakeDbSnapshot"/> must open its own, freshly-constructed
/// <see cref="SqliteConnection"/> rather than reusing <see cref="IDatabaseContext.Connection"/>.
/// Using the shared connection from a background thread (as the backup timer may do) while the
/// UI thread holds an open write transaction is undefined behaviour in Microsoft.Data.Sqlite,
/// which is not thread-safe.</para>
///
/// <para>The canonical proof: the mock <see cref="IDatabaseContext"/> used in every test has
/// its <c>Connection</c> property wired to throw <see cref="InvalidOperationException"/>.
/// If <see cref="BackupService.TakeDbSnapshot"/> ever touches <c>Connection</c>, the test
/// fails. If it only uses <see cref="IDatabaseContext.DatabasePath"/> (the fix), the test
/// passes.</para>
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _backupDir;
    private readonly string? _savedBackupFolderPath;

    /// <summary>
    /// Creates isolated temporary directories and saves/restores
    /// <see cref="AppSettings.BackupFolderPath"/> so tests do not interfere with each other
    /// or with real user settings.
    /// </summary>
    public BackupServiceTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"bst_src_{id}");
        _backupDir  = Path.Combine(Path.GetTempPath(), $"bst_bak_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_backupDir);

        _savedBackupFolderPath = AppSettings.Current.BackupFolderPath;
    }

    /// <summary>Deletes temp directories and restores <see cref="AppSettings.BackupFolderPath"/>.</summary>
    public void Dispose()
    {
        AppSettings.Current.BackupFolderPath = _savedBackupFolderPath;
        try { Directory.Delete(_tempDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_backupDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal valid SQLite database file in <paramref name="dir"/> and returns its path.
    /// <c>Pooling=False</c> ensures the connection is fully released on Dispose so subsequent
    /// VACUUM INTO from a fresh connection is not blocked by a pooled OS handle.
    /// </summary>
    private static string CreateSourceDb(string dir)
    {
        var path = Path.Combine(dir, $"source_{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _seed (id INTEGER PRIMARY KEY)";
        cmd.ExecuteNonQuery();
        return path;
    }

    /// <summary>
    /// Builds a <see cref="BackupService"/> with a real <see cref="IDatabaseContext"/> stub
    /// (returned by <paramref name="dbFactory"/>) and Moq-stub repositories for all
    /// secondary dependencies. The repositories are never called by <see cref="BackupService.TakeDbSnapshot"/>;
    /// they exist only to satisfy the constructor.
    /// </summary>
    private static BackupService MakeBackupService(IDatabaseContext db)
    {
        return new BackupService(
            db,
            new SemesterContext(),
            new Mock<ISectionRepository>().Object,
            new Mock<ICourseRepository>().Object,
            new Mock<IInstructorRepository>().Object,
            new Mock<IRoomRepository>().Object,
            new Mock<ISemesterRepository>().Object,
            new Mock<ISchedulingEnvironmentRepository>().Object,
            new Mock<ICampusRepository>().Object,
            new NullLogger());
    }

    // ── Group 1 — F1: TakeDbSnapshot uses a fresh connection (not the shared one) ──

    /// <summary>
    /// <see cref="BackupService.TakeDbSnapshot"/> must open a fresh <see cref="SqliteConnection"/>
    /// (via <see cref="IDatabaseContext.DatabasePath"/>) rather than using
    /// <see cref="IDatabaseContext.Connection"/>.
    ///
    /// <para>The mock's <c>Connection</c> property throws <see cref="InvalidOperationException"/>
    /// to simulate what happens when the shared connection is called from the wrong thread
    /// (as the backup timer may do). If the F1 fix is absent and TakeDbSnapshot touches
    /// <c>Connection</c>, this test fails immediately. With the fix it passes.</para>
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_NeverTouchesSharedConnection()
    {
        // Arrange
        var sourcePath = CreateSourceDb(_tempDir);
        AppSettings.Current.BackupFolderPath = _backupDir;

        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(sourcePath);

        // Connection must NOT be called — if it is, this throws and the test fails.
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException(
                  "BackupService.TakeDbSnapshot must not access the shared connection. " +
                  "F1 fix: use IDatabaseContext.DatabasePath and open a fresh connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act
        svc.TakeDbSnapshot();

        // Assert — a backup file was produced
        var files = Directory.GetFiles(_backupDir, "*.db");
        Assert.Single(files);
    }

    /// <summary>
    /// The backup file produced by <see cref="BackupService.TakeDbSnapshot"/> must be a
    /// structurally valid SQLite database. This guards against silent copy corruption —
    /// e.g. if the VACUUM INTO path were wrong or the file were truncated.
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_ProducesValidSqliteDatabase()
    {
        // Arrange
        var sourcePath = CreateSourceDb(_tempDir);
        AppSettings.Current.BackupFolderPath = _backupDir;

        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(sourcePath);
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException("Must not touch shared connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act
        svc.TakeDbSnapshot();

        // Assert — integrity_check returns "ok"
        var backupPath = Directory.GetFiles(_backupDir, "*.db").Single();
        using var conn = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        var result = (string)cmd.ExecuteScalar()!;
        Assert.Equal("ok", result);
    }

    /// <summary>
    /// <see cref="BackupService.TakeDbSnapshot"/> must not throw when
    /// <see cref="IDatabaseContext.DatabasePath"/> is empty (as it is in the WASM demo context).
    /// The <see cref="InvalidOperationException"/> thrown by <c>VacuumIntoFreshConnection</c>
    /// when the path is empty must be caught and logged rather than propagated.
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_WhenDatabasePathEmpty_DoesNotThrow()
    {
        // Arrange
        AppSettings.Current.BackupFolderPath = _backupDir;

        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(string.Empty);
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException("Must not touch shared connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act — must not propagate any exception
        var ex = Record.Exception(() => svc.TakeDbSnapshot());

        // Assert
        Assert.Null(ex);
        Assert.Empty(Directory.GetFiles(_backupDir, "*.db")); // nothing written
    }

    /// <summary>
    /// When <see cref="AppSettings.BackupFolderPath"/> is null or empty,
    /// <see cref="BackupService.TakeDbSnapshot"/> is a no-op: it returns immediately
    /// without touching the shared connection or writing any file.
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_WhenNoBackupFolder_DoesNotThrow()
    {
        // Arrange — no backup folder configured
        AppSettings.Current.BackupFolderPath = null;

        var sourcePath = CreateSourceDb(_tempDir);
        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(sourcePath);
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException("Must not touch shared connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act
        var ex = Record.Exception(() => svc.TakeDbSnapshot());

        // Assert
        Assert.Null(ex);
    }

    // ── Group 2 — F10: fresh Pooling=False connections release file handles immediately ──

    /// <summary>
    /// F10's root cause: if VACUUM INTO used the shared pooled connection, the connection
    /// pool would hold D' open after <c>SqliteConnection.ClearAllPools()</c>, blocking
    /// <c>CleanupWorkingCopy()</c> on Windows.  The F1 fix uses <c>Pooling=False</c>, so
    /// each backup connection is fully closed on <c>Dispose</c>.
    ///
    /// <para>This test verifies the F1/F10 property: after <see cref="BackupService.TakeDbSnapshot"/>
    /// returns, the source database file can be deleted — no OS file handle remains open.
    /// On Windows, an open SQLite pooled handle would cause <see cref="IOException"/> here.</para>
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_ReleasesSourceFileHandleImmediately()
    {
        // Arrange
        var sourcePath = CreateSourceDb(_tempDir);
        AppSettings.Current.BackupFolderPath = _backupDir;

        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(sourcePath);
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException("Must not touch shared connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act
        svc.TakeDbSnapshot();

        // Assert — source file can be deleted immediately (no pooled handle holds it open)
        // This is the exact operation that CleanupWorkingCopy() performs on D' after DI teardown.
        var ex = Record.Exception(() => File.Delete(sourcePath));
        Assert.Null(ex);
    }

    /// <summary>
    /// Multiple successive <see cref="BackupService.TakeDbSnapshot"/> calls must complete
    /// without throwing and must leave the source file available for subsequent operations.
    /// This simulates rapid timer ticks or a backup-then-StopSession cycle. (F10.)
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_CalledRepeatedly_NoHandlesRemain()
    {
        // Arrange
        var sourcePath = CreateSourceDb(_tempDir);
        AppSettings.Current.BackupFolderPath = _backupDir;

        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(sourcePath);
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException("Must not touch shared connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act — call TakeDbSnapshot three times (timestamp may repeat if within same second;
        // VACUUM INTO silently overwrites the destination if it already exists, which is fine).
        var callEx1 = Record.Exception(() => svc.TakeDbSnapshot());
        var callEx2 = Record.Exception(() => svc.TakeDbSnapshot());
        var callEx3 = Record.Exception(() => svc.TakeDbSnapshot());

        // Assert — no call threw
        Assert.Null(callEx1);
        Assert.Null(callEx2);
        Assert.Null(callEx3);

        // Source file is deletable — no pooled handle holds it open (F10/F1 guarantee)
        var deleteEx = Record.Exception(() => File.Delete(sourcePath));
        Assert.Null(deleteEx);
    }

    // ── Group 3 — F4: Pre-save snapshots use a distinct filename suffix ───────

    /// <summary>
    /// <see cref="BackupService.TakeDbSnapshot"/> must write a file whose name ends with
    /// <c>_presave.db</c> rather than the plain <c>.db</c> used by periodic backups.
    /// The suffix is defined by <see cref="BackupService.PresaveSuffix"/> and is checked
    /// against the actual file created in the backup folder. (F4, 2026-05-04.)
    /// </summary>
    [Fact]
    public void TakeDbSnapshot_WritesPresaveSuffix()
    {
        // Arrange
        var sourcePath = CreateSourceDb(_tempDir);
        AppSettings.Current.BackupFolderPath = _backupDir;

        var mockDb = new Mock<IDatabaseContext>();
        mockDb.Setup(d => d.DatabasePath).Returns(sourcePath);
        mockDb.Setup(d => d.Connection)
              .Throws(new InvalidOperationException("Must not touch shared connection."));

        using var svc = MakeBackupService(mockDb.Object);

        // Act
        svc.TakeDbSnapshot();

        // Assert — exactly one file created; its stem ends with the presave suffix
        var files = Directory.GetFiles(_backupDir, "*.db");
        Assert.Single(files);
        var stem = Path.GetFileNameWithoutExtension(files[0]);
        Assert.EndsWith(BackupService.PresaveSuffix,
                        stem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <see cref="BackupService.RotateBackups"/> must not include <c>*_presave.db</c> files
    /// in the rotation count. When the backup folder contains 9 periodic backups and
    /// 3 pre-save snapshots, rotating with <c>maxCount = 9</c> must leave all 12 files
    /// intact — no pre-save file should be deleted, and no periodic backup should be
    /// deleted either (because the 9 periodic files are exactly at the limit).
    ///
    /// <para>This is the primary F4 correctness test: pre-save snapshots must not consume
    /// rotation slots reserved for periodic backups.</para>
    /// </summary>
    [Fact]
    public void RotateBackups_ExcludesPresaveFiles()
    {
        // Arrange — 9 periodic + 3 presave backup files
        const string prefix = "testdb";

        for (int i = 1; i <= 9; i++)
        {
            var ts = $"2026-05-{i:D2}_10-00-00";
            File.WriteAllText(Path.Combine(_backupDir, $"{prefix}_{ts}.db"), "x");
        }
        for (int i = 1; i <= 3; i++)
        {
            var ts = $"2026-05-{i:D2}_09-00-00";
            File.WriteAllText(Path.Combine(_backupDir, $"{prefix}_{ts}_presave.db"), "x");
        }

        // Act — rotate at exactly the periodic-file count (should be a no-op for periodics)
        BackupService.RotateBackups(_backupDir, prefix, maxCount: 9);

        // Assert — all 12 files survive
        var all      = Directory.GetFiles(_backupDir, "*.db");
        var presave  = all.Where(f => f.EndsWith("_presave.db", StringComparison.OrdinalIgnoreCase)).ToList();
        var periodic = all.Where(f => !f.EndsWith("_presave.db", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal(3, presave.Count);   // all presave files untouched
        Assert.Equal(9, periodic.Count);  // periodic files at exactly the limit — none deleted
    }

    /// <summary>
    /// When the backup folder contains only pre-save snapshots and then a periodic backup is
    /// added via <see cref="BackupService.RotateBackups"/>, the periodic backups must not be
    /// rotated away because pre-save files pushed the count over the limit.
    ///
    /// <para>This covers the motivating F4 scenario: after 10 save operations (each writing a
    /// presave file) plus 1 periodic backup, the periodic backup must survive even though
    /// the raw file count is 11.</para>
    /// </summary>
    [Fact]
    public void RotateBackups_PresaveFilesDoNotConsumeSlots()
    {
        // Arrange — 10 presave files + 1 periodic file
        const string prefix = "mydb";

        for (int i = 1; i <= 10; i++)
        {
            var ts = $"2026-04-{i:D2}_08-00-00";
            File.WriteAllText(Path.Combine(_backupDir, $"{prefix}_{ts}_presave.db"), "x");
        }
        // One periodic backup — this must survive even though 10 presave files also exist
        File.WriteAllText(Path.Combine(_backupDir, $"{prefix}_2026-05-01_12-00-00.db"), "x");

        // Act — rotate with maxCount = 5 (well below the total file count of 11)
        BackupService.RotateBackups(_backupDir, prefix, maxCount: 5);

        // Assert — the 1 periodic backup survived; presave files untouched
        var all      = Directory.GetFiles(_backupDir, "*.db");
        var presave  = all.Where(f => f.EndsWith("_presave.db", StringComparison.OrdinalIgnoreCase)).ToList();
        var periodic = all.Where(f => !f.EndsWith("_presave.db", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal(10, presave.Count);  // presave files never rotated
        Assert.Single(periodic);          // the 1 periodic backup was within limit — not deleted
    }

    // ── Support types ─────────────────────────────────────────────────────────

    /// <summary>Discards all log output. Equivalent to the NullLogger in CheckoutServiceTests.</summary>
    private sealed class NullLogger : IAppLogger
    {
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }

        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;

        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null) { }

        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }

        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
    }
}

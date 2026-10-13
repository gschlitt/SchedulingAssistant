using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the tri-state integrity check (spec item 4e):
/// <see cref="BackupService.CheckIntegrityDetailed"/> must call a file <b>Damaged</b> only on
/// positive evidence, and report a healthy file that merely can't be opened right now (another
/// program holds it exclusively, access denied) as <b>Unreadable</b>. The boolean
/// <see cref="BackupService.CheckIntegrity"/> must keep behaving exactly as before for every case.
///
/// <para>The reported bug: a locked-but-healthy database was shown as "failed its integrity check
/// and may be damaged", and a user told that may restore a backup over a good, current file.</para>
///
/// <para>The "held open" cases use <c>FileShare.None</c>, which is only enforced by the OS on
/// Windows, so they are marked <see cref="FactWindowsOnlyAttribute"/> (CI runs
/// <c>windows-latest</c>). Everything else runs everywhere. Each test works in its own temp
/// directory and never touches a real database.</para>
/// </summary>
public sealed class BackupServiceIntegrityTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public BackupServiceIntegrityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"integrity_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>Full path of <paramref name="name"/> inside the temp directory.</summary>
    private string PathOf(string name = "test.db") => Path.Combine(_tempDir, name);

    // ── Healthy / damaged files ───────────────────────────────────────────────

    /// <summary>A healthy database is Ok, and the bool wrapper agrees.</summary>
    [Fact]
    public void HealthyDatabase_IsOk()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);

        Assert.Equal(IntegrityCheckResult.Ok, BackupService.CheckIntegrityDetailed(path));
        Assert.True(BackupService.CheckIntegrity(path));
    }

    /// <summary>
    /// A genuine SQLite file with a trashed page (past the header) is positive evidence of damage:
    /// Damaged, and the bool wrapper still says false.
    /// </summary>
    [Fact]
    public void CorruptedPage_IsDamaged()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        DbFileTestHelpers.TrashPage(path, pageNumber: 2);

        Assert.Equal(IntegrityCheckResult.Damaged, BackupService.CheckIntegrityDetailed(path));
        Assert.False(BackupService.CheckIntegrity(path));
    }

    /// <summary>A file that is not SQLite at all (SQLITE_NOTADB) is Damaged, not Unreadable.</summary>
    [Fact]
    public void NonSqliteBytes_AreDamaged()
    {
        var path = PathOf("notadb.db");
        File.WriteAllText(path, "this is not a sqlite database, just some text that is long enough");

        Assert.Equal(IntegrityCheckResult.Damaged, BackupService.CheckIntegrityDetailed(path));
        Assert.False(BackupService.CheckIntegrity(path));
    }

    /// <summary>A file cut off after 512 bytes still carries the header but is structurally broken: Damaged.</summary>
    [Fact]
    public void TruncatedFile_IsDamaged()
    {
        var source = PathOf("source.db");
        var truncated = PathOf("truncated.db");
        DbFileTestHelpers.CreateHealthyDatabase(source);
        File.WriteAllBytes(truncated, File.ReadAllBytes(source)[..512]);

        Assert.Equal(IntegrityCheckResult.Damaged, BackupService.CheckIntegrityDetailed(truncated));
        Assert.False(BackupService.CheckIntegrity(truncated));
    }

    /// <summary>
    /// A path with no file cannot be opened read-only (SQLITE_CANTOPEN): that is "couldn't read
    /// it", never "damaged". (Callers probe for existence first, so Missing is reported before
    /// this is reached; this pins down that the race can't produce a damage verdict either.)
    /// </summary>
    [Fact]
    public void MissingFile_IsUnreadable_NotDamaged()
    {
        var path = PathOf("nothing-here.db");

        Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.CheckIntegrityDetailed(path));
        Assert.False(BackupService.CheckIntegrity(path));
        Assert.False(File.Exists(path));   // a read-only check must not create it
    }

    // ── A healthy file held open by another program ───────────────────────────

    /// <summary>
    /// THE BUG. A healthy database that another program holds with <c>FileShare.None</c> (a sharing
    /// violation — reproduced by the user with PowerShell) used to be reported as damaged. It must
    /// now be Unreadable, the bool wrapper must still be false (unchanged for its other callers),
    /// the file's bytes must be untouched, and once the other program lets go the very same file is
    /// Ok again.
    /// </summary>
    [FactWindowsOnly]
    public void HealthyFileHeldExclusively_IsUnreadable_ThenOkAfterRelease()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var hashBefore = DbFileTestHelpers.HashOf(path);

        using (DbFileTestHelpers.LockExclusively(path))
        {
            Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.CheckIntegrityDetailed(path));
            Assert.False(BackupService.CheckIntegrity(path));
        }

        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(path));   // nothing was written
        Assert.Equal(IntegrityCheckResult.Ok, BackupService.CheckIntegrityDetailed(path));
        Assert.True(BackupService.CheckIntegrity(path));
    }

    /// <summary>
    /// Pins down empirically which SQLite code a sharing violation produces: a read-only open of
    /// an exclusively-held file fails with the primary code SQLITE_CANTOPEN (14). (Classification
    /// does not depend on this — every non-corruption code is Unreadable — but the doc comments
    /// name it, so this keeps them honest if SQLite ever changes.)
    /// </summary>
    [FactWindowsOnly]
    public void SharingViolation_ProducesSqliteCantOpen()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);

        SqliteException? caught = null;
        using (DbFileTestHelpers.LockExclusively(path))
        {
            try
            {
                var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
                using var conn = new SqliteConnection(cs);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                cmd.ExecuteScalar();
            }
            catch (SqliteException ex)
            {
                caught = ex;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        Assert.NotNull(caught);
        Assert.True((caught!.SqliteErrorCode & 0xFF) == 14,
            $"Expected primary SQLITE_CANTOPEN (14); got SqliteErrorCode={caught.SqliteErrorCode}, " +
            $"extended={caught.SqliteExtendedErrorCode}: {caught.Message}");
    }

    /// <summary>
    /// Permission denied behaves like a lock: a healthy file the current user is explicitly denied
    /// read access to is Unreadable, not damaged. (The deny rule is removed in a <c>finally</c> so
    /// the temp directory can be cleaned up.)
    /// </summary>
    [FactWindowsOnly]
    public void HealthyFileWithReadDenied_IsUnreadable()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var info = new FileInfo(path);
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.ReadData, AccessControlType.Deny);

        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.CheckIntegrityDetailed(path));
            Assert.False(BackupService.CheckIntegrity(path));
        }
        finally
        {
            var restore = info.GetAccessControl();
            restore.RemoveAccessRule(deny);
            info.SetAccessControl(restore);
        }

        Assert.Equal(IntegrityCheckResult.Ok, BackupService.CheckIntegrityDetailed(path));
    }

    // ── Exception classification (no file system needed) ──────────────────────

    /// <summary>
    /// SQLite codes that positively say "the contents are bad" — CORRUPT (11), NOTADB (26),
    /// FORMAT (24) — are Damaged, including extended codes that fold into them (267 =
    /// SQLITE_CORRUPT_VTAB: low byte 11).
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(26)]
    [InlineData(24)]
    [InlineData(267)]   // SQLITE_CORRUPT_VTAB (11 | 1<<8)
    [InlineData(523)]   // SQLITE_CORRUPT_SEQUENCE (11 | 2<<8)
    public void Classify_CorruptionCodes_AreDamaged(int code)
    {
        var ex = new SqliteException("corrupt", code);

        Assert.Equal(IntegrityCheckResult.Damaged, BackupService.ClassifyIntegrityException(ex));
    }

    /// <summary>
    /// SQLite codes about being unable to get at the file — PERM (3), BUSY (5), LOCKED (6),
    /// IOERR (10), CANTOPEN (14), AUTH (23) — and unrelated ones (ERROR 1, READONLY 8, NOMEM 7,
    /// FULL 13) are Unreadable, including extended codes (261 = BUSY_RECOVERY, 522 = IOERR_SHORT_READ,
    /// 782 = CANTOPEN_FULLPATH — any code whose low byte is not a corruption code).
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(23)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(261)]   // SQLITE_BUSY_RECOVERY (5 | 1<<8)
    [InlineData(522)]   // SQLITE_IOERR_SHORT_READ (10 | 2<<8)
    [InlineData(782)]   // SQLITE_CANTOPEN_FULLPATH (14 | 3<<8)
    public void Classify_UnableToAccessCodes_AreUnreadable(int code)
    {
        var ex = new SqliteException("cannot access", code);

        Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.ClassifyIntegrityException(ex));
    }

    /// <summary>
    /// Non-SQLite exceptions are Unreadable: I/O and permission errors by design, and anything
    /// unanticipated because a file is never called damaged without positive evidence.
    /// </summary>
    [Fact]
    public void Classify_NonSqliteExceptions_AreUnreadable()
    {
        Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.ClassifyIntegrityException(new IOException("sharing violation")));
        Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.ClassifyIntegrityException(new UnauthorizedAccessException()));
        Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.ClassifyIntegrityException(new InvalidOperationException("odd")));
        Assert.Equal(IntegrityCheckResult.Unreadable, BackupService.ClassifyIntegrityException(new ArgumentException("bad connection string")));
    }
}

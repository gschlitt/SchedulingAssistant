using Microsoft.Data.Sqlite;
using TermPoint.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="DatabaseValidator"/>.
///
/// <para>Each test operates on a fresh temporary directory. All five corruption
/// techniques discussed in the design docs are exercised, plus the normal missing-file
/// and valid-database paths:</para>
///
/// <list type="bullet">
///   <item><description>Null / empty path → Missing</description></item>
///   <item><description>Path does not exist on disk → Missing</description></item>
///   <item><description>Valid SQLite database → Ok</description></item>
///   <item><description>Plain text file (garbage content) → Corrupt</description></item>
///   <item><description>Zero-byte file → Corrupt</description></item>
///   <item><description>Truncated file (first 512 bytes of a real DB) → Corrupt</description></item>
///   <item><description>Schema poisoned via <c>PRAGMA writable_schema</c> → Corrupt</description></item>
///   <item><description>Page trashed past the header → Corrupt</description></item>
///   <item><description>Healthy file held exclusively by another program → Unreadable, never Corrupt (spec 4e)</description></item>
/// </list>
///
/// <para>No UI thread or DI container is involved. Tests are entirely self-contained
/// and clean up their temporary files on disposal.</para>
/// </summary>
public sealed class DatabaseValidatorTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public DatabaseValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dbval_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory and all files created during the test.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a full path inside the temp directory.</summary>
    private string DbPath(string name = "test.db") => Path.Combine(_tempDir, name);

    /// <summary>
    /// Creates a minimal valid SQLite database at <paramref name="path"/> with a
    /// <c>Sections</c> table, matching the real schema pattern used by the application.
    /// </summary>
    private static void CreateValidDatabase(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE Sections (id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}');";
        cmd.ExecuteNonQuery();
        conn.Close();
        // ClearAllPools releases the pooled connection so the file handle is
        // freed immediately on Windows, allowing File.ReadAllBytes to succeed.
        SqliteConnection.ClearAllPools();
    }

    // ── Missing path tests ────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_NullPath_ReturnsMissing()
    {
        var result = await DatabaseValidator.ValidateAsync(null);
        Assert.Equal(DatabaseValidationResult.Missing, result);
    }

    [Fact]
    public async Task Validate_EmptyPath_ReturnsMissing()
    {
        var result = await DatabaseValidator.ValidateAsync(string.Empty);
        Assert.Equal(DatabaseValidationResult.Missing, result);
    }

    [Fact]
    public async Task Validate_WhitespacePath_ReturnsMissing()
    {
        var result = await DatabaseValidator.ValidateAsync("   ");
        Assert.Equal(DatabaseValidationResult.Missing, result);
    }

    [Fact]
    public async Task Validate_FileDoesNotExist_ReturnsMissing()
    {
        var result = await DatabaseValidator.ValidateAsync(DbPath("nonexistent.db"));
        Assert.Equal(DatabaseValidationResult.Missing, result);
    }

    // ── Valid database ────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ValidDatabase_ReturnsOk()
    {
        var path = DbPath();
        CreateValidDatabase(path);

        var result = await DatabaseValidator.ValidateAsync(path);
        Assert.Equal(DatabaseValidationResult.Ok, result);
    }

    // ── Corruption scenarios ──────────────────────────────────────────────────

    /// <summary>
    /// Method 1: replace the file with plain text.
    /// SQLite rejects the file immediately — not a valid database header.
    /// </summary>
    [Fact]
    public async Task Validate_GarbageTextFile_ReturnsCorrupt()
    {
        var path = DbPath("garbage.db");
        File.WriteAllText(path, "this is not a sqlite database");

        var result = await DatabaseValidator.ValidateAsync(path);
        Assert.Equal(DatabaseValidationResult.Corrupt, result);
    }

    /// <summary>
    /// Method 2: zero-byte file.
    /// SQLite treats an empty file as a valid (uninitialised) database — integrity_check
    /// returns "ok". <see cref="DatabaseValidator"/> therefore returns Ok, and
    /// <see cref="DatabaseContext"/> would initialise the schema on first open.
    /// This test documents that behaviour rather than asserting Corrupt.
    /// </summary>
    [Fact]
    public async Task Validate_ZeroByteFile_ReturnsOk()
    {
        var path = DbPath("empty.db");
        File.WriteAllBytes(path, []);

        var result = await DatabaseValidator.ValidateAsync(path);
        Assert.Equal(DatabaseValidationResult.Ok, result);
    }

    /// <summary>
    /// Method 3: keep only the first 512 bytes of a real database file.
    /// The SQLite header survives so the file looks plausible, but the
    /// page data is missing — integrity_check fails.
    /// </summary>
    [Fact]
    public async Task Validate_TruncatedFile_ReturnsCorrupt()
    {
        var sourcePath = DbPath("source.db");
        var truncPath  = DbPath("truncated.db");
        CreateValidDatabase(sourcePath);

        var bytes = File.ReadAllBytes(sourcePath);
        File.WriteAllBytes(truncPath, bytes[..Math.Min(512, bytes.Length)]);

        var result = await DatabaseValidator.ValidateAsync(truncPath);
        Assert.Equal(DatabaseValidationResult.Corrupt, result);
    }

    /// <summary>
    /// Method 4: poison the schema table via PRAGMA writable_schema.
    /// The file opens as SQLite, but integrity_check reports structural errors.
    /// </summary>
    [Fact]
    public async Task Validate_SchemaPoisoned_ReturnsCorrupt()
    {
        var path = DbPath("poisoned.db");
        CreateValidDatabase(path);

        // Open the database and overwrite the schema entry with invalid SQL.
        // SQLite allows this when writable_schema is ON, producing a file that
        // opens but fails integrity_check.
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        // Two separate commands: PRAGMA must be set before the UPDATE.
        // LIMIT in UPDATE is not compiled in by default in this SQLite build,
        // so we omit it — there is only one table in the test schema anyway.
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA writable_schema = ON";
            pragma.ExecuteNonQuery();
        }
        using (var update = conn.CreateCommand())
        {
            update.CommandText =
                "UPDATE sqlite_master SET sql = 'GARBAGE SQL HERE' WHERE type = 'table'";
            update.ExecuteNonQuery();
        }

        conn.Close();
        SqliteConnection.ClearAllPools();

        var result = await DatabaseValidator.ValidateAsync(path);
        Assert.Equal(DatabaseValidationResult.Corrupt, result);
    }

    // ── ValidateWithoutSqliteAsync (never opens the file with SQLite) ─────────

    /// <summary>
    /// Returns the SHA-256 of a file, opened with sharing so it works while another handle is open.
    /// </summary>
    private static string HashOf(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
    }

    /// <summary>Null, empty and whitespace paths are reported Missing, like <see cref="DatabaseValidator.ValidateAsync"/>.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateWithoutSqlite_BlankPath_ReturnsMissing(string? path)
    {
        Assert.Equal(DatabaseValidationResult.Missing, await DatabaseValidator.ValidateWithoutSqliteAsync(path));
    }

    /// <summary>A path where nothing exists (answered, not unreachable) is Missing.</summary>
    [Fact]
    public async Task ValidateWithoutSqlite_FileDoesNotExist_ReturnsMissing()
    {
        Assert.Equal(DatabaseValidationResult.Missing,
            await DatabaseValidator.ValidateWithoutSqliteAsync(DbPath("nonexistent.db")));
    }

    /// <summary>A real SQLite database carries the signature and is Ok.</summary>
    [Fact]
    public async Task ValidateWithoutSqlite_ValidDatabase_ReturnsOk()
    {
        var path = DbPath();
        CreateValidDatabase(path);

        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateWithoutSqliteAsync(path));
    }

    /// <summary>A non-SQLite file (plain text, even longer than 16 bytes) is Corrupt.</summary>
    [Fact]
    public async Task ValidateWithoutSqlite_GarbageTextFile_ReturnsCorrupt()
    {
        var path = DbPath("garbage.db");
        File.WriteAllText(path, "this is not a sqlite database");

        Assert.Equal(DatabaseValidationResult.Corrupt, await DatabaseValidator.ValidateWithoutSqliteAsync(path));
    }

    /// <summary>
    /// A zero-byte file and a file shorter than the 16-byte signature are Corrupt. (Unlike
    /// <see cref="DatabaseValidator.ValidateAsync"/>, where SQLite treats an empty file as a valid
    /// empty database: an empty file is not a TermPoint database someone can join.)
    /// </summary>
    [Fact]
    public async Task ValidateWithoutSqlite_EmptyOrShortFile_ReturnsCorrupt()
    {
        var empty = DbPath("empty.db");
        File.WriteAllBytes(empty, []);
        var shortFile = DbPath("short.db");
        File.WriteAllBytes(shortFile, "SQLite form"u8.ToArray());

        Assert.Equal(DatabaseValidationResult.Corrupt, await DatabaseValidator.ValidateWithoutSqliteAsync(empty));
        Assert.Equal(DatabaseValidationResult.Corrupt, await DatabaseValidator.ValidateWithoutSqliteAsync(shortFile));
    }

    /// <summary>
    /// The point of the method: validating a database changes nothing about it and leaves no
    /// handle or sidecar behind — the bytes (hence the hash the active editor compares against)
    /// are identical, no -wal/-shm appears, and the file can be deleted/renamed immediately.
    /// </summary>
    [Fact]
    public async Task ValidateWithoutSqlite_DoesNotTouchOrHoldTheFile()
    {
        var path = DbPath("shared.db");
        CreateValidDatabase(path);
        var before = HashOf(path);

        var result = await DatabaseValidator.ValidateWithoutSqliteAsync(path);

        Assert.Equal(DatabaseValidationResult.Ok, result);
        Assert.Equal(before, HashOf(path));
        Assert.False(File.Exists(path + "-wal"));
        Assert.False(File.Exists(path + "-shm"));
        Assert.False(File.Exists(path + "-journal"));

        // No lingering handle: a rename (what the editor's save does to D) must succeed at once.
        var renamed = DbPath("renamed.db");
        File.Move(path, renamed);
        Assert.True(File.Exists(renamed));
    }

    /// <summary>
    /// Validation must work while another process (the active editor) holds D open for
    /// reading and writing.
    /// </summary>
    [Fact]
    public async Task ValidateWithoutSqlite_FileOpenElsewhereWithSharing_StillOk()
    {
        var path = DbPath("busy.db");
        CreateValidDatabase(path);
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateWithoutSqliteAsync(path));
    }

    // ── Spec item 4e: a locked file is Unreadable, never "damaged" ────────────

    /// <summary>
    /// A SQLite file with a trashed page is still reported Corrupt by <see cref="DatabaseValidator.ValidateAsync"/>
    /// — the tri-state check must not turn real damage into "unreadable".
    /// </summary>
    [Fact]
    public async Task Validate_TrashedPage_ReturnsCorrupt()
    {
        var path = DbPath("trashed.db");
        DbFileTestHelpers.CreateHealthyDatabase(path);
        DbFileTestHelpers.TrashPage(path, pageNumber: 2);

        Assert.Equal(DatabaseValidationResult.Corrupt, await DatabaseValidator.ValidateAsync(path));
    }

    /// <summary>
    /// THE BUG, end to end through <see cref="DatabaseValidator.ValidateAsync"/>: a healthy database
    /// that another program holds exclusively (sharing violation) is Unreadable — NOT Corrupt, which
    /// would tell the user their good database is damaged and invite a restore over it. The bytes
    /// are untouched, and once the other program lets go the same file validates Ok.
    /// </summary>
    [FactWindowsOnly]
    public async Task Validate_HealthyFileHeldExclusively_ReturnsUnreadable()
    {
        var path = DbPath("locked.db");
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var hashBefore = DbFileTestHelpers.HashOf(path);

        using (DbFileTestHelpers.LockExclusively(path))
        {
            var result = await DatabaseValidator.ValidateAsync(path);

            Assert.Equal(DatabaseValidationResult.Unreadable, result);
            Assert.NotEqual(DatabaseValidationResult.Corrupt, result);
        }

        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(path));
        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateAsync(path));
    }

    /// <summary>
    /// The wizard-join validator (<see cref="DatabaseValidator.ValidateWithoutSqliteAsync"/>) has the
    /// same rule: a sharing violation while reading the 16-byte signature is Unreadable, never
    /// Corrupt, the bytes are untouched, and it is Ok again after the lock is released.
    /// </summary>
    [FactWindowsOnly]
    public async Task ValidateWithoutSqlite_HealthyFileHeldExclusively_ReturnsUnreadable()
    {
        var path = DbPath("locked.db");
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var hashBefore = DbFileTestHelpers.HashOf(path);

        using (DbFileTestHelpers.LockExclusively(path))
        {
            var result = await DatabaseValidator.ValidateWithoutSqliteAsync(path);

            Assert.Equal(DatabaseValidationResult.Unreadable, result);
            Assert.NotEqual(DatabaseValidationResult.Corrupt, result);
        }

        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(path));
        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateWithoutSqliteAsync(path));
    }

    /// <summary>
    /// <see cref="DatabaseValidator.IsLockedOrAccessDenied"/> recognises access-denied and the two
    /// Win32 "someone else has it" HRESULTs (sharing violation 0x80070020, lock violation
    /// 0x80070021), and nothing else — a dropped share (0x80070040, network name deleted), a plain
    /// IOException and unrelated exceptions are not "locked", so they keep their previous result.
    /// </summary>
    [Fact]
    public void IsLockedOrAccessDenied_RecognisesOnlyLockAndAccessErrors()
    {
        Assert.True(DatabaseValidator.IsLockedOrAccessDenied(new UnauthorizedAccessException()));
        Assert.True(DatabaseValidator.IsLockedOrAccessDenied(new IOException("sharing", unchecked((int)0x80070020))));
        Assert.True(DatabaseValidator.IsLockedOrAccessDenied(new IOException("lock", unchecked((int)0x80070021))));

        Assert.False(DatabaseValidator.IsLockedOrAccessDenied(new IOException("net name deleted", unchecked((int)0x80070040))));
        Assert.False(DatabaseValidator.IsLockedOrAccessDenied(new IOException("plain")));
        Assert.False(DatabaseValidator.IsLockedOrAccessDenied(new InvalidOperationException()));
    }
}

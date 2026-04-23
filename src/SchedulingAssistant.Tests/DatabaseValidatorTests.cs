using Microsoft.Data.Sqlite;
using SchedulingAssistant.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SchedulingAssistant.Tests;

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
}

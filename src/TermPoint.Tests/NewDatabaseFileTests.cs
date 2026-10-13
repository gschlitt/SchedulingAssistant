using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="NewDatabaseFile"/> — creation of a brand-new database on the shared path
/// without ever opening that path with SQLite (spec item 4a, path 3).
///
/// <para>"Shared" here is a temp folder, which exercises the same code path as a network share:
/// the file is built in a separate LOCAL folder (<c>localDir</c>, so the tests can prove nothing
/// is left behind there), copied to <c>D.tmp</c>, verified, then moved into place without overwrite.</para>
/// </summary>
public sealed class NewDatabaseFileTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Stands in for the shared folder holding D.</summary>
    private readonly string _shareDir;

    /// <summary>The local scratch folder the helper builds into; must be empty afterwards.</summary>
    private readonly string _localDir;

    /// <summary>Creates isolated "share" and "local" folders for this test instance.</summary>
    public NewDatabaseFileTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir = Path.Combine(Path.GetTempPath(), $"ndf_share_{id}");
        _localDir = Path.Combine(Path.GetTempPath(), $"ndf_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_localDir);
    }

    /// <summary>Removes both folders (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_shareDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_localDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Path of the database to create, inside the "share" folder.</summary>
    private string TargetPath(string name = "new.db") => Path.Combine(_shareDir, name);

    /// <summary>Creates a database at <paramref name="target"/> through the helper under test.</summary>
    private Task<NewDatabaseResult> CreateAsync(string target) => NewDatabaseFile.CreateAsync(target, _localDir);

    /// <summary>
    /// Returns the value of <c>PRAGMA journal_mode</c> for the database at <paramref name="path"/>,
    /// read on a scratch COPY (so the file under test is never opened by SQLite) with pooling off.
    /// </summary>
    private string JournalModeOfCopy(string path)
    {
        var copy = Path.Combine(_localDir, $"copy_{Guid.NewGuid():N}.db");
        File.Copy(path, copy);
        using var conn = new SqliteConnection($"Data Source={copy};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        var mode = (string)cmd.ExecuteScalar()!;
        conn.Close();
        File.Delete(copy);
        return mode;
    }

    // ── Creation ──────────────────────────────────────────────────────────────

    /// <summary>The happy path reports Created and the file exists with content.</summary>
    [Fact]
    public async Task CreateAsync_NewTarget_ReturnsCreatedAndFileExists()
    {
        var target = TargetPath();

        var result = await CreateAsync(target);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.True(File.Exists(target));
        Assert.True(new FileInfo(target).Length > 0);
    }

    /// <summary>
    /// The created file is a complete TermPoint database: the schema tables exist, SQLite's
    /// integrity check passes, and the app's own context can open it without error.
    /// </summary>
    [Fact]
    public async Task CreateAsync_CreatesValidTermPointSchema()
    {
        var target = TargetPath();
        await CreateAsync(target);

        // Inspect a copy so the created file itself stays untouched.
        var copy = Path.Combine(_localDir, "inspect.db");
        File.Copy(target, copy);
        using (var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False"))
        {
            conn.Open();
            using var tables = conn.CreateCommand();
            tables.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
                "AND name IN ('AcademicYears','Semesters','Instructors','Rooms','Courses','Sections','AppConfiguration')";
            Assert.Equal(7L, (long)tables.ExecuteScalar()!);

            using var integrity = conn.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", (string)integrity.ExecuteScalar()!);
        }

        // The app's real context accepts it as-is (no migration needed, no exception).
        using (new DatabaseContext(copy)) { }

        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateWithoutSqliteAsync(target));
    }

    // ── No WAL on the shipped file ────────────────────────────────────────────

    /// <summary>
    /// The shipped file is in rollback-journal mode: header bytes 18 and 19 (file-format write /
    /// read version) are 1, not 2 (WAL), and SQLite reports <c>delete</c> mode. WAL must never be
    /// active on a network file.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ResultIsNotInWalMode()
    {
        var target = TargetPath();
        await CreateAsync(target);

        var header = new byte[20];
        using (var fs = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            fs.ReadExactly(header);

        Assert.Equal(1, header[18]);
        Assert.Equal(1, header[19]);
        Assert.Equal("delete", JournalModeOfCopy(target), ignoreCase: true);
    }

    /// <summary>No -wal, -shm, -journal or D.tmp is left at the target — only the database itself.</summary>
    [Fact]
    public async Task CreateAsync_LeavesNoSidecarsOrTmpAtTarget()
    {
        var target = TargetPath();
        await CreateAsync(target);

        Assert.Equal(new[] { target }, Directory.GetFileSystemEntries(_shareDir));
    }

    /// <summary>The temporary local build is deleted (with every sidecar) after a successful creation.</summary>
    [Fact]
    public async Task CreateAsync_DeletesLocalTempFile()
    {
        await CreateAsync(TargetPath());

        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }

    // ── Never overwrite ───────────────────────────────────────────────────────

    /// <summary>
    /// An existing target is refused (AlreadyExists) and its bytes are unchanged; nothing else
    /// is created next to it and the local scratch folder is clean.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ExistingTarget_IsNotOverwritten()
    {
        var target = TargetPath("existing.db");
        var original = "precious existing database content"u8.ToArray();
        File.WriteAllBytes(target, original);

        var result = await CreateAsync(target);

        Assert.Equal(NewDatabaseOutcome.AlreadyExists, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(original, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFileSystemEntries(_shareDir));
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }

    // ── Folder creation and failure ───────────────────────────────────────────

    /// <summary>A target in a folder that does not exist yet is created, folder included.</summary>
    [Fact]
    public async Task CreateAsync_MissingTargetFolder_IsCreated()
    {
        var target = Path.Combine(_shareDir, "sub", "deeper", "new.db");

        var result = await CreateAsync(target);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.True(File.Exists(target));
    }

    /// <summary>
    /// A genuine failure (the "folder" is really an existing file, so it cannot be created) is
    /// reported as Failed with the exception, never thrown, and leaves the scratch folder clean.
    /// </summary>
    [Fact]
    public async Task CreateAsync_UncreatableFolder_ReportsFailedWithErrorAndCleansUp()
    {
        var blocker = TargetPath("iam-a-file");
        File.WriteAllText(blocker, "not a directory");
        var target = Path.Combine(blocker, "new.db");

        var result = await CreateAsync(target);

        Assert.Equal(NewDatabaseOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
        Assert.Equal("not a directory", File.ReadAllText(blocker));   // untouched
    }

    /// <summary>Two creations in a row at different targets both succeed and share no state.</summary>
    [Fact]
    public async Task CreateAsync_CalledTwice_CreatesBothDatabases()
    {
        var first  = TargetPath("a.db");
        var second = TargetPath("b.db");

        Assert.Equal(NewDatabaseOutcome.Created, (await CreateAsync(first)).Outcome);
        Assert.Equal(NewDatabaseOutcome.Created, (await CreateAsync(second)).Outcome);

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Empty(Directory.GetFileSystemEntries(_localDir));
    }
}

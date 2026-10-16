using Microsoft.Data.Sqlite;
using TermPoint.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Probe (2026-10-15, before item 10a): does anything a READER does, or the front-door check every open runs, change
/// the shared database D? A save is refused with <see cref="SaveOutcome.SourceModified"/> when D's hash no longer
/// matches the one taken at checkout, so any such change would make an editor's saves fail with no other writer
/// involved.
///
/// <para>The front-door check (<see cref="DatabaseValidator.ValidateAsync"/>) is the one place that opens D with SQLite
/// itself (read-only, <c>PRAGMA integrity_check</c>). The reader's own work only copies D. Both are exercised on a D
/// shaped exactly as a real save leaves it: D′ in WAL mode with its connection open during the save, as
/// <c>DatabaseContext</c> keeps it in production.</para>
/// </summary>
public sealed class FrontDoorCheckProbeTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _sourceDir;
    private readonly string _workingDir;

    /// <summary>Creates a private source folder (stands in for the share) and a private working folder.</summary>
    /// <param name="output">xUnit's per-test output, used to record D's journal-mode header bytes.</param>
    public FrontDoorCheckProbeTests(ITestOutputHelper output)
    {
        _output = output;
        var id = Guid.NewGuid().ToString("N");
        _sourceDir  = Path.Combine(Path.GetTempPath(), $"fdp_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"fdp_wrk_{id}");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Deletes both folders (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_sourceDir,  recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The front-door check on a D written by a real save leaves D byte-identical, leaves no -wal / -shm / -journal
    /// beside it, and holds no handle on it (an immediate rename, which is what the editor's save does, succeeds).
    /// </summary>
    [Fact]
    public async Task FrontDoorCheck_OnADatabaseWrittenByASave_LeavesDUnchanged()
    {
        var db = Path.Combine(_sourceDir, "shared.db");
        var (writer, _) = await CheckoutAndSaveInWalModeAsync(db);
        RecordJournalHeader(db, "D after a real save");
        var before = DbFileTestHelpers.HashOf(db);

        var result = await DatabaseValidator.ValidateAsync(db, logger: new RecordingAppLogger());

        Assert.Equal(DatabaseValidationResult.Ok, result);
        AssertUntouched(db, before);
        Assert.Equal(before, writer.HashAtCheckout);
    }

    /// <summary>
    /// A reader's whole visit — the front-door check, the read-only checkout (copy D → D″) and a refresh — leaves D
    /// byte-identical, so the writer's next save is not refused as <see cref="SaveOutcome.SourceModified"/>.
    /// </summary>
    [Fact]
    public async Task Reader_CheckCheckoutAndRefresh_LeaveDUnchanged_AndTheWriterStillSaves()
    {
        var db = Path.Combine(_sourceDir, "shared.db");
        var (writer, writerDb) = await CheckoutAndSaveInWalModeAsync(db, keepWriterConnection: true);
        var before = DbFileTestHelpers.HashOf(db);

        var reader = new CheckoutService(new WriteLockService(), new RecordingAppLogger(), a => a(), _workingDir);
        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateAsync(db, logger: new RecordingAppLogger()));
        Assert.Equal(CheckoutOutcome.ReadOnly, await reader.CheckoutAsync(db));
        Assert.Equal(RefreshOutcome.Updated, await reader.RefreshReadOnlySnapshotAsync());
        SqliteConnection.ClearAllPools();

        AssertUntouched(db, before);

        // The writer's next save goes through: D is exactly as the writer last saw it.
        using (var cmd = writerDb!.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO _probe VALUES('second edit')";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(SaveOutcome.Success, await writer.SaveAsync());
        writerDb.Dispose();
    }

    /// <summary>
    /// Builds D the way production does: a write checkout, D′ switched to WAL with its connection held open (as
    /// <c>DatabaseContext</c> holds it), one edit, and a real <see cref="CheckoutService.SaveAsync"/>.
    /// </summary>
    /// <param name="db">Where D is created.</param>
    /// <param name="keepWriterConnection">True to return the open D′ connection (the caller disposes it); false to close it.</param>
    /// <returns>The writer's service and, when kept, its open D′ connection.</returns>
    private async Task<(CheckoutService Writer, SqliteConnection? WriterDb)> CheckoutAndSaveInWalModeAsync(
        string db, bool keepWriterConnection = false)
    {
        using (var create = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            create.Open();
            using var cmd = create.CreateCommand();
            cmd.CommandText = "CREATE TABLE _probe(val TEXT)";
            cmd.ExecuteNonQuery();
        }

        var writer = new CheckoutService(new WriteLockService(), new RecordingAppLogger(), a => a(), _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await writer.CheckoutAsync(db));

        var conn = new SqliteConnection($"Data Source={writer.WorkingPath};Mode=ReadWrite;Pooling=False");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO _probe VALUES('first edit');";
            cmd.ExecuteNonQuery();
        }
        writer.MarkDirty();

        Assert.Equal(SaveOutcome.Success, await writer.SaveAsync());

        if (keepWriterConnection)
            return (writer, conn);

        conn.Dispose();
        return (writer, null);
    }

    /// <summary>Asserts D still has hash <paramref name="before"/>, has no sidecar files, and can be renamed at once.</summary>
    /// <param name="db">Path of D.</param>
    /// <param name="before">D's hash before the operation under test.</param>
    private static void AssertUntouched(string db, string before)
    {
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.False(File.Exists(db + "-wal"), "No -wal may be left beside D.");
        Assert.False(File.Exists(db + "-shm"), "No -shm may be left beside D.");
        Assert.False(File.Exists(db + "-journal"), "No -journal may be left beside D.");

        var moved = db + ".moved";
        File.Move(db, moved);
        File.Move(moved, db);
    }

    /// <summary>
    /// Writes D's file-format version bytes (offsets 18 and 19: 1 = rollback journal, 2 = WAL) to the test output,
    /// so the probe also records which journal mode a saved D advertises.
    /// </summary>
    /// <param name="db">Path of D.</param>
    /// <param name="label">What the reading is of.</param>
    private void RecordJournalHeader(string db, string label)
    {
        var header = new byte[20];
        using (var stream = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            stream.ReadExactly(header);
        _output.WriteLine($"{label}: write version {header[18]}, read version {header[19]} (1 = rollback, 2 = WAL)");
    }
}

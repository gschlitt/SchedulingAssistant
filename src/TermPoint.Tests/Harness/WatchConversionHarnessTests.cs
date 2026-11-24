using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Access Analyzer piece B, the two paths that open a FRESH copy of the shared file D with the real
/// <see cref="CheckoutService"/> and a real <see cref="DatabaseContext"/> wired to its callbacks, onto a file a 1.2.4
/// writer saved (schema version 1, access watches still in <c>ProgramWatches</c>):
/// <list type="bullet">
///   <item>a reader pressing Refresh (<c>RefreshReadOnlySnapshotAsync</c>, then <c>ReinitializeConnection</c>);</item>
///   <item>a writer whose lock was lost (<c>DemoteToReadOnlyAsync</c>, then <c>ReinitializeConnection</c> on the fresh D'').</item>
/// </list>
/// Both must show converted data (a watched tag), and neither may write to D.
///
/// <para>Kept as two ordinary-structure tests beside the harness (gated by <c>TERMPOINT_HARNESS</c>) rather than as
/// harness scenarios: the harness's <c>Computer</c> drives a fake database layer, and these two checks need the real
/// context, which <c>RefreshTests</c> and <c>ReadOnlySessionTests</c> use the same way.</para>
/// </summary>
public sealed class WatchConversionHarnessTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates a private temp folder.</summary>
    public WatchConversionHarnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"wch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Releases handles and removes the temp folder.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Writes D as a 1.2.4 writer would have saved it: version 1, one tag, one enabled single-tag watch on it.
    /// </summary>
    /// <param name="path">Where to write D.</param>
    /// <param name="tagName">The tag's name (differs between saves so a refresh can be seen to take effect).</param>
    private static void WriteVersionOneD(string path, string tagName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 1";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA journal_mode = DELETE";
            cmd.ExecuteScalar();
            cmd.CommandText = "INSERT INTO SchedulingEnvironmentValues (id, type, name, data) VALUES ('tag-1', 'tag', $n, $d)";
            cmd.Parameters.AddWithValue("$n", tagName);
            cmd.Parameters.AddWithValue("$d", JsonHelpers.Serialize(new SchedulingEnvironmentValue { Id = "tag-1", Name = tagName }));
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO ProgramWatches (id, semester_id, name, mode, data) VALUES ('w1', 'sem-1', $n, 'tag', $w)";
            cmd.Parameters.AddWithValue("$w", $"{{\"id\":\"w1\",\"name\":{System.Text.Json.JsonSerializer.Serialize(tagName)},\"mode\":0,\"isEnabled\":true,\"tagIds\":[\"tag-1\"],\"courseIds\":[],\"levelIds\":[]}}");
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    /// <summary>A real checkout service on its own working folder.</summary>
    /// <param name="name">Folder name.</param>
    /// <param name="lockService">Receives the service's lock service.</param>
    private CheckoutService NewService(string name, out WriteLockService lockService)
    {
        var work = Path.Combine(_dir, name);
        Directory.CreateDirectory(work);
        lockService = new WriteLockService();
        return new CheckoutService(lockService: lockService, logger: new QuietLogger(), dispatch: a => a(), workingDir: work);
    }

    /// <summary>
    /// A reader refreshing onto an unconverted file a 1.2.4 writer saved sees the converted data: the refresh swaps D''
    /// for the fresh copy and <c>ReinitializeConnection</c> converts it (read-only afterwards); D is not touched.
    /// </summary>
    [HarnessFact]
    public async Task AReaderRefreshingOntoAVersionOneFile_SeesConvertedData()
    {
        var d = Path.Combine(_dir, "share", "dept.db");
        WriteVersionOneD(d, "Year 3");

        var writer = NewService("writer", out _);
        Assert.Equal(CheckoutOutcome.WriteAccess, await writer.CheckoutAsync(d));
        var reader = NewService("reader", out _);
        Assert.Equal(CheckoutOutcome.ReadOnly, await reader.CheckoutAsync(d));
        using var ctx = new DatabaseContext(reader.WorkingPath, readOnly: true);   // converts D'' (a copy of the old D)
        Assert.True(new SchedulingEnvironmentRepository(ctx).GetById("tag-1")!.IsAccessWatch);

        // The 1.2.4 writer saves again: a new version 1 file replaces D (the rename), with different content.
        WriteVersionOneD(d, "Year 3 renamed");
        var dBytes = File.ReadAllBytes(d);

        var outcome = await reader.RefreshReadOnlySnapshotAsync(
            beforeOverwrite: () => { ctx.CloseConnection(); return Task.CompletedTask; },
            afterOverwrite:  () => { ctx.ReinitializeConnection(reader.WorkingPath, readOnly: true); return Task.CompletedTask; });

        Assert.Equal(RefreshOutcome.Updated, outcome);
        Assert.True(ctx.IsReadOnly);
        var tag = new SchedulingEnvironmentRepository(ctx).GetById("tag-1")!;
        Assert.Equal("Year 3 renamed", tag.Name);
        Assert.True(tag.IsAccessWatch, "the refreshed copy must be converted, or the reader sees no watches");
        Assert.Equal(dBytes, File.ReadAllBytes(d));                                  // the shared file was never written
        using var check = new SqliteConnection($"Data Source={d};Pooling=False;Mode=ReadOnly");
        check.Open();
        using var cmd = check.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ProgramWatches";
        Assert.Equal(1L, cmd.ExecuteScalar());                                         // D still holds the unconverted rows
    }

    /// <summary>
    /// A writer demoted after losing the lock, with no unsaved edits, reopens on a fresh read-only copy of D. When the last
    /// saver was a 1.2.4 writer that copy is unconverted, and <c>ReinitializeConnection</c> must convert it.
    /// </summary>
    [HarnessFact]
    public async Task AWriterDemotedOntoAVersionOneFile_SeesConvertedData()
    {
        var d = Path.Combine(_dir, "share2", "dept.db");
        WriteVersionOneD(d, "Year 3");
        var bob = NewService("bob", out var bobLock);
        Assert.Equal(CheckoutOutcome.WriteAccess, await bob.CheckoutAsync(d));
        using var ctx = new DatabaseContext(bob.WorkingPath, onFirstWrite: () => bob.MarkDirty(), onEveryWrite: () => bob.NoteEdit());
        Assert.True(new SchedulingEnvironmentRepository(ctx).GetById("tag-1")!.IsAccessWatch);   // D' converted at open
        var dBytes = File.ReadAllBytes(d);

        var demoted = await bob.DemoteToReadOnlyAsync(
            beforeClose: () =>
            {
                bobLock.PendingRelease?.Wait(TimeSpan.FromSeconds(10));   // settle the background lock-file deletion
                ctx.CloseConnection();
                return Task.CompletedTask;
            },
            afterOpen: () => { ctx.ReinitializeConnection(bob.WorkingPath, readOnly: true); return Task.CompletedTask; });

        Assert.True(demoted);
        Assert.Equal(CheckoutMode.ReadOnly, bob.Mode);
        Assert.True(ctx.IsReadOnly);
        Assert.True(new SchedulingEnvironmentRepository(ctx).GetById("tag-1")!.IsAccessWatch);
        Assert.Equal(dBytes, File.ReadAllBytes(d));
    }

    /// <summary>No-op logger.</summary>
    private sealed class QuietLogger : IAppLogger
    {
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }
}

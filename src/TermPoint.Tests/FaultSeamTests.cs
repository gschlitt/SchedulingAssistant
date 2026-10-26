using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the two test-only seams of spec step 9.2b: (4) the network fault injector
/// (<see cref="NetworkFaults"/>, consulted by <see cref="NetworkFileOps"/>) and (5) the crash points
/// (<see cref="CrashPoints"/>, hit at step boundaries in the save, checkout, release, restore and lock code).
/// Both are static hooks that are null in the app; every test resets them in <see cref="Dispose"/>.
///
/// <para>The network tests drive the real save pipeline through <see cref="CheckoutService.SaveAsync"/> and check
/// what a faulted step returns, what it leaves on the share, and that the answer reaches the user as the same
/// banner a real outage would. The crash-point tests check the order in which a save hits its points, that an
/// exception thrown from a hook is contained by the save's existing safety net, and that the catalogue
/// <see cref="CrashPoints.All"/> matches the points hit in the source exactly.</para>
/// </summary>
public sealed class FaultSeamTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Folder playing the share: holds D, D.tmp and D.lock.</summary>
    private readonly string _shareDir;

    /// <summary>Local folder for working copies (D', its marker and snapshot).</summary>
    private readonly string _workingDir;

    /// <summary>Creates isolated temporary folders for the share and for the local working copies.</summary>
    public FaultSeamTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"fst_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"fst_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Resets both static hooks and deletes the temporary folders (best effort).</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        CrashPoints.Hook     = null;
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Path of D inside the fake share.</summary>
    private string DbPath => Path.Combine(_shareDir, "test.db");

    /// <summary>True when <paramref name="path"/> is inside the fake share (so a fault should apply to it).</summary>
    /// <param name="path">A path, or null.</param>
    private bool OnShare(string? path)
        => path is not null && path.StartsWith(_shareDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a real, empty SQLite database at <paramref name="path"/>.</summary>
    /// <param name="path">Where to create it.</param>
    private static void CreateSqliteDb(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
    }

    /// <summary>Inserts a value into the <c>_test</c> table of the database at <paramref name="dbPath"/>.</summary>
    /// <param name="dbPath">The database.</param>
    /// <param name="value">The value to add.</param>
    private static void InsertTestValue(string dbPath, string value)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the newest value of the <c>_test</c> table, or null when the table is absent or empty.</summary>
    /// <param name="dbPath">The database.</param>
    private static string? ReadTestValue(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = '_test'";
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return null;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Creates a service wired to this fixture (synchronous dispatcher, the given logger, private working folder).
    /// </summary>
    /// <param name="logger">The logger to inject.</param>
    private CheckoutService CreateService(IAppLogger logger)
        => new(lockService: new WriteLockService(), logger: logger, dispatch: action => action(), workingDir: _workingDir);

    /// <summary>
    /// Checks out a fresh database for writing and leaves one unsaved edit with the dirty marker written.
    /// </summary>
    /// <param name="logger">The logger to inject.</param>
    /// <returns>The service, ready to save.</returns>
    private async Task<CheckoutService> CheckoutWithUnsavedEditAsync(IAppLogger logger)
    {
        var svc = CreateService(logger);
        CreateSqliteDb(DbPath);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(DbPath));
        InsertTestValue(svc.WorkingPath, "unsaved-edit");
        svc.MarkDirty();
        return svc;
    }

    /// <summary>Records every banner a service raises through <see cref="CheckoutService.SaveFailed"/>.</summary>
    /// <param name="svc">The service to observe.</param>
    private static List<string> CaptureBanners(CheckoutService svc)
    {
        var banners = new List<string>();
        svc.SaveFailed += (message, _) => banners.Add(message);
        return banners;
    }

    /// <summary>A logger that remembers breadcrumbs and info lines; everything else is dropped.</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        /// <summary>Every breadcrumb message, in order.</summary>
        public ConcurrentQueue<string> Breadcrumbs { get; } = new();

        /// <summary>Every info line, in order.</summary>
        public ConcurrentQueue<string> Infos { get; } = new();

        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged { add { } remove { } }
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) => Infos.Enqueue(message);
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) => Breadcrumbs.Enqueue(message);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Seam 4 — network faults through a full save
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The share does not answer the hash of D at the start of a save (TimeOut): the save reports CopyError with the
    /// "location isn't responding" banner at once (not after the 5-second deadline), D and D' are untouched, and
    /// nothing was written to the share. With the fault gone the same save succeeds.
    /// </summary>
    [Fact]
    public async Task TimeOut_OnTheSourceHash_FailsTheSaveQuickly_AndChangesNothing()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        var banners = CaptureBanners(svc);
        var before = NetworkFileOps.ComputeHash(DbPath);

        NetworkFaults.Decide = r => r.Label == "ComputeHash" && r.Path == DbPath
            ? NetworkFaultDecision.TimeOut
            : NetworkFaultDecision.None;

        var clock = Stopwatch.StartNew();
        var outcome = await svc.SaveAsync();
        clock.Stop();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.True(clock.ElapsedMilliseconds < NetworkFileOps.TimeoutMs / 2,
            $"A faulted timeout must not wait the deadline (took {clock.ElapsedMilliseconds} ms).");
        Assert.Equal(SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false), Assert.Single(banners));   // the one sticky outage notice (spec item 28)
        Assert.Equal(before, NetworkFileOps.ComputeHash(DbPath));
        Assert.False(File.Exists(DbPath + ".tmp"));
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));

        NetworkFaults.Decide = null;
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(DbPath));
    }

    /// <summary>
    /// Spec item 28 (F15): an AUTOSAVE that cannot reach the share raises the same outage notice as a manual save, and
    /// it is STICKY: <c>autoDismiss</c> is false although the save was an autosave (it used to clear itself after 30
    /// seconds while saves kept failing). With autosave on, the text says TermPoint will try again automatically.
    /// A second failing autosave raises the same notice again (the window shows it once); once the share answers, the
    /// next autosave succeeds and raises <see cref="CheckoutService.SaveCompleted"/>, which is what clears the notice
    /// in the window. D and D' are untouched by the failures, and the edit reaches D with the next successful save.
    /// </summary>
    [Fact]
    public async Task AnAutoSaveThatTimesOut_RaisesTheStickyOutageNotice_AndTheNextSuccessfulAutoSaveCompletes()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        var banners = new List<(string Message, bool AutoDismiss)>();
        svc.SaveFailed += (message, autoDismiss) => banners.Add((message, autoDismiss));
        var completed = 0;
        svc.SaveCompleted += () => completed++;
        svc.StartAutoSave();   // autosave is on (the timer fires only every AutoSaveIntervalMinutes; the test drives ticks by hand)
        try
        {
            NetworkFaults.Decide = r => r.Label == "ComputeHash" && r.Path == DbPath
                ? NetworkFaultDecision.TimeOut
                : NetworkFaultDecision.None;

            await svc.TickAutoSaveAsync();
            await svc.TickAutoSaveAsync();

            var expected = (
                "TermPoint can't reach the database location. Your changes are kept on this computer. " +
                "TermPoint will try again automatically.",
                false);
            Assert.Equal(new[] { expected, expected }, banners);
            Assert.Equal(0, completed);
            Assert.Null(ReadTestValue(DbPath));
            Assert.True(File.Exists(svc.WorkingPath + ".dirty"));

            NetworkFaults.Decide = null;
            await svc.TickAutoSaveAsync();

            Assert.Equal(2, banners.Count);     // the third autosave raised no banner
            Assert.Equal(1, completed);         // it completed: the window clears the notice on this event
            Assert.Equal("unsaved-edit", ReadTestValue(DbPath));
        }
        finally
        {
            svc.StopAutoSave();
            NetworkFaults.Decide = null;
        }
    }

    /// <summary>
    /// Spec item 28 (F15): with autosave OFF the outage notice for a failed save tells the user to press Save once the
    /// connection is back (nothing retries by itself), and is sticky for a manual save as for an autosave.
    /// </summary>
    [Fact]
    public async Task AManualSaveThatTimesOut_WithAutoSaveOff_SaysPressSaveOnceTheConnectionIsBack()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        var banners = new List<(string Message, bool AutoDismiss)>();
        svc.SaveFailed += (message, autoDismiss) => banners.Add((message, autoDismiss));
        Assert.False(svc.IsAutoSaveRunning);

        NetworkFaults.Decide = r => r.Label == "ComputeHash" && r.Path == DbPath
            ? NetworkFaultDecision.TimeOut
            : NetworkFaultDecision.None;
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        Assert.Equal(
            ("TermPoint can't reach the database location. Your changes are kept on this computer. " +
             "Press Save once the connection is back.", false),
            Assert.Single(banners));
    }

    /// <summary>
    /// The ghost rename: the rename onto D runs but is reported as timed out (TimeOutAfterRunning), and the
    /// confirming re-hash of D also does not answer. D holds the new content already, but the save reports CopyError
    /// and records the snapshot hash in the dirty marker. The next save, with the share back, recognises D as its
    /// own earlier write ("GhostRenameHealed") and succeeds, pushing a later edit as well.
    /// </summary>
    [Fact]
    public async Task TimeOutAfterRunning_OnTheRename_LeavesAGhost_TheNextSaveHeals()
    {
        var log = new RecordingLogger();
        var svc = await CheckoutWithUnsavedEditAsync(log);
        var banners = CaptureBanners(svc);

        var renamed = false;
        NetworkFaults.Decide = r =>
        {
            if (r.Label == "File.Move" && r.DestinationPath == DbPath)
            {
                renamed = true;
                return NetworkFaultDecision.TimeOutAfterRunning;
            }
            // After the rename, also stop the share answering the confirming hash of D.
            return renamed && r.Label == "ComputeHash" && r.Path == DbPath
                ? NetworkFaultDecision.TimeOut
                : NetworkFaultDecision.None;
        };

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal(SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false), Assert.Single(banners));   // the one sticky outage notice (spec item 28)
        Assert.Equal("unsaved-edit", ReadTestValue(DbPath));   // the abandoned rename landed
        Assert.False(File.Exists(DbPath + ".tmp"));
        var marker = JsonSerializer.Deserialize<DirtyMarkerData>(File.ReadAllText(svc.WorkingPath + ".dirty"));
        Assert.NotNull(marker?.PendingSaveHash);
        Assert.Equal(NetworkFileOps.ComputeHash(DbPath), marker!.PendingSaveHash, ignoreCase: true);

        // The share is back; the user edits again and saves.
        NetworkFaults.Decide = null;
        InsertTestValue(svc.WorkingPath, "second-edit");
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Contains("Save: GhostRenameHealed", log.Breadcrumbs);
        Assert.Equal("second-edit", ReadTestValue(DbPath));
    }

    /// <summary>
    /// The copy of the snapshot to D.tmp throws an IOException (Throw): the save reports CopyError with the
    /// "writing failed" banner, no D.tmp is left on the share, D is unchanged, and D' with its marker is kept.
    /// </summary>
    [Fact]
    public async Task Throw_OnTheTmpCopy_FailsTheSave_AndLeavesNoTmp()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        var banners = CaptureBanners(svc);
        var before = NetworkFileOps.ComputeHash(DbPath);

        NetworkFaults.Decide = r => r.Label == "CopyFile" && r.DestinationPath == DbPath + ".tmp"
            ? NetworkFaultDecision.Throwing(new IOException("simulated: the share refused the write"))
            : NetworkFaultDecision.None;

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal(SavePrompt.ForTransientFailure(SavePrompt.WriteFailedMessage, isAutoSave: false), Assert.Single(banners));
        Assert.Equal(before, NetworkFileOps.ComputeHash(DbPath));
        Assert.False(File.Exists(DbPath + ".tmp"));
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// A decision that fails only paths under the share leaves local operations working: the same helpers on a
    /// local file complete normally, while on the share they report "not completed" at once.
    /// </summary>
    [Fact]
    public async Task ShareOnlyFaults_LeaveLocalOperationsWorking()
    {
        var shareFile = Path.Combine(_shareDir, "a.txt");
        var localFile = Path.Combine(_workingDir, "b.txt");
        File.WriteAllText(shareFile, "share");
        File.WriteAllText(localFile, "local");

        NetworkFaults.Decide = r => OnShare(r.Path) || OnShare(r.DestinationPath)
            ? NetworkFaultDecision.TimeOut
            : NetworkFaultDecision.None;

        // Local: all complete.
        Assert.Equal((true, true), await NetworkFileOps.ExistsAsync(localFile));
        Assert.Equal((true, "local"), await NetworkFileOps.ReadAllTextAsync(localFile));
        var (hashed, hash) = await NetworkFileOps.ComputeHashAsync(localFile);
        Assert.True(hashed);
        Assert.NotNull(hash);
        var localCopy = Path.Combine(_workingDir, "c.txt");
        Assert.True(await NetworkFileOps.CopyAsync(localFile, localCopy));
        Assert.Equal("local", File.ReadAllText(localCopy));

        // Share: all report not completed, and nothing ran.
        Assert.Equal((false, false), await NetworkFileOps.ExistsAsync(shareFile));
        Assert.Equal((false, null), await NetworkFileOps.ReadAllTextAsync(shareFile));
        Assert.Equal((false, null), await NetworkFileOps.ComputeHashAsync(shareFile));
        Assert.False(await NetworkFileOps.CopyAsync(localFile, Path.Combine(_shareDir, "copy.txt")));
        Assert.False(File.Exists(Path.Combine(_shareDir, "copy.txt")));
        Assert.False(await NetworkFileOps.MoveAsync(localCopy, Path.Combine(_shareDir, "moved.txt")));
        Assert.True(File.Exists(localCopy));
        Assert.False(await NetworkFileOps.WriteAllTextAsync(Path.Combine(_shareDir, "w.txt"), "x"));
        Assert.False(File.Exists(Path.Combine(_shareDir, "w.txt")));
    }

    /// <summary>
    /// ProbeFileAsync maps faults the way it maps real outcomes: a timeout, a thrown I/O error and a run-then-timeout
    /// are all Unreachable; a thrown "not found" is Missing; with no fault the real answer comes back.
    /// </summary>
    [Fact]
    public async Task ProbeFile_MapsFaultsLikeRealOutcomes()
    {
        var file = Path.Combine(_shareDir, "probe.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(FileProbeResult.Exists, await NetworkFileOps.ProbeFileAsync(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOut;
        Assert.Equal(FileProbeResult.Unreachable, await NetworkFileOps.ProbeFileAsync(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOutAfterRunning;
        Assert.Equal(FileProbeResult.Unreachable, await NetworkFileOps.ProbeFileAsync(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.Throwing(new IOException("network name deleted"));
        Assert.Equal(FileProbeResult.Unreachable, await NetworkFileOps.ProbeFileAsync(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.Throwing(new FileNotFoundException());
        Assert.Equal(FileProbeResult.Missing, await NetworkFileOps.ProbeFileAsync(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.Throwing(new DirectoryNotFoundException());
        Assert.Equal(FileProbeResult.Missing, await NetworkFileOps.ProbeFileAsync(file));

        NetworkFaults.Decide = null;
        Assert.Equal(FileProbeResult.Exists, await NetworkFileOps.ProbeFileAsync(file));
    }

    /// <summary>
    /// DeleteAsync never throws: a timeout and a thrown error both give false and leave the file; a run-then-timeout
    /// gives false but the file is really gone (the abandoned delete landed).
    /// </summary>
    [Fact]
    public async Task Delete_ReturnsFalseUnderEveryFault_AndHonoursRunThenTimeOut()
    {
        var file = Path.Combine(_shareDir, "del.txt");

        File.WriteAllText(file, "x");
        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOut;
        Assert.False(await NetworkFileOps.DeleteAsync(file));
        Assert.True(File.Exists(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.Throwing(new IOException("boom"));
        Assert.False(await NetworkFileOps.DeleteAsync(file));
        Assert.True(File.Exists(file));

        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOutAfterRunning;
        Assert.False(await NetworkFileOps.DeleteAsync(file));
        Assert.False(File.Exists(file));

        File.WriteAllText(file, "x");
        NetworkFaults.Decide = null;
        Assert.True(await NetworkFileOps.DeleteAsync(file));
        Assert.False(File.Exists(file));
    }

    /// <summary>
    /// The copy and the hash honour all three kinds the way their docs say: TimeOut does nothing, TimeOutAfterRunning
    /// does the whole job and still reports "not completed", Throw throws before any work.
    /// </summary>
    [Fact]
    public async Task CopyAndHash_HonourEachFaultKind()
    {
        var src = Path.Combine(_shareDir, "src.bin");
        var dst = Path.Combine(_shareDir, "dst.bin");
        File.WriteAllBytes(src, new byte[200_000]);

        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOut;
        Assert.False(await NetworkFileOps.CopyAsync(src, dst));
        Assert.False(File.Exists(dst));

        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOutAfterRunning;
        Assert.False(await NetworkFileOps.CopyAsync(src, dst));
        Assert.Equal(200_000, new FileInfo(dst).Length);
        Assert.Equal((false, null), await NetworkFileOps.ComputeHashAsync(src));

        File.Delete(dst);
        NetworkFaults.Decide = _ => NetworkFaultDecision.Throwing(new IOException("boom"));
        await Assert.ThrowsAsync<IOException>(() => NetworkFileOps.CopyAsync(src, dst));
        await Assert.ThrowsAsync<IOException>(() => NetworkFileOps.ComputeHashAsync(src));
        Assert.False(File.Exists(dst));
    }

    /// <summary>
    /// The generic RunAsync (used directly by callers such as "lock acquire") is consulted by its label and gives
    /// exactly what a real timeout gives: (false, default) for a result-returning operation, false for an action.
    /// A run-then-timeout runs the operation once; a plain timeout never runs it.
    /// </summary>
    [Fact]
    public async Task RunAsync_ByLabel_ReturnsRealTimeoutResults()
    {
        var ran = 0;

        NetworkFaults.Decide = r => r.Label == "lock acquire" ? NetworkFaultDecision.TimeOut : NetworkFaultDecision.None;
        Assert.False(await NetworkFileOps.RunAsync(() => { ran++; }, "lock acquire"));
        Assert.Equal((false, 0), await NetworkFileOps.RunAsync(() => { ran++; return 7; }, "lock acquire"));
        Assert.Equal(0, ran);
        Assert.Equal((true, 7), await NetworkFileOps.RunAsync(() => { ran++; return 7; }, "other"));
        Assert.Equal(1, ran);

        NetworkFaults.Decide = _ => NetworkFaultDecision.TimeOutAfterRunning;
        Assert.False(await NetworkFileOps.RunAsync(() => { ran++; }, "lock acquire"));
        Assert.Equal(2, ran);

        NetworkFaults.Decide = _ => NetworkFaultDecision.Throwing(new InvalidOperationException("injected"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => NetworkFileOps.RunAsync(() => { ran++; }, "lock acquire"));
        Assert.Equal(2, ran);
    }

    /// <summary>
    /// The injector is consulted exactly once per logical operation by every named helper (a helper built on
    /// RunAsync must not be consulted a second time), with the helper's label and the paths it knows.
    /// </summary>
    [Fact]
    public async Task EveryNamedHelper_IsConsultedExactlyOnce_WithItsLabelAndPaths()
    {
        var a = Path.Combine(_shareDir, "a.db");
        var b = Path.Combine(_shareDir, "b.db");
        var c = Path.Combine(_shareDir, "c.db");
        CreateSqliteDb(a);

        var seen = new ConcurrentQueue<NetworkFaultRequest>();
        NetworkFaults.Decide = r => { seen.Enqueue(r); return NetworkFaultDecision.None; };

        async Task<NetworkFaultRequest> Single(Func<Task> call)
        {
            while (seen.TryDequeue(out _)) { }
            await call();
            Assert.Single(seen);
            seen.TryDequeue(out var request);
            return request;
        }

        Assert.Equal(new NetworkFaultRequest("File.Exists", a, null), await Single(() => NetworkFileOps.ExistsAsync(a)));
        Assert.Equal(new NetworkFaultRequest("Directory.Exists", _shareDir, null), await Single(() => NetworkFileOps.DirectoryExistsAsync(_shareDir)));
        Assert.Equal(new NetworkFaultRequest("ProbeFile", a, null), await Single(() => NetworkFileOps.ProbeFileAsync(a)));
        Assert.Equal(new NetworkFaultRequest("ComputeHash", a, null), await Single(() => NetworkFileOps.ComputeHashAsync(a)));
        Assert.Equal(new NetworkFaultRequest("CopyFile", a, b), await Single(() => NetworkFileOps.CopyAsync(a, b)));
        Assert.Equal(new NetworkFaultRequest("File.Move", b, c), await Single(() => NetworkFileOps.MoveAsync(b, c)));
        Assert.Equal(new NetworkFaultRequest("File.WriteAllText", b, null), await Single(() => NetworkFileOps.WriteAllTextAsync(b, "x")));
        Assert.Equal(new NetworkFaultRequest("File.ReadAllText", b, null), await Single(() => NetworkFileOps.ReadAllTextAsync(b)));
        Assert.Equal(new NetworkFaultRequest("File.Delete", b, null), await Single(() => NetworkFileOps.DeleteAsync(b)));
        Assert.Equal(new NetworkFaultRequest("CheckIntegrity", a, null), await Single(() => NetworkFileOps.CheckIntegrityAsync(a)));
        Assert.Equal(new NetworkFaultRequest("CheckIntegrity", a, null), await Single(() => NetworkFileOps.CheckIntegrityWithSummaryAsync(a)));
        Assert.Equal(new NetworkFaultRequest("lock acquire", null, null), await Single(() => NetworkFileOps.RunAsync(() => { }, "lock acquire")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Seam 5 — crash points
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A full successful save hits every Save.* point, in catalogue order, and no other point.
    /// </summary>
    [Fact]
    public async Task SuccessfulSave_HitsEverySavePoint_InCatalogueOrder()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        var hits = new ConcurrentQueue<string>();
        CrashPoints.Hook = hits.Enqueue;

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());

        Assert.Equal(CrashPoints.All.Where(n => n.StartsWith("Save.")), hits);
    }

    /// <summary>
    /// A checkout for writing, then the first edit, hit the checkout and dirty-marker points in order (the lock
    /// file's creation first, since the checkout takes the lock).
    /// </summary>
    [Fact]
    public async Task CheckoutAndFirstEdit_HitTheirPointsInOrder()
    {
        var svc = CreateService(new RecordingLogger());
        CreateSqliteDb(DbPath);
        var hits = new ConcurrentQueue<string>();
        CrashPoints.Hook = hits.Enqueue;

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(DbPath));
        InsertTestValue(svc.WorkingPath, "edit");
        svc.MarkDirty();

        Assert.Equal(
            ["Lock.AfterCreated", "Checkout.AfterLockAcquired", "Checkout.AfterCopyVerified",
             "Checkout.AfterVersionGate", "Dirty.AfterMarkerWritten"],
            hits);
    }

    /// <summary>
    /// A hook that throws at Save.AfterTmpCopy (standing in for a process kill there) stops the save with D
    /// unchanged and D.tmp left on the share, and the exception does not escape SaveAsync: its existing safety net
    /// turns it into CopyError with the "unexpected" banner, leaving D' and the marker in place.
    /// </summary>
    [Fact]
    public async Task ThrowingHook_AtTmpCopy_LeavesDUnchanged_AndATmpOnDisk_AndSaveAsyncContainsIt()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        var banners = CaptureBanners(svc);
        var before = NetworkFileOps.ComputeHash(DbPath);
        CrashPoints.Hook = name =>
        {
            if (name == "Save.AfterTmpCopy") throw new InvalidOperationException("simulated crash");
        };

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal(SavePrompt.ForTransientFailure(SavePrompt.UnexpectedMessage, isAutoSave: false), Assert.Single(banners));
        Assert.Equal(before, NetworkFileOps.ComputeHash(DbPath));
        Assert.True(File.Exists(DbPath + ".tmp"), "A crash after the copy leaves D.tmp behind.");
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// A hook that throws at Save.AfterRename leaves D already replaced and the dirty marker still present: the
    /// state the recovery logic must handle after a kill right after the rename.
    /// </summary>
    [Fact]
    public async Task ThrowingHook_AfterTheRename_LeavesDReplaced_AndTheMarkerPresent()
    {
        var svc = await CheckoutWithUnsavedEditAsync(new RecordingLogger());
        CrashPoints.Hook = name =>
        {
            if (name == "Save.AfterRename") throw new InvalidOperationException("simulated crash");
        };

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        Assert.Equal("unsaved-edit", ReadTestValue(DbPath));
        Assert.False(File.Exists(DbPath + ".tmp"));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// The catalogue is well formed: no duplicates, and every name has the dotted <c>Group.AfterStep</c> shape.
    /// </summary>
    [Fact]
    public void Catalogue_HasNoDuplicates_AndEveryNameIsWellFormed()
    {
        Assert.Equal(CrashPoints.All.Count, CrashPoints.All.Distinct().Count());
        foreach (var name in CrashPoints.All)
            Assert.Matches(@"^[A-Z][A-Za-z]+\.After[A-Za-z]+$", name);
    }

    /// <summary>
    /// Every point name passed to <c>CrashPoints.Hit("…")</c> anywhere in the production source is in
    /// <see cref="CrashPoints.All"/>, and every catalogue entry is hit somewhere. The source is scanned from the
    /// repository (found by walking up from the test binaries), so adding a hit without cataloguing it, or the
    /// reverse, fails here.
    /// </summary>
    [Fact]
    public void Catalogue_MatchesTheHitsInTheSource_Exactly()
    {
        var productionDir = FindProductionSourceDirectory();
        var pattern = new Regex(@"CrashPoints\.Hit\(\s*""([^""]+)""\s*\)", RegexOptions.CultureInvariant);

        var hit = Directory.EnumerateFiles(productionDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => pattern.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.Empty(hit.Except(CrashPoints.All));
        Assert.Empty(CrashPoints.All.Except(hit));
    }

    /// <summary>
    /// Finds <c>src/TermPoint</c> (the production project folder). The compiler records this file's own path
    /// (<see cref="CallerFilePathAttribute"/>), which works wherever the test binaries are built to (the suite is
    /// often run with <c>-o</c>); walking up from the binaries is the fallback.
    /// </summary>
    /// <param name="thisFile">Filled in by the compiler with the full path of this source file.</param>
    /// <returns>The full path of the production project folder.</returns>
    /// <exception cref="DirectoryNotFoundException">The repository layout could not be found.</exception>
    private static string FindProductionSourceDirectory([CallerFilePath] string thisFile = "")
    {
        var fromSource = Path.Combine(Path.GetDirectoryName(thisFile) ?? "", "..", "TermPoint");
        if (File.Exists(Path.Combine(fromSource, "TermPoint.csproj")))
            return Path.GetFullPath(fromSource);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "TermPoint");
            if (File.Exists(Path.Combine(candidate, "TermPoint.csproj")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not find src/TermPoint from " + thisFile);
    }
}

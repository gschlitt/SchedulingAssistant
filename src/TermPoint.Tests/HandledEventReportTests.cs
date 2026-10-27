using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 7 (steps 7a, 7b): handled events reach the developers as named, once-per-run reports. Covers the reporter
/// itself (<see cref="HandledEventReporter"/>, <see cref="HandledEventException"/>) and the report sites in
/// <see cref="CheckoutService"/>, driven through real saves and opens with a <see cref="RecordingAppLogger"/>, so
/// nothing reaches BugSnag. Each test makes its own logger, which also gives it its own "already reported" memory.
/// </summary>
public sealed class HandledEventReportTests : IDisposable
{
    private readonly string _shareDir;
    private readonly string _workingDir;

    /// <summary>Creates a folder playing the share and a local working folder.</summary>
    public HandledEventReportTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"her_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"her_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Resets the fault injector and deletes both folders (best effort).</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        _lastLockService?.PendingRelease?.Wait(TimeSpan.FromSeconds(10));   // no background delete may outlive the test
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    private string DbPath => Path.Combine(_shareDir, "dept.db");

    /// <summary>Creates a small real SQLite database with a table for test values.</summary>
    private static void CreateDb(string path, string value = "start")
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>The lock service of the most recent <see cref="NewService"/> (to settle its background release).</summary>
    private WriteLockService? _lastLockService;

    /// <summary>A service on this fixture's working folder, with a synchronous dispatcher.</summary>
    private CheckoutService NewService(IAppLogger logger)
    {
        _lastLockService = new WriteLockService();
        return new(lockService: _lastLockService, logger: logger, dispatch: a => a(), workingDir: _workingDir);
    }

    /// <summary>Checks out the database for writing and makes one unsaved edit (marker written).</summary>
    private async Task<CheckoutService> CheckoutWithEditAsync(IAppLogger logger)
    {
        CreateDb(DbPath);
        var svc = NewService(logger);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(DbPath));
        CreateDb(svc.WorkingPath, "edit");
        svc.MarkDirty();
        return svc;
    }

    /// <summary>The reports of one kind a logger received.</summary>
    private static List<HandledReport> ReportsOf(RecordingAppLogger log, HandledEvent kind)
        => log.Handled.Where(r => r.Exception is HandledEventException e && e.Kind == kind).ToList();

    // ── The reporter ────────────────────────────────────────────────────────────

    /// <summary>
    /// A report carries the named exception with its fixed message (and a stack trace), the same text as its context,
    /// a grouping key per kind and the metadata under the "event" tab; a second report of the same kind to the same
    /// logger is not sent, another kind is, and another logger has its own memory.
    /// </summary>
    [Fact]
    public void Report_SendsEachKindOncePerLogger_WithItsNameKeyAndTab()
    {
        var log = new RecordingAppLogger();

        Assert.True(HandledEventReporter.Report(HandledEvent.SaveTornTransfer, log,
            new Dictionary<string, string> { ["autoSave"] = "True" }));
        Assert.False(HandledEventReporter.Report(HandledEvent.SaveTornTransfer, log));
        Assert.True(HandledEventReporter.Report(HandledEvent.BackupCopyStalled, log));
        Assert.True(HandledEventReporter.Report(HandledEvent.SaveTornTransfer, new RecordingAppLogger()));

        Assert.Equal(2, log.Handled.Count);
        var report = log.Handled[0];
        var exception = Assert.IsType<HandledEventException>(report.Exception);
        Assert.Equal(HandledEventException.MessageFor(HandledEvent.SaveTornTransfer), exception.Message);
        Assert.NotNull(exception.StackTrace);
        Assert.Equal(exception.Message, report.Context);
        Assert.Equal("handled-event:SaveTornTransfer", report.GroupingKey);
        Assert.Equal("event", report.MetadataTab);
        Assert.Equal("True", report.Metadata!["autoSave"]);
    }

    /// <summary>A logger that fails can't make reporting throw; the report is simply not sent.</summary>
    [Fact]
    public void Report_WhenTheLoggerThrows_DoesNotThrow()
    {
        var log = new RecordingAppLogger { ThrowFromReportHandled = true };

        Assert.False(HandledEventReporter.Report(HandledEvent.LockLostTakenOver, log));
    }

    /// <summary>Every kind has its own fixed message, none of them blank or the fallback, and none with a path.</summary>
    [Fact]
    public void EveryKind_HasItsOwnFixedMessage()
    {
        var messages = Enum.GetValues<HandledEvent>().Select(HandledEventException.MessageFor).ToList();

        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m) || m.StartsWith("Handled event:")));
        Assert.All(messages, m => Assert.DoesNotContain(@":\", m));
        Assert.Equal(messages.Count, messages.Distinct().Count());
    }

    // ── The report sites ────────────────────────────────────────────────────────

    /// <summary>A save refused because the database changed elsewhere is reported once, however often it repeats.</summary>
    [Fact]
    public async Task ASaveRefusedBecauseTheDatabaseChanged_IsReportedOnce()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        CreateDb(DbPath, "a colleague's change");   // D changes behind the session

        Assert.Equal(SaveOutcome.SourceModified, await svc.SaveAsync(isAutoSave: true));
        Assert.Equal(SaveOutcome.SourceModified, await svc.SaveAsync());

        var report = Assert.Single(ReportsOf(log, HandledEvent.SaveRefusedDatabaseChanged));
        Assert.Equal("True", report.Metadata!["autoSave"]);
        Assert.Equal("False", report.Metadata["finalCheck"]);
    }

    /// <summary>A save whose copy to the location stalls is reported once as "location not responding".</summary>
    [Fact]
    public async Task ASaveThatTimesOut_IsReportedOnceAsLocationNotResponding()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        NetworkFaults.Decide = r => r.Label == "CopyFile" && r.DestinationPath == DbPath + ".tmp"
            ? NetworkFaultDecision.TimeOut
            : NetworkFaultDecision.None;

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        var report = Assert.Single(ReportsOf(log, HandledEvent.SaveLocationNotResponding));
        Assert.Equal("False", report.Metadata!["autoSave"]);
    }

    /// <summary>A save whose copy at the location doesn't match the snapshot (a torn transfer) is reported.</summary>
    [Fact]
    public async Task ATornTransfer_IsReported()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        svc.BeforeTmpVerifyAsync = tmp => File.WriteAllBytesAsync(tmp, new byte[] { 1, 2, 3 });

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        Assert.Single(ReportsOf(log, HandledEvent.SaveTornTransfer));
    }

    /// <summary>
    /// Kept changes whose database was changed in the meantime: the refused restore is reported as a recovery conflict
    /// (source changed).
    /// </summary>
    [Fact]
    public async Task ARestoreRefusedBecauseTheDatabaseChanged_IsReported()
    {
        var first = await CheckoutWithEditAsync(new RecordingAppLogger());
        await first.ReleaseAsync(saveFirst: false);      // the session ends, its kept changes and marker stay
        _lastLockService!.PendingRelease?.Wait(TimeSpan.FromSeconds(10));   // the lock file goes in the background
        CreateDb(DbPath, "a colleague's change");

        var log = new RecordingAppLogger();
        Assert.Equal(CheckoutOutcome.RecoveryConflict, await NewService(log).CheckoutAsync(DbPath, recoverWorkingCopy: true));

        Assert.Single(ReportsOf(log, HandledEvent.RecoveryConflictSourceChanged));
    }

    /// <summary>A lock taken over by another session is reported, with the holder as a relation, never a name.</summary>
    [Fact]
    public async Task ALockTakenOver_IsReported_WithTheHolderAsARelation()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        var now = DateTime.UtcNow;
        File.WriteAllText(Path.ChangeExtension(DbPath, ".lock"), JsonSerializer.Serialize(new LockFileData
        {
            Username = "zqxtakeover", Machine = "ZQX-TAKEOVER-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = now, Heartbeat = now,
        }));

        Assert.Equal(SaveOutcome.LockLost, await svc.SaveAsync());

        var report = Assert.Single(ReportsOf(log, HandledEvent.LockLostTakenOver));
        Assert.Equal(WriteLockService.HolderOther, report.Metadata!["newHolder"]);
    }
}

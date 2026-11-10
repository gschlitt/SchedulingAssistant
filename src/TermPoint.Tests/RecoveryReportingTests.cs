using System.Text;
using Bugsnag;
using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec 1.2.4 item 7.2: a save (or a reader's Refresh) that works after failing is reported once as an informational
/// "recovered" event. Covers <see cref="HandledEventReporter"/>'s <c>maxPerRun</c>/<c>informational</c>, the
/// BugSnag severity in <see cref="FileAppLogger"/>, the save hook in <see cref="CheckoutService"/> (driven through real
/// saves and the <see cref="NetworkFaults"/> seam) and the refresh hook
/// (<see cref="MainWindowViewModel.TrackRefreshOutcome"/>). Each test makes its own logger, which also gives it its
/// own "already reported" memory.
/// </summary>
public sealed class RecoveryReportingTests : IDisposable
{
    private readonly string _shareDir;
    private readonly string _workingDir;
    private WriteLockService? _lockService;

    /// <summary>The Win32 ERROR_SHARING_VIOLATION as an HRESULT.</summary>
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    /// <summary>Creates a folder playing the share and a local working folder.</summary>
    public RecoveryReportingTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"rrt_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"rrt_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Resets the fault injector and deletes both folders (best effort).</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        _lockService?.PendingRelease?.Wait(TimeSpan.FromSeconds(10));
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    private string DbPath => Path.Combine(_shareDir, "dept.db");

    /// <summary>Creates (or adds a row to) a small real SQLite database.</summary>
    private static void CreateDb(string path, string value = "start")
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Checks out the database for writing and makes one unsaved edit.</summary>
    private async Task<CheckoutService> CheckoutWithEditAsync(IAppLogger logger)
    {
        CreateDb(DbPath);
        _lockService = new WriteLockService();
        var svc = new CheckoutService(lockService: _lockService, logger: logger, dispatch: a => a(), workingDir: _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(DbPath));
        Edit(svc, "edit");
        return svc;
    }

    /// <summary>Makes one more unsaved edit in the working copy.</summary>
    private static void Edit(CheckoutService svc, string value)
    {
        CreateDb(svc.WorkingPath, value);
        svc.MarkDirty();
    }

    /// <summary>The reports of one kind a logger received.</summary>
    private static List<HandledReport> ReportsOf(RecordingAppLogger log, HandledEvent kind)
        => log.Handled.Where(r => r.Exception is HandledEventException e && e.Kind == kind).ToList();

    /// <summary>The metadata of every breadcrumb with this message, in order.</summary>
    private static List<Dictionary<string, string>> BreadcrumbsNamed(RecordingAppLogger log, string message)
    {
        lock (log.Breadcrumbs)
            return log.Breadcrumbs.Where(b => b.Message == message).Select(b => b.Metadata ?? new()).ToList();
    }

    /// <summary>Makes the copy to the share's temporary file time out (the share "does not respond").</summary>
    private void StallCopy()
        => NetworkFaults.Decide = r => r.Label == "CopyFile" && r.DestinationPath is { } d && ShareTempFile.IsLeftoverOf(DbPath, d)
            ? NetworkFaultDecision.TimeOut
            : NetworkFaultDecision.None;

    /// <summary>Makes the rename onto D fail the way an antivirus-held file does.</summary>
    private void RenameInUse()
        => NetworkFaults.Decide = r => r.Label == "File.Move" && r.DestinationPath == DbPath
            ? NetworkFaultDecision.Throwing(new IOException("simulated: in use", SharingViolationHResult))
            : NetworkFaultDecision.None;

    /// <summary>Asserts that no value of <paramref name="metadata"/> contains a path separator.</summary>
    private static void AssertNoPaths(IEnumerable<KeyValuePair<string, string>> metadata)
        => Assert.All(metadata, kv =>
        {
            Assert.DoesNotContain('\\', kv.Value);
            Assert.DoesNotContain('/', kv.Value);
        });

    // ── HandledEventReporter ────────────────────────────────────────────────────

    /// <summary>With <c>maxPerRun = 3</c> the reporter sends three and refuses the fourth.</summary>
    [Fact]
    public void Reporter_MaxPerRun3_SendsThreeThenRefuses()
    {
        var log = new RecordingAppLogger();

        Assert.True(HandledEventReporter.Report(HandledEvent.SaveRecovered, log, maxPerRun: 3));
        Assert.True(HandledEventReporter.Report(HandledEvent.SaveRecovered, log, maxPerRun: 3));
        Assert.True(HandledEventReporter.Report(HandledEvent.SaveRecovered, log, maxPerRun: 3));
        Assert.False(HandledEventReporter.Report(HandledEvent.SaveRecovered, log, maxPerRun: 3));

        Assert.Equal(3, ReportsOf(log, HandledEvent.SaveRecovered).Count);
    }

    /// <summary>The default is still once per run per kind.</summary>
    [Fact]
    public void Reporter_Default_StillOncePerRun()
    {
        var log = new RecordingAppLogger();

        Assert.True(HandledEventReporter.Report(HandledEvent.SaveRenameBlocked, log));
        Assert.False(HandledEventReporter.Report(HandledEvent.SaveRenameBlocked, log));

        var report = Assert.Single(log.Handled);
        Assert.False(report.Informational);
    }

    /// <summary>The <c>informational</c> flag reaches the logger.</summary>
    [Fact]
    public void Reporter_Informational_ReachesTheLogger()
    {
        var log = new RecordingAppLogger();

        HandledEventReporter.Report(HandledEvent.RefreshRecovered, log, informational: true);

        Assert.True(Assert.Single(log.Handled).Informational);
        Assert.Equal("Refresh recovered after failing", Assert.Single(log.Handled).Context);
    }

    /// <summary>The two new kinds have their fixed messages.</summary>
    [Fact]
    public void NewKinds_HaveFixedMessages()
    {
        Assert.Equal("Save recovered after failing", HandledEventException.MessageFor(HandledEvent.SaveRecovered));
        Assert.Equal("Refresh recovered after failing", HandledEventException.MessageFor(HandledEvent.RefreshRecovered));
    }

    // ── FileAppLogger severity ──────────────────────────────────────────────────

    /// <summary>A BugSnag delivery that keeps each payload's JSON instead of sending it anywhere.</summary>
    private sealed class CapturingDelivery : IDelivery
    {
        /// <summary>The JSON of every payload "sent", in order.</summary>
        public List<string> Payloads { get; } = new();

        /// <inheritdoc/>
        public void Send(IPayload payload) => Payloads.Add(Encoding.UTF8.GetString(payload.Serialize()));
    }

    /// <summary>Sends one handled report through a real BugSnag client and returns the captured payload.</summary>
    /// <param name="informational">The flag under test.</param>
    private static string SendThroughBugsnag(bool informational)
    {
        var delivery = new CapturingDelivery();
        var config = new Configuration("0123456789abcdef0123456789abcdef")
        {
            ReleaseStage = "production", NotifyReleaseStages = new[] { "production" },
            AppVersion = "1.2.4", AutoCaptureSessions = false,
        };
        var logger = new FileAppLogger { BugsnagClient = new Bugsnag.Client(config, delivery) };
        logger.ReportHandled(new HandledEventException(HandledEvent.SaveRecovered), "ctx", informational: informational);
        return Assert.Single(delivery.Payloads);
    }

    /// <summary>An informational report goes to BugSnag with severity info; an ordinary one stays warning.</summary>
    [Fact]
    public void FileAppLogger_Informational_SendsSeverityInfo()
    {
        var ev = System.Text.Json.JsonDocument.Parse(SendThroughBugsnag(true)).RootElement.GetProperty("events")[0];
        Assert.Equal("info", ev.GetProperty("severity").GetString());

        var normal = System.Text.Json.JsonDocument.Parse(SendThroughBugsnag(false)).RootElement.GetProperty("events")[0];
        Assert.Equal("warning", normal.GetProperty("severity").GetString());
    }

    // ── Save ────────────────────────────────────────────────────────────────────

    /// <summary>Two failed saves then a good one: one informational <c>SaveRecovered</c>, with the right numbers.</summary>
    [Fact]
    public async Task TwoFailedSaves_ThenSuccess_ReportsOneRecovery()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        StallCopy();
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync(isAutoSave: true));
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync(isAutoSave: true));
        Assert.Empty(ReportsOf(log, HandledEvent.SaveRecovered));
        NetworkFaults.Decide = null;

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync(isAutoSave: true));

        var report = Assert.Single(ReportsOf(log, HandledEvent.SaveRecovered));
        Assert.True(report.Informational);
        var m = report.Metadata!;
        Assert.Equal(new[] { "autoSave", "failedAttempts", "failedSeconds", "firstKind", "lastKind", "spellInRun" },
            m.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("2", m["failedAttempts"]);
        Assert.True(long.TryParse(m["failedSeconds"], out var secs) && secs >= 0);
        Assert.Equal("NotResponding", m["firstKind"]);
        Assert.Equal("NotResponding", m["lastKind"]);
        Assert.Equal("True", m["autoSave"]);
        Assert.Equal("1", m["spellInRun"]);
        AssertNoPaths(m);
        Assert.Single(BreadcrumbsNamed(log, "Save: Recovered"));
    }

    /// <summary>A not-responding failure followed by a file-in-use one reports first and last kinds correctly.</summary>
    [Fact]
    public async Task MixedKinds_ReportFirstAndLast()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        StallCopy();
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        RenameInUse();
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        NetworkFaults.Decide = null;

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());

        var m = Assert.Single(ReportsOf(log, HandledEvent.SaveRecovered)).Metadata!;
        Assert.Equal("2", m["failedAttempts"]);
        Assert.Equal("NotResponding", m["firstKind"]);
        Assert.Equal("InUse", m["lastKind"]);
        Assert.Equal("False", m["autoSave"]);
    }

    /// <summary>The "a save is already in progress" refusal is not a failure and is not counted.</summary>
    [Fact]
    public async Task TheInProgressRefusal_IsNotCounted()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        StallCopy();
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        NetworkFaults.Decide = null;

        var started = 0;
        SaveOutcome? refused = null;
        svc.SaveStarted += () =>
        {
            if (++started == 1)
                refused = svc.SaveAsync().GetAwaiter().GetResult();   // reentrant call: refused before it starts
        };
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());

        Assert.Equal(SaveOutcome.CopyError, refused);
        Assert.Equal("1", Assert.Single(ReportsOf(log, HandledEvent.SaveRecovered)).Metadata!["failedAttempts"]);
    }

    /// <summary>A save that works with no failure before it sends nothing.</summary>
    [Fact]
    public async Task SuccessWithNoFailure_SendsNothing()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());

        Assert.Empty(ReportsOf(log, HandledEvent.SaveRecovered));
        Assert.Empty(BreadcrumbsNamed(log, "Save: Recovered"));
    }

    /// <summary>Four spells give four breadcrumbs but only three events, numbered by spell.</summary>
    [Fact]
    public async Task FourSpells_GiveThreeEvents_AndFourBreadcrumbs()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        for (var i = 1; i <= 4; i++)
        {
            StallCopy();
            Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
            NetworkFaults.Decide = null;
            Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
            Edit(svc, $"edit-{i}");
        }

        var events = ReportsOf(log, HandledEvent.SaveRecovered);
        Assert.Equal(new[] { "1", "2", "3" }, events.Select(e => e.Metadata!["spellInRun"]));
        var crumbs = BreadcrumbsNamed(log, "Save: Recovered");
        Assert.Equal(new[] { "1", "2", "3", "4" }, crumbs.Select(c => c["spellInRun"]));
    }

    /// <summary>A lock lost in the middle of a spell sends no recovery; it leaves the "ended without recovery" breadcrumb.</summary>
    [Fact]
    public async Task LockLostMidSpell_SendsNoRecovery_ButLeavesABreadcrumb()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        StallCopy();
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        NetworkFaults.Decide = null;

        File.WriteAllText(Path.ChangeExtension(DbPath, ".lock"), System.Text.Json.JsonSerializer.Serialize(new LockFileData
        {
            Username = "other", Machine = "OTHER-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = DateTime.UtcNow, Heartbeat = DateTime.UtcNow,
        }));
        Assert.Equal(SaveOutcome.LockLost, await svc.SaveAsync());

        Assert.Empty(ReportsOf(log, HandledEvent.SaveRecovered));
        var crumb = Assert.Single(BreadcrumbsNamed(log, "Save: FailuresEndedWithoutRecovery"));
        Assert.Equal("1", crumb["failedAttempts"]);
        Assert.Equal("LockLost", crumb["reason"]);
        Assert.Contains(log.Infos, i => i.Contains("ended without a recovery"));
    }

    /// <summary>
    /// The crash-protection marker problem is reported from places that are not failed saves, so it must not set the
    /// current save's failure kind.
    /// </summary>
    [Fact]
    public async Task AMarkerFailure_SetsNoSaveFailureKind()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        var markerPath = svc.WorkingPath + ".dirty";
        File.Delete(markerPath);
        Directory.CreateDirectory(markerPath);   // the marker can no longer be written
        try { svc.MarkDirty(); }
        finally { Directory.Delete(markerPath); }

        var field = typeof(CheckoutService).GetField("_saveFailureKind",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Null(field.GetValue(svc));
    }

    /// <summary>A failing logger cannot make a recovering save throw or fail.</summary>
    [Fact]
    public async Task ARecovery_WithAFailingReporter_StillSucceeds()
    {
        var log = new RecordingAppLogger { ThrowFromReportHandled = true };
        var svc = await CheckoutWithEditAsync(log);
        StallCopy();
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        NetworkFaults.Decide = null;

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
    }

    // ── Refresh ─────────────────────────────────────────────────────────────────

    /// <summary>Fail, fail, succeed: one <c>RefreshRecovered</c> with two failed attempts and the outcome names as kinds.</summary>
    [Fact]
    public void Refresh_FailFailSucceed_ReportsOneRecovery()
    {
        var log = new RecordingAppLogger();
        var tracker = new FailureStreakTracker();

        MainWindowViewModel.TrackRefreshOutcome(tracker, log, "line", threw: false, RefreshOutcome.SourceUnavailable, 100);
        MainWindowViewModel.TrackRefreshOutcome(tracker, log, "line", threw: true, null, 200);
        Assert.Empty(log.Handled);
        MainWindowViewModel.TrackRefreshOutcome(tracker, log, null, threw: false, RefreshOutcome.Updated, 300);

        var report = Assert.Single(ReportsOf(log, HandledEvent.RefreshRecovered));
        Assert.True(report.Informational);
        var m = report.Metadata!;
        Assert.Equal(new[] { "failedAttempts", "failedSeconds", "firstKind", "lastKind", "spellInRun" },
            m.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("2", m["failedAttempts"]);
        Assert.Equal("SourceUnavailable", m["firstKind"]);
        Assert.Equal("Threw", m["lastKind"]);
        Assert.Equal("1", m["spellInRun"]);
        Assert.Single(BreadcrumbsNamed(log, "Refresh: Recovered"));
    }

    /// <summary>A refresh that works with no failure before it sends nothing.</summary>
    [Fact]
    public void Refresh_SuccessWithNoFailure_SendsNothing()
    {
        var log = new RecordingAppLogger();

        MainWindowViewModel.TrackRefreshOutcome(new FailureStreakTracker(), log, null, threw: false, RefreshOutcome.Updated, 1);

        Assert.Empty(log.Handled);
        Assert.Empty(BreadcrumbsNamed(log, "Refresh: Recovered"));
    }
}

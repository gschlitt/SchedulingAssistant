using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Field reporting (1.2.4 items 7.1 and 7.3): how long each save took (the <c>Save: Success</c> breadcrumb), which wait ran
/// out when a save gave up (<see cref="HandledEvent.SaveLocationNotResponding"/> and the <c>Save: LocationNotResponding</c>
/// breadcrumb), and a rename refused by another program (<see cref="HandledEvent.SaveRenameBlocked"/>). Real saves are
/// driven through <see cref="CheckoutService"/> with a <see cref="RecordingAppLogger"/> (nothing reaches BugSnag) and the
/// <see cref="NetworkFaults"/> seam. Each test makes its own logger, which also gives it its own "already reported" memory.
/// </summary>
public sealed class SaveFieldReportingTests : IDisposable
{
    private readonly string _shareDir;
    private readonly string _workingDir;

    /// <summary>The Win32 ERROR_SHARING_VIOLATION as an HRESULT: the "another program has the file open" error.</summary>
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    /// <summary>Creates a folder playing the share and a local working folder.</summary>
    public SaveFieldReportingTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"sfr_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"sfr_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Resets the fault injector and deletes both folders (best effort).</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        _lockService?.PendingRelease?.Wait(TimeSpan.FromSeconds(10));   // no background delete may outlive the test
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    private string DbPath => Path.Combine(_shareDir, "dept.db");

    private WriteLockService? _lockService;

    /// <summary>Creates (or adds a row to) a small real SQLite database.</summary>
    /// <param name="path">The database file.</param>
    /// <param name="value">The value of the row to insert.</param>
    private static void CreateDb(string path, string value = "start")
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Checks out the database for writing and makes one unsaved edit (marker written).</summary>
    /// <param name="logger">The logger the service reports to.</param>
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
    /// <param name="svc">The service whose working copy is edited.</param>
    /// <param name="value">The value of the new row.</param>
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

    /// <summary>Makes the fault injector time out the one wait that <paramref name="stall"/> names.</summary>
    /// <param name="stall">One of LockCheck, SourceStat, SourceHash, CopyToLocation, VerifyAtLocation, Rename.</param>
    private void TimeOutAt(string stall)
    {
        var lockPath = Path.ChangeExtension(DbPath, ".lock");
        NetworkFaults.Decide = r =>
        {
            var hit = stall switch
            {
                "LockCheck"        => r.Label == "File.Exists" && r.Path == lockPath,
                "SourceStat"       => r.Label == "File.Stat" && r.Path == DbPath,
                "SourceHash"       => r.Label == "ComputeHash" && r.Path == DbPath,
                "CopyToLocation"   => r.Label == "CopyFile" && r.DestinationPath is { } d && ShareTempFile.IsLeftoverOf(DbPath, d),
                "VerifyAtLocation" => r.Label == "ComputeHash" && r.Path is { } p && ShareTempFile.IsLeftoverOf(DbPath, p),
                "Rename"           => r.Label == "File.Move" && r.DestinationPath == DbPath,
                _                  => false,
            };
            return hit ? NetworkFaultDecision.TimeOut : NetworkFaultDecision.None;
        };
    }

    /// <summary>Makes the rename onto D fail the way an antivirus-held file does (sharing violation).</summary>
    private void RenameInUse()
        => NetworkFaults.Decide = r => r.Label == "File.Move" && r.DestinationPath == DbPath
            ? NetworkFaultDecision.Throwing(new IOException("simulated: in use", SharingViolationHResult))
            : NetworkFaultDecision.None;

    /// <summary>Asserts that no metadata value of <paramref name="metadata"/> contains a path separator.</summary>
    /// <param name="metadata">The metadata to check.</param>
    private static void AssertNoPaths(IEnumerable<KeyValuePair<string, string>> metadata)
        => Assert.All(metadata, kv =>
        {
            Assert.DoesNotContain('\\', kv.Value);
            Assert.DoesNotContain('/', kv.Value);
        });

    // ── 7.1: timing ─────────────────────────────────────────────────────────────

    /// <summary>A successful save's breadcrumb carries a whole, non-negative <c>elapsedMs</c> and the <c>autoSave</c> flag.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASuccessfulSave_BreadcrumbCarriesElapsedMsAndAutoSave(bool isAutoSave)
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync(isAutoSave: isAutoSave));

        var crumb = Assert.Single(BreadcrumbsNamed(log, "Save: Success"));
        Assert.True(long.TryParse(crumb["elapsedMs"], out var ms) && ms >= 0, $"elapsedMs was '{crumb["elapsedMs"]}'");
        Assert.Equal(isAutoSave.ToString(), crumb["autoSave"]);
        AssertNoPaths(crumb);
    }

    /// <summary>
    /// A save that times out at each of the six waits sends one report with the right <c>stalledAt</c>, the 5000 ms
    /// deadline, a whole <c>elapsedMs</c> and <c>recentSaveMs</c> "none" (no earlier success), plus the breadcrumb.
    /// </summary>
    [Theory]
    [InlineData("LockCheck")]
    [InlineData("SourceStat")]
    [InlineData("SourceHash")]
    [InlineData("CopyToLocation")]
    [InlineData("VerifyAtLocation")]
    [InlineData("Rename")]
    public async Task ASaveThatTimesOut_ReportsWhichWaitRanOut(string stall)
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        TimeOutAt(stall);

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync(isAutoSave: true));

        var report = Assert.Single(ReportsOf(log, HandledEvent.SaveLocationNotResponding));
        var m = report.Metadata!;
        Assert.Equal(new[] { "autoSave", "deadlineMs", "elapsedMs", "recentSaveMs", "stalledAt" }, m.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("True", m["autoSave"]);
        Assert.Equal(stall, m["stalledAt"]);
        Assert.Equal("5000", m["deadlineMs"]);
        Assert.True(long.TryParse(m["elapsedMs"], out var ms) && ms >= 0, $"elapsedMs was '{m["elapsedMs"]}'");
        Assert.Equal("none", m["recentSaveMs"]);
        AssertNoPaths(m);

        var crumb = Assert.Single(BreadcrumbsNamed(log, "Save: LocationNotResponding"));
        Assert.Equal(stall, crumb["stalledAt"]);
        Assert.Equal(m["elapsedMs"], crumb["elapsedMs"]);
    }

    /// <summary>
    /// After earlier successes the report lists their times: at most five, oldest first (six successes, so the very
    /// first one has dropped out).
    /// </summary>
    [Fact]
    public async Task TheReport_ListsTheLastFiveSuccessfulSaves_OldestFirst()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
            Edit(svc, $"edit-{i}");
        }
        var times = BreadcrumbsNamed(log, "Save: Success").Select(c => c["elapsedMs"]).ToList();
        Assert.Equal(6, times.Count);

        TimeOutAt("CopyToLocation");
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        var report = Assert.Single(ReportsOf(log, HandledEvent.SaveLocationNotResponding));
        Assert.Equal(string.Join(",", times.Skip(1)), report.Metadata!["recentSaveMs"]);
    }

    /// <summary>With one earlier success the report lists just that one (no trailing comma).</summary>
    [Fact]
    public async Task TheReport_ListsASingleEarlierSuccess()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        var first = Assert.Single(BreadcrumbsNamed(log, "Save: Success"))["elapsedMs"];
        Edit(svc, "again");

        TimeOutAt("Rename");
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        Assert.Equal(first, Assert.Single(ReportsOf(log, HandledEvent.SaveLocationNotResponding)).Metadata!["recentSaveMs"]);
    }

    /// <summary>A second timeout sends no second event but leaves a second breadcrumb.</summary>
    [Fact]
    public async Task ASecondTimeout_SendsNoSecondEvent_ButLeavesASecondBreadcrumb()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        TimeOutAt("CopyToLocation");

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        Assert.Single(ReportsOf(log, HandledEvent.SaveLocationNotResponding));
        Assert.Equal(2, BreadcrumbsNamed(log, "Save: LocationNotResponding").Count);
    }

    /// <summary>A logger that throws when reporting cannot make the save's timeout path throw.</summary>
    [Fact]
    public async Task ATimeout_WithAFailingReporter_StillEndsAsACopyError()
    {
        var log = new RecordingAppLogger { ThrowFromReportHandled = true };
        var svc = await CheckoutWithEditAsync(log);
        TimeOutAt("Rename");

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());
    }

    // ── 7.3: a rename blocked by another program ────────────────────────────────

    /// <summary>
    /// A rename refused because the file is in use sends one <see cref="HandledEvent.SaveRenameBlocked"/> with only the
    /// listed fields; a second one sends no event, and the breadcrumb count goes to 2.
    /// </summary>
    [Fact]
    public async Task ARenameInUse_SendsOneEvent_AndCountsTheRepeats()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        RenameInUse();

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync(isAutoSave: true));
        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync(isAutoSave: true));

        var report = Assert.Single(ReportsOf(log, HandledEvent.SaveRenameBlocked));
        var m = report.Metadata!;
        Assert.Equal(new[] { "attempts", "autoSave", "exceptionType", "hresult" }, m.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("True", m["autoSave"]);
        Assert.Equal("IOException", m["exceptionType"]);
        Assert.Equal("0x80070020", m["hresult"]);
        Assert.Equal(NetworkFileOps.MoveRetryAttempts.ToString(), m["attempts"]);
        AssertNoPaths(m);

        var crumbs = BreadcrumbsNamed(log, "Save: rename blocked");
        Assert.Equal(new[] { "1", "2" }, crumbs.Select(c => c["count"]));
        Assert.All(crumbs, c => Assert.Equal("0x80070020", c["hresult"]));
        Assert.Empty(ReportsOf(log, HandledEvent.SaveLocationNotResponding));
    }

    /// <summary>An access-denied rename (not an IOException) is reported too, with its own exception type and HRESULT.</summary>
    [Fact]
    public async Task ARenameAccessDenied_IsReportedWithItsExceptionType()
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        NetworkFaults.Decide = r => r.Label == "File.Move" && r.DestinationPath == DbPath
            ? NetworkFaultDecision.Throwing(new UnauthorizedAccessException("simulated: denied"))
            : NetworkFaultDecision.None;

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        var m = Assert.Single(ReportsOf(log, HandledEvent.SaveRenameBlocked)).Metadata!;
        Assert.Equal("False", m["autoSave"]);
        Assert.Equal("UnauthorizedAccessException", m["exceptionType"]);
        Assert.Matches("^0x[0-9A-F]{8}$", m["hresult"]);
    }

    /// <summary>A copy or a verify refused because the file is in use stays local-only: no event, no rename breadcrumb.</summary>
    [Theory]
    [InlineData("CopyFile")]
    [InlineData("ComputeHash")]
    public async Task ACopyOrVerifyInUse_SendsNoRenameBlockedEvent(string label)
    {
        var log = new RecordingAppLogger();
        var svc = await CheckoutWithEditAsync(log);
        NetworkFaults.Decide = r =>
            r.Label == label
            && (r.DestinationPath ?? r.Path) is { } p && ShareTempFile.IsLeftoverOf(DbPath, p)
                ? NetworkFaultDecision.Throwing(new IOException("simulated: in use", SharingViolationHResult))
                : NetworkFaultDecision.None;

        Assert.Equal(SaveOutcome.CopyError, await svc.SaveAsync());

        Assert.Empty(ReportsOf(log, HandledEvent.SaveRenameBlocked));
        Assert.Empty(BreadcrumbsNamed(log, "Save: rename blocked"));
    }

    /// <summary>The new event kind has its fixed message, the one the design approved.</summary>
    [Fact]
    public void SaveRenameBlocked_HasItsFixedMessage()
        => Assert.Equal("Save rename refused: the file was in use or access was denied",
            HandledEventException.MessageFor(HandledEvent.SaveRenameBlocked));
}

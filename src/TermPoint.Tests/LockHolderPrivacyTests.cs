using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 6, step 6c: lock holders never appear by name in what TermPoint sends. A breadcrumb records the holder's
/// relation to this user (<see cref="WriteLockService.DescribeHolder"/>), and every holder name and machine read from a
/// lock file is registered with <see cref="PrivacyScrubber"/> (<see cref="WriteLockService.ParseLockFile"/>), so it is
/// removed from any other text that might mention it. The names used are made up ("zqx…").
/// </summary>
public sealed class LockHolderPrivacyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lhp_{Guid.NewGuid():N}");

    /// <summary>Creates the temporary folder for the end-to-end test.</summary>
    public LockHolderPrivacyTests() => Directory.CreateDirectory(_dir);

    /// <summary>Deletes the temporary folder (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A logger that keeps each breadcrumb (its text and metadata) and nothing else.</summary>
    private sealed class BreadcrumbLogger : IAppLogger
    {
        public List<(string Message, Dictionary<string, string>? Metadata)> Breadcrumbs { get; } = new();
        public bool ThrowOnError { get; set; }
        public event EventHandler<string>? ErrorLogged { add { } remove { } }
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        public void LogWarning(string message, string? context = null) { }
        public void LogInfo(string message, string? context = null) { }
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null)
            => Breadcrumbs.Add((message, metadata));
    }

    /// <summary>A lock service that believes it is user "zqxme" on computer "ZQX-MY-PC", with no timers running.</summary>
    private static WriteLockService LockServiceForZqxMe()
        => new(TimeProvider.System, new LockIdentity("ZQX-MY-PC", "zqxme", 4242, null), startTimers: false);

    // ── The marker ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The five markers: no holder; an unreadable or nameless one; someone else; this user here (letter case doesn't
    /// matter); this user's name on another computer.
    /// </summary>
    [Theory]
    [InlineData(null,         null,         WriteLockService.HolderNone)]
    [InlineData("(corrupted)", "(corrupted)", WriteLockService.HolderUnreadable)]
    [InlineData("",           "ZQX-MY-PC",  WriteLockService.HolderUnreadable)]
    [InlineData("zqxcolleague", "ZQX-LAB-1", WriteLockService.HolderOther)]
    [InlineData("ZQXME",      "zqx-my-pc",  WriteLockService.HolderSelf)]
    [InlineData("zqxme",      "ZQX-LAPTOP", WriteLockService.HolderSelfOtherComputer)]
    public void DescribeHolder_GivesTheRelationToThisUser(string? user, string? machine, string expected)
    {
        var holder = user is null ? null : new LockFileData { Username = user, Machine = machine ?? string.Empty };

        Assert.Equal(expected, LockServiceForZqxMe().DescribeHolder(holder));
    }

    // ── Reading a lock file ─────────────────────────────────────────────────────

    /// <summary>Reading a lock file registers its holder's user name and machine with the scrubber.</summary>
    [Fact]
    public void ParseLockFile_RegistersTheHoldersNames()
    {
        var json = JsonSerializer.Serialize(new LockFileData { Username = "zqxparsed", Machine = "ZQX-PARSED-PC" });

        var holder = WriteLockService.ParseLockFile(json);

        Assert.Equal("zqxparsed", holder!.Username);
        Assert.Equal("held by <user> on <machine>", PrivacyScrubber.ScrubText("held by zqxparsed on zqx-parsed-pc"));
    }

    /// <summary>Reading keeps the old behaviour for bad files: invalid text throws, the JSON literal null gives null.</summary>
    [Fact]
    public void ParseLockFile_BadTextThrows_AndNullGivesNull()
    {
        Assert.ThrowsAny<JsonException>(() => WriteLockService.ParseLockFile("{ not json"));
        Assert.ThrowsAny<JsonException>(() => WriteLockService.ParseLockFile(null));
        Assert.Null(WriteLockService.ParseLockFile("null"));
    }

    // ── End to end ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opening a database a colleague is editing: the checkout's breadcrumb records "other", never the colleague's name,
    /// and the name is now one the scrubber removes.
    /// </summary>
    [Fact]
    public async Task ACheckoutBlockedByAColleague_RecordsOther_NotTheirName()
    {
        var db = Path.Combine(_dir, "dept.db");
        File.WriteAllText(db, "dummy-database-content");
        var now = DateTime.UtcNow;
        File.WriteAllText(Path.ChangeExtension(db, ".lock"), JsonSerializer.Serialize(new LockFileData
        {
            Username = "zqxcolleague", Machine = "ZQX-COLLEAGUE-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = now, Heartbeat = now,
        }));

        var logger = new BreadcrumbLogger();
        var svc = new CheckoutService(lockService: new WriteLockService(), logger: logger, dispatch: a => a(),
                                      workingDir: Path.Combine(_dir, "working"));

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        var crumb = Assert.Single(logger.Breadcrumbs, b => b.Message == "Checkout: ReadOnly");
        Assert.Equal(WriteLockService.HolderOther, crumb.Metadata!["holder"]);
        Assert.DoesNotContain(logger.Breadcrumbs, b => b.Metadata?.Values.Any(v => v.Contains("zqxcolleague")) == true);
        Assert.Equal("<user>", PrivacyScrubber.ScrubText("zqxcolleague"));
    }
}

using TermPoint.Exceptions;
using TermPoint.Services;
using TermPoint.ViewModels.Wizard;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="StartupWizardViewModel.DescribeDbFailureAsync"/>, the wizard's builder of the
/// inline error shown when opening or creating a database fails (spec item 16, step 16b).
///
/// <para>The method used to probe the chosen folder for write access synchronously on the UI thread,
/// which froze the wizard for the SMB timeout when that folder was on a dead share. It now awaits the
/// probe off the UI thread under the <see cref="NetworkFileOps"/> deadline, and a stalled probe maps to
/// the wizard's "cannot reach the database location" message. Almost every test drives the method through
/// its <c>writeProbe</c> test seam, so none waits on the 5 s deadline and none touches a real share; one
/// test per default-probe outcome runs the real probe against a local temp folder, where it completes
/// at once.</para>
/// </summary>
public class WizardDbFailureMessageTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The suffix the wizard appends when the write block carries IT-relevant detail.</summary>
    private const string ItSuffix = "\n\nAsk your IT department for details.";

    /// <summary>A database path inside a (never created) temp folder, so no test depends on disk state.</summary>
    private static string FakeDbPath() =>
        Path.Combine(Path.GetTempPath(), $"wiz_dbfail_{Guid.NewGuid():N}", "term.db");

    /// <summary>
    /// A fake write probe that records every folder it was asked about and answers with a fixed result.
    /// </summary>
    /// <param name="completed">Whether the fake probe reports that it finished within the deadline.</param>
    /// <param name="canCreate">What the fake probe reports about creating a file (when it completed).</param>
    /// <returns>
    /// The probe delegate to pass as <c>writeProbe</c>, and the list that receives the folder of every call
    /// (so a test can assert both "was called with X" and "was not called").
    /// </returns>
    private static (Func<string?, Task<(bool Completed, bool CanCreate)>> Probe, List<string?> Calls)
        FakeProbe(bool completed, bool canCreate)
    {
        var calls = new List<string?>();
        Task<(bool Completed, bool CanCreate)> Probe(string? folder)
        {
            calls.Add(folder);
            return Task.FromResult((completed, canCreate));
        }
        return (Probe, calls);
    }

    // ── DatabaseFolderNotWritableException: its own wording, no probe ─────────

    /// <summary>The open path already classified the failure: use its message and never probe the folder.</summary>
    [Fact]
    public async Task FolderNotWritableException_UsesItsUserMessage_WithoutProbing()
    {
        var (probe, calls) = FakeProbe(completed: true, canCreate: true);
        var ex = new DatabaseFolderNotWritableException(FakeDbPath(), "Pick another folder.", itDetail: null, inner: null);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(ex, FakeDbPath(), "open", probe);

        Assert.Equal("Pick another folder.", message);
        Assert.Empty(calls);
    }

    /// <summary>IT detail is not dumped into the inline field; the wizard points to the IT department instead.</summary>
    [Fact]
    public async Task FolderNotWritableException_WithItDetail_AddsItSuffix_WithoutProbing()
    {
        var (probe, calls) = FakeProbe(completed: false, canCreate: false);
        var ex = new DatabaseFolderNotWritableException(FakeDbPath(), "Pick another folder.", itDetail: "Controlled Folder Access ...", inner: null);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(ex, FakeDbPath(), "open", probe);

        Assert.Equal("Pick another folder." + ItSuffix, message);
        Assert.Empty(calls);
    }

    // ── Probe stalled: the new branch ─────────────────────────────────────────

    /// <summary>
    /// A probe that does not finish means the folder's share is not answering: show the wizard's network
    /// message (not the generic one), and probe exactly once.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("create")]
    public async Task StalledProbe_ReturnsUnreachableMessage_AndProbesOnce(string verb)
    {
        var (probe, calls) = FakeProbe(completed: false, canCreate: false);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(
            new IOException("network path was not found"), FakeDbPath(), verb, probe);

        Assert.Equal(StartupWizardViewModel.UnreachableLocationMessage, message);
        Assert.Equal("Cannot reach the database location — check your network connection and try again.", message);
        Assert.Single(calls);
    }

    // ── Probe completed: write block vs. generic failure ──────────────────────

    /// <summary>
    /// The folder answered but cannot take a new file, and is not a protected known folder: the shared
    /// write-block wording, with no IT suffix.
    /// </summary>
    [Fact]
    public async Task CompletedProbe_CannotCreate_ReturnsWriteBlockWording()
    {
        var dbPath = FakeDbPath();
        var folder = Path.GetDirectoryName(dbPath);
        var (probe, _) = FakeProbe(completed: true, canCreate: false);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(
            new UnauthorizedAccessException("denied"), dbPath, "create", probe);

        var (userMessage, itDetail) = WriteAccessProbe.DescribeWriteBlock(folder);
        Assert.Null(itDetail);   // a temp folder is never a Controlled Folder Access location
        Assert.Equal(userMessage, message);
    }

    /// <summary>
    /// The folder answered but cannot take a new file, and IS a protected known folder (Documents): the
    /// write-block wording plus the "Ask your IT department" suffix.
    /// </summary>
    [Fact]
    public async Task CompletedProbe_CannotCreate_ProtectedFolder_AddsItSuffix()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs)) return;   // some CI images have no Documents folder
        var folder = Path.Combine(docs, "SchedulerData");
        var dbPath = Path.Combine(folder, "term.db");
        var (probe, _) = FakeProbe(completed: true, canCreate: false);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(
            new UnauthorizedAccessException("denied"), dbPath, "create", probe);

        var (userMessage, itDetail) = WriteAccessProbe.DescribeWriteBlock(folder);
        Assert.NotNull(itDetail);
        Assert.Equal(userMessage + ItSuffix, message);
    }

    /// <summary>
    /// The folder is writable, so the failure was something else: the original generic message, naming
    /// the verb and carrying the exception text.
    /// </summary>
    [Theory]
    [InlineData("create")]
    [InlineData("open")]
    public async Task CompletedProbe_CanCreate_ReturnsGenericFallback(string verb)
    {
        var (probe, _) = FakeProbe(completed: true, canCreate: true);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(
            new InvalidOperationException("something odd"), FakeDbPath(), verb, probe);

        Assert.Equal($"Could not {verb} the database: something odd", message);
    }

    // ── What the probe is asked about ─────────────────────────────────────────

    /// <summary>The probe is handed the database's folder — not the file path.</summary>
    [Fact]
    public async Task Probe_ReceivesTheDatabaseFolder()
    {
        var dbPath = FakeDbPath();
        var (probe, calls) = FakeProbe(completed: true, canCreate: true);

        await StartupWizardViewModel.DescribeDbFailureAsync(new IOException("x"), dbPath, "open", probe);

        Assert.Equal(Path.GetDirectoryName(dbPath), Assert.Single(calls));
    }

    /// <summary>A missing or blank path means no folder: the probe gets null, exactly as before this change.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Probe_NullOrBlankDbPath_ReceivesNull(string? dbPath)
    {
        var (probe, calls) = FakeProbe(completed: true, canCreate: true);

        await StartupWizardViewModel.DescribeDbFailureAsync(new IOException("x"), dbPath, "create", probe);

        Assert.Null(Assert.Single(calls));
    }

    // ── Default probe, end to end (real thread-pool probe on a local disk) ────

    /// <summary>
    /// With no seam, the real probe runs through <see cref="NetworkFileOps"/> against a writable local
    /// folder, completes at once, and the failure falls through to the generic message.
    /// </summary>
    [Fact]
    public async Task DefaultProbe_WritableFolder_ReturnsGenericFallback()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wiz_dbfail_ok_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var message = await StartupWizardViewModel.DescribeDbFailureAsync(
                new InvalidOperationException("boom"), Path.Combine(dir, "term.db"), "create");

            Assert.Equal("Could not create the database: boom", message);
            Assert.Empty(Directory.GetFiles(dir));   // the probe cleaned up its test file
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// With no seam, a folder that does not exist cannot take a file: the real probe completes quickly
    /// (a local disk answers at once) and reports a write block, not an unreachable location.
    /// </summary>
    [Fact]
    public async Task DefaultProbe_MissingFolder_ReturnsWriteBlockWording()
    {
        var dbPath = FakeDbPath();   // its folder is never created
        var folder = Path.GetDirectoryName(dbPath);

        var message = await StartupWizardViewModel.DescribeDbFailureAsync(
            new IOException("x"), dbPath, "create");

        Assert.Equal(WriteAccessProbe.DescribeWriteBlock(folder).userMessage, message);
        Assert.NotEqual(StartupWizardViewModel.UnreachableLocationMessage, message);
    }
}

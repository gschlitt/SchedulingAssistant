using System.Text;
using System.Text.Json;
using Bugsnag;
using Bugsnag.Payload;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ScrubbingDelivery"/> (spec item 6, step 6b), the gate every BugSnag payload passes. A real
/// <see cref="Bugsnag.Client"/> is built on the gate, and the gate on a capturing delivery, so the payload BugSnag would
/// receive can be read back and nothing ever reaches the network. The private values pushed through are made up
/// ("zqx…" names, "Jo Smith"), except this computer's own name, which BugSnag adds by itself.
/// </summary>
public sealed class ScrubbingDeliveryTests : IDisposable
{
    private const string InstallId = "install-0123456789abcdef";

    /// <summary>A delivery that keeps each payload (and its JSON) instead of sending it anywhere.</summary>
    private sealed class CapturingDelivery : IDelivery
    {
        /// <summary>The JSON of every payload forwarded, in order.</summary>
        public List<string> Payloads { get; } = new();

        /// <inheritdoc/>
        public void Send(IPayload payload) => Payloads.Add(Encoding.UTF8.GetString(payload.Serialize()));
    }

    /// <summary>A payload of a kind the gate does not know.</summary>
    private sealed class UnknownPayload : IPayload
    {
        public Uri Endpoint => new("https://example.invalid/");
        public KeyValuePair<string, string>[] Headers => Array.Empty<KeyValuePair<string, string>>();
        public byte[] Serialize() => Encoding.UTF8.GetBytes("{\"secret\":\"C:\\\\Users\\\\Jo Smith\"}");
    }

    private readonly CapturingDelivery _sent = new();
    private readonly List<string> _localLog = new();

    /// <summary>Registers the made-up names every test pushes through.</summary>
    public ScrubbingDeliveryTests()
    {
        PrivacyScrubber.RegisterSensitive("zqxholder", PrivacyScrubber.UserLabel);
        PrivacyScrubber.RegisterSensitive("ZQX-PC-42", PrivacyScrubber.MachineLabel);
    }

    /// <summary>Resets the scrubber's test seam.</summary>
    public void Dispose() => PrivacyScrubber.BeforeScrubForTests = null;

    /// <summary>A BugSnag configuration that notifies (a fake API key; nothing is ever sent) and starts no session tracking.</summary>
    private static Configuration NewConfig() => new("0123456789abcdef0123456789abcdef")
    {
        ReleaseStage        = "production",
        NotifyReleaseStages = new[] { "production" },
        AppVersion          = "1.2.3",
        AutoCaptureSessions = false,
        ProjectNamespaces   = new[] { "TermPoint" }
    };

    /// <summary>A client whose every payload goes through the gate into <see cref="_sent"/>.</summary>
    /// <param name="installId">What the gate reads as the install ID.</param>
    private Bugsnag.Client NewClient(Func<string?>? installId = null)
        => new(NewConfig(), new ScrubbingDelivery(_sent, installId ?? (() => InstallId), _localLog.Add));

    /// <summary>Sends one report full of private details: a path in the context, the message, an inner exception's
    /// message, two breadcrumbs and a metadata tab, plus a lock holder's name and machine, and an email address.</summary>
    /// <param name="client">The client to notify through.</param>
    private static void NotifyEverything(Bugsnag.Client client)
    {
        client.Breadcrumbs.Leave("Checkout: WriteAccess", BreadcrumbType.State,
            new Dictionary<string, string> { ["path"] = @"\\fileserver\BIOL Dept\BIOL-TT.db", ["holder"] = "zqxholder" });
        client.Breadcrumbs.Leave(@"Wizard: finish (C:\Users\Jo Smith\Documents\BIOL-TT.db)");

        var error = new IOException(
            @"Access to the path 'C:\Users\Jo Smith\AppData\Roaming\TermPoint\working\1a2b_BIOL-TT.db' is denied.",
            new UnauthorizedAccessException(@"Locked by zqxholder on ZQX-PC-42: H:\Schedules\BIOL-TT.db"));

        client.Notify(error, report =>
        {
            report.Event.Context = @"Backup folder unavailable: H:\Backups\BIOL";
            report.Event.Metadata.Add("save", new Dictionary<string, string>
            {
                ["step"]  = "Rename",
                ["where"] = @"/Users/jsmith/Library/TermPoint/BIOL-TT.db",
                ["who"]   = "mailto:jo.smith@ufv.ca",
            });
        });
    }

    /// <summary>The first event of a payload's JSON.</summary>
    private static JsonElement FirstEvent(string json) => JsonDocument.Parse(json).RootElement.GetProperty("events")[0];

    // ── An error report ─────────────────────────────────────────────────────────

    /// <summary>
    /// None of the private values reaches the payload: no user name, machine name, path piece, file name or email address,
    /// and no host name at all. What BugSnag needs survives: the error class, the context's words, the shape tags, the
    /// metadata keys and plain values, and the install ID as the user.
    /// </summary>
    [Fact]
    public void AReport_LosesEveryPrivateValue_AndKeepsWhatIsNeeded()
    {
        NotifyEverything(NewClient());

        var json = Assert.Single(_sent.Payloads);
        var ev = FirstEvent(json);

        // Gone, anywhere in the payload.
        foreach (var secret in new[] { "Jo Smith", "jsmith", "zqxholder", "ZQX-PC-42", "BIOL-TT", "fileserver",
                                       "BIOL Dept", "jo.smith@ufv.ca", Environment.MachineName })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.False(ev.GetProperty("device").TryGetProperty("hostname", out _), "the host name must be removed");

        // Kept.
        var errors = ev.GetProperty("exceptions");
        Assert.Equal("System.IO.IOException", errors[0].GetProperty("errorClass").GetString());
        Assert.Equal("Access to the path '<drive C: path>' is denied.", errors[0].GetProperty("message").GetString());
        Assert.Contains(errors.EnumerateArray(), e =>
            e.GetProperty("message").GetString() == "Locked by <user> on <machine>: <drive H: path>");
        Assert.Equal("Backup folder unavailable: <drive H: path>", ev.GetProperty("context").GetString());
        Assert.Equal(InstallId, ev.GetProperty("user").GetProperty("id").GetString());

        var tab = ev.GetProperty("metaData").GetProperty("save");
        Assert.Equal("Rename", tab.GetProperty("step").GetString());
        Assert.Equal("<unix path>", tab.GetProperty("where").GetString());
        Assert.Equal("mailto:<email>", tab.GetProperty("who").GetString());

        var crumbs = ev.GetProperty("breadcrumbs").EnumerateArray().ToList();
        var checkout = Assert.Single(crumbs, c => c.GetProperty("name").GetString() == "Checkout: WriteAccess");
        Assert.Equal("<unc path>", checkout.GetProperty("metaData").GetProperty("path").GetString());
        Assert.Equal("<user>", checkout.GetProperty("metaData").GetProperty("holder").GetString());
        // A path runs to the end of its line or quote, so the closing parenthesis goes with it: paths may contain
        // parentheses ("Program Files (x86)", "BIOL-TT (recovered).db"), and stopping at one could leak the rest.
        Assert.Contains(crumbs, c => c.GetProperty("name").GetString() == "Wizard: finish (<drive C: path>");
    }

    /// <summary>
    /// The breadcrumbs in the report are scrubbed copies: the client's own breadcrumb buffer keeps the originals (which
    /// never leave the computer), so scrubbing one report can't disturb another.
    /// </summary>
    [Fact]
    public void Breadcrumbs_AreScrubbedCopies_TheClientsBufferIsUntouched()
    {
        var client = NewClient();
        NotifyEverything(client);

        Assert.Contains(client.Breadcrumbs.Retrieve(), b => b.Name.Contains("Jo Smith"));
    }

    /// <summary>
    /// The install ID is stamped on every report by the gate itself (no BeforeNotify needed); when it can't be read the
    /// report still goes, with no user.
    /// </summary>
    [Fact]
    public void TheInstallId_IsStampedByTheGate_AndAnUnreadableIdLeavesNoUser()
    {
        NewClient().Notify(new InvalidOperationException("plain"));
        Assert.Equal(InstallId, FirstEvent(_sent.Payloads[0]).GetProperty("user").GetProperty("id").GetString());

        NewClient(() => throw new InvalidOperationException("settings unreadable")).Notify(new InvalidOperationException("plain"));
        var user = FirstEvent(_sent.Payloads[1]).GetProperty("user");
        Assert.False(user.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String,
            "no install ID could be read, so the report has no user ID");
    }

    // ── A session batch ─────────────────────────────────────────────────────────

    /// <summary>A session batch reaches BugSnag without the computer's name (BugSnag puts it there by itself).</summary>
    [Fact]
    public void ASessionBatch_LosesTheHostName()
    {
        var batch = new BatchedSessions(NewConfig(), new[] { KeyValuePair.Create("2026-10-26T10:00:00Z", 1L) });
        Assert.Contains("hostname", Encoding.UTF8.GetString(batch.Serialize()));   // so the test means something

        new ScrubbingDelivery(_sent, () => InstallId, _localLog.Add).Send(batch);

        var json = Assert.Single(_sent.Payloads);
        Assert.DoesNotContain("hostname", json);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
    }

    // ── Failing closed ──────────────────────────────────────────────────────────

    /// <summary>When the scrubber fails (here: a pattern "times out"), nothing is forwarded, and one local log line says so.</summary>
    [Fact]
    public void WhenScrubbingFails_NothingIsSent_AndTheLocalLogSaysSo()
    {
        PrivacyScrubber.BeforeScrubForTests = _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException();

        NotifyEverything(NewClient());

        Assert.Empty(_sent.Payloads);
        Assert.Equal(new[] { ScrubbingDelivery.DroppedMessage }, _localLog);
    }

    /// <summary>A payload of a kind the gate doesn't know can't be checked, so it is dropped.</summary>
    [Fact]
    public void AnUnknownKindOfPayload_IsDropped()
    {
        new ScrubbingDelivery(_sent, () => InstallId, _localLog.Add).Send(new UnknownPayload());

        Assert.Empty(_sent.Payloads);
        Assert.Single(_localLog);
    }
}

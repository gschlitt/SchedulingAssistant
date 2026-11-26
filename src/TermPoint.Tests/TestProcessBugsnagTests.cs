using System.Text;
using Bugsnag;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// A test run must never report to BugSnag. <c>App</c>'s static initialiser builds a real BugSnag client in the test
/// process, and that client's process-wide exception hooks outlive the logger swap in
/// <see cref="TestAssemblyGuards.KeepAppLoggerAwayFromRealFilesAndBugsnag"/>; a leaked faulted task in a test once
/// reached BugSnag as a "development" report. These tests check that the client is muted.
/// </summary>
public sealed class TestProcessBugsnagTests
{
    /// <summary>A BugSnag delivery that counts payloads instead of sending them.</summary>
    private sealed class CountingDelivery : IDelivery
    {
        /// <summary>The number of payloads "sent".</summary>
        public int Sent { get; private set; }

        /// <inheritdoc/>
        public void Send(IPayload payload)
        {
            _ = Encoding.UTF8.GetString(payload.Serialize());
            Sent++;
        }
    }

    /// <summary>A client that would notify (a fake API key; nothing leaves the process), wired to <paramref name="delivery"/>.</summary>
    /// <param name="delivery">Where the client's payloads go.</param>
    private static Client NewClient(IDelivery delivery) => new(new Configuration("0123456789abcdef0123456789abcdef")
    {
        ReleaseStage        = "development",
        NotifyReleaseStages = new[] { "production", "development" },
        AutoCaptureSessions = false,
    }, delivery);

    /// <summary>
    /// After <see cref="TestAssemblyGuards.MuteBugsnag"/>, a report sends nothing, although the client was built to
    /// notify. The control client shows the same report is sent when the client is not muted.
    /// </summary>
    [Fact]
    public void MuteBugsnag_ClientBuiltToNotify_SendsNothing()
    {
        var control = new CountingDelivery();
        NewClient(control).Notify(new IOException("control"));

        var muted = new CountingDelivery();
        var client = NewClient(muted);
        TestAssemblyGuards.MuteBugsnag(client);
        client.Notify(new IOException("muted"));

        Assert.Equal(1, control.Sent);
        Assert.Equal(0, muted.Sent);
    }

    /// <summary>The real client <c>App</c> built for this test process is muted before any test runs.</summary>
    [Fact]
    public void AppBugsnagClient_InTestProcess_IsMuted()
    {
        var client = TestAssemblyGuards.OriginalBugsnagClient;

        Assert.NotNull(client);
        Assert.False(client.Configuration.AutoNotify);
        Assert.Empty(client.Configuration.NotifyReleaseStages ?? Array.Empty<string>());
    }
}

using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="AppNotificationService"/> and related types.
///
/// <para>Because <see cref="AppNotificationService.NotificationChanged"/> is fired
/// via <c>Dispatcher.UIThread.Post</c>, and the Avalonia UI thread is not available
/// in unit tests, these tests exercise the queue state synchronously via
/// <see cref="AppNotificationService.Enqueue"/> and
/// <see cref="AppNotificationService.Dismiss"/> without relying on the event.</para>
///
/// <para>The <see cref="AppNotificationService.GetUnseenAnnouncements"/> filtering
/// logic is tested separately as a pure function (no DI, no Avalonia).</para>
/// </summary>
public class AppNotificationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a service wired to a stub logger that never fires ErrorLogged.</summary>
    private static AppNotificationService MakeService() => new(new StubLogger());

    private static AppNotification Info(string msg = "hello") => new()
        { Message = msg, Severity = NotificationSeverity.Info, IsDismissable = true };

    private static AppNotification Warning(string msg = "warn") => new()
        { Message = msg, Severity = NotificationSeverity.Warning, IsDismissable = true };

    private static AppNotification Error(string msg = "err") => new()
        { Message = msg, Severity = NotificationSeverity.Error, IsDismissable = true };

    /// <summary>Low-priority notification, as announcements are.</summary>
    private static AppNotification LowPrio(string msg = "announce") => new()
        { Message = msg, Severity = NotificationSeverity.Info, IsDismissable = true, IsLowPriority = true };

    // ═════════════════════════════════════════════════════════════════════════
    // Enqueue / Current
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Enqueue_WhenEmpty_SetsCurrent()
    {
        var svc = MakeService();
        svc.Enqueue(Info("first"));
        Assert.Equal("first", svc.Current?.Message);
    }

    [Fact]
    public void Enqueue_WhenOccupied_DoesNotReplaceCurrent()
    {
        var svc = MakeService();
        svc.Enqueue(Info("first"));
        svc.Enqueue(Info("second"));
        Assert.Equal("first", svc.Current?.Message);
    }

    [Fact]
    public void HasNotification_TrueAfterEnqueue()
    {
        var svc = MakeService();
        svc.Enqueue(Info());
        Assert.True(svc.HasNotification);
    }

    [Fact]
    public void HasNotification_FalseOnFreshService()
    {
        var svc = MakeService();
        Assert.False(svc.HasNotification);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Dismiss
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dismiss_WithNoQueue_ClearsCurrent()
    {
        var svc = MakeService();
        svc.Enqueue(Info());
        svc.Dismiss();
        Assert.Null(svc.Current);
        Assert.False(svc.HasNotification);
    }

    [Fact]
    public void Dismiss_AdvancesToNextInQueue()
    {
        var svc = MakeService();
        svc.Enqueue(Info("first"));
        svc.Enqueue(Warning("second"));
        svc.Enqueue(Error("third"));

        svc.Dismiss();
        Assert.Equal("second", svc.Current?.Message);

        svc.Dismiss();
        Assert.Equal("third", svc.Current?.Message);

        svc.Dismiss();
        Assert.Null(svc.Current);
    }

    [Fact]
    public void Dismiss_WhenAlreadyEmpty_IsNoOp()
    {
        var svc = MakeService();
        // Should not throw
        svc.Dismiss();
        Assert.Null(svc.Current);
    }

    [Fact]
    public void Dismiss_PreservesQueueFifoOrder()
    {
        var svc = MakeService();
        var messages = new[] { "a", "b", "c", "d" };
        foreach (var m in messages)
            svc.Enqueue(Info(m));

        foreach (var expected in messages)
        {
            Assert.Equal(expected, svc.Current?.Message);
            svc.Dismiss();
        }

        Assert.Null(svc.Current);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Low-priority (announcement) queue
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LowPriority_WhenQueueEmpty_BecomesCurrent()
    {
        var svc = MakeService();
        svc.Enqueue(LowPrio("announce"));
        Assert.Equal("announce", svc.Current?.Message);
    }

    [Fact]
    public void LowPriority_WhenNormalIsCurrent_IsQueued()
    {
        var svc = MakeService();
        svc.Enqueue(Info("normal"));
        svc.Enqueue(LowPrio("announce"));
        // Normal stays current; announcement waits
        Assert.Equal("normal", svc.Current?.Message);
    }

    [Fact]
    public void LowPriority_SurfacesAfterNormalQueueExhausted()
    {
        var svc = MakeService();
        svc.Enqueue(Info("normal"));
        svc.Enqueue(LowPrio("announce"));

        svc.Dismiss();   // normal dismissed → announcement should surface
        Assert.Equal("announce", svc.Current?.Message);
    }

    [Fact]
    public void NormalArrives_WhileLowPriorityIsCurrent_DemotesLowPriority()
    {
        var svc = MakeService();
        svc.Enqueue(LowPrio("announce"));        // becomes current (nothing else there)
        svc.Enqueue(Warning("backup warning"));  // normal → takes over immediately

        Assert.Equal("backup warning", svc.Current?.Message);
    }

    [Fact]
    public void NormalArrives_WhileLowPriorityIsCurrent_LowPriorityResurfacesAfterNormal()
    {
        var svc = MakeService();
        svc.Enqueue(LowPrio("announce"));
        svc.Enqueue(Warning("backup warning"));

        svc.Dismiss();  // dismiss backup warning → announcement should resurface
        Assert.Equal("announce", svc.Current?.Message);
    }

    [Fact]
    public void MultipleLowPriority_AllSurfaceAfterNormalDrained()
    {
        var svc = MakeService();
        svc.Enqueue(LowPrio("a1"));
        svc.Enqueue(LowPrio("a2"));
        svc.Enqueue(Info("normal"));

        // normal enqueued while a1 is current — a1 gets demoted to low-priority queue
        Assert.Equal("normal", svc.Current?.Message);

        svc.Dismiss();
        Assert.Equal("a1", svc.Current?.Message);  // low-priority FIFO

        svc.Dismiss();
        Assert.Equal("a2", svc.Current?.Message);

        svc.Dismiss();
        Assert.Null(svc.Current);
    }

    [Fact]
    public void NormalAndLowPriority_MixedOrder_NormalAlwaysFirst()
    {
        var svc = MakeService();
        svc.Enqueue(Info("n1"));
        svc.Enqueue(LowPrio("a1"));
        svc.Enqueue(Info("n2"));
        svc.Enqueue(LowPrio("a2"));

        // n1 is current, n2 queued normally, a1 and a2 in low-priority queue
        Assert.Equal("n1", svc.Current?.Message);
        svc.Dismiss();
        Assert.Equal("n2", svc.Current?.Message);
        svc.Dismiss();
        Assert.Equal("a1", svc.Current?.Message);
        svc.Dismiss();
        Assert.Equal("a2", svc.Current?.Message);
        svc.Dismiss();
        Assert.Null(svc.Current);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // AppNotification properties
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Notification_HasLink_TrueWhenBothSet()
    {
        var n = new AppNotification
        {
            Message  = "test",
            LinkText = "Learn more",
            LinkUrl  = "https://example.com"
        };
        Assert.True(n.HasLink);
    }

    [Fact]
    public void Notification_HasLink_FalseWhenLinkTextMissing()
    {
        var n = new AppNotification { Message = "test", LinkUrl = "https://example.com" };
        Assert.False(n.HasLink);
    }

    [Fact]
    public void Notification_HasLink_FalseWhenLinkUrlMissing()
    {
        var n = new AppNotification { Message = "test", LinkText = "Click here" };
        Assert.False(n.HasLink);
    }

    [Theory]
    [InlineData(NotificationSeverity.Info,    true,  false, false)]
    [InlineData(NotificationSeverity.Warning, false, true,  false)]
    [InlineData(NotificationSeverity.Error,   false, false, true)]
    public void Notification_SeverityFlags_MatchSeverity(
        NotificationSeverity severity, bool expectedInfo, bool expectedWarning, bool expectedError)
    {
        var n = new AppNotification { Severity = severity };
        Assert.Equal(expectedInfo,    n.IsInfo);
        Assert.Equal(expectedWarning, n.IsWarning);
        Assert.Equal(expectedError,   n.IsError);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Logger error forwarding
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LoggerError_EnqueuesErrorNotification()
    {
        var logger = new StubLogger();
        var svc    = new AppNotificationService(logger);

        // Simulate a LogError call by raising the event on the stub logger
        logger.RaiseErrorLogged("Something went wrong");

        Assert.NotNull(svc.Current);
        Assert.Equal("Something went wrong", svc.Current!.Message);
        Assert.Equal(NotificationSeverity.Error, svc.Current.Severity);
    }

    [Fact]
    public void LoggerError_SecondError_IsQueued()
    {
        var logger = new StubLogger();
        var svc    = new AppNotificationService(logger);

        logger.RaiseErrorLogged("first error");
        logger.RaiseErrorLogged("second error");

        Assert.Equal("first error", svc.Current?.Message);

        svc.Dismiss();
        Assert.Equal("second error", svc.Current?.Message);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GetUnseenAnnouncements (pure logic)
    // ═════════════════════════════════════════════════════════════════════════

    private static VersionedAnnouncement A(int major, int minor, int patch, string msg = "msg")
        => new(new Version(major, minor, patch), msg);

    [Fact]
    public void GetUnseen_NullLastAck_ReturnsAllUpToCurrent()
    {
        var all = new[] { A(1, 0, 0), A(1, 1, 0), A(2, 0, 0) };
        var result = AppNotificationService.GetUnseenAnnouncements(all, null, new Version(1, 1, 0));
        Assert.Equal(2, result.Count);
        Assert.Equal(new Version(1, 0, 0), result[0].SinceVersion);
        Assert.Equal(new Version(1, 1, 0), result[1].SinceVersion);
    }

    [Fact]
    public void GetUnseen_LastAckAtCurrent_ReturnsEmpty()
    {
        var all = new[] { A(1, 0, 0), A(1, 1, 0) };
        var result = AppNotificationService.GetUnseenAnnouncements(all, new Version(1, 1, 0), new Version(1, 1, 0));
        Assert.Empty(result);
    }

    [Fact]
    public void GetUnseen_LastAckBetweenVersions_ReturnsOnlyNewer()
    {
        var all = new[] { A(1, 0, 0, "v1.0"), A(1, 1, 0, "v1.1"), A(1, 2, 0, "v1.2") };
        var result = AppNotificationService.GetUnseenAnnouncements(all, new Version(1, 1, 0), new Version(1, 2, 0));
        Assert.Single(result);
        Assert.Equal("v1.2", result[0].Message);
    }

    [Fact]
    public void GetUnseen_FutureAnnouncementsExcluded()
    {
        // An announcement for v2.0 should not appear when running v1.0
        var all = new[] { A(1, 0, 0), A(2, 0, 0, "future") };
        var result = AppNotificationService.GetUnseenAnnouncements(all, null, new Version(1, 0, 0));
        Assert.Single(result);
        Assert.Equal(new Version(1, 0, 0), result[0].SinceVersion);
    }

    [Fact]
    public void GetUnseen_EmptyList_ReturnsEmpty()
    {
        var result = AppNotificationService.GetUnseenAnnouncements(
            Array.Empty<VersionedAnnouncement>(), null, new Version(1, 0, 0));
        Assert.Empty(result);
    }

    [Fact]
    public void GetUnseen_ResultsSortedOldestFirst()
    {
        var all = new[] { A(1, 2, 0), A(1, 0, 0), A(1, 1, 0) };
        var result = AppNotificationService.GetUnseenAnnouncements(all, null, new Version(1, 2, 0));
        Assert.Equal(new Version(1, 0, 0), result[0].SinceVersion);
        Assert.Equal(new Version(1, 1, 0), result[1].SinceVersion);
        Assert.Equal(new Version(1, 2, 0), result[2].SinceVersion);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Stub logger
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal <see cref="IAppLogger"/> implementation for testing.
    /// Exposes <see cref="RaiseErrorLogged"/> so tests can simulate a LogError call
    /// without going through file I/O or Avalonia dispatcher logic.
    /// </summary>
    private sealed class StubLogger : IAppLogger
    {
        public bool ThrowOnError { get; set; }
        public event EventHandler<string>? ErrorLogged;

        public void LogError(Exception? ex, string? context = null)
            => ErrorLogged?.Invoke(this, context ?? ex?.Message ?? "error");

        public void LogWarning(string message, string? context = null) { }
        public void LogInfo(string message, string? context = null) { }

        /// <summary>Directly fires the <see cref="ErrorLogged"/> event for test control.</summary>
        public void RaiseErrorLogged(string message) => ErrorLogged?.Invoke(this, message);
    }
}

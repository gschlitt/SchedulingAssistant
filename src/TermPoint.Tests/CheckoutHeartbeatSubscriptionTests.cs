using System.Reflection;
using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Pins that <see cref="CheckoutService"/> listens to <see cref="WriteLockService.HeartbeatFailed"/> and
/// <see cref="WriteLockService.WakeDetected"/> exactly once per write session (spec item 8.12, leftover). A release unhooks only the wake handler,
/// so before the fix a release followed by a fresh checkout on the same service left the heartbeat handler subscribed twice, and one failed
/// heartbeat then started two ownership checks.
/// </summary>
public sealed class CheckoutHeartbeatSubscriptionTests : IDisposable
{
    /// <summary>Folder for the source database and the working copies.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chs_" + Guid.NewGuid().ToString("N"));

    /// <summary>The lock service of the test, so <see cref="Dispose"/> can wait for its background lock-file delete.</summary>
    private readonly WriteLockService _lockService = new();

    /// <summary>Creates the folder.</summary>
    public CheckoutHeartbeatSubscriptionTests() => Directory.CreateDirectory(_dir);

    /// <summary>Waits for the background release, then deletes the folder (best effort).</summary>
    public void Dispose()
    {
        try { _lockService.PendingRelease?.Wait(TimeSpan.FromSeconds(10)); } catch (Exception) { /* not this cleanup's concern */ }
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// How many handlers of the lock service's event <paramref name="eventName"/> belong to <paramref name="owner"/>. An event's delegate is kept in a
    /// field of the same name, which is read here.
    /// </summary>
    /// <param name="eventName">"HeartbeatFailed" or "WakeDetected".</param>
    /// <param name="owner">The object whose handlers are counted.</param>
    private int HandlerCount(string eventName, object owner)
    {
        var field = typeof(WriteLockService).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var current = field!.GetValue(_lockService) as Delegate;
        return current?.GetInvocationList().Count(d => ReferenceEquals(d.Target, owner)) ?? 0;
    }

    /// <summary>After a release and a fresh checkout the service is subscribed to each event once, not twice.</summary>
    [Fact]
    public async Task ReleaseThenCheckout_SubscribesOncePerWriteSession()
    {
        var db = Path.Combine(_dir, "d.db");
        using (var conn = new SqliteConnection($"Data Source={db};Pooling=False")) conn.Open();

        var svc = new CheckoutService(_lockService, new NullAppLogger(), action => action(), Path.Combine(_dir, "work"));

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.Equal(1, HandlerCount("HeartbeatFailed", svc));
        Assert.Equal(1, HandlerCount("WakeDetected", svc));

        await svc.ReleaseAsync(saveFirst: false);
        _lockService.PendingRelease?.Wait(TimeSpan.FromSeconds(10));

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.Equal(1, HandlerCount("HeartbeatFailed", svc));   // was 2 before the fix
        Assert.Equal(1, HandlerCount("WakeDetected", svc));

        await svc.ReleaseAsync(saveFirst: false);
    }

    /// <summary>No-op logger.</summary>
    private sealed class NullAppLogger : IAppLogger
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

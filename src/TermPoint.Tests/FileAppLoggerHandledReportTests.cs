using System.Text;
using System.Text.Json;
using Bugsnag;
using TermPoint.Exceptions;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="FileAppLogger.ReportHandled"/> and <see cref="FileAppLogger.NotifyUser"/> (spec item 5, step
/// 5e): a handled report goes to BugSnag with its context, metadata tab and grouping key, but — unlike
/// <see cref="FileAppLogger.LogError"/> — never re-throws and never raises <see cref="IAppLogger.ErrorLogged"/> (no
/// banner); a user notice raises the banner event and sends nothing. A real <see cref="Bugsnag.Client"/> is used with
/// a delivery that captures the payload instead of sending it, so what BugSnag would receive can be read back.
/// </summary>
public sealed class FileAppLoggerHandledReportTests
{
    /// <summary>A BugSnag delivery that keeps each payload's JSON instead of sending it anywhere.</summary>
    private sealed class CapturingDelivery : IDelivery
    {
        /// <summary>The JSON of every payload "sent", in order.</summary>
        public List<string> Payloads { get; } = new();

        /// <inheritdoc/>
        public void Send(IPayload payload) => Payloads.Add(Encoding.UTF8.GetString(payload.Serialize()));
    }

    /// <summary>
    /// Builds a logger wired to a real BugSnag client whose delivery is a <see cref="CapturingDelivery"/>.
    /// </summary>
    /// <param name="throwOnError">The logger's development-time re-throw switch.</param>
    private static (FileAppLogger Logger, CapturingDelivery Delivery) NewLogger(bool throwOnError = false)
    {
        var delivery = new CapturingDelivery();
        return (new FileAppLogger { BugsnagClient = new Bugsnag.Client(NewConfig(), delivery), ThrowOnError = throwOnError }, delivery);
    }

    /// <summary>A BugSnag configuration that notifies (a fake API key; nothing is ever sent) and starts no session tracking.</summary>
    private static Configuration NewConfig() => new("0123456789abcdef0123456789abcdef")
    {
        ReleaseStage        = "production",
        NotifyReleaseStages = new[] { "production" },
        AppVersion          = "1.2.3",
        AutoCaptureSessions = false,
        ProjectNamespaces   = new[] { "TermPoint" }
    };

    /// <summary>The first event of a captured payload.</summary>
    private static JsonElement FirstEvent(string payloadJson)
        => JsonDocument.Parse(payloadJson).RootElement.GetProperty("events")[0];

    // ── ReportHandled ─────────────────────────────────────────────────────────

    /// <summary>
    /// The report BugSnag receives: a HANDLED event, the named exception class and fixed message, the context, the
    /// grouping key, and the metadata under the requested tab.
    /// </summary>
    [Fact]
    public void ReportHandled_SendsAHandledEventWithContextGroupingKeyAndMetadataTab()
    {
        var (logger, delivery) = NewLogger();
        var ex = new DatabaseDamagedException(DamagedFileKind.SharedDatabase, "Page 2: btree problem");

        logger.ReportHandled(ex, "Database damage: SharedDatabase",
            new Dictionary<string, string> { ["kind"] = "SharedDatabase", ["summary"] = "Page 2: btree problem" },
            metadataTab: "damage", groupingKey: "database-damage:SharedDatabase");

        var payload = Assert.Single(delivery.Payloads);
        var ev = FirstEvent(payload);
        Assert.False(ev.GetProperty("unhandled").GetBoolean());
        Assert.Equal("Database damage: SharedDatabase", ev.GetProperty("context").GetString());
        Assert.Equal("database-damage:SharedDatabase", ev.GetProperty("groupingHash").GetString());
        var error = ev.GetProperty("exceptions")[0];
        Assert.Equal("TermPoint.Exceptions.DatabaseDamagedException", error.GetProperty("errorClass").GetString());
        Assert.Equal("Damaged shared database detected by the integrity check", error.GetProperty("message").GetString());
        var tab = ev.GetProperty("metaData").GetProperty("damage");
        Assert.Equal("SharedDatabase", tab.GetProperty("kind").GetString());
        Assert.Equal("Page 2: btree problem", tab.GetProperty("summary").GetString());
    }

    /// <summary>Without a grouping key or metadata, neither is added to the report.</summary>
    [Fact]
    public void ReportHandled_WithoutGroupingKeyOrMetadata_AddsNeither()
    {
        var (logger, delivery) = NewLogger();

        logger.ReportHandled(new InvalidOperationException("odd"), "Something handled");

        var ev = FirstEvent(Assert.Single(delivery.Payloads));
        Assert.Equal("Something handled", ev.GetProperty("context").GetString());
        Assert.False(ev.TryGetProperty("groupingHash", out _));
        Assert.False(ev.TryGetProperty("metaData", out var meta) && meta.TryGetProperty("details", out _));
    }

    /// <summary>
    /// With <see cref="IAppLogger.ThrowOnError"/> set (development), a handled report is neither re-thrown nor shown
    /// as a banner — unlike <c>LogError</c>, which does both — and is still sent.
    /// </summary>
    [Fact]
    public void ReportHandled_IgnoresThrowOnError_AndRaisesNoBanner()
    {
        var (logger, delivery) = NewLogger(throwOnError: true);
        var banners = new List<string>();
        logger.ErrorLogged += (_, message) => banners.Add(message);

        var thrown = Record.Exception(() =>
            logger.ReportHandled(new DatabaseDamagedException(DamagedFileKind.LocalCopy), "Database damage: LocalCopy"));

        Assert.Null(thrown);
        Assert.Empty(banners);
        Assert.Single(delivery.Payloads);
    }

    /// <summary>Without a BugSnag client (tests, unconfigured builds) the report is just a local log line and nothing throws.</summary>
    [Fact]
    public void ReportHandled_WithNoBugsnagClient_DoesNotThrow()
    {
        var logger = new FileAppLogger { BugsnagClient = null, ThrowOnError = true };
        var banners = new List<string>();
        logger.ErrorLogged += (_, message) => banners.Add(message);

        var thrown = Record.Exception(() =>
            logger.ReportHandled(new DatabaseDamagedException(DamagedFileKind.LocalCopy), "Database damage: LocalCopy"));

        Assert.Null(thrown);
        Assert.Empty(banners);
    }

    /// <summary>A BugSnag client that fails when notified (a queue disposed at shutdown) cannot make the report throw.</summary>
    [Fact]
    public void ReportHandled_WhenBugsnagFails_DoesNotThrow()
    {
        var logger = new FileAppLogger
        {
            BugsnagClient = new Bugsnag.Client(NewConfig(), new ThrowingDelivery()),
            ThrowOnError  = true
        };

        var thrown = Record.Exception(() =>
            logger.ReportHandled(new DatabaseDamagedException(DamagedFileKind.LocalCopy), "Database damage: LocalCopy"));

        Assert.Null(thrown);
    }

    /// <summary>A BugSnag delivery that always fails, as a disposed delivery queue does.</summary>
    private sealed class ThrowingDelivery : IDelivery
    {
        /// <inheritdoc/>
        public void Send(IPayload payload) => throw new ObjectDisposedException("the delivery queue");
    }

    // ── Contrast: LogError still re-throws and shows a banner ─────────────────

    /// <summary>
    /// The behaviour <see cref="FileAppLogger.ReportHandled"/> deliberately does NOT have: with
    /// <see cref="IAppLogger.ThrowOnError"/> set, <c>LogError</c> re-throws the exception it was given.
    /// </summary>
    [Fact]
    public void LogError_StillRethrows_WithThrowOnError()
    {
        var (logger, _) = NewLogger(throwOnError: true);

        Assert.Throws<InvalidOperationException>(() => logger.LogError(new InvalidOperationException("bug"), "ctx"));
    }

    // ── NotifyUser ────────────────────────────────────────────────────────────

    /// <summary>
    /// A user notice raises <see cref="IAppLogger.ErrorLogged"/> with exactly its text (so the banner shows it) and
    /// sends NOTHING to BugSnag; it does not re-throw even with <see cref="IAppLogger.ThrowOnError"/> set.
    /// </summary>
    [Fact]
    public void NotifyUser_RaisesTheBannerEvent_AndSendsNoReport()
    {
        var (logger, delivery) = NewLogger(throwOnError: true);
        var banners = new List<string>();
        logger.ErrorLogged += (_, message) => banners.Add(message);

        var thrown = Record.Exception(() => logger.NotifyUser(BackupService.SnapshotFailedIntegrityNotice));

        Assert.Null(thrown);
        Assert.Equal(new[] { "The latest backup failed its integrity check, so it may not be usable for a restore." }, banners);
        Assert.Empty(delivery.Payloads);
    }

    /// <summary>
    /// End to end with the real banner service: a user notice appears in the notification banner as an error with its
    /// exact text, whereas a handled report shows nothing.
    /// </summary>
    [Fact]
    public void TheBanner_ShowsAUserNotice_ButNotAHandledReport()
    {
        var (logger, _) = NewLogger();
        var banner = new AppNotificationService(logger);

        logger.ReportHandled(new DatabaseDamagedException(DamagedFileKind.LocalCopy), "Database damage: LocalCopy");
        Assert.Null(banner.Current);

        logger.NotifyUser(BackupService.SnapshotFailedIntegrityNotice);
        Assert.Equal(BackupService.SnapshotFailedIntegrityNotice, banner.Current?.Message);
        Assert.Equal(NotificationSeverity.Error, banner.Current?.Severity);
    }

    /// <summary>A subscriber that throws cannot make a user notice throw.</summary>
    [Fact]
    public void NotifyUser_WhenASubscriberThrows_DoesNotThrow()
    {
        var logger = new FileAppLogger();
        logger.ErrorLogged += (_, _) => throw new InvalidOperationException("bad subscriber");

        Assert.Null(Record.Exception(() => logger.NotifyUser("hello")));
    }

    // ── The default implementations (every other logger) ──────────────────────

    /// <summary>A logger that implements nothing new gets a local-only <c>ReportHandled</c> and <c>NotifyUser</c>.</summary>
    [Fact]
    public void TheDefaultImplementations_OnlyWriteLocalLogLines()
    {
        var log = new DefaultsOnlyLogger();
        IAppLogger asInterface = log;

        asInterface.ReportHandled(
            new DatabaseDamagedException(DamagedFileKind.BackupSnapshot), "Database damage: BackupSnapshot",
            new Dictionary<string, string> { ["kind"] = "BackupSnapshot", ["summary"] = "line 1\nline 2" });
        asInterface.NotifyUser("something to tell the user");

        Assert.Equal(2, log.Warnings.Count);
        Assert.Equal("DatabaseDamagedException: Damaged backup snapshot detected by the integrity check [kind=BackupSnapshot; summary=line 1 | line 2]", log.Warnings[0]);
        Assert.Equal("something to tell the user", log.Warnings[1]);
    }

    /// <summary>A logger that overrides only the older members, as every existing logger and test double does.</summary>
    private sealed class DefaultsOnlyLogger : IAppLogger
    {
        /// <summary>Every warning line.</summary>
        public List<string> Warnings { get; } = new();
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { ErrorLogged?.Invoke(this, "x"); }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) => Warnings.Add(message);
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }
}

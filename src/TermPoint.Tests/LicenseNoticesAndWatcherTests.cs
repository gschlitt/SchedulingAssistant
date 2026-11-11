using TermPoint.Licensing;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// 1.2.4: the exact wording of every licence/trial banner (<see cref="LicenseNotices"/>) and the daily check
/// (<see cref="LicenseWatcher"/>), driven through <c>CheckNow</c> with no timers.
/// </summary>
public sealed class LicenseNoticesAndWatcherTests
{
    private const string Url = "https://termpoint.ca/license";
    private const string Tail = "After that, TermPoint opens read-only: you can view your timetable but not edit it.";

    private static readonly DateOnly Today = new(2027, 6, 1);

    // ── Result builders ───────────────────────────────────────────────────────

    private static AppAccessResult Trial(int days) => new()
    { AccessLevel = AccessLevel.FullAccess, Reason = AccessReason.Trial, DaysRemaining = days, ShowPurchasePrompt = true };

    private static AppAccessResult Licensed(DateOnly? expiry) => new()
    {
        AccessLevel = AccessLevel.FullAccess,
        Reason = AccessReason.Licensed,
        ExpiryDate = expiry?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
    };

    private static AppAccessResult Expired() => new()
    { AccessLevel = AccessLevel.ReadOnly, Reason = AccessReason.Expired, ShowPurchasePrompt = true };

    private static AppAccessResult Unlicensed() => new()
    { AccessLevel = AccessLevel.ReadOnly, Reason = AccessReason.Unlicensed, ShowPurchasePrompt = true };

    // ── ForOpen ───────────────────────────────────────────────────────────────

    [Fact]
    public void Open_Trial_PluralAndSingular()
    {
        var n = LicenseNotices.ForOpen(Trial(12), Today)!;
        Assert.Equal("You have 12 days remaining in your trial.", n.Message);
        Assert.Equal("Purchase a license", n.LinkText);
        Assert.Equal(Url, n.LinkUrl);
        Assert.Equal(NotificationSeverity.Info, n.Severity);
        Assert.True(n.IsDismissable);

        Assert.Equal("You have 1 day remaining in your trial.", LicenseNotices.ForOpen(Trial(1), Today)!.Message);
    }

    [Fact]
    public void Open_Expired_And_TrialEnded_AreNotDismissable()
    {
        var expired = LicenseNotices.ForOpen(Expired(), Today)!;
        Assert.Equal("Your license has expired. Editing is disabled.", expired.Message);
        Assert.Equal("Purchase or renew", expired.LinkText);
        Assert.False(expired.IsDismissable);
        Assert.Equal(NotificationSeverity.Warning, expired.Severity);

        var ended = LicenseNotices.ForOpen(Unlicensed(), Today)!;
        Assert.Equal("Your trial has ended. Editing is disabled.", ended.Message);
        Assert.Equal("Purchase a license", ended.LinkText);
        Assert.False(ended.IsDismissable);
        Assert.Equal(NotificationSeverity.Warning, ended.Severity);
    }

    [Fact]
    public void Open_ExpiringLicence_Wording()
    {
        Assert.Null(LicenseNotices.ForOpen(Licensed(Today.AddDays(31)), Today));
        Assert.Null(LicenseNotices.ForOpen(Licensed(null), Today));

        var d30 = LicenseNotices.ForOpen(Licensed(new DateOnly(2027, 7, 1)), Today)!;
        Assert.Equal($"Your license expires on July 1, 2027 (30 days from now). {Tail}", d30.Message);
        Assert.Equal("Purchase or renew", d30.LinkText);
        Assert.Equal(Url, d30.LinkUrl);
        Assert.True(d30.IsDismissable);
        Assert.Equal(NotificationSeverity.Info, d30.Severity);

        var d8 = LicenseNotices.ForOpen(Licensed(Today.AddDays(8)), Today)!;
        Assert.Equal(NotificationSeverity.Info, d8.Severity);

        var d2 = LicenseNotices.ForOpen(Licensed(Today.AddDays(2)), Today)!;
        Assert.Equal($"Your license expires on June 3, 2027 (2 days from now). {Tail}", d2.Message);
        Assert.Equal(NotificationSeverity.Warning, d2.Severity);

        var d1 = LicenseNotices.ForOpen(Licensed(Today.AddDays(1)), Today)!;
        Assert.Equal($"Your license expires tomorrow, June 2, 2027. {Tail}", d1.Message);

        var d0 = LicenseNotices.ForOpen(Licensed(Today), Today)!;
        Assert.Equal($"Your license expires at the end of today. {Tail}", d0.Message);
        Assert.Equal(NotificationSeverity.Warning, d0.Severity);
    }

    [Fact]
    public void Open_ExpiringLicence_FormatIgnoresMachineCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var n = LicenseNotices.ForOpen(Licensed(new DateOnly(2027, 7, 1)), Today)!;
            Assert.StartsWith("Your license expires on July 1, 2027", n.Message);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }

    // ── ForDailyCheck ─────────────────────────────────────────────────────────

    [Fact]
    public void Daily_SessionFullAccess_FreshTrialEnded_KeepWorkingNotice()
    {
        var n = LicenseNotices.ForDailyCheck(Trial(1), Unlicensed(), Today)!;
        Assert.Equal("Your trial has ended. You can keep working until you close TermPoint; next time it opens read-only.", n.Message);
        Assert.Equal("Purchase a license", n.LinkText);
        Assert.False(n.IsDismissable);
        Assert.Equal(NotificationSeverity.Warning, n.Severity);
    }

    [Fact]
    public void Daily_SessionFullAccess_FreshLicenceExpired_KeepWorkingNotice()
    {
        var n = LicenseNotices.ForDailyCheck(Licensed(Today), Expired(), Today)!;
        Assert.Equal("Your license has expired. You can keep working until you close TermPoint; next time it opens read-only.", n.Message);
        Assert.Equal("Purchase or renew", n.LinkText);
        Assert.False(n.IsDismissable);
    }

    [Fact]
    public void Daily_SessionAlreadyReadOnly_NoKeepWorkingNotice()
    {
        Assert.Null(LicenseNotices.ForDailyCheck(Unlicensed(), Unlicensed(), Today));
        Assert.Null(LicenseNotices.ForDailyCheck(Expired(), Expired(), Today));
    }

    [Fact]
    public void Daily_StillFine_GivesCountdownOrExpiringNotice()
    {
        Assert.Equal("You have 5 days remaining in your trial.",
            LicenseNotices.ForDailyCheck(Trial(6), Trial(5), Today)!.Message);
        Assert.Equal($"Your license expires tomorrow, June 2, 2027. {Tail}",
            LicenseNotices.ForDailyCheck(Licensed(Today.AddDays(1)), Licensed(Today.AddDays(1)), Today)!.Message);
        Assert.Null(LicenseNotices.ForDailyCheck(Licensed(null), Licensed(null), Today));
    }

    // ── LicenseWatcher ────────────────────────────────────────────────────────

    /// <summary>A watcher wired to fakes; records posts and evaluation calls.</summary>
    private sealed class Rig
    {
        public readonly TestClock Clock = new(new DateTime(2027, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        public readonly List<AppNotification> Posted = new();
        public int Evaluations;
        public AppAccessResult? Fresh = Trial(5);
        public Exception? Throw;
        public AppAccessResult Session = Trial(6);
        public readonly LicenseWatcher Watcher;

        public Rig()
        {
            Watcher = new LicenseWatcher(
                Clock,
                evaluate: () => { Evaluations++; if (Throw != null) throw Throw; return Fresh; },
                sessionStatus: () => Session,
                post: Posted.Add);
        }
    }

    [Fact]
    public void Watcher_SameDay_DoesNothing()
    {
        var rig = new Rig();
        rig.Clock.UtcNow = rig.Clock.UtcNow.AddHours(10);
        rig.Watcher.CheckNow();
        Assert.Equal(0, rig.Evaluations);
        Assert.Empty(rig.Posted);
    }

    [Fact]
    public void Watcher_NewDay_EvaluatesPostsOnce()
    {
        var rig = new Rig();
        rig.Clock.UtcNow = rig.Clock.UtcNow.AddDays(1);

        rig.Watcher.CheckNow();
        rig.Watcher.CheckNow();

        Assert.Equal(1, rig.Evaluations);
        Assert.Equal("You have 5 days remaining in your trial.", Assert.Single(rig.Posted).Message);
    }

    [Fact]
    public void Watcher_Timeout_PostsNothing_AndRetries()
    {
        var rig = new Rig { Fresh = null };
        rig.Clock.UtcNow = rig.Clock.UtcNow.AddDays(1);

        rig.Watcher.CheckNow();
        Assert.Empty(rig.Posted);

        rig.Fresh = Trial(5);
        rig.Watcher.CheckNow();
        Assert.Equal(2, rig.Evaluations);
        Assert.Single(rig.Posted);
    }

    [Fact]
    public void Watcher_Exception_IsSwallowed_AndRetried()
    {
        var rig = new Rig { Throw = new IOException("boom") };
        rig.Clock.UtcNow = rig.Clock.UtcNow.AddDays(1);

        rig.Watcher.CheckNow();
        Assert.Empty(rig.Posted);

        rig.Throw = null;
        rig.Watcher.CheckNow();
        Assert.Single(rig.Posted);
    }

    [Fact]
    public void Watcher_SessionFullAccess_FreshUnlicensed_PostsKeepWorking_AndNeverChangesSession()
    {
        var rig = new Rig { Fresh = Unlicensed() };
        var sessionBefore = rig.Session;
        rig.Clock.UtcNow = rig.Clock.UtcNow.AddDays(40);

        rig.Watcher.CheckNow();

        var n = Assert.Single(rig.Posted);
        Assert.Equal("Your trial has ended. You can keep working until you close TermPoint; next time it opens read-only.", n.Message);
        Assert.False(n.IsDismissable);
        Assert.Same(sessionBefore, rig.Session);
        Assert.Equal(AccessLevel.FullAccess, rig.Session.AccessLevel);
    }

    [Fact]
    public void Watcher_NothingToSay_StillAdvancesTheDate()
    {
        var rig = new Rig { Fresh = Licensed(null), Session = Licensed(null) };
        rig.Clock.UtcNow = rig.Clock.UtcNow.AddDays(1);

        rig.Watcher.CheckNow();
        rig.Watcher.CheckNow();

        Assert.Equal(1, rig.Evaluations);
        Assert.Empty(rig.Posted);
    }
}

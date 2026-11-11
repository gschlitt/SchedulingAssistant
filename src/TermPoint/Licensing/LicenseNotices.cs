using System.Globalization;
using TermPoint.Services;

namespace TermPoint.Licensing;

/// <summary>
/// Builds the licence and trial banners shown at database open and by the daily check. Pure: the same
/// <see cref="AppAccessResult"/> and date always give the same notice, so every wording string is unit-tested.
/// </summary>
public static class LicenseNotices
{
    /// <summary>Where the "Purchase ..." links go.</summary>
    public const string PurchaseUrl = "https://termpoint.ca/license";

    /// <summary>Link text for a trial (a first purchase).</summary>
    private const string PurchaseLinkText = "Purchase a license";

    /// <summary>Link text for a licence (a renewal, or a first purchase).</summary>
    private const string RenewLinkText = "Purchase or renew";

    /// <summary>How many days before expiry the licence notice starts.</summary>
    private const int ExpiryWarningDays = 30;

    /// <summary>At or under this many days the expiring-licence notice is a warning rather than information.</summary>
    private const int ExpiryUrgentDays = 7;

    /// <summary>Ending shared by the three expiring-licence messages.</summary>
    private const string ReadOnlyConsequence =
        "After that, TermPoint opens read-only: you can view your timetable but not edit it.";

    /// <summary>Date format for the expiring-licence notice (always US English, never the machine's locale).</summary>
    private static readonly CultureInfo DateCulture = new("en-US");

    /// <summary>
    /// The notice shown when a database opens.
    /// </summary>
    /// <param name="status">The access result for this open.</param>
    /// <param name="today">The local calendar date.</param>
    /// <returns>The notice, or null when nothing needs saying (a licence with no expiry, or more than 30 days left).</returns>
    public static AppNotification? ForOpen(AppAccessResult status, DateOnly today)
    {
        switch (status.Reason)
        {
            case AccessReason.Trial when status.DaysRemaining.HasValue:
                return TrialCountdown(status.DaysRemaining.Value);

            case AccessReason.Expired:
                return new AppNotification
                {
                    Message = "Your license has expired. Editing is disabled.",
                    Severity = NotificationSeverity.Warning,
                    IsDismissable = false,
                    LinkText = RenewLinkText,
                    LinkUrl = PurchaseUrl
                };

            case AccessReason.Unlicensed:
                return new AppNotification
                {
                    Message = "Your trial has ended. Editing is disabled.",
                    Severity = NotificationSeverity.Warning,
                    IsDismissable = false,
                    LinkText = PurchaseLinkText,
                    LinkUrl = PurchaseUrl
                };

            case AccessReason.Licensed:
                return ExpiringLicense(status, today);

            default:
                return null;
        }
    }

    /// <summary>
    /// The notice for the daily check of a session that stays open across days.
    /// </summary>
    /// <param name="sessionStatus">The access result this session opened with (never changed by the check).</param>
    /// <param name="fresh">The result of the new evaluation.</param>
    /// <param name="today">The local calendar date.</param>
    /// <returns>
    /// The "you can keep working" notice when the session has full access but <paramref name="fresh"/> is read-only;
    /// otherwise the trial countdown or expiring-licence notice for <paramref name="fresh"/>, if it has one;
    /// otherwise null. The read-only notices of <see cref="ForOpen"/> are never produced here.
    /// </returns>
    public static AppNotification? ForDailyCheck(AppAccessResult sessionStatus, AppAccessResult fresh, DateOnly today)
    {
        if (sessionStatus.AccessLevel == AccessLevel.FullAccess && fresh.AccessLevel == AccessLevel.ReadOnly)
        {
            return fresh.Reason switch
            {
                AccessReason.Unlicensed => new AppNotification
                {
                    Message = "Your trial has ended. You can keep working until you close TermPoint; next time it opens read-only.",
                    Severity = NotificationSeverity.Warning,
                    IsDismissable = false,
                    LinkText = PurchaseLinkText,
                    LinkUrl = PurchaseUrl
                },
                AccessReason.Expired => new AppNotification
                {
                    Message = "Your license has expired. You can keep working until you close TermPoint; next time it opens read-only.",
                    Severity = NotificationSeverity.Warning,
                    IsDismissable = false,
                    LinkText = RenewLinkText,
                    LinkUrl = PurchaseUrl
                },
                _ => null
            };
        }

        // Anything else: only the countdown kinds, never "editing is disabled".
        return fresh.Reason is AccessReason.Trial or AccessReason.Licensed
            ? ForOpen(fresh, today)
            : null;
    }

    /// <summary>
    /// The trial countdown notice.
    /// </summary>
    /// <param name="days">Days remaining.</param>
    /// <returns>The notice, with "1 day" singular.</returns>
    private static AppNotification TrialCountdown(int days) => new()
    {
        Message = days == 1
            ? "You have 1 day remaining in your trial."
            : $"You have {days} days remaining in your trial.",
        Severity = NotificationSeverity.Info,
        LinkText = PurchaseLinkText,
        LinkUrl = PurchaseUrl
    };

    /// <summary>
    /// The expiring-licence notice for a licence with an expiry date 0 to 30 days away.
    /// </summary>
    /// <param name="status">A Licensed access result.</param>
    /// <param name="today">The local calendar date.</param>
    /// <returns>The notice, or null for a permanent licence or one with more than 30 days left.</returns>
    private static AppNotification? ExpiringLicense(AppAccessResult status, DateOnly today)
    {
        if (!status.ExpiryDate.HasValue)
            return null;

        var expiry = DateOnly.FromDateTime(status.ExpiryDate.Value);
        var days = expiry.DayNumber - today.DayNumber;
        if (days < 0 || days > ExpiryWarningDays)
            return null;

        var dateText = expiry.ToString("MMMM d, yyyy", DateCulture);
        var message = days switch
        {
            0 => $"Your license expires at the end of today. {ReadOnlyConsequence}",
            1 => $"Your license expires tomorrow, {dateText}. {ReadOnlyConsequence}",
            _ => $"Your license expires on {dateText} ({days} days from now). {ReadOnlyConsequence}"
        };

        return new AppNotification
        {
            Message = message,
            Severity = days <= ExpiryUrgentDays ? NotificationSeverity.Warning : NotificationSeverity.Info,
            LinkText = RenewLinkText,
            LinkUrl = PurchaseUrl
        };
    }
}

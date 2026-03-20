namespace SchedulingAssistant.Services;

/// <summary>
/// Severity level of an application notification banner.
/// Controls which colour scheme is applied to the banner.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Informational — blue banner. Used for feature announcements.</summary>
    Info,

    /// <summary>Warning — amber banner. Used for non-critical problems the user should address.</summary>
    Warning,

    /// <summary>Error — red banner. Used for failures that need user attention.</summary>
    Error
}

/// <summary>
/// A single notification to display in the main-window banner.
/// Notifications are queued by <see cref="AppNotificationService"/> and shown one at a time.
/// </summary>
public class AppNotification
{
    /// <summary>The main message text shown in the banner.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Severity level — drives the banner colour scheme.</summary>
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

    /// <summary>
    /// When true the user can dismiss the banner manually.
    /// When false it persists until replaced by a new notification.
    /// Defaults to true.
    /// </summary>
    public bool IsDismissable { get; init; } = true;

    /// <summary>
    /// When set, the banner is automatically dismissed after this duration.
    /// Null means the banner persists until dismissed manually.
    /// </summary>
    public TimeSpan? AutoDismissAfter { get; init; }

    /// <summary>Display label for the optional "for more information" hyperlink.</summary>
    public string? LinkText { get; init; }

    /// <summary>URL opened when the user clicks the hyperlink. Null when no link is needed.</summary>
    public string? LinkUrl { get; init; }

    /// <summary>
    /// When true, this notification is treated as low-priority and will only surface
    /// after all normal-priority notifications have been dismissed.
    /// Used for versioned feature announcements, which should never block operational
    /// notices such as backup warnings or logged errors.
    /// Defaults to false (normal priority).
    /// </summary>
    public bool IsLowPriority { get; init; } = false;

    /// <summary>True when both <see cref="LinkText"/> and <see cref="LinkUrl"/> are non-null.</summary>
    public bool HasLink => LinkText is not null && LinkUrl is not null;

    /// <summary>
    /// CSS-style class name used by the AXAML <c>Styles</c> block in <c>MainWindow.axaml</c>
    /// to apply the appropriate colour scheme to the banner border.
    /// Values: <c>"notification-info"</c>, <c>"notification-warning"</c>, <c>"notification-error"</c>.
    /// </summary>
    /// <summary>True when <see cref="Severity"/> is <see cref="NotificationSeverity.Info"/>.</summary>
    public bool IsInfo    => Severity == NotificationSeverity.Info;
    /// <summary>True when <see cref="Severity"/> is <see cref="NotificationSeverity.Warning"/>.</summary>
    public bool IsWarning => Severity == NotificationSeverity.Warning;
    /// <summary>True when <see cref="Severity"/> is <see cref="NotificationSeverity.Error"/>.</summary>
    public bool IsError   => Severity == NotificationSeverity.Error;
}

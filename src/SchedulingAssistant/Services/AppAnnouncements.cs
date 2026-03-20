namespace SchedulingAssistant.Services;

/// <summary>
/// Describes a new-feature announcement tied to a specific application version.
/// When the user first launches a version that includes this announcement
/// (and they have not already acknowledged it), it is shown in the notification banner.
/// </summary>
/// <param name="SinceVersion">
/// The application version in which this announcement was introduced.
/// Users upgrading from an older version will see it on first launch.
/// </param>
/// <param name="Message">Banner message text — keep to one or two sentences.</param>
/// <param name="LinkText">Optional label for the hyperlink, e.g. "What's new".</param>
/// <param name="LinkUrl">
/// Optional URL opened when the user clicks the link.
/// Typically a GitHub release page or wiki article.
/// </param>
public record VersionedAnnouncement(
    Version SinceVersion,
    string  Message,
    string? LinkText = null,
    string? LinkUrl  = null);

/// <summary>
/// <para>The authoritative list of new-feature announcements shipped with each release.</para>
///
/// <para><b>How to author a new announcement:</b>
/// <list type="number">
///   <item>Add a new <see cref="VersionedAnnouncement"/> entry to <see cref="All"/>,
///   typically in the same PR as the feature it describes.</item>
///   <item>Set <c>SinceVersion</c> to the version being released — it must match
///   <c>&lt;Version&gt;</c> in <c>SchedulingAssistant.csproj</c>.</item>
///   <item>Keep the message short (one or two sentences). Link to release notes for detail.</item>
///   <item>Entries are shown oldest-first; users who skip multiple versions see all unseen entries.</item>
///   <item>Old entries may be pruned once no upgrade path could still encounter them.</item>
/// </list>
/// </para>
/// </summary>
public static class AppAnnouncements
{
    /// <summary>
    /// All versioned announcements in ascending version order.
    /// <see cref="AppNotificationService.EnqueueUnseenAnnouncements"/> reads this list
    /// at startup to decide which entries to show.
    /// </summary>
    public static readonly IReadOnlyList<VersionedAnnouncement> All =
    [        
        new VersionedAnnouncement(
            SinceVersion: new Version(1,0,2),
            Message     : "Good luck!")
    ];
}

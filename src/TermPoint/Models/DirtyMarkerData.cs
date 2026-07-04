namespace TermPoint.Models;

/// <summary>
/// JSON payload of the dirty-marker file written alongside D' by
/// <see cref="Services.CheckoutService.MarkDirty"/> when the first user edit of a
/// session occurs. Its presence means "D' contains changes not yet saved to D."
///
/// <para><b>Role in crash recovery:</b> <see cref="HashAtCheckout"/> records the
/// SHA-256 of D at the moment the session became dirty. After a crash, recovery is
/// only safe when D still has this hash — proof that no other writer touched D while
/// the unsaved changes were stranded. If the hashes differ, the changes cannot be
/// restored automatically and the user is offered an exported copy instead.</para>
///
/// <para><b>Legacy format:</b> markers written before this type existed contain a bare
/// ISO-8601 timestamp, not JSON. Readers must treat a parse failure as
/// "hash unknown" (see <c>CheckoutService.TryReadMarkerHash</c>), which routes
/// recovery down the conservative export path.</para>
/// </summary>
public sealed class DirtyMarkerData
{
    /// <summary>UTC time at which the session first became dirty.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// SHA-256 hex digest of D at the last checkout or successful save preceding the
    /// first unsaved edit. Null only when unknown (should not occur for markers
    /// written by current code — MarkDirty is a no-op in new-database mode, the one
    /// state where no source hash exists).
    /// </summary>
    public string? HashAtCheckout { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the D.tmp snapshot from a save whose final rename was
    /// never confirmed (it timed out or threw, but the abandoned move may still land
    /// later — the "ghost rename"). Recorded by
    /// <c>CheckoutService.RecordPendingSaveHash</c> so that recovery after a restart
    /// can recognize a D bearing this hash as "updated by our own delayed rename, not
    /// by another writer" and safely restore the working copy. Null when no
    /// unconfirmed rename is outstanding.
    /// </summary>
    public string? PendingSaveHash { get; set; }
}

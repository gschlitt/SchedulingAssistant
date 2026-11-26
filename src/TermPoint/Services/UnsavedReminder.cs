namespace TermPoint.Services;

/// <summary>
/// When to show the one-time "You haven't saved since 10:42" notice (undo design, decision 6): two hours after changes
/// became unsaved, once per unsaved stretch. Since TermPoint no longer saves on its own, a lost lock (story 8) keeps
/// everything since the last save on the user's computer; the notice keeps that stretch from growing unnoticed.
/// Pure, so the rule is tested without timers.
/// </summary>
public static class UnsavedReminder
{
    /// <summary>How long changes stay unsaved before the notice is shown.</summary>
    public static readonly TimeSpan After = TimeSpan.FromHours(2);

    /// <summary>
    /// How long until the notice is due for the current unsaved stretch.
    /// </summary>
    /// <param name="unsavedSince">When the changes became unsaved (<see cref="SectionHistory.UnsavedSince"/>); null when nothing is unsaved.</param>
    /// <param name="remindedFor">The <paramref name="unsavedSince"/> value the notice was last shown for; null if never.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Null when no notice is due (nothing unsaved, or already shown for this stretch); otherwise the wait, zero when overdue.</returns>
    public static TimeSpan? DueIn(DateTime? unsavedSince, DateTime? remindedFor, DateTime now)
    {
        if (unsavedSince is not { } since || remindedFor == since) return null;
        var wait = since + After - now;
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }
}

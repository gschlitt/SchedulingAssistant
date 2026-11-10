namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of the Refresh button that a read-only session shows (spec item 28, from item 11): the
/// button's tooltip, and the one line of the yellow save-error row that says a refresh failed. Also the one pure
/// function that picks that line from the facts of a failed refresh (<see cref="FailureLine"/>), so the choice can be
/// unit-tested without a window. <c>MainWindowViewModel.RefreshData</c> only gathers the facts and calls it.
/// Modelled on <see cref="SavePrompt"/> and <see cref="KeptChangesPrompt"/>.
///
/// <para><b>What the wording promises.</b> A refresh that worked changes the tooltip to the time it worked; a refresh
/// that failed leaves the tooltip alone and says, in one line, what the user is looking at ("Showing data from 2:14 PM",
/// or "from when you opened the database" when no refresh has worked yet) and what to do. The one case where nothing
/// can help, a connection that was closed for the swap and that three reopen attempts could not reopen, says to close
/// and reopen TermPoint.</para>
///
/// <para><b>The wording rules</b> (rule 4 of the four rules for the shared database): plain words; no path, raw
/// exception message or log text; never "SQLite", "snapshot" or "write access". Tests enforce them.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser).</para>
/// </summary>
public static class RefreshPrompt
{
    // ── The tooltip ───────────────────────────────────────────────────────────

    /// <summary>Tooltip of the Refresh button before any refresh has worked in this session.</summary>
    public const string TooltipBeforeFirstRefresh = "Refresh to see changes made by the person editing this database.";

    /// <summary>The time format of every refresh text: hour without a leading zero, minutes, AM/PM ("2:14 PM"), in the current culture.</summary>
    private const string TimeFormat = "h:mm tt";

    /// <summary>
    /// Tooltip of the Refresh button after a refresh that worked: when, and what a click does.
    /// </summary>
    /// <param name="lastRefreshedAt">When the last refresh that WORKED finished, or null when none has yet.</param>
    /// <returns>"Last refreshed at 2:14 PM. Click to refresh again.", or <see cref="TooltipBeforeFirstRefresh"/> when null.</returns>
    public static string Tooltip(DateTime? lastRefreshedAt)
        => lastRefreshedAt is { } at
            ? $"Last refreshed at {at.ToString(TimeFormat)}. Click to refresh again."
            : TooltipBeforeFirstRefresh;

    // ── The failure lines ─────────────────────────────────────────────────────

    /// <summary>
    /// The line when the connection was closed for the swap and three reopen attempts failed (outcome
    /// <see cref="RefreshOutcome.ConnectionLost"/>), so the only next step is to restart.
    /// </summary>
    public const string ConnectionLost =
        "TermPoint couldn't refresh and lost its connection to the database. Close TermPoint and open it again.";

    /// <summary>
    /// What the user is looking at after a failed refresh: the time of the last refresh that worked, or the moment the
    /// database was opened when none has worked yet.
    /// </summary>
    /// <param name="lastRefreshedAt">When the last refresh that worked finished, or null when none has.</param>
    /// <returns>"2:14 PM" or "when you opened the database" (to follow "Showing data from ").</returns>
    private static string ShowingDataFrom(DateTime? lastRefreshedAt)
        => lastRefreshedAt is { } at ? at.ToString(TimeFormat) : "when you opened the database";

    /// <summary>The line when the database location did not respond (the refresh outcome <see cref="RefreshOutcome.SourceUnavailable"/>).</summary>
    /// <param name="lastRefreshedAt">When the last refresh that worked finished, or null when none has.</param>
    /// <returns>"Couldn't refresh: the database location isn't responding. Showing data from 2:14 PM."</returns>
    public static string NotResponding(DateTime? lastRefreshedAt)
        => $"Couldn't refresh: the database location isn't responding. Showing data from {ShowingDataFrom(lastRefreshedAt)}.";

    /// <summary>The line for any other failure (an exception, or an outcome nothing above names).</summary>
    /// <param name="lastRefreshedAt">When the last refresh that worked finished, or null when none has.</param>
    /// <returns>"Couldn't refresh just now. Showing data from 2:14 PM. Try again in a moment."</returns>
    public static string OtherFailure(DateTime? lastRefreshedAt)
        => $"Couldn't refresh just now. Showing data from {ShowingDataFrom(lastRefreshedAt)}. Try again in a moment.";

    /// <summary>
    /// Picks the one line to show for a refresh, from the facts the view model gathered, or null when the refresh
    /// worked (so "null" is also the test for "set the tooltip's time and reload the list"). In order:
    /// <list type="number">
    /// <item><description><paramref name="threw"/>: <see cref="OtherFailure"/>;</description></item>
    /// <item><description>outcome <see cref="RefreshOutcome.Updated"/>: null;</description></item>
    /// <item><description>outcome <see cref="RefreshOutcome.SourceUnavailable"/>: <see cref="NotResponding"/>;</description></item>
    /// <item><description>outcome <see cref="RefreshOutcome.ReplaceFailed"/>: <see cref="OtherFailure"/>;</description></item>
    /// <item><description>outcome <see cref="RefreshOutcome.ConnectionLost"/>: <see cref="ConnectionLost"/>;</description></item>
    /// <item><description>anything else (no outcome, or one added later): <see cref="OtherFailure"/>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="outcome">What <c>RefreshReadOnlySnapshotAsync</c> returned, or null when it did not return (it threw).</param>
    /// <param name="threw">True when the refresh call threw an exception.</param>
    /// <param name="lastRefreshedAt">When the last refresh that worked finished, or null when none has.</param>
    /// <returns>The line for the yellow row, or null when the refresh worked.</returns>
    public static string? FailureLine(RefreshOutcome? outcome, bool threw, DateTime? lastRefreshedAt)
    {
        if (threw) return OtherFailure(lastRefreshedAt);

        return outcome switch
        {
            RefreshOutcome.Updated           => null,
            RefreshOutcome.SourceUnavailable => NotResponding(lastRefreshedAt),
            RefreshOutcome.ReplaceFailed     => OtherFailure(lastRefreshedAt),
            RefreshOutcome.ConnectionLost    => ConnectionLost,
            _                                => OtherFailure(lastRefreshedAt)
        };
    }
}

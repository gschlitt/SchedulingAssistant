namespace TermPoint.Services;

/// <summary>
/// Every user-facing text about the file TermPoint uses to mark a database as in use (the lock file) being lost or put
/// back while a session is open (spec item 28; data-persistence review findings F6 and F13):
/// <list type="bullet">
/// <item><description><b>The lock-lost banner</b> (<see cref="Banner"/>): shown, sticky, when a mid-session check finds
/// the session no longer owns the lock and the session is made read-only. Says who or what took the lock, whether there
/// were unsaved changes and where they are, what happens to them next (<see cref="KeptChangesPrompt.OfferToAdd"/>), and
/// how to edit again.</description></item>
/// <item><description><b>The lock-put-back notice</b> (<see cref="LockFilePutBack"/>): the informational,
/// auto-dismissing banner shown when another program removed the file and TermPoint re-created it, so nothing was
/// lost.</description></item>
/// </list>
/// Kept out of <c>SessionLifecycle</c> and <c>CheckoutService</c> so the wording can be pinned by unit tests. Modelled on
/// <see cref="SavePrompt"/> and <see cref="KeptChangesPrompt"/>.
///
/// <para><b>The wording rules</b> (rule 4 of the four rules for the shared database): plain words; no path, raw
/// exception or log text; never "lock file" (the texts say "the file TermPoint uses to mark this database as in use"),
/// "write access", "working copy", "local copy" or "safe". A test enforces them.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser): it takes plain values, not
/// <c>WriteLockLostReason</c> or <c>LockFileData</c>, so it refers to no desktop-only type.</para>
/// </summary>
public static class LockLossPrompt
{
    // ── The lock-lost banner, first sentence (why the session is read-only now) ──

    /// <summary>
    /// First sentence of the banner when another person took the lock over and their name is known.
    /// </summary>
    /// <param name="who">The new holder as <c>"Username on Machine"</c>, or just <c>"Username"</c> when the machine is unknown.</param>
    /// <returns>The sentence.</returns>
    public static string TakenOverBy(string who)
        => $"{who} has taken over editing this database, so it's read-only for you now.";

    /// <summary>First sentence of the banner when the lock was taken over and the new holder cannot be named.</summary>
    public const string TakenOverByUnknown =
        "Someone else has taken over editing this database, so it's read-only for you now.";

    /// <summary>
    /// First sentence of the banner for every reason other than a takeover: the lock file was deleted <b>or is
    /// unreadable</b> (finding F13: a damaged file is not "removed"). Antivirus and cloud-sync software are the usual
    /// cause, so the sentence says so.
    /// </summary>
    public const string FileRemovedOrChanged =
        "Another program removed or changed the file TermPoint uses to mark this database as in use " +
        "(antivirus or cloud-sync software can do this), so it's read-only for you now.";

    // ── The lock-lost banner, middle (the user's changes) ─────────────────────

    /// <summary>
    /// The banner's second part when there WERE unsaved changes: they are kept on this computer and
    /// <see cref="KeptChangesPrompt.OfferToAdd"/> says what happens next. Starts with a space so it can follow the
    /// first sentence directly.
    /// </summary>
    public const string WithUnsavedChanges =
        " " + KeptChangesPrompt.ChangesSinceLastSaveKept + " " + KeptChangesPrompt.OfferToAdd;

    /// <summary>The banner's second part when there were NO unsaved changes. Starts with a space.</summary>
    public const string WithoutUnsavedChanges = " There were no unsaved changes at the time.";

    // ── The lock-lost banner, last sentence (how to edit again) ───────────────

    /// <summary>Last sentence after a takeover: wait for the other person to finish. Starts with a space.</summary>
    public const string EditAgainAfterTakeover = " To edit again once they've finished, close TermPoint and open it again.";

    /// <summary>Last sentence for any other reason: nobody else is editing, so just reopen. Starts with a space.</summary>
    public const string EditAgainOtherwise = " To edit again, close TermPoint and open it again.";

    /// <summary>
    /// The whole lock-lost banner, joined from its parts with single spaces:
    /// <list type="number">
    /// <item><description>why the session is read-only now (taken over by a named person, by someone unnamed, or the file was removed or changed);</description></item>
    /// <item><description>the user's changes: kept (<see cref="WithUnsavedChanges"/>) or none (<see cref="WithoutUnsavedChanges"/>);</description></item>
    /// <item><description>how to edit again.</description></item>
    /// </list>
    /// </summary>
    /// <param name="takenOver">True when the lock now belongs to another session (<c>WriteLockLostReason.TakenOver</c>); false when the file was deleted or is unreadable.</param>
    /// <param name="holderUsername">The new holder's user name, or null or blank when unknown. Read only when <paramref name="takenOver"/>.</param>
    /// <param name="holderMachine">The new holder's machine name, or null or blank when unknown. Read only when <paramref name="takenOver"/> and the user name is known.</param>
    /// <param name="hadUnsavedChanges">True when the session had unsaved changes (its dirty marker existed) when the lock was lost.</param>
    /// <returns>The banner text.</returns>
    public static string Banner(bool takenOver, string? holderUsername, string? holderMachine, bool hadUnsavedChanges)
    {
        string first;
        if (!takenOver)
        {
            first = FileRemovedOrChanged;
        }
        else if (string.IsNullOrWhiteSpace(holderUsername))
        {
            first = TakenOverByUnknown;
        }
        else
        {
            var name    = holderUsername.Trim();
            var machine = holderMachine?.Trim();
            first = TakenOverBy(string.IsNullOrEmpty(machine) ? name : $"{name} on {machine}");
        }

        return first
               + (hadUnsavedChanges ? WithUnsavedChanges : WithoutUnsavedChanges)
               + (takenOver ? EditAgainAfterTakeover : EditAgainOtherwise);
    }

    // ── The lock file put back ────────────────────────────────────────────────

    /// <summary>
    /// The informational banner shown (auto-dismissing) after another program removed the lock file and TermPoint
    /// re-created it atomically. The session carries on, so nothing needs doing.
    /// </summary>
    public const string LockFilePutBack =
        "Another program removed the file TermPoint uses to mark this database as in use " +
        "(antivirus or cloud-sync software can do this). TermPoint has put it back, and you can carry on as normal.";
}

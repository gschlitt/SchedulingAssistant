namespace TermPoint.Services;

/// <summary>
/// Every user-facing text that tells the user their unsaved changes were <b>kept</b> after something went wrong (spec
/// item 28; data-persistence review finding F6): the lock-lost banner's kept-changes sentences, "Unsaved Changes Kept"
/// (shown when a recovery open lands in read-only), and the two "Changes Not Saved" messages (a failed save when
/// switching database, and a failed save when closing). Kept out of <c>SessionLifecycle</c> so the wording can be pinned
/// by unit tests. Modelled on <see cref="SavePrompt"/>, <see cref="RecoveryConflictPrompt"/> and
/// <see cref="LocalCopyCheckPrompt"/>.
///
/// <para><b>What 1.2.3 really does, and so what the wording promises.</b> Kept changes (left by a failed save, a lost
/// lock, a failed exit save or a crash) are offered back at the next open of the database for editing, by "Restore
/// Unsaved Changes?". If the database changed in the meantime they cannot be added, and the user gets "Can't Restore
/// Unsaved Changes" with <see cref="RecoveryConflictPrompt.SaveCopyButton"/>. The old texts promised "TermPoint will offer
/// to restore them" as though that always works. These say exactly what happens: <see cref="OfferToAdd"/> names both
/// outcomes. The restore flow and its prompts are untouched (they are replaced in 1.2.4).</para>
///
/// <para><b>The rules the wording keeps</b> (rule 4 of the four rules for the shared database): every text says what
/// happened, that the work is kept and where (on this computer), and what happens next. No text carries a path, a raw
/// exception message or log text, and none says "working copy", "local copy" or "write access". Tests enforce this.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser): it refers to no desktop-only type.</para>
/// </summary>
public static class KeptChangesPrompt
{
    // ── The shared sentences ──────────────────────────────────────────────────

    /// <summary>
    /// Says where the changes are, in a sentence that follows one naming "your unsaved changes" or "your latest
    /// changes". Never mentions a file or a path: from the user's point of view the changes are on their computer.
    /// </summary>
    public const string KeptSentence = "They're kept on this computer.";

    /// <summary>
    /// The lock-lost banner's own version of <see cref="KeptSentence"/>: the sentence that opens its kept-changes
    /// half, naming which changes (those since the last save) as well as where they are.
    /// </summary>
    public const string ChangesSinceLastSaveKept = "Your changes since your last save are kept on this computer.";

    /// <summary>
    /// What happens to kept changes next, for a message that does not name the database (the user is already looking at
    /// it, or the message is about the database they are about to close). Says both outcomes: they are offered back the
    /// next time the database is opened for editing, unless someone else changed the database first, in which case they
    /// can still be saved as a separate copy.
    /// </summary>
    public const string OfferToAdd =
        "TermPoint will offer to add them to the database the next time you open it for editing. " +
        "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy.";

    /// <summary>
    /// Same promise as <see cref="OfferToAdd"/>, naming the database. For the message shown while leaving one database
    /// for another, where "the database" would be ambiguous.
    /// </summary>
    /// <param name="databaseName">The name of the database the changes belong to (no folder, no extension).</param>
    /// <returns>The sentence pair, with the name in straight double quotes.</returns>
    public static string OfferToAddTo(string databaseName)
        => $"TermPoint will offer to add them to \"{databaseName}\" the next time you open it for editing. " +
           "If someone else changes it first, they can't be added, but you'll be able to save them as a separate copy.";

    // ── "Unsaved Changes Kept" ────────────────────────────────────────────────

    /// <summary>Title of the message shown when kept changes from last time cannot be added yet because someone else is editing.</summary>
    public const string UnsavedChangesKeptTitle = "Unsaved Changes Kept";

    /// <summary>
    /// Body of "Unsaved Changes Kept": a recovery open landed in read-only (another editor holds the lock, or the user
    /// declined a stale-lock takeover), so the changes from last time cannot be added yet.
    /// </summary>
    public const string UnsavedChangesKeptMessage =
        "Someone else is editing this database right now, so your unsaved changes from last time can't be added yet. " +
        KeptSentence + " " + OfferToAdd;

    // ── "Changes Not Saved" ───────────────────────────────────────────────────

    /// <summary>Title of both "Changes Not Saved" messages (a failed save when switching database, and when closing).</summary>
    public const string ChangesNotSavedTitle = "Changes Not Saved";

    /// <summary>
    /// Body of "Changes Not Saved" when the final save of a database failed while the user was switching to another one.
    /// </summary>
    /// <param name="oldDatabaseName">The name of the database being left (no folder, no extension).</param>
    /// <returns>The message text.</returns>
    public static string ChangesNotSavedOnSwitch(string oldDatabaseName)
        => $"Your latest changes to \"{oldDatabaseName}\" couldn't be saved. " +
           KeptSentence + " " + OfferToAddTo(oldDatabaseName);

    /// <summary>Body of "Changes Not Saved" when the final save failed while the user was closing TermPoint.</summary>
    public const string ChangesNotSavedOnClose =
        "Your latest changes couldn't be saved to the database. " + KeptSentence + " " + OfferToAdd;
}

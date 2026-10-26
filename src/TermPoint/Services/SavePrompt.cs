namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of a FAILED SAVE (spec item 12): the one yellow save-error banner that
/// <see cref="CheckoutService.SaveFailed"/> raises when <see cref="CheckoutService.SaveAsync"/> cannot finish.
/// Kept out of <see cref="CheckoutService"/> so the wording can be pinned by unit tests. Modelled on
/// <see cref="RestorePrompt"/> and <see cref="LocalCopyCheckPrompt"/>.
///
/// <para><b>The rules the wording keeps</b> (rule 4 of the four rules for the shared database): every text says what
/// happened, that the user's work is kept, and what happens next (autosave retries by itself; a manual save can be
/// tried again). No text carries a raw exception message, a path or log text, and none mentions a "working copy" or a
/// "local copy". Tests enforce this.</para>
///
/// <para><b>One notice for an outage</b> (spec item 28, finding F15). Every failure that means "the database location is
/// not responding" (a save of any kind that times out, and the heartbeat's lock check) shows the SAME notice,
/// <see cref="OutageNotice"/>: it is sticky (never auto-dismisses) and clears only when the location responds again (a
/// save succeeds, or the heartbeat reaches the lock file). It replaces the old per-save "isn't responding" text, its
/// retry suffix and the heartbeat's separate, differently worded message. The other transient failures keep their
/// texts and their retry suffixes.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser).</para>
/// </summary>
public static class SavePrompt
{
    // ── Transient failures (retryable; the suffix says what happens next) ─────

    /// <summary>
    /// The database file (D) or the file the save writes beside it (D.tmp) is held by another program, access was
    /// denied, or D.tmp vanished mid-save.
    /// </summary>
    public const string InUseMessage =
        "TermPoint couldn't save because the database file is in use by another program or can't be accessed. " +
        "Your changes are kept.";

    /// <summary>The snapshot of the session's own files could not be prepared (or hashed) while those files still exist.</summary>
    public const string PrepareFailedMessage =
        "TermPoint couldn't prepare the save on this computer. Your changes are kept.";

    /// <summary>Any other I/O error while copying to, verifying at, or renaming onto the database location.</summary>
    public const string WriteFailedMessage =
        "TermPoint couldn't save because writing to the database location failed. Your changes are kept.";

    /// <summary>A torn transfer: the file written beside the database did not hash the same as the snapshot.</summary>
    public const string VerifyFailedMessage =
        "TermPoint couldn't save because the file it wrote to the database location didn't pass its check. " +
        "The database wasn't changed, and your changes are kept.";

    /// <summary>The final safety net in <see cref="CheckoutService.SaveAsync"/>: an exception nobody anticipated.</summary>
    public const string UnexpectedMessage =
        "TermPoint couldn't save because of an unexpected problem. Your changes are kept.";

    // ── Non-transient failures (retrying cannot help; no suffix) ──────────────

    /// <summary>
    /// One of the session's own files is gone, so the snapshot cannot be taken. Retrying can never succeed, so this
    /// text takes no retry suffix and its banner is always sticky.
    /// </summary>
    public const string SessionFileMissingMessage =
        "TermPoint couldn't save because one of its files for this session is missing, so changes since your last " +
        "save can't be saved. Close TermPoint and open the database again.";

    /// <summary>
    /// The crash-protection file could not be written. Saving itself still works; this is a warning, not a failed
    /// save, so it takes no retry suffix.
    /// </summary>
    public const string CrashProtectionMessage =
        "TermPoint couldn't create its crash-protection file for this session. " +
        "Saving still works normally, but unsaved changes may not be recoverable " +
        "after a crash — save your work frequently.";

    // ── The outage notice (the database location is not responding; spec item 28, finding F15) ──

    /// <summary>
    /// First part of the outage notice when the session HAS unsaved changes: the location can't be reached and the
    /// changes are kept on this computer. Followed by <see cref="OutageAutoSaveSuffix"/> or
    /// <see cref="OutageManualSuffix"/>, which say how the user gets them saved.
    /// </summary>
    public const string OutageWithChangesMessage =
        "TermPoint can't reach the database location. Your changes are kept on this computer.";

    /// <summary>
    /// Ends the outage notice when autosave is on: the next autosave cycle retries by itself. The same words as
    /// <see cref="AutoSaveRetrySuffix"/>.
    /// </summary>
    public const string OutageAutoSaveSuffix = AutoSaveRetrySuffix;

    /// <summary>
    /// Ends the outage notice when autosave is off: nothing retries by itself, so the user is told when to press Save.
    /// (The other transient failures say "Try saving again in a moment"; in an outage of unknown length that is the
    /// wrong advice, hence a different suffix.)
    /// </summary>
    public const string OutageManualSuffix = " Press Save once the connection is back.";

    /// <summary>
    /// The whole outage notice when the session has NO unsaved changes (possible when the heartbeat's lock check, not a
    /// save, finds the location unreachable). Nothing needs doing, and it says so. It promises nothing about when it clears
    /// (it clears when a save works or the lock check reaches the location again; the user can also dismiss it).
    /// </summary>
    public const string OutageNoChangesMessage =
        "TermPoint can't reach the database location right now. You have no unsaved changes.";

    /// <summary>
    /// The one notice for "the database location is not responding": raised for a save of any kind that times out and
    /// for the heartbeat's lock check, always sticky, cleared when the location responds again.
    /// </summary>
    /// <param name="hasUnsavedChanges">True when the session has unsaved changes (its dirty marker exists).</param>
    /// <param name="autoSaveEnabled">True when autosave is on, so the next cycle retries by itself. Ignored when there are no unsaved changes.</param>
    /// <returns>The notice text.</returns>
    public static string OutageNotice(bool hasUnsavedChanges, bool autoSaveEnabled)
        => !hasUnsavedChanges
            ? OutageNoChangesMessage
            : OutageWithChangesMessage + (autoSaveEnabled ? OutageAutoSaveSuffix : OutageManualSuffix);

    /// <summary>
    /// True when <paramref name="text"/> is exactly one of the three texts <see cref="OutageNotice"/> can return. The
    /// view model uses it to know that the banner on screen is the outage notice, so that "the location responds again"
    /// clears that notice and never another banner (a lost-lock banner, a failed save's text, a refresh failure).
    /// </summary>
    /// <param name="text">The text of the banner currently shown, or null when none is.</param>
    /// <returns>True for an outage notice; false for any other text, including null.</returns>
    public static bool IsOutageNotice(string? text)
        => text is not null
           && (text == OutageNotice(hasUnsavedChanges: false, autoSaveEnabled: false)
               || text == OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: true)
               || text == OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false));

    // ── Suffixes ──────────────────────────────────────────────────────────────

    /// <summary>Appended to a transient failure's text when the save was an autosave (the next cycle retries by itself).</summary>
    public const string AutoSaveRetrySuffix = " TermPoint will try again automatically.";

    /// <summary>Appended to a transient failure's text when the save was manual (the user can try again).</summary>
    public const string ManualRetrySuffix = " Try saving again in a moment.";

    /// <summary>
    /// The full banner text for a transient failure: <paramref name="message"/> followed by the retry suffix that
    /// matches how the save was started.
    /// </summary>
    /// <param name="message">One of the transient texts above (they all end with "Your changes are kept.").</param>
    /// <param name="isAutoSave">True when the autosave timer started the save; false for a manual save or the exit save.</param>
    /// <returns>The banner text, e.g. <c>"… Your changes are kept. TermPoint will try again automatically."</c>.</returns>
    public static string ForTransientFailure(string message, bool isAutoSave)
        => message + (isAutoSave ? AutoSaveRetrySuffix : ManualRetrySuffix);
}

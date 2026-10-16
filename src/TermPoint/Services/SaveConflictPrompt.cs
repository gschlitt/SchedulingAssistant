namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of the "your changes can't be saved" flow (spec item 10, step 10a): the persistent banner
/// shown when a save is refused because the shared database was changed elsewhere since this session opened it, its
/// two buttons, the wait dialog and the confirmation shown while the user's version is saved as a separate file, the
/// "Discard Changes" confirmation, and the refusal shown while an inline editor is open. The wording is kept out of
/// <c>MainWindow</c> and <c>MainWindowViewModel</c> so it can be pinned by unit tests. Modelled on
/// <see cref="RecoveryConflictPrompt"/>, whose save-dialog title and suggested file name the save picker reuses.
///
/// <para><b>UX rule:</b> the user is never told about a "working copy" or a "local copy", and never sees a path —
/// only the NAME of the file they chose. A test enforces the rule.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser): it refers to no desktop-only
/// type.</para>
/// </summary>
public static class SaveConflictPrompt
{
    // ── The banner ────────────────────────────────────────────────────────────

    /// <summary>
    /// Text of the persistent banner. It says what happened (the database was changed elsewhere, so the changes
    /// can't be saved) and what to do (save a separate copy, or discard; either way the current database is
    /// reopened). It does not mention autosave: the refused save may have been a manual one. There is deliberately no Dismiss button: the state does not go away until the user chooses.
    /// </summary>
    public const string BannerMessage =
        "TermPoint can't save your changes: the database was changed elsewhere since you opened it. " +
        "Save your version as a separate file, or discard your changes, " +
        "and TermPoint will reopen the current database.";

    /// <summary>Label of the banner's first button: save the user's version as a separate file.</summary>
    public const string SaveCopyButton = "Save a Copy…";

    /// <summary>Label of the banner's second button: give up the changes made since the last save.</summary>
    public const string DiscardChangesButton = "Discard Changes";

    // ── While the user's version is being saved ───────────────────────────────

    /// <summary>Title of the wait dialog shown while the user's version is written to the chosen file.</summary>
    public const string SavingTitle = "Saving Your Version";

    /// <summary>Body of the wait dialog.</summary>
    public const string SavingMessage = "Saving your version…";

    // ── After the user's version was saved ────────────────────────────────────

    /// <summary>Title of the message shown once the user's version has been saved.</summary>
    public const string SavedTitle = "Your Version Was Saved";

    /// <summary>
    /// Body of the "saved" message: names the file the user chose, says where to find it again, and says what
    /// TermPoint does next.
    /// </summary>
    /// <param name="fileName">
    /// The NAME of the saved file only (the caller passes <c>Path.GetFileName</c>) — never a folder or a path.
    /// </param>
    /// <returns>The message text.</returns>
    public static string SavedMessage(string fileName)
        => $"Your version was saved as \"{fileName}\". You can open it any time from Files → Open Database. " +
           "TermPoint will now reopen the current database.";

    // ── The Discard Changes confirmation ──────────────────────────────────────

    /// <summary>Title of the confirmation shown before the user's changes are discarded.</summary>
    public const string DiscardConfirmTitle = "Discard Changes?";

    /// <summary>Body of the confirmation: says what is lost and that it cannot be undone.</summary>
    public const string DiscardConfirmMessage =
        "Discard the changes you made since your last save? This can't be undone.";

    /// <summary>Label of the confirmation's confirming button.</summary>
    public const string DiscardConfirmButton = "Discard";

    /// <summary>Label of the confirmation's cancelling button.</summary>
    public const string DiscardCancelButton = "Cancel";

    // ── While an inline editor is open ────────────────────────────────────────

    /// <summary>
    /// Title of the refusal shown when either choice is made while an inline editor is open. (An edit that has not
    /// been applied is not in the database at all, so neither choice could keep it. Spec item 14 will handle it
    /// properly later.)
    /// </summary>
    public const string EditorOpenTitle = "Finish Editing First";

    /// <summary>Body of the editor-open refusal.</summary>
    public const string EditorOpenMessage = "Apply or cancel the open edit first.";
}

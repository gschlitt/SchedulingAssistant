namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of the "can't restore unsaved changes" flow (spec item 16, step 16a): the prompt shown
/// when crash recovery finds that the shared database changed since the unsaved changes were made, the save dialog
/// that offers to keep them as a separate file, and the two messages that can follow (the chosen name is taken; the
/// copy could not be written). The wording is kept out of <c>MainWindow</c> so it can be pinned by unit tests.
/// Modelled on <see cref="LocalCopyCheckPrompt"/>, <see cref="StaleLockPrompt"/> and <see cref="RestorePrompt"/>.
///
/// <para><b>UX rule:</b> the user is never told about a "working copy" or a "local copy" — from their point of view
/// they have "unsaved changes" and "your version". A test enforces the rule.</para>
///
/// <para><b>The promise the wording keeps.</b> The recovered copy is only ever written to a file that does not exist
/// yet, so the "choose a new name" message says so; and the conflict message points to the one supported way to make
/// the saved version the database again ("Restore from a backup" in the startup chooser), which takes the write lock
/// and keeps the replaced database.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser): it refers to no desktop-only
/// type.</para>
/// </summary>
public static class RecoveryConflictPrompt
{
    // ── The conflict prompt ───────────────────────────────────────────────────

    /// <summary>Title of the prompt shown when the unsaved changes cannot be restored automatically.</summary>
    public const string ConflictTitle = "Can't Restore Unsaved Changes";

    /// <summary>
    /// Body of the prompt. The first two paragraphs are the wording the prompt has always had. The third explains how
    /// a version the user and their colleagues decide to keep can replace the database: through "Restore from a
    /// backup" in the database chooser (which is shown at startup only when the "Always open the most recent
    /// database at startup" preference is off, hence the parenthesis).
    /// </summary>
    public const string ConflictMessage =
        "The database has been changed since your unsaved changes were made, " +
        "so they can't be restored automatically.\n\n" +
        "Would you like to save your version as a separate file instead? " +
        "You can open it later from Files → Open Database.\n\n" +
        "If you and your colleagues decide to keep your version, it can replace the database: " +
        "choose \"Restore from a backup\" in the database chooser when TermPoint starts. " +
        "(To see the chooser at startup, turn off \"Always open the most recent database at startup\" in Preferences.)";

    /// <summary>Label of the prompt's confirming button: choose where to save the separate file.</summary>
    public const string SaveCopyButton = "Save a Copy…";

    /// <summary>Label of the prompt's other button: give up the unsaved changes.</summary>
    public const string DiscardButton = "Discard";

    // ── The save dialog ───────────────────────────────────────────────────────

    /// <summary>Title of the save-file dialog in which the user picks where to save the recovered copy.</summary>
    public const string SaveDialogTitle = "Save Recovered Copy";

    /// <summary>
    /// The file name the save dialog suggests: the database's own name (no folder, no extension) plus
    /// <c> (recovered).db</c>, e.g. <c>C:\Shared\BIOL-TT.db</c> becomes <c>BIOL-TT (recovered).db</c>.
    /// </summary>
    /// <param name="sourcePath">Path of the shared database whose unsaved changes are being saved.</param>
    /// <returns>The suggested file name (no folder).</returns>
    public static string SuggestedFileName(string sourcePath)
        => Path.GetFileNameWithoutExtension(sourcePath) + " (recovered).db";

    // ── What can follow the save dialog ───────────────────────────────────────

    /// <summary>
    /// Title of the message shown when the chosen file name is already taken (the shared database itself included).
    /// The save dialog is offered again afterwards.
    /// </summary>
    public const string AlreadyExistsTitle = "Choose a New Name";

    /// <summary>
    /// Body of the "name already taken" message. The recovered copy is deliberately never saved over an existing
    /// file: replacing the shared database this way would silently erase the colleagues' saves made since the crash.
    /// </summary>
    public const string AlreadyExistsMessage =
        "A file with that name already exists. Choose a new name — the recovered copy is never saved over an existing file.";

    /// <summary>Title of the message shown when the copy could not be written.</summary>
    public const string ExportFailedTitle = "Export Failed";

    /// <summary>
    /// Body of the "export failed" message, with the reason when the failure carried one.
    /// </summary>
    /// <param name="error">The exception behind the failure, or null when there is none to quote.</param>
    /// <returns>The message text.</returns>
    public static string ExportFailedMessage(Exception? error)
        => error is null ? "Could not save the copy." : $"Could not save the copy: {error.Message}";
}

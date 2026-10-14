using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of the handler for a failed open-time check of the local copy (spec item 5, step 5d):
/// the "in use" prompt, the "damaged" message, the "keeps failing" message, and the front-door messages shown when
/// the shared database cannot be used either. The wording is the user-approved wording of the 5d plan, kept out of
/// <c>MainWindow</c> so it can be pinned by unit tests. Modelled on <see cref="StaleLockPrompt"/> and
/// <see cref="RestorePrompt"/>.
///
/// <para><b>UX rule:</b> the user is never told about a "working copy" or a "local copy" — from their point of view
/// TermPoint has a database that opens, or does not. The texts therefore speak of "its files" and "the files it
/// prepares here". A test enforces the rule.</para>
/// </summary>
public static class LocalCopyCheckPrompt
{
    // ── The "in use" prompt ───────────────────────────────────────────────────

    /// <summary>
    /// Title of the "in use" prompt and of the "keeps failing" message — the same title the "couldn't make a copy of
    /// the database" prompt has always had, so all three read as one family.
    /// </summary>
    public const string CouldntOpenTitle = "Couldn't Open the Database";

    /// <summary>
    /// Body of the "in use" prompt: another program on this computer is using the files TermPoint prepares to
    /// open a database (the quiet retries did not clear it).
    /// </summary>
    public const string InUseMessage =
        "TermPoint couldn't finish opening the database because another program on this computer is using its " +
        "files, often antivirus or backup software checking a file that was just copied. This usually clears in " +
        "a few seconds.";

    /// <summary>Label of the "in use" prompt's confirming button: try opening again.</summary>
    public const string TryAgainButton = "Try again";

    /// <summary>Label of the "in use" prompt's other button, and of the single button of the "keeps failing" message: give up.</summary>
    public const string CloseButton = "Close";

    // ── Unsaved changes that could not be kept ────────────────────────────────

    /// <summary>
    /// Title of the message shown when unsaved changes from an earlier session were found but were damaged. The
    /// same text crash recovery shows for a damaged recovery file (<c>MainWindow.OfferCrashRecoveryAsync</c> uses
    /// these constants), because it is the same fact.
    /// </summary>
    public const string UnsavedChangesLostTitle = "Unsaved Changes Lost";

    /// <summary>Body of the "Unsaved Changes Lost" message.</summary>
    public const string UnsavedChangesLostMessage =
        "Unsaved changes from a previous session with this database were " +
        "found, but they were damaged and could not be restored.";

    // ── The shared database is damaged ────────────────────────────────────────

    /// <summary>Title of the message shown when the shared database itself failed its integrity check (the front-door damaged title).</summary>
    public const string DamagedSharedTitle = DatabaseSwitchPrecheck.DamagedTitle;

    /// <summary>
    /// Body of the message shown when the shared database itself failed its integrity check. It is followed by the
    /// database chooser in recovery mode, which offers Restore from a backup.
    /// </summary>
    public const string DamagedSharedMessage =
        "The database failed its integrity check and may be damaged.";

    // ── The copy keeps failing ────────────────────────────────────────────────

    /// <summary>
    /// Body of the message shown when the shared database is fine but the files TermPoint prepares on this computer
    /// keep failing their safety check, even after one fresh attempt. Shown with a single Close button.
    /// </summary>
    public const string KeepsFailingMessage =
        "The shared database is fine, but TermPoint can't open it on this computer: the files it prepares here " +
        "keep failing their safety check. Nothing has been changed, and your colleagues aren't affected. This " +
        "usually points to a problem with this computer, such as a failing disk or security software interfering " +
        "with TermPoint's files. Try restarting the computer. If it keeps happening, contact your IT support and " +
        "mention this message. Meanwhile, you can open the database from another computer.";

    // ── The shared database cannot be used either ─────────────────────────────

    /// <summary>
    /// The front-door message for a shared database that cannot be used, so the user hears exactly what they would
    /// have heard had the front door (the check made before any open) found it: the same titles and texts as
    /// <see cref="DatabaseSwitchPrecheck"/>.
    /// </summary>
    /// <param name="shared">The re-check's verdict. Only Unreadable, Unreachable and Missing have a front-door message.</param>
    /// <param name="databasePath">Full path of the shared database (named by the "not found" message).</param>
    /// <returns>The title and the body to show.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shared"/> is not one of the three above.</exception>
    public static (string Title, string Message) FrontDoorMessage(SharedDatabaseVerdict shared, string databasePath)
        => shared switch
        {
            SharedDatabaseVerdict.Unreadable =>
                (DatabaseSwitchPrecheck.UnreadableTitle, DatabaseChooserViewModel.UnreadableDatabaseMessage),
            SharedDatabaseVerdict.Unreachable =>
                (DatabaseSwitchPrecheck.UnreachableTitle, NetworkFileOps.UnreachableMessage),
            SharedDatabaseVerdict.Missing =>
                (DatabaseSwitchPrecheck.MissingTitle, DatabaseSwitchPrecheck.MissingMessage(databasePath)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(shared), shared, "Only Unreadable, Unreachable and Missing have a front-door message.")
        };
}

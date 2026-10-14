namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of "Restore from backup" (spec item 5, step 5b): the confirmation, the wait dialog,
/// the panel's description and read-only note, and one message for each <see cref="RestoreOutcome"/>. Kept out
/// of <c>MainWindow</c> and the view models so the wording — including the rule that a message names the
/// pre-restore file by its NAME only, never by its folder — can be unit-tested without any UI. Modelled on
/// <see cref="StaleLockPrompt"/>.
///
/// <para><b>The promise the wording keeps.</b> Every outcome except <see cref="RestoreOutcome.Restored"/> and
/// <see cref="RestoreOutcome.RenameUnconfirmed"/> leaves the database exactly as it was, and says so in so many
/// words ("Your database is unchanged."). The two outcomes that may have replaced the database say what was
/// kept and where, and that TermPoint is about to close.</para>
///
/// <para>This file is pure text and compiles on both targets (desktop and browser), like
/// <see cref="RestoreResult"/> which it reads.</para>
/// </summary>
public static class RestorePrompt
{
    // ── Fixed texts ───────────────────────────────────────────────────────────

    /// <summary>Label of the confirmation dialog's confirming button.</summary>
    public const string ConfirmLabel = "Restore";

    /// <summary>Explanatory text shown in the Save &amp; Backup panel above the list of backups.</summary>
    public const string PanelDescription =
        "Restoring replaces the database for everyone with the selected backup, then restarts TermPoint. " +
        "The database as it was before the restore is kept beside it.";

    /// <summary>
    /// The note shown in the Save &amp; Backup panel when this session cannot restore (it is a reader, or its
    /// licence is read-only); also the first sentence of the "not a writer" refusal.
    /// </summary>
    public const string ReadOnlyNote = "Only the person editing this database can restore it.";

    /// <summary>Title of the wait dialog shown while TermPoint saves and restores.</summary>
    public const string WaitTitle = "Restoring the Backup";

    /// <summary>Body of the wait dialog.</summary>
    public const string WaitMessage =
        "Saving your changes and restoring the backup. This can take a minute on a slow network.";

    /// <summary>Title of every message that says the restore was never started (or was refused up front).</summary>
    public const string NotStartedTitle = "Restore Not Started";

    /// <summary>
    /// Body shown when the save that precedes the restore failed, so the restore was not started. The save has
    /// already shown its own banner; this says what that means for the restore.
    /// </summary>
    public const string SaveFailedMessage =
        "Your latest changes couldn't be saved, so the restore wasn't started. Your database is unchanged.";

    /// <summary>Title of a message saying a started restore was stopped with the database untouched.</summary>
    private const string StoppedTitle = "Restore Stopped";

    /// <summary>Title of the message shown after a confirmed restore.</summary>
    private const string RestoredTitle = "Backup Restored";

    /// <summary>Title of the message shown when the final step could not be confirmed.</summary>
    private const string NotConfirmedTitle = "Restore Not Confirmed";

    /// <summary>
    /// How the pre-restore file is referred to when its path is unexpectedly missing from the result (which the
    /// routine never produces for the outcomes that need it). Reads as a noun phrase, so the sentences around
    /// it stay grammatical.
    /// </summary>
    private const string PreRestoreFileFallback = "the \"(before restore …)\" file";

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the body of the confirmation dialog: which backup, what the restore does to the shared database,
    /// and what TermPoint keeps and does afterwards.
    /// </summary>
    /// <param name="timestampDisplay">The backup's display time, as listed in the panel (for example "Oct 13, 2026 1:00 PM").</param>
    /// <param name="databaseName">The database's file name without its extension — the start of the kept copy's name.</param>
    /// <returns>The dialog body; paragraphs are separated by a blank line.</returns>
    public static string BuildConfirmMessage(string timestampDisplay, string databaseName)
        => $"Restore from backup:\n\n{timestampDisplay}\n\n" +
           "This replaces the database for everyone who uses it. " +
           "TermPoint first saves your current changes, then keeps a copy of the database as it is now beside it, " +
           $"named \"{databaseName} (before restore …)\". TermPoint then restarts.\n\n" +
           "Continue?";

    /// <summary>
    /// Maps a restore result to the message the user is shown: a title and a body. Defined for EVERY
    /// <see cref="RestoreOutcome"/>; an undefined value (which cannot be produced) is treated as
    /// <see cref="RestoreOutcome.Failed"/>, so the user always gets a sentence that is true — nothing changed.
    /// </summary>
    /// <param name="result">The result of the restore.</param>
    /// <returns>
    /// The title and body. For <see cref="RestoreResult.TargetUnchanged"/> outcomes the body always contains
    /// "Your database is unchanged."; for the two other outcomes it names the pre-restore file by file name.
    /// </returns>
    public static (string Title, string Message) Describe(RestoreResult result)
        => result.Outcome switch
        {
            RestoreOutcome.Restored => (RestoredTitle,
                "The backup was restored. The database as it was just before the restore is kept beside it as " +
                $"{PreRestoreFileReference(result)}. TermPoint will now restart."),

            RestoreOutcome.RenameUnconfirmed => (NotConfirmedTitle,
                "The connection was lost at the last step, so TermPoint can't tell whether the restore finished. " +
                "Nothing is lost: your changes were saved first, and a copy of the database as it was then is kept as " +
                $"{PreRestoreFileReference(result)}. TermPoint will now close. " +
                "Reopen it once the connection is back to see which version is in place."),

            RestoreOutcome.NotWriter => (NotStartedTitle,
                $"{ReadOnlyNote} Your database is unchanged."),

            RestoreOutcome.Busy => (NotStartedTitle,
                "A save is in progress, so the restore wasn't started. Your database is unchanged. " +
                "Try again in a moment."),

            RestoreOutcome.Unreachable => (StoppedTitle,
                "The database's location stopped responding, so the restore was stopped. Your database is unchanged. " +
                "Check the network connection and try again."),

            RestoreOutcome.BackupDamaged => (StoppedTitle,
                "That backup is damaged, so it can't be restored. Your database is unchanged."),

            RestoreOutcome.NotTermPointDatabase => (StoppedTitle,
                "That file isn't a TermPoint database, so it can't be restored. Your database is unchanged."),

            RestoreOutcome.BackupUnreadable => (StoppedTitle,
                "TermPoint couldn't read that backup — it may have been moved or deleted, or another program has it open — " +
                "so it can't be restored. Your database is unchanged."),

            RestoreOutcome.TargetChanged => (StoppedTitle,
                "The database was changed by someone else just before the restore, so the restore was stopped " +
                "to protect their changes. Your database is unchanged."),

            RestoreOutcome.FileInUse => (StoppedTitle,
                "The database file is in use by another program (often antivirus, backup or sync software), " +
                "so the restore was stopped. Your database is unchanged. Try again in a moment."),

            RestoreOutcome.LockLost => (StoppedTitle,
                "TermPoint no longer holds the editing lock on this database, so the restore was stopped. " +
                "Your database is unchanged."),

            // Failed, and anything unforeseen: the one thing that is always true is that nothing changed.
            _ => (StoppedTitle,
                "Something went wrong, so the restore was stopped. Your database is unchanged.")
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// How a message refers to the kept pre-restore file: its file name in quotes — never the folder, which on a
    /// share is long, means nothing to the reader, and may reveal a user or server name. Falls back to a generic
    /// phrase when the path is missing.
    /// </summary>
    /// <param name="result">A result with a pre-restore copy (<see cref="RestoreOutcome.Restored"/> or <see cref="RestoreOutcome.RenameUnconfirmed"/>).</param>
    /// <returns>A noun phrase: <c>"name.db"</c> in quotes, or the fallback phrase.</returns>
    private static string PreRestoreFileReference(RestoreResult result)
    {
        var fileName = string.IsNullOrWhiteSpace(result.PreRestoreCopyPath)
            ? null
            : Path.GetFileName(result.PreRestoreCopyPath);

        return string.IsNullOrEmpty(fileName) ? PreRestoreFileFallback : $"\"{fileName}\"";
    }
}

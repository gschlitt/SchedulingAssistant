namespace TermPoint.Services;

/// <summary>
/// Every user-facing text of "Restore from backup" (spec item 5, steps 5b and 5c): the confirmations, the wait
/// dialog, the panel's description and read-only note, the database chooser's destination status lines, and one
/// message for each <see cref="RestoreOutcome"/> in each place the user can meet it (<see cref="RestoreContext"/>).
/// Kept out of <c>MainWindow</c> and the view models so the wording — including the rule that a message names the
/// pre-restore file by its NAME only, never by its folder — can be unit-tested without any UI. Modelled on
/// <see cref="StaleLockPrompt"/>.
///
/// <para><b>The promise the wording keeps.</b> Every outcome except <see cref="RestoreOutcome.Restored"/>,
/// <see cref="RestoreOutcome.RenameUnconfirmed"/> and <see cref="RestoreOutcome.Created"/> leaves the destination
/// exactly as it was, and says so in so many words ("Your database is unchanged."; on the chooser's create path,
/// where there was no database to leave alone, "Nothing was created."). The outcomes that may have replaced the
/// database say what was kept and where, and what TermPoint does next.</para>
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

    // ── Fixed texts: the database chooser (step 5c) ───────────────────────────

    /// <summary>Title of the chooser's confirmation, shown only when the restore would replace an existing database.</summary>
    public const string ChooserConfirmTitle = "Restore from Backup";

    /// <summary>
    /// The chooser's reassurance under the destination status: restoring only ever reads the backup. (Kept as its
    /// own line so it is shown whatever the status says.)
    /// </summary>
    public const string BackupNotModifiedNote = "Your backup file will not be modified.";

    /// <summary>The chooser's destination status while the destination is being probed. The Restore button waits for the answer.</summary>
    public const string DestinationCheckingText = "Checking this location…";

    /// <summary>The chooser's destination status when nothing is at the destination: the restore creates a new database.</summary>
    public const string DestinationMissingText = "A new database will be created here from the backup.";

    /// <summary>The chooser's destination status when the destination did not answer in time. The Restore button stays disabled.</summary>
    public const string DestinationUnreachableText = "This location isn't responding right now.";

    /// <summary>The chooser's destination status when the destination is the backup file itself (restoring a file onto itself is never allowed).</summary>
    public const string DestinationIsBackupFileText = "That's the backup file itself — choose a different folder.";

    /// <summary>The chooser's in-progress line, shown while the restore runs (the chooser's controls are disabled meanwhile).</summary>
    public const string RestoringText = "Restoring the backup…";

    /// <summary>The sentence that ends every message about a refused or failed restore into an existing database.</summary>
    private const string DatabaseUnchanged = "Your database is unchanged.";

    /// <summary>The sentence that replaces <see cref="DatabaseUnchanged"/> on the chooser's create path, where there was no database to leave alone.</summary>
    private const string NothingCreated = "Nothing was created.";

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
    /// Builds the body of the chooser's confirmation, shown only when the destination already holds a database:
    /// which backup, which database it replaces and where, and what TermPoint keeps. (Creating a new file needs no
    /// confirmation.) Unlike the Save &amp; Backup confirmation there is no "saves your changes" and no restart:
    /// nobody has the database open here, and it simply opens afterwards.
    /// </summary>
    /// <param name="backupFileName">The backup's file name (not its folder).</param>
    /// <param name="targetPath">Full path of the database that would be replaced.</param>
    /// <returns>The dialog body; paragraphs are separated by a blank line.</returns>
    public static string BuildChooserConfirmMessage(string backupFileName, string targetPath)
    {
        var targetFileName = Path.GetFileName(targetPath);
        var targetFolder   = Path.GetDirectoryName(targetPath);
        var name           = Path.GetFileNameWithoutExtension(targetPath);

        return $"Restore from backup:\n\n{backupFileName}\n\n" +
               $"This replaces \"{targetFileName}\" in {targetFolder} for everyone who uses it. " +
               "TermPoint keeps a copy of the database as it is now beside it, " +
               $"named \"{name} (before restore …)\".\n\n" +
               "Continue?";
    }

    /// <summary>
    /// The chooser's step-2 status for a destination that already holds a database: what the restore does to it
    /// and what is kept.
    /// </summary>
    /// <param name="destinationPath">Full path of the destination; its file name without extension starts the kept copy's name.</param>
    /// <returns>One sentence-pair, ready to show.</returns>
    public static string BuildDestinationExistsText(string destinationPath)
        => "This replaces the database at this location for everyone who uses it. " +
           $"The current file is kept beside it as \"{Path.GetFileNameWithoutExtension(destinationPath)} (before restore …)\".";

    /// <summary>
    /// Maps a restore result to the message the user is shown: a title and a body. Defined for EVERY
    /// <see cref="RestoreOutcome"/> in every <see cref="RestoreContext"/>; an undefined value (which cannot be
    /// produced) is treated as <see cref="RestoreOutcome.Failed"/>, so the user always gets a sentence that is
    /// true — nothing changed.
    ///
    /// <para>The Save &amp; Backup wording (the default context) is the approved text of step 5b and must not
    /// change. The chooser differs where the situation does: nobody is restarting (the database opens), a
    /// refusal can name the person editing, and on the create path an unchanged destination reads "Nothing was
    /// created." because there was no database there to leave alone.</para>
    /// </summary>
    /// <param name="result">The result of the restore.</param>
    /// <param name="context">Where the message will be shown; defaults to the Save &amp; Backup panel.</param>
    /// <returns>
    /// The title and body. For <see cref="RestoreResult.TargetUnchanged"/> outcomes the body says the database is
    /// unchanged (or, in the chooser on the create path, that nothing was created); for the outcomes that may have
    /// replaced the database it names the pre-restore file by file name.
    /// </returns>
    public static (string Title, string Message) Describe(RestoreResult result, RestoreContext context = RestoreContext.SaveAndBackup)
    {
        var chooser   = context == RestoreContext.Chooser;
        var unchanged = chooser && result.CreatePath ? NothingCreated : DatabaseUnchanged;

        return result.Outcome switch
        {
            RestoreOutcome.Restored => (RestoredTitle, chooser
                ? "The backup was restored. The database as it was just before the restore is kept beside it as " +
                  $"{PreRestoreFileReference(result)}. It will open now."
                : "The backup was restored. The database as it was just before the restore is kept beside it as " +
                  $"{PreRestoreFileReference(result)}. TermPoint will now restart."),

            RestoreOutcome.RenameUnconfirmed => (NotConfirmedTitle, chooser
                ? "The connection was lost at the last step, so TermPoint can't tell whether the restore finished. " +
                  $"Nothing is lost: a copy of the database as it was is kept as {PreRestoreFileReference(result)}. " +
                  "Open the database once the connection is back to see which version is in place."
                : "The connection was lost at the last step, so TermPoint can't tell whether the restore finished. " +
                  "Nothing is lost: your changes were saved first, and a copy of the database as it was then is kept as " +
                  $"{PreRestoreFileReference(result)}. TermPoint will now close. " +
                  "Reopen it once the connection is back to see which version is in place."),

            RestoreOutcome.Created => (RestoredTitle, chooser
                ? "A new database was created from the backup. It will open now."
                : "A new database was created from the backup."),

            RestoreOutcome.AlreadyExists => (StoppedTitle, chooser
                ? "A file appeared at this location while the backup was being prepared, so nothing was created. " +
                  "Choose the location again."
                : $"A file already exists at that location, so nothing was created. {unchanged}"),

            RestoreOutcome.LockHeld => (NotStartedTitle, chooser
                ? $"{StaleLockPrompt.DescribeHolder(result.Holder)} is editing this database, so it can't be restored right now. " +
                  "Restore it after they've closed it. If they're no longer using it, open the database normally " +
                  "(TermPoint will offer to take over), then restore from Configuration → Save & Backup."
                : $"{StaleLockPrompt.DescribeHolder(result.Holder)} is editing this database, so it can't be restored right now. {unchanged}"),

            RestoreOutcome.NotWriter => (NotStartedTitle,
                $"{ReadOnlyNote} {unchanged}"),

            RestoreOutcome.Busy => (NotStartedTitle,
                $"A save is in progress, so the restore wasn't started. {unchanged} Try again in a moment."),

            RestoreOutcome.Unreachable => (StoppedTitle,
                $"The database's location stopped responding, so the restore was stopped. {unchanged} " +
                "Check the network connection and try again."),

            RestoreOutcome.BackupDamaged => (StoppedTitle,
                $"That backup is damaged, so it can't be restored. {unchanged}"),

            RestoreOutcome.NotTermPointDatabase => (StoppedTitle,
                $"That file isn't a TermPoint database, so it can't be restored. {unchanged}"),

            RestoreOutcome.BackupUnreadable => (StoppedTitle,
                "TermPoint couldn't read that backup — it may have been moved or deleted, or another program has it open — " +
                $"so it can't be restored. {unchanged}"),

            RestoreOutcome.TargetChanged => (StoppedTitle,
                "The database was changed by someone else just before the restore, so the restore was stopped " +
                $"to protect their changes. {unchanged}"),

            RestoreOutcome.FileInUse => (StoppedTitle,
                "The database file is in use by another program (often antivirus, backup or sync software), " +
                $"so the restore was stopped. {unchanged} Try again in a moment."),

            RestoreOutcome.LockLost => (StoppedTitle,
                "TermPoint no longer holds the editing lock on this database, so the restore was stopped. " +
                $"{unchanged}"),

            // A failure TermPoint can word precisely (the editing lock file could not be created): say why.
            RestoreOutcome.Failed when !string.IsNullOrWhiteSpace(result.Detail) => (NotStartedTitle,
                $"The restore wasn't started. {WithoutBannerPrefix(result.Detail!)} {unchanged}"),

            // Failed, and anything unforeseen: the one thing that is always true is that nothing changed.
            _ => (StoppedTitle,
                $"Something went wrong, so the restore was stopped. {unchanged}")
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The read-only banner prefix that <see cref="WriteLockService.LockWriteError"/> texts start with (they were
    /// written for the "opened read-only" banner). Inside a restore message it reads wrongly, so it is removed.
    /// </summary>
    private const string BannerPrefix = "Read-only —";   // no trailing space: the detail is trimmed first

    /// <summary>
    /// Trims <paramref name="detail"/> and removes a leading <see cref="BannerPrefix"/>, capitalising what follows,
    /// so "Read-only — this folder's security settings prevented…" becomes "This folder's security settings
    /// prevented…". Text without the prefix is returned trimmed and otherwise unchanged.
    /// </summary>
    /// <param name="detail">A failure reason, typically a <see cref="WriteLockService.LockWriteError"/>.</param>
    /// <returns>The reason, ready to sit inside a restore message.</returns>
    internal static string WithoutBannerPrefix(string detail)
    {
        var text = detail.Trim();
        if (!text.StartsWith(BannerPrefix, StringComparison.Ordinal))
            return text;

        var rest = text[BannerPrefix.Length..].TrimStart();
        return rest.Length == 0 ? rest : char.ToUpperInvariant(rest[0]) + rest[1..];
    }

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

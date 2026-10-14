namespace TermPoint.Services;

/// <summary>
/// What the open-time check of the local copy (D' for a writer, D'' for a reader) said (spec item 5, step 5d).
/// </summary>
public enum LocalCopyVerdict
{
    /// <summary>The copy passed its integrity check: open normally.</summary>
    Ok,

    /// <summary>
    /// The copy could not be examined, even after the quiet retries (<c>DatabaseInUseException</c>): another
    /// program on this computer has it open. Says nothing about its health and is never described as damage.
    /// </summary>
    Locked,

    /// <summary>Positive evidence of damage (<c>DatabaseCorruptException</c>).</summary>
    Damaged
}

/// <summary>
/// What a re-check of the SHARED database D (the read-only front-door check) said, once a local copy has failed.
/// </summary>
public enum SharedDatabaseVerdict
{
    /// <summary>D has not been re-checked yet.</summary>
    NotChecked,

    /// <summary>D passes: the local copy is the problem, not the database.</summary>
    Ok,

    /// <summary>D itself is damaged.</summary>
    Damaged,

    /// <summary>D exists but could not be read just now (another program has it open, or access is denied).</summary>
    Unreadable,

    /// <summary>D's location could not be reached.</summary>
    Unreachable,

    /// <summary>D is gone.</summary>
    Missing
}

/// <summary>Where the "one fresh copy" step has got to.</summary>
public enum FreshCopyState
{
    /// <summary>No fresh copy has been made in this open. The one allowed attempt is still available.</summary>
    NotTried,

    /// <summary>A fresh copy was made and the open was retried with it.</summary>
    Made,

    /// <summary>The fresh copy could not be made at all.</summary>
    Failed
}

/// <summary>What the handler of a failed local-copy check must do next.</summary>
public enum LocalCopyAction
{
    /// <summary>The copy passed: open the database normally.</summary>
    OpenNormally,

    /// <summary>
    /// The copy is in use by another program: show "Couldn't Open the Database" with Try again / Close. Try
    /// again re-runs the open (with its quiet retries); Close gives up.
    /// </summary>
    ShowInUseDialog,

    /// <summary>
    /// The damaged copy holds the user's recovered unsaved changes, which must never be deleted: set it aside
    /// (kept, not deleted), tell the user they could not be restored ("Unsaved Changes Lost"), then ask again.
    /// </summary>
    ArchiveRecoveredChangesThenContinue,

    /// <summary>Re-check the shared database D (read-only, the front-door check), then ask again.</summary>
    CheckSharedDatabase,

    /// <summary>
    /// The shared database is damaged too: give up (end the session without saving), say so, and show the
    /// recovery chooser with the damaged reason and Restore from a backup.
    /// </summary>
    ShowDamagedThenRecoveryChooser,

    /// <summary>The shared database cannot be read right now: give up and show the front-door "can't read it" message.</summary>
    ShowUnreadableMessage,

    /// <summary>The shared database cannot be reached: give up and show the network message.</summary>
    ShowUnreachableMessage,

    /// <summary>The shared database has gone: give up and show the "not found" message.</summary>
    ShowMissingMessage,

    /// <summary>The shared database is fine and the copy is damaged: make ONE fresh copy and retry the open with it.</summary>
    MakeFreshCopy,

    /// <summary>
    /// The fresh copy failed too (or could not be made): set the bad copy aside (kept, not deleted), give up, and
    /// show the "keeps failing" message with a single Close button.
    /// </summary>
    ShowKeepsFailing
}

/// <summary>
/// The decision table of the shared handler for a failed open-time check of the local copy (spec item 5, step 5d),
/// as a pure function: no I/O, no UI, no state. <c>MainWindow</c> gathers the inputs (it only asks for the ones a
/// row needs, in the order the rows are evaluated here), performs the action returned, and asks again with the
/// updated inputs until the answer is <see cref="LocalCopyAction.OpenNormally"/> or a give-up.
///
/// <para><b>The rows, in order of evaluation:</b></para>
/// <list type="number">
/// <item><description>The fresh copy could not be made → <see cref="LocalCopyAction.ShowKeepsFailing"/>.</description></item>
/// <item><description>The copy passed → <see cref="LocalCopyAction.OpenNormally"/>.</description></item>
/// <item><description>The copy is locked → <see cref="LocalCopyAction.ShowInUseDialog"/>, whatever else is known:
/// a locked copy is never touched, so nothing else about it matters.</description></item>
/// <item><description>The copy is damaged, and the fresh copy has already been tried → <see cref="LocalCopyAction.ShowKeepsFailing"/>:
/// there is exactly ONE fresh copy per open.</description></item>
/// <item><description>The copy is damaged and holds recovered unsaved changes → <see cref="LocalCopyAction.ArchiveRecoveredChangesThenContinue"/>
/// (before anything else can touch the file).</description></item>
/// <item><description>The copy is damaged: the shared database decides — not yet checked →
/// <see cref="LocalCopyAction.CheckSharedDatabase"/>; damaged → <see cref="LocalCopyAction.ShowDamagedThenRecoveryChooser"/>;
/// unreadable / unreachable / missing → the matching message; passes → <see cref="LocalCopyAction.MakeFreshCopy"/>.</description></item>
/// </list>
/// </summary>
public static class LocalCopyCheckDecision
{
    /// <summary>
    /// Decides what to do next.
    /// </summary>
    /// <param name="copy">What the most recent open-time check of the local copy said.</param>
    /// <param name="holdsRecoveredChanges">
    /// True when the copy holds the user's recovered unsaved changes (it has a dirty marker). Only a writer's
    /// copy can; always false for a reader's.
    /// </param>
    /// <param name="shared">What the re-check of the shared database said; <see cref="SharedDatabaseVerdict.NotChecked"/> if not yet done.</param>
    /// <param name="fresh">Where the single fresh-copy attempt has got to.</param>
    /// <returns>The next action.</returns>
    public static LocalCopyAction Decide(
        LocalCopyVerdict copy, bool holdsRecoveredChanges, SharedDatabaseVerdict shared, FreshCopyState fresh)
    {
        // A fresh copy that could not even be made ends the attempt, whatever the copy last said.
        if (fresh == FreshCopyState.Failed)
            return LocalCopyAction.ShowKeepsFailing;

        if (copy == LocalCopyVerdict.Ok)
            return LocalCopyAction.OpenNormally;

        // Locked is "busy", not "damaged": the file is never touched and nothing else is asked.
        if (copy == LocalCopyVerdict.Locked)
            return LocalCopyAction.ShowInUseDialog;

        // The copy is damaged. The one fresh copy has been tried and the new copy failed its check as well.
        if (fresh == FreshCopyState.Made)
            return LocalCopyAction.ShowKeepsFailing;

        // Unsaved work comes first: set the copy aside before anything can replace or delete it.
        if (holdsRecoveredChanges)
            return LocalCopyAction.ArchiveRecoveredChangesThenContinue;

        return shared switch
        {
            SharedDatabaseVerdict.NotChecked  => LocalCopyAction.CheckSharedDatabase,
            SharedDatabaseVerdict.Damaged     => LocalCopyAction.ShowDamagedThenRecoveryChooser,
            SharedDatabaseVerdict.Unreadable  => LocalCopyAction.ShowUnreadableMessage,
            SharedDatabaseVerdict.Unreachable => LocalCopyAction.ShowUnreachableMessage,
            SharedDatabaseVerdict.Missing     => LocalCopyAction.ShowMissingMessage,
            _                                 => LocalCopyAction.MakeFreshCopy
        };
    }
}

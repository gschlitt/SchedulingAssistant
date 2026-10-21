namespace TermPoint.Tests.Harness;

/// <summary>
/// Thrown by the multi-computer harness (spec item 9.3, step 9.3b) when a walk breaks an invariant it did not declare
/// (see <see cref="World.ExpectFindings"/>), or when the findings that occurred differ from the declared ones at the
/// end of the walk. The message IS the printed story (see <see cref="Story"/>): what each person did, the dialogs they
/// saw, where every edit is, and the violations.
/// </summary>
internal sealed class HarnessViolationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="story">The printed story, which becomes the message.</param>
    public HarnessViolationException(string story) : base(story) { }
}

/// <summary>What kind of breach a <see cref="Violation"/> is. The classifier (<see cref="Findings"/>) reads it.</summary>
internal enum ViolationKind
{
    /// <summary>I1: edits that were in the shared database are no longer in it.</summary>
    SharedEditsDisappeared,

    /// <summary>I2: an edit a computer applied exists nowhere that survives a crash.</summary>
    EditLost,

    /// <summary>I2 (separate message): an edit survives only in a support archive (<c>*.corrupt-*</c>).</summary>
    EditOnlyInSupportArchive,

    /// <summary>I3: the shared database fails <c>PRAGMA integrity_check</c> (or cannot be opened at all).</summary>
    IntegrityCheckFailed,

    /// <summary>I4: a rename onto the shared database happened while the saver did not hold the lock or the database was not the version it last saw.</summary>
    WriteWithoutLockOrOnStaleVersion,

    /// <summary>I5: a message promised something that did not come true.</summary>
    BrokenPromise,

    /// <summary>I5: a message that mentions kept / restore / saved / safe matches nothing in the promise catalogue.</summary>
    UnclassifiedPromise,

    /// <summary>I5 (the F7 rule): "Restore Unsaved Changes?" was answered Restore and was then followed by "Can't Restore Unsaved Changes".</summary>
    RestoreOfferedThenImpossible,

    /// <summary>I6 (a): a session whose checkout is read-only still holds its database's lock (the lock service says writer, or the lock file names the session).</summary>
    ReadOnlySessionHoldsLock,

    /// <summary>I6 (b): a lost-lock demotion carried on against a database other than the one it started on.</summary>
    DemotionOnAnotherDatabase,

    /// <summary>A paused operation, or the event run while it was held, did not finish within the real-time limit (step 9.3c-2b): the harness abandoned the stuck work.</summary>
    Hang
}

/// <summary>
/// One breach of an invariant, found at the end of a funnel step. A violation that is still true on later steps is
/// reported once (its <see cref="Key"/> is remembered).
/// </summary>
internal sealed class Violation
{
    /// <summary>The funnel step at whose end the breach was found (0 = before any step).</summary>
    public required int Step { get; init; }

    /// <summary>The invariant: "I1" to "I6", or "Hang" for a pause that did not finish.</summary>
    public required string Invariant { get; init; }

    /// <summary>What kind of breach it is.</summary>
    public required ViolationKind Kind { get; init; }

    /// <summary>One line, in plain words, saying what is wrong.</summary>
    public required string Reason { get; init; }

    /// <summary>Identity of the breach, so one that stays true is reported once.</summary>
    public required string Key { get; init; }

    /// <summary>The computer the breach concerns (whose edit, whose message), or null.</summary>
    public string? Computer { get; init; }

    /// <summary>The edit the breach concerns, or null.</summary>
    public string? Edit { get; init; }

    /// <summary>For <see cref="ViolationKind.BrokenPromise"/>: the kind of promise that broke.</summary>
    public PromiseKind? Promise { get; init; }

    /// <summary>For <see cref="ViolationKind.WriteWithoutLockOrOnStaleVersion"/>: the crash point at which it was seen.</summary>
    public string? CrashPoint { get; init; }

    /// <summary>
    /// For <see cref="ViolationKind.RestoreOfferedThenImpossible"/>: true when the shared database's set of edits had NOT
    /// changed since the computer's work was kept (nobody else changed any data, so the refusal came from the database
    /// file's bytes alone: finding F5), false when someone did change data (F7). Null for every other kind.
    /// </summary>
    public bool? DataUnchangedSinceKept { get; init; }

    /// <summary>
    /// For <see cref="ViolationKind.RestoreOfferedThenImpossible"/>: true when every edit the computer made that sits in
    /// its kept working file is already in the shared database (its own save landed and the crash came before the
    /// unsaved-changes marker was deleted: finding F8), false otherwise. Null for every other kind.
    /// </summary>
    public bool? AllKeptEditsAlreadyShared { get; init; }

    /// <summary>For <see cref="ViolationKind.SharedEditsDisappeared"/>: the edits that were in the shared database and are not any more (empty for every other kind).</summary>
    public IReadOnlyList<string> LostEdits { get; init; } = Array.Empty<string>();

    /// <summary>
    /// For <see cref="ViolationKind.SharedEditsDisappeared"/>: true when every lost edit was committed to the shared
    /// database, by another computer, while a save of the paused computer was held before its rename, in this very step
    /// (the save's rename then landed over them: finding F12). Null for every other kind.
    /// </summary>
    public bool? LostEditsCommittedDuringHeldSave { get; init; }

    /// <summary>
    /// For <see cref="ViolationKind.EditLost"/>: true when the lost edit was applied by its computer WHILE that computer's own
    /// operation was held in the edit-loss window (<see cref="PausePoints.EditLossWindow"/>: from a save's snapshot to the cleanup of the
    /// working copy; the edit missed the save, and what protected it was thrown away: the marker, or for a close or switch the whole working copy: finding F2). Null for every other kind.
    /// </summary>
    public bool? EditAppliedDuringHeldSave { get; init; }

    /// <summary>The dialogs the concerned computer was asked during the step (for classification).</summary>
    public IReadOnlyList<PromptCall> ComputerPrompts { get; init; } = Array.Empty<PromptCall>();

    /// <summary>The finding id the classifier gave it: "F1".."F16" or "new". Set when the violation is recorded.</summary>
    public string FindingId { get; set; } = Findings.NewFinding;
}

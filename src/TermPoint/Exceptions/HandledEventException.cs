namespace TermPoint.Exceptions;

/// <summary>
/// The kinds of handled event TermPoint reports to the developers (spec item 7): problems the application dealt with,
/// and that the user has already been told about or needs no message for, but whose frequency in the field matters.
/// Each kind is reported at most once per run (<see cref="Services.HandledEventReporter"/>).
/// </summary>
public enum HandledEvent
{
    /// <summary>A backup was skipped: the backup folder was missing or did not respond.</summary>
    BackupFolderUnavailable,

    /// <summary>A backup failed: the copy to the backup folder stalled.</summary>
    BackupCopyStalled,

    /// <summary>A save was refused because the shared database was changed elsewhere since this session read it.</summary>
    SaveRefusedDatabaseChanged,

    /// <summary>The write lock was taken over by another session.</summary>
    LockLostTakenOver,

    /// <summary>The write lock file was removed or became unreadable, and could not be put back.</summary>
    LockLostFileRemoved,

    /// <summary>Kept changes could not be restored: the shared database no longer exists.</summary>
    RecoveryConflictSourceMissing,

    /// <summary>Kept changes could not be restored: their marker carried no fingerprint, or the kept file was missing.</summary>
    RecoveryConflictNoVerifiableMarker,

    /// <summary>Kept changes could not be restored: the shared database was changed since they were kept.</summary>
    RecoveryConflictSourceChanged,

    /// <summary>A save's copy at the database location did not match the local snapshot (a torn transfer).</summary>
    SaveTornTransfer,

    /// <summary>A save gave up because the database location did not respond.</summary>
    SaveLocationNotResponding,

    /// <summary>Opening for editing: the copy of the shared database never matched it, even after the retry.</summary>
    CheckoutCopyMismatch,

    /// <summary>A reader's Refresh failed because the database location did not respond.</summary>
    RefreshNotResponding,

    /// <summary>A reader's Refresh failed and lost its connection to the database.</summary>
    RefreshConnectionLost,

    /// <summary>The previous TermPoint session on this computer ended without exiting normally (crash, force-quit, power loss).</summary>
    PreviousSessionEndedUnexpectedly,
}

/// <summary>
/// The named exception of a HANDLED-EVENT REPORT (spec item 7): never thrown to anyone (the event has been dealt
/// with); it exists so BugSnag receives an error that has a name, groups sensibly and carries a stack trace of where
/// the event was found. <see cref="Services.HandledEventReporter"/> creates it and hands it to
/// <see cref="Services.IAppLogger.ReportHandled"/>. Modelled on <see cref="SaveFailedException"/>.
///
/// <para><b>Privacy.</b> The message is fixed text per kind (<see cref="MessageFor"/>): no path, name or original
/// message, and no <see cref="Exception.InnerException"/> (BugSnag sends inner exceptions, and their messages carry
/// paths).</para>
/// </summary>
public sealed class HandledEventException : Exception
{
    /// <summary>The kind of event reported.</summary>
    public HandledEvent Kind { get; }

    /// <param name="kind">The kind of event reported.</param>
    public HandledEventException(HandledEvent kind) : base(MessageFor(kind)) => Kind = kind;

    /// <summary>The fixed message for <paramref name="kind"/>, so every report of one kind reads and groups the same.</summary>
    /// <param name="kind">The kind of event.</param>
    /// <returns>The message, e.g. "Save refused: the database was changed elsewhere".</returns>
    public static string MessageFor(HandledEvent kind) => kind switch
    {
        HandledEvent.BackupFolderUnavailable            => "Backup skipped: the backup folder was not available",
        HandledEvent.BackupCopyStalled                  => "Backup failed: the copy to the backup folder stalled",
        HandledEvent.SaveRefusedDatabaseChanged         => "Save refused: the database was changed elsewhere",
        HandledEvent.LockLostTakenOver                  => "Write lock lost: taken over by another session",
        HandledEvent.LockLostFileRemoved                => "Write lock lost: the lock file was removed or unreadable",
        HandledEvent.RecoveryConflictSourceMissing      => "Kept changes not restored: the database no longer exists",
        HandledEvent.RecoveryConflictNoVerifiableMarker => "Kept changes not restored: no verifiable marker or kept file",
        HandledEvent.RecoveryConflictSourceChanged      => "Kept changes not restored: the database was changed since",
        HandledEvent.SaveTornTransfer                   => "Save failed: the copy at the database location did not match",
        HandledEvent.SaveLocationNotResponding          => "Save failed: the database location did not respond",
        HandledEvent.CheckoutCopyMismatch               => "Open failed: the copy of the database never matched it",
        HandledEvent.RefreshNotResponding               => "Refresh failed: the database location did not respond",
        HandledEvent.RefreshConnectionLost              => "Refresh failed and lost the connection to the database",
        HandledEvent.PreviousSessionEndedUnexpectedly   => "The previous session ended without exiting normally",
        _                                               => "Handled event: " + kind,
    };
}

namespace TermPoint.Exceptions;

/// <summary>
/// The step of a save that failed (spec item 12). Used by <see cref="SaveFailedException"/> to name the failure in a
/// handled report, and as the key that throttles reports to one per step per session.
/// </summary>
public enum SaveStep
{
    /// <summary>An exception nobody anticipated, caught by the safety net in <c>SaveAsync</c>.</summary>
    Unexpected,

    /// <summary>Taking the local snapshot of the session's database copy failed.</summary>
    Snapshot,

    /// <summary>Hashing the local snapshot failed.</summary>
    SnapshotHash,

    /// <summary>Copying the snapshot to the temporary file beside the shared database failed.</summary>
    CopyToLocation,

    /// <summary>Reading the temporary file back to verify it failed.</summary>
    VerifyAtLocation,

    /// <summary>Renaming the temporary file onto the shared database failed.</summary>
    Rename,

    /// <summary>Writing the crash-protection (dirty) marker failed.</summary>
    CrashProtectionMarker
}

/// <summary>
/// The named exception of a SAVE-FAILURE REPORT (spec item 12): never thrown to anyone — the save already failed,
/// the user already has a banner — it exists so BugSnag receives an error that has a name and groups sensibly.
/// <c>CheckoutService</c> creates it and hands it to <see cref="Services.IAppLogger.ReportHandled"/>.
/// Modelled on <see cref="DatabaseDamagedException"/>.
///
/// <para><b>Privacy.</b> The message is fixed text per step (see <see cref="MessageFor"/>) plus, when there is one,
/// the TYPE NAME of the original exception. It never carries the original's message and has no
/// <see cref="Exception.InnerException"/>: BugSnag sends inner exceptions, and their messages carry paths.</para>
/// </summary>
public sealed class SaveFailedException : Exception
{
    /// <summary>The step of the save that failed.</summary>
    public SaveStep Step { get; }

    /// <summary>The original exception's type name (e.g. "IOException"), or null when there was no exception.</summary>
    public string? OriginalType { get; }

    /// <param name="step">The step of the save that failed.</param>
    /// <param name="originalType">The original exception's <c>GetType().Name</c>, or null.</param>
    public SaveFailedException(SaveStep step, string? originalType = null)
        : base(MessageFor(step) + (originalType is null ? string.Empty : " (" + originalType + ")"))
    {
        Step         = step;
        OriginalType = originalType;
    }

    /// <summary>
    /// The fixed message for <paramref name="step"/>, so every report of one step reads — and groups — the same.
    /// </summary>
    /// <param name="step">The step that failed.</param>
    /// <returns>The message, e.g. "Save failed while copying to the database location".</returns>
    public static string MessageFor(SaveStep step) => step switch
    {
        SaveStep.Unexpected             => "Save failed with an unexpected exception",
        SaveStep.Snapshot               => "Save failed while taking the local snapshot",
        SaveStep.SnapshotHash           => "Save failed while hashing the local snapshot",
        SaveStep.CopyToLocation         => "Save failed while copying to the database location",
        SaveStep.VerifyAtLocation       => "Save failed while verifying the copy at the database location",
        SaveStep.Rename                 => "Save failed while renaming onto the database",
        SaveStep.CrashProtectionMarker  => "Save could not write the crash-protection marker",
        _                               => "Save failed"
    };

    /// <summary>
    /// Builds the exception for a report with a real stack trace (it is thrown and caught on the spot): an exception
    /// that was never thrown has none, and the report would show where nothing happened.
    /// </summary>
    /// <param name="step">The step that failed.</param>
    /// <param name="originalType">The original exception's type name, or null.</param>
    /// <returns>The exception, carrying a stack trace of the reporting call.</returns>
    public static SaveFailedException Create(SaveStep step, string? originalType)
    {
        try
        {
            throw new SaveFailedException(step, originalType);
        }
        catch (SaveFailedException ex)
        {
            return ex;
        }
    }
}

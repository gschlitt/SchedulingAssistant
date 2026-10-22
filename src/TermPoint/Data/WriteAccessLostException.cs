namespace TermPoint.Data;

/// <summary>
/// Thrown by <see cref="IDatabaseContext.MarkDirty"/> (every repository write calls it first) when the
/// session is read-only: this user no longer holds the write lock, or never did. The write is refused BEFORE
/// it reaches the database, so nothing is written into the throw-away read-only copy and the change is not
/// silently lost.
///
/// <para><b>One message, in plain words.</b> <see cref="Exception.Message"/> is exactly the sentence the user
/// should read (<see cref="PlainMessage"/>). Error sites that show <c>ex.Message</c> therefore show the right
/// text with no extra code, and <see cref="Services.WriteFailureText"/> picks it for sites that otherwise show a
/// fixed fallback. The loggers recognise this exception (<see cref="Services.WriteFailureText.IsWriteAccessLost"/>)
/// and keep it out of the crash reports and the developer-label banner: a refused write is the program working as
/// designed, not a bug.</para>
///
/// <para>Lives in <c>Data/</c> (not <c>Exceptions/</c>) because the shared view models and loggers refer to it
/// and the browser build compiles this folder but not <c>Exceptions/</c>.</para>
/// </summary>
public sealed class WriteAccessLostException : Exception
{
    /// <summary>
    /// The exact text shown to the user: it says what happened, that the change was not saved, and (by
    /// implication, from the banner the lock-loss handler already showed) what to do next.
    /// </summary>
    public const string PlainMessage = "You no longer have write access, so this change wasn't saved.";

    /// <summary>Creates the exception with <see cref="PlainMessage"/> as its message.</summary>
    public WriteAccessLostException() : base(PlainMessage)
    {
    }
}

using TermPoint.Data;

namespace TermPoint.Services;

/// <summary>
/// Chooses what a user sees when a save fails. A refused write in a read-only session
/// (<see cref="WriteAccessLostException"/>) has one fixed, plain sentence, shown as the app banner; every other
/// failure keeps whatever inline text the call site already showed. Shared by the view models' catch blocks, the
/// loggers and the global unhandled-exception handler, so the "no write access" rule lives in exactly one place.
///
/// <para><b>Why the banner and not the inline error line.</b> The management views hide their inline error line
/// while an editor is open (it sits in the list panel, which is collapsed during editing), and a refused Apply
/// keeps the editor open, so an inline message would never be seen. This was found in the F4 hand test
/// (2026-10-21). <see cref="FileAppLogger.LogError"/>, which every catch block calls first, shows the banner for
/// this exception; <see cref="For"/> then gives the call site no inline text, so the user sees the message
/// exactly once.</para>
/// </summary>
public static class WriteFailureText
{
    /// <summary>
    /// True when <paramref name="ex"/>, or anything in its inner-exception chain (including the branches of an
    /// <see cref="AggregateException"/>), is a <see cref="WriteAccessLostException"/>.
    /// </summary>
    /// <param name="ex">The exception to inspect; null is allowed and gives false.</param>
    /// <returns>True when a refused read-only write is part of the failure.</returns>
    public static bool IsWriteAccessLost(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is WriteAccessLostException) return true;
            if (e is AggregateException agg && agg.InnerExceptions.Any(IsWriteAccessLost)) return true;
        }
        return false;
    }

    /// <summary>
    /// The inline text to show for a failed save: null when the failure is a refused read-only write (the logger's
    /// banner already carries the one plain message, see the class remarks), otherwise <paramref name="fallback"/>.
    /// </summary>
    /// <param name="ex">The exception the catch block caught (after it has called <c>App.Logger.LogError</c>).</param>
    /// <param name="fallback">The text the site showed before (for example "The save could not be completed. Please try again.").</param>
    /// <returns>The text to put in the site's inline error, or null for none.</returns>
    public static string? For(Exception? ex, string fallback)
        => IsWriteAccessLost(ex) ? null : fallback;

    /// <summary>
    /// For the global unhandled-exception handler, which does not go through <see cref="FileAppLogger.LogError"/>:
    /// when the failure is a refused read-only write, tells the user with the one plain banner
    /// (<see cref="IAppLogger.NotifyUser"/>: no crash report, no developer label).
    /// </summary>
    /// <param name="logger">The logger whose banner to use (normally <c>App.Logger</c>).</param>
    /// <param name="ex">The exception that reached the handler.</param>
    /// <returns>True when it was a refused read-only write and the user has been told; false (nothing done) otherwise.</returns>
    public static bool NotifyIfWriteAccessLost(IAppLogger logger, Exception? ex)
    {
        if (!IsWriteAccessLost(ex)) return false;
        logger.NotifyUser(WriteAccessLostException.PlainMessage);
        return true;
    }
}

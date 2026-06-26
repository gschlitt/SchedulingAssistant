namespace TermPoint.Services;

/// <summary>
/// Application-wide error logger.
/// Implementations can write to a local file, a remote database, a cloud sink, etc.
/// All methods are non-throwing in production. When <see cref="ThrowOnError"/> is true
/// (dev/debug use only), <see cref="LogError"/> re-throws the exception after logging it
/// so that it surfaces immediately in the debugger rather than being silently swallowed.
/// </summary>
public interface IAppLogger
{
    /// <summary>
    /// When true, <see cref="LogError"/> re-throws the original exception (preserving its
    /// stack trace) instead of firing the notification event. Intended for development use
    /// only — keeps the debugger break signal clean and avoids a confusing banner alongside
    /// the exception. Defaults to false.
    /// </summary>
    bool ThrowOnError { get; set; }

    /// <summary>
    /// Fired after every <see cref="LogError"/> call (when <see cref="ThrowOnError"/> is
    /// false) with a short user-readable message (the <c>context</c> if provided, otherwise
    /// the exception message). <see cref="AppNotificationService"/> subscribes to this so
    /// logged errors are automatically surfaced in the main-window notification banner.
    /// Not fired when <see cref="ThrowOnError"/> is true — the re-throw is the signal.
    /// </summary>
    event EventHandler<string>? ErrorLogged;

    /// <summary>Log an exception with an optional human-readable context message.</summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="context">Short human-readable description shown in the notification banner.</param>
    /// <param name="unhandled">
    /// True when the exception escaped all application-level catch blocks and was caught
    /// by a global handler (AppDomain, TaskScheduler, Dispatcher). BugSnag uses this to
    /// distinguish crashes from handled errors in its stability score.
    /// </param>
    void LogError(Exception? ex, string? context = null, bool unhandled = false);

    /// <summary>Log a plain message (no exception).</summary>
    void LogWarning(string message, string? context = null);

    /// <summary>Log an informational message.</summary>
    void LogInfo(string message, string? context = null);

    /// <summary>
    /// Records a breadcrumb — a short, timestamped note describing a significant event.
    /// BugSnag attaches the last 25 breadcrumbs to every error report, providing a
    /// chronological trail of what happened before the crash. No-op in environments
    /// without BugSnag (e.g. WASM).
    /// </summary>
    /// <param name="message">Short human-readable description of the event (e.g. "Checkout: WriteAccess").</param>
    /// <param name="metadata">Optional key-value pairs with additional context (e.g. path, outcome).</param>
    void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null);
}

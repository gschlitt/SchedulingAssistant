namespace SchedulingAssistant.Services;

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
    void LogError(Exception? ex, string? context = null);

    /// <summary>Log a plain message (no exception).</summary>
    void LogWarning(string message, string? context = null);

    /// <summary>Log an informational message.</summary>
    void LogInfo(string message, string? context = null);
}

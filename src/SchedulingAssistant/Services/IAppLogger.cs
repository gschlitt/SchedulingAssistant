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
    /// stack trace) after writing to the log. Intended for development use only — never
    /// enable in production. Defaults to false.
    /// </summary>
    bool ThrowOnError { get; set; }

    /// <summary>Log an exception with an optional human-readable context message.</summary>
    void LogError(Exception ex, string? context = null);

    /// <summary>Log a plain message (no exception).</summary>
    void LogWarning(string message, string? context = null);

    /// <summary>Log an informational message.</summary>
    void LogInfo(string message, string? context = null);
}

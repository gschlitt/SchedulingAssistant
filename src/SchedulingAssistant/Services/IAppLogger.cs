namespace SchedulingAssistant.Services;

/// <summary>
/// Application-wide error logger.
/// Implementations can write to a local file, a remote database, a cloud sink, etc.
/// All methods must be non-throwing — logger failures must never crash the app.
/// </summary>
public interface IAppLogger
{
    /// <summary>Log an exception with an optional human-readable context message.</summary>
    void LogError(Exception ex, string? context = null);

    /// <summary>Log a plain message (no exception).</summary>
    void LogWarning(string message, string? context = null);

    /// <summary>Log an informational message.</summary>
    void LogInfo(string message, string? context = null);
}

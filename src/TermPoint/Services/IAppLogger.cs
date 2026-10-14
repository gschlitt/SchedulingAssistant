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

    /// <summary>
    /// Records a HANDLED problem that is worth knowing about but that the user has already been told about (or
    /// that needs no banner): the application dealt with it, and the report exists so the developers can see how
    /// often it happens and in what circumstances. Examples: a damaged database that was refused and routed to
    /// recovery (spec item 5, step 5e); the not-an-exception problems of spec item 7.
    ///
    /// <para><b>Contract.</b> Always leaves a local log line (text, exception type and the metadata). Where the
    /// implementation has a remote sink (<c>FileAppLogger</c> and BugSnag) the problem is also sent there.
    /// Unlike <see cref="LogError"/> it NEVER re-throws (<see cref="ThrowOnError"/> is ignored: a handled problem
    /// is not a bug to break the debugger on) and NEVER raises <see cref="ErrorLogged"/> (no banner: the caller
    /// shows the user whatever is appropriate). It never throws at all.</para>
    ///
    /// <para><b>Privacy.</b> Everything passed here is sent off the machine by implementations that have a remote
    /// sink: <paramref name="context"/>, <paramref name="metadata"/> and <paramref name="groupingKey"/> must carry
    /// no paths, file names, user names or user data. Callers are responsible for that.</para>
    ///
    /// <para>The default implementation only writes the local log line, so a logger that has no remote sink (and
    /// every test logger) needs no code for it.</para>
    /// </summary>
    /// <param name="ex">
    /// A named exception describing the problem. It need not have been thrown. Prefer a dedicated exception type
    /// with a message that is stable for the kind of problem, so the remote sink groups reports sensibly.
    /// </param>
    /// <param name="context">Short, stable description of what was happening; becomes the report's context.</param>
    /// <param name="metadata">Optional extra key/value details, shown in the report under <paramref name="metadataTab"/>.</param>
    /// <param name="metadataTab">Name of the report tab that holds <paramref name="metadata"/>.</param>
    /// <param name="groupingKey">
    /// Optional stable key that makes the remote sink put every report with the same key into one issue,
    /// whatever the stack trace; null leaves the sink's own grouping.
    /// </param>
    void ReportHandled(
        Exception ex,
        string context,
        IReadOnlyDictionary<string, string>? metadata = null,
        string metadataTab = "details",
        string? groupingKey = null)
        => LogWarning(AppLoggerText.DescribeHandled(ex, metadata), context);

    /// <summary>
    /// Tells the user that something went wrong (the main-window notification banner) and records it in the local
    /// log, WITHOUT sending any report to a remote sink. For problems the user should know about but that are not
    /// bugs, or whose report is made some other way (for example the backup snapshot that failed its integrity
    /// check, which goes through <see cref="ReportHandled"/> with no path in it). Never throws.
    ///
    /// <para>The default implementation only writes the local log line (a logger with no banner, such as the
    /// browser build's, has nowhere to show it).</para>
    /// </summary>
    /// <param name="message">What to tell the user: one short sentence, with no paths or technical detail.</param>
    void NotifyUser(string message)
        => LogWarning(message);
}

/// <summary>
/// Text helpers shared by <see cref="IAppLogger"/>'s default implementations and <c>FileAppLogger</c>, so a
/// handled report reads the same in every logger's local log.
/// </summary>
internal static class AppLoggerText
{
    /// <summary>
    /// One-line description of a handled problem for a logger that has only a plain-text log: the exception's type
    /// and message, then the metadata (see <see cref="FormatMetadata"/>).
    /// </summary>
    /// <param name="ex">The exception that names the problem.</param>
    /// <param name="metadata">Extra key/value details, or null.</param>
    /// <returns>The description; never null, never more than one line.</returns>
    internal static string DescribeHandled(Exception ex, IReadOnlyDictionary<string, string>? metadata)
        => Flatten($"{ex.GetType().Name}: {ex.Message}") + FormatMetadata(metadata);

    /// <summary>
    /// Formats metadata for a log line as <c> [key=value; key=value]</c> (with a leading space), or an empty string
    /// when there is none. Line breaks inside values are flattened to " | " so the entry stays on one line.
    /// </summary>
    /// <param name="metadata">Extra key/value details, or null.</param>
    /// <returns>The formatted text, or an empty string.</returns>
    internal static string FormatMetadata(IReadOnlyDictionary<string, string>? metadata)
        => metadata is { Count: > 0 }
            ? Flatten(" [" + string.Join("; ", metadata.Select(kv => $"{kv.Key}={kv.Value}")) + "]")
            : string.Empty;

    /// <summary>Replaces every line break in <paramref name="text"/> with " | ".</summary>
    /// <param name="text">The text to flatten.</param>
    /// <returns>The text on a single line.</returns>
    private static string Flatten(string text)
        => text.Replace("\r\n", "\n").Replace("\n", " | ");
}

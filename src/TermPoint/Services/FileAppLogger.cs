using System.Reflection;
using System.Runtime.ExceptionServices;
using Bugsnag;

namespace TermPoint.Services;

/// <summary>
/// Writes log entries to a rolling daily log file under
/// %AppData%\TermPoint\Logs\app-YYYY-MM-DD.log.
///
/// Designed to be swapped out for a remote/database sink later by implementing
/// IAppLogger differently and updating the DI registration in App.axaml.cs.
///
/// All public methods are non-throwing in production. When <see cref="ThrowOnError"/>
/// is true, <see cref="LogError"/> re-throws the original exception instead of firing
/// the notification event, so exceptions surface immediately in the debugger with a
/// full stack trace. This flag is intended for development only.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    /// <inheritdoc/>
    public bool ThrowOnError { get; set; }

    /// <inheritdoc/>
    public event EventHandler<string>? ErrorLogged;

    /// <summary>The log folder every real run uses: <c>%AppData%\TermPoint\Logs</c>.</summary>
    private static readonly string DefaultLogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermPoint", "Logs");

    /// <summary>
    /// Test-only: when set, log files are written to (and old ones pruned from) this folder instead of
    /// <see cref="DefaultLogDirectory"/>. The test assembly sets it once, before any test runs, so tests of this class
    /// never touch the developer's real log folder. Always null in the app.
    /// </summary>
    internal static string? LogDirectoryOverride { get; set; }

    /// <summary>The folder log files go to: the test override when set, otherwise the real one.</summary>
    private static string LogDirectory => LogDirectoryOverride ?? DefaultLogDirectory;

    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    private readonly object _lock = new();

    
    
    public Bugsnag.IClient? BugsnagClient { get; set; }
    public void LogError(Exception? ex, string? context = null, bool unhandled = false)
    {
        Write("ERROR", context, ex);
        // Bugsnag's internal BlockingCollection may be disposed during app
        // termination — the error reporter must never throw.
        try
        {
            if (unhandled)
                BugsnagClient?.Notify(ex, Bugsnag.Payload.HandledState.ForUnhandledException(),
                    report => report.Event.Context = context);
            else
                BugsnagClient?.Notify(ex, report => report.Event.Context = context);
        }
        catch (ObjectDisposedException) { }
        catch (Exception) { }
        // In dev mode, re-throw immediately so exceptions surface with a full stack
        // trace rather than being silently swallowed and shown as a notification banner.
        // ExceptionDispatchInfo preserves the original stack trace. The re-throw exits
        // this method, so the ErrorLogged event is intentionally NOT fired — a banner
        // alongside a debugger break is confusing noise.
        if (ThrowOnError && ex is not null)
            ExceptionDispatchInfo.Capture(ex).Throw();

        // Production path: surface the error as a notification banner.
        // Use the context string when provided; it is more user-readable than the
        // raw exception message.
        var notificationMessage = !string.IsNullOrWhiteSpace(context)
            ? context
            : ex?.Message ?? "An error occurred.";
        ErrorLogged?.Invoke(this, notificationMessage);
    }

    /// <summary>
    /// Writes a local log line (level REPORT) and sends the problem to BugSnag as a HANDLED error with the given
    /// context, grouping key and metadata tab. Unlike <see cref="LogError"/> it does not re-throw when
    /// <see cref="ThrowOnError"/> is set and does not raise <see cref="ErrorLogged"/> (no banner). Never throws:
    /// Bugsnag's internal delivery queue may be disposed during termination, and a report must never take the
    /// application down. A null <see cref="BugsnagClient"/> (tests, unconfigured builds) just skips the remote half.
    /// </summary>
    /// <param name="ex">A named exception describing the problem (it need not have been thrown).</param>
    /// <param name="context">Short, stable description of what was happening; becomes the report's context.</param>
    /// <param name="metadata">Optional key/value details, sent under <paramref name="metadataTab"/>. No paths, file names or user names.</param>
    /// <param name="metadataTab">Name of the BugSnag tab that holds <paramref name="metadata"/>.</param>
    /// <param name="groupingKey">Optional stable key that groups every report that carries it into one BugSnag issue.</param>
    public void ReportHandled(
        Exception ex,
        string context,
        IReadOnlyDictionary<string, string>? metadata = null,
        string metadataTab = "details",
        string? groupingKey = null)
    {
        try
        {
            Write("REPORT", context + AppLoggerText.FormatMetadata(metadata), ex);

            BugsnagClient?.Notify(ex, report =>
            {
                report.Event.Context = context;
                if (groupingKey is not null)
                    report.Event.GroupingHash = groupingKey;
                if (metadata is { Count: > 0 })
                    report.Event.Metadata.Add(metadataTab, new Dictionary<string, string>(metadata));
            });
        }
        catch (Exception)
        {
            // Includes ObjectDisposedException from a Bugsnag queue that is shutting down. Nothing to do:
            // the local log line above has already been written.
        }
    }

    /// <summary>
    /// Writes a local log line (level WARN) and raises <see cref="ErrorLogged"/> with <paramref name="message"/>, so
    /// <see cref="AppNotificationService"/> shows it in the notification banner. Sends NOTHING to BugSnag. Does not
    /// honour <see cref="ThrowOnError"/>: there is no exception to re-throw.
    /// </summary>
    /// <param name="message">What to tell the user: one short sentence, with no paths or technical detail.</param>
    public void NotifyUser(string message)
    {
        Write("WARN", message, null);
        try { ErrorLogged?.Invoke(this, message); }
        catch (Exception) { /* a faulty subscriber must not break the caller */ }
    }

    public void LogWarning(string message, string? context = null)
        => Write("WARN", context ?? message, null);

    public void LogInfo(string message, string? context = null)
        => Write("INFO", context ?? message, null);

    private void Write(string level, string? message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var logFile = Path.Combine(LogDirectory,
                $"app-{DateTime.Now:yyyy-MM-dd}.log");

            var lines = new List<string>
            {
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [v{AppVersion}]"
            };

            if (!string.IsNullOrWhiteSpace(message))
                lines.Add($"  Context : {message}");

            if (ex is not null)
            {
                lines.Add($"  Type    : {ex.GetType().FullName}");
                lines.Add($"  Message : {ex.Message}");

                var inner = ex.InnerException;
                while (inner is not null)
                {
                    lines.Add($"  Inner   : [{inner.GetType().FullName}] {inner.Message}");
                    inner = inner.InnerException;
                }

                lines.Add($"  Stack   :");
                foreach (var stackLine in (ex.StackTrace ?? "(no stack trace)").Split('\n'))
                    lines.Add($"    {stackLine.TrimEnd()}");
            }

            lines.Add(string.Empty); // blank separator between entries

            lock (_lock)
                File.AppendAllLines(logFile, lines);
        }
        catch (Exception logEx)
        {
            // Logger itself failed — last-resort: write to stderr so it's at least
            // visible in a debug console, but never let this propagate.
            try { Console.Error.WriteLine($"[FileAppLogger] Failed to write log: {logEx.Message}"); }
            catch { /* truly last resort */ }
        }
    }

    /// <inheritdoc/>
    public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null)
    {
        try
        {
            if (metadata is { Count: > 0 })
                BugsnagClient?.Breadcrumbs.Leave(message, BreadcrumbType.State, metadata!);
            else
                BugsnagClient?.Breadcrumbs.Leave(message);
        }
        catch { /* breadcrumbs must never throw */ }
    }

    /// <summary>
    /// Deletes log files older than <paramref name="days"/> days.
    /// Call once at startup to prevent unbounded log growth.
    /// Non-throwing.
    /// </summary>
    public void PruneOldLogs(int days = 30)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var cutoff = DateTime.Now.AddDays(-days);
            foreach (var file in Directory.GetFiles(LogDirectory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* non-throwing */ }
    }
}

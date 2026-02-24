using System.Reflection;

namespace SchedulingAssistant.Services;

/// <summary>
/// Writes log entries to a rolling daily log file under
/// %AppData%\SchedulingAssistant\Logs\app-YYYY-MM-DD.log.
///
/// Designed to be swapped out for a remote/database sink later by implementing
/// IAppLogger differently and updating the DI registration in App.axaml.cs.
///
/// All public methods are non-throwing — any I/O failure is silently ignored
/// (or written to stderr as a last resort) so the logger never crashes the app.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SchedulingAssistant", "Logs");

    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    private readonly object _lock = new();

    public void LogError(Exception ex, string? context = null)
        => Write("ERROR", context, ex);

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

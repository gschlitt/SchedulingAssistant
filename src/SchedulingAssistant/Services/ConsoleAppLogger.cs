using System.Runtime.ExceptionServices;

namespace SchedulingAssistant.Services;

/// <summary>
/// A lightweight <see cref="IAppLogger"/> implementation that writes log entries to
/// <see cref="Console.Error"/> instead of a file. Used in environments where file-system
/// access is unavailable or undesirable (e.g. the WASM browser demo build).
///
/// <para>All methods are non-throwing in production. When <see cref="ThrowOnError"/> is true,
/// <see cref="LogError"/> re-throws the original exception after logging it, identical to the
/// behaviour of <see cref="FileAppLogger"/>.</para>
/// </summary>
public sealed class ConsoleAppLogger : IAppLogger
{
    /// <inheritdoc/>
    public bool ThrowOnError { get; set; }

    /// <inheritdoc/>
    public void LogError(Exception ex, string? context = null)
    {
        Write("ERROR", context, ex);

        if (ThrowOnError)
            ExceptionDispatchInfo.Capture(ex).Throw();
    }

    /// <inheritdoc/>
    public void LogWarning(string message, string? context = null)
        => Write("WARN", context ?? message, null);

    /// <inheritdoc/>
    public void LogInfo(string message, string? context = null)
        => Write("INFO", context ?? message, null);

    /// <summary>
    /// Formats a log entry and writes it to <see cref="Console.Error"/>.
    /// Non-throwing: any exception during formatting or writing is silently swallowed.
    /// </summary>
    /// <param name="level">Severity label (e.g. "INFO", "WARN", "ERROR").</param>
    /// <param name="message">Optional human-readable context message.</param>
    /// <param name="ex">Optional exception to include in the output.</param>
    private static void Write(string level, string? message, Exception? ex)
    {
        try
        {
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}]");

            if (!string.IsNullOrWhiteSpace(message))
                Console.Error.WriteLine($"  Context : {message}");

            if (ex is not null)
            {
                Console.Error.WriteLine($"  Type    : {ex.GetType().FullName}");
                Console.Error.WriteLine($"  Message : {ex.Message}");

                var inner = ex.InnerException;
                while (inner is not null)
                {
                    Console.Error.WriteLine($"  Inner   : [{inner.GetType().FullName}] {inner.Message}");
                    inner = inner.InnerException;
                }

                Console.Error.WriteLine($"  Stack   :");
                foreach (var line in (ex.StackTrace ?? "(no stack trace)").Split('\n'))
                    Console.Error.WriteLine($"    {line.TrimEnd()}");
            }

            Console.Error.WriteLine();
        }
        catch
        {
            // Truly last resort — never let the logger throw.
        }
    }
}

using System.Runtime.ExceptionServices;

namespace SchedulingAssistant.Services;

/// <summary>
/// Lightweight <see cref="IAppLogger"/> that writes to <see cref="Console.Error"/>
/// instead of a file. Used in environments without file-system access (e.g. WASM).
/// </summary>
public sealed class ConsoleAppLogger : IAppLogger
{
    /// <inheritdoc/>
    public event EventHandler<string>? ErrorLogged;

    /// <inheritdoc/>
    public bool ThrowOnError { get; set; }

    /// <inheritdoc/>
    public void LogError(Exception? ex, string? context = null)
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
            }
            Console.Error.WriteLine();
        }
        catch { }
    }
}

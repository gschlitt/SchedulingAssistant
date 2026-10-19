using System.Runtime.ExceptionServices;
using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// The logger <see cref="App.Logger"/> points at for the whole test run (installed by
/// <see cref="TestAssemblyGuards.KeepAppLoggerAwayFromRealFilesAndBugsnag"/>). It behaves like
/// <c>FileAppLogger</c> as far as callers can tell — <see cref="LogError"/> re-throws when <see cref="ThrowOnError"/>
/// is set, and otherwise raises <see cref="ErrorLogged"/> with the same message — but it writes no log file, sends
/// nothing to BugSnag and keeps no history, so a long harness run neither touches the developer's real
/// <c>%AppData%\TermPoint\Logs</c> nor reports from their machine, and does not grow in memory.
/// </summary>
internal sealed class SilentAppLogger : IAppLogger
{
    /// <inheritdoc/>
    public bool ThrowOnError { get; set; }

    /// <inheritdoc/>
    public event EventHandler<string>? ErrorLogged;

    /// <summary>
    /// Mirrors <c>FileAppLogger.LogError</c> without its file write and BugSnag report: re-throws
    /// <paramref name="ex"/> (with its original stack trace) when <see cref="ThrowOnError"/> is set; otherwise raises
    /// <see cref="ErrorLogged"/> with <paramref name="context"/>, or the exception's message, as the real one does.
    /// </summary>
    /// <param name="ex">The exception, if any.</param>
    /// <param name="context">Optional description of what failed.</param>
    /// <param name="unhandled">Ignored (it only changes how the real logger reports to BugSnag).</param>
    public void LogError(Exception? ex, string? context = null, bool unhandled = false)
    {
        if (ThrowOnError && ex is not null)
            ExceptionDispatchInfo.Capture(ex).Throw();

        var message = !string.IsNullOrWhiteSpace(context)
            ? context
            : ex?.Message ?? "An error occurred.";
        ErrorLogged?.Invoke(this, message);
    }

    /// <summary>Does nothing (no file is written).</summary>
    /// <param name="message">Ignored.</param>
    /// <param name="context">Ignored.</param>
    public void LogWarning(string message, string? context = null) { }

    /// <summary>Does nothing (no file is written).</summary>
    /// <param name="message">Ignored.</param>
    /// <param name="context">Ignored.</param>
    public void LogInfo(string message, string? context = null) { }

    /// <summary>Does nothing (no breadcrumb is left anywhere).</summary>
    /// <param name="message">Ignored.</param>
    /// <param name="metadata">Ignored.</param>
    public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
}

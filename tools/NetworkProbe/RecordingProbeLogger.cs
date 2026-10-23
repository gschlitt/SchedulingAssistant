using TermPoint.Services;

namespace NetworkProbe;

/// <summary>One line TermPoint wrote to its logger while the probe was running.</summary>
/// <param name="At">When it was written, on the probe's clock (<see cref="ProbeClock"/>).</param>
/// <param name="Level">INFO, WARN or ERROR.</param>
/// <param name="Text">The message.</param>
internal sealed record LogLine(TimeSpan At, string Level, string Text);

/// <summary>
/// The probe's own <see cref="IAppLogger"/>. It takes the place of <c>App.Logger</c> (see <see cref="LoggerRig"/>)
/// so that nothing TermPoint logs reaches a real log file or BugSnag, and so that each case can show exactly which
/// lines <see cref="NetworkFileOps"/> wrote ("... made no progress for 5000ms", "... timed out after 5000ms",
/// "abandoned ... task faulted after timeout"). Thread-safe: those lines are written from thread-pool threads.
/// </summary>
internal sealed class RecordingProbeLogger : IAppLogger
{
    /// <summary>Guards <see cref="_lines"/>.</summary>
    private readonly object _gate = new();

    /// <summary>Everything logged so far, in order.</summary>
    private readonly List<LogLine> _lines = new();

    /// <inheritdoc/>
    public bool ThrowOnError { get; set; }

    /// <summary>Never raised: the probe has no banner to show. Accessors exist only to satisfy the interface.</summary>
    public event EventHandler<string>? ErrorLogged { add { } remove { } }

    /// <summary>
    /// Remembers where the log stands now, so a case can later ask for just the lines written after this point.
    /// </summary>
    /// <returns>A marker to pass to <see cref="Since"/>.</returns>
    public int Mark()
    {
        lock (_gate) return _lines.Count;
    }

    /// <summary>
    /// The lines written since a marker.
    /// </summary>
    /// <param name="mark">A value returned by <see cref="Mark"/>.</param>
    /// <returns>A copy of the lines, oldest first.</returns>
    public IReadOnlyList<LogLine> Since(int mark)
    {
        lock (_gate) return _lines.Skip(mark).ToList();
    }

    /// <inheritdoc/>
    public void LogError(Exception? ex, string? context = null, bool unhandled = false)
        => Add("ERROR", $"{context} {ex?.GetType().Name}: {ex?.Message}".Trim());

    /// <inheritdoc/>
    public void LogWarning(string message, string? context = null) => Add("WARN", message);

    /// <inheritdoc/>
    public void LogInfo(string message, string? context = null) => Add("INFO", message);

    /// <inheritdoc/>
    public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }

    /// <summary>Records one line with the current probe time.</summary>
    /// <param name="level">INFO, WARN or ERROR.</param>
    /// <param name="text">The message.</param>
    private void Add(string level, string text)
    {
        var line = new LogLine(ProbeClock.Now, level, text);
        lock (_gate) _lines.Add(line);
    }
}

using System.Runtime.CompilerServices;
using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>One call to <see cref="IAppLogger.ReportHandled"/>, as a recording logger saw it.</summary>
/// <param name="Exception">The named exception.</param>
/// <param name="Context">The report's context.</param>
/// <param name="Metadata">The metadata, or null.</param>
/// <param name="MetadataTab">The tab that holds the metadata.</param>
/// <param name="GroupingKey">The grouping key, or null.</param>
internal sealed record HandledReport(
    Exception Exception,
    string Context,
    IReadOnlyDictionary<string, string>? Metadata,
    string MetadataTab,
    string? GroupingKey);

/// <summary>
/// A test <see cref="IAppLogger"/> that remembers everything it is told: information and warning lines, errors,
/// handled reports (<see cref="IAppLogger.ReportHandled"/>) and user notices (<see cref="IAppLogger.NotifyUser"/>).
/// Nothing reaches a file or BugSnag. Used by the damage-reporting tests (spec item 5, step 5e); each test makes its
/// own, which also gives it its own "already reported this run" memory.
/// </summary>
internal sealed class RecordingAppLogger : IAppLogger
{
    /// <summary>Every <see cref="LogInfo"/> message, in order.</summary>
    public List<string> Infos { get; } = new();

    /// <summary>Every <see cref="LogWarning"/> message, in order.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Every <see cref="LogError"/> call, in order.</summary>
    public List<(Exception? Ex, string? Context)> Errors { get; } = new();

    /// <summary>Every <see cref="ReportHandled"/> call, in order.</summary>
    public List<HandledReport> Handled { get; } = new();

    /// <summary>Every <see cref="NotifyUser"/> message, in order.</summary>
    public List<string> UserNotices { get; } = new();

    /// <summary>When true, <see cref="ReportHandled"/> throws (to prove callers survive a failing logger).</summary>
    public bool ThrowFromReportHandled { get; set; }

    /// <inheritdoc/>
    public bool ThrowOnError { get; set; }

    /// <inheritdoc/>
    public event EventHandler<string>? ErrorLogged;

    /// <inheritdoc/>
    public void LogError(Exception? ex, string? context = null, bool unhandled = false)
    {
        Errors.Add((ex, context));
        ErrorLogged?.Invoke(this, context ?? ex?.Message ?? "error");
    }

    /// <inheritdoc/>
    public void LogWarning(string message, string? context = null) => Warnings.Add(message);

    /// <inheritdoc/>
    public void LogInfo(string message, string? context = null) => Infos.Add(message);

    /// <inheritdoc/>
    public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }

    /// <inheritdoc/>
    public void ReportHandled(
        Exception ex,
        string context,
        IReadOnlyDictionary<string, string>? metadata = null,
        string metadataTab = "details",
        string? groupingKey = null)
    {
        if (ThrowFromReportHandled)
            throw new InvalidOperationException("the logger is failing");

        Handled.Add(new HandledReport(ex, context, metadata, metadataTab, groupingKey));
    }

    /// <inheritdoc/>
    public void NotifyUser(string message) => UserNotices.Add(message);
}

/// <summary>
/// Assembly-wide guards that keep tests away from the outside world.
/// </summary>
internal static class TestAssemblyGuards
{
    /// <summary>
    /// The logger damage reports go to when a test reaches the reporter without passing a logger of its own (for
    /// example a validator test that checks a damaged file). Without this, such a test would use the real
    /// <c>App.Logger</c> — and send a real report to BugSnag from the developer's machine.
    /// </summary>
    internal static RecordingAppLogger SilentDamageLogger { get; } = new();

    /// <summary>Runs once, when the test assembly loads, before any test.</summary>
    [ModuleInitializer]
    internal static void KeepDamageReportsAwayFromBugsnag()
        => DatabaseDamageReporter.DefaultLogger = static () => SilentDamageLogger;

    /// <summary>
    /// The private settings folder this test run uses instead of <c>%AppData%\TermPoint</c> (spec parking lot P5):
    /// one per test process, so a run can never read or overwrite the developer's real <c>settings.json</c>.
    /// </summary>
    internal static string SettingsDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"termpoint-test-settings-{Environment.ProcessId}");

    /// <summary>
    /// Runs once, when the test assembly loads, before any test: points <see cref="AppSettings"/> at
    /// <see cref="SettingsDirectory"/>, starting empty, and removes that folder when the test process exits
    /// (best effort).
    /// </summary>
    [ModuleInitializer]
    internal static void KeepSettingsAwayFromTheRealFile()
    {
        try { if (Directory.Exists(SettingsDirectory)) Directory.Delete(SettingsDirectory, recursive: true); }
        catch { /* best effort: a leftover folder from a reused process id only holds test settings */ }

        AppSettings.SettingsDirectoryOverride = SettingsDirectory;

        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            try { Directory.Delete(SettingsDirectory, recursive: true); }
            catch { /* best effort */ }
        };
    }
}

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
        lock (Errors) Errors.Add((ex, context));
        ErrorLogged?.Invoke(this, context ?? ex?.Message ?? "error");
    }

    /// <inheritdoc/>
    public void LogWarning(string message, string? context = null) { lock (Warnings) Warnings.Add(message); }

    /// <inheritdoc/>
    public void LogInfo(string message, string? context = null) { lock (Infos) Infos.Add(message); }

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

        lock (Handled) Handled.Add(new HandledReport(ex, context, metadata, metadataTab, groupingKey));
    }

    /// <inheritdoc/>
    public void NotifyUser(string message) { lock (UserNotices) UserNotices.Add(message); }
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
    /// Runs once, when the test assembly loads, before any test: replaces <see cref="App.Logger"/> — in a test process
    /// otherwise the REAL file logger, writing to the developer's <c>%AppData%\TermPoint\Logs</c> and holding a live
    /// BugSnag client whose "development" stage is reported — with a <see cref="SilentAppLogger"/> that keeps the same
    /// <see cref="IAppLogger.ThrowOnError"/>. Production code that calls <c>App.Logger.LogError</c> during a test
    /// (and the harness provokes thousands of failures) therefore sends nothing and writes nothing.
    ///
    /// <para>Reading <see cref="App.Logger"/> first runs <c>App</c>'s static initialisers, and the static services they
    /// create (<c>App.Checkout</c>, <c>App.InstanceGuard</c>) capture the ORIGINAL logger. So that one is disconnected
    /// from BugSnag too: its <c>BugsnagClient</c> is cleared. (Those services may still append lines to the real log
    /// file if a test drives them directly; none of the harness does.) The swap uses the compiler-generated backing
    /// field, as <c>WatchCommandErrorsTests</c> already does, because the setter is private.</para>
    /// </summary>
    [ModuleInitializer]
    internal static void KeepAppLoggerAwayFromRealFilesAndBugsnag()
    {
        // Any FileAppLogger in this process — the original below, and the ones FileAppLogger's own tests build —
        // writes to the run's private folder (beside the private settings folder), never %AppData%\TermPoint\Logs.
        FileAppLogger.LogDirectoryOverride = Path.Combine(SettingsDirectory, "Logs");

        var original = App.Logger;
        if (original is FileAppLogger fileLogger)
            fileLogger.BugsnagClient = null;

        var field = typeof(App).GetField("<Logger>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(null, new SilentAppLogger { ThrowOnError = original.ThrowOnError });
    }

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
    /// <summary>
    /// Deletes the private settings folders (<c>termpoint-test-settings-&lt;pid&gt;</c>) left in the temp folder by
    /// earlier test runs whose process no longer exists. Best effort: a folder that can't be deleted, or whose process
    /// id is in use again, is left for a later run.
    /// </summary>
    private static void SweepFoldersOfFinishedRuns()
    {
        const string prefix = "termpoint-test-settings-";
        try
        {
            foreach (var folder in Directory.GetDirectories(Path.GetTempPath(), prefix + "*"))
            {
                if (!int.TryParse(Path.GetFileName(folder)[prefix.Length..], out var pid)) continue;
                if (pid == Environment.ProcessId || IsProcessRunning(pid)) continue;
                try { Directory.Delete(folder, recursive: true); }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>True when a process with id <paramref name="pid"/> exists right now.</summary>
    /// <param name="pid">The process id.</param>
    /// <returns>True when the process exists; false when it does not or cannot be checked.</returns>
    private static bool IsProcessRunning(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    [ModuleInitializer]
    internal static void KeepSettingsAwayFromTheRealFile()
    {
        try { if (Directory.Exists(SettingsDirectory)) Directory.Delete(SettingsDirectory, recursive: true); }
        catch { /* best effort: a leftover folder from a reused process id only holds test settings */ }

        // The ProcessExit cleanup below does not run when the test runner ends its host process abruptly, so earlier
        // runs leave their folders behind. Sweep up those whose process is gone; a concurrent run's folder is kept.
        SweepFoldersOfFinishedRuns();

        AppSettings.SettingsDirectoryOverride = SettingsDirectory;

        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            try { Directory.Delete(SettingsDirectory, recursive: true); }
            catch { /* best effort */ }
        };
    }
}

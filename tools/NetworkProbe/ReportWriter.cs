using System.Text;
using TermPoint.Services;

namespace NetworkProbe;

/// <summary>
/// Everything the probe prints and writes for the user (spec section 8): the case matrix for <c>--list</c>, one line
/// per case as it finishes, the summary table, and the Markdown report.
/// </summary>
internal static class ReportWriter
{
    /// <summary>Column titles of the summary table (spec section 8).</summary>
    private static readonly string[] SummaryHeaders =
        { "case", "mode", "verdict", "call-return", "result-after-cut", "landed-in", "recovery" };

    // ── --list ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prints the case matrix (<c>--list</c>).
    /// </summary>
    /// <param name="options">The command line (for the repeat count).</param>
    public static void PrintCaseMatrix(ProbeOptions options)
    {
        ConsoleOut.Line($"NetworkProbe case matrix (deadline under test: NetworkFileOps.TimeoutMs = {NetworkFileOps.TimeoutMs} ms)");
        ConsoleOut.Line();
        ConsoleOut.Line("Controls (always run first, not selectable; if either fails the rig is not working and no verdicts are given):");
        ConsoleOut.Line("  control-good   CB   ExistsAsync(no-such-file): must report a timeout within TimeoutMs + 1.5 s of the cut being confirmed");
        ConsoleOut.Line("  control-bad    CB   unguarded File.WriteAllBytes(64 KB) on its own thread: must still be blocked 15 s after the cut");
        ConsoleOut.Line();

        var rows = CaseCatalog.All
            .Select(c => (IReadOnlyList<string>)new[]
            {
                c.Name,
                c.ModeLabel,
                c.Cut == CutPoint.BeforeCall ? "1" : options.Repeat.ToString(),
                c.CallText
            })
            .ToList();
        ConsoleOut.Line("Cases (CB = network cut before the call; CM = cut in the middle of the operation):");
        ConsoleOut.Line(TextFormat.ConsoleTable(new[] { "case", "cut", "runs", "call" }, rows));
        ConsoleOut.Line();
        ConsoleOut.Line("Why each case exists:");
        foreach (var c in CaseCatalog.All)
            ConsoleOut.Line($"  {c.Name,-15} {c.Why}");
        ConsoleOut.Line();
        ConsoleOut.Line("Run only some with --only <name,name,...>. 'runs' for CM cases is --repeat (default 3).");
    }

    // ── progress and summary ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prints one line for a case that has just finished.
    /// </summary>
    /// <param name="r">The result.</param>
    public static void PrintCaseLine(CaseResult r)
    {
        ConsoleOut.Line(
            $"  => [{r.VerdictText}] {r.Label} ({r.Mode}): call-return {TextFormat.Seconds2(r.CallReturn)}; " +
            $"result-after-cut {TextFormat.Seconds(r.ResultAfterCut)}; landed-in {r.LandedIn}; recovery {TextFormat.Seconds(r.Recovery)}");
        ConsoleOut.Line($"     {r.Reason}");
    }

    /// <summary>
    /// Prints the controls, the summary table and the headline at the end of the run.
    /// </summary>
    /// <param name="report">The run's report.</param>
    public static void PrintSummary(RunReport report)
    {
        ConsoleOut.Line();
        ConsoleOut.Line("==== SUMMARY ====");
        ConsoleOut.Line(report.Headline);
        if (report.AbortMessage is not null)
            ConsoleOut.Line($"STOPPED EARLY: {report.AbortMessage}");
        ConsoleOut.Line();

        if (report.Controls.Count > 0)
        {
            ConsoleOut.Line("Controls:");
            foreach (var c in report.Controls)
                ConsoleOut.Line($"  [{c.OutcomeText}] {c.Name}: {c.Detail}");
            ConsoleOut.Line();
        }

        if (report.Results.Count > 0)
        {
            ConsoleOut.Line(TextFormat.ConsoleTable(SummaryHeaders, SummaryRows(report)));
            ConsoleOut.Line();
        }

        foreach (var r in report.Results.Where(r => r.Verdict is Verdict.Fail or Verdict.NotTested))
            ConsoleOut.Line($"  {r.VerdictText} {r.Label}: {r.Reason}");
        foreach (var note in report.CleanupNotes)
            ConsoleOut.Line($"  CLEAN-UP: {note}");
    }

    /// <summary>The summary table's rows, one per case run.</summary>
    /// <param name="report">The run's report.</param>
    /// <returns>Rows of cells in <see cref="SummaryHeaders"/> order.</returns>
    private static List<IReadOnlyList<string>> SummaryRows(RunReport report)
        => report.Results
            .Select(r => (IReadOnlyList<string>)new[]
            {
                r.Label,
                r.Mode,
                r.VerdictText,
                TextFormat.Seconds2(r.CallReturn),
                r.CompletedAfterRestore
                    ? $"{TextFormat.Seconds(r.ResultAfterCut)} (AFTER the network came back)"
                    : r.ResultAfterCut is null && r.Verdict == Verdict.Fail ? "never completed" : TextFormat.Seconds(r.ResultAfterCut),
                r.LandedIn,
                TextFormat.Seconds(r.Recovery)
            })
            .ToList();

    // ── Markdown ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the Markdown report: date, computer, server, filter, clumsy path and pids, the controls, the table, and
    /// per case the captured log lines, under a plain-words headline.
    /// </summary>
    /// <param name="report">The run's report.</param>
    /// <param name="directory">The folder to write into (created if needed).</param>
    /// <returns>The full path of the report file.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static string WriteMarkdown(RunReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var name = $"netprobe-{report.StartedAt:yyyyMMdd-HHmmss}{(report.DryRun ? "-dryrun" : "")}.md";
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, BuildMarkdown(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>Builds the Markdown text.</summary>
    /// <param name="report">The run's report.</param>
    /// <returns>The document.</returns>
    private static string BuildMarkdown(RunReport report)
    {
        var o = report.Options;
        var sb = new StringBuilder();
        sb.AppendLine(report.DryRun ? "# NetworkProbe report (DRY RUN)" : "# NetworkProbe report");
        sb.AppendLine();
        sb.AppendLine(report.Headline);
        if (report.AbortMessage is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"**The run stopped early:** {report.AbortMessage}");
        }
        sb.AppendLine();

        sb.AppendLine("## Run");
        sb.AppendLine();
        var details = new List<IReadOnlyList<string>>
        {
            new[] { "date", report.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz") },
            new[] { "computer / user", $"{report.Computer} / {report.UserName}" },
            new[] { "mode", report.DryRun ? "dry run (no clumsy, no server, every case SIMULATED)" : "real run" },
            new[] { "server", report.Server ?? "-" },
            new[] { "server addresses cut and checked", report.Addresses.Count == 0 ? "-" : string.Join(", ", report.Addresses) },
            new[] { "addresses left out (no answer on 445 at the start)", report.IgnoredAddresses.Count == 0 ? "none" : string.Join(", ", report.IgnoredAddresses) },
            new[] { "clumsy filter", report.Filter ?? "-" },
            new[] { "clumsy", report.ClumsyPath ?? "-" },
            new[] { "clumsy pids started", report.ClumsyPids.Count == 0 ? "-" : string.Join(", ", report.ClumsyPids) },
            new[] { "probe folder", report.ProbeFolder ?? "-" },
            new[] { "options", $"hold {o.HoldSeconds} s, big file {o.BigMb} MB, lag {o.LagMs} ms, repeat {o.Repeat}, settle {o.SettleSeconds} s, deadline under test {NetworkFileOps.TimeoutMs} ms" },
            new[] { "log redirect / BugSnag", LoggerRigText(report.LoggerRig) }
        };
        sb.AppendLine(TextFormat.MarkdownTable(new[] { "item", "value" }, details));
        sb.AppendLine();

        if (report.Controls.Count > 0)
        {
            sb.AppendLine("## Controls (rig self-checks)");
            sb.AppendLine();
            sb.AppendLine(TextFormat.MarkdownTable(
                new[] { "control", "must show", "outcome", "what happened" },
                report.Controls.Select(c => (IReadOnlyList<string>)new[] { c.Name, c.Expectation, c.OutcomeText, c.Detail }).ToList()));
            sb.AppendLine();
        }

        if (report.Results.Count > 0)
        {
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine(TextFormat.MarkdownTable(SummaryHeaders, SummaryRows(report)));
            sb.AppendLine();
            sb.AppendLine("Reading the table: **PASS** = the call came back within the deadline plus 2 s of the cut being confirmed, reporting the timeout. " +
                          "**FAIL** = it waited for the network, took too long, never completed, or blocked its caller (call-return over 1 s). " +
                          "**NOT TESTED** = no verdict possible (see the reason). `landed-in` is the step the stall was reported in, from TermPoint's own log line.");
            sb.AppendLine();

            sb.AppendLine("## Per case");
            foreach (var r in report.Results)
                AppendCase(sb, r);
        }

        if (report.CleanupNotes.Count > 0)
        {
            sb.AppendLine("## Clean-up");
            sb.AppendLine();
            foreach (var note in report.CleanupNotes) sb.AppendLine($"- {note}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Appends one case's section: verdict, reason, measurements and the log lines it captured.</summary>
    /// <param name="sb">Where to append.</param>
    /// <param name="r">The result.</param>
    private static void AppendCase(StringBuilder sb, CaseResult r)
    {
        sb.AppendLine();
        sb.AppendLine($"### {r.Label} ({r.Mode}): {r.VerdictText}");
        sb.AppendLine();
        sb.AppendLine($"- reason: {r.Reason}");
        sb.AppendLine($"- result: {r.ResultText}");
        sb.AppendLine($"- call-return: {TextFormat.Seconds2(r.CallReturn)}; result-after-cut: {TextFormat.Seconds(r.ResultAfterCut)}; landed-in: {r.LandedIn}; recovery: {TextFormat.Seconds(r.Recovery)}");
        if (r.BigMb > 0) sb.AppendLine($"- large file: {r.BigMb} MB");
        if (r.Notes.Length > 0) sb.AppendLine($"- note: {r.Notes}");
        sb.AppendLine("- log lines captured:");
        sb.AppendLine();
        sb.AppendLine("```");
        if (r.Log.Count == 0) sb.AppendLine("(nothing was logged)");
        foreach (var line in r.Log)
            sb.AppendLine($"{ProbeClock.Stamp(line.At)} [{line.Level}] {line.Text}");
        sb.AppendLine("```");
    }

    /// <summary>Describes what the logger rig did, for the report.</summary>
    /// <param name="status">The rig status, or null if it was never installed.</param>
    /// <returns>One sentence.</returns>
    private static string LoggerRigText(LoggerRigStatus? status)
        => status is null
            ? "not installed"
            : $"App.Logger replaced by the probe's recorder; log folder redirected to a temp folder; BugSnag client " +
              $"{(status.BugsnagWasAttached ? "was attached and is now detached" : "was not attached")}; " +
              $"{status.DetachedHandlers.Count} BugSnag process-wide handler(s) removed, {status.RemainingBugsnagHandlers.Count} left";
}

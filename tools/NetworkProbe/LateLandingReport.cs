using System.Text;

namespace NetworkProbe;

/// <summary>
/// Console and Markdown output of the late-landing experiment (<c>--late-landing</c>): one line per cycle as it finishes,
/// the summary table, and the report's "late landings" sections. <see cref="ReportWriter"/> calls in here, so the two
/// modes share the report's opening, run details and controls.
/// </summary>
internal static class LateLandingReport
{
    /// <summary>Column titles of the late-landing table.</summary>
    private static readonly string[] Headers =
    {
        "case", "hold", "repetition", "NetworkFileOps returned (s, result)", "outcome",
        "first seen changed (s after issue)", "inner op finished (s, how)", "recovery (s)", "folder mtime before / after (server clock)"
    };

    /// <summary>
    /// Prints one line for a cycle that has just finished.
    /// </summary>
    /// <param name="r">The result.</param>
    public static void PrintCycleLine(LateLandingResult r)
    {
        ConsoleOut.Line(
            $"  => [{r.OutcomeText}] {r.Label}: NetworkFileOps returned {ReturnedText(r)}; first seen changed {TextFormat.Seconds(r.FirstChangedAfterIssue)} after the call; " +
            $"inner op finished {InnerText(r)}; recovery {TextFormat.Seconds(r.Recovery)}");
        ConsoleOut.Line($"     {r.Reason}");
    }

    /// <summary>
    /// Prints the table of cycles and the cycles that were not a clean answer, at the end of the run.
    /// </summary>
    /// <param name="report">The run's report.</param>
    public static void PrintSummary(RunReport report)
    {
        if (report.LateResults.Count == 0) return;
        ConsoleOut.Line(TextFormat.ConsoleTable(Headers, Rows(report)));
        ConsoleOut.Line();
        foreach (var r in report.LateResults.Where(r => r.Outcome is LateOutcome.NotTested or LateOutcome.StillPending))
            ConsoleOut.Line($"  {r.OutcomeText} {r.Label}: {r.Reason}");
    }

    /// <summary>
    /// Appends the late-landing sections of the Markdown report: the table, how to read it, and the per-cycle log lines.
    /// </summary>
    /// <param name="sb">Where to append.</param>
    /// <param name="report">The run's report.</param>
    public static void AppendMarkdown(StringBuilder sb, RunReport report)
    {
        if (report.LateResults.Count == 0) return;

        sb.AppendLine("## Late landings");
        sb.AppendLine();
        sb.AppendLine(TextFormat.MarkdownTable(Headers, Rows(report)));
        sb.AppendLine();
        sb.AppendLine("Reading the table: **LANDED LATE** = the abandoned delete or rename still took effect on the server after the network came back. " +
                      "\"First seen changed\" is the time, counted from the call being issued, of the first look at the folder that differed from before the cut; " +
                      "it is an UPPER bound, because the operation may have landed up to a second (plus any look that could not be made) earlier. " +
                      "**DIED** = nothing changed and the abandoned operation was seen to end with an error (its \"faulted after timeout\" line). " +
                      "**STILL PENDING** = nothing changed and no line says it ended (a successful finish is never logged, so this means it may still be blocked, or ended without effect). " +
                      "**NOT TESTED** = no conclusion (see the reason). The folder times are the server's clock, which may differ from this computer's by about 1 s, " +
                      "and SMB may show a directory time up to about 10 s late.");
        sb.AppendLine();

        sb.AppendLine("## Per cycle");
        foreach (var r in report.LateResults)
            AppendCycle(sb, r);
    }

    /// <summary>The table's rows, one per cycle.</summary>
    /// <param name="report">The run's report.</param>
    /// <returns>Rows of cells in <see cref="Headers"/> order.</returns>
    private static List<IReadOnlyList<string>> Rows(RunReport report)
        => report.LateResults
            .Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name,
                $"{r.HoldSeconds} s",
                $"#{r.Repetition}",
                ReturnedText(r),
                r.OutcomeText,
                TextFormat.Seconds(r.FirstChangedAfterIssue),
                InnerText(r),
                TextFormat.Seconds(r.Recovery),
                $"{TextFormat.ServerTime(r.FolderMtimeBefore)} / {TextFormat.ServerTime(r.FolderMtimeAfter)}"
            })
            .ToList();

    /// <summary>Appends one cycle's section: outcome, reason and the timeline.</summary>
    /// <param name="sb">Where to append.</param>
    /// <param name="r">The result.</param>
    private static void AppendCycle(StringBuilder sb, LateLandingResult r)
    {
        sb.AppendLine();
        sb.AppendLine($"### {r.Label}: {r.OutcomeText}");
        sb.AppendLine();
        sb.AppendLine($"- reason: {r.Reason}");
        sb.AppendLine($"- NetworkFileOps returned: {ReturnedText(r)}; recovery: {TextFormat.Seconds(r.Recovery)}; " +
                      $"watched: {r.Observations} looks over {TextFormat.Seconds(r.ObservedFor)}, {r.UnreadableObservations} could not look");
        sb.AppendLine("- timeline (time on the probe's clock; the second time is counted from the call being issued) and log lines captured:");
        sb.AppendLine();
        sb.AppendLine("```");
        if (r.Timeline.Count == 0) sb.AppendLine("(nothing was recorded)");
        foreach (var line in r.Timeline)
            sb.AppendLine($"{ProbeClock.Stamp(line.At)} ({AfterIssue(r, line)}) [{line.Level}] {line.Text}");
        sb.AppendLine("```");
    }

    /// <summary>A timeline line's time counted from the call being issued, for example <c>+62.4 s</c> or <c>-11.0 s</c>.</summary>
    /// <param name="r">The result (knows when the call was issued).</param>
    /// <param name="line">The timeline line.</param>
    /// <returns>The signed seconds, or <c>no call</c> when the call was never issued.</returns>
    private static string AfterIssue(LateLandingResult r, LogLine line)
    {
        if (r.IssuedAt == TimeSpan.Zero) return "no call";
        var seconds = (line.At - r.IssuedAt).TotalSeconds;
        return seconds >= 0 ? $"+{seconds:F1} s" : $"{seconds:F1} s";
    }

    /// <summary>What <c>NetworkFileOps</c> answered and when, for a table cell.</summary>
    /// <param name="r">The result.</param>
    /// <returns>For example <c>5.0 s, returned False</c>, or <c>-</c> when the call was never issued.</returns>
    private static string ReturnedText(LateLandingResult r)
        => r.NetworkFileOpsReturnedAfter is { } after ? $"{TextFormat.Seconds(after)}, {r.NetworkFileOpsResult}"
            : r.IssuedAt == TimeSpan.Zero ? "-" : r.NetworkFileOpsResult;

    /// <summary>When and how the inner operation was seen to end, for a table cell.</summary>
    /// <param name="r">The result.</param>
    /// <returns>For example <c>62.4 s, faulted: ...</c>, or <c>-</c> when no line says it ended.</returns>
    private static string InnerText(LateLandingResult r)
        => r.InnerFinishedAfterIssue is { } after ? $"{TextFormat.Seconds(after)}, {r.InnerFinishedHow}" : "-";
}

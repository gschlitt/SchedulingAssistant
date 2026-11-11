using System.Globalization;
using System.Text;

namespace NetworkProbe;

/// <summary>
/// Console and Markdown output of the share-semantics experiment (<c>--share-semantics</c>): one line per run as it
/// finishes, the case matrix for <c>--list</c>, and the Markdown report with one table per case (per-run rows and a
/// summary row) and a "Reading the result" section that maps the outcome onto the result tables of
/// <c>designs/network-experiments-5.8.md</c>. It states which row applies, and nothing beyond what those tables say.
/// </summary>
internal static class ShareSemanticsReport
{
    // ── console ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prints the case matrix (<c>--list</c> with <c>--share-semantics</c>).
    /// </summary>
    /// <param name="options">The command line (for <c>--runs</c>).</param>
    public static void PrintCaseMatrix(ProbeOptions options)
    {
        ConsoleOut.Line("NetworkProbe share-semantics tables (designs/network-experiments-5.8.md, E1 and E2). --only accepts: " +
                        string.Join(", ", ShareSemanticsCatalog.Names));
        ConsoleOut.Line();
        foreach (var c in ShareSemanticsCatalog.All)
        {
            ConsoleOut.Line($"{c.Id,-11} {options.Runs ?? c.DefaultRuns,3} runs  {c.Title}");
            ConsoleOut.Line($"{"",-11}            {c.Why}");
        }
    }

    /// <summary>
    /// Prints one line for a run that has just finished.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="r">The run's result.</param>
    public static void PrintRunLine(SemanticsCase c, SemanticsRun r)
    {
        var flag = r.CriticalRun ? "  <<<<<< CRITICAL" : "";
        ConsoleOut.Line($"  => [{c.Id} #{r.Run}] {r.Outcome}: {string.Join(" | ", r.Cells)}{flag}");
        if (r.Note.Length > 0) ConsoleOut.Line($"     {r.Note}");
    }

    /// <summary>
    /// Prints, at the end of the run, one line per table: the summary row.
    /// </summary>
    /// <param name="report">The run's report.</param>
    public static void PrintSummary(SemanticsReport report)
    {
        ConsoleOut.Line();
        if (report.HasCritical)
            ConsoleOut.Line("CRITICAL: CreateNew succeeded over a file the partner had created (lock not exclusive). Stop and investigate before anything else.");
        foreach (var c in ShareSemanticsCatalog.All)
        {
            var runs = report.RunsOf(c.Id);
            if (runs.Count == 0) continue;
            ConsoleOut.Line($"{c.Id}: {OutcomeCounts(runs)}{(Stats(c, runs) is { Length: > 0 } s ? $" [{s}]" : "")}");
        }
    }

    // ── Markdown ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the Markdown report.
    /// </summary>
    /// <param name="report">The run's report.</param>
    /// <param name="directory">The folder to write into (created if needed).</param>
    /// <returns>The full path of the report file.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static string WriteMarkdown(SemanticsReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var name = $"netprobe-{report.StartedAt:yyyyMMdd-HHmmss}-share-semantics{(report.DryRun ? "-dryrun" : "")}.md";
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, BuildMarkdown(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>Builds the Markdown text.</summary>
    /// <param name="report">The run's report.</param>
    /// <returns>The document. Its first line is the CRITICAL warning when CreateNew ever succeeded over the partner's file.</returns>
    private static string BuildMarkdown(SemanticsReport report)
    {
        var o = report.Options;
        var sb = new StringBuilder();

        // Design: a CreateNew that succeeds over an existing file makes this the report's FIRST line.
        if (report.HasCritical)
        {
            sb.AppendLine("**CRITICAL: lock not exclusive.** FileMode.CreateNew SUCCEEDED over a file the partner had just created " +
                          $"({report.Runs.Count(r => r.CriticalRun)} run(s), see E1b). Per the design this is serious: stop and investigate before anything else.");
            sb.AppendLine();
        }

        sb.AppendLine(report.DryRun ? "# NetworkProbe share-semantics report (DRY RUN, SIMULATED)" : "# NetworkProbe share-semantics report");
        sb.AppendLine();
        sb.AppendLine(Headline(report));
        if (report.AbortMessage is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"**The run stopped early:** {report.AbortMessage}");
        }
        sb.AppendLine();

        sb.AppendLine("## Run");
        sb.AppendLine();
        var rtts = report.RoundTripsMs.OrderBy(x => x).ToList();
        var details = new List<IReadOnlyList<string>>
        {
            new[] { "date", report.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz") },
            new[] { "runner computer / user", $"{report.Computer} / {report.UserName}" },
            new[] { "mode", report.DryRun
                ? "dry run: SIMULATED. The partner ran inside this process (TCP on 127.0.0.1) and every file call was on a local folder, so there is no SMB client, no SMB cache and nothing here maps onto the design's tables"
                : "real run" },
            new[] { "share (runner's view)", report.Share },
            new[] { "share is a network path?", report.Share.StartsWith(@"\\", StringComparison.Ordinal) ? "yes (UNC)" : "NO: a local path, so these results say nothing about SMB" },
            new[] { "runner's own folder on the share", report.ProbeFolder ?? "-" },
            new[] { "partner", report.Partner },
            new[] { "partner says", report.PartnerInfo },
            new[] { "control-channel round trip (PING)", rtts.Count == 0 ? "-" : $"min {rtts[0]:F2} ms, median {Median(rtts):F2} ms, from {rtts.Count} pings" },
            new[] { "options", $"runs {(o.Runs is { } n ? n.ToString(CultureInfo.InvariantCulture) : "default (20 for E1, 5 for E2)")}, " +
                               $"settle {o.SemanticsSettleSeconds} s, big file {o.SemanticsBigMb} MB, only {(o.Only is null ? "all" : string.Join(",", o.Only))}" },
            new[] { "clocks", "every time is the runner's own stopwatch (or, where marked \"partner ms\", the partner's stopwatch for one call); no clock is compared across computers" }
        };
        sb.AppendLine(TextFormat.MarkdownTable(new[] { "item", "value" }, details));
        sb.AppendLine();

        AppendSmb(sb, "Runner's SMB client settings (Get-SmbClientConfiguration)", report.RunnerSmb);
        AppendSmb(sb, "Partner's SMB client settings (Get-SmbClientConfiguration)", report.PartnerSmb);

        foreach (var c in ShareSemanticsCatalog.All)
            AppendCase(sb, c, report.RunsOf(c.Id));

        sb.AppendLine("## Reading the result");
        sb.AppendLine();
        if (report.DryRun)
        {
            sb.AppendLine("**SIMULATED.** This dry run only checks the plumbing. A local disk has no SMB not-found cache and no server, so the reading below shows how a real run " +
                          "would be read; it is not a result.");
            sb.AppendLine();
        }
        foreach (var line in Reading(report))
        {
            sb.AppendLine(line);
            sb.AppendLine();
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

    /// <summary>The one-line summary under the title: each table that ran, with its outcome counts.</summary>
    /// <param name="report">The run's report.</param>
    /// <returns>The sentence(s).</returns>
    private static string Headline(SemanticsReport report)
    {
        var parts = ShareSemanticsCatalog.All
            .Select(c => (c, runs: report.RunsOf(c.Id)))
            .Where(x => x.runs.Count > 0)
            .Select(x => $"{x.c.Id}: {OutcomeCounts(x.runs)}")
            .ToList();
        var body = parts.Count == 0 ? "No case ran." : string.Join("; ", parts) + ".";
        return report.DryRun ? $"DRY RUN (SIMULATED, local folder, no SMB): {body} None of this is a result." : body;
    }

    /// <summary>Appends an SMB settings block.</summary>
    /// <param name="sb">Where to append.</param>
    /// <param name="title">The heading.</param>
    /// <param name="smb">The settings text or the reason it could not be read.</param>
    private static void AppendSmb(StringBuilder sb, string title, (bool Ok, string Text) smb)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (!smb.Ok) sb.AppendLine($"Could not be read: {smb.Text}");
        else
        {
            sb.AppendLine("```");
            sb.AppendLine(smb.Text);
            sb.AppendLine("```");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Appends one case's section: title, what it asks, per-run table with a summary row, and any run notes.
    /// </summary>
    /// <param name="sb">Where to append.</param>
    /// <param name="c">The table.</param>
    /// <param name="runs">Its runs (empty when it did not run).</param>
    private static void AppendCase(StringBuilder sb, SemanticsCase c, List<SemanticsRun> runs)
    {
        sb.AppendLine($"## {c.Title}");
        sb.AppendLine();
        sb.AppendLine(c.Why);
        sb.AppendLine();
        if (runs.Count == 0)
        {
            sb.AppendLine("(not run)");
            sb.AppendLine();
            return;
        }

        var headers = new[] { "run" }.Concat(c.Headers).Concat(new[] { "outcome", "note" }).ToList();
        var rows = runs
            .Select(r => (IReadOnlyList<string>)new[] { $"#{r.Run}" }.Concat(r.Cells).Concat(new[] { r.Outcome, r.Note }).ToList())
            .ToList();
        // Summary row: outcome counts in the outcome column, min / median / max in the note column.
        var summary = new[] { "summary" }.Concat(c.Headers.Select(_ => "")).Concat(new[] { OutcomeCounts(runs), Stats(c, runs) }).ToList();
        rows.Add(summary);
        sb.AppendLine(TextFormat.MarkdownTable(headers, rows));
        sb.AppendLine();
    }

    /// <summary>Counts of each distinct outcome, for example <c>18 x never missing, 2 x SAID MISSING</c>.</summary>
    /// <param name="runs">The runs.</param>
    /// <returns>The counts, in order of first appearance.</returns>
    private static string OutcomeCounts(IEnumerable<SemanticsRun> runs)
        => string.Join(", ", runs.GroupBy(r => r.Outcome).Select(g => $"{g.Count()} x {g.Key}"));

    /// <summary>Min / median / max of a table's measured numbers.</summary>
    /// <param name="c">The table (names the numbers).</param>
    /// <param name="runs">The runs.</param>
    /// <returns>For example <c>blocked for (s): min 0.0 / median 0.0 / max 0.0</c>; empty when nothing was measured.</returns>
    private static string Stats(SemanticsCase c, IReadOnlyList<SemanticsRun> runs)
    {
        var parts = new List<string>();
        for (var i = 0; i < c.MetricNames.Count; i++)
        {
            var values = runs.Select(r => i == 0 ? r.Metric1 : r.Metric2).Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
            if (values.Count == 0) continue;
            parts.Add($"{c.MetricNames[i]}: min {values[0]:F2} / median {Median(values):F2} / max {values[^1]:F2} (n={values.Count})");
        }
        return string.Join("; ", parts);
    }

    /// <summary>Median of a sorted list.</summary>
    /// <param name="sorted">Values in ascending order (at least one).</param>
    /// <returns>The middle value, or the mean of the two middle values.</returns>
    private static double Median(IReadOnlyList<double> sorted)
        => sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    // ── reading the result ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps the outcomes onto the design's result tables (E1 and E2). Each paragraph states the counts it rests on, then
    /// which row applies; where the runs do not agree, or a case did not run, it says so instead of choosing.
    /// </summary>
    /// <param name="report">The run's report.</param>
    /// <returns>Paragraphs of Markdown.</returns>
    public static IReadOnlyList<string> Reading(SemanticsReport report)
    {
        var lines = new List<string>();
        ReadE1(report, lines);
        ReadE2(report, lines);
        if (lines.Count == 0) lines.Add("No E1 or E2 case ran, so there is nothing to read.");
        lines.Add("This reading follows the design's result tables and goes no further. A result holds for this client, this server and this kind of share only; " +
                  "macOS's SMB client and a non-Windows server are not covered.");
        return lines;
    }

    /// <summary>Counts runs with a given outcome.</summary>
    /// <param name="runs">The runs.</param>
    /// <param name="outcome">An <see cref="Outcomes"/> constant.</param>
    /// <returns>How many have exactly that outcome.</returns>
    private static int Count(List<SemanticsRun> runs, string outcome) => runs.Count(r => r.Outcome == outcome);

    /// <summary>Counts runs whose outcome contains some text (the E2 cases 3 and 4 outcomes have two halves).</summary>
    /// <param name="runs">The runs.</param>
    /// <param name="text">Text to look for.</param>
    /// <returns>How many contain it.</returns>
    private static int CountContaining(List<SemanticsRun> runs, string text) => runs.Count(r => r.Outcome.Contains(text, StringComparison.Ordinal));

    /// <summary>Adds the reading of E1 (design: "E1: does Bob's own create clear his cached not found?").</summary>
    /// <param name="report">The run's report.</param>
    /// <param name="lines">Where to add paragraphs.</param>
    private static void ReadE1(SemanticsReport report, List<string> lines)
    {
        var a = report.RunsOf("E1a");
        var b = report.RunsOf("E1b-create");
        var s = report.RunsOf("E1b-stale");
        if (a.Count + b.Count + s.Count == 0) return;

        var sb = new StringBuilder("**E1 (design: result table \"does Bob's own create clear his cached not found?\", item 5.1).** ");
        if (a.Count > 0)
            sb.Append($"(a) {a.Count} runs: {Count(a, Outcomes.NeverMissing)} never said \"missing\" after our own create, {Count(a, Outcomes.SaidMissing)} said \"missing\", {Count(a, Outcomes.Error)} ended in ERROR. ");
        if (b.Count > 0)
            sb.Append($"(b) {b.Count} runs: CreateNew over the partner's file failed with \"already exists\" in {Count(b, Outcomes.RefusedExists)}, " +
                      $"failed with another error in {Count(b, Outcomes.RefusedOther)}, SUCCEEDED in {Count(b, Outcomes.Critical)}, ERROR in {Count(b, Outcomes.Error)}. ");
        if (s.Count > 0)
        {
            var ok = s.Where(r => r.Outcome != Outcomes.Error).ToList();
            var maxExists = ok.Select(r => r.Metric1).Where(v => v is not null).DefaultIfEmpty().Max();
            var maxOpen = ok.Select(r => r.Metric2).Where(v => v is not null).DefaultIfEmpty().Max();
            sb.Append($"Not-found timing after the partner's create, {s.Count} runs: {OutcomeCounts(s)}; " +
                      $"longest File.Exists \"missing\" {FormatMax(maxExists)}, longest open \"missing\" {FormatMax(maxOpen)} " +
                      $"(a value equal to the {ShareSemanticsRunner.StaleCap.TotalSeconds:F0} s cap means it never found the file within the cap; the design expected up to about 5 s). ");
        }
        lines.Add(sb.ToString().TrimEnd());

        // Which row.
        string verdict;
        if (b.Count > 0 && Count(b, Outcomes.Critical) > 0)
            verdict = "**Row 3 applies** (design: \"CreateNew ever succeeds over an existing file\" = serious, the lock is not exclusive): stop and investigate before anything else.";
        else if (a.Count == 0 || b.Count == 0)
            verdict = $"Only {(a.Count > 0 ? "part (a)" : "part (b)")} ran, so no row of the design's table can be chosen: row 1 needs both (a) and (b); " +
                      (a.Count > 0
                          ? (Count(a, Outcomes.SaidMissing) > 0 ? "part (a) alone matches row 2 (the second check sometimes said \"missing\")." : "part (a) alone matches row 1's (a) (the second check never said \"missing\").")
                          : (Count(b, Outcomes.RefusedExists) == b.Count ? "part (b) alone matches row 1's (b) (CreateNew always failed)." : "part (b) did not match row 1's (b) in every run."));
        else if (Count(a, Outcomes.Error) + Count(b, Outcomes.Error) > 0)
            verdict = "Not conclusive: some runs ended in ERROR (see their notes). Fix the cause and re-run before using the design's table.";
        else if (Count(a, Outcomes.SaidMissing) > 0)
            verdict = "**Row 2 applies** (design: \"(a) second check sometimes says missing\"): before acting on \"my lock is missing\", the writer confirms with CreateNew (\"already exists\" = stale, ignore); " +
                      "the design says to check this against the rules and the takeover first.";
        else if (Count(b, Outcomes.RefusedExists) == b.Count)
            verdict = "**Row 1 applies** (design: \"(a) second check never says missing; (b) CreateNew always fails\"): writer story closed; 5.1 = the two-reader fix + tidy the exists/read race. Small.";
        else
            verdict = $"No row fits exactly: (a) never said \"missing\", but in {Count(b, Outcomes.RefusedOther)} of {b.Count} (b) runs CreateNew failed with an error other than \"already exists\" " +
                      "(see the HResults in the E1b table). The design's row 1 says \"always fails\" and its wording was about \"already exists\"; decide from those errors.";
        lines.Add(verdict);
    }

    /// <summary>Formats a maximum in seconds, or "-" when nothing was measured.</summary>
    /// <param name="v">The value or null.</param>
    /// <returns>For example <c>3.25 s</c>.</returns>
    private static string FormatMax(double? v) => v is null ? "-" : $"{v.Value:F2} s";

    /// <summary>Adds the reading of E2 (design: "E2: does a reader's open file block the writer's save?").</summary>
    /// <param name="report">The run's report.</param>
    /// <param name="lines">Where to add paragraphs.</param>
    private static void ReadE2(SemanticsReport report, List<string> lines)
    {
        var c1 = report.RunsOf("E2-1");
        var c2 = report.RunsOf("E2-2");
        var c3 = report.RunsOf("E2-3");
        var c4 = report.RunsOf("E2-4");
        var c5 = report.RunsOf("E2-5");
        if (c1.Count + c2.Count + c3.Count + c4.Count + c5.Count == 0) return;

        var facts = new StringBuilder("**E2 (design: result table \"does a reader's open file block the writer's save?\", items 5.3 and 5.4).** ");
        if (c1.Count > 0)
            facts.Append($"Case 1 (control, share ReadWrite), {c1.Count} runs: {OutcomeCounts(c1)}. ");
        if (c2.Count > 0)
            facts.Append($"Case 2 (ReadWrite|Delete), {c2.Count} runs: {Count(c2, Outcomes.RenameOk)} renamed, {Count(c2, Outcomes.RenameRefused)} refused, {Count(c2, Outcomes.Error)} ERROR. ");
        if (c3.Count > 0)
            facts.Append($"Case 3 (reading during the rename), {c3.Count} runs: rename ok in {CountContaining(c3, Outcomes.RenameOk)}; the reader's handle gave " +
                         $"{CountContaining(c3, Outcomes.ReaderOld)} old, {CountContaining(c3, Outcomes.ReaderNew)} new, {CountContaining(c3, Outcomes.ReaderMix)} mix, " +
                         $"{CountContaining(c3, Outcomes.ReaderError)} error, {CountContaining(c3, Outcomes.ReaderIncomplete)} incomplete; {Count(c3, Outcomes.Error)} ERROR. ");
        if (c4.Count > 0)
            facts.Append($"Case 4 (lock file replaced, reader reading), {c4.Count} runs: rename ok in {CountContaining(c4, Outcomes.RenameOk)}; the reader got " +
                         $"{CountContaining(c4, Outcomes.ReaderOld)} old, {CountContaining(c4, Outcomes.ReaderNew)} new, {CountContaining(c4, Outcomes.ReaderMix)} mix, " +
                         $"{CountContaining(c4, Outcomes.ReaderError)} error, {CountContaining(c4, Outcomes.ReaderIncomplete)} incomplete; {Count(c4, Outcomes.Error)} ERROR. ");
        if (c5.Count > 0)
            facts.Append($"Case 5 (delete while held), {c5.Count} runs: {OutcomeCounts(c5)}. ");
        lines.Add(facts.ToString().TrimEnd());

        // Control: case 1 should have been refused.
        if (c1.Count > 0 && Count(c1, Outcomes.RenamedWhileHeld) > 0)
            lines.Add($"Control check: in {Count(c1, Outcomes.RenamedWhileHeld)} of {c1.Count} case 1 runs the rename went through although the partner held the file with share ReadWrite " +
                      "(the design expected \"in use\"). Cases 2 to 5 cannot be read against this control until that is understood.");

        var allCases23 = c2.Count > 0 && c3.Count > 0 && c5.Count > 0;
        if (!allCases23)
        {
            lines.Add("Cases 2, 3 and 5 did not all run, so no row of the design's table can be chosen (each row needs all three).");
        }
        else if (Count(c2, Outcomes.Error) + Count(c3, Outcomes.Error) + Count(c5, Outcomes.Error) > 0)
        {
            lines.Add("Not conclusive: some runs of cases 2, 3 or 5 ended in ERROR (see their notes). Fix the cause and re-run before using the design's table.");
        }
        else
        {
            var twoPasses = Count(c2, Outcomes.RenameOk) == c2.Count;
            var twoRefused = Count(c2, Outcomes.RenameRefused) == c2.Count;
            var threeNeverMixes = CountContaining(c3, Outcomes.ReaderMix) == 0;
            var fiveBlocks = Count(c5, Outcomes.Blocked) > 0 || Count(c5, Outcomes.DeleteRefused) > 0;
            var fiveFree = Count(c5, Outcomes.NotBlocked) == c5.Count;

            if (twoRefused)
                lines.Add("**Row 3 applies** (design: \"2 refused\"): 5.3 doesn't help on Windows servers; readers should hold the file for less time (copy fast, close), and 5.4 carries the rest.");
            else if (twoPasses && threeNeverMixes && fiveFree)
                lines.Add("**Row 1 applies** (design: \"2 passes, 3 never mixes, 5 doesn't block\"): 5.3 as planned for the database and the lock file.");
            else if (twoPasses && fiveBlocks)
                lines.Add("**Row 2 applies** (design: \"2 passes but 5 blocks\"): \"allow replace\" for the database only; lock reads unchanged." +
                          (threeNeverMixes ? "" : " Case 3 also showed a mix, which the design's table does not address: look at the case 3 notes."));
            else
                lines.Add("**No row of the design's table fits cleanly.** " +
                          (!twoPasses ? $"Case 2 renamed in {Count(c2, Outcomes.RenameOk)} of {c2.Count} runs (not all, not none: the runs disagree). " : "") +
                          (twoPasses && !threeNeverMixes ? "Case 2 passed but case 3 showed a mix, which no row covers. " : "") +
                          "Look at the per-run tables; do not pick a row from this.");
        }

        if (c4.Count > 0)
        {
            var ok = c4.Where(r => r.Outcome != Outcomes.Error).ToList();
            lines.Add($"Lock file (case 4, \"as 2 and 3 for the lock\"): rename ok in {CountContaining(ok, Outcomes.RenameOk)} of {c4.Count}, mix in {CountContaining(ok, Outcomes.ReaderMix)}. " +
                      "The design's table has no separate row for it; the design asks for the lock file to be read the same way as cases 2 and 3 (compare the counts above).");
        }
    }
}

namespace NetworkProbe;

/// <summary>
/// Everything recorded about one run of one table (one row of the report): the cells, a short outcome from
/// <see cref="Outcomes"/>, and up to two numbers for the summary row.
/// </summary>
internal sealed record SemanticsRun
{
    /// <summary>The table's id (<see cref="SemanticsCase.Id"/>).</summary>
    public required string CaseId { get; init; }

    /// <summary>Which run of the table this is, from 1.</summary>
    public required int Run { get; init; }

    /// <summary>The short outcome, one of the <see cref="Outcomes"/> constants (possibly two joined with "; ").</summary>
    public required string Outcome { get; init; }

    /// <summary>The row's cells, in the order of <see cref="SemanticsCase.Headers"/>.</summary>
    public required IReadOnlyList<string> Cells { get; init; }

    /// <summary>First number for the summary row (see <see cref="SemanticsCase.MetricNames"/>), or null when not measured.</summary>
    public double? Metric1 { get; init; }

    /// <summary>Second number for the summary row, or null.</summary>
    public double? Metric2 { get; init; }

    /// <summary>Anything worth saying about this run in plain words (errors, a reader that finished too early...).</summary>
    public string Note { get; init; } = "";

    /// <summary>True for an E1b run where CreateNew succeeded over the partner's file: the report's first line.</summary>
    public bool CriticalRun => Outcome == Outcomes.Critical;
}

/// <summary>
/// Everything the share-semantics run learned, gathered for the console and the Markdown report
/// (the counterpart of <see cref="RunReport"/> for this mode).
/// </summary>
internal sealed class SemanticsReport
{
    /// <summary>When the run started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The runner computer's name.</summary>
    public required string Computer { get; init; }

    /// <summary>The user running the runner.</summary>
    public required string UserName { get; init; }

    /// <summary>The command line.</summary>
    public required ProbeOptions Options { get; init; }

    /// <summary>True for <c>--dry-run</c>: the partner ran inside this process and everything is on a local folder.</summary>
    public required bool DryRun { get; init; }

    /// <summary>The shared folder as the runner sees it (the dry-run folder for a dry run).</summary>
    public string Share { get; set; } = "-";

    /// <summary>The partner's endpoint, <c>host:port</c>.</summary>
    public string Partner { get; set; } = "-";

    /// <summary>What the partner said about itself (<c>INFO</c>).</summary>
    public string PartnerInfo { get; set; } = "-";

    /// <summary>The probe's own folder under the share.</summary>
    public string? ProbeFolder { get; set; }

    /// <summary>Control-channel round trips measured by PING (milliseconds).</summary>
    public IReadOnlyList<double> RoundTripsMs { get; set; } = Array.Empty<double>();

    /// <summary>The runner's SMB client settings, or why they could not be read.</summary>
    public (bool Ok, string Text) RunnerSmb { get; set; } = (false, "not read");

    /// <summary>The partner's SMB client settings, or why they could not be read.</summary>
    public (bool Ok, string Text) PartnerSmb { get; set; } = (false, "not read");

    /// <summary>Every run of every table, in the order they ran.</summary>
    public List<SemanticsRun> Runs { get; } = new();

    /// <summary>Why the run stopped early, or null.</summary>
    public string? AbortMessage { get; set; }

    /// <summary>Problems cleaning up at the end.</summary>
    public IReadOnlyList<string> CleanupNotes { get; set; } = Array.Empty<string>();

    /// <summary>True when any E1b run let CreateNew succeed over the partner's file.</summary>
    public bool HasCritical => Runs.Any(r => r.CriticalRun);

    /// <summary>The runs of one table.</summary>
    /// <param name="caseId">The table's id.</param>
    /// <returns>Its runs, in order (empty when the table did not run).</returns>
    public List<SemanticsRun> RunsOf(string caseId) => Runs.Where(r => r.CaseId == caseId).ToList();

    /// <summary>
    /// The process exit code: 0 completed, 1 a CRITICAL result (CreateNew succeeded over an existing file), 2 the run
    /// stopped early.
    /// </summary>
    public int ExitCode => AbortMessage is not null ? 2 : HasCritical ? 1 : 0;
}

namespace NetworkProbe;

/// <summary>
/// Everything one run learned, gathered for the console summary and the Markdown report (spec section 8).
/// </summary>
internal sealed class RunReport
{
    /// <summary>When the run started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>This computer's name.</summary>
    public required string Computer { get; init; }

    /// <summary>The user running the probe.</summary>
    public required string UserName { get; init; }

    /// <summary>True for the plumbing check (<c>--dry-run</c>).</summary>
    public required bool DryRun { get; init; }

    /// <summary>The command line.</summary>
    public required ProbeOptions Options { get; init; }

    /// <summary>The file server's name (real run only).</summary>
    public string? Server { get; set; }

    /// <summary>The server addresses cut and checked.</summary>
    public IReadOnlyList<string> Addresses { get; set; } = Array.Empty<string>();

    /// <summary>Addresses that were left out because they did not answer even with the network up.</summary>
    public IReadOnlyList<string> IgnoredAddresses { get; set; } = Array.Empty<string>();

    /// <summary>The clumsy filter used (real run only).</summary>
    public string? Filter { get; set; }

    /// <summary>The clumsy executable used (real run only).</summary>
    public string? ClumsyPath { get; set; }

    /// <summary>Every clumsy process id started.</summary>
    public IReadOnlyList<int> ClumsyPids { get; set; } = Array.Empty<int>();

    /// <summary>What the logger rig did (log redirect, BugSnag detach).</summary>
    public LoggerRigStatus? LoggerRig { get; set; }

    /// <summary>The probe's folder on the share (or in the dry-run folder).</summary>
    public string? ProbeFolder { get; set; }

    /// <summary>The rig self-checks.</summary>
    public IReadOnlyList<ControlResult> Controls { get; set; } = Array.Empty<ControlResult>();

    /// <summary>One result per case run, in the order they ran.</summary>
    public List<CaseResult> Results { get; } = new();

    /// <summary>Why the run stopped early, or null.</summary>
    public string? AbortMessage { get; set; }

    /// <summary>Problems cleaning up at the end; empty when both folders are gone.</summary>
    public IReadOnlyList<string> CleanupNotes { get; set; } = Array.Empty<string>();

    /// <summary>True when a control failed (the rig is not working, so there are no TermPoint verdicts).</summary>
    public bool RigFailed => Controls.Any(c => c.Passed == false);

    /// <summary>Number of cases that passed.</summary>
    public int Passed => Results.Count(r => r.Verdict == Verdict.Pass);

    /// <summary>Number of cases that failed.</summary>
    public int Failed => Results.Count(r => r.Verdict == Verdict.Fail);

    /// <summary>Number of cases that could not be tested.</summary>
    public int NotTested => Results.Count(r => r.Verdict == Verdict.NotTested);

    /// <summary>Number of cases made with no cut (dry run).</summary>
    public int Simulated => Results.Count(r => r.Verdict == Verdict.Simulated);

    /// <summary>
    /// The process exit code: 0 clean (or a dry run that worked), 1 at least one case FAILED, 2 the rig did not work or
    /// the run stopped early.
    /// </summary>
    public int ExitCode => RigFailed || AbortMessage is not null ? 2 : Failed > 0 ? 1 : 0;

    /// <summary>
    /// The one-line summary that opens the report, in plain words: "N passed, N failed, N not tested; the rig's
    /// self-checks passed / failed".
    /// </summary>
    public string Headline
    {
        get
        {
            if (DryRun)
                return $"DRY RUN (nothing was cut): {Simulated} case runs were made against a local folder, to check the plumbing. None of them is a verdict.";

            var rig = RigFailed ? "FAILED" : Controls.Count == 0 ? "did not run" : "passed";
            var counts = RigFailed
                ? "TEST RIG NOT WORKING: no TermPoint verdicts are reported"
                : $"{Passed} passed, {Failed} failed, {NotTested} not tested";
            return $"{counts}; the rig's self-checks {rig}." + (AbortMessage is null ? "" : " The run stopped early (see below).");
        }
    }
}

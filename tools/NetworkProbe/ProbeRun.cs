namespace NetworkProbe;

/// <summary>
/// One whole run, from refusals to report: the real run (<c>--share</c>) and the plumbing check (<c>--dry-run</c>) are
/// the same sequence with a different <see cref="INetworkRig"/>.
///
/// <para>Sequence: refusals, resolve the server and its filter, create the folders, swap the logger, take the safety nets
/// down, run the two controls, run the cases (retrying a mid-operation case once with a larger file when it finished
/// before the cut), then, in a <c>finally</c>: stop clumsy, wait for the network, delete both folders, write the report.</para>
/// </summary>
internal static class ProbeRun
{
    /// <summary>Most the large file may grow to when a mid-operation case finishes before the cut (megabytes).</summary>
    private const int MaxRetryMb = 256;

    /// <summary>
    /// Runs everything.
    /// </summary>
    /// <param name="options">The parsed command line (not <c>--list</c>, not <c>--help</c>).</param>
    /// <returns>The process exit code: 0 clean, 1 a case FAILED, 2 the rig did not work or the run stopped early.</returns>
    /// <exception cref="ProbeRefusalException">A safety rule is not met; nothing was cut or written.</exception>
    public static async Task<int> RunAsync(ProbeOptions options)
    {
        var dry = options.IsDryRun;
        var report = new RunReport
        {
            StartedAt = DateTimeOffset.Now,
            Computer = Environment.MachineName,
            UserName = Environment.UserName,
            DryRun = dry,
            Options = options
        };

        void Say(string text) => ConsoleOut.Line($"[{ProbeClock.Now:mm\\:ss}] {text}");

        using var cts = new CancellationTokenSource();
        ClumsyController? clumsy = null;
        INetworkRig? rig = null;
        ProbeWorkspace? workspace = null;

        try
        {
            // ── 1. Refusals, target, folders ────────────────────────────────────────────────────────────────
            CutTarget? target = null;
            if (!dry)
            {
                Preflight.RequireAll();
                var clumsyPath = ClumsyLocator.Resolve(options.ClumsyPath);
                report.ClumsyPath = clumsyPath;
                Say($"Using clumsy: {clumsyPath} [{ClumsyLocator.PeMachine(clumsyPath)}]");

                target = await CutTargetResolver.ResolveAsync(options.Share!, options.PortOnly, cts.Token).ConfigureAwait(false);
                report.Server = target.Server;
                report.Addresses = target.Addresses.Select(a => a.ToString()).ToList();
                report.IgnoredAddresses = target.IgnoredAddresses.Select(a => a.ToString()).ToList();
                report.Filter = target.Filter;
                Say($"Server {target.Server}: {string.Join(", ", report.Addresses)}");
                if (report.IgnoredAddresses.Count > 0)
                    Say($"Left out (no answer on port 445 right now): {string.Join(", ", report.IgnoredAddresses)}");
                Say($"Filter: {target.Filter}");
                if (target.PortOnly)
                    Say("NOTE: --port-only also cuts SMB to ANY other server on this computer for the duration of each cut.");
            }
            else
            {
                Say("DRY RUN: no clumsy, no server, no cut. Every case runs against a local folder and is reported SIMULATED.");
            }

            var shareParent = dry ? options.DryRunFolder! : options.Share!;
            workspace = ProbeWorkspace.Create(shareParent, createParentIfMissing: dry);
            report.ProbeFolder = workspace.ShareRoot;
            Say($"Probe folder: {workspace.ShareRoot}");
            Say($"Local temp folder: {workspace.LocalRoot}");

            // ── 2. Logger: BEFORE the first NetworkFileOps call ──────────────────────────────────────────────
            var logger = new RecordingProbeLogger();
            var loggerStatus = LoggerRig.Install(logger, workspace.LogDirectory);
            report.LoggerRig = loggerStatus;
            Say($"Logger rig: App.Logger replaced ({loggerStatus.OriginalLoggerType} -> recorder); log folder -> temp; " +
                $"BugSnag client {(loggerStatus.BugsnagWasAttached ? "detached" : "was not attached")}; " +
                $"{loggerStatus.DetachedHandlers.Count} BugSnag process-wide handler(s) removed.");
            if (!loggerStatus.Safe)
                throw new ProbeRefusalException(
                    "Could not keep BugSnag out of this run (" +
                    string.Join("; ", loggerStatus.RemainingBugsnagHandlers) +
                    "). Refusing to run: a crash of the probe could be reported as a TermPoint error.");

            // ── 3. The rig, and the safety nets ──────────────────────────────────────────────────────────────
            if (dry)
            {
                rig = new SimulatedNetworkRig();
                Console.CancelKeyPress += (_, e) => { e.Cancel = !cts.IsCancellationRequested; cts.Cancel(); };
            }
            else
            {
                clumsy = new ClumsyController(report.ClumsyPath!, target!.Filter, options.LagMs, Say);
                clumsy.InstallSafetyNets(() => cts.Cancel());
                rig = new ClumsyNetworkRig(clumsy, target, workspace.ShareRoot);
                Say("The network to the server will be cut several times. Press Ctrl+C to stop; clumsy is stopped on the way out.");
            }

            var ctx = new ProbeContext
            {
                Options = options,
                Rig = rig,
                Logger = logger,
                Workspace = workspace,
                Say = Say,
                FilterNamesAddresses = !dry && !options.PortOnly
            };
            var runner = new CaseRunner(ctx);

            // ── 4. Controls first ────────────────────────────────────────────────────────────────────────────
            Say("Running the two rig controls first.");
            report.Controls = await new RigControls(ctx, runner).RunAsync(cts.Token).ConfigureAwait(false);
            foreach (var control in report.Controls)
                Say($"control [{control.OutcomeText}] {control.Name}: {control.Detail}");

            if (report.RigFailed)
            {
                Say("TEST RIG NOT WORKING: no TermPoint verdicts will be reported.");
            }
            else
            {
                // ── 5. The cases ─────────────────────────────────────────────────────────────────────────────
                await RunCasesAsync(ctx, runner, report, cts.Token).ConfigureAwait(false);
            }
        }
        catch (ProbeAbortException ex)
        {
            report.AbortMessage = ex.Message;
            Say($"STOPPED: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            report.AbortMessage = "Cancelled with Ctrl+C.";
            Say("Cancelled with Ctrl+C: restoring the network and cleaning up.");
        }
        catch (Exception ex) when (ex is not ProbeRefusalException)
        {
            // Anything unforeseen still gets the full clean-up below and a report that says what happened.
            report.AbortMessage = $"Unexpected error: {ex.GetType().Name}: {ex.Message}";
            Say($"STOPPED by an unexpected error: {ex}");
        }
        finally
        {
            // ── 6. Never leave the network cut; clean up only once it is back ───────────────────────────────
            report.ClumsyPids = rig?.ClumsyPids ?? Array.Empty<int>();
            await RestoreAndCleanUpAsync(rig, workspace, report, Say).ConfigureAwait(false);
        }

        // ── 7. Report ────────────────────────────────────────────────────────────────────────────────────────
        ReportWriter.PrintSummary(report);
        try
        {
            var path = ReportWriter.WriteMarkdown(report, RepoLocator.ReportDirectory(options.ReportDir));
            ConsoleOut.Line();
            ConsoleOut.Line($"Report written: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConsoleOut.Line($"The report could not be written: {ex.Message}");
        }
        return report.ExitCode;
    }

    /// <summary>
    /// Runs every selected case: cut-before-call cases once, mid-operation cases <c>--repeat</c> times. A mid-operation
    /// case that finishes before the cut is retried once with the large file doubled (never reported as a pass), and the
    /// larger size is kept for the repetitions that follow.
    /// </summary>
    /// <param name="ctx">The run's shared state.</param>
    /// <param name="runner">The case runner.</param>
    /// <param name="report">Where results are collected.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <exception cref="ProbeAbortException">The run cannot go on.</exception>
    private static async Task RunCasesAsync(ProbeContext ctx, CaseRunner runner, RunReport report, CancellationToken ct)
    {
        var bigMb = ctx.Options.BigMb;
        foreach (var def in CaseCatalog.Select(ctx.Options.Only))
        {
            var repetitions = def.Cut == CutPoint.MidOperation ? ctx.Options.Repeat : 1;
            for (var repetition = 1; repetition <= repetitions; repetition++)
            {
                var result = await runner.RunAsync(def, repetition, bigMb, 1, ct).ConfigureAwait(false);

                if (result.RetryWithLargerFile && !ctx.Simulated && bigMb < MaxRetryMb)
                {
                    var bigger = Math.Min(bigMb * 2, MaxRetryMb);
                    ctx.Say($"the operation finished before the cut; retrying once with {bigger} MB instead of {bigMb} MB");
                    bigMb = bigger; // keep the larger size for the repetitions that follow
                    var retry = await runner.RunAsync(def, repetition, bigMb, 2, ct).ConfigureAwait(false);
                    result = retry with { Notes = $"retried with {bigger} MB because the first attempt finished before the cut" };
                }

                report.Results.Add(result);
                ReportWriter.PrintCaseLine(result);
            }
        }
    }

    /// <summary>
    /// Stops clumsy, waits for the network to be back (up to a minute), deletes the probe's two folders and records
    /// anything that could not be deleted. Never throws: this runs in a <c>finally</c>.
    /// </summary>
    /// <param name="rig">The rig, or null if it was never created.</param>
    /// <param name="workspace">The workspace, or null if it was never created.</param>
    /// <param name="report">Receives the clean-up notes.</param>
    /// <param name="say">Console output.</param>
    private static async Task RestoreAndCleanUpAsync(INetworkRig? rig, ProbeWorkspace? workspace, RunReport report, Action<string> say)
    {
        var notes = new List<string>();
        try
        {
            if (rig is not null)
            {
                // Stops clumsy (by pid) first; then wait for the share so the delete does not hang on a dead connection.
                var recovery = await rig.StopAndWaitForRecoveryAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
                if (recovery is null)
                    notes.Add("The share did not answer within 60 s after clumsy was stopped, so the clean-up may be incomplete.");
                rig.Dispose();
            }

            if (workspace is not null)
            {
                say("Cleaning up the probe's folders.");
                notes.AddRange(workspace.DeleteAll());
            }
        }
        catch (Exception ex)
        {
            notes.Add($"Clean-up failed: {ex.Message}");
        }
        report.CleanupNotes = notes;
    }
}

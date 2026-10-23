namespace NetworkProbe;

/// <summary>
/// Runs one case through its whole cycle (spec section 5.1): network up, set-up, cut, confirm the cut, measure while
/// the cut is held, restore, record, clean up. Handles both ways of cutting: before the call (CB) and in the middle of
/// a long operation (CM, spec section 5.4).
/// </summary>
internal sealed class CaseRunner
{
    /// <summary>How long to wait for the share to answer before a case starts, and again after a cut (spec: 120 s).</summary>
    private static readonly TimeSpan NetworkUpWait = TimeSpan.FromSeconds(120);

    /// <summary>How long a cut may take to be confirmed before the case is NOT TESTED (spec: 10 s).</summary>
    private static readonly TimeSpan CutConfirmWait = TimeSpan.FromSeconds(10);

    /// <summary>How long to keep waiting for a call that was still running when the hold ended (spec: cap 120 s).</summary>
    private static readonly TimeSpan LateCompletionWait = TimeSpan.FromSeconds(120);

    /// <summary>How long a call gets to hand back its task before it is reported as blocking the probe's thread.</summary>
    private static readonly TimeSpan CallReturnWait = TimeSpan.FromSeconds(1.5);

    /// <summary>Minimum time a long operation must have been running before the cut (spec 5.4 step 3: 1.5 s).</summary>
    private static readonly TimeSpan UnderWayMinimum = TimeSpan.FromSeconds(1.5);

    /// <summary>Longest to wait for a long operation to show progress before giving up on the case.</summary>
    private static readonly TimeSpan UnderWayMaximum = TimeSpan.FromSeconds(90);

    /// <summary>Time clumsy needs after starting before its lag is really in effect (about a second).</summary>
    private static readonly TimeSpan LagEngage = TimeSpan.FromSeconds(1.2);

    /// <summary>Patience for the destination-size look before a copy is cut (spec 5.4 step 3: 2 s).</summary>
    private static readonly TimeSpan SizeCheckTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Bytes this process must have read since a read-watched call started before it counts as reading
    /// (<see cref="CaseDefinition.WatchProcessReads"/>): 1 MB, far more than anything else the probe reads meanwhile.
    /// </summary>
    private const long ReadEvidenceBytes = 1L << 20;

    /// <summary>The shared state of the run.</summary>
    private readonly ProbeContext _ctx;

    /// <summary>Creates a runner.</summary>
    /// <param name="ctx">The run's shared state.</param>
    public CaseRunner(ProbeContext ctx) => _ctx = ctx;

    /// <summary>
    /// Runs the case once, start to finish, and cleans its files up.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Which repetition this is (1 for a case run once).</param>
    /// <param name="bigMb">Size of the large file for this run (used by the cases that need one).</param>
    /// <param name="attempt">1 for the first try, 2 for the retry with a larger file.</param>
    /// <param name="ct">Cancels the run (Ctrl+C).</param>
    /// <returns>The recorded result.</returns>
    /// <exception cref="ProbeAbortException">The run cannot go on (the share did not come back, set-up failed, the filter does not cut).</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public async Task<CaseResult> RunAsync(CaseDefinition def, int repetition, int bigMb, int attempt, CancellationToken ct)
    {
        var size = def.Needs.HasFlag(Fixtures.BigLocal) || def.Needs.HasFlag(Fixtures.BigOnShare) ? $", {bigMb} MB" : "";
        var rep = def.Cut == CutPoint.MidOperation ? $" #{repetition}" : "";
        _ctx.Say($"--- {def.Name} ({def.ModeLabel}){rep}{size}{(attempt > 1 ? ", retry" : "")}");

        // 1. Network up?
        await RequireNetworkUpAsync($"before case '{def.Name}' could start", ct).ConfigureAwait(false);

        // 2. Set up, with the network normal.
        var files = BuildFiles(def, repetition, bigMb, attempt);

        CaseResult result;
        if (_ctx.Simulated)
        {
            result = await RunSimulatedAsync(def, repetition, bigMb, files, ct).ConfigureAwait(false);
        }
        else if (def.Cut == CutPoint.BeforeCall)
        {
            await SettleAsync(def, ct).ConfigureAwait(false);
            result = await RunCutBeforeAsync(def, repetition, bigMb, files, ct).ConfigureAwait(false);
        }
        else
        {
            result = await RunCutMidAsync(def, repetition, bigMb, files, ct).ConfigureAwait(false);
        }

        // 7. Clean up (the network is back by now).
        if (!_ctx.Workspace.DeleteCase(files))
            _ctx.Say($"note: could not delete the folder of case '{def.Name}' yet; it is retried at the end.");
        return result;
    }

    // ── CB: cut first, then call ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cut-before-call: cut the network, confirm it, make the call, measure.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="files">The case's files.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The recorded result.</returns>
    private async Task<CaseResult> RunCutBeforeAsync(
        CaseDefinition def, int repetition, int bigMb, CaseFiles files, CancellationToken ct)
    {
        _ctx.Rig.StartDrop();
        var confirmed = await ConfirmCutOrAbortAsync(ct).ConfigureAwait(false);
        if (confirmed is null)
            return await NotTestedBecauseCutFailedAsync(def, repetition, bigMb, ct).ConfigureAwait(false);

        var mark = _ctx.Logger.Mark();
        var run = new OperationRun(() => def.Execute(files));
        run.Start();
        return await MeasureAsync(def, repetition, bigMb, run, confirmed.Value, mark, ct).ConfigureAwait(false);
    }

    // ── CM: slow it down, start the call, cut in the middle ───────────────────────────────────────────────

    /// <summary>
    /// Cut-mid-operation (spec section 5.4): lag only, start the call, wait until it is demonstrably in progress, swap
    /// the lag for the drop, confirm the cut. A call that finishes before the cut is NOT TESTED, never a pass.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="files">The case's files.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The recorded result.</returns>
    private async Task<CaseResult> RunCutMidAsync(
        CaseDefinition def, int repetition, int bigMb, CaseFiles files, CancellationToken ct)
    {
        // 1. Lag only, so the operation is slow. Give clumsy its second to start filtering.
        _ctx.Rig.StartLag();
        await Task.Delay(LagEngage, ct).ConfigureAwait(false);

        // 2. Start the operation (do not wait for it). For a read-watched case, note how much this process has read so
        //    far, so step 3 can see the read loop itself making progress.
        var mark = _ctx.Logger.Mark();
        var readBaseline = def.WatchProcessReads ? ProcessIo.ReadBytes() : -1;
        var run = new OperationRun(() => def.Execute(files));
        run.Start();

        // 3. Wait until it is clearly under way.
        var progress = await WaitUntilUnderWayAsync(def, files, run, readBaseline, ct).ConfigureAwait(false);
        if (progress != UnderWay.Yes)
        {
            // Nothing to cut. Put the network back, let the call end, and report why.
            var recovery = await _ctx.Rig.StopAndWaitForRecoveryAsync(NetworkUpWait, ct).ConfigureAwait(false);
            await Task.WhenAny(run.Completion, Task.Delay(LateCompletionWait, ct)).ConfigureAwait(false);
            var early = progress == UnderWay.FinishedEarly;
            return Build(def, repetition, bigMb, run, mark, recovery,
                new VerdictOutcome(Verdict.NotTested,
                    early ? "the operation finished before the cut (the file was too small for the network's speed)"
                          : $"the operation showed no progress within {UnderWayMaximum.TotalSeconds:F0} s, so there was nothing to cut"),
                resultAfterCut: null, completedAfterRestore: false, retryWithLarger: early);
        }

        // 4. Swap the lag for the black hole (one restart: about 1 s with no impairment, harmless here), then 5. confirm.
        _ctx.Rig.StartDrop();
        var confirmed = await ConfirmCutOrAbortAsync(ct).ConfigureAwait(false);
        if (confirmed is null)
        {
            await Task.WhenAny(run.Completion, Task.Delay(LateCompletionWait, ct)).ConfigureAwait(false);
            return await NotTestedBecauseCutFailedAsync(def, repetition, bigMb, ct).ConfigureAwait(false);
        }

        return await MeasureAsync(def, repetition, bigMb, run, confirmed.Value, mark, ct).ConfigureAwait(false);
    }

    /// <summary>Whether a long operation is demonstrably in progress.</summary>
    private enum UnderWay
    {
        /// <summary>At least 1.5 s elapsed, not finished, and (for a copy) the destination already has bytes.</summary>
        Yes,

        /// <summary>The operation finished before it could be cut.</summary>
        FinishedEarly,

        /// <summary>It never showed progress within the maximum wait.</summary>
        NoProgress
    }

    /// <summary>
    /// Waits until the operation is clearly under way (spec 5.4 step 3): at least 1.5 s elapsed, the task not
    /// completed, for a copy the destination file on the share already larger than 0 (looked at through a plain
    /// <c>FileInfo</c> on a background thread with a 2 s patience; the lag makes this slow but fine), and for a
    /// read-watched case this process having read at least <see cref="ReadEvidenceBytes"/> since the call started.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="files">The case's files.</param>
    /// <param name="run">The running operation.</param>
    /// <param name="readBaseline">
    /// This process's read byte count when the call started, or -1 when not watched or unknown (then time alone counts).
    /// </param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>Whether the cut can go ahead.</returns>
    private static async Task<UnderWay> WaitUntilUnderWayAsync(
        CaseDefinition def, CaseFiles files, OperationRun run, long readBaseline, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (run.IsCompleted) return UnderWay.FinishedEarly;

            var elapsed = ProbeClock.Now - run.StartedAt;
            if (elapsed > UnderWayMaximum) return UnderWay.NoProgress;

            if (elapsed >= UnderWayMinimum)
            {
                var grown = !def.WatchDestination
                    || await Task.Run(() => RigIo.TryGetLength(files.CopyDest, SizeCheckTimeout), CancellationToken.None).ConfigureAwait(false) is > 0;
                if (grown && def.WatchProcessReads && readBaseline >= 0)
                    grown = ProcessIo.ReadBytes() - readBaseline >= ReadEvidenceBytes;
                if (grown)
                    return run.IsCompleted ? UnderWay.FinishedEarly : UnderWay.Yes;
            }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    // ── Measure (both modes) ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Measures a call while the cut is held, restores the network, waits for a call that outlived the hold, and
    /// judges (spec sections 5.1 steps 5 to 7 and 5.6).
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="run">The running call.</param>
    /// <param name="confirmed">Probe-clock time the cut was confirmed; all timings are measured from here.</param>
    /// <param name="mark">Log position from before the call started.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The recorded result.</returns>
    private async Task<CaseResult> MeasureAsync(
        CaseDefinition def, int repetition, int bigMb, OperationRun run, TimeSpan confirmed, int mark, CancellationToken ct)
    {
        var holdEnd = confirmed + TimeSpan.FromSeconds(_ctx.Options.HoldSeconds);

        // Did the call hand back its task? (A call that blocks its caller is a finding of its own.)
        await Task.WhenAny(run.CallReturned, Task.Delay(CallReturnWait, ct)).ConfigureAwait(false);

        // Hold the cut for the whole of --hold, unless the call had already finished before the cut (then there is
        // nothing to measure and no reason to keep the network down).
        var finishedBeforeCut = run.CompletedAt is { } early && early <= confirmed;
        if (!finishedBeforeCut)
        {
            var remaining = holdEnd - ProbeClock.Now;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct).ConfigureAwait(false);
        }

        // Restore, and start timing the recovery at once; meanwhile give a call that is still running time to end.
        var restoredAt = ProbeClock.Now;
        var recoveryTask = _ctx.Rig.StopAndWaitForRecoveryAsync(NetworkUpWait, ct);
        if (!run.IsCompleted)
            await Task.WhenAny(run.Completion, Task.Delay(LateCompletionWait, ct)).ConfigureAwait(false);
        var recovery = await recoveryTask.ConfigureAwait(false);

        var completedAt = run.CompletedAt;
        TimeSpan? afterCut = completedAt is { } done ? done - confirmed : null;
        var measurement = new Measurement(
            CallReturn: run.CallReturn,
            CompletedAfterCut: afterCut,
            CompletedBeforeCut: completedAt is { } d1 && d1 <= confirmed,
            CompletedAfterRestore: completedAt is { } d2 && d2 > restoredAt,
            GaveUp: run.Result?.GaveUp ?? false,
            ErrorType: run.Error?.GetType().Name);
        var outcome = RequireLanding(def,
            VerdictJudge.Judge(measurement, def.PassWindow ?? VerdictJudge.DefaultWindow),
            LogAnalysis.LandedIn(_ctx.Logger.Since(mark)));

        return Build(def, repetition, bigMb, run, mark, recovery, outcome, afterCut,
            measurement.CompletedAfterRestore, retryWithLarger: measurement.CompletedBeforeCut && def.Cut == CutPoint.MidOperation);
    }

    /// <summary>
    /// Applies <see cref="CaseDefinition.RequiredLanding"/>: a PASS caught in a different step than the one the case
    /// is about proves nothing about that step, so it becomes NOT TESTED. Failures are left alone (a call that waited
    /// for the network is a finding wherever it waited).
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="outcome">The verdict from <see cref="VerdictJudge"/>.</param>
    /// <param name="landedIn">The step the stall was reported in (<see cref="LogAnalysis.LandedIn"/>).</param>
    /// <returns>The verdict, possibly downgraded to NOT TESTED.</returns>
    private static VerdictOutcome RequireLanding(CaseDefinition def, VerdictOutcome outcome, string landedIn)
        => def.RequiredLanding is { } required
           && outcome.Verdict == Verdict.Pass
           && !string.Equals(landedIn, required, StringComparison.Ordinal)
            ? new VerdictOutcome(Verdict.NotTested,
                $"gave up in time, but the stall was caught in '{landedIn}', not in '{required}', which this case is about")
            : outcome;

    // ── Dry run ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dry run: make the real call against the local folder with no cut at all, and record what it did. Never a verdict.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="files">The case's files.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>A result whose verdict is <see cref="Verdict.Simulated"/>.</returns>
    private async Task<CaseResult> RunSimulatedAsync(
        CaseDefinition def, int repetition, int bigMb, CaseFiles files, CancellationToken ct)
    {
        var mark = _ctx.Logger.Mark();
        var run = new OperationRun(() => def.Execute(files));
        run.Start();
        await Task.WhenAny(run.Completion, Task.Delay(LateCompletionWait, ct)).ConfigureAwait(false);

        var took = run.CompletedAt is { } done ? done - run.StartedAt : (TimeSpan?)null;
        var outcome = new VerdictOutcome(Verdict.Simulated,
            $"dry run: the call was made with no cut (it took {TextFormat.Seconds2(took)}), so there is no verdict");
        return Build(def, repetition, bigMb, run, mark, recovery: null, outcome, resultAfterCut: null,
            completedAfterRestore: false, retryWithLarger: false);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits until the network is up, or aborts the run.
    /// </summary>
    /// <param name="when">Where in the run this is, for the message.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="ProbeAbortException">The share did not answer within 120 s.</exception>
    private async Task RequireNetworkUpAsync(string when, CancellationToken ct)
    {
        if (!await _ctx.Rig.WaitUntilUpAsync(NetworkUpWait, ct).ConfigureAwait(false))
            throw new ProbeAbortException(
                $"The share did not answer for {NetworkUpWait.TotalSeconds:F0} s {when}. The run was stopped; the network is back to normal as far as clumsy is concerned, so check the server and the cable.");
    }

    /// <summary>
    /// Creates the case's files; a share that stops answering here ends the run.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="attempt">1 for the first try, 2 for the retry.</param>
    /// <returns>The case's paths.</returns>
    /// <exception cref="ProbeAbortException">Set-up failed.</exception>
    private CaseFiles BuildFiles(CaseDefinition def, int repetition, int bigMb, int attempt)
    {
        var caseId = $"{def.Name}-{repetition}{(attempt > 1 ? $"-try{attempt}" : "")}";
        try
        {
            return _ctx.Workspace.BuildFor(def, caseId, bigMb);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            throw new ProbeAbortException($"Setting up case '{def.Name}' failed with the network normal: {ex.Message}");
        }
    }

    /// <summary>
    /// For a cut-before-call case: lets SMB's information caches (about 10 s) expire between creating the files and
    /// cutting the network, because a call answered from the cache would not need the network and prove nothing.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="ct">Cancels the wait.</param>
    private async Task SettleAsync(CaseDefinition def, CancellationToken ct)
    {
        var seconds = _ctx.Options.SettleSeconds;
        if (seconds <= 0 || def.Needs == Fixtures.None) return;
        _ctx.Say($"waiting {seconds} s so SMB's caches expire before the cut");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the cut to be confirmed (10 s). If it is not, and no cut has ever been confirmed in this run while
    /// the filter names the server's addresses, the address syntax is the likely cause: the run is aborted with the
    /// advice to try <c>--port-only</c> (spec section 5.2).
    /// </summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The probe-clock time of the confirming check, or null if the cut was not confirmed in time.</returns>
    /// <exception cref="ProbeAbortException">The first cut of the run was not confirmed and the filter names addresses.</exception>
    private async Task<TimeSpan?> ConfirmCutOrAbortAsync(CancellationToken ct)
    {
        var confirmed = await _ctx.Rig.ConfirmCutAsync(CutConfirmWait, ct).ConfigureAwait(false);
        if (confirmed is not null)
        {
            _ctx.CutEverConfirmed = true;
            _ctx.Say($"cut confirmed ({ProbeClock.Stamp(confirmed.Value)}): a fresh connection to the server fails on every address");
            return confirmed;
        }

        _ctx.Say($"cut NOT confirmed within {CutConfirmWait.TotalSeconds:F0} s");
        if (!_ctx.CutEverConfirmed && _ctx.FilterNamesAddresses)
        {
            await _ctx.Rig.StopAndWaitForRecoveryAsync(NetworkUpWait, ct).ConfigureAwait(false);
            throw new ProbeAbortException(
                "The very first cut was never confirmed, with a filter that names the server's addresses. The address syntax of the filter may be what failed: try again with --port-only (which also cuts SMB to any other server).");
        }
        return null;
    }

    /// <summary>
    /// Records a case that could not be tested because the cut was not confirmed, and puts the network back.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>A NOT TESTED result.</returns>
    private async Task<CaseResult> NotTestedBecauseCutFailedAsync(
        CaseDefinition def, int repetition, int bigMb, CancellationToken ct)
    {
        var recovery = await _ctx.Rig.StopAndWaitForRecoveryAsync(NetworkUpWait, ct).ConfigureAwait(false);
        return new CaseResult
        {
            Name = def.Name,
            Mode = def.ModeLabel,
            Repetition = repetition,
            BigMb = bigMb,
            Verdict = Verdict.NotTested,
            Reason = "cut not confirmed within 10 s",
            Recovery = recovery
        };
    }

    /// <summary>
    /// Assembles the recorded result of a case from what its run did.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="repetition">Repetition number.</param>
    /// <param name="bigMb">Large-file size.</param>
    /// <param name="run">The run.</param>
    /// <param name="mark">Log position from before the call started.</param>
    /// <param name="recovery">How long the share took to come back, or null.</param>
    /// <param name="outcome">The verdict and its reason.</param>
    /// <param name="resultAfterCut">Time from the cut being confirmed until completion, or null.</param>
    /// <param name="completedAfterRestore">Whether the call finished only after the network came back.</param>
    /// <param name="retryWithLarger">Whether a larger file might turn a NOT TESTED into a verdict.</param>
    /// <returns>The result.</returns>
    private CaseResult Build(
        CaseDefinition def, int repetition, int bigMb, OperationRun run, int mark, TimeSpan? recovery,
        VerdictOutcome outcome, TimeSpan? resultAfterCut, bool completedAfterRestore, bool retryWithLarger)
    {
        var log = _ctx.Logger.Since(mark);
        var resultText = run.Result?.Text
            ?? (run.Error is { } error ? $"threw {error.GetType().Name}: {error.Message}" : "(did not complete)");
        return new CaseResult
        {
            Name = def.Name,
            Mode = def.ModeLabel,
            Repetition = repetition,
            BigMb = def.Needs.HasFlag(Fixtures.BigLocal) || def.Needs.HasFlag(Fixtures.BigOnShare) ? bigMb : 0,
            Verdict = outcome.Verdict,
            Reason = outcome.Reason,
            CallReturn = run.CallReturn,
            ResultAfterCut = resultAfterCut,
            CompletedAfterRestore = completedAfterRestore,
            ResultText = resultText,
            LandedIn = LogAnalysis.LandedIn(log),
            Recovery = recovery,
            Log = log,
            RetryWithLargerFile = retryWithLarger
        };
    }
}

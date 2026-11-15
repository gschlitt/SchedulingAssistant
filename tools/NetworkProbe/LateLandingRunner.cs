namespace NetworkProbe;

/// <summary>
/// The late-landing experiment ("E1", <c>--late-landing</c>). <c>NetworkFileOps</c> races each share operation against a
/// 5 s deadline and ABANDONS the inner operation when the deadline passes; the inner <c>File.Delete</c> or
/// <c>File.Move</c> keeps running on its thread. The question: when the network drops while a delete or a
/// rename-over-existing is in flight and later comes back, does the abandoned operation still take effect on the server
/// ("lands late")? If so, how long after the call was issued, as a function of how long the cut was held?
///
/// <para>One cycle (<see cref="RunCycleAsync"/>): wait for the share, create the fixtures, let SMB's caches settle, read
/// the folder as a baseline, cut and confirm the cut, issue the real call on its dedicated thread, hold the cut for the
/// stated time counted from the call being issued, stop clumsy, wait for the share to answer, then look at the folder
/// once a second with plain file calls (never <c>NetworkFileOps</c>, so the recorder's log stays clean) until it
/// changes, the abandoned operation is seen to end, or the observation time is up.</para>
///
/// <para>What the log can and cannot tell: <c>NetworkFileOps</c> logs an abandoned operation only when it FAULTS
/// ("abandoned ... task faulted after timeout"). A successful late finish writes no line, so a late landing is seen
/// from the folder, not from the log; the log line, when there is one, shows the operation DIED (for example when the
/// SMB client gave up) and when.</para>
/// </summary>
internal sealed class LateLandingRunner
{
    /// <summary>How long to wait for the share to answer before a cycle starts, and again after the cut.</summary>
    private static readonly TimeSpan NetworkUpWait = TimeSpan.FromSeconds(120);

    /// <summary>How long the folder is watched after the share first answers (real run).</summary>
    private static readonly TimeSpan ObservationTime = TimeSpan.FromSeconds(120);

    /// <summary>How long the folder is watched in a dry run, so the loop is exercised without a two-minute wait.</summary>
    private static readonly TimeSpan DryObservationTime = TimeSpan.FromSeconds(3);

    /// <summary>Pause between two looks at the folder.</summary>
    private static readonly TimeSpan ObservationInterval = TimeSpan.FromSeconds(1);

    /// <summary>Patience for one look at the folder; a look that hangs is abandoned and counted as "could not look".</summary>
    private static readonly TimeSpan ObservationPatience = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the folder keeps being watched after the abandoned operation was seen to end with an error: once it has
    /// ended nothing more can land, so a few unchanged looks are enough.
    /// </summary>
    private static readonly TimeSpan AfterFaultTime = TimeSpan.FromSeconds(5);

    /// <summary>The shared state of the run.</summary>
    private readonly ProbeContext _ctx;

    /// <summary>The case runner, borrowed for its network-up wait and cut confirmation.</summary>
    private readonly CaseRunner _runner;

    /// <summary>Creates the runner.</summary>
    /// <param name="ctx">The run's shared state.</param>
    /// <param name="runner">The case runner.</param>
    public LateLandingRunner(ProbeContext ctx, CaseRunner runner)
    {
        _ctx = ctx;
        _runner = runner;
    }

    /// <summary>
    /// Runs every late-landing case for every hold, <c>--repeat</c> times each, and collects the results.
    /// </summary>
    /// <param name="report">Where the results are collected.</param>
    /// <param name="ct">Cancels the run (Ctrl+C).</param>
    /// <exception cref="ProbeAbortException">The run cannot go on (the share did not come back, set-up failed, the filter does not cut).</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public async Task RunAllAsync(RunReport report, CancellationToken ct)
    {
        var options = _ctx.Options;
        _ctx.Say($"Late-landing experiment: {LateLandingCatalog.All.Count} cases x {options.Holds.Count} holds ({string.Join(", ", options.Holds)} s) x {options.LateRepeat} repetitions.");
        foreach (var c in LateLandingCatalog.All)
            foreach (var hold in options.Holds)
                for (var repetition = 1; repetition <= options.LateRepeat; repetition++)
                {
                    var result = await RunCycleAsync(c, hold, repetition, ct).ConfigureAwait(false);
                    report.LateResults.Add(result);
                    LateLandingReport.PrintCycleLine(result);
                }
    }

    /// <summary>
    /// Runs one cycle and cleans its folder up.
    /// </summary>
    /// <param name="c">The case.</param>
    /// <param name="holdSeconds">Seconds the cut is held, counted from the call being issued.</param>
    /// <param name="repetition">Which repetition this is.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The recorded result.</returns>
    /// <exception cref="ProbeAbortException">The run cannot go on.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    private async Task<LateLandingResult> RunCycleAsync(LateLandingCase c, int holdSeconds, int repetition, CancellationToken ct)
    {
        _ctx.Say($"--- {c.Name}: cut held {holdSeconds} s, repetition {repetition}");
        var cycle = new Cycle(c, holdSeconds, repetition);

        // 1. Network up, then the fixtures, with the network normal.
        await _runner.RequireNetworkUpAsync($"before late-landing case '{c.Name}' could start", ct).ConfigureAwait(false);
        string dir;
        try
        {
            dir = _ctx.Workspace.BuildTextFolder($"{c.Name}-h{holdSeconds}-{repetition}", c.Fixtures);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            throw new ProbeAbortException($"Setting up case '{c.Name}' failed with the network normal: {ex.Message}");
        }

        try
        {
            await RunWithFixturesAsync(cycle, dir, ct).ConfigureAwait(false);
        }
        finally
        {
            // 6. Clean up (the network is back by now, or the run is being torn down and DeleteAll will finish the job).
            if (!_ctx.Workspace.DeleteFolderOnce(dir))
                _ctx.Say($"note: could not delete the folder of case '{c.Name}' yet; it is retried at the end.");
        }
        return cycle.Result!;
    }

    /// <summary>
    /// Steps 1 to 5 of a cycle, once the fixtures exist: settle, baseline, cut, call, hold, restore, observe, classify.
    /// The result is left in <see cref="Cycle.Result"/>.
    /// </summary>
    /// <param name="cycle">The cycle's working state.</param>
    /// <param name="dir">The case's folder on the share.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <exception cref="ProbeAbortException">The run cannot go on.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    private async Task RunWithFixturesAsync(Cycle cycle, string dir, CancellationToken ct)
    {
        var c = cycle.Case;

        // Let SMB's information caches (about 10 s) expire, so the call under test needs the network.
        var settle = _ctx.Options.SettleSeconds;
        if (!_ctx.Simulated && settle > 0)
        {
            _ctx.Say($"waiting {settle} s so SMB's caches expire before the cut");
            await Task.Delay(TimeSpan.FromSeconds(settle), ct).ConfigureAwait(false);
        }

        // 1. Baseline, with the network normal: the folder must be exactly as created, and its mtime is noted.
        var before = await ObserveAsync(c, dir).ConfigureAwait(false);
        cycle.MtimeBefore = await ReadFolderMtimeAsync(dir).ConfigureAwait(false);
        if (before != c.ExpectedState)
        {
            cycle.Finish(LateOutcome.NotTested,
                $"before the cut the folder was not as created (expected '{c.ExpectedState}', found '{before ?? "could not look"}'), so nothing can be concluded");
            return;
        }
        cycle.Note($"baseline: {before}; folder last written (server clock) {TextFormat.ServerTime(cycle.MtimeBefore)}");

        // 2. Cut, confirm, issue the call.
        var mark = _ctx.Logger.Mark();
        _ctx.Rig.StartDrop();
        var confirmed = await _runner.ConfirmCutOrAbortAsync(ct).ConfigureAwait(false);
        if (confirmed is null)
        {
            cycle.Recovery = await _ctx.Rig.StopAndWaitForRecoveryAsync(NetworkUpWait, ct).ConfigureAwait(false);
            cycle.Finish(LateOutcome.NotTested, "cut not confirmed within 10 s");
            return;
        }
        cycle.Note("cut confirmed");

        var run = new OperationRun(() => c.Execute(dir));
        run.Start();
        cycle.IssuedAt = run.StartedAt;
        cycle.Note($"call issued: {c.CallText}");
        _ctx.Say($"call issued; holding the cut until {cycle.HoldSeconds} s after the call");

        // 3. Hold for H seconds measured from T_issue, then give the network back.
        var remaining = cycle.IssuedAt + TimeSpan.FromSeconds(cycle.HoldSeconds) - ProbeClock.Now;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct).ConfigureAwait(false);

        cycle.NetworkFileOpsReturnedAfter = run.CompletedAt is { } done ? done - cycle.IssuedAt : null;
        cycle.NetworkFileOpsResult = run.Result?.Text
            ?? (run.Error is { } error ? $"threw {error.GetType().Name}: {error.Message}" : "(did not return)");
        if (run.CompletedAt is { } returnedAt)
            cycle.Note($"NetworkFileOps returned ({cycle.NetworkFileOpsResult}) {(returnedAt - cycle.IssuedAt).TotalSeconds:F1} s after the call");
        else
            cycle.Note("NetworkFileOps had not returned by the end of the hold");

        var restoredAt = ProbeClock.Now;
        cycle.Note("network restored (clumsy stopped)");
        _ctx.Say("network restored; waiting for the share to answer");
        cycle.Recovery = await _ctx.Rig.StopAndWaitForRecoveryAsync(NetworkUpWait, ct).ConfigureAwait(false);
        if (cycle.Recovery is null)
        {
            cycle.Finish(LateOutcome.NotTested,
                $"the share did not answer within {NetworkUpWait.TotalSeconds:F0} s after the network was restored, so the folder could not be watched", mark, _ctx.Logger);
            return;
        }
        cycle.Note($"share answered {cycle.Recovery.Value.TotalSeconds:F1} s after the network was restored");

        // A call answered inside the deadline without the network (success, not a timeout) proves nothing. (A dry run has
        // no cut, so its call always succeeds; it goes on to exercise the observation loop and is reported SIMULATED.)
        if (!_ctx.Simulated && run.Result is { GaveUp: false })
        {
            cycle.Finish(LateOutcome.NotTested,
                $"the call returned success ({cycle.NetworkFileOpsResult}) {TextFormat.Seconds(cycle.NetworkFileOpsReturnedAfter)} after it was issued: it was answered without the network (for example from SMB's cache), so nothing was abandoned",
                mark, _ctx.Logger);
            return;
        }

        // 4. Watch the folder with plain file calls.
        await ObserveUntilDoneAsync(cycle, dir, mark, ct).ConfigureAwait(false);

        // 5. Classify.
        Classify(cycle, mark);
    }

    /// <summary>
    /// Step 4: looks at the folder once a second, starting when the share first answered, until its state differs from
    /// the pre-cut state, the abandoned operation has been seen to end with an error for a few seconds, or the observation
    /// time is up. A look that could not be made (the share did not answer in time) is counted, never taken as a change.
    /// </summary>
    /// <param name="cycle">The cycle's working state; receives the observations.</param>
    /// <param name="dir">The case's folder.</param>
    /// <param name="mark">Log position from before the call.</param>
    /// <param name="ct">Cancels the observation.</param>
    private async Task ObserveUntilDoneAsync(Cycle cycle, string dir, int mark, CancellationToken ct)
    {
        var c = cycle.Case;
        var started = ProbeClock.Now;
        var window = _ctx.Simulated ? DryObservationTime : ObservationTime;
        _ctx.Say($"watching the folder once a second for up to {window.TotalSeconds:F0} s");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var state = await ObserveAsync(c, dir).ConfigureAwait(false);
            var now = ProbeClock.Now;
            cycle.Observations++;

            if (state is null)
            {
                cycle.UnreadableObservations++;
            }
            else if (state != c.ExpectedState)
            {
                cycle.FirstChangedAfterIssue = now - cycle.IssuedAt;
                cycle.ChangedState = state;
                cycle.Note($"first seen changed: {state}");
                _ctx.Say($"the folder CHANGED {cycle.FirstChangedAfterIssue.Value.TotalSeconds:F1} s after the call: {state}");
                break;
            }

            var ended = InnerEndLine(_ctx.Logger.Since(mark));
            if (ended is not null && now - ended.At >= AfterFaultTime)
                break;
            if (now - started >= window)
                break;
            await Task.Delay(ObservationInterval, ct).ConfigureAwait(false);
        }

        cycle.ObservedFor = ProbeClock.Now - started;
        cycle.MtimeAfter = await ReadFolderMtimeAsync(dir).ConfigureAwait(false);
    }

    /// <summary>
    /// Step 5: turns what was seen into an outcome and a plain-words reason.
    /// </summary>
    /// <param name="cycle">The cycle's working state, with the observations in it.</param>
    /// <param name="mark">Log position from before the call.</param>
    private void Classify(Cycle cycle, int mark)
    {
        var log = _ctx.Logger.Since(mark);
        var ended = InnerEndLine(log);
        if (ended is not null)
        {
            cycle.InnerFinishedAfterIssue = ended.At - cycle.IssuedAt;
            cycle.InnerFinishedHow = $"faulted: {ended.Text}";
        }
        else
        {
            cycle.InnerFinishedHow = "no line logged (a successful finish is not logged)";
        }

        var mtime = $"folder last written (server clock, which may differ from this computer's by about 1 s): {TextFormat.ServerTime(cycle.MtimeBefore)} before, {TextFormat.ServerTime(cycle.MtimeAfter)} at the last look";
        var watched = $"{cycle.Observations} looks over {TextFormat.Seconds(cycle.ObservedFor)}, {cycle.UnreadableObservations} could not look";

        if (cycle.FirstChangedAfterIssue is { } changed)
        {
            var reason = $"the abandoned operation took effect: first seen changed {TextFormat.Seconds(changed)} after the call was issued (an upper bound; the cut was held {cycle.HoldSeconds} s, the share answered {TextFormat.Seconds(cycle.Recovery)} after it was restored) - {cycle.ChangedState}; {mtime}";
            cycle.Finish(_ctx.Simulated ? LateOutcome.Simulated : LateOutcome.LandedLate,
                _ctx.Simulated ? $"dry run: nothing was cut, so there is no verdict (the loop saw the folder change {TextFormat.Seconds(changed)} after the call: {cycle.ChangedState})" : reason, log);
            return;
        }

        if (_ctx.Simulated)
        {
            cycle.Finish(LateOutcome.Simulated, $"dry run: nothing was cut, so there is no verdict (the folder did not change in {watched})", log);
            return;
        }

        if (ended is not null)
        {
            cycle.Finish(LateOutcome.Died,
                $"nothing changed, and the abandoned operation ended with an error {TextFormat.Seconds(cycle.InnerFinishedAfterIssue)} after the call was issued ({ended.Text}); {watched}; {mtime}", log);
            return;
        }

        cycle.Finish(LateOutcome.StillPending,
            $"nothing changed in {watched}, and no line says the abandoned operation ended: it may still be blocked; {mtime}", log);
    }

    /// <summary>
    /// Finds the line <c>NetworkFileOps</c> wrote when an abandoned operation ended with an error.
    /// </summary>
    /// <param name="lines">The lines logged since the call was about to be made.</param>
    /// <returns>The first such line, or null when none was logged.</returns>
    private static LogLine? InnerEndLine(IReadOnlyList<LogLine> lines)
        => lines.FirstOrDefault(l => l.Text.Contains("abandoned", StringComparison.Ordinal)
                                     && (l.Text.Contains("faulted after timeout", StringComparison.Ordinal)
                                         || l.Text.Contains("completed after timeout", StringComparison.Ordinal)));

    /// <summary>
    /// One look at the folder's state, with plain file calls on a thread-pool thread and a deadline of its own.
    /// </summary>
    /// <param name="c">The case (knows how to read its folder).</param>
    /// <param name="dir">The case's folder.</param>
    /// <returns>The state, or null when the look could not be made in time (or failed).</returns>
    private static Task<string?> ObserveAsync(LateLandingCase c, string dir)
        => Task.Run(() =>
        {
            try { return RigIo.Run(() => c.Observe(dir), ObservationPatience, "looking at the folder"); }
            catch { return null; }
        });

    /// <summary>
    /// Reads the folder's last-write time as the server reports it, with a deadline.
    /// </summary>
    /// <param name="dir">The folder.</param>
    /// <returns>The time in UTC (server clock), or null when it could not be read or the folder does not exist.</returns>
    private static Task<DateTime?> ReadFolderMtimeAsync(string dir)
        => Task.Run(() =>
        {
            try
            {
                return RigIo.Run(() =>
                {
                    var info = new DirectoryInfo(dir);
                    return info.Exists ? (DateTime?)info.LastWriteTimeUtc : null;
                }, ObservationPatience, "reading the folder's time");
            }
            catch { return null; }
        });

    /// <summary>
    /// The working state of one cycle: what has been measured so far, a timeline, and the finished result.
    /// </summary>
    private sealed class Cycle
    {
        /// <summary>The case.</summary>
        public LateLandingCase Case { get; }

        /// <summary>Seconds the cut is held.</summary>
        public int HoldSeconds { get; }

        /// <summary>The repetition number.</summary>
        public int Repetition { get; }

        /// <summary>Probe-clock time the call was issued; zero until it is.</summary>
        public TimeSpan IssuedAt { get; set; }

        /// <summary>See <see cref="LateLandingResult.NetworkFileOpsReturnedAfter"/>.</summary>
        public TimeSpan? NetworkFileOpsReturnedAfter { get; set; }

        /// <summary>See <see cref="LateLandingResult.NetworkFileOpsResult"/>.</summary>
        public string NetworkFileOpsResult { get; set; } = "-";

        /// <summary>See <see cref="LateLandingResult.FirstChangedAfterIssue"/>.</summary>
        public TimeSpan? FirstChangedAfterIssue { get; set; }

        /// <summary>See <see cref="LateLandingResult.ChangedState"/>.</summary>
        public string ChangedState { get; set; } = "-";

        /// <summary>See <see cref="LateLandingResult.InnerFinishedAfterIssue"/>.</summary>
        public TimeSpan? InnerFinishedAfterIssue { get; set; }

        /// <summary>See <see cref="LateLandingResult.InnerFinishedHow"/>.</summary>
        public string InnerFinishedHow { get; set; } = "-";

        /// <summary>See <see cref="LateLandingResult.Recovery"/>.</summary>
        public TimeSpan? Recovery { get; set; }

        /// <summary>See <see cref="LateLandingResult.FolderMtimeBefore"/>.</summary>
        public DateTime? MtimeBefore { get; set; }

        /// <summary>See <see cref="LateLandingResult.FolderMtimeAfter"/>.</summary>
        public DateTime? MtimeAfter { get; set; }

        /// <summary>See <see cref="LateLandingResult.Observations"/>.</summary>
        public int Observations { get; set; }

        /// <summary>See <see cref="LateLandingResult.UnreadableObservations"/>.</summary>
        public int UnreadableObservations { get; set; }

        /// <summary>See <see cref="LateLandingResult.ObservedFor"/>.</summary>
        public TimeSpan ObservedFor { get; set; }

        /// <summary>The cycle's own events, oldest first.</summary>
        private readonly List<LogLine> _events = new();

        /// <summary>The finished result; null until <see cref="Finish(LateOutcome, string, IReadOnlyList{LogLine}?)"/> is called.</summary>
        public LateLandingResult? Result { get; private set; }

        /// <summary>Starts the working state of a cycle.</summary>
        /// <param name="c">The case.</param>
        /// <param name="holdSeconds">Seconds the cut is held.</param>
        /// <param name="repetition">The repetition number.</param>
        public Cycle(LateLandingCase c, int holdSeconds, int repetition)
        {
            Case = c;
            HoldSeconds = holdSeconds;
            Repetition = repetition;
        }

        /// <summary>Adds an event to the timeline, stamped with the probe clock.</summary>
        /// <param name="text">What happened.</param>
        public void Note(string text) => _events.Add(new LogLine(ProbeClock.Now, "EVENT", text));

        /// <summary>
        /// Ends the cycle with an outcome that needs no log analysis (nothing was abandoned, or the folder could not be watched).
        /// </summary>
        /// <param name="outcome">The outcome.</param>
        /// <param name="reason">Why, in plain words.</param>
        /// <param name="mark">Log position from before the call, to include the logged lines (or -1 for none).</param>
        /// <param name="logger">The recorder to take the lines from; null for none.</param>
        public void Finish(LateOutcome outcome, string reason, int mark = -1, RecordingProbeLogger? logger = null)
            => Finish(outcome, reason, logger is null || mark < 0 ? null : logger.Since(mark));

        /// <summary>
        /// Ends the cycle: builds the result from everything measured, merging the events and the logged lines in time order.
        /// </summary>
        /// <param name="outcome">The outcome.</param>
        /// <param name="reason">Why, in plain words.</param>
        /// <param name="log">The lines the logger received during the cycle, or null for none.</param>
        public void Finish(LateOutcome outcome, string reason, IReadOnlyList<LogLine>? log)
        {
            Result = new LateLandingResult
            {
                Name = Case.Name,
                HoldSeconds = HoldSeconds,
                Repetition = Repetition,
                Outcome = outcome,
                Reason = reason,
                IssuedAt = IssuedAt,
                NetworkFileOpsReturnedAfter = NetworkFileOpsReturnedAfter,
                NetworkFileOpsResult = NetworkFileOpsResult,
                FirstChangedAfterIssue = FirstChangedAfterIssue,
                ChangedState = ChangedState,
                InnerFinishedAfterIssue = InnerFinishedAfterIssue,
                InnerFinishedHow = InnerFinishedHow,
                Recovery = Recovery,
                FolderMtimeBefore = MtimeBefore,
                FolderMtimeAfter = MtimeAfter,
                Observations = Observations,
                UnreadableObservations = UnreadableObservations,
                ObservedFor = ObservedFor,
                Timeline = _events.Concat(log ?? Array.Empty<LogLine>()).OrderBy(l => l.At).ToList()
            };
        }
    }
}

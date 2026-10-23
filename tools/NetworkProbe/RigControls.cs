namespace NetworkProbe;

/// <summary>The outcome of one rig self-check (spec section 5.3).</summary>
/// <param name="Name">The control's name.</param>
/// <param name="Expectation">What the control must show for the rig to be trusted, in plain words.</param>
/// <param name="Passed">True/false when run for real; null in a dry run.</param>
/// <param name="Detail">What actually happened.</param>
/// <param name="Log">Lines logged during the control, if any.</param>
internal sealed record ControlResult(
    string Name, string Expectation, bool? Passed, string Detail, IReadOnlyList<LogLine> Log)
{
    /// <summary>The control's outcome as shown to the user.</summary>
    public string OutcomeText => Passed switch { true => "OK", false => "FAILED", null => "SIMULATED" };
}

/// <summary>
/// The two controls that run first (spec section 5.3). If either fails the rig is not working and the run stops
/// without reporting any TermPoint verdict:
///
/// <list type="bullet">
/// <item><b>Known-good:</b> <c>NetworkFileOps.ExistsAsync</c> on a file that does not exist, cut before the call, must
/// report a timeout within <c>TimeoutMs + 1.5 s</c> of the cut being confirmed (this path was proven in the July field
/// test).</item>
/// <item><b>Known-bad:</b> an UNGUARDED plain <c>File.WriteAllBytes</c> of 64 KB on a dedicated background thread, cut
/// before the call, must still be blocked 15 s after the cut was confirmed. If it finishes sooner the cut is not really
/// blocking SMB (a cache answered, say) and nothing else can be trusted. The thread is abandoned, never joined on the
/// main path.</item>
/// </list>
/// </summary>
internal sealed class RigControls
{
    /// <summary>How long the unguarded write must stay blocked after the cut is confirmed.</summary>
    private static readonly TimeSpan BlockedFor = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for the abandoned unguarded write to end once the network is back.</summary>
    private static readonly TimeSpan LateWait = TimeSpan.FromSeconds(120);

    /// <summary>The shared state of the run.</summary>
    private readonly ProbeContext _ctx;

    /// <summary>Runs the known-good control through the ordinary case runner.</summary>
    private readonly CaseRunner _runner;

    /// <summary>Creates the controls.</summary>
    /// <param name="ctx">The run's shared state.</param>
    /// <param name="runner">The case runner (used for the known-good control).</param>
    public RigControls(ProbeContext ctx, CaseRunner runner)
    {
        _ctx = ctx;
        _runner = runner;
    }

    /// <summary>
    /// Runs both controls, known-good first.
    /// </summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The two results, known-good first.</returns>
    /// <exception cref="ProbeAbortException">The run cannot go on.</exception>
    public async Task<IReadOnlyList<ControlResult>> RunAsync(CancellationToken ct)
    {
        var good = await RunKnownGoodAsync(ct).ConfigureAwait(false);
        var bad = await RunKnownBadAsync(ct).ConfigureAwait(false);
        return new[] { good, bad };
    }

    /// <summary>
    /// Known-good control: an ordinary case run, judged with the tighter window of the control.
    /// </summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The control's result.</returns>
    private async Task<ControlResult> RunKnownGoodAsync(CancellationToken ct)
    {
        const string expectation = "ExistsAsync(no-such-file), cut before the call, reports a timeout within TimeoutMs + 1.5 s of the cut being confirmed";
        var result = await _runner.RunAsync(CaseCatalog.ControlGood, 1, 0, 1, ct).ConfigureAwait(false);
        bool? passed = result.Verdict switch
        {
            Verdict.Pass => true,
            Verdict.Simulated => null,
            _ => false
        };
        var detail = $"{result.VerdictText}: {result.Reason}; result {result.ResultText}; result-after-cut {TextFormat.Seconds(result.ResultAfterCut)}";
        return new ControlResult("known-good: ExistsAsync(no-such-file)", expectation, passed, detail, result.Log);
    }

    /// <summary>
    /// Known-bad control: an unguarded write must stay blocked while the cut is held.
    /// </summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The control's result.</returns>
    private async Task<ControlResult> RunKnownBadAsync(CancellationToken ct)
    {
        const string name = "known-bad: unguarded File.WriteAllBytes(64 KB)";
        const string expectation = "an UNGUARDED plain write, cut before the call, is still blocked 15 s after the cut was confirmed";
        _ctx.Say("--- control-bad (unguarded File.WriteAllBytes on its own thread)");

        if (!await _ctx.Rig.WaitUntilUpAsync(TimeSpan.FromSeconds(120), ct).ConfigureAwait(false))
            throw new ProbeAbortException("The share did not answer for 120 s before the known-bad control could start.");

        var folder = Path.Combine(_ctx.Workspace.ShareRoot, "control-bad");
        var path = Path.Combine(folder, "control.bin");
        try { RigIo.Run(() => Directory.CreateDirectory(folder), RigIo.Short, "creating the control folder"); }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            throw new ProbeAbortException($"Setting up the known-bad control failed with the network normal: {ex.Message}");
        }

        var unguarded = new UnguardedWrite(path);

        if (_ctx.Simulated)
        {
            // Dry run: the write is made with no cut and simply finishes.
            unguarded.Start();
            await Task.WhenAny(unguarded.Finished, Task.Delay(TimeSpan.FromSeconds(30), ct)).ConfigureAwait(false);
            _ctx.Workspace.DeleteFolderOnce(folder);
            return new ControlResult(name, expectation, null,
                $"dry run: the unguarded write was made with no cut and finished in {TextFormat.Seconds2(unguarded.FinishedAt - unguarded.StartedAt)}", Array.Empty<LogLine>());
        }

        _ctx.Rig.StartDrop();
        var confirmed = await _ctx.Rig.ConfirmCutAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        if (confirmed is null)
        {
            await _ctx.Rig.StopAndWaitForRecoveryAsync(TimeSpan.FromSeconds(120), ct).ConfigureAwait(false);
            if (!_ctx.CutEverConfirmed && _ctx.FilterNamesAddresses)
                throw new ProbeAbortException(
                    "The very first cut was never confirmed, with a filter that names the server's addresses. The address syntax of the filter may be what failed: try again with --port-only (which also cuts SMB to any other server).");
            return new ControlResult(name, expectation, false, "the cut was not confirmed within 10 s, so the control could not run", Array.Empty<LogLine>());
        }
        _ctx.CutEverConfirmed = true;
        _ctx.Say($"cut confirmed ({ProbeClock.Stamp(confirmed.Value)}); starting the unguarded write");

        unguarded.Start();
        var checkAt = confirmed.Value + BlockedFor;
        var wait = checkAt - ProbeClock.Now;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct).ConfigureAwait(false);
        var stillBlocked = !unguarded.Finished.IsCompleted;
        var finishedAfterCut = unguarded.FinishedAt is { } f ? f - confirmed.Value : (TimeSpan?)null;

        // Give the network back; the abandoned write ends by itself once SMB answers (or gives up).
        var restoredAt = ProbeClock.Now;
        var recovery = await _ctx.Rig.StopAndWaitForRecoveryAsync(TimeSpan.FromSeconds(120), ct).ConfigureAwait(false);
        if (!unguarded.Finished.IsCompleted)
            await Task.WhenAny(unguarded.Finished, Task.Delay(LateWait, ct)).ConfigureAwait(false);

        string detail;
        if (stillBlocked)
        {
            detail = unguarded.FinishedAt is { } late
                ? $"still blocked at +{BlockedFor.TotalSeconds:F0} s as required; it ended {(late - restoredAt).TotalSeconds:F1} s after the network came back{(unguarded.Error is null ? "" : $" with {unguarded.Error.GetType().Name}")}"
                : $"still blocked at +{BlockedFor.TotalSeconds:F0} s as required, and still blocked {LateWait.TotalSeconds:F0} s after the network came back (the thread is abandoned)";
        }
        else
        {
            detail = $"the unguarded write finished {TextFormat.Seconds(finishedAfterCut)} after the cut was confirmed" +
                     (unguarded.Error is null ? "" : $" ({unguarded.Error.GetType().Name})") +
                     ", sooner than 15 s: the cut is not really blocking SMB for plain file calls";
        }
        _ctx.Workspace.DeleteFolderOnce(folder);
        return new ControlResult(name, expectation, stillBlocked, detail + $"; recovery {TextFormat.Seconds(recovery)}", Array.Empty<LogLine>());
    }

    /// <summary>
    /// The unguarded call of the known-bad control: a plain <c>File.WriteAllBytes</c> on a dedicated background thread.
    /// The probe never joins the thread on its main path; it only watches <see cref="Finished"/>.
    /// </summary>
    private sealed class UnguardedWrite
    {
        /// <summary>Where to write.</summary>
        private readonly string _path;

        /// <summary>Completes when the write has ended (successfully or with an exception).</summary>
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Guards the result fields (written by the write thread, read by the main thread).</summary>
        private readonly object _gate = new();

        /// <summary>Probe-clock time the write ended; null while it has not.</summary>
        private TimeSpan? _finishedAt;

        /// <summary>The exception the write ended with, or null.</summary>
        private Exception? _error;

        /// <summary>Creates the write; nothing is written yet.</summary>
        /// <param name="path">The file to write (64 KB of zeros).</param>
        public UnguardedWrite(string path) => _path = path;

        /// <summary>Probe-clock time <see cref="Start"/> was called.</summary>
        public TimeSpan StartedAt { get; private set; }

        /// <summary>Completes when the write has ended; it never faults.</summary>
        public Task Finished => _finished.Task;

        /// <summary>Probe-clock time the write ended; null while it has not.</summary>
        public TimeSpan? FinishedAt { get { lock (_gate) return _finishedAt; } }

        /// <summary>The exception the write ended with, or null.</summary>
        public Exception? Error { get { lock (_gate) return _error; } }

        /// <summary>Starts the plain, unguarded write on a dedicated background thread.</summary>
        public void Start()
        {
            StartedAt = ProbeClock.Now;
            new Thread(() =>
            {
                try { File.WriteAllBytes(_path, new byte[64 * 1024]); }
                catch (Exception ex) { lock (_gate) _error = ex; } // observed here, so nothing is left unobserved
                lock (_gate) _finishedAt = ProbeClock.Now;
                _finished.TrySetResult();
            })
            { IsBackground = true, Name = "probe-unguarded-write" }.Start();
        }
    }
}

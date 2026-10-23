using System.Net;

namespace NetworkProbe;

/// <summary>
/// The thing that cuts the network, proves it is cut, and gives it back. Two implementations: the real one
/// (<see cref="ClumsyNetworkRig"/>, which drives clumsy) and the dry-run one (<see cref="SimulatedNetworkRig"/>, which
/// does nothing), so the case runner is the same code in both.
/// </summary>
internal interface INetworkRig : IDisposable
{
    /// <summary>True for the dry-run rig: nothing is cut and no verdict can be given.</summary>
    bool IsSimulated { get; }

    /// <summary>Every clumsy process id started so far (for the report); empty when simulated.</summary>
    IReadOnlyList<int> ClumsyPids { get; }

    /// <summary>
    /// Waits until the share answers normally (spec section 5.1, step 1).
    /// </summary>
    /// <param name="max">How long to wait before giving up.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>True when the network is up; false when <paramref name="max"/> passed first.</returns>
    Task<bool> WaitUntilUpAsync(TimeSpan max, CancellationToken ct);

    /// <summary>Starts the lag-only clumsy (stopping any running one first). Does not wait for it to take effect.</summary>
    void StartLag();

    /// <summary>Starts the drop clumsy (stopping any running one first). Does not wait for it to take effect.</summary>
    void StartDrop();

    /// <summary>
    /// Waits until a fresh connection to the server fails on every address, which proves the cut is in effect
    /// (spec section 5.3).
    /// </summary>
    /// <param name="max">How long to wait before giving up (10 s in the spec).</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The probe-clock time of the first check that confirmed the cut, or null if it was not confirmed in time.</returns>
    Task<TimeSpan?> ConfirmCutAsync(TimeSpan max, CancellationToken ct);

    /// <summary>
    /// Stops clumsy and waits for the share to answer again (SMB can take tens of seconds to reconnect).
    /// </summary>
    /// <param name="max">How long to wait for the share before giving up (120 s in the spec).</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>How long it took from stopping clumsy until the share answered, or null if it never did within <paramref name="max"/>.</returns>
    Task<TimeSpan?> StopAndWaitForRecoveryAsync(TimeSpan max, CancellationToken ct);
}

/// <summary>
/// The dry-run rig: every method is a no-op that reports success at once. Used with <c>--dry-run</c> to check the
/// plumbing (options, logger swap, test files, every real <c>NetworkFileOps</c> call, report, clean-up) without clumsy,
/// elevation or a server.
/// </summary>
internal sealed class SimulatedNetworkRig : INetworkRig
{
    /// <inheritdoc/>
    public bool IsSimulated => true;

    /// <inheritdoc/>
    public IReadOnlyList<int> ClumsyPids => Array.Empty<int>();

    /// <inheritdoc/>
    public Task<bool> WaitUntilUpAsync(TimeSpan max, CancellationToken ct) => Task.FromResult(true);

    /// <inheritdoc/>
    public void StartLag() { }

    /// <inheritdoc/>
    public void StartDrop() { }

    /// <inheritdoc/>
    public Task<TimeSpan?> ConfirmCutAsync(TimeSpan max, CancellationToken ct) => Task.FromResult<TimeSpan?>(ProbeClock.Now);

    /// <inheritdoc/>
    public Task<TimeSpan?> StopAndWaitForRecoveryAsync(TimeSpan max, CancellationToken ct) => Task.FromResult<TimeSpan?>(TimeSpan.Zero);

    /// <inheritdoc/>
    public void Dispose() { }
}

/// <summary>
/// The real rig: clumsy on this computer cuts SMB to the server, and fresh TCP connections to port 445 (plus a
/// plain directory check on the probe's folder) prove whether the network is cut or restored.
/// </summary>
internal sealed class ClumsyNetworkRig : INetworkRig
{
    /// <summary>How often the up and cut checks are repeated.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>The clumsy driver.</summary>
    private readonly ClumsyController _clumsy;

    /// <summary>The server addresses to check.</summary>
    private readonly IReadOnlyList<IPAddress> _addresses;

    /// <summary>The probe's own folder on the share, used to see that the share really answers.</summary>
    private readonly string _probeFolder;

    /// <summary>The directory check still in flight from an earlier poll, if any (never stack up blocked threads).</summary>
    private Task<bool>? _folderCheck;

    /// <summary>Creates the rig.</summary>
    /// <param name="clumsy">The clumsy driver.</param>
    /// <param name="target">The server and its addresses.</param>
    /// <param name="probeFolder">The probe's folder on the share (it must exist).</param>
    public ClumsyNetworkRig(ClumsyController clumsy, CutTarget target, string probeFolder)
    {
        _clumsy = clumsy;
        _addresses = target.Addresses;
        _probeFolder = probeFolder;
    }

    /// <inheritdoc/>
    public bool IsSimulated => false;

    /// <inheritdoc/>
    public IReadOnlyList<int> ClumsyPids => _clumsy.StartedPids;

    /// <inheritdoc/>
    public async Task<bool> WaitUntilUpAsync(TimeSpan max, CancellationToken ct)
    {
        var deadline = ProbeClock.Now + max;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsUpOnceAsync(ct).ConfigureAwait(false)) return true;
            if (ProbeClock.Now >= deadline) return false;
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void StartLag() => _clumsy.Start(CutKind.Lag);

    /// <inheritdoc/>
    public void StartDrop() => _clumsy.Start(CutKind.Drop);

    /// <inheritdoc/>
    public async Task<TimeSpan?> ConfirmCutAsync(TimeSpan max, CancellationToken ct)
    {
        var deadline = ProbeClock.Now + max;
        while (ProbeClock.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await TcpReachability.NoneConnectAsync(_addresses, TcpReachability.CheckTimeout, ct).ConfigureAwait(false))
                return ProbeClock.Now;
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
        }
        return null;
    }

    /// <inheritdoc/>
    public async Task<TimeSpan?> StopAndWaitForRecoveryAsync(TimeSpan max, CancellationToken ct)
    {
        var stoppedAt = ProbeClock.Now;
        _clumsy.Stop();
        return await WaitUntilUpAsync(max, ct).ConfigureAwait(false) ? ProbeClock.Now - stoppedAt : null;
    }

    /// <inheritdoc/>
    public void Dispose() => _clumsy.Dispose();

    /// <summary>
    /// One look at whether the network is up: a fresh connection to every server address succeeds AND the probe's
    /// folder answers a plain directory check.
    /// </summary>
    /// <param name="ct">Cancels the check.</param>
    /// <returns>True when both hold.</returns>
    private async Task<bool> IsUpOnceAsync(CancellationToken ct)
    {
        if (!await TcpReachability.AllConnectAsync(_addresses, TcpReachability.CheckTimeout, ct).ConfigureAwait(false))
            return false;
        return await FolderAnswersAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks whether the probe's folder exists, with a 3 s patience. A check that hangs is left to finish on its own and
    /// no new one is started until it has, so a dead share cannot use up the thread pool.
    /// </summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>True when the folder was seen within the patience.</returns>
    private async Task<bool> FolderAnswersAsync(CancellationToken ct)
    {
        if (_folderCheck is null || _folderCheck.IsCompleted)
            _folderCheck = Task.Run(() => Directory.Exists(_probeFolder), CancellationToken.None);

        var check = _folderCheck;
        var winner = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(3), ct)).ConfigureAwait(false);
        return winner == check && check.IsCompletedSuccessfully && check.Result;
    }
}

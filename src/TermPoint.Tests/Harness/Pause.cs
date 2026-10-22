namespace TermPoint.Tests.Harness;

/// <summary>
/// How a pause (spec item 9.3, step 9.3c-2b) ended. A pause holds one operation of one computer at a named crash point,
/// runs one other event meanwhile, then lets the operation go on (<see cref="World.RunWithPauseAsync"/>).
/// </summary>
internal enum PauseOutcome
{
    /// <summary>The operation never reached the point (the n-th time), so it simply completed and the event did not happen.</summary>
    NotReached,

    /// <summary>The operation was held, but the event was not possible at that moment (for example "Bob edits" while Bob's connection is closed), so nothing happened meanwhile.</summary>
    Skipped,

    /// <summary>The event finished while the operation was held: the two really interleaved.</summary>
    Interleaved,

    /// <summary>
    /// The event could not finish until the operation was let go (for example <c>CheckoutService.ReleaseAsync</c> waits for a save in
    /// flight). That is designed behaviour, not a violation: the operation was released after the grace period and both finished.
    /// </summary>
    Waited,

    /// <summary>Something was still unfinished at the real-time limit: a <see cref="ViolationKind.Hang"/> was recorded and the stuck work abandoned.</summary>
    Hung
}

/// <summary>What one pause did, for the report and for the tests.</summary>
/// <param name="Computer">The paused computer's name ("bob").</param>
/// <param name="Point">The crash point the operation was held at.</param>
/// <param name="Occurrence">Which time the point was reached (1 = the first).</param>
/// <param name="Event">What happened meanwhile, as a sentence ("Alice takes over the lock").</param>
/// <param name="Outcome">How the pause ended.</param>
internal sealed record PauseResult(string Computer, string Point, int Occurrence, string Event, PauseOutcome Outcome);

/// <summary>
/// The real-time limits of a pause. The harness runs on fake time, but a pause has to wait on real threads, so these three are the
/// only real-time numbers in it, and they keep a stuck pause from hanging the test run.
/// </summary>
/// <param name="Grace">
/// How long the event may take while the operation is held. When it has not finished by then the operation is released and both are
/// left to finish (<see cref="PauseOutcome.Waited"/>): the event is waiting for the operation, as the switch waits for a save in flight.
/// </param>
/// <param name="ReachLimit">How long the operation may take to reach the point (or to end without reaching it) before the run calls a hang.</param>
/// <param name="FinishLimit">How long the operation and the event may take, together, to finish once the operation has been released, before the run calls a hang.</param>
internal sealed record PauseLimits(TimeSpan Grace, TimeSpan ReachLimit, TimeSpan FinishLimit)
{
    /// <summary>The limits the generated runs use: about 2 seconds of grace, 30 seconds for everything else.</summary>
    public static PauseLimits Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    /// <summary>How long a thread of a held operation waits to be released before it goes on by itself (a safety net against a bug in the harness, never reached in a normal run).</summary>
    public static TimeSpan SafetyWait { get; } = TimeSpan.FromMinutes(2);
}

/// <summary>Which crash points matter to which finding: the pause machinery and the classifier share these lists.</summary>
internal static class PausePoints
{
    /// <summary>
    /// The edit-loss window (finding F2; spec item 15): from a save's local snapshot to the cleanup of the working copy. An edit applied
    /// while the computer's own operation is held at one of these points is not in the save, and what protected it is thrown away when
    /// the operation ends: the save's last step deletes the unsaved-changes marker (a crash before another save then makes the next
    /// start delete the unmarked working file), and a final save (close, switch) is followed by the release and the cleanup of the
    /// working copy itself (no crash is needed: "edits made during the exit save are deleted"). The window ends where the cleanup
    /// has happened (<c>Close.AfterWorkingCopyCleanup</c>, <c>Switch.AfterOldCopyHandled</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> EditLossWindow = new[]
    {
        "Save.AfterLocalSnapshot", "Save.AfterTmpCopy", "Save.AfterTmpVerify", "Save.AfterFinalCheck", "Save.AfterRename", "Save.AfterMarkerDelete",
        "Release.AfterFinalSave", "Release.AfterLockReleased",
        "Close.AfterRelease", "Close.AfterServicesDisposed", "Switch.AfterRelease", "Switch.AfterServicesReleased"
    };

    /// <summary>
    /// The save's points before its rename onto the shared database: whatever another computer commits meanwhile is overwritten by the
    /// rename (finding F12) unless the save's final check, between <c>Save.AfterTmpVerify</c> and <c>Save.AfterFinalCheck</c>, refuses it.
    /// </summary>
    public static readonly IReadOnlyList<string> SaveBeforeRename = new[]
    {
        "Save.AfterLockCheck", "Save.AfterSourceCheck", "Save.AfterLocalSnapshot", "Save.AfterTmpCopy", "Save.AfterTmpVerify", "Save.AfterFinalCheck"
    };

    /// <summary>
    /// The crash points no pause sweep holds an operation at. <c>Save.AfterFinalCheck</c> is the true last moment before a save's rename:
    /// the lock and D were checked a few microseconds ago, and nothing else of the operation stands between the point and the rename.
    /// The window is a few microseconds of LOCAL code, not a network step, so no sleeping laptop or slow link can hold a save there; a
    /// pause would hold it for as long as the harness likes and the sweep would report, as a "new" violation, exactly the accepted
    /// residual window that closing would need a server for (a plain file share has no compare-and-swap). The CRASH sweep still cuts
    /// the power there: a crash at that point is meaningful (D.tmp is verified and checked, the rename has not happened). A proof test
    /// may still pause there on purpose, to show invariant I4 can see that window.
    /// </summary>
    public static readonly IReadOnlyList<string> NeverPaused = new[] { "Save.AfterFinalCheck" };

    /// <summary>True when no pause sweep holds an operation at <paramref name="point"/> (see <see cref="NeverPaused"/>).</summary>
    /// <param name="point">A crash point name.</param>
    public static bool IsNeverPaused(string point) => NeverPaused.Contains(point);

    /// <summary>True when an edit applied while the computer's own operation is held at <paramref name="point"/> falls in the edit-loss window (finding F2).</summary>
    /// <param name="point">A crash point name.</param>
    public static bool IsInEditLossWindow(string point) => EditLossWindow.Contains(point);

    /// <summary>True when a save held at <paramref name="point"/> has not yet renamed onto the shared database.</summary>
    /// <param name="point">A crash point name.</param>
    public static bool IsSaveBeforeRename(string point) => SaveBeforeRename.Contains(point);
}

/// <summary>
/// A pause that is armed for one operation of one computer: "the n-th time the operation reaches this crash point, hold it
/// there". Created by <see cref="World.RunWithPauseAsync"/>; read by the crash-point hook and the network fault hook from any thread.
///
/// <para><b>Holding is per flow of control.</b> The operation runs on its own task, marked with this object
/// (<see cref="World"/> keeps the mark in an <c>AsyncLocal</c>), so the mark follows every task and thread the operation starts. The
/// thread that makes the n-th arrival is held at the point; every other thread of the operation stands still at its next crash point
/// or network operation. That is a computer that sleeps in the middle of an operation: the whole process is suspended, not one thread
/// of it. The event that runs meanwhile is NOT marked, so it is never held, even when it is the same computer's.</para>
/// </summary>
internal sealed class ArmedPause
{
    /// <summary>How many times the computer has reached the point, in the operation's flow, so far.</summary>
    private int _arrivals;

    /// <summary>0 until a thread claims the hold (see <see cref="TryHold"/>).</summary>
    private int _held;

    /// <summary>Set to let the held operation go on.</summary>
    private readonly ManualResetEventSlim _released = new(false);

    /// <summary>Completed when the operation has been held.</summary>
    private readonly TaskCompletionSource _heldSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the armed pause.</summary>
    /// <param name="computer">The computer whose operation is held.</param>
    /// <param name="point">The crash point (one of <see cref="TermPoint.Services.CrashPoints.All"/>).</param>
    /// <param name="occurrence">Which time the point is reached (1 = the first).</param>
    public ArmedPause(Computer computer, string point, int occurrence)
    {
        Computer   = computer;
        Point      = point;
        Occurrence = occurrence;
    }

    /// <summary>The computer whose operation is held.</summary>
    public Computer Computer { get; }

    /// <summary>The crash point.</summary>
    public string Point { get; }

    /// <summary>Which time the point is reached (1 = the first).</summary>
    public int Occurrence { get; }

    /// <summary>True once the operation has been held (and stays true after it is released).</summary>
    public bool IsHeld => Volatile.Read(ref _held) != 0;

    /// <summary>True once the operation was let go.</summary>
    public bool IsReleased => _released.IsSet;

    /// <summary>True while the operation is held: every thread of it stands still.</summary>
    public bool IsFrozen => IsHeld && !IsReleased;

    /// <summary>A task that completes when the operation has been held.</summary>
    public Task HeldTask => _heldSignal.Task;

    /// <summary>Counts one more arrival of the operation at the point.</summary>
    /// <returns>True when this arrival is the occurrence the pause waits for.</returns>
    public bool CountArrival() => Interlocked.Increment(ref _arrivals) == Occurrence;

    /// <summary>Claims the hold for the calling thread, so that only one thread is the one that held the operation, and tells the harness it is held.</summary>
    /// <returns>True for the first caller only.</returns>
    public bool TryHold()
    {
        if (Interlocked.CompareExchange(ref _held, 1, 0) != 0) return false;
        _heldSignal.TrySetResult();
        return true;
    }

    /// <summary>Lets the operation go on: every held thread resumes.</summary>
    public void Release() => _released.Set();

    /// <summary>Blocks the calling thread until the operation is released (or the safety wait ends).</summary>
    /// <param name="timeout">The longest to wait.</param>
    /// <returns>True when the pause was released; false when the wait timed out.</returns>
    public bool WaitForRelease(TimeSpan timeout) => _released.Wait(timeout);
}

/// <summary>
/// An ordinary event of the catalogue, done with a PAUSE at a named crash point (spec item 9.3, step 9.3c-2b): "Bob presses Save; at
/// <c>Save.AfterTmpVerify</c> his save is held while Alice takes over the lock and saves; then his save goes on". It is enabled exactly
/// when the event it wraps is, and the action may be offered while that event is held (<see cref="PauseAction.IsOfferedWhileHolding"/>). The wrapped event runs on its own task and is held when it reaches the point (the n-th time); the
/// <see cref="PauseAction"/> runs on the test's flow; then the held event is released. When the event never reaches the point it simply
/// happens, and the story says the pause did not happen. The whole thing is ONE funnel step (<see cref="World.RunWithPauseAsync"/>).
///
/// <para>Like <see cref="PowerCutEvent"/> it is NOT in <see cref="EventCatalogue.Build"/>; a run that uses it is given it in
/// <see cref="RunOptions.ExtraEvents"/>, built from the keys it replays (<see cref="EventCatalogue.ExtrasFor"/>). Its key reads like the
/// decision list it lives in: <c>Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)</c>, or <c>... at Save.AfterTmpVerify #2; ...)</c>
/// for the second time the point is reached.</para>
/// </summary>
internal sealed class PauseEvent : HarnessEvent
{
    /// <summary>What every key of a pause event starts with.</summary>
    public const string KeyPrefix = "Pause(";

    /// <summary>The separator between the held event and the event that runs meanwhile in a key.</summary>
    private const string ActionSeparator = "; ";

    /// <summary>The event that is held.</summary>
    private readonly ComputerEvent _inner;

    /// <summary>What happens meanwhile.</summary>
    private readonly PauseAction _action;

    /// <summary>Creates the event.</summary>
    /// <param name="inner">The event to do (and to hold).</param>
    /// <param name="point">The crash point at which it is held (one of <see cref="TermPoint.Services.CrashPoints.All"/>).</param>
    /// <param name="occurrence">Which time the point is reached (1 = the first).</param>
    /// <param name="action">What happens while it is held.</param>
    public PauseEvent(ComputerEvent inner, string point, int occurrence, PauseAction action)
    {
        _inner     = inner;
        Point      = point;
        Occurrence = occurrence;
        _action    = action;
    }

    /// <summary>The crash point at which the event is held.</summary>
    public string Point { get; }

    /// <summary>Which time the point is reached (1 = the first).</summary>
    public int Occurrence { get; }

    /// <summary>The wrapped event.</summary>
    public ComputerEvent Inner => _inner;

    /// <summary>What happens while the event is held.</summary>
    public PauseAction Action => _action;

    /// <inheritdoc/>
    public override string Key
        => $"{KeyPrefix}{_inner.Key} at {Point}{(Occurrence > 1 ? $" #{Occurrence}" : "")}{ActionSeparator}{_action.Key})";

    /// <inheritdoc/>
    public override string Story => $"{_inner.Story}, and at {Point} it is held while {_action.Story}";

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => _inner.IsEnabled(ctx) && _action.IsOfferedWhileHolding(_inner);

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
        => await ctx.World.RunWithPauseAsync(
            _inner.ComputerOf(ctx), Point, Occurrence, _inner.Story, () => _inner.ExecuteAsync(ctx),
            _action.Story, () => _action.IsPossible(ctx), () => _action.RunAsync(ctx), _action.ComputersOf(ctx));

    /// <summary>Reads a key written by <see cref="Key"/> back into the event.</summary>
    /// <param name="key">An event key.</param>
    /// <param name="events">The events the wrapped one is found in, by key.</param>
    /// <returns>The event; null when the key is not a pause key, or names a wrapped event or an action that does not exist.</returns>
    public static PauseEvent? TryParse(string key, IReadOnlyDictionary<string, HarnessEvent> events)
    {
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || !key.EndsWith(')')) return null;

        var body = key[KeyPrefix.Length..^1];
        var semi = body.LastIndexOf(ActionSeparator, StringComparison.Ordinal);
        if (semi < 0) return null;

        var action = PauseActions.All.FirstOrDefault(a => a.Key == body[(semi + ActionSeparator.Length)..]);
        if (action is null) return null;

        var head = body[..semi];
        var at   = head.LastIndexOf(" at ", StringComparison.Ordinal);
        if (at < 0) return null;

        var innerKey   = head[..at];
        var rest       = head[(at + 4)..];
        var occurrence = 1;
        var hash = rest.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
        {
            if (!int.TryParse(rest[(hash + 2)..], out occurrence)) return null;
            rest = rest[..hash];
        }

        return events.TryGetValue(innerKey, out var inner) && inner is ComputerEvent computerEvent
            ? new PauseEvent(computerEvent, rest, occurrence, action)
            : null;
    }
}

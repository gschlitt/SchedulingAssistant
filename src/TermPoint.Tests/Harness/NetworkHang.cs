using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// One hang of one computer's network (spec item 8.2): from "Bob's network hangs" until "Bob's network came back", every
/// call that computer makes on the share BLOCKS instead of failing. A call's real work is held on this gate, inside the
/// deadline-guarded part of <see cref="NetworkFileOps"/>, so the ordinary five-second deadline decides what the caller sees
/// (the timeout, the log line, the abandoned task); when the network returns <see cref="Open"/> lets every held call go on, and
/// each really runs and lands late, exactly like an operation the SMB redirector finally completes after the program gave up
/// on it.
///
/// <para>At most <see cref="MaxHeldCalls"/> calls are held per hang; further calls of the same hang are answered with a plain
/// timeout. Every held call parks a thread-pool thread, and a pool that is entirely parked starts the next held call only as
/// fast as it grows (about one thread a second), which would make a long hang crawl for no gain: four held calls already show
/// every shape (a late rename, a late lock write, a late copy).</para>
/// </summary>
internal sealed class NetworkHangGate
{
    /// <summary>The most calls of one hang that are held; later calls time out at once instead.</summary>
    public const int MaxHeldCalls = 4;

    /// <summary>How long <see cref="Open"/> waits, in real time, for the released calls to finish landing before it gives up and goes on.</summary>
    private static readonly TimeSpan LandingLimit = TimeSpan.FromSeconds(10);

    /// <summary>Guards the counters and <see cref="_open"/>.</summary>
    private readonly object _lock = new();

    /// <summary>Set when the network returns (or the world ends): held calls go on.</summary>
    private readonly ManualResetEventSlim _open = new(false);

    /// <summary>How many calls of this hang have been held so far (counts against <see cref="MaxHeldCalls"/>).</summary>
    private int _held;

    /// <summary>How many held calls have reached their block and have not yet finished running (they are landing or still blocked).</summary>
    private int _inFlight;

    /// <summary>True once <see cref="Open"/> has been called.</summary>
    public bool IsOpen => _open.IsSet;

    /// <summary>How many calls this hang held in all.</summary>
    public int HeldCalls
    {
        get { lock (_lock) return _held; }
    }

    /// <summary>
    /// Reserves a place for one more held call.
    /// </summary>
    /// <returns>True when the call is to be held; false when the hang has held its limit (or is over) and the call should simply time out.</returns>
    public bool TryHold()
    {
        lock (_lock)
        {
            if (_open.IsSet || _held >= MaxHeldCalls) return false;
            _held++;
            return true;
        }
    }

    /// <summary>
    /// Called on the pool thread, inside the guarded task, where the real call would block: blocks until the gate opens, then
    /// returns a scope the call disposes when it has run (<see cref="Open"/> waits for that).
    /// </summary>
    /// <param name="onLanding">Run once the gate has opened and just before the real call does its work (the harness checks invariant I9 there); may be null.</param>
    /// <returns>The scope to dispose when the real call has finished.</returns>
    public IDisposable Enter(Action? onLanding)
    {
        lock (_lock) _inFlight++;
        _open.Wait(PauseLimits.SafetyWait);
        try { onLanding?.Invoke(); }
        catch { /* an observer must never break the landing */ }
        return new Landing(this);
    }

    /// <summary>
    /// The network returns: lets every held call go on, then waits (in real time, bounded) until each has run, so the late landings
    /// happen at this step of the story and not at some later moment of the run.
    /// </summary>
    public void Open()
    {
        _open.Set();
        var deadline = DateTime.UtcNow + LandingLimit;
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock) { if (_inFlight == 0) return; }
            Thread.Sleep(1);
        }
    }

    /// <summary>Marks one held call as finished.</summary>
    private void Landed()
    {
        lock (_lock) _inFlight--;
    }

    /// <summary>The scope <see cref="Enter"/> returns: disposing it tells the gate the call has run.</summary>
    private sealed class Landing : IDisposable
    {
        /// <summary>The gate to tell.</summary>
        private readonly NetworkHangGate _gate;

        /// <summary>Guards against a double dispose.</summary>
        private int _done;

        /// <summary>Creates the scope.</summary>
        /// <param name="gate">The gate to tell.</param>
        public Landing(NetworkHangGate gate) => _gate = gate;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) _gate.Landed();
        }
    }
}

/// <summary>
/// The clock <see cref="World"/> gives <see cref="NetworkFileOps"/> (item 8.2): the system clock, except for the deadline of a
/// call that the network-hang injector holds. That deadline fires as soon as the call's real work is parked on its gate, so a
/// hung call times out at once instead of after a real five seconds, and every other deadline (an ordinary call, another
/// computer's) runs on the real clock exactly as in the app.
///
/// <para><b>How a deadline is matched to a held call.</b> The fault hook is asked about a call at the start of the async
/// method that makes the call, so it can leave a <see cref="Hold"/> in an <see cref="AsyncLocal{T}"/> that only that method's
/// own timers (and the tasks it starts) see; it never leaks to the caller. A five-second timer created while a hold is set is
/// handed to the hold, and fired as soon as the held work has parked (immediately if it already has). That includes the earlier
/// deadlines of the same call (a copy's open and read) which have long finished: firing a finished deadline is harmless.</para>
/// </summary>
internal sealed class HangDeadlineClock : TimeProvider
{
    /// <summary>The deadline the app uses for every race (only timers of exactly this length are matched to a held call).</summary>
    private static readonly TimeSpan DeadlineLength = TimeSpan.FromMilliseconds(NetworkFileOps.TimeoutMs);

    /// <summary>The hold of the call whose async method is running, or null.</summary>
    private readonly AsyncLocal<Hold?> _hold = new();

    /// <summary>
    /// Sets (or clears, with null) the hold the current async method's timers are matched to. Called by the fault hook, once per
    /// consulted call, so a call that is not held never sees an earlier call's hold.
    /// </summary>
    /// <param name="hold">The new hold, or null.</param>
    public void SetHold(Hold? hold) => _hold.Value = hold;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (_hold.Value is { } hold && dueTime == DeadlineLength && period == Timeout.InfiniteTimeSpan)
            return hold.Adopt(callback, state);
        return System.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>
    /// The held call a set of deadlines belong to: remembers the timers adopted so far and fires them (on the thread pool, so no
    /// continuation runs inside the hook) once the held work has parked.
    /// </summary>
    internal sealed class Hold
    {
        /// <summary>Guards <see cref="_pending"/> and <see cref="_parked"/>.</summary>
        private readonly object _lock = new();

        /// <summary>Timers adopted before the work parked.</summary>
        private readonly List<ManualTimer> _pending = new();

        /// <summary>True once the held work is blocked on its gate.</summary>
        private bool _parked;

        /// <summary>Takes a deadline timer: fires it at once when the work is already parked, otherwise when it parks.</summary>
        /// <param name="callback">The timer's callback.</param>
        /// <param name="state">The callback's argument.</param>
        /// <returns>The timer to hand back to the deadline.</returns>
        public ITimer Adopt(TimerCallback callback, object? state)
        {
            var timer = new ManualTimer(callback, state);
            bool fireNow;
            lock (_lock)
            {
                fireNow = _parked;
                if (!fireNow) _pending.Add(timer);
            }
            if (fireNow) timer.FireOnPool();
            return timer;
        }

        /// <summary>The held work is now blocked on its gate: fires every deadline adopted so far (and, from now on, every new one at once).</summary>
        public void Parked()
        {
            ManualTimer[] due;
            lock (_lock)
            {
                _parked = true;
                due = _pending.ToArray();
                _pending.Clear();
            }
            foreach (var timer in due) timer.FireOnPool();
        }
    }

    /// <summary>A timer that never fires by itself: only <see cref="Hold"/> fires it, once.</summary>
    private sealed class ManualTimer : ITimer
    {
        /// <summary>The deadline's callback.</summary>
        private readonly TimerCallback _callback;

        /// <summary>The callback's argument.</summary>
        private readonly object? _state;

        /// <summary>Guards against firing twice.</summary>
        private int _fired;

        /// <summary>Creates the timer.</summary>
        /// <param name="callback">The deadline's callback.</param>
        /// <param name="state">The callback's argument.</param>
        public ManualTimer(TimerCallback callback, object? state)
        {
            _callback = callback;
            _state    = state;
        }

        /// <summary>Queues the callback on the thread pool, once.</summary>
        public void FireOnPool()
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                ThreadPool.UnsafeQueueUserWorkItem(_ => _callback(_state), null);
        }

        /// <inheritdoc/>
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        /// <inheritdoc/>
        public void Dispose() { }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

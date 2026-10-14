namespace TermPoint.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test calls <see cref="Advance"/>, so
/// minutes of lock-file waiting take no real time (spec item 9b). Deliberately tiny: it covers exactly
/// what the lock code uses (the UTC clock, the monotonic stopwatch and one-shot timers for
/// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>), not the whole of
/// Microsoft.Extensions.TimeProvider.Testing.
///
/// <para>The UTC clock and the monotonic stopwatch advance together by the same amount, so a test can
/// reason about both with one number. Start it where you like; by default it starts at the real current
/// time, which keeps hand-written lock files with "now minus N seconds" heartbeats meaningful.</para>
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = new();
    private DateTimeOffset _utcNow;
    private long _ticks;

    /// <summary>Creates a fake clock.</summary>
    /// <param name="start">The starting UTC time; the real current time when omitted.</param>
    public FakeTimeProvider(DateTimeOffset? start = null) => _utcNow = start ?? DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() { lock (_gate) return _utcNow; }

    /// <inheritdoc/>
    public override long GetTimestamp() { lock (_gate) return _ticks; }

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>
    /// Moves both clocks forward by <paramref name="by"/> and fires every timer that has become due
    /// (synchronously, on the calling thread, after the clock has moved).
    /// </summary>
    /// <param name="by">How far to move the clocks; must not be negative.</param>
    public void Advance(TimeSpan by)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _utcNow += by;
            _ticks  += by.Ticks;
            due = _timers.Where(t => t.IsDue(_ticks)).ToList();
        }
        foreach (var timer in due)
            timer.Fire();
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate) _timers.Add(timer);
        return timer;
    }

    /// <summary>A one-shot (or periodic) timer driven by <see cref="Advance"/>.</summary>
    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long? _dueAtTicks;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner    = owner;
            _callback = callback;
            _state    = state;
        }

        /// <summary>True when the timer is armed and its due time has been reached at <paramref name="nowTicks"/>.</summary>
        public bool IsDue(long nowTicks) => _dueAtTicks is { } due && nowTicks >= due;

        /// <summary>Runs the callback and re-arms (periodic) or disarms (one-shot) the timer.</summary>
        public void Fire()
        {
            lock (_owner._gate)
            {
                _dueAtTicks = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero
                    ? null
                    : _owner._ticks + _period.Ticks;
            }
            _callback(_state);
        }

        /// <inheritdoc/>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                _period     = period;
                _dueAtTicks = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._ticks + dueTime.Ticks;
            }
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_owner._gate)
            {
                _dueAtTicks = null;
                _owner._timers.Remove(this);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

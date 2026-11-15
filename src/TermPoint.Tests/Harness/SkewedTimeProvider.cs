namespace TermPoint.Tests.Harness;

/// <summary>
/// One computer's own clock (spec item 8.4, "one clock per computer"): the shared fake time plus a wall-clock offset that this
/// computer's person (or a dead battery, or a time-sync service) has set. Only WALL time is skewed. The monotonic timestamp
/// (<see cref="GetTimestamp"/>, <see cref="TimestampFrequency"/>) and the timers are the shared fake's, so elapsed time is the same on
/// every computer and a skew or a jump never changes how long anything took. That is the property of a real machine: setting the
/// clock moves the calendar, not the stopwatch.
/// </summary>
internal sealed class SkewedTimeProvider : TimeProvider
{
    /// <summary>The shared fake clock every computer's provider reads.</summary>
    private readonly TimeProvider _shared;

    /// <summary>The offset in ticks (a TimeSpan stored atomically), so a service thread never reads a torn value.</summary>
    private long _offsetTicks;

    /// <summary>Creates a provider with no offset.</summary>
    /// <param name="shared">The world's one fake clock.</param>
    public SkewedTimeProvider(TimeProvider shared) => _shared = shared;

    /// <summary>How far this computer's wall clock is from true (the shared) time: positive is ahead, negative is behind.</summary>
    public TimeSpan Offset
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
        set => Interlocked.Exchange(ref _offsetTicks, value.Ticks);
    }

    /// <summary>The shared time plus this computer's offset.</summary>
    public override DateTimeOffset GetUtcNow() => _shared.GetUtcNow() + Offset;

    /// <summary>The shared monotonic timestamp: skew never touches it.</summary>
    public override long GetTimestamp() => _shared.GetTimestamp();

    /// <summary>The shared monotonic frequency.</summary>
    public override long TimestampFrequency => _shared.TimestampFrequency;

    /// <summary>A timer on the shared clock (timers run on elapsed time, not on the wall clock).</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The callback's state.</param>
    /// <param name="dueTime">Time until the first tick.</param>
    /// <param name="period">Time between ticks.</param>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => _shared.CreateTimer(callback, state, dueTime, period);
}

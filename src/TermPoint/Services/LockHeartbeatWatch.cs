using System;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Measures, on THIS computer's own clock, how long the lock file's holder snapshot (its
/// <see cref="LockFileData.SessionGuid"/> and <see cref="LockFileData.Heartbeat"/>) has gone without
/// changing (spec item 9b).
///
/// <para><b>Why this exists.</b> A live writer rewrites its heartbeat every
/// <see cref="WriteLockService.HeartbeatIntervalSeconds"/> seconds, so "the heartbeat has not changed
/// for three minutes" is evidence that the writer is gone. Comparing the timestamp inside the file with
/// this computer's clock is not: the two computers' clocks can differ by minutes, which makes a live
/// writer look dead (writer clock behind) or a dead one look alive (writer clock ahead). This class
/// never compares the two clocks. It remembers the value it saw and the moment it first saw it, using a
/// monotonic stopwatch (<see cref="TimeProvider.GetTimestamp"/>), which is unaffected by the system
/// clock being adjusted. Only the CHANGE of the value matters, never its content.</para>
///
/// <para><b>One observation at a time.</b> It tracks the single most recent (lock path, session,
/// heartbeat) triple. Seeing a different triple replaces it and restarts the measurement from zero. It
/// is deliberately NOT cleared when a <see cref="WriteLockService"/> releases or re-acquires, so the
/// time already measured while polling carries into the takeover that follows, and the time measured at
/// the open-time prompt carries into the read-only polling that follows it.</para>
///
/// <para><b>Reading between sightings.</b> A writer's heartbeat only ever moves forward, so seeing the
/// identical triple at both ends of an interval proves it did not change in between, even though the
/// file was not read in the middle.</para>
///
/// <para>Thread-safe: it is touched from the thread-pool thread that runs <c>TryAcquire</c>, from the
/// UI thread that runs the poll, and from the UI code that asks how much longer a takeover must wait.</para>
/// </summary>
internal sealed class LockHeartbeatWatch
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    // The observation being timed. _lockPath is null when nothing has been observed.
    private string? _lockPath;
    private string  _sessionGuid = string.Empty;
    private DateTime _heartbeat;
    private long _firstSeen;

    /// <summary>Creates a watch that measures elapsed time with <paramref name="time"/>.</summary>
    /// <param name="time">
    /// The time source; <see cref="TimeProvider.System"/> in production, a controllable fake in tests.
    /// </param>
    public LockHeartbeatWatch(TimeProvider time) => _time = time;

    /// <summary>
    /// Records that <paramref name="holder"/> is what the lock file at <paramref name="lockPath"/> holds
    /// right now, and reports how long this exact snapshot has been unchanged. The first sighting of a
    /// snapshot (or any change from the previous one) starts the measurement and returns zero.
    /// </summary>
    /// <param name="lockPath">Full path of the lock file that was read.</param>
    /// <param name="holder">
    /// What was read from it. For an unreadable (garbled) file pass the synthetic corrupted holder, so
    /// that a file which stays garbled counts as unchanged.
    /// </param>
    /// <returns>How long the same snapshot has been observed unchanged; zero when it is new.</returns>
    public TimeSpan Observe(string lockPath, LockFileData holder)
    {
        lock (_gate)
        {
            if (!Matches(lockPath, holder))
            {
                _lockPath    = lockPath;
                _sessionGuid = holder.SessionGuid;
                _heartbeat   = holder.Heartbeat;
                _firstSeen   = _time.GetTimestamp();
            }
            return _time.GetElapsedTime(_firstSeen);
        }
    }

    /// <summary>
    /// Reports how long <paramref name="holder"/> has been observed unchanged WITHOUT recording a new
    /// sighting. Used by the takeover confirmation, which already holds the snapshot the user was asked
    /// about and only needs to know how long it has stood.
    /// </summary>
    /// <param name="lockPath">Full path of the lock file the snapshot was read from.</param>
    /// <param name="holder">The snapshot to look up.</param>
    /// <returns>
    /// How long that snapshot has been observed unchanged; zero when it is not the one being timed
    /// (never seen, or since replaced by a different one).
    /// </returns>
    public TimeSpan UnchangedFor(string lockPath, LockFileData holder)
    {
        lock (_gate)
            return Matches(lockPath, holder) ? _time.GetElapsedTime(_firstSeen) : TimeSpan.Zero;
    }

    /// <summary>
    /// Forgets the current observation. Called when the lock file is seen to be gone: whatever appears
    /// there next is a new lock and must be timed from scratch.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lockPath    = null;
            _sessionGuid = string.Empty;
            _heartbeat   = default;
        }
    }

    /// <summary>True when the snapshot being timed is exactly this path, session and heartbeat.</summary>
    /// <param name="lockPath">Lock file path to compare.</param>
    /// <param name="holder">Snapshot to compare.</param>
    private bool Matches(string lockPath, LockFileData holder)
        => _lockPath is not null
           && string.Equals(_lockPath, lockPath, StringComparison.Ordinal)
           && string.Equals(_sessionGuid, holder.SessionGuid, StringComparison.Ordinal)
           && _heartbeat == holder.Heartbeat;
}

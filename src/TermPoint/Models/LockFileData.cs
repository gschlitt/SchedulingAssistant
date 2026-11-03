using System;
using System.Text.Json.Serialization;

namespace TermPoint.Models;

/// <summary>
/// Data stored in the <c>.lock</c> file that sits alongside the database.
/// Serialized as JSON. Any instance of the app can read this file to determine
/// who currently holds the write lock and whether the lock is still alive.
/// </summary>
public class LockFileData
{
    /// <summary>The OS user name of the process that holds the lock (e.g., "jsmith").</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The machine name of the process that holds the lock (e.g., "LAPTOP-42").</summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>
    /// A GUID generated fresh for each app session. Used to distinguish this session
    /// from another by the same user on the same machine (e.g., after a crash and restart).
    /// </summary>
    public string SessionGuid { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the lock was first acquired in this session.</summary>
    public DateTime Acquired { get; set; }

    /// <summary>
    /// UTC timestamp last written by the heartbeat timer.
    /// Updated every <see cref="WriteLockService.HeartbeatIntervalSeconds"/> seconds.
    /// A heartbeat that stops CHANGING (as observed by a reader, on the reader's own clock, for
    /// <see cref="WriteLockService.StaleLockThresholdSeconds"/> seconds) indicates the holding process
    /// has died or is asleep, and the lock can be taken over with the user's confirmation. Readers do
    /// not compare this timestamp with their own clock to decide that (the two computers' clocks can
    /// disagree by minutes); it is the change, not the value, that matters. See
    /// <see cref="LockHeartbeatWatch"/>.
    /// </summary>
    public DateTime Heartbeat { get; set; }

    /// <summary>
    /// OS process id of the holder. Used together with <see cref="Machine"/> and
    /// <see cref="ProcessStartTimeUtc"/> to determine — on the same machine only — whether
    /// the holding process is still alive, so a crashed holder can be auto-reclaimed while a
    /// live sibling instance is never stolen from. Zero for locks written by older builds
    /// (no process identity); those fall back to heartbeat-age stale detection.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// UTC start time of the holding process. Compared against the live process with the
    /// same <see cref="ProcessId"/> to defend against PID reuse (the OS may assign a dead
    /// process's id to an unrelated one): a mismatch means the original holder is gone.
    /// Null for locks written by older builds.
    /// </summary>
    public DateTime? ProcessStartTimeUtc { get; set; }

    /// <summary>
    /// The factor the holder's lock timers run at (spec item 8.7, 1.2.4): 2 or 3 in a test build that runs its heartbeat,
    /// stale threshold and wake check faster (<see cref="LockTimings"/>), and ABSENT from the file (null) for a normal
    /// build, so a normal build writes exactly the lock file 1.2.3 wrote. A computer judging whether this lock is stale
    /// uses THIS factor, not its own (<see cref="LockTimings.ForHolder"/>); null, or any value outside 1 to
    /// <see cref="LockTimings.MaxScale"/>, means 1. Older versions ignore the unknown member when they read the file
    /// (System.Text.Json skips members it does not know, and this file is read with default options).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimingScale { get; set; }
}

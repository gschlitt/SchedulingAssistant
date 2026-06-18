using System;

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
    /// A value older than <see cref="WriteLockService.StaleLockThresholdSeconds"/> seconds
    /// indicates the holding process has died and the lock can safely be reclaimed.
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
}

using System;

namespace SchedulingAssistant.Models;

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

    /// <summary>UTC timestamp when the lock was first acquired in this session.</summary>
    public DateTime Acquired { get; set; }

    /// <summary>
    /// UTC timestamp last written by the heartbeat timer.
    /// Updated every <see cref="WriteLockService.HeartbeatIntervalSeconds"/> seconds.
    /// A value older than <see cref="WriteLockService.StaleLockThresholdSeconds"/> seconds
    /// indicates the holding process has died and the lock can safely be reclaimed.
    /// </summary>
    public DateTime Heartbeat { get; set; }
}

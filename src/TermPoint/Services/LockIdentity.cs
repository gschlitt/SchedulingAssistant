using System;
using System.Diagnostics;

namespace TermPoint.Services;

/// <summary>
/// A running process as <see cref="WriteLockService"/> needs to see it when it asks "is the lock holder
/// still alive?": just enough to read when the process started (the PID-reuse check). Disposed by the
/// caller, like the <see cref="Process"/> it wraps in production.
/// </summary>
internal interface ILockProcess : IDisposable
{
    /// <summary>
    /// UTC time at which the process started.
    /// </summary>
    /// <exception cref="Exception">Thrown when the start time cannot be read (for example access denied
    /// or the process has just exited); the caller treats that as "cannot tell" and never reclaims.</exception>
    DateTime StartTimeUtc { get; }
}

/// <summary>
/// Who "this TermPoint" is, as far as the write lock is concerned: the machine name, user name, process id
/// and process start time stamped into the lock file, plus the way to look up OTHER processes on the same
/// machine (the "is the holder still alive?" check). In the app this is always <see cref="Current"/>, built
/// from the real <see cref="Environment"/> and <see cref="Process"/> values exactly as
/// <see cref="WriteLockService"/> read them before this type existed. It is a seam for tests: a harness can
/// give each simulated computer its own identity, so several "computers" can share one test process.
/// </summary>
internal sealed class LockIdentity
{
    /// <summary>
    /// Creates an identity from explicit values (tests, or <see cref="Current"/>).
    /// </summary>
    /// <param name="machineName">Machine name written to the lock file and compared with a holder's.</param>
    /// <param name="userName">User name written to the lock file.</param>
    /// <param name="processId">Process id written to the lock file.</param>
    /// <param name="processStartTimeUtc">
    /// UTC start time of this process written to the lock file, or null when unreadable (then the
    /// PID-reuse defense is skipped by whoever reads the lock).
    /// </param>
    /// <param name="processLookup">
    /// Finds the process with a given id on this machine. Must behave like
    /// <see cref="Process.GetProcessById(int)"/>: throw <see cref="ArgumentException"/> when no such
    /// process exists, throw any other exception when it cannot be queried, otherwise return a process whose
    /// <see cref="ILockProcess.StartTimeUtc"/> throws if unreadable. Defaults to the real thing.
    /// </param>
    public LockIdentity(
        string machineName,
        string userName,
        int processId,
        DateTime? processStartTimeUtc,
        Func<int, ILockProcess>? processLookup = null)
    {
        MachineName         = machineName;
        UserName            = userName;
        ProcessId           = processId;
        ProcessStartTimeUtc = processStartTimeUtc;
        LookupProcess       = processLookup ?? LookupRealProcess;
    }

    /// <summary>Machine name stamped into the lock file; a holder is "on this machine" when its name matches (case-insensitive).</summary>
    public string MachineName { get; }

    /// <summary>User name stamped into the lock file.</summary>
    public string UserName { get; }

    /// <summary>Process id stamped into the lock file.</summary>
    public int ProcessId { get; }

    /// <summary>UTC start time of this process stamped into the lock file; null when it could not be read.</summary>
    public DateTime? ProcessStartTimeUtc { get; }

    /// <summary>
    /// Looks up a process on this machine by id. Throws <see cref="ArgumentException"/> when there is no
    /// such process and any other exception when it cannot be queried (see the constructor's
    /// <c>processLookup</c> contract). The caller disposes the result.
    /// </summary>
    public Func<int, ILockProcess> LookupProcess { get; }

    /// <summary>
    /// The identity of the running process, built once from the real environment. The process start time is
    /// captured here, once, exactly as the lock service's old static field did.
    /// </summary>
    public static LockIdentity Current { get; } = BuildCurrent();

    /// <summary>
    /// Builds <see cref="Current"/> from <see cref="Environment"/> and the current <see cref="Process"/>.
    /// </summary>
    /// <returns>The identity of this process.</returns>
    private static LockIdentity BuildCurrent()
        => new(Environment.MachineName, Environment.UserName, Environment.ProcessId, TryGetOwnStartTimeUtc());

    /// <summary>
    /// This process's start time in UTC, or null if it could not be read (then the PID-reuse defense is skipped).
    /// </summary>
    /// <returns>The start time, or null.</returns>
    private static DateTime? TryGetOwnStartTimeUtc()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return null; }
    }

    /// <summary>
    /// The default process lookup: <see cref="Process.GetProcessById(int)"/>, wrapped so its start time reads as UTC.
    /// </summary>
    /// <param name="processId">Id of the process to find.</param>
    /// <returns>The process; the caller disposes it.</returns>
    /// <exception cref="ArgumentException">No process with that id exists.</exception>
    private static ILockProcess LookupRealProcess(int processId) => new RealProcess(Process.GetProcessById(processId));

    /// <summary>
    /// <see cref="ILockProcess"/> over a real <see cref="Process"/>.
    /// </summary>
    private sealed class RealProcess : ILockProcess
    {
        private readonly Process _process;

        /// <summary>Wraps a process; this object owns and disposes it.</summary>
        /// <param name="process">The process to wrap.</param>
        public RealProcess(Process process) => _process = process;

        /// <inheritdoc/>
        public DateTime StartTimeUtc => _process.StartTime.ToUniversalTime();

        /// <inheritdoc/>
        public void Dispose() => _process.Dispose();
    }
}

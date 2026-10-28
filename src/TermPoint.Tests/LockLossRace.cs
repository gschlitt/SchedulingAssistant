using System.Reflection;
using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// Shared pieces of the A12 tests (a lost write lock must be reported ONCE per write session, even when the wake
/// check and the heartbeat both find the loss at the same moment). Used by <see cref="CheckoutServiceTests"/> (the
/// service on its own) and <see cref="SessionLifecycleLeaveTests"/> (the service plus the window's demotion), so the
/// log wording they pin, the way they make two checks overlap, and the way they wait for them live in one place.
/// </summary>
internal static class LockLossRace
{
    /// <summary>The part of the line the wake check logs when it finds the lock gone (also what the harness counts as one wake outcome).</summary>
    internal const string WakeVerdict = "wake check — lock is no longer ours";

    /// <summary>The part of the line the heartbeat check logs when it finds the lock gone (also what the harness counts as one heartbeat outcome).</summary>
    internal const string HeartbeatVerdict = "heartbeat verify — lock genuinely lost";

    /// <summary>
    /// The line a check logs when it finds the loss but another check already raised it for this write session. It
    /// deliberately contains neither "wake check" nor "heartbeat verify", so the harness's outcome counters ignore it.
    /// </summary>
    internal const string AlreadyHandled =
        "CheckoutService: the lock loss is already being handled for this write session; not raising it again.";

    /// <summary>How long the tests wait for background checks before giving up.</summary>
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many information lines of <paramref name="logger"/> contain <paramref name="fragment"/>. Takes the logger's
    /// lock, because the checks log from background threads while the test is looking.
    /// </summary>
    /// <param name="logger">The recording logger the service under test was given.</param>
    /// <param name="fragment">Text to look for (ordinal, case-sensitive).</param>
    /// <returns>The number of matching lines logged so far.</returns>
    internal static int CountInfos(RecordingAppLogger logger, string fragment)
    {
        lock (logger.Infos)
            return logger.Infos.Count(line => line.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>Polls <paramref name="condition"/> every few milliseconds until it is true or <paramref name="timeout"/> passes.</summary>
    /// <param name="condition">The condition to wait for; called from the test thread only.</param>
    /// <param name="timeout">The longest to wait.</param>
    /// <returns>True when the condition became true; false when the time ran out.</returns>
    internal static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(5);
        }
        return true;
    }

    /// <summary>
    /// Waits until the wake check AND the heartbeat check have both found the loss and been dealt with: both verdict
    /// lines are logged, and either the "already being handled" line is logged (the guard worked) or the loss has been
    /// raised twice (it did not, which the caller's assertions then report).
    /// </summary>
    /// <param name="logger">The recording logger the service under test was given.</param>
    /// <param name="timesRaised">How many times <see cref="CheckoutService.WriteLockLost"/> has been raised so far.</param>
    /// <returns>True when both checks have finished; false when they did not within <see cref="Deadline"/>.</returns>
    internal static Task<bool> WaitForBothChecksAsync(RecordingAppLogger logger, Func<int> timesRaised)
        => WaitUntilAsync(
            () => CountInfos(logger, WakeVerdict) >= 1
               && CountInfos(logger, HeartbeatVerdict) >= 1
               && (CountInfos(logger, AlreadyHandled) >= 1 || timesRaised() >= 2),
            Deadline);

    /// <summary>
    /// Makes two ownership checks overlap for certain. Installs a network fault hook that holds whichever check first
    /// asks whether the lock file exists until the SECOND check asks too, then lets both go on at once — so neither can
    /// reach its decision (and unsubscribe the other's trigger) before both are running. Without this the test would
    /// depend on which background thread happens to be quicker. Dispose the result to remove the hook (it is static
    /// state; tests do not run in parallel).
    /// </summary>
    /// <param name="lockPath">Path of the lock file beside D.</param>
    /// <returns>A scope that removes the hook when disposed.</returns>
    internal static IDisposable HoldFirstCheckUntilTheSecondArrives(string lockPath)
    {
        var scope = new HoldScope();
        var arrivals = 0;
        NetworkFaults.Decide = request =>
        {
            if (request.Label != "File.Exists"
                || !string.Equals(request.Path, lockPath, StringComparison.OrdinalIgnoreCase))
                return NetworkFaultDecision.None;

            var n = Interlocked.Increment(ref arrivals);
            if (n == 1)      scope.BothArrived.Wait(Deadline);   // the first check waits for the other
            else if (n == 2) scope.BothArrived.Set();            // the second arrives: release both
            return NetworkFaultDecision.None;
        };
        return scope;
    }

    /// <summary>
    /// Raises <see cref="WriteLockService.WakeDetected"/> on <paramref name="lockService"/> exactly as its wake timer
    /// does, for a lock service whose clock cannot be advanced (the lifecycle fixture builds a real one). An event can
    /// only be raised from inside its class, so the delegate is read from the event's compiler-generated field.
    /// </summary>
    /// <param name="lockService">The lock service whose subscribers (the checkout service's wake check) are called.</param>
    internal static void RaiseWakeDetected(WriteLockService lockService)
    {
        var field = typeof(WriteLockService).GetField(
            nameof(WriteLockService.WakeDetected), BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((Action?)field.GetValue(lockService))?.Invoke();
    }

    /// <summary>The scope <see cref="HoldFirstCheckUntilTheSecondArrives"/> returns: removes the fault hook on disposal.</summary>
    private sealed class HoldScope : IDisposable
    {
        /// <summary>Set when the second check has arrived; the first check waits on it.</summary>
        internal readonly ManualResetEventSlim BothArrived = new(false);

        /// <summary>Removes the fault hook and releases anything still waiting on it.</summary>
        public void Dispose()
        {
            NetworkFaults.Decide = null;
            BothArrived.Set();   // never leave a check waiting if the test ends early
        }
    }
}

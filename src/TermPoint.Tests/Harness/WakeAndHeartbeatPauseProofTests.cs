using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for the new interleavings of a wake check and a heartbeat on one computer. On a real wake (or when a frozen process
/// resumes) both fire at the same moment and both find the same lost lock; field bug A12 was both of them raising WriteLockLost, which
/// ran two overlapping demotions. The sleep event runs the two one after the other (deterministic), so the concurrency is covered by
/// pauses instead: the wake check held at <c>WakeCheck.AfterVerify</c> while the same computer's heartbeat ticks, and a heartbeat
/// verify held at <c>HeartbeatVerify.AfterVerify</c> while the wake check runs. In both, the check that is not held finds the loss and
/// demotes first; when the held one is released it must find the loss already handled. The fix is the once-per-write-session guard
/// (<c>_lockLossRaised</c> in <c>CheckoutService.HandleLockLossAsync</c>); these tests fail without it.
/// They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// </summary>
public sealed class WakeAndHeartbeatPauseProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>The log line the second check writes when it finds the loss already being handled.</summary>
    private const string AlreadyHandled = "the lock loss is already being handled for this write session";

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public WakeAndHeartbeatPauseProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Replays the keys and returns the result together with Bob's computer, so a test can read what the run did to it.
    /// </summary>
    /// <param name="keys">The event keys, in order.</param>
    /// <returns>The run and Bob's computer.</returns>
    private static async Task<(RunResult Run, Computer Bob)> RunAsync(params string[] keys)
    {
        var record = new RunRecord();
        foreach (var key in keys) record.Steps.Add(new RecordedStep { EventKey = key });

        Computer? bob = null;
        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record), new RunOptions
        {
            Title            = "a wake-check and heartbeat interleaving",
            ExtraEvents      = EventCatalogue.ExtrasFor(keys),
            ConfigureContext = ctx => bob = ctx.Computer("bob")
        });
        return (run, bob!);
    }

    /// <summary>
    /// The shared assertions: the pause really interleaved, Bob's window was told ONCE that he lost the lock, the other check logged that
    /// the loss was already being handled, and the harness found nothing new.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="bob">Bob's computer.</param>
    /// <param name="point">The point the pause held at.</param>
    private void AssertOneDemotion(RunResult run, Computer bob, string point)
    {
        _output.WriteLine(run.Story);

        var pause = Assert.Single(run.PauseResults);
        Assert.Equal(point, pause.Point);
        Assert.Equal(PauseOutcome.Interleaved, pause.Outcome);

        Assert.Equal(1, bob.LockLostRaised);
        Assert.Equal(1, bob.Logger.Infos.Count(line => line.Contains(AlreadyHandled, StringComparison.Ordinal)));
        Assert.Empty(run.NewViolations);
        Assert.Empty(run.Violations);
    }

    /// <summary>Alice took Bob's stale lock; Bob's wake check is held after it read the lock, and his heartbeat ticks meanwhile. One demotion, no unsaved edits.</summary>
    [HarnessFact]
    public async Task WakeCheckHeld_WhileTheHeartbeatTicks_DemotesOnce()
    {
        var (run, bob) = await RunAsync(
            "Open(bob)", "AdvanceTime(4m)", "Open(alice)",
            "Pause(Sleep(bob,10m) at WakeCheck.AfterVerify; BobsHeartbeatTicks)");
        AssertOneDemotion(run, bob, "WakeCheck.AfterVerify");
    }

    /// <summary>The same, with Bob holding unsaved edits that the demotion must keep.</summary>
    [HarnessFact]
    public async Task WakeCheckHeld_WhileTheHeartbeatTicks_DemotesOnce_WithUnsavedEdits()
    {
        var (run, bob) = await RunAsync(
            "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)",
            "Pause(Sleep(bob,10m) at WakeCheck.AfterVerify; BobsHeartbeatTicks)");
        AssertOneDemotion(run, bob, "WakeCheck.AfterVerify");
    }

    /// <summary>The reverse: Bob's heartbeat verify is held after it read the lock, and his wake check runs meanwhile. One demotion, no unsaved edits.</summary>
    [HarnessFact]
    public async Task HeartbeatVerifyHeld_WhileTheWakeCheckRuns_DemotesOnce()
    {
        var (run, bob) = await RunAsync(
            "Open(bob)", "AdvanceTime(4m)", "Open(alice)",
            "Pause(HeartbeatTick(bob) at HeartbeatVerify.AfterVerify; BobsWakeCheckRuns)");
        AssertOneDemotion(run, bob, "HeartbeatVerify.AfterVerify");
    }

    /// <summary>The same reverse interleaving, with unsaved edits.</summary>
    [HarnessFact]
    public async Task HeartbeatVerifyHeld_WhileTheWakeCheckRuns_DemotesOnce_WithUnsavedEdits()
    {
        var (run, bob) = await RunAsync(
            "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)",
            "Pause(HeartbeatTick(bob) at HeartbeatVerify.AfterVerify; BobsWakeCheckRuns)");
        AssertOneDemotion(run, bob, "HeartbeatVerify.AfterVerify");
    }
}

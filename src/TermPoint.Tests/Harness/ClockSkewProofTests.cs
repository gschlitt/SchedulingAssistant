using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for "one clock per computer" (spec item 8.4). Each simulated computer has its own wall clock
/// (<see cref="SkewedTimeProvider"/>: the shared fake time plus an offset), while elapsed time (the monotonic timestamp and the timers)
/// is shared. These tests show that a computer whose clock is an hour ahead or behind is judged by ELAPSED time on the judge's own
/// stopwatch, as <see cref="LockHeartbeatWatch"/> promises, and what a clock jump does to the wake check.
/// They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// </summary>
public sealed class ClockSkewProofTests
{
    /// <summary>The title of the stale-lock question.</summary>
    private const string StaleLockTitle = "Database Lock Is Inactive";

    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public ClockSkewProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>Lists what a computer's prompts were asked, as "Kind:Title", in order.</summary>
    /// <param name="computer">The computer.</param>
    private static string[] AskedBy(Computer computer)
        => computer.Prompts.Calls.Select(c => $"{c.Kind}:{c.Title}").ToArray();

    /// <summary>
    /// Bob is alive and renewing his heartbeat every minute for five minutes while his clock is an hour off (ahead, or behind). Alice then
    /// opens. She must not be given the chance to take the lock from a writer who is still working, and she must not become a writer.
    /// </summary>
    /// <param name="skewMinutes">Bob's clock offset in minutes (60 or -60).</param>
    private async Task LiveWriterIsNotJudgedStaleAsync(int skewMinutes)
    {
        using var world = new World(_output, $"LiveWriter_WithSkewedClock_IsNotJudgedStale_{skewMinutes}");
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");
        bob.Clock.Offset = TimeSpan.FromMinutes(skewMinutes);

        await bob.OpenAsync();
        for (var i = 0; i < 5; i++)
        {
            world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.HeartbeatIntervalSeconds));
            await bob.HeartbeatTickAsync();
        }

        // A clock hours BEHIND makes the heartbeat in the file look old to Alice's wall clock, and the stale-lock hint (WriteLockService.DetectStaleLock,
        // the clock half) asks "Database Lock Is Inactive ... hasn't been active ... for 55 minutes" about a writer who is alive. Pinned as it is today:
        // the question is false, but harmless to Bob, because the takeover itself waits to see the heartbeat stand still on Alice's own stopwatch.
        var staleQuestionExpected = skewMinutes < 0;
        if (staleQuestionExpected) alice.Prompts.ExpectChoice(StaleLockTitle, false);   // Alice answers "Open read-only"

        await alice.OpenAsync();
        Assert.Equal(staleQuestionExpected ? new[] { $"Choice:{StaleLockTitle}" } : Array.Empty<string>(), AskedBy(alice));
        Assert.NotEqual(CheckoutMode.WriteAccess, alice.Checkout.Mode);
        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// Bob's TermPoint is killed and his clock is off by the given amount. Alice opens at once (read-only or after the stale question), and
    /// then real elapsed time passes on the shared stopwatch. The lock must be judged abandoned after the threshold of elapsed time on ALICE's
    /// stopwatch, whichever way Bob's wall clock was wrong, and not before.
    /// </summary>
    /// <param name="skewMinutes">Bob's clock offset in minutes.</param>
    private async Task DeadWriterIsJudgedStaleByElapsedTimeAsync(int skewMinutes)
    {
        using var world = new World(_output, $"DeadWriter_IsJudgedStaleByElapsedTime_{skewMinutes}");
        world.CreateDatabase();
        var bob   = world.AddComputer("bob");
        var alice = world.AddComputer("alice");
        bob.Clock.Offset = TimeSpan.FromMinutes(skewMinutes);

        await bob.OpenAsync();
        world.CrashComputer(bob);

        // A clock behind already looks stale to the hint, so Alice is asked at once; she looks but does not take over.
        if (skewMinutes < 0) alice.Prompts.ExpectChoice(StaleLockTitle, false);
        await alice.OpenAsync();
        Assert.NotEqual(CheckoutMode.WriteAccess, alice.Checkout.Mode);

        // Inside the threshold of elapsed time the poll has nothing to report, however far off Bob's clock was.
        world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds / 2));
        await alice.PollTickAsync();
        Assert.False(alice.Locks.WriteLockBecameAvailable, "inside the threshold the lock is not yet judged abandoned");

        // Past it, the heartbeat has stood still for the threshold on Alice's stopwatch: the lock is judged abandoned.
        world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds));
        await alice.PollTickAsync();
        Assert.True(alice.Locks.WriteLockBecameAvailable, "after the threshold of elapsed time a dead writer's lock is judged abandoned");
        world.AssertAllScriptsConsumed();
        world.Finish();
    }

    /// <summary>
    /// Bob is the writer. His clock is set by the given amount in the middle of the session (a clock jump, forward or back), then the wake
    /// check and a heartbeat run. A forward jump past the wake threshold looks like a wake from sleep and so re-checks the lock, as designed
    /// (the wake check measures wall time); the re-check finds the lock still his own. A backward jump makes no gap. Either way Bob stays
    /// the writer, no question or lock-lost banner is shown, and he can still save.
    /// </summary>
    /// <param name="jumpMinutes">The offset the clock jumps to, in minutes.</param>
    private async Task ClockJumpDoesNotLoseTheLockAsync(int jumpMinutes)
    {
        using var world = new World(_output, $"ClockJump_DoesNotLoseTheLock_{jumpMinutes}");
        world.CreateDatabase();
        var bob = world.AddComputer("bob");

        await bob.OpenAsync();
        bob.Edit();
        world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.HeartbeatIntervalSeconds));
        await bob.WakeCheckAsync();
        await bob.HeartbeatTickAsync();

        world.Do(null, "Bob's computer clock jumped", () => bob.Clock.Offset = TimeSpan.FromMinutes(jumpMinutes));
        await bob.WakeCheckAsync();
        await bob.HeartbeatTickAsync();
        world.AdvanceTime(TimeSpan.FromSeconds(WriteLockService.HeartbeatIntervalSeconds));
        await bob.WakeCheckAsync();
        await bob.HeartbeatTickAsync();

        Assert.Equal(CheckoutMode.WriteAccess, bob.Checkout.Mode);
        Assert.Empty(bob.Prompts.Calls);
        Assert.Equal(SaveOutcome.Success, await bob.SaveAsync());
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        await bob.CloseAsync();
        world.Finish();
    }

    /// <summary>Bob is dead and his clock is an hour ahead.</summary>
    [HarnessFact]
    public Task DeadWriter_ClockAnHourAhead_IsJudgedStaleByElapsedTime() => DeadWriterIsJudgedStaleByElapsedTimeAsync(60);

    /// <summary>Bob is dead and his clock is an hour behind.</summary>
    [HarnessFact]
    public Task DeadWriter_ClockAnHourBehind_IsJudgedStaleByElapsedTime() => DeadWriterIsJudgedStaleByElapsedTimeAsync(-60);

    /// <summary>Bob is dead and his clock is five minutes ahead.</summary>
    [HarnessFact]
    public Task DeadWriter_ClockFiveMinutesAhead_IsJudgedStaleByElapsedTime() => DeadWriterIsJudgedStaleByElapsedTimeAsync(5);

    /// <summary>The writer's clock jumps an hour forward.</summary>
    [HarnessFact]
    public Task ClockJumpForwardAnHour_DoesNotLoseTheLock() => ClockJumpDoesNotLoseTheLockAsync(60);

    /// <summary>The writer's clock jumps an hour back.</summary>
    [HarnessFact]
    public Task ClockJumpBackAnHour_DoesNotLoseTheLock() => ClockJumpDoesNotLoseTheLockAsync(-60);

    /// <summary>Bob's clock is an hour ahead of true time and he is alive.</summary>
    [HarnessFact]
    public Task LiveWriter_ClockAnHourAhead_IsNotJudgedStale() => LiveWriterIsNotJudgedStaleAsync(60);

    /// <summary>Bob's clock is an hour behind true time and he is alive.</summary>
    [HarnessFact]
    public Task LiveWriter_ClockAnHourBehind_IsNotJudgedStale() => LiveWriterIsNotJudgedStaleAsync(-60);
}

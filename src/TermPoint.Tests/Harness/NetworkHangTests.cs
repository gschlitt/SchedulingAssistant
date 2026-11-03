using System.Diagnostics;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Tests for spec item 8.2, "a network fault that hangs instead of failing at once": the replaceable deadline clock of
/// <see cref="NetworkFileOps"/>, the <see cref="NetworkFaultKind.Hang"/> fault, the hook at the chunk write where field bug A1 blocked,
/// and the harness's network-hang event (<see cref="NetworkHangEvent"/>). The unit tests need no harness; the story tests run only with
/// <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
///
/// <para>Static state (the fault hook and the deadline clock) is shared by the whole process, so every test restores both in
/// <see cref="Dispose"/>; the test assembly runs its tests one at a time.</para>
/// </summary>
public sealed class NetworkHangTests : IDisposable
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>A private folder for the files of the unit tests.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nht_" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public NetworkHangTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Restores the fault hook and the deadline clock and removes the folder.</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        NetworkFileOps.SetTimeProviderForTests(null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── The clock ────────────────────────────────────────────────────────────

    /// <summary>
    /// Production is untouched by item 8.2: the deadline clock is the system clock and the deadline is still five seconds.
    /// </summary>
    [Fact]
    public void TheDeadlineClock_IsTheSystemClock_AndTheDeadlineIsFiveSeconds()
    {
        Assert.Same(TimeProvider.System, NetworkFileOps.Time);
        Assert.Equal(5000, NetworkFileOps.TimeoutMs);
        Assert.Equal(150, NetworkFileOps.MoveRetryDelayMs);
        Assert.Equal(1 << 16, NetworkFileOps.ChunkSize);
    }

    /// <summary>The test seam swaps the clock and null puts the system clock back.</summary>
    [Fact]
    public void TheClockSeam_SwapsAndRestores()
    {
        var fake = new FakeTimeProvider();
        NetworkFileOps.SetTimeProviderForTests(fake);
        Assert.Same(fake, NetworkFileOps.Time);
        NetworkFileOps.SetTimeProviderForTests(null);
        Assert.Same(TimeProvider.System, NetworkFileOps.Time);
    }

    // ── The Hang fault ───────────────────────────────────────────────────────

    /// <summary>
    /// A gate the unit tests hold a call on, and the landing count the production code reports back through the scope.
    /// </summary>
    private sealed class TestGate
    {
        /// <summary>Set to let the held call go on.</summary>
        public readonly ManualResetEventSlim Open = new(false);

        /// <summary>Set when the held call has reached its block.</summary>
        public readonly ManualResetEventSlim Entered = new(false);

        /// <summary>Set when the held call has finished running.</summary>
        public readonly ManualResetEventSlim Landed = new(false);

        /// <summary>The hook <see cref="NetworkFaultDecision.Hanging"/> takes.</summary>
        /// <returns>The scope the production code disposes when the call has run.</returns>
        public IDisposable Enter()
        {
            Entered.Set();
            Open.Wait(TimeSpan.FromSeconds(30));
            return new Scope(Landed);
        }

        /// <summary>Disposing it marks the call as landed.</summary>
        private sealed class Scope : IDisposable
        {
            /// <summary>The event to set.</summary>
            private readonly ManualResetEventSlim _landed;

            /// <summary>Creates the scope.</summary>
            /// <param name="landed">The event to set.</param>
            public Scope(ManualResetEventSlim landed) => _landed = landed;

            /// <inheritdoc/>
            public void Dispose() => _landed.Set();
        }
    }

    /// <summary>
    /// Waits for <paramref name="call"/> to finish while advancing the fake clock past the deadline until it does (the deadline timer is created
    /// on a pool thread a moment after the call starts, so one advance is not enough).
    /// </summary>
    /// <typeparam name="T">The call's result type.</typeparam>
    /// <param name="call">The call under test.</param>
    /// <param name="clock">The clock the call's deadline runs on.</param>
    /// <param name="gate">The gate the call is held on: the clock is advanced only once the call has reached it, so the deadline that fires is the held step's own and not an earlier step's.</param>
    /// <returns>The call's result.</returns>
    private static async Task<T> FinishByDeadlineAsync<T>(Task<T> call, FakeTimeProvider clock, TestGate gate)
    {
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(20)), "the call never reached its hang");
        var watch = Stopwatch.StartNew();
        while (!call.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(NetworkFileOps.TimeoutMs));
            await Task.Delay(5);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "the call never ended although its deadline was passed");
        }
        return await call;
    }

    /// <summary>
    /// A held generic call (here a text write) reports the timeout within the deadline while it is still blocked, and when the gate opens it
    /// really runs and lands late.
    /// </summary>
    [Fact]
    public async Task AHungWrite_TimesOutByTheDeadline_ThenLandsLateWhenTheGateOpens()
    {
        var clock = new FakeTimeProvider();
        var gate  = new TestGate();
        NetworkFileOps.SetTimeProviderForTests(clock);
        NetworkFaults.Decide = _ => NetworkFaultDecision.Hanging(gate.Enter);
        var path = Path.Combine(_dir, "late.txt");

        var completed = await FinishByDeadlineAsync(NetworkFileOps.WriteAllTextAsync(path, "late"), clock, gate);

        Assert.False(completed);                 // the caller was told "timed out"
        Assert.True(gate.Entered.IsSet);         // ... while the real write was parked on the gate
        Assert.False(File.Exists(path));         // ... and had not happened

        gate.Open.Set();
        Assert.True(gate.Landed.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal("late", File.ReadAllText(path));   // it landed late, as a real abandoned write does
    }

    /// <summary>A held hash reports "no progress" within the deadline and never throws.</summary>
    [Fact]
    public async Task AHungHash_TimesOutByTheDeadline()
    {
        var clock = new FakeTimeProvider();
        var gate  = new TestGate();
        NetworkFileOps.SetTimeProviderForTests(clock);
        NetworkFaults.Decide = _ => NetworkFaultDecision.Hanging(gate.Enter);
        var path = Path.Combine(_dir, "h.bin");
        File.WriteAllBytes(path, new byte[1000]);

        var (completed, hash) = await FinishByDeadlineAsync(NetworkFileOps.ComputeHashAsync(path), clock, gate);

        Assert.False(completed);
        Assert.Null(hash);
        gate.Open.Set();
        Assert.True(gate.Landed.Wait(TimeSpan.FromSeconds(10)));
    }

    /// <summary>A copy held at its open times out and creates nothing until the gate opens.</summary>
    [Fact]
    public async Task ACopyHungAtItsOpen_TimesOutByTheDeadline()
    {
        var clock = new FakeTimeProvider();
        var gate  = new TestGate();
        NetworkFileOps.SetTimeProviderForTests(clock);
        NetworkFaults.Decide = _ => NetworkFaultDecision.Hanging(gate.Enter, NetworkHangPoint.Start);
        var (source, dest) = MakeSource();

        var ok = await FinishByDeadlineAsync(NetworkFileOps.CopyAsync(source, dest), clock, gate);

        Assert.False(ok);
        Assert.False(File.Exists(dest));
        gate.Open.Set();
        Assert.True(gate.Landed.Wait(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// <b>The A1 spot.</b> A copy whose chunk write blocks, on the pool thread, immediately before the real write call: the fixed code
    /// (the write is started under <c>Task.Run</c>) still reports the stall within the deadline. The destination file exists (the open went
    /// through) but nothing was written.
    /// </summary>
    [Fact]
    public async Task ACopyWhoseWriteBlocks_StillTimesOutByTheDeadline_TheA1Fix()
    {
        var clock = new FakeTimeProvider();
        var gate  = new TestGate();
        NetworkFileOps.SetTimeProviderForTests(clock);
        NetworkFaults.Decide = _ => NetworkFaultDecision.Hanging(gate.Enter, NetworkHangPoint.ChunkWrite);
        var (source, dest) = MakeSource();

        var ok = await FinishByDeadlineAsync(NetworkFileOps.CopyAsync(source, dest), clock, gate);

        Assert.False(ok);                        // the deadline fired ...
        Assert.True(gate.Entered.IsSet);         // ... while the write was blocked at its hook
        Assert.False(gate.Landed.IsSet);
        gate.Open.Set();
        Assert.True(gate.Landed.Wait(TimeSpan.FromSeconds(10)));   // and the abandoned write goes on and is observed, not thrown
    }

    /// <summary>
    /// <b>Proof that the hook would have caught A1.</b> The 1.2.3 bug was a write started INLINE (no <c>Task.Run</c>): the call itself blocked on the
    /// calling thread, before the deadline race began. This test rebuilds that old shape around the same hook (the hook, then the write, called
    /// inline, then the race) and shows that, with the same gate closed, the caller never reaches its race: no deadline can fire, and the caller
    /// is stuck until the gate opens. The production code under the same hang (test above) returns by its deadline. (The old production code
    /// itself is gone, so this is a faithful model of its shape, not a run of it.)
    /// </summary>
    [Fact]
    public async Task TheOldInlineWriteShape_BlocksBeforeItsDeadlineRace_SoTheHookWouldHaveCaughtA1()
    {
        var gate    = new TestGate();
        var reached = new ManualResetEventSlim(false);

        // The OLD shape: the hook (standing for the blocked write call) runs inline, then the race starts.
        var caller = Task.Factory.StartNew(() =>
        {
            using (gate.Enter()) { /* the inline write call: returns only once the share lets it */ }
            reached.Set();                                   // the race "Task.WhenAny(writeTask, deadline)" would start here
        }, TaskCreationOptions.LongRunning);

        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));
        await Task.Delay(300);
        Assert.False(reached.IsSet, "an inline blocking write never gets to its deadline race while the share is hung");

        gate.Open.Set();
        await caller;
        Assert.True(reached.IsSet);
    }

    /// <summary>Makes a source file bigger than one chunk, so a copy writes more than once.</summary>
    /// <returns>The source and destination paths.</returns>
    private (string Source, string Dest) MakeSource()
    {
        var source = Path.Combine(_dir, "src.bin");
        File.WriteAllBytes(source, new byte[NetworkFileOps.ChunkSize + 100]);
        return (source, Path.Combine(_dir, "dst.bin"));
    }

    // ── The harness's hang ───────────────────────────────────────────────────

    /// <summary>
    /// A save during a hang reports the problem within the deadline (here: at once, the deadline fires as soon as the call is parked), loses
    /// nothing (the edit stays in the working copy and the shared database is unchanged), and after the network returns the next save lands.
    /// The held call lands late, when the network returns, and the invariants find nothing wrong with it.
    /// </summary>
    [HarnessFact]
    public async Task Hang_ASaveDuringAHang_ReportsTheTimeoutAndLosesNothing()
    {
        using var world = new World(_output, nameof(Hang_ASaveDuringAHang_ReportsTheTimeoutAndLosesNothing));
        world.CreateDatabase();
        var bob = world.AddComputer("bob");
        await bob.OpenAsync();
        bob.Edit();

        world.NetworkHang(bob);
        var watch   = Stopwatch.StartNew();
        var outcome = await bob.SaveAsync();
        watch.Stop();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(NetworkFileOps.TimeoutMs / 1000.0),
            $"the save reported its timeout after {watch.Elapsed.TotalSeconds:0.0} s: the deadline did not fire at once");
        Assert.Equal(new[] { "bob-1" }, bob.WorkingFileEdits());   // nothing lost
        Assert.Empty(world.EditsInDatabase());                      // and nothing half-saved
        Assert.True(bob.HasDirtyMarker);

        world.NetworkUp(bob);                                       // the held call lands now
        Assert.Equal(SaveOutcome.Success, await bob.SaveAsync());
        Assert.Equal(new[] { "bob-1" }, world.EditsInDatabase());
        Assert.Empty(world.Violations);
        world.Finish();
    }

    /// <summary>
    /// A computer that opens during a hang times out, and the lock acquire it had started is held; when the network returns that acquire lands
    /// late. The invariants judge what the late landing leaves (the walk declares no finding: whatever they report is shown).
    /// </summary>
    [HarnessFact]
    public async Task Hang_AnOpenDuringAHang_ThenTheNetworkReturns_LandsTheHeldCallsLate()
    {
        using var world = new World(_output, nameof(Hang_AnOpenDuringAHang_ThenTheNetworkReturns_LandsTheHeldCallsLate));
        world.CreateDatabase();
        var alice = world.AddComputer("alice");

        alice.Prompts.ExpectMessage("Network Unreachable");   // the open reports the timeout, once
        world.NetworkHang(alice);
        await alice.OpenAsync();
        world.NetworkUp(alice);

        Assert.Empty(world.Violations);
        world.Finish();
    }

    /// <summary>
    /// The hang variant of field bug A13 (spec item 6.5): Alice holds the lock, so Bob's open copies the database to a read-only snapshot, and Bob's
    /// network HANGS (instead of dropping) just before that copy. The copy blocks, the deadline ends it, and Bob is shown the same pair of messages the
    /// drop shows ("Checkout Failed", then "Couldn't Open the Database"): the known finding A13, nothing new. When the network returns the held copy lands
    /// late; the invariants find nothing wrong with that.
    /// </summary>
    [HarnessFact]
    public async Task Hang_AReadOnlyCopyThatHangs_ShowsTheSameTwoMessagesAsADrop_FindingA13()
    {
        const string key = "Pause(Open(bob) at Checkout.AfterLiveHolderSeen; BobsNetworkHangs)";
        var record = new RunRecord();
        foreach (var k in new[] { "Open(alice)", key, "NetworkUp(bob)" }) record.Steps.Add(new RecordedStep { EventKey = k });

        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record), new RunOptions
        {
            Title       = "a read-only copy that hangs at open",
            ExtraEvents = EventCatalogue.ExtrasFor(new[] { key })
        });
        _output.WriteLine(run.Story);

        var pause = Assert.Single(run.PauseResults);
        Assert.Equal(PauseOutcome.Interleaved, pause.Outcome);
        Assert.Equal("Checkout.AfterLiveHolderSeen", pause.Point);
        Assert.Contains(run.Violations, v => v.FindingId == "A13");
        Assert.Empty(run.NewViolations);
    }
}

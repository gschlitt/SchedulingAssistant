using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof test for invariant I8 (b), "one message per problem", on field bug A13 (spec item 6.5): Alice holds the lock, so Bob's open copies the database to a
/// read-only snapshot; Bob's network drops just before that copy (the pause at <c>Checkout.AfterLiveHolderSeen</c>). The copy fails, and today Bob is shown
/// "Checkout Failed" (which promises the program will open read-only) and THEN "Couldn't Open the Database" (Try again / Close): two messages, the first wrong.
/// The fix is a separate item, so A13 is a known finding; these tests pin that the harness sees the pair.
/// They run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// </summary>
public sealed class OneMessagePerProblemProofTests
{
    /// <summary>The test output, where each story is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public OneMessagePerProblemProofTests(ITestOutputHelper output) => _output = output;

    /// <summary>The key of the pause event: Bob's open is held just before the read-only copy while his network drops.</summary>
    private const string PauseKey = "Pause(Open(bob) at Checkout.AfterLiveHolderSeen; BobsNetworkDrops)";

    /// <summary>Replays Alice's open and Bob's paused open and returns the result with the world (for its message sightings).</summary>
    private static async Task<(RunResult Run, World World)> RunAsync()
    {
        var record = new RunRecord();
        foreach (var key in new[] { "Open(alice)", PauseKey }) record.Steps.Add(new RecordedStep { EventKey = key });

        World? world = null;
        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record), new RunOptions
        {
            Title       = "a read-only copy that fails at open",
            ExtraEvents = EventCatalogue.ExtrasFor(new[] { PauseKey }),
            ConfigureWorld = w => world = w
        });
        return (run, world!);
    }

    /// <summary>The pause really held Bob's open at the new point while his network dropped.</summary>
    /// <param name="run">The run.</param>
    private static void AssertInterleaved(RunResult run)
    {
        var pause = Assert.Single(run.PauseResults);
        Assert.Equal("Checkout.AfterLiveHolderSeen", pause.Point);
        Assert.Equal(PauseOutcome.Interleaved, pause.Outcome);
    }

    /// <summary>The two messages are a violation, classified as the known finding A13.</summary>
    [HarnessFact]
    public async Task FailedReadOnlyCopy_ShowsTwoMessages_AndIsFindingA13()
    {
        var (run, world) = await RunAsync();
        _output.WriteLine(run.Story);

        AssertInterleaved(run);
        var a13 = Assert.Single(run.Violations, v => v.FindingId == "A13");
        Assert.Equal("I8", a13.Invariant);
        Assert.Empty(run.NewViolations);

        var sighting = Assert.Single(world.MessagePairSightings);
        Assert.StartsWith("Checkout Failed | ", sighting.First);
        Assert.StartsWith("Couldn't Open the Database | ", sighting.Second);
    }
}

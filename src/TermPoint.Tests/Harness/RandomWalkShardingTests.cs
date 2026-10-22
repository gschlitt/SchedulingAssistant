using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Proof tests for splitting the random walks across separate test processes (spec item 9.3, step 9.3c-2b): the sharded run finds
/// exactly what the in-process loop finds; a shard that fails fails the test with its output; a shard that takes too long is killed
/// (that process's tree only) and fails the test. The tests launch real child test processes, so they run only with
/// <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// </summary>
public sealed class RandomWalkShardingTests
{
    /// <summary>The test output, where progress is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public RandomWalkShardingTests(ITestOutputHelper output) => _output = output;

    /// <summary>Writes a line to the test output.</summary>
    /// <param name="line">The line.</param>
    private void Say(string line)
    {
        try { _output.WriteLine(line); } catch (InvalidOperationException) { /* the test already finished */ }
    }

    /// <summary>
    /// For a small seed range, the walks split across two processes give the same log as the in-process loop: the same number of
    /// runs and events, the same events ever enabled, and for every kind of breach the same number of runs that showed it, the same
    /// finding, and the same size of smallest story. (A walk is a pure function of its seed, so how the seeds are dealt out cannot matter.)
    /// </summary>
    [HarnessFact]
    public async Task Sharded_GivesTheSameTalliesAsTheInProcessLoop()
    {
        var inProcess = new HarnessBudget(3, Seeds: 24, Steps: 25, Processes: 1);
        var sharded   = inProcess with { Processes = 2 };

        var single = await Explorers.RunRandomAsync(inProcess, Say);
        var split  = await Explorers.RunRandomAsync(sharded, Say);
        Say($"in this process: {single.Elapsed:mm\\:ss}; in 2 processes: {split.Elapsed:mm\\:ss}");

        Assert.Equal(single.Log.Runs, split.Log.Runs);
        Assert.Equal(single.Log.Events, split.Log.Events);
        Assert.Equal(single.Log.RunsEndedByMemo, split.Log.RunsEndedByMemo);
        Assert.Equal(single.Log.EnabledKeys.OrderBy(k => k), split.Log.EnabledKeys.OrderBy(k => k));
        Assert.Equal(single.Log.Tallies.Keys, split.Log.Tallies.Keys);

        // The comparison must have had something to compare. It used to require a breach in the range, but most of the
        // range's breaches were F1, and as findings are fixed (F1, F8 and F12 in 1.2.3) a small range may show none. The
        // runs, the events and the events ever enabled are compared above in any case; require that they are not trivial.
        Assert.True(single.Log.Events > 0, "the walks made no events, so the comparison proves nothing");
        Assert.NotEmpty(single.Log.EnabledKeys);

        foreach (var (signature, expected) in single.Log.Tallies)
        {
            var actual = split.Log.Tallies[signature];
            Assert.Equal(expected.Runs, actual.Runs);
            Assert.Equal(expected.FindingId, actual.FindingId);
            Assert.Equal(expected.BestEvents, actual.BestEvents);
        }
    }

    /// <summary>A shard that fails (here: its test filter matches nothing, so it writes no result) fails the parent test with the shard's output.</summary>
    [HarnessFact]
    public async Task AShardThatFails_FailsTheTestWithItsOutput()
    {
        RandomWalkSharding.FilterOverride = "FullyQualifiedName~NoSuchShardTestAnywhere";
        try
        {
            var budget = new HarnessBudget(3, Seeds: 4, Steps: 10, Processes: 2);
            var failure = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => RandomWalkSharding.RunAsync(budget, Say));
            Say(failure.Message);
            Assert.Contains("Random-walk shard", failure.Message);
            Assert.Contains("failed", failure.Message);
        }
        finally
        {
            RandomWalkSharding.FilterOverride = null;
        }
    }

    /// <summary>
    /// A shard that takes too long is killed, and only the process this test started: the failure names its process id, the test fails
    /// at the limit (not at the child's own pace), and that process is gone afterwards.
    /// </summary>
    [HarnessFact]
    public async Task AShardThatTakesTooLong_IsKilled_AndFailsTheTest()
    {
        var budget = new HarnessBudget(3, Seeds: 200, Steps: 30, Processes: 2);
        var watch  = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(
            () => RandomWalkSharding.RunAsync(budget, Say, timeout: TimeSpan.FromSeconds(2)));
        watch.Stop();
        Say(failure.Message);

        Assert.Contains("did not finish within 2 seconds and were killed", failure.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"the test took {watch.Elapsed}: the shards should have been killed at the limit");

        foreach (Match match in Regex.Matches(failure.Message, @"\(process (\d+)\)"))
        {
            var pid = int.Parse(match.Groups[1].Value);
            Assert.True(await HasExitedAsync(pid), $"process {pid} is still running after it was killed");
        }
    }

    /// <summary>Waits a few seconds for a process we started to be gone.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>True when no process with that id is running.</returns>
    private static async Task<bool> HasExitedAsync(int processId)
    {
        for (var i = 0; i < 50; i++)
        {
            try { using var process = Process.GetProcessById(processId); if (process.HasExited) return true; }
            catch (ArgumentException) { return true; }
            await Task.Delay(100);
        }
        return false;
    }
}

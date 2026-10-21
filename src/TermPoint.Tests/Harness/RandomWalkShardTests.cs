using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// A <see cref="FactAttribute"/> for the shard test (spec item 9.3, step 9.3c-2b): the test runs only when a parent test process has
/// put the name of a shard job file in the environment variable <see cref="RandomWalkSharding.JobVariable"/>. Without it the test is
/// reported as <em>skipped</em>, so a normal run, and a harness run, never run a shard by themselves.
/// </summary>
public sealed class ShardFactAttribute : FactAttribute
{
    /// <summary>Marks the test as skipped unless a parent process named a job file.</summary>
    public ShardFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RandomWalkSharding.JobVariable)))
            Skip = $"Random-walk shard: a parent harness run sets {RandomWalkSharding.JobVariable} to run it.";
    }
}

/// <summary>
/// The child side of the sharded random walks (spec item 9.3, step 9.3c-2b). A parent test process starts
/// <c>dotnet test &lt;this assembly&gt; --filter RandomWalkShardTests</c> once per shard; this test reads the job (the seeds and the
/// number of events), runs those random walks in THIS process, and writes the tallies to the result file the job names. It decides
/// nothing: new violations are the parent's to report, after it has merged every shard. It fails only when the walks themselves could
/// not be run (an event hung, say), and then the parent fails with this process's output.
/// </summary>
public sealed class RandomWalkShardTests
{
    /// <summary>The test output, where progress is written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public RandomWalkShardTests(ITestOutputHelper output) => _output = output;

    /// <summary>Runs the walks of the job named in the environment and writes the result file.</summary>
    [ShardFact]
    public async Task Shard_RunsItsSeedsAndWritesItsTallies()
    {
        var jobFile = Environment.GetEnvironmentVariable(RandomWalkSharding.JobVariable)!;
        var job = JsonSerializer.Deserialize<ShardJob>(File.ReadAllText(jobFile))
                  ?? throw new InvalidOperationException($"The shard job file {jobFile} is empty.");

        var log = await RandomWalkSharding.RunSeedsAsync(job.Seeds, job.Steps, line =>
        {
            try { _output.WriteLine(line); } catch (InvalidOperationException) { /* the test already finished */ }
        });

        File.WriteAllText(job.ResultFile, JsonSerializer.Serialize(RandomWalkSharding.ToResult(log)));
        _output.WriteLine($"shard done: {log.Runs} walks, {log.Events} events, {log.Tallies.Count} kinds of breach");
    }
}

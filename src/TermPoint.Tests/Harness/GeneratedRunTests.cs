using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The generated tests of the multi-computer harness (spec item 9.3, step 9.3c-1): runs produced from decisions instead
/// of written by hand. The bounded exhaustive search and the random walks explore, a shrinker shortens whatever breaks,
/// and a central list of known findings (<see cref="KnownFindings"/>) decides what fails: only a breach no known
/// finding explains. After the generated tests a Markdown report is written to the path in the environment variable
/// <c>TERMPOINT_HARNESS_REPORT</c> (default <c>%TEMP%/termpoint-harness-report.md</c>) and to the test output.
///
/// <para>The budget comes from <c>TERMPOINT_HARNESS_DEPTH</c>, <c>TERMPOINT_HARNESS_SEEDS</c> and
/// <c>TERMPOINT_HARNESS_STEPS</c>. The tests run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>).
/// The exhaustive search and the random walks run once per test process and are shared by the tests that read them.</para>
/// </summary>
public sealed class GeneratedRunTests
{
    /// <summary>The test output, where progress and the report are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public GeneratedRunTests(ITestOutputHelper output) => _output = output;

    /// <summary>Writes a line to the test output, ignoring a test that has already ended (a shared run reports late).</summary>
    /// <param name="line">The line.</param>
    private void Say(string line)
    {
        try { _output.WriteLine(line); } catch (InvalidOperationException) { /* the test that started the run has finished */ }
    }

    /// <summary>Builds the report from every strategy that has finished, writes it to its file and to the test output.</summary>
    /// <param name="budget">The budget in force.</param>
    private void PublishReport(HarnessBudget budget)
    {
        var text = HarnessReport.Build(budget, GeneratedSuite.Finished());
        HarnessReport.Write(text);
        Say(text);
        Say($"(report written to {HarnessReport.Path})");
    }

    /// <summary>Fails the test when a strategy found a breach no known finding explains, with each one's shrunk story.</summary>
    /// <param name="outcome">What the strategy found.</param>
    private static void AssertNoNewViolations(StrategyOutcome outcome)
    {
        var fresh = outcome.Log.NewTallies.ToList();
        if (fresh.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{outcome.Name} found {fresh.Count} breach(es) that no known finding explains:");
        foreach (var t in fresh)
        {
            sb.AppendLine();
            sb.AppendLine($"--- {t.Signature} ({t.Runs} runs; shrunk to {t.BestEvents} events) ---");
            sb.AppendLine(t.Reason);
            sb.AppendLine(t.BestStory);
        }
        Assert.Fail(sb.ToString());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The strategies
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every sequence of enabled events up to the budget's depth (with every combination of dialog answers), pruned by
    /// the abstract-state memo. Fails only on a NEW violation, with its shrunk story.
    /// </summary>
    [HarnessFact]
    public async Task Generated_Exhaustive_NoNewViolations()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.ExhaustiveAsync(budget, Say);
        PublishReport(budget);
        AssertNoNewViolations(outcome);
    }

    /// <summary>
    /// Random walks from fixed seeds (so every run is repeatable). Fails only on a NEW violation, with its shrunk story.
    /// </summary>
    [HarnessFact]
    public async Task Generated_RandomWalks_NoNewViolations()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.RandomAsync(budget, Say);
        PublishReport(budget);
        AssertNoNewViolations(outcome);
    }

    /// <summary>
    /// Across the known-sequence replays and both strategies at the budget, the generated runs find F1, F6 and F7. F1
    /// and F7 come from the search; F6 needs a route longer than the search reaches by default, so it comes from the
    /// replay list (<see cref="KnownSequences"/>) and the report says so. F5 and F12 are reported (F12 needs a pause in
    /// the middle of a save, which comes with step 9.3c-2).
    /// </summary>
    [HarnessFact]
    public async Task Generated_RediscoversKnownFindings()
    {
        var budget     = HarnessBudget.FromEnvironment();
        var exhaustive = await GeneratedSuite.ExhaustiveAsync(budget, Say);   // replays first, then the search
        var random     = await GeneratedSuite.RandomAsync(budget, Say);
        var replays    = await GeneratedSuite.ReplaysAsync(Say);
        PublishReport(budget);

        static HashSet<string> Known(StrategyOutcome o) => o.Log.Tallies.Values.Where(t => t.IsKnown).Select(t => t.FindingId).ToHashSet();
        var bySearch = Known(exhaustive).Union(Known(random)).ToHashSet();
        var byReplay = Known(replays);
        var found    = bySearch.Union(byReplay).ToHashSet();

        Say("Known findings found by the search: " + (bySearch.Count == 0 ? "none" : string.Join(", ", bySearch.OrderBy(f => f))));
        Say("Known findings found by a replay: " + (byReplay.Count == 0 ? "none" : string.Join(", ", byReplay.OrderBy(f => f))));
        Say($"F5 reached: {found.Contains("F5")}.  F12 reached: {found.Contains("F12")}.");

        foreach (var id in new[] { "F1", "F6", "F7" })
            Assert.True(found.Contains(id), $"{id} was not found by a replay, the exhaustive search or the random walks at this budget.");
    }

    /// <summary>
    /// Every known sequence still shows the finding its entry names, with no breach nobody explains. A sequence that no
    /// longer does is stale: either the finding was fixed (take the entry off the list with the finding) or the route is
    /// no longer possible (the event catalogue changed).
    /// </summary>
    [HarnessFact]
    public async Task KnownSequences_EachShowsItsFinding()
    {
        var outcome = await GeneratedSuite.ReplaysAsync(Say);
        foreach (var replay in outcome.Replays)
        {
            Assert.True(replay.ShowsItsFinding,
                $"The known sequence \"{replay.Sequence.Name}\" no longer shows {replay.Sequence.Finding} (it showed: " +
                $"{(replay.FindingIds.Count == 0 ? "nothing" : string.Join(", ", replay.FindingIds))}).");
            Assert.Equal(0, replay.NewViolations);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Replay and the shrinker
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A recorded run replayed from its record gives the same decisions and the same story: the property the exhaustive
    /// search, the shrinker and every "run it again" rely on.
    /// </summary>
    [HarnessFact]
    public async Task Replay_OfARandomRun_GivesTheSameDecisionsAndTheSameStory()
    {
        var options  = new RunOptions { Title = "a replay check" };
        var original = await RunExecutor.ExecuteAsync(new RandomSource(7, 25), options);
        var replayed = await RunExecutor.ExecuteAsync(new ReplaySource(original.Record), options);

        Assert.Equal(original.Record.ToLines(), replayed.Record.ToLines());
        Assert.Equal(original.Story, replayed.Story);
        Assert.Equal(original.Violations.Select(v => v.Signature), replayed.Violations.Select(v => v.Signature));
    }

    /// <summary>
    /// The sleep event: Bob edits, his computer sleeps for ten minutes (network down, no timers) and wakes; the wake
    /// check finds the lock still his and renews it, so nothing is lost and nothing is asked. Proves the event runs
    /// end to end, including the wait for the fire-and-forget wake handler.
    /// </summary>
    [HarnessFact]
    public async Task Replay_OfASleep_WakesTheComputerAndKeepsTheLock()
    {
        var record = new RunRecord();
        foreach (var key in new[] { "Open(bob)", "Edit(bob)", "Sleep(bob,10m)", "Save(bob)" })
            record.Steps.Add(new RecordedStep { EventKey = key });

        var result = await RunExecutor.ExecuteAsync(new ReplaySource(record), new RunOptions { Title = "a sleep" });
        _output.WriteLine(result.Story);

        Assert.Contains("woke up", result.Story);
        Assert.Contains("Bob saved the database — Success", result.Story);
        Assert.Empty(result.Violations);
    }

    /// <summary>
    /// F5 reached by a written-down run: Bob edits and crashes, Alice takes over the stale lock, changes NOTHING and closes
    /// (her close still rewrites the shared database), and Bob returns and is refused although nobody changed any data.
    /// The record names only events; every dialog gets its simplest answer (take over, wait, Restore, Save a Copy…).
    /// </summary>
    [HarnessFact]
    public async Task Replay_OfANoEditSession_IsClassifiedAsF5_NotF7()
    {
        var record = new RunRecord();
        foreach (var key in new[]
                 {
                     "Open(bob)", "Edit(bob)", "Crash(bob)", "AdvanceTime(4m)",
                     "Open(alice)", "Close(alice)", "Open(bob)"
                 })
            record.Steps.Add(new RecordedStep { EventKey = key });

        var result = await RunExecutor.ExecuteAsync(new ReplaySource(record), new RunOptions { Title = "a no-edit session" });
        _output.WriteLine(result.Story);

        Assert.Contains(result.Violations, v => v.FindingId == "F5");
        Assert.DoesNotContain(result.Violations, v => v.FindingId == "F7");
        Assert.Empty(result.NewViolations);
    }

    /// <summary>
    /// The shrinker and the "new violation" path, tested by planting a bug WITHOUT changing production code: at the crash
    /// point <c>Dirty.AfterMarkerWritten</c> a hook deletes the marker that was just written ("MarkDirty forgot the
    /// marker"). A kept working file without its marker is deleted by the next start, so after a crash the invariant I2
    /// (nothing applied is lost) must fire, and no known finding explains it: it is NEW. Random walks run until one does;
    /// the shrinker must then reduce the run to a handful of steps.
    /// </summary>
    [HarnessFact]
    public async Task Shrinker_PlantedBug_IsCaughtAsNew_AndShrunk()
    {
        var options = new RunOptions
        {
            Title = "planted bug: MarkDirty forgets the marker",
            ConfigureWorld = world => world.AtCrashPointEveryTime("Dirty.AfterMarkerWritten", () =>
            {
                var actor = world.CurrentActor;
                if (actor is not null && File.Exists(actor.MarkerFile)) File.Delete(actor.MarkerFile);
            })
        };

        RunResult? failing = null;
        FoundViolation? breach = null;
        var tried = 0;
        for (var seed = 1; seed <= 300 && failing is null; seed++)
        {
            tried++;
            var result = await RunExecutor.ExecuteAsync(new RandomSource(seed, 30), options);
            breach = result.NewViolations.FirstOrDefault(v => v.Invariant == "I2");
            if (breach is not null) failing = result;
        }

        Assert.True(failing is not null, $"no random walk of {tried} found the planted bug");
        _output.WriteLine($"The planted bug was found by seed {tried} after {breach!.EventIndex} events: {breach.Reason}");

        var shrunk = await Shrinker.ShrinkAsync(failing!.Record.Take(breach.EventIndex), breach.Signature, options);
        _output.WriteLine($"Shrunk from {breach.EventIndex} to {shrunk.Events} events in {shrunk.Replays} replays:");
        _output.WriteLine(shrunk.Story);

        Assert.True(shrunk.Reproduced, "the failing run did not reproduce on replay");
        Assert.True(shrunk.Events <= 5, $"the shrunk story still has {shrunk.Events} events");
    }
}

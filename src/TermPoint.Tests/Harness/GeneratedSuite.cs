using System.Diagnostics;
using System.Text;

namespace TermPoint.Tests.Harness;

/// <summary>What the generated runs found of one kind of breach (one <see cref="FoundViolation.Signature"/>).</summary>
internal sealed class FindingTally
{
    /// <summary>Invariant, kind and finding, as <see cref="FoundViolation.Signature"/> writes them.</summary>
    public required string Signature { get; init; }

    /// <summary>"F1", "F5", ... or "new".</summary>
    public required string FindingId { get; init; }

    /// <summary>"I1" to "I5", or "exception".</summary>
    public required string Invariant { get; init; }

    /// <summary>The reason text of the first occurrence.</summary>
    public required string Reason { get; init; }

    /// <summary>In how many runs it appeared.</summary>
    public int Runs { get; set; }

    /// <summary>The number of events of the smallest run that shows it (after shrinking); <see cref="int.MaxValue"/> until one is shrunk.</summary>
    public int BestEvents { get; set; } = int.MaxValue;

    /// <summary>The printed story of the smallest run.</summary>
    public string BestStory { get; set; } = "";

    /// <summary>How many candidates the shrinks of this breach replayed in all.</summary>
    public int ShrinkReplays { get; set; }

    /// <summary>False when a replay did not reproduce the breach (the story is then the unshrunk first one).</summary>
    public bool Reproduced { get; set; } = true;

    /// <summary>True when a known finding explains the breach.</summary>
    public bool IsKnown => KnownFindings.IsKnown(FindingId);
}

/// <summary>
/// Everything one strategy found (spec item 9.3, step 9.3c): the tallies of breaches (known and new) with their smallest
/// shrunk stories, the events that were ever enabled (coverage), and the counts of runs and events. Each observed run is
/// folded in, and a breach seen for the first time, or by a shorter run than the best so far, is shrunk.
/// </summary>
internal sealed class GeneratedLog
{
    /// <summary>The options every shrinking replay runs with.</summary>
    private readonly RunOptions _options;

    /// <summary>Creates the log.</summary>
    /// <param name="options">The options replays run with.</param>
    public GeneratedLog(RunOptions options) => _options = options;

    /// <summary>The tallies, by signature.</summary>
    public SortedDictionary<string, FindingTally> Tallies { get; } = new(StringComparer.Ordinal);

    /// <summary>The keys of every event that was enabled in some run.</summary>
    public HashSet<string> EnabledKeys { get; } = new();

    /// <summary>The number of runs observed.</summary>
    public int Runs { get; private set; }

    /// <summary>The number of runs that ended because their state was already explored.</summary>
    public int RunsEndedByMemo { get; private set; }

    /// <summary>The number of events that happened in all runs.</summary>
    public int Events { get; private set; }

    /// <summary>The time the runs themselves took (shrinking replays are counted separately).</summary>
    public TimeSpan RunTime { get; private set; }

    /// <summary>The number of replays the shrinker made in all.</summary>
    public int ShrinkReplays { get; private set; }

    /// <summary>The breaches no known finding explains.</summary>
    public IEnumerable<FindingTally> NewTallies => Tallies.Values.Where(t => !t.IsKnown);

    /// <summary>
    /// Folds in everything another process found (step 9.3c-2b: the random walks run in several processes). The counts are added, the
    /// tallies of one signature are added, and the shorter story wins; on a tie the story already held (the earlier shard's) stays,
    /// so merging shards in shard order always gives the same log.
    /// </summary>
    /// <param name="shard">What one shard process found.</param>
    internal void Absorb(ShardResult shard)
    {
        Runs            += shard.Runs;
        RunsEndedByMemo += shard.RunsEndedByMemo;
        Events          += shard.Events;
        RunTime         += TimeSpan.FromMilliseconds(shard.RunTimeMilliseconds);
        ShrinkReplays   += shard.ShrinkReplays;
        EnabledKeys.UnionWith(shard.EnabledKeys);

        foreach (var found in shard.Tallies)
        {
            if (!Tallies.TryGetValue(found.Signature, out var tally))
            {
                tally = new FindingTally
                {
                    Signature = found.Signature, FindingId = found.FindingId, Invariant = found.Invariant, Reason = found.Reason,
                    BestEvents = found.BestEvents, BestStory = found.BestStory, Reproduced = found.Reproduced
                };
                Tallies[found.Signature] = tally;
            }
            else if (found.BestEvents < tally.BestEvents)
            {
                tally.BestEvents = found.BestEvents;
                tally.BestStory  = found.BestStory;
                tally.Reproduced = found.Reproduced;
            }
            tally.Runs          += found.Runs;
            tally.ShrinkReplays += found.ShrinkReplays;
        }
    }

    /// <summary>Folds one run into the log, shrinking any breach that is new to it or shorter than before.</summary>
    /// <param name="result">The run.</param>
    /// <param name="options">The options the run had (the extra events a replay of it needs); null for the log's own options.</param>
    /// <returns>A task that completes when the run (and any shrinking) has been folded in.</returns>
    /// <exception cref="InvalidOperationException">An event of the run hung, so the process may be in an unknown state.</exception>
    public async Task ObserveAsync(RunResult result, RunOptions? options = null)
    {
        Runs++;
        Events += result.Events;
        RunTime += result.Elapsed;
        if (result.EndedByMemo) RunsEndedByMemo++;
        EnabledKeys.UnionWith(result.EnabledKeys);

        if (result.Hung)
            throw new InvalidOperationException("A generated run hung, so the exploration stopped:\n" + result.Story);

        foreach (var group in result.Violations.GroupBy(v => v.Signature))
        {
            var first = group.First();
            if (!Tallies.TryGetValue(first.Signature, out var tally))
            {
                tally = new FindingTally
                {
                    Signature = first.Signature, FindingId = first.FindingId, Invariant = first.Invariant, Reason = first.Reason
                };
                Tallies[first.Signature] = tally;
            }
            tally.Runs++;

            if (first.EventIndex >= tally.BestEvents) continue;

            var shrunk = await Shrinker.ShrinkAsync(result.Record.Take(first.EventIndex), first.Signature, options ?? _options);
            tally.ShrinkReplays += shrunk.Replays;
            ShrinkReplays       += shrunk.Replays;
            if (shrunk.Reproduced)
            {
                tally.BestEvents = shrunk.Events;
                tally.BestStory  = shrunk.Story;
            }
            else
            {
                tally.Reproduced = false;
                tally.BestEvents = first.EventIndex;
                tally.BestStory  = result.Story;
            }
        }
    }
}

/// <summary>What one strategy did.</summary>
internal sealed class StrategyOutcome
{
    /// <summary>"Bounded exhaustive" or "Random walks".</summary>
    public required string Name { get; init; }

    /// <summary>The parameters in words ("depth 3").</summary>
    public required string Parameters { get; init; }

    /// <summary>What it found.</summary>
    public required GeneratedLog Log { get; init; }

    /// <summary>Wall-clock time of the whole strategy, shrinking included.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>For the exhaustive search: the number of distinct abstract states reached; otherwise 0.</summary>
    public int DistinctStates { get; init; }

    /// <summary>True for the replay of the known sequences (<see cref="KnownSequences"/>), which is not a search: the report credits its findings to "replay".</summary>
    public bool IsReplay { get; init; }

    /// <summary>For the replay of the known sequences: what each sequence did, in list order; empty for a search.</summary>
    public IReadOnlyList<ReplayedSequence> Replays { get; init; } = Array.Empty<ReplayedSequence>();

    /// <summary>True for the crash sweep (<see cref="CrashSweep"/>): the report credits its findings to "sweep".</summary>
    public bool IsSweep { get; init; }

    /// <summary>For the crash sweep: its coverage and probes; null for every other strategy.</summary>
    public CrashSweepResult? Sweep { get; init; }

    /// <summary>True for the pause sweep (<see cref="PauseSweep"/>, step 9.3c-2b): the report credits its findings to "the pause sweep".</summary>
    public bool IsPauseSweep { get; init; }

    /// <summary>For the pause sweep: how its pauses ended; null for every other strategy.</summary>
    public PauseSweepResult? PauseSweep { get; init; }
}

/// <summary>What the replay of one <see cref="KnownSequence"/> did.</summary>
/// <param name="Sequence">The sequence.</param>
/// <param name="Events">The number of events that really happened (a skipped event does not count).</param>
/// <param name="FindingIds">The known findings the replay showed, sorted.</param>
/// <param name="NewViolations">The breaches no known finding explains (should be none).</param>
internal sealed record ReplayedSequence(KnownSequence Sequence, int Events, IReadOnlyList<string> FindingIds, int NewViolations)
{
    /// <summary>True when the replay showed the finding its entry promises.</summary>
    public bool ShowsItsFinding => FindingIds.Contains(Sequence.Finding);
}

/// <summary>
/// The strategies (spec item 9.3, step 9.3c). Both build a fresh world for every run: the exhaustive search replays each
/// branch from its prefix, which is deterministic, and a random walk is a pure function of its seed.
/// </summary>
internal static class Explorers
{
    /// <summary>
    /// <b>Bounded exhaustive:</b> depth-first over every sequence of enabled events up to <see cref="HarnessBudget.Depth"/>
    /// events; dialog answers inside an event are extra branching, not extra depth. A branch whose abstract state
    /// (<see cref="AbstractState"/>) was already explored at an equal or smaller depth is not explored again.
    /// </summary>
    /// <param name="budget">The budget.</param>
    /// <param name="progress">Receives a progress line now and then; may be null.</param>
    /// <returns>What the search found.</returns>
    public static async Task<StrategyOutcome> RunExhaustiveAsync(HarnessBudget budget, Action<string>? progress)
    {
        var stopwatch = Stopwatch.StartNew();
        var options   = new RunOptions { Title = "an exhaustive run" };
        var log       = new GeneratedLog(options);
        var path      = new ChoicePath();
        var memo      = new Dictionary<string, int>();

        do
        {
            var source = new ExhaustiveSource(path, budget.Depth, memo);
            var result = await RunExecutor.ExecuteAsync(source, options);
            await log.ObserveAsync(result);

            if (log.Runs % 200 == 0)
                progress?.Invoke($"exhaustive: {log.Runs} runs, {memo.Count} states, {log.Tallies.Count} kinds of breach, {stopwatch.Elapsed:mm\\:ss}");
        }
        while (path.Advance());

        stopwatch.Stop();
        return new StrategyOutcome
        {
            Name = "Bounded exhaustive", Parameters = $"depth {budget.Depth}", Log = log, Elapsed = stopwatch.Elapsed,
            DistinctStates = memo.Count
        };
    }

    /// <summary>
    /// <b>Known sequences:</b> replays every entry of <see cref="KnownSequences.All"/>, once, before any search. Not a
    /// search: it exists so a finding the bounded search cannot reach (F6) is still rediscovered and shown, and so the
    /// report can say which findings came from the search and which only from a replay.
    /// </summary>
    /// <param name="progress">Receives a progress line per sequence; may be null.</param>
    /// <returns>What the replays found.</returns>
    public static async Task<StrategyOutcome> RunKnownSequencesAsync(Action<string>? progress)
    {
        var stopwatch = Stopwatch.StartNew();
        var options   = new RunOptions { Title = "a known sequence" };
        var log       = new GeneratedLog(options);
        var replays   = new List<ReplayedSequence>();

        foreach (var sequence in KnownSequences.All)
        {
            var sequenceOptions = new RunOptions { Title = sequence.Name, ExtraEvents = EventCatalogue.ExtrasFor(sequence.Events) };
            var result = await RunExecutor.ExecuteAsync(new ReplaySource(sequence.ToRecord()), sequenceOptions);
            await log.ObserveAsync(result, sequenceOptions);

            var ids = result.Violations.Where(v => v.IsKnown).Select(v => v.FindingId).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
            replays.Add(new ReplayedSequence(sequence, result.Events, ids, result.NewViolations.Count()));
            progress?.Invoke($"known sequence \"{sequence.Name}\": {result.Events} events, shows {(ids.Count == 0 ? "nothing" : string.Join(", ", ids))}");
        }

        stopwatch.Stop();
        return new StrategyOutcome
        {
            Name = "Known sequences", Parameters = $"{KnownSequences.All.Count} replayed", Log = log, Elapsed = stopwatch.Elapsed,
            IsReplay = true, Replays = replays
        };
    }

    /// <summary><b>Random walks:</b> one run per seed (1 to <see cref="HarnessBudget.Seeds"/>), each <see cref="HarnessBudget.Steps"/> events long.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="progress">Receives a progress line now and then; may be null.</param>
    /// <returns>What the walks found.</returns>
    public static async Task<StrategyOutcome> RunRandomAsync(HarnessBudget budget, Action<string>? progress)
    {
        // The walks are split across separate test processes by seed range when more than one is asked for (step 9.3c-2b); the merged
        // log is shaped exactly like the one the loop below fills, so everything that reads it is unchanged.
        if (budget.Processes > 1 && budget.Seeds > 1)
            return await RandomWalkSharding.RunAsync(budget, progress);

        var stopwatch = Stopwatch.StartNew();
        var log       = await RandomWalkSharding.RunSeedsAsync(Enumerable.Range(1, budget.Seeds), budget.Steps, progress);

        stopwatch.Stop();
        return new StrategyOutcome
        {
            Name = "Random walks", Parameters = $"{budget.Seeds} seeds x {budget.Steps} events", Log = log, Elapsed = stopwatch.Elapsed
        };
    }
}

/// <summary>
/// The Markdown report of the generated tests (spec item 9.3, step 9.3c): the budget, the runs and the time, each known
/// finding with how often it appeared and its smallest shrunk story, any NEW breach with its shrunk story, and the events
/// that were never enabled in any run (a coverage hint). Everything above the "Timing" section is deterministic: the same
/// code and the same budget give the same text.
/// </summary>
internal static class HarnessReport
{
    /// <summary>The environment variable that names the report file.</summary>
    public const string PathVariable = "TERMPOINT_HARNESS_REPORT";

    /// <summary>The file the report is written to: the variable's value, or <c>termpoint-harness-report.md</c> in the temp folder.</summary>
    public static string Path
        => Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } custom
            ? custom
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "termpoint-harness-report.md");

    /// <summary>Builds the report from whichever strategies have run.</summary>
    /// <param name="budget">The budget in force.</param>
    /// <param name="outcomes">The strategies that have run, in order.</param>
    /// <returns>The Markdown text.</returns>
    public static string Build(HarnessBudget budget, IReadOnlyList<StrategyOutcome> outcomes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# TermPoint harness: generated runs, crash sweep and pause sweep (steps 9.3c-1, 9.3c-2a and 9.3c-2b)");
        sb.AppendLine();
        sb.AppendLine("## Budget");
        sb.AppendLine($"- Exhaustive depth: {budget.Depth} ({HarnessBudget.DepthVariable})");
        sb.AppendLine($"- Random walks: {budget.Seeds} seeds ({HarnessBudget.SeedsVariable}) x {budget.Steps} events ({HarnessBudget.StepsVariable}), " +
                      $"split across {budget.Processes} process{(budget.Processes == 1 ? "" : "es")} ({HarnessBudget.ProcessesVariable}; 1 = in the test process)");
        sb.AppendLine($"- Crash sweep width: {budget.CrashWidth} ({HarnessBudget.CrashWidthVariable}; 1 = the fixed starting situations, each point cut the first time it is reached; 2 = more situations, later times a point is reached, and a follow-up after the restart)");
        sb.AppendLine($"- Pause sweep width: {budget.PauseWidth} ({HarnessBudget.PauseWidthVariable}; 1 = the multi-step operations at their save, demotion, release, restore and checkout points, each (operation, point, event) once, then a crash and a restart; 2 = every operation and point, later times a point is reached, the wide situations, and a \"close normally\" follow-up as well)");
        sb.AppendLine();

        sb.AppendLine("## Runs");
        foreach (var o in outcomes)
        {
            sb.AppendLine($"- **{o.Name}** ({o.Parameters}): {o.Log.Runs} runs, {o.Log.Events} events" +
                          (o.DistinctStates > 0 ? $", {o.DistinctStates} distinct states, {o.Log.RunsEndedByMemo} runs cut short by the memo" : "") +
                          $", {o.Log.ShrinkReplays} shrink replays");
        }
        sb.AppendLine();

        var merged = new SortedDictionary<string, (FindingTally Best, int Runs)>(StringComparer.Ordinal);
        foreach (var tally in outcomes.SelectMany(o => o.Log.Tallies.Values))
        {
            if (!merged.TryGetValue(tally.Signature, out var current))
                merged[tally.Signature] = (tally, tally.Runs);
            else
                merged[tally.Signature] = (tally.BestEvents < current.Best.BestEvents ? tally : current.Best, current.Runs + tally.Runs);
        }

        var replayed = outcomes.SelectMany(o => o.Replays).ToList();
        if (replayed.Count > 0)
        {
            sb.AppendLine("## Known sequences (replayed before the search)");
            foreach (var r in replayed)
            {
                sb.AppendLine($"- **{r.Sequence.Name}**: {r.Events} events, shows " +
                              (r.FindingIds.Count == 0 ? "nothing" : string.Join(", ", r.FindingIds)) +
                              (r.ShowsItsFinding ? "" : $" (EXPECTED {r.Sequence.Finding}: no longer reproduced)") +
                              (r.NewViolations > 0 ? $"; {r.NewViolations} NEW breach(es)" : "") +
                              $". {r.Sequence.Reason}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Known findings (expected on today's code)");
        foreach (var id in KnownFindings.Ids)
        {
            int Runs(Func<StrategyOutcome, bool> pick)
                => outcomes.Where(pick).SelectMany(o => o.Log.Tallies.Values).Where(t => t.FindingId == id).Sum(t => t.Runs);

            var search = Runs(o => !o.IsReplay && !o.IsSweep && !o.IsPauseSweep);
            var replay = Runs(o => o.IsReplay);
            var sweep  = Runs(o => o.IsSweep);
            var pause  = Runs(o => o.IsPauseSweep);

            var parts = new List<string>();
            if (search > 0) parts.Add($"the search in {search} runs");
            if (replay > 0) parts.Add($"replay in {replay} runs");
            if (sweep > 0)  parts.Add($"the crash sweep in {sweep} runs");
            if (pause > 0)  parts.Add($"the pause sweep in {pause} runs");
            var source = parts.Count == 0 ? "not found" : "found by " + string.Join(" and by ", parts);
            if (search == 0 && parts.Count > 0) source += "; the search did not reach it";
            sb.AppendLine($"- **{id}**: {source}. {KnownFindings.Describe(id)}");
        }
        sb.AppendLine();

        foreach (var (best, runs) in merged.Values.Where(m => m.Best.IsKnown).OrderBy(m => m.Best.FindingId, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {best.FindingId}: {best.Signature} ({runs} runs, smallest story {best.BestEvents} events)");
            sb.AppendLine("```");
            sb.Append(best.BestStory.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine();
        }

        var newOnes = merged.Values.Where(m => !m.Best.IsKnown).ToList();
        sb.AppendLine($"## NEW violations ({newOnes.Count})");
        if (newOnes.Count == 0) sb.AppendLine("None.");
        sb.AppendLine();
        foreach (var (best, runs) in newOnes)
        {
            sb.AppendLine($"### {best.Signature} ({runs} runs, smallest story {best.BestEvents} events)");
            sb.AppendLine($"{best.Reason}");
            sb.AppendLine("```");
            sb.Append(best.BestStory.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine();
        }

        foreach (var sweep in outcomes.Where(o => o.Sweep is not null))
            sb.Append(CrashSweepReport.Build(sweep));

        foreach (var sweep in outcomes.Where(o => o.PauseSweep is not null))
            sb.Append(PauseSweepReport.Build(sweep));

        var enabled = outcomes.Where(o => !o.IsSweep && !o.IsPauseSweep).SelectMany(o => o.Log.EnabledKeys).ToHashSet();
        var unreached = EventCatalogue.AllKeys().Where(k => !enabled.Contains(k)).ToList();
        sb.AppendLine("## Events never enabled in any run");
        sb.AppendLine(unreached.Count == 0 ? "None: every event was enabled at some point." : string.Join(", ", unreached));
        sb.AppendLine();

        sb.AppendLine("## Timing (varies from run to run)");
        foreach (var o in outcomes)
        {
            var perRun = o.Log.Runs == 0 ? 0 : o.Log.RunTime.TotalMilliseconds / o.Log.Runs;
            sb.AppendLine($"- {o.Name}: {o.Elapsed:hh\\:mm\\:ss} in all; {perRun:0} ms per run on average (shrinking not included)");
        }
        return sb.ToString();
    }

    /// <summary>Writes the report to <see cref="Path"/>.</summary>
    /// <param name="text">The report.</param>
    public static void Write(string text)
    {
        try { File.WriteAllText(Path, text); }
        catch (IOException) { /* the report is also in the test output */ }
        catch (UnauthorizedAccessException) { /* the report is also in the test output */ }
    }
}

/// <summary>
/// Runs each strategy once per test process and shares the result between the tests that look at it: the exhaustive test,
/// the random-walk test and the "rediscovers known findings" test all read the same runs, so the suite pays for each
/// strategy once.
/// </summary>
internal static class GeneratedSuite
{
    /// <summary>Guards the cached tasks.</summary>
    private static readonly object Gate = new();

    /// <summary>The replay of the known sequences, once started.</summary>
    private static Task<StrategyOutcome>? _replays;

    /// <summary>The exhaustive search, once started.</summary>
    private static Task<StrategyOutcome>? _exhaustive;

    /// <summary>The random walks, once started.</summary>
    private static Task<StrategyOutcome>? _random;

    /// <summary>The crash sweep, once started.</summary>
    private static Task<StrategyOutcome>? _sweep;

    /// <summary>The pause sweep (step 9.3c-2b), once started.</summary>
    private static Task<StrategyOutcome>? _pauseSweep;

    /// <summary>Starts the exhaustive search the first time, and returns it.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="progress">Receives progress lines (from the test that happens to start it).</param>
    public static Task<StrategyOutcome> ExhaustiveAsync(HarnessBudget budget, Action<string> progress)
    {
        lock (Gate) return _exhaustive ??= AfterReplaysAsync(progress, () => Explorers.RunExhaustiveAsync(budget, progress));
    }

    /// <summary>Starts the random walks the first time, and returns them.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="progress">Receives progress lines (from the test that happens to start it).</param>
    public static Task<StrategyOutcome> RandomAsync(HarnessBudget budget, Action<string> progress)
    {
        lock (Gate) return _random ??= AfterReplaysAsync(progress, () => Explorers.RunRandomAsync(budget, progress));
    }

    /// <summary>Starts the crash sweep (step 9.3c-2a) the first time, and returns it.</summary>
    /// <param name="budget">The budget (its crash width sets how wide the sweep goes).</param>
    /// <param name="progress">Receives progress lines (from the test that happens to start it).</param>
    public static Task<StrategyOutcome> SweepAsync(HarnessBudget budget, Action<string> progress)
    {
        lock (Gate) return _sweep ??= CrashSweep.RunAsync(budget, progress);
    }

    /// <summary>Starts the pause sweep (step 9.3c-2b) the first time, and returns it.</summary>
    /// <param name="budget">The budget (its pause width sets how wide the sweep goes).</param>
    /// <param name="progress">Receives progress lines (from the test that happens to start it).</param>
    public static Task<StrategyOutcome> PauseSweepAsync(HarnessBudget budget, Action<string> progress)
    {
        lock (Gate) return _pauseSweep ??= PauseSweep.RunAsync(budget, progress);
    }

    /// <summary>Replays the known sequences the first time, and returns that outcome.</summary>
    /// <param name="progress">Receives progress lines (from the test that happens to start it).</param>
    public static Task<StrategyOutcome> ReplaysAsync(Action<string> progress)
    {
        lock (Gate) return _replays ??= Explorers.RunKnownSequencesAsync(progress);
    }

    /// <summary>Runs the known-sequence replays (once per process, shared), then the given search: every exploration starts with the replays.</summary>
    /// <param name="progress">Receives progress lines.</param>
    /// <param name="search">Starts the search.</param>
    private static async Task<StrategyOutcome> AfterReplaysAsync(Action<string> progress, Func<Task<StrategyOutcome>> search)
    {
        await ReplaysAsync(progress);
        return await search();
    }

    /// <summary>The strategies that have finished, in the order known-sequence replays, exhaustive, random, crash sweep, pause sweep.</summary>
    public static IReadOnlyList<StrategyOutcome> Finished()
    {
        lock (Gate)
        {
            var list = new List<StrategyOutcome>();
            if (_replays is { IsCompletedSuccessfully: true }) list.Add(_replays.Result);
            if (_exhaustive is { IsCompletedSuccessfully: true }) list.Add(_exhaustive.Result);
            if (_random is { IsCompletedSuccessfully: true }) list.Add(_random.Result);
            if (_sweep is { IsCompletedSuccessfully: true }) list.Add(_sweep.Result);
            if (_pauseSweep is { IsCompletedSuccessfully: true }) list.Add(_pauseSweep.Result);
            return list;
        }
    }
}

using System.Diagnostics;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// A short, fixed way to reach a state worth cutting the power in (spec item 9.3, step 9.3c-2a): "Bob edited", "Alice
/// holds a stale lock". Written as event keys; every dialog on the way gets its simplest answer. The crash sweep tries
/// every operation of the named computers in the state the prefix leaves.
/// </summary>
/// <param name="Name">A short name for the report and the story ("Bob edited").</param>
/// <param name="Prefix">The event keys that lead to the state.</param>
/// <param name="Computers">The computers whose operations are cut in this state (each is tried in turn).</param>
/// <param name="Wide">True for a situation that only the wider sweep (<see cref="HarnessBudget.CrashWidth"/> 2) tries.</param>
internal sealed record CrashSituation(string Name, IReadOnlyList<string> Prefix, IReadOnlyList<string> Computers, bool Wide = false);

/// <summary>
/// One operation the crash sweep cuts the power in: an event of a computer, with the dialog answers it needs. The same
/// event can appear twice with different answers (opening and choosing Restore, opening and choosing Discard).
/// </summary>
/// <param name="Name">What the person does, for the report ("Open, Restore").</param>
/// <param name="Make">Builds the event for a computer (by name).</param>
/// <param name="Answers">The dialog answers the event is recorded with, for a computer (by name); null for the simplest answers.</param>
internal sealed record SweepOperation(
    string Name, Func<string, ComputerEvent> Make, Func<string, IReadOnlyList<RecordedAnswer>>? Answers = null);

/// <summary>What the crash sweep found out about one (starting situation, computer, operation) before cutting anything.</summary>
/// <param name="Situation">The starting situation.</param>
/// <param name="Computer">The computer's name.</param>
/// <param name="Operation">The operation.</param>
/// <param name="Applicable">True when the operation was enabled in the state (the dry run did it).</param>
/// <param name="Points">The crash points the computer reached while doing it, in order, with repeats.</param>
internal sealed record SweepProbe(
    string Situation, string Computer, string Operation, bool Applicable, IReadOnlyList<string> Points);

/// <summary>What the crash sweep did: the probes, how many cases ran, and which crash points were reached and cut.</summary>
internal sealed class CrashSweepResult
{
    /// <summary>Every (situation, computer, operation) the sweep probed with a dry run, in order.</summary>
    public List<SweepProbe> Probes { get; } = new();

    /// <summary>For each crash point: in how many probes (dry runs of an operation) it was reached.</summary>
    public Dictionary<string, int> Reached { get; } = new(StringComparer.Ordinal);

    /// <summary>For each crash point: in how many cases the power was really cut there.</summary>
    public Dictionary<string, int> Cut { get; } = new(StringComparer.Ordinal);

    /// <summary>For each crash point: the first case (situation / operation) that cut the power there, for the report.</summary>
    public Dictionary<string, string> FirstCase { get; } = new(StringComparer.Ordinal);

    /// <summary>The number of cases run (a cut, then a restart).</summary>
    public int Cases { get; set; }

    /// <summary>The number of cases in which the planned cut did not fire (the replay did not reach the point again; should be none).</summary>
    public int CasesNotCut { get; set; }

    /// <summary>The number of dry runs.</summary>
    public int DryRuns { get; set; }

    /// <summary>The number of situations the sweep used.</summary>
    public int Situations { get; set; }

    /// <summary>The width the sweep ran at.</summary>
    public int Width { get; set; }

    /// <summary>The cases whose planned cut did not fire, described.</summary>
    public List<string> NotCutCases { get; } = new();
}

/// <summary>
/// The crash sweep (spec item 9.3, step 9.3c-2a): for each multi-step operation and each crash point it reaches, a power
/// cut there, then a restart, with every invariant checked through the normal funnel.
///
/// <para><b>How a case is made.</b> From a small fixed set of starting situations (<see cref="Situations"/>) and a list
/// of operations (<see cref="Operations"/>): (1) a DRY RUN does the situation's prefix and then the operation, and
/// records which crash points the computer reached; (2) for each of those points, a CASE replays the prefix, does the
/// operation with the power cut at the point, and then restarts the computer by opening the database again with the
/// simplest answers. A case is an ordinary decision list (<see cref="RunRecord"/>) whose cut is the event
/// <see cref="PowerCutEvent"/>, so a failing case is replayed and shrunk by the same machinery as a search run, and
/// printed by the same story printer.</para>
///
/// <para><b>What is not covered.</b> A point is cut only if some operation in some situation reaches it; the report lists
/// the points no case could cut, with the reason (<see cref="CrashSweepCoverage.MissReasons"/>).</para>
/// </summary>
internal static class CrashSweep
{
    /// <summary>
    /// The starting situations. The first group is the default; the second (<see cref="CrashSituation.Wide"/>) is added by
    /// <see cref="HarnessBudget.CrashWidth"/> 2.
    /// </summary>
    public static IReadOnlyList<CrashSituation> Situations { get; } = new CrashSituation[]
    {
        new("Bob edited",
            new[] { "Open(bob)", "Edit(bob)" },
            new[] { "bob" }),

        new("Bob edited and saved once, then edited again",
            new[] { "Open(bob)", "Edit(bob)", "Save(bob)", "Edit(bob)" },
            new[] { "bob" }),

        new("Bob crashed with kept changes",
            new[] { "Open(bob)", "Edit(bob)", "Crash(bob)" },
            new[] { "bob" }),

        new("Alice holds a stale lock",
            new[] { "Open(alice)", "Edit(alice)", "Crash(alice)", "AdvanceTime(4m)" },
            new[] { "bob" }),

        new("Bob lost the lock to Alice",
            new[] { "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)" },
            new[] { "bob", "alice" }),

        new("Bob is reading while Alice edits",
            new[] { "Open(alice)", "Edit(alice)", "Open(bob)" },
            new[] { "bob", "alice" }),

        new("Bob's save was refused (conflict banner)",
            new[] { "Open(bob)", "Edit(bob)", "ColleagueWrites", "Save(bob)" },
            new[] { "bob" }),

        new("Bob saved after a backup was taken",
            new[] { "TakeSnapshot", "Open(bob)", "Edit(bob)", "Save(bob)" },
            new[] { "bob" }),

        // The wider sweep.
        new("Bob is on the other database",
            new[] { "Open(bob)", "Edit(bob)", "SwitchAway(bob)" },
            new[] { "bob" }, Wide: true),

        new("Bob lost the lock and was demoted",
            new[] { "Open(bob)", "Edit(bob)", "AdvanceTime(4m)", "Open(alice)", "HeartbeatTick(bob)" },
            new[] { "bob", "alice" }, Wide: true),

        new("Bob crashed after saving once",
            new[] { "Open(bob)", "Edit(bob)", "Save(bob)", "Edit(bob)", "Crash(bob)" },
            new[] { "bob" }, Wide: true),

        new("Alice holds a stale lock and Bob has kept changes",
            new[] { "Open(bob)", "Edit(bob)", "Crash(bob)", "AdvanceTime(4m)", "Open(alice)", "Close(alice)" },
            new[] { "bob", "alice" }, Wide: true),
    };

    /// <summary>What a shrunk case is called in its story (the case it came from named the longer run).</summary>
    private const string ShrunkTitle = "the shortest run found that still shows it";

    /// <summary>The Restore question's dialog label, as the run's decision source sees it, for a computer.</summary>
    /// <param name="computer">The computer's name.</param>
    private static string RestoreQuestion(string computer) => $"{computer}: {HarnessTitles.RestoreUnsavedChanges}";

    /// <summary>The operations the sweep cuts the power in, in the order they are tried.</summary>
    public static IReadOnlyList<SweepOperation> Operations { get; } = new SweepOperation[]
    {
        new("Edit", c => new EditEvent(c)),
        new("Save", c => new SaveEvent(c)),
        new("Autosave tick", c => new AutosaveTickEvent(c)),
        new("Heartbeat tick", c => new HeartbeatTickEvent(c)),
        new("Poll tick", c => new PollTickEvent(c)),
        new("Close", c => new CloseEvent(c)),
        new("Switch to the other database", c => new SwitchAwayEvent(c)),
        new("Switch back", c => new SwitchBackEvent(c)),
        new("Open (Restore, take over: the simplest answers)", c => new OpenEvent(c)),
        new("Open, answering Discard to the restore question", c => new OpenEvent(c),
            c => new[] { new RecordedAnswer(RestoreQuestion(c), "Negative", GeneratedPrompts.ChoiceOptions) }),
        new("Ask to take over as writer (from reading)", c => new ReopenForEditingEvent(c)),
        new("Save a Copy on the conflict banner", c => new ResolveConflictEvent(c, saveCopy: true)),
        new("Discard Changes on the conflict banner", c => new ResolveConflictEvent(c, saveCopy: false)),
        new("Sleep 10 minutes (wake check)", c => new SleepEvent(c, TimeSpan.FromMinutes(10), "10m", "4m")),
        new("Refresh the read-only view", c => new RefreshEvent(c)),
        new("Restore from a backup", c => new RestoreBackupEvent(c)),
    };

    /// <summary>The situations a sweep of the given width uses.</summary>
    /// <param name="width">1 (default) or 2.</param>
    public static IEnumerable<CrashSituation> SituationsFor(int width) => Situations.Where(s => width >= 2 || !s.Wide);

    /// <summary>
    /// Builds a decision list: the situation's prefix, then the steps given.
    /// </summary>
    /// <param name="prefix">The prefix's event keys.</param>
    /// <param name="steps">The steps that follow.</param>
    private static RunRecord Record(IEnumerable<string> prefix, params RecordedStep[] steps)
    {
        var record = new RunRecord();
        foreach (var key in prefix) record.Steps.Add(new RecordedStep { EventKey = key });
        record.Steps.AddRange(steps);
        return record;
    }

    /// <summary>
    /// The decision list of one case: the prefix, the operation with the power cut at the point, the restart (the same
    /// computer opens the database again with the simplest answers) and, at width 2, a follow-up in which the other
    /// computer opens and closes.
    /// </summary>
    /// <param name="situation">The starting situation.</param>
    /// <param name="computer">The computer whose power is cut.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="point">The crash point.</param>
    /// <param name="occurrence">Which time the point is reached.</param>
    /// <param name="width">The sweep's width.</param>
    /// <returns>The decision list.</returns>
    public static RunRecord CaseRecord(
        CrashSituation situation, string computer, SweepOperation operation, string point, int occurrence, int width)
    {
        var inner = operation.Make(computer);
        var cut   = new PowerCutEvent(inner, point, occurrence);
        var steps = new List<RecordedStep>
        {
            new() { EventKey = cut.Key, Answers = operation.Answers?.Invoke(computer).ToList() ?? new List<RecordedAnswer>() },
            new() { EventKey = $"Open({computer})" },
        };

        if (width >= 2)
        {
            var other = EventCatalogue.ComputerNames.First(n => n != computer);
            steps.Add(new RecordedStep { EventKey = $"Close({computer})" });
            steps.Add(new RecordedStep { EventKey = "AdvanceTime(4m)" });
            steps.Add(new RecordedStep { EventKey = $"Open({other})" });
            steps.Add(new RecordedStep { EventKey = $"Close({other})" });
        }
        return Record(situation.Prefix, steps.ToArray());
    }

    /// <summary>
    /// Runs the sweep: every situation, every computer of it, every operation; a dry run each, then a case per crash
    /// point the dry run reached. Breaches are folded into the returned log (known ones counted, new ones shrunk).
    /// </summary>
    /// <param name="budget">The budget (its <see cref="HarnessBudget.CrashWidth"/> sets the width).</param>
    /// <param name="progress">Receives a progress line now and then; may be null.</param>
    /// <returns>What the sweep found and covered.</returns>
    /// <exception cref="InvalidOperationException">A situation's prefix does not replay (an event in it was not enabled), so the sweep would not be testing what it says.</exception>
    public static async Task<StrategyOutcome> RunAsync(HarnessBudget budget, Action<string>? progress)
    {
        var stopwatch = Stopwatch.StartNew();
        var options   = new RunOptions { Title = "a crash sweep run" };
        var log       = new GeneratedLog(options);
        var result    = new CrashSweepResult { Width = budget.CrashWidth };

        foreach (var point in CrashPoints.All)
        {
            result.Reached[point] = 0;
            result.Cut[point]     = 0;
        }

        foreach (var situation in SituationsFor(budget.CrashWidth))
        {
            result.Situations++;
            foreach (var computer in situation.Computers)
            {
                foreach (var operation in Operations)
                {
                    var innerKey = operation.Make(computer).Key;

                    // 1. The dry run: what does this operation reach, in this situation?
                    var dryRecord = Record(situation.Prefix,
                        new RecordedStep { EventKey = innerKey, Answers = operation.Answers?.Invoke(computer).ToList() ?? new List<RecordedAnswer>() });
                    var dryOptions = new RunOptions
                    {
                        Title = $"{situation.Name} / {computer}: {operation.Name} (a dry run)",
                        ExtraEvents = EventCatalogue.ExtrasFor(new[] { innerKey })
                    };
                    var dry = await RunExecutor.ExecuteAsync(new ReplaySource(dryRecord), dryOptions);
                    result.DryRuns++;
                    await log.ObserveAsync(dry, dryOptions.WithTitle(ShrunkTitle));

                    var prefixHeld = dry.Record.Steps.Take(situation.Prefix.Count).Select(s => s.EventKey).SequenceEqual(situation.Prefix);
                    if (!prefixHeld && !dry.Violations.Any(v => !v.IsKnown))
                        throw new InvalidOperationException(
                            $"The crash sweep's situation \"{situation.Name}\" does not replay: its prefix was cut short at event {dry.Record.Steps.Count}. " +
                            $"Fix the prefix ({string.Join(", ", situation.Prefix)}).");

                    var applicable = prefixHeld && dry.Record.Steps.Count == situation.Prefix.Count + 1 && dry.Record.Steps[^1].EventKey == innerKey;
                    var points = applicable && dry.HitsByEvent.Count > situation.Prefix.Count
                        ? dry.HitsByEvent[situation.Prefix.Count].Where(h => h.Computer == computer).Select(h => h.Point).ToList()
                        : new List<string>();
                    result.Probes.Add(new SweepProbe(situation.Name, computer, operation.Name, applicable, points));
                    foreach (var point in points.Distinct()) result.Reached[point]++;

                    // 2. One case per crash point it reached (width 2: per time it is reached, up to three).
                    foreach (var group in points.GroupBy(p => p))
                    {
                        var times = budget.CrashWidth >= 2 ? Math.Min(3, group.Count()) : 1;
                        for (var occurrence = 1; occurrence <= times; occurrence++)
                            await RunCaseAsync(situation, computer, operation, group.Key, occurrence, budget.CrashWidth, result, log);
                    }

                    if (result.DryRuns % 25 == 0)
                        progress?.Invoke($"crash sweep: {result.DryRuns} dry runs, {result.Cases} cases, {log.Tallies.Count} kinds of breach, {stopwatch.Elapsed:mm\\:ss}");
                }
            }
        }

        stopwatch.Stop();
        return new StrategyOutcome
        {
            Name = "Crash sweep", Parameters = $"width {budget.CrashWidth}, {result.Situations} situations", Log = log, Elapsed = stopwatch.Elapsed,
            IsSweep = true, Sweep = result
        };
    }

    /// <summary>Runs one case and folds it into the result and the log.</summary>
    /// <param name="situation">The starting situation.</param>
    /// <param name="computer">The computer whose power is cut.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="point">The crash point.</param>
    /// <param name="occurrence">Which time the point is reached.</param>
    /// <param name="width">The sweep's width.</param>
    /// <param name="result">Where coverage is counted.</param>
    /// <param name="log">Where breaches are folded in.</param>
    private static async Task RunCaseAsync(
        CrashSituation situation, string computer, SweepOperation operation, string point, int occurrence, int width,
        CrashSweepResult result, GeneratedLog log)
    {
        var record = CaseRecord(situation, computer, operation, point, occurrence, width);
        var options = new RunOptions
        {
            Title = $"{situation.Name} / {computer}: {operation.Name}, power cut at {point}" + (occurrence > 1 ? $" (time {occurrence})" : ""),
            ExtraEvents = EventCatalogue.ExtrasFor(record.Steps.Select(s => s.EventKey))
        };

        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record), options);
        result.Cases++;
        await log.ObserveAsync(run, options.WithTitle(ShrunkTitle));

        var cutHere = run.FiredCuts.Contains($"{computer}@{point}");
        if (cutHere)
        {
            result.Cut[point]++;
            result.FirstCase.TryAdd(point, $"{situation.Name} / {computer}: {operation.Name}");
        }
        else if (!run.Violations.Any(v => !v.IsKnown))
        {
            result.CasesNotCut++;
            result.NotCutCases.Add($"{situation.Name} / {computer}: {operation.Name}, {point} #{occurrence}");
        }
    }
}

/// <summary>
/// Why a crash point is not cut by any case of the sweep. Every point the sweep misses must have an entry here, and an entry
/// for a point the sweep does cut is stale: a test checks both ways, so the report's "not hit" list is always explained
/// and never out of date.
/// </summary>
internal static class CrashSweepCoverage
{
    /// <summary>Crash point to the reason no case cuts the power there.</summary>
    public static IReadOnlyDictionary<string, string> MissReasons { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
    };

    /// <summary>The reason for a point the sweep did not cut, or a loud placeholder when nobody wrote one.</summary>
    /// <param name="point">The crash point.</param>
    public static string ReasonFor(string point)
        => MissReasons.TryGetValue(point, out var reason) ? reason : "NO REASON RECORDED";
}

/// <summary>
/// The "Crash sweep" section of the harness report (spec item 9.3, step 9.3c-2a): how many cases ran, which crash points
/// were reached and which were really cut, the points that were never cut with the reason for each, and where each
/// operation was possible. Deterministic: the same code and the same budget give the same text.
/// </summary>
internal static class CrashSweepReport
{
    /// <summary>The crash points that at least one case cut the power at.</summary>
    /// <param name="sweep">The sweep's result.</param>
    public static IReadOnlyList<string> CutPoints(CrashSweepResult sweep)
        => CrashPoints.All.Where(p => sweep.Cut.TryGetValue(p, out var n) && n > 0).ToList();

    /// <summary>The crash points no case cut the power at, in catalogue order.</summary>
    /// <param name="sweep">The sweep's result.</param>
    public static IReadOnlyList<string> MissedPoints(CrashSweepResult sweep)
        => CrashPoints.All.Where(p => !sweep.Cut.TryGetValue(p, out var n) || n == 0).ToList();

    /// <summary>Builds the section.</summary>
    /// <param name="outcome">The crash sweep's outcome.</param>
    /// <returns>Markdown, ending with a blank line.</returns>
    public static string Build(StrategyOutcome outcome)
    {
        var sweep = outcome.Sweep!;
        var sb    = new System.Text.StringBuilder();
        var cut   = CutPoints(sweep);
        var missed = MissedPoints(sweep);

        sb.AppendLine("## Crash sweep");
        sb.AppendLine($"- Width {sweep.Width} ({HarnessBudget.CrashWidthVariable}): {sweep.Situations} starting situations, {sweep.DryRuns} dry runs " +
                      $"(an operation done once in a situation to see which points it reaches), {sweep.Cases} cases (power cut, restart, every invariant).");
        sb.AppendLine($"- **Points cut: {cut.Count} of {CrashPoints.All.Count}** (a case really cut the power there). " +
                      $"Reached in some dry run: {CrashPoints.All.Count(p => sweep.Reached[p] > 0)} of {CrashPoints.All.Count}.");
        sb.AppendLine($"- Cases whose planned cut did not fire: {sweep.CasesNotCut}" + (sweep.CasesNotCut == 0 ? "." : ":"));
        foreach (var line in sweep.NotCutCases) sb.AppendLine($"  - {line}");
        sb.AppendLine();

        sb.AppendLine("### Per point");
        sb.AppendLine("| Point | Dry runs that reach it | Cases that cut it | First case that cut it |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var point in CrashPoints.All)
        {
            sb.AppendLine($"| {point} | {sweep.Reached[point]} | {sweep.Cut[point]} | " +
                          (sweep.FirstCase.TryGetValue(point, out var first) ? first : "-") + " |");
        }
        sb.AppendLine();

        sb.AppendLine($"### Points never cut ({missed.Count})");
        if (missed.Count == 0) sb.AppendLine("None: every crash point was cut at least once.");
        foreach (var point in missed) sb.AppendLine($"- **{point}**: {CrashSweepCoverage.ReasonFor(point)}");
        sb.AppendLine();

        sb.AppendLine("### Where each operation was possible");
        foreach (var group in sweep.Probes.GroupBy(p => p.Situation))
        {
            sb.AppendLine($"- **{group.Key}**");
            foreach (var computer in group.GroupBy(p => p.Computer))
            {
                var possible = computer.Where(p => p.Applicable)
                    .Select(p => $"{p.Operation} ({p.Points.Distinct().Count()} points)");
                sb.AppendLine($"  - {computer.Key}: " + (possible.Any() ? string.Join("; ", possible) : "no operation was possible"));
            }
        }
        sb.AppendLine();
        return sb.ToString();
    }
}

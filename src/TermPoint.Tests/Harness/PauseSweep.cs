using System.Diagnostics;
using System.Text;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>What the pause sweep does after the pause step, to see what the interleaving left behind.</summary>
internal enum PauseFollowUp
{
    /// <summary>The paused computer crashes and starts again (the default answers: Restore). This is what makes finding F2 visible, and it judges every kept-work promise.</summary>
    Crash,

    /// <summary>The paused computer closes normally and starts again (width 2).</summary>
    Close
}

/// <summary>One crash point and one event, as the dry run's survey saw them: "at this very point, in this state, these events would be possible".</summary>
/// <param name="Index">The point's position in the world's hit log.</param>
/// <param name="Computer">The computer that reached it.</param>
/// <param name="Point">The crash point.</param>
/// <param name="PossibleEvents">The keys of the pause actions that were possible at that moment.</param>
/// <param name="WaitingEvents">The keys, among the possible ones, of the actions that are known to wait for the held operation (a switch while the same computer's save is in flight).</param>
internal sealed record SurveyHit(int Index, string Computer, string Point, IReadOnlyList<string> PossibleEvents, IReadOnlyList<string> WaitingEvents);

/// <summary>One case the pause sweep will run: a starting situation, an operation of a computer, the point it is held at, what happens meanwhile, and the follow-up.</summary>
/// <param name="Situation">The starting situation.</param>
/// <param name="Computer">The computer whose operation is held.</param>
/// <param name="Operation">The operation.</param>
/// <param name="Point">The crash point.</param>
/// <param name="Occurrence">Which time the point is reached.</param>
/// <param name="Action">What happens while the operation is held.</param>
/// <param name="FollowUp">What the paused computer does afterwards.</param>
internal sealed record PauseCasePlan(
    CrashSituation Situation, string Computer, SweepOperation Operation, string Point, int Occurrence, PauseAction Action, PauseFollowUp FollowUp);

/// <summary>How the pauses at one crash point (or of one kind of event) ended, counted.</summary>
internal sealed class PauseCounts
{
    /// <summary>The cases planned.</summary>
    public int Cases { get; set; }

    /// <summary>Cases whose event finished while the operation was held.</summary>
    public int Interleaved { get; set; }

    /// <summary>Cases whose event had to wait for the operation (designed waiting, not a violation).</summary>
    public int Waited { get; set; }

    /// <summary>Cases in which the operation was held but the event was not possible then.</summary>
    public int Skipped { get; set; }

    /// <summary>Cases in which the operation never reached the point, so no pause happened.</summary>
    public int NotReached { get; set; }

    /// <summary>Cases that ended in a hang.</summary>
    public int Hung { get; set; }

    /// <summary>Adds one pause's outcome.</summary>
    /// <param name="outcome">How the pause ended.</param>
    public void Add(PauseOutcome outcome)
    {
        switch (outcome)
        {
            case PauseOutcome.Interleaved: Interleaved++; break;
            case PauseOutcome.Waited:      Waited++;      break;
            case PauseOutcome.Skipped:     Skipped++;     break;
            case PauseOutcome.Hung:        Hung++;        break;
            default:                       NotReached++;  break;
        }
    }
}

/// <summary>What the pause sweep did: the probes, the cases, and how each pause ended, by crash point and by kind of event.</summary>
internal sealed class PauseSweepResult
{
    /// <summary>The width the sweep ran at.</summary>
    public int Width { get; set; }

    /// <summary>The number of starting situations the sweep used.</summary>
    public int Situations { get; set; }

    /// <summary>The number of dry runs (an operation done once in a situation, with the survey on).</summary>
    public int DryRuns { get; set; }

    /// <summary>The number of cases the width plans (a sampling run, see the sweep's stride, runs only some of them).</summary>
    public int Planned { get; set; }

    /// <summary>The number of cases run.</summary>
    public int Cases { get; set; }

    /// <summary>The number of (situation, computer, operation) combinations whose dry run showed the operation possible.</summary>
    public int ApplicableProbes { get; set; }

    /// <summary>For each crash point: how its pauses ended.</summary>
    public SortedDictionary<string, PauseCounts> PerPoint { get; } = new(StringComparer.Ordinal);

    /// <summary>For each kind of event: how its pauses ended.</summary>
    public SortedDictionary<string, PauseCounts> PerEvent { get; } = new(StringComparer.Ordinal);

    /// <summary>The totals over every case.</summary>
    public PauseCounts Total { get; } = new();

    /// <summary>The cases that ended in a hang, described (should be none).</summary>
    public List<string> HangCases { get; } = new();

    /// <summary>The cases whose pause step did not run at all (the replay skipped it), described (should be none).</summary>
    public List<string> NotRunCases { get; } = new();

    /// <summary>The cases whose operation never reached the point in the paused run although its dry run had (should be none), described.</summary>
    public List<string> NeverReachedCases { get; } = new();

    /// <summary>How many (point, event) combinations the width-1 sweep left to width 2 because the event is known to wait for the held operation.</summary>
    public int LeftToWidth2 { get; set; }

    /// <summary>
    /// How many cases the sweep left out BY DECISION, at any width, because the action is not offered while the held operation is what it
    /// is (<see cref="PauseAction.IsOfferedWhileHolding"/>): today the same computer switching database while its own Close is held (not
    /// pursued, user, 2026-10-21). Counted once per case identity (an operation, a point and an event at width 1; with the situation and the
    /// time the point is reached at width 2), before the follow-ups multiply them, so the report shows the rule at work and a test can tell
    /// that it was.
    /// </summary>
    public int NotOffered { get; set; }
}

/// <summary>
/// The pause sweep (spec item 9.3, step 9.3c-2b): for each multi-step operation and each crash point it reaches, a PAUSE there, one
/// other event while the operation is held, then a follow-up, with every invariant checked through the normal funnel. It is the
/// crash sweep's twin: where that one ends an operation with a power cut, this one only delays it, so that someone else acts in
/// the middle. That is how it found the route of F12 (Alice takes over between Bob's checks and his rename; fixed 2026-10-21: the save
/// now re-checks before the rename, so the sweep proves the route gone), and how it finds F2 (Bob edits during his own save) and F16
/// (Bob switches while a demotion is copying). It never pauses at <c>Save.AfterFinalCheck</c> (<see cref="PausePoints.NeverPaused"/>).
///
/// <para><b>How a case is made.</b> From the crash sweep's starting situations and operations: (1) a DRY RUN does the situation's
/// prefix and then the operation with a survey on, which records every crash point the computer reaches AND which pause events
/// would be possible at that very moment; (2) for each point and each such event, a CASE replays the prefix, does the operation
/// paused at the point with the event (<see cref="PauseEvent"/>), and then the follow-up: the paused computer crashes and starts
/// again with the default answers. A case is an ordinary decision list (<see cref="RunRecord"/>), so a failing case is replayed
/// and shrunk by the same machinery as any run.</para>
///
/// <para><b>The default (width 1) is a subset,</b> because the full cross product (every point of every operation of every
/// situation, times every event) is about 4590 cases (what width 2 plans) and takes about three hours, not a few minutes, and the point of a pause is an
/// INTERLEAVING, which needs a multi-step operation and a state-changing other event. So the default tries (a) the operations
/// that have such points: Save, Close, switch away, the heartbeat and the wake check (which run the lost-lock demotion), Open
/// (checkout and recovery) and restore from a backup; (b) only their <c>Save.*</c>, <c>Demote.*</c>, <c>Release.*</c>,
/// <c>Restore.*</c>, <c>Checkout.*</c> and <c>Recovery.*</c> points, the steps where the lock, the shared database and the kept
/// work change hands, and a FINAL save (Close, switch) only from its rename on, because before that it is the Save button's code
/// and what is special about it is what follows it; (c) the first time each point is reached; (d) each (operation, point,
/// event) once, in the first situation where it is possible, leaving out the events that are known to wait for the held
/// operation (they cost the whole grace period, and a proof test pins them); and (e) the crash follow-up. Width 2
/// (<see cref="HarnessBudget.PauseWidthVariable"/>=2) tries all of these in every situation: every operation, every point,
/// up to the third time it is reached, the wide situations, the events that wait, and the "close normally" follow-up as
/// well.</para>
/// </summary>
internal static class PauseSweep
{
    /// <summary>The prefixes of the crash points the default sweep pauses at (see the class remarks for why these).</summary>
    public static readonly IReadOnlyList<string> DefaultPointPrefixes = new[]
    {
        "Save.", "Demote.", "Release.", "Restore.", "Checkout.", "Recovery."
    };

    /// <summary>The names (see <see cref="SweepOperation.Name"/>) of the operations the default sweep pauses.</summary>
    public static readonly IReadOnlyList<string> DefaultOperationNames = new[]
    {
        "Save",
        "Close",
        "Switch to the other database",
        "Heartbeat tick",
        "Sleep 10 minutes (wake check)",
        "Open (Restore, take over: the simplest answers)",
        "Restore from a backup"
    };

    /// <summary>What a shrunk case is called in its story.</summary>
    private const string ShrunkTitle = "the shortest run found that still shows it";

    /// <summary>The operations a sweep of the given width pauses.</summary>
    /// <param name="width">1 (default) or 2.</param>
    public static IEnumerable<SweepOperation> OperationsFor(int width)
        => CrashSweep.Operations.Where(o => width >= 2 || DefaultOperationNames.Contains(o.Name));

    /// <summary>The operations whose save is a FINAL save (the exit save of Close, the leaving save of a switch).</summary>
    public static readonly IReadOnlyList<string> FinalSaveOperations = new[] { "Close", "Switch to the other database" };

    /// <summary>
    /// The save points the default sweep pauses in a final save. Before the rename a final save is the same code as the Save button's
    /// (the Save operation pauses there); what is special about a final save is what FOLLOWS it (the lock release and the cleanup of
    /// the working copy), so it is paused from its rename on.
    /// </summary>
    public static readonly IReadOnlyList<string> FinalSavePointsKept = new[] { "Save.AfterRename", "Save.AfterMarkerDelete" };

    /// <summary>
    /// True when a sweep of the given width pauses at the point, whichever operation reaches it. A point in <see cref="PausePoints.NeverPaused"/>
    /// is not paused at any width (<c>Save.AfterFinalCheck</c>: the accepted microsecond window before a rename; the crash sweep still cuts it).
    /// </summary>
    /// <param name="point">A crash point.</param>
    /// <param name="width">1 (default) or 2.</param>
    public static bool PointIsSwept(string point, int width)
        => !PausePoints.IsNeverPaused(point)
           && (width >= 2 || DefaultPointPrefixes.Any(p => point.StartsWith(p, StringComparison.Ordinal)));

    /// <summary>True when a sweep of the given width pauses the given operation at the point (a final save is paused only from its rename on at width 1).</summary>
    /// <param name="point">A crash point.</param>
    /// <param name="width">1 (default) or 2.</param>
    /// <param name="operation">The name of the operation that reaches the point.</param>
    public static bool PointIsSwept(string point, int width, string operation)
        => PointIsSwept(point, width)
           && (width >= 2
               || !FinalSaveOperations.Contains(operation)
               || !point.StartsWith("Save.", StringComparison.Ordinal)
               || FinalSavePointsKept.Contains(point));

    /// <summary>The follow-ups a sweep of the given width tries after each pause.</summary>
    /// <param name="width">1 (default) or 2.</param>
    public static IEnumerable<PauseFollowUp> FollowUpsFor(int width)
        => width >= 2 ? new[] { PauseFollowUp.Crash, PauseFollowUp.Close } : new[] { PauseFollowUp.Crash };

    /// <summary>
    /// The decision list of one case: the prefix, the operation paused at the point with the event, and the follow-up (the paused
    /// computer crashes, or closes normally, and then starts again with the simplest answers).
    /// </summary>
    /// <param name="plan">The case.</param>
    /// <returns>The decision list.</returns>
    public static RunRecord CaseRecord(PauseCasePlan plan)
    {
        var inner = plan.Operation.Make(plan.Computer);
        var pause = new PauseEvent(inner, plan.Point, plan.Occurrence, plan.Action);
        var steps = new List<RecordedStep>
        {
            new() { EventKey = pause.Key, Answers = plan.Operation.Answers?.Invoke(plan.Computer).ToList() ?? new List<RecordedAnswer>() },
            new() { EventKey = plan.FollowUp == PauseFollowUp.Crash ? $"Crash({plan.Computer})" : $"Close({plan.Computer})" },
            new() { EventKey = $"Open({plan.Computer})" }
        };
        return CrashSweep.Record(plan.Situation.Prefix, steps.ToArray());
    }

    /// <summary>
    /// Runs the sweep: every situation, every computer of it, every operation; a dry run each, with the survey on, then a case per
    /// crash point and possible event. Breaches are folded into the returned log (known ones counted, new ones shrunk).
    /// </summary>
    /// <param name="budget">The budget (its <see cref="HarnessBudget.PauseWidth"/> sets the width).</param>
    /// <param name="progress">Receives a progress line now and then; may be null.</param>
    /// <param name="stride">Runs only every stride-th planned case (1 = all, the default). A smoke test of the wide sweep uses a larger one to sample its cases in a few minutes; every case is still PLANNED, so the count of cases the width would run is known.</param>
    /// <param name="maxCases">Runs at most this many cases (default: all).</param>
    /// <returns>What the sweep found and covered.</returns>
    /// <exception cref="InvalidOperationException">A situation's prefix does not replay, so the sweep would not be testing what it says.</exception>
    public static async Task<StrategyOutcome> RunAsync(HarnessBudget budget, Action<string>? progress, int stride = 1, int maxCases = int.MaxValue)
    {
        var stopwatch = Stopwatch.StartNew();
        var options   = new RunOptions { Title = "a pause sweep run" };
        var log       = new GeneratedLog(options);
        var width     = budget.PauseWidth;
        var result    = new PauseSweepResult { Width = width };
        var planned   = new HashSet<string>(StringComparer.Ordinal);

        foreach (var situation in CrashSweep.SituationsFor(width))
        {
            result.Situations++;
            foreach (var computer in situation.Computers)
            {
                foreach (var operation in OperationsFor(width))
                {
                    var plans = await PlanAsync(situation, computer, operation, width, planned, result);
                    foreach (var plan in plans)
                    {
                        result.Planned++;
                        if ((result.Planned - 1) % Math.Max(1, stride) != 0 || result.Cases >= maxCases) continue;

                        await RunCaseAsync(plan, result, log);
                        if (result.Cases % 25 == 0)
                            progress?.Invoke($"pause sweep: {result.DryRuns} dry runs, {result.Cases} cases " +
                                             $"({result.Total.Interleaved} interleaved, {result.Total.Waited} waited), " +
                                             $"{log.Tallies.Count} kinds of breach, {stopwatch.Elapsed:mm\\:ss}");
                    }
                }
            }
        }

        stopwatch.Stop();
        return new StrategyOutcome
        {
            Name = "Pause sweep", Parameters = $"width {width}, {result.Situations} situations", Log = log, Elapsed = stopwatch.Elapsed,
            IsPauseSweep = true, PauseSweep = result
        };
    }

    /// <summary>
    /// Does the dry run of one (situation, computer, operation) and plans its cases: for each crash point the computer reached (that
    /// the sweep pauses at) and each event that was possible at that moment, one case per follow-up.
    /// </summary>
    /// <param name="situation">The starting situation.</param>
    /// <param name="computer">The computer whose operation is held.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="width">The sweep's width.</param>
    /// <param name="planned">The (operation, point, event) combinations already planned: at width 1 each is planned once, in the first situation where it is possible.</param>
    /// <param name="result">Where the dry run is counted.</param>
    /// <returns>The planned cases (empty when the operation was not possible in the situation).</returns>
    private static async Task<IReadOnlyList<PauseCasePlan>> PlanAsync(
        CrashSituation situation, string computer, SweepOperation operation, int width, HashSet<string> planned, PauseSweepResult result)
    {
        var held     = operation.Make(computer);
        var innerKey = held.Key;
        var dryRecord = CrashSweep.Record(situation.Prefix,
            new RecordedStep { EventKey = innerKey, Answers = operation.Answers?.Invoke(computer).ToList() ?? new List<RecordedAnswer>() });

        var hits = new List<SurveyHit>();
        var dryOptions = new RunOptions
        {
            Title = $"{situation.Name} / {computer}: {operation.Name} (a dry run with the survey on)",
            ExtraEvents = EventCatalogue.ExtrasFor(new[] { innerKey }),
            ConfigureContext = ctx => ctx.World.CrashPointSurvey = (index, who, point) =>
            {
                if (who is null) return;
                var possibleActions = PauseActions.For(who.Name).Where(a => SafelyPossible(a, ctx)).ToList();
                var possible = possibleActions.Select(a => a.Key).ToList();
                var waiting  = possibleActions.Where(a => SafelyWaits(a, ctx)).Select(a => a.Key).ToList();
                lock (hits) hits.Add(new SurveyHit(index, who.Name, point, possible, waiting));
            }
        };
        var dry = await RunExecutor.ExecuteAsync(new ReplaySource(dryRecord), dryOptions);
        result.DryRuns++;

        var prefixHeld = dry.Record.Steps.Take(situation.Prefix.Count).Select(s => s.EventKey).SequenceEqual(situation.Prefix);
        if (!prefixHeld && !dry.Violations.Any(v => !v.IsKnown))
            throw new InvalidOperationException(
                $"The pause sweep's situation \"{situation.Name}\" does not replay: its prefix was cut short at event {dry.Record.Steps.Count}. " +
                $"Fix the prefix ({string.Join(", ", situation.Prefix)}).");

        var applicable = prefixHeld && dry.Record.Steps.Count == situation.Prefix.Count + 1 && dry.Record.Steps[^1].EventKey == innerKey;
        if (!applicable) return Array.Empty<PauseCasePlan>();
        result.ApplicableProbes++;

        // The hits of the operation itself: the survey also saw the prefix's.
        var operationStart = dry.HitStartByEvent[situation.Prefix.Count];
        List<SurveyHit> mine;
        lock (hits) mine = hits.Where(h => h.Index >= operationStart && h.Computer == computer).OrderBy(h => h.Index).ToList();

        var plans = new List<PauseCasePlan>();
        // In catalogue order, so the plan (and a sample of it) does not depend on which thread happened to reach a point first.
        var catalogue = CrashPoints.All.ToList();
        foreach (var group in mine.GroupBy(h => h.Point).OrderBy(g => catalogue.IndexOf(g.Key)))
        {
            if (!PointIsSwept(group.Key, width, operation.Name)) continue;

            var arrivals = group.ToList();
            var times    = width >= 2 ? Math.Min(3, arrivals.Count) : 1;
            for (var occurrence = 1; occurrence <= times; occurrence++)
            {
                var arrival = arrivals[occurrence - 1];
                foreach (var action in PauseActions.For(computer).Where(a => arrival.PossibleEvents.Contains(a.Key)))
                {
                    var identity = width >= 2
                        ? $"{situation.Name}|{innerKey}|{group.Key}|{occurrence}|{action.Key}"
                        : $"{innerKey}|{group.Key}|{action.Key}";

                    // A pairing left out by decision (see PausedComputerSwitchesAction): neither planned nor run, at any width. Counted once
                    // per identity, like a case, so the count is the number of cases the rule removed (per follow-up).
                    if (!action.IsOfferedWhileHolding(held))
                    {
                        if (planned.Add("left out|" + identity)) result.NotOffered++;
                        continue;
                    }

                    // An event that is known to wait for the held operation (a switch while the same computer's save is in flight) is
                    // designed behaviour that costs the whole grace period to demonstrate: a proof test pins it, and width 2 sweeps it.
                    if (width < 2 && arrival.WaitingEvents.Contains(action.Key))
                    {
                        result.LeftToWidth2++;
                        continue;
                    }

                    if (!planned.Add(identity)) continue;

                    foreach (var followUp in FollowUpsFor(width))
                        plans.Add(new PauseCasePlan(situation, computer, operation, group.Key, occurrence, action, followUp));
                }
            }
        }
        return plans;
    }

    /// <summary>Asks whether a pause action is known to wait for the held operation, treating a question that throws as "no" (the survey must never break a dry run).</summary>
    /// <param name="action">The action.</param>
    /// <param name="ctx">The run.</param>
    private static bool SafelyWaits(PauseAction action, RunContext ctx)
    {
        try { return action.WaitsForTheHeldOperation(ctx); }
        catch (Exception) { return false; }
    }

    /// <summary>Asks whether a pause action is possible, treating a question that throws as "not possible" (the survey must never break a dry run).</summary>
    /// <param name="action">The action.</param>
    /// <param name="ctx">The run.</param>
    private static bool SafelyPossible(PauseAction action, RunContext ctx)
    {
        try { return action.IsPossible(ctx); }
        catch (Exception) { return false; }
    }

    /// <summary>Runs one case and folds it into the result and the log.</summary>
    /// <param name="plan">The case.</param>
    /// <param name="result">Where coverage is counted.</param>
    /// <param name="log">Where breaches are folded in.</param>
    private static async Task RunCaseAsync(PauseCasePlan plan, PauseSweepResult result, GeneratedLog log)
    {
        var record  = CaseRecord(plan);
        var options = new RunOptions
        {
            Title = $"{plan.Situation.Name} / {plan.Computer}: {plan.Operation.Name}, paused at {plan.Point}" +
                    (plan.Occurrence > 1 ? $" (time {plan.Occurrence})" : "") + $" while {plan.Action.Key}, then {plan.FollowUp}",
            ExtraEvents = EventCatalogue.ExtrasFor(record.Steps.Select(s => s.EventKey))
        };

        var run = await RunExecutor.ExecuteAsync(new ReplaySource(record), options);
        result.Cases++;
        await log.ObserveAsync(run, options.WithTitle(ShrunkTitle));

        var pointCounts = Counts(result.PerPoint, plan.Point);
        var eventCounts = Counts(result.PerEvent, plan.Action.Key);
        pointCounts.Cases++;
        eventCounts.Cases++;
        result.Total.Cases++;

        var pause = run.PauseResults.FirstOrDefault();
        if (pause is null)
        {
            // The replay skipped the pause step (it was not enabled): the case did not test what it says.
            result.NotRunCases.Add($"{plan.Situation.Name} / {plan.Computer}: {plan.Operation.Name}, {plan.Point} while {plan.Action.Key}");
            pointCounts.NotReached++;
            eventCounts.NotReached++;
            result.Total.NotReached++;
            return;
        }

        pointCounts.Add(pause.Outcome);
        eventCounts.Add(pause.Outcome);
        result.Total.Add(pause.Outcome);
        if (pause.Outcome == PauseOutcome.NotReached)
            result.NeverReachedCases.Add($"{plan.Situation.Name} / {plan.Computer}: {plan.Operation.Name}, {plan.Point} while {plan.Action.Key}");
        if (pause.Outcome == PauseOutcome.Hung)
            result.HangCases.Add($"{plan.Situation.Name} / {plan.Computer}: {plan.Operation.Name}, {plan.Point} while {plan.Action.Key}");
    }

    /// <summary>The counts of one key, created on first use.</summary>
    /// <param name="table">The table.</param>
    /// <param name="key">The crash point or the kind of event.</param>
    private static PauseCounts Counts(SortedDictionary<string, PauseCounts> table, string key)
    {
        if (!table.TryGetValue(key, out var counts)) table[key] = counts = new PauseCounts();
        return counts;
    }
}

/// <summary>
/// The "Pause sweep" section of the harness report (spec item 9.3, step 9.3c-2b): how many cases ran, how each pause ended
/// (interleaved, waited, not possible, never reached, hung), and the same by crash point and by kind of event. Deterministic
/// apart from the interleaved/waited split of a case that sits on the grace period.
/// </summary>
internal static class PauseSweepReport
{
    /// <summary>Builds the section.</summary>
    /// <param name="outcome">The pause sweep's outcome.</param>
    /// <returns>Markdown, ending with a blank line.</returns>
    public static string Build(StrategyOutcome outcome)
    {
        var sweep = outcome.PauseSweep!;
        var sb    = new StringBuilder();
        var total = sweep.Total;

        sb.AppendLine("## Pause sweep");
        sb.AppendLine($"- Width {sweep.Width} ({HarnessBudget.PauseWidthVariable}): {sweep.Situations} starting situations, {sweep.DryRuns} dry runs " +
                      $"({sweep.ApplicableProbes} with the operation possible; a dry run does the operation once, with a survey of which events are possible at each crash point), " +
                      $"{sweep.Cases} cases{(sweep.Planned > sweep.Cases ? $" (a sample of the {sweep.Planned} the width plans)" : "")} (pause, event, follow-up, every invariant).");
        sb.AppendLine($"- Pauses: **{total.Interleaved} interleaved** (the event finished while the operation was held), **{total.Waited} waited** " +
                      $"(the event could not finish until the operation was let go: designed waiting, such as the switch that waits for a save in flight, not a violation), " +
                      $"{total.Skipped} not possible at the moment, {total.NotReached} never reached, **{total.Hung} hung**.");
        sb.AppendLine($"- Cases whose pause step did not run: {sweep.NotRunCases.Count}" + (sweep.NotRunCases.Count == 0 ? "." : ":"));
        foreach (var line in sweep.NotRunCases) sb.AppendLine($"  - {line}");
        sb.AppendLine($"- Cases whose operation never reached the point although its dry run had: {sweep.NeverReachedCases.Count}" + (sweep.NeverReachedCases.Count == 0 ? "." : ":"));
        foreach (var line in sweep.NeverReachedCases) sb.AppendLine($"  - {line}");
        if (sweep.Width < 2)
            sb.AppendLine($"- Left to width 2: {sweep.LeftToWidth2} (point, event) combinations in which the event is known to wait for the held operation " +
                          "(a switch while the same computer's save is in flight); a proof test pins that behaviour.");
        sb.AppendLine($"- Left out by decision: {sweep.NotOffered} pairings (cases, before the follow-ups) in which the same computer switches database while its own Close is held " +
                      "(not pursued, user, 2026-10-21: the switch deletes the working copy the close kept; see PausedComputerSwitchesAction). Another computer's events during a close are swept.");
        sb.AppendLine($"- Hangs: {sweep.HangCases.Count}" + (sweep.HangCases.Count == 0 ? "." : ":"));
        foreach (var line in sweep.HangCases) sb.AppendLine($"  - {line}");
        sb.AppendLine($"- Default subset (width 1): the operations {string.Join("; ", PauseSweep.DefaultOperationNames)}, at their " +
                      $"{string.Join(", ", PauseSweep.DefaultPointPrefixes.Select(p => p + "*"))} points, the first time each is reached, each (operation, point, event) once, " +
                      $"followed by a crash and a restart. A final save (Close, switch) is paused from its rename on ({string.Join(", ", PauseSweep.FinalSavePointsKept)}); " +
                      $"before that it is the Save button's code. Everything else is width 2.");
        sb.AppendLine();

        sb.AppendLine("### Per point");
        sb.AppendLine("| Point | Cases | Interleaved | Waited | Not possible | Never reached | Hung |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var point in CrashPoints.All)
        {
            if (!sweep.PerPoint.TryGetValue(point, out var c))
            {
                var why = PausePoints.IsNeverPaused(point) ? " (never paused: a few microseconds of local code; the crash sweep cuts it)"
                        : PauseSweep.PointIsSwept(point, sweep.Width) ? ""
                        : " (width 2 only)";
                sb.AppendLine($"| {point} | 0{why} | - | - | - | - | - |");
                continue;
            }
            sb.AppendLine($"| {point} | {c.Cases} | {c.Interleaved} | {c.Waited} | {c.Skipped} | {c.NotReached} | {c.Hung} |");
        }
        sb.AppendLine();

        sb.AppendLine("### Per event");
        sb.AppendLine("| Event | Cases | Interleaved | Waited | Not possible | Never reached | Hung |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (key, c) in sweep.PerEvent)
            sb.AppendLine($"| {key} | {c.Cases} | {c.Interleaved} | {c.Waited} | {c.Skipped} | {c.NotReached} | {c.Hung} |");
        sb.AppendLine();
        return sb.ToString();
    }
}

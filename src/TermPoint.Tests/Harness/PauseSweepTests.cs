using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The tests of the pause sweep (spec item 9.3, step 9.3c-2b): an operation held at a named crash point, one other event meanwhile,
/// then a crash and a restart, with every invariant checked. The sweep runs once per test process and the tests read the same result.
/// The tests run only with <c>TERMPOINT_HARNESS=1</c> (<see cref="HarnessFactAttribute"/>); the width comes from
/// <c>TERMPOINT_HARNESS_PAUSE_WIDTH</c>.
/// </summary>
public sealed class PauseSweepTests
{
    /// <summary>The test output, where progress and the report are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public PauseSweepTests(ITestOutputHelper output) => _output = output;

    /// <summary>Writes a line to the test output, ignoring a test that has already ended (a shared run reports late).</summary>
    /// <param name="line">The line.</param>
    private void Say(string line)
    {
        try { _output.WriteLine(line); } catch (InvalidOperationException) { /* the test that started the run has finished */ }
    }

    /// <summary>
    /// Every case of the sweep ends without a breach that no known finding explains: no hang, no torn database, no committed edit
    /// lost (F12 is fixed, so nothing explains one), no applied edit lost that the F2 route does not explain, no read-only session
    /// holding a lock that the F16 route does not explain. Fails with each new breach's shrunk story; the known ones are counted and reported.
    /// </summary>
    [HarnessFact]
    public async Task PauseSweep_NoNewViolations()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.PauseSweepAsync(budget, Say);
        GeneratedRunTests.PublishReportTo(budget, Say);
        GeneratedRunTests.AssertNoNewViolations(outcome);
    }

    /// <summary>
    /// Every planned pause really happened: a replayed case reaches its point again (runs are deterministic), no case ended in a hang,
    /// and every pause step ran. Cases whose point was not reached, or whose event was not possible then, are counted, not failed.
    /// </summary>
    [HarnessFact]
    public async Task PauseSweep_EveryPlannedPauseRan_AndNothingHung()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.PauseSweepAsync(budget, Say);
        GeneratedRunTests.PublishReportTo(budget, Say);
        var sweep = outcome.PauseSweep!;

        Assert.True(sweep.HangCases.Count == 0, "These pauses hung:\n  " + string.Join("\n  ", sweep.HangCases));
        Assert.True(sweep.NotRunCases.Count == 0, "These cases never ran their pause step:\n  " + string.Join("\n  ", sweep.NotRunCases));
        Assert.True(sweep.Total.Interleaved > 0, "no pause interleaved: the sweep tested nothing");
        Say($"Pause sweep: {sweep.Cases} cases; {sweep.Total.Interleaved} interleaved, {sweep.Total.Waited} waited, " +
            $"{sweep.Total.Skipped} not possible, {sweep.Total.NotReached} never reached, {outcome.Elapsed:mm\\:ss} in all.");
    }

    /// <summary>
    /// The sweep finds F2 and F16 on its own, each by its own route: Bob edits during his save (F2), Bob switches while a demotion is
    /// copying (F16). Reports each with its smallest story. It no longer finds F12 (Alice takes over between Bob's checks and his
    /// rename), fixed 2026-10-21: the save re-checks the lock and D before the rename, so the route ends with Bob's save refused; the
    /// sweep proves the route gone by finding no F12 (and any other violation fails <see cref="PauseSweep_NoNewViolations"/>).
    /// </summary>
    [HarnessFact]
    public async Task PauseSweep_FindsF2_AndF16_AndNoLongerF12()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.PauseSweepAsync(budget, Say);
        GeneratedRunTests.PublishReportTo(budget, Say);

        foreach (var id in new[] { "F2", "F16" })
        {
            var tallies = outcome.Log.Tallies.Values.Where(t => t.FindingId == id).ToList();
            Assert.True(tallies.Count > 0, $"the pause sweep did not find {id}");
            foreach (var tally in tallies) Say($"{id} found in {tally.Runs} runs; smallest story {tally.BestEvents} events.\n{tally.BestStory}");
        }

        Assert.DoesNotContain(outcome.Log.Tallies.Values, t => t.FindingId == "F12");
    }

    /// <summary>
    /// A bounded sample of the WIDE sweep (width 2: every operation and crash point, up to the third time a point is reached, the wide
    /// starting situations, the events that wait, and the "close normally" follow-up as well as the crash). The full wide sweep plans
    /// far more cases than anyone waits for (see the count it reports), so this runs every 40th planned case, at most 60, which touches
    /// every kind of case in a few minutes: nothing hangs, every pause step runs, and EVERY breach is explained by a known finding.
    /// There is no pinned exception any more: the one that used to be here (a switch overlapping a close, 9.3c-2b) was not pursued by the
    /// user on 2026-10-21, and the pairing is no longer offered at all (<see cref="PauseAction.IsOfferedWhileHolding"/>).
    /// </summary>
    [HarnessFact]
    public async Task PauseSweep_Width2_ASampleOfItsCasesRunsCleanly()
    {
        var budget  = new HarnessBudget(HarnessBudget.DefaultDepth, HarnessBudget.DefaultSeeds, HarnessBudget.DefaultSteps, PauseWidth: 2);
        var outcome = await PauseSweep.RunAsync(budget, Say, stride: 40, maxCases: 60);
        var sweep   = outcome.PauseSweep!;
        Say(PauseSweepReport.Build(outcome));
        Say($"The wide sweep plans {sweep.Planned} cases; this sample ran {sweep.Cases} of them in {outcome.Elapsed:mm\\:ss}.");

        Assert.True(sweep.Cases > 0);
        Assert.True(sweep.HangCases.Count == 0, "These pauses hung:\n  " + string.Join("\n  ", sweep.HangCases));
        Assert.True(sweep.NotRunCases.Count == 0, "These cases never ran their pause step:\n  " + string.Join("\n  ", sweep.NotRunCases));

        // The rule that leaves out a switch during the same computer's close is applied while the cases are planned (all of them are planned, only some run).
        Assert.True(sweep.NotOffered > 0, "no pairing was left out: PauseAction.IsOfferedWhileHolding is not being applied by the plan");

        var unexplained = outcome.Log.NewTallies.ToList();
        Assert.True(unexplained.Count == 0,
            "The wide sample found breaches that nothing explains:\n" + string.Join("\n", unexplained.Select(t => $"{t.Signature}: {t.Reason}\n{t.BestStory}")));
    }
}

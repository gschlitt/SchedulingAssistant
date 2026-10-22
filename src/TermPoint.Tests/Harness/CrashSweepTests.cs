using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The tests of the crash sweep (spec item 9.3, step 9.3c-2a): a power cut at each named crash point of each operation,
/// from a small fixed set of starting situations, then a restart and every invariant. The sweep runs once per test
/// process and the tests read the same result. The tests run only with <c>TERMPOINT_HARNESS=1</c>
/// (<see cref="HarnessFactAttribute"/>); the width comes from <c>TERMPOINT_HARNESS_CRASH_WIDTH</c>.
/// </summary>
public sealed class CrashSweepTests
{
    /// <summary>The test output, where progress and the report are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public CrashSweepTests(ITestOutputHelper output) => _output = output;

    /// <summary>Writes a line to the test output, ignoring a test that has already ended (a shared run reports late).</summary>
    /// <param name="line">The line.</param>
    private void Say(string line)
    {
        try { _output.WriteLine(line); } catch (InvalidOperationException) { /* the test that started the run has finished */ }
    }

    /// <summary>
    /// Every case of the sweep ends without a breach that no known finding explains. Fails with each new breach's
    /// shrunk story; the known ones (F5, F6, F7, ...) are counted and reported. F1 and F8 are fixed, so a breach of their kinds is new.
    /// </summary>
    [HarnessFact]
    public async Task CrashSweep_NoNewViolations()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.SweepAsync(budget, Say);
        GeneratedRunTests.PublishReportTo(budget, Say);
        GeneratedRunTests.AssertNoNewViolations(outcome);
    }

    /// <summary>
    /// Every planned cut fired (a replayed case reaches its point again, because runs are deterministic), and every crash
    /// point is either cut by some case or has a written reason in <see cref="CrashSweepCoverage.MissReasons"/>. A reason
    /// for a point the sweep does cut is stale and fails too.
    /// </summary>
    [HarnessFact]
    public async Task CrashSweep_EveryPointIsCutOrExplained()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.SweepAsync(budget, Say);
        GeneratedRunTests.PublishReportTo(budget, Say);
        var sweep = outcome.Sweep!;

        Assert.True(sweep.CasesNotCut == 0,
            $"{sweep.CasesNotCut} planned cut(s) did not fire:\n  " + string.Join("\n  ", sweep.NotCutCases));

        var missed = CrashSweepReport.MissedPoints(sweep);
        var unexplained = missed.Where(p => !CrashSweepCoverage.MissReasons.ContainsKey(p)).ToList();
        Assert.True(unexplained.Count == 0,
            "These crash points were never cut and have no reason in CrashSweepCoverage.MissReasons: " + string.Join(", ", unexplained));

        var stale = CrashSweepCoverage.MissReasons.Keys.Where(p => !missed.Contains(p)).ToList();
        Assert.True(stale.Count == 0,
            "These crash points have a miss reason but are now cut by the sweep (delete the reason): " + string.Join(", ", stale));
    }

    /// <summary>
    /// The longer case the next tests use: Bob edits, saves, edits again, and his computer loses power at a crash point of the second
    /// save (the rename by default), then restarts.
    /// </summary>
    /// <param name="record">The case's decision list.</param>
    /// <param name="point">The crash point of the second save where the power goes.</param>
    /// <param name="plantForgottenMarker">
    /// True to plant a bug without touching production code: at <c>Dirty.AfterMarkerWritten</c> the marker that was just written is deleted
    /// ("MarkDirty forgot the marker"), so a cut before the rename leaves a kept working file the next start deletes as "nothing was edited".
    /// </param>
    private static RunOptions LongCaseOptions(out RunRecord record, string point = "Save.AfterRename", bool plantForgottenMarker = false)
    {
        var situation = CrashSweep.Situations.First(s => s.Name.StartsWith("Bob edited and saved once", StringComparison.Ordinal));
        var save      = CrashSweep.Operations.First(o => o.Name == "Save");
        record        = CrashSweep.CaseRecord(situation, "bob", save, point, occurrence: 1, width: 1);
        return new RunOptions
        {
            Title       = "a long crash-sweep case",
            ExtraEvents = EventCatalogue.ExtrasFor(record.Steps.Select(s => s.EventKey)),
            ConfigureWorld = plantForgottenMarker
                ? world => world.AtCrashPointEveryTime("Dirty.AfterMarkerWritten", () =>
                {
                    var actor = world.ActingComputer;   // the computer whose flow reached the hook (see World.ActingComputer)
                    if (actor is not null && File.Exists(actor.MarkerFile)) File.Delete(actor.MarkerFile);
                })
                : null
        };
    }

    /// <summary>
    /// A crash-sweep case is an ordinary decision list: replaying a case from its recorded decisions gives the same
    /// decisions, the same story and the same findings, which is what shrinking and "run it again" rely on. This case
    /// (power cut at the rename of the second save, then the restart) used to show F8; it is fixed, so it now shows nothing at all.
    /// </summary>
    [HarnessFact]
    public async Task CrashSweepCase_ReplaysToTheSameStory()
    {
        var options  = LongCaseOptions(out var record);
        var original = await RunExecutor.ExecuteAsync(new ReplaySource(record), options);
        var replayed = await RunExecutor.ExecuteAsync(new ReplaySource(original.Record), options);

        Say(original.Story);
        Assert.Contains("Bob's computer lost power at Save.AfterRename", original.Story);
        Assert.Equal(new[] { "bob@Save.AfterRename" }, original.FiredCuts);
        Assert.Equal(original.Record.ToLines(), replayed.Record.ToLines());
        Assert.Equal(original.Story, replayed.Story);
        Assert.Equal(original.Violations.Select(v => v.Signature), replayed.Violations.Select(v => v.Signature));
        Assert.Empty(original.Violations);
    }

    /// <summary>
    /// A failing crash-sweep case goes through the existing shrinker. F8 (fixed) used to be the failing case here, so a bug is planted
    /// instead, without touching production code: "MarkDirty forgot the marker" (see <see cref="LongCaseOptions"/>). The long case (edit,
    /// save, edit, power cut at the D.tmp copy of the next save, restart) then loses the second edit (I2, no known finding explains it),
    /// and is shrunk to the four events that matter (open, edit, the save cut, open again).
    /// </summary>
    [HarnessFact]
    public async Task CrashSweepCase_IsShrunkByTheExistingShrinker()
    {
        var options = LongCaseOptions(out var record, point: "Save.AfterTmpCopy", plantForgottenMarker: true);
        var run     = await RunExecutor.ExecuteAsync(new ReplaySource(record), options);
        var lost    = run.NewViolations.FirstOrDefault(v => v.Invariant == "I2");
        Assert.NotNull(lost);

        var shrunk = await Shrinker.ShrinkAsync(run.Record.Take(lost!.EventIndex), lost.Signature, options);
        Say(shrunk.Story);

        Assert.True(shrunk.Reproduced);
        Assert.True(shrunk.Events < record.Count, $"the case was not shortened: {shrunk.Events} of {record.Count} events");
        Assert.True(shrunk.Events <= 4, $"the shrunk case still has {shrunk.Events} events");
        Assert.Contains("Bob's computer lost power at Save.AfterTmpCopy", shrunk.Story);
    }

    /// <summary>
    /// F8 is gone: a power cut between the rename and the marker delete of a save, then a restart that chooses Restore, used to end in
    /// "Can't Restore" although the shared database already held every kept change. The save now records its fingerprint in the marker
    /// before the rename, so the crash sweep, which cuts the power at <c>Save.AfterRename</c> in several situations, finds no F8 (and any
    /// other breach fails <see cref="CrashSweep_NoNewViolations"/>). Also checks that the sweep really cut at the rename and at the new
    /// <c>Save.AfterFinalCheck</c> point, so "no F8" is not an empty result.
    /// </summary>
    [HarnessFact]
    public async Task CrashSweep_NoLongerFindsF8()
    {
        var budget  = HarnessBudget.FromEnvironment();
        var outcome = await GeneratedSuite.SweepAsync(budget, Say);
        GeneratedRunTests.PublishReportTo(budget, Say);

        Assert.DoesNotContain(outcome.Log.Tallies.Values, t => t.FindingId == "F8");
        Assert.True(outcome.Sweep!.Cut["Save.AfterRename"] > 0, "the crash sweep never cut the power at Save.AfterRename");
        Assert.True(outcome.Sweep.Cut["Save.AfterFinalCheck"] > 0, "the crash sweep never cut the power at Save.AfterFinalCheck");
    }
}
